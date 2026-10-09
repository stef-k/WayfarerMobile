using System.Text.Json;
using SQLite;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Services;

/// <summary>Persists and verifies Timeline mutation provenance and rollback data.</summary>
public sealed partial class TimelineSyncService
{
    /// <summary>Rejects unlinked selections and validates the exact originating row before mutation.</summary>
    private async Task<LocalTimelineEntry?> ResolveMutationEntryAsync(TimelineEntryIdentity identity)
    {
        if (identity == null || !identity.CanMutate)
            throw new InvalidOperationException(TimelineEntryIdentity.ReadOnlyExplanation);

        if (!identity.LocalEntryId.HasValue)
            return await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(identity.ServerId!.Value);

        var entry = await _timelineRepository.GetLocalTimelineEntryAsync(identity.LocalEntryId.Value);
        if (entry == null || !entry.IsSynced || entry.ServerId != identity.ServerId)
            throw new InvalidOperationException("Timeline linkage changed; reload this entry before editing or deleting.");

        return entry;
    }

    /// <summary>
    /// Requires explicit server confirmation, then verifies every available source binding.
    /// Historical bindings and snapshots may be consistent despite originating from an ID collision.
    /// </summary>
    private async Task<string?> GetMutationAuthorityErrorAsync(PendingTimelineMutation mutation)
    {
        string? error = null;
        await _database!.RunInTransactionAsync(connection =>
            error = GetMutationAuthorityError(connection, mutation));
        return error;
    }

    /// <summary>Uses the same fail-closed authority checks for replay and transactional merging.</summary>
    private static string? GetMutationAuthorityError(
        SQLiteConnection connection, PendingTimelineMutation mutation)
    {
        if (mutation.LocationId <= 0 || mutation.OperationType is not ("Update" or "Delete"))
            return "Mutation held: invalid server identity or operation.";
        if (!mutation.ServerIdentityConfirmed)
            return "Mutation held: legacy record has no confirmed server provenance; retained for inspection.";

        LocalTimelineEntry? snapshot = null;
        if (mutation.DeletedEntryJson != null)
        {
            try
            {
                snapshot = JsonSerializer.Deserialize<LocalTimelineEntry>(mutation.DeletedEntryJson);
            }
            catch (JsonException)
            {
                return "Mutation held: deletion snapshot is unreadable.";
            }

            if (mutation.OperationType != "Delete" || snapshot?.Id is not > 0
                || !snapshot.IsSynced || snapshot.ServerId != mutation.LocationId)
                return "Mutation held: deletion snapshot does not prove this server identity.";
            if (mutation.LocalEntryId.HasValue && mutation.LocalEntryId != snapshot.Id)
                return "Mutation held: deletion snapshot and local source disagree.";
        }

        var sourceId = mutation.LocalEntryId ?? snapshot?.Id;
        if (sourceId.HasValue)
        {
            if (sourceId <= 0)
                return "Mutation held: invalid local source binding.";

            var source = connection.Find<LocalTimelineEntry>(sourceId.Value);
            if (source != null && (!source.IsSynced || source.ServerId != mutation.LocationId))
                return "Mutation held: the originating local row is not linked to this server identity.";
            if (source == null && snapshot == null)
                return "Mutation held: the originating local row is unavailable.";

            // Updates need a live source; deletes may rely on their validated pre-delete snapshot.
            if (mutation.OperationType == "Delete" && snapshot == null)
                return "Mutation held: deletion source has no saved snapshot.";
            return null;
        }

        return null;
    }

