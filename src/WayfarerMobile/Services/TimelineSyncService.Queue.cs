using System.Text.Json;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;

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
    /// A freshly authorized deletion source may validate updates replaced after its optimistic removal.
    /// </summary>
    private async Task<string?> GetMutationAuthorityErrorAsync(
        PendingTimelineMutation mutation, LocalTimelineEntry? deletedSource = null)
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

            var source = await _timelineRepository.GetLocalTimelineEntryAsync(sourceId.Value);
            if (source == null && deletedSource?.Id == sourceId)
                source = deletedSource;
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
    private async Task<List<PendingTimelineMutation>> GetCompatibleMutationsAsync(
        int locationId, int? localEntryId, LocalTimelineEntry? deletedSource = null)
    {
        var candidates = await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsRejected && m.AuthorityError == null)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        var compatible = new List<PendingTimelineMutation>();
        foreach (var candidate in candidates)
        {
            if (candidate.LocalEntryId != localEntryId)
                continue;
            if (!localEntryId.HasValue && !candidate.ServerIdentityConfirmed)
                continue;
            if (await GetMutationAuthorityErrorAsync(candidate, deletedSource) == null)
                compatible.Add(candidate);
        }
        return compatible;
    }

    #region LocalTimelineEntry Integration (Persisted Rollback)

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
    private async Task ApplyLocalEntryUpdateAsync(
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

        await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Enqueues a mutation with rollback data persisted in the mutation entity.
    /// </summary>
    private async Task EnqueueMutationWithRollbackAsync(
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
        var compatible = await GetCompatibleMutationsAsync(locationId, originalValues.localEntryId);
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
            await _database!.UpdateAsync(existing);
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
            await _database!.InsertAsync(mutation);
        }
    }

    /// <summary>
    /// Reverts local entry using provided original values.
    /// </summary>
    private async Task RevertLocalEntryFromValuesAsync(
        int locationId,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType) originalValues)
    {
        if (!originalValues.localEntryId.HasValue) return;

        var localEntry = await _timelineRepository.GetLocalTimelineEntryAsync(originalValues.localEntryId.Value);
        if (localEntry?.IsSynced != true || localEntry.ServerId != locationId) return;

        if (originalValues.lat.HasValue) localEntry.Latitude = originalValues.lat.Value;
        if (originalValues.lng.HasValue) localEntry.Longitude = originalValues.lng.Value;
        if (originalValues.timestamp.HasValue) localEntry.Timestamp = originalValues.timestamp.Value;
        localEntry.Notes = originalValues.notes;
        localEntry.ActivityType = originalValues.activityType;

        await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Gets the full entry serialized as JSON for delete rollback.
    /// </summary>
    private static string? GetDeletedEntryJson(LocalTimelineEntry? localEntry) =>
        localEntry == null ? null : JsonSerializer.Serialize(localEntry);

    /// <summary>
    /// Deletes the local timeline entry (optimistic delete).
    /// </summary>
    private async Task ApplyLocalEntryDeleteAsync(LocalTimelineEntry? localEntry)
    {
        if (localEntry?.IsSynced != true) return;

        await _timelineRepository.DeleteLocalTimelineEntryAsync(localEntry.Id);
    }

    /// <summary>
    /// Enqueues a delete mutation with rollback data (full entry as JSON).
    /// </summary>
    private async Task EnqueueDeleteMutationWithRollbackAsync(
        int locationId, string? deletedEntryJson, LocalTimelineEntry? deletedEntry)
    {
        // Replace only work bound to the same proven source; retain ambiguous and unrelated rows.
        var compatible = await GetCompatibleMutationsAsync(locationId, deletedEntry?.Id, deletedEntry);
        foreach (var existing in compatible)
            await _database!.DeleteAsync(existing);

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            ServerIdentityConfirmed = true,
            LocalEntryId = deletedEntry?.Id,
            DeletedEntryJson = deletedEntryJson,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    /// <summary>
    /// Restores a deleted entry from JSON.
    /// </summary>
    private async Task RestoreDeletedEntryAsync(string? deletedEntryJson)
    {
        if (string.IsNullOrEmpty(deletedEntryJson)) return;

        try
        {
            var entry = JsonSerializer.Deserialize<LocalTimelineEntry>(deletedEntryJson);
            if (entry?.IsSynced != true) return;

            entry.Id = 0; // Reset ID for new insert
            await _timelineRepository.InsertLocalTimelineEntryAsync(entry);
        }
        catch (JsonException)
        {
            // JSON deserialization failed - entry cannot be restored
        }
    }

    /// <summary>
    /// Reverts local entry using rollback data persisted in the mutation.
    /// </summary>
    private async Task RevertLocalEntryFromMutationAsync(PendingTimelineMutation mutation)
    {
        if (mutation.OperationType == "Delete")
        {
            // Restore deleted entry from JSON
            await RestoreDeletedEntryAsync(mutation.DeletedEntryJson);
        }
        else
        {
            // Revert updated fields
            if (!mutation.HasRollbackData) return;

            var localEntry = mutation.LocalEntryId.HasValue
                ? await _timelineRepository.GetLocalTimelineEntryAsync(mutation.LocalEntryId.Value)
                : await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(mutation.LocationId);
            if (localEntry?.IsSynced != true || localEntry.ServerId != mutation.LocationId) return;

            if (mutation.OriginalLatitude.HasValue) localEntry.Latitude = mutation.OriginalLatitude.Value;
            if (mutation.OriginalLongitude.HasValue) localEntry.Longitude = mutation.OriginalLongitude.Value;
            if (mutation.OriginalTimestamp.HasValue) localEntry.Timestamp = mutation.OriginalTimestamp.Value;
            localEntry.Notes = mutation.OriginalNotes;
            localEntry.ActivityType = mutation.OriginalActivityType;

            await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
        }
    }

    #endregion
}