    /// <summary>Finds only deliverable work with the same source binding for merging or replacement.</summary>
    private static List<PendingTimelineMutation> GetCompatibleMutations(
        SQLiteConnection connection, int locationId, int? localEntryId)
    {
        var candidates = connection.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsRejected && m.AuthorityError == null)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        var compatible = new List<PendingTimelineMutation>();
        foreach (var candidate in candidates)
        {
            if (candidate.LocalEntryId != localEntryId)
                continue;
            if (!localEntryId.HasValue && !candidate.ServerIdentityConfirmed)
                continue;
            if (GetMutationAuthorityError(connection, candidate) == null)
                compatible.Add(candidate);
        }
        return compatible;
    }

    #region LocalTimelineEntry Integration (Persisted Rollback)

    /// <summary>
    /// Revalidates the previously resolved source inside the ownership transaction. Never adopts a replacement.
    /// A proven API selection without a cache copy needs no local rollback or optimistic change.
    /// </summary>
    private static LocalTimelineEntry? RevalidateMutationEntry(
        SQLiteConnection connection, int locationId, LocalTimelineEntry? resolvedEntry)
    {
        if (resolvedEntry == null) return null;

        var source = connection.Find<LocalTimelineEntry>(resolvedEntry.Id);
        if (source?.IsSynced != true || source.ServerId != locationId)
            throw new InvalidOperationException("Timeline linkage changed; reload this entry before editing or deleting.");
        return source;
    }

    /// <summary>Commits current rollback values, compatible durable intent and optimistic state together.</summary>
    private async Task<PendingTimelineMutation> PersistUpdateMutationAsync(
        int locationId, LocalTimelineEntry? resolvedEntry,
        double? latitude, double? longitude, DateTime? localTimestamp, string? notes, bool includeNotes,
        int? activityTypeId, bool clearActivity, string? activityTypeName)
    {
        PendingTimelineMutation mutation = null!;
        // This service owns PendingTimelineMutations; use the shared repository connection for both tables.
        await _database!.RunInTransactionAsync(connection =>
        {
            var source = RevalidateMutationEntry(connection, locationId, resolvedEntry);
            mutation = EnqueueMutationWithRollback(connection, locationId, latitude, longitude, localTimestamp,
                notes, includeNotes, activityTypeId, clearActivity, GetOriginalValues(source));
            if (source == null) return;

            ApplyLocalEntryUpdate(source, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeName, clearActivity);
            connection.Update(source);
        });
        return mutation;
    }

    /// <summary>Commits the confirmed deletion snapshot and its intent atomically with source removal.</summary>
    private async Task<PendingTimelineMutation> PersistDeleteMutationAsync(int locationId, LocalTimelineEntry? resolvedEntry)
    {
        PendingTimelineMutation mutation = null!;
        await _database!.RunInTransactionAsync(connection =>
        {
            var source = RevalidateMutationEntry(connection, locationId, resolvedEntry);
            mutation = EnqueueDeleteMutationWithRollback(connection, locationId, GetDeletedEntryJson(source), source);
            if (source != null)
                connection.Delete<LocalTimelineEntry>(source.Id);
        });
        return mutation;
    }

    /// <summary>
    /// Gets original values from LocalTimelineEntry for rollback support.
    /// </summary>
    private static (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType) GetOriginalValues(LocalTimelineEntry? localEntry)
    {
        if (localEntry == null)
            return (null, null, null, null, null, null);

        return (localEntry.Id, localEntry.Latitude, localEntry.Longitude, localEntry.Timestamp, localEntry.Notes, localEntry.ActivityType);
    }

    /// <summary>
    /// Applies an update to the local timeline entry (optimistic update).
    /// </summary>
    private static void ApplyLocalEntryUpdate(
        LocalTimelineEntry? localEntry,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes,
        string? activityTypeName,
        bool clearActivity)
    {
        if (localEntry?.IsSynced != true) return;

        // Apply optimistic update
        if (latitude.HasValue) localEntry.Latitude = latitude.Value;
        if (longitude.HasValue) localEntry.Longitude = longitude.Value;
        if (localTimestamp.HasValue) localEntry.Timestamp = localTimestamp.Value;
        if (includeNotes) localEntry.Notes = notes;
        // Activity: set name if provided, clear if requested
        if (!string.IsNullOrEmpty(activityTypeName)) localEntry.ActivityType = activityTypeName;
        if (clearActivity) localEntry.ActivityType = null;
    }

    /// <summary>
    /// Enqueues a mutation with rollback data persisted in the mutation entity.
    /// </summary>
    private static PendingTimelineMutation EnqueueMutationWithRollback(
        SQLiteConnection connection,
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes,
        int? activityTypeId,
        bool clearActivity,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType) originalValues)
    {
        var compatible = GetCompatibleMutations(connection, locationId, originalValues.localEntryId);
        var existing = compatible.FirstOrDefault(m => m.OperationType == "Update");

        if (existing != null)
        {
            // Merge with existing mutation (latest values win, keep original rollback data)
            if (latitude.HasValue) existing.Latitude = latitude;
            if (longitude.HasValue) existing.Longitude = longitude;
            if (localTimestamp.HasValue) existing.LocalTimestamp = localTimestamp;
            if (includeNotes)
            {
                existing.Notes = notes;
                existing.IncludeNotes = true;
            }
            // Activity: setting an activity clears the clear flag, clearing removes any pending activity
            if (activityTypeId.HasValue)
            {
                existing.ActivityTypeId = activityTypeId;
                existing.ClearActivity = false;
            }
            if (clearActivity)
            {
                existing.ClearActivity = true;
                existing.ActivityTypeId = null;
            }
            existing.CreatedAt = DateTime.UtcNow;
            connection.Update(existing);
            return existing;
        }
        else
        {
            var mutation = new PendingTimelineMutation
            {
                OperationType = "Update",
                LocationId = locationId,
                ServerIdentityConfirmed = true,
                LocalEntryId = originalValues.localEntryId,
                Latitude = latitude,
                Longitude = longitude,
                LocalTimestamp = localTimestamp,
                Notes = notes,
                IncludeNotes = includeNotes,
                ActivityTypeId = activityTypeId,
                ClearActivity = clearActivity,
                // Persist original values for rollback
                OriginalLatitude = originalValues.lat,
                OriginalLongitude = originalValues.lng,
                OriginalTimestamp = originalValues.timestamp,
                OriginalNotes = originalValues.notes,
                OriginalActivityType = originalValues.activityType,
                CreatedAt = DateTime.UtcNow
            };
            connection.Insert(mutation);
            return mutation;
        }
    }

    /// <summary>
    /// Gets the full entry serialized as JSON for delete rollback.
    /// </summary>
    private static string? GetDeletedEntryJson(LocalTimelineEntry? localEntry) =>
        localEntry == null ? null : JsonSerializer.Serialize(localEntry);

    /// <summary>
    /// Enqueues a delete mutation with rollback data (full entry as JSON).
    /// </summary>
    private static PendingTimelineMutation EnqueueDeleteMutationWithRollback(
        SQLiteConnection connection, int locationId, string? deletedEntryJson, LocalTimelineEntry? deletedEntry)
    {
        // Replace only work bound to the same proven source; retain ambiguous and unrelated rows.
        var compatible = GetCompatibleMutations(connection, locationId, deletedEntry?.Id);
        foreach (var existing in compatible)
            connection.Delete(existing);

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            ServerIdentityConfirmed = true,
            LocalEntryId = deletedEntry?.Id,
            DeletedEntryJson = deletedEntryJson,
            CreatedAt = DateTime.UtcNow
        };
        connection.Insert(mutation);
        return mutation;
    }

    /// <summary>
    /// Restores a deleted entry from JSON.
    /// </summary>
    private static void RestoreDeletedEntry(SQLiteConnection connection, PendingTimelineMutation mutation)
    {
        var deletedEntryJson = mutation.DeletedEntryJson;
        if (string.IsNullOrEmpty(deletedEntryJson)) return;

        try
        {
            var entry = JsonSerializer.Deserialize<LocalTimelineEntry>(deletedEntryJson);
            if (entry?.IsSynced != true || entry.ServerId != mutation.LocationId
                || (mutation.LocalEntryId.HasValue && entry.Id != mutation.LocalEntryId)) return;

            // Restore only the saved source identity; never overwrite a row that has taken its place.
            if (connection.Find<LocalTimelineEntry>(entry.Id) == null)
            {
                // SQLite-net's ordinary Insert omits auto-increment IDs; this preserves the vacant saved ID.
                connection.InsertOrReplace(entry);
            }
        }
        catch (JsonException)
        {
            // JSON deserialization failed - entry cannot be restored
        }
    }

    /// <summary>
    /// Reverts local entry using rollback data persisted in the mutation.
    /// </summary>
    private static void RevertLocalEntryFromMutation(SQLiteConnection connection, PendingTimelineMutation mutation)
    {
        if (mutation.OperationType == "Delete")
        {
            // Restore deleted entry from JSON
            RestoreDeletedEntry(connection, mutation);
        }
        else
        {
            // Revert updated fields
            if (!mutation.HasRollbackData || !mutation.LocalEntryId.HasValue) return;

            var localEntry = connection.Find<LocalTimelineEntry>(mutation.LocalEntryId.Value);
            if (localEntry?.IsSynced != true || localEntry.ServerId != mutation.LocationId) return;

            if (mutation.OriginalLatitude.HasValue) localEntry.Latitude = mutation.OriginalLatitude.Value;
            if (mutation.OriginalLongitude.HasValue) localEntry.Longitude = mutation.OriginalLongitude.Value;
            if (mutation.OriginalTimestamp.HasValue) localEntry.Timestamp = mutation.OriginalTimestamp.Value;
            localEntry.Notes = mutation.OriginalNotes;
            localEntry.ActivityType = mutation.OriginalActivityType;

            connection.Update(localEntry);
        }
    }

    #endregion
}
