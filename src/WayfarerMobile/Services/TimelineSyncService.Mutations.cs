using System.Text.Json;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>Validates selected Timeline identity before optimistic updates or remote mutations.</summary>
public sealed partial class TimelineSyncService
{
    #region Public Mutation Methods

    /// <summary>
    /// Updates a timeline location with optimistic UI pattern.
    /// Also updates LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task UpdateLocationAsync(
        TimelineEntryIdentity identity,
        double? latitude = null,
        double? longitude = null,
        DateTime? localTimestamp = null,
        string? notes = null,
        bool includeNotes = false,
        int? activityTypeId = null,
        bool clearActivity = false,
        string? activityTypeName = null)
    {
        var localEntry = await ResolveMutationEntryAsync(identity);
        var locationId = identity.ServerId!.Value;
        await EnsureInitializedAsync();

        // Capture the same authorized row before applying changes.
        var originalValues = GetOriginalValues(localEntry);

        // Apply optimistic update to LocalTimelineEntry
        await ApplyLocalEntryUpdateAsync(localEntry, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeName, clearActivity);

        // Build request
        var request = new TimelineLocationUpdateRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            LocalTimestamp = localTimestamp,
            Notes = includeNotes ? notes : null,
            ActivityTypeId = activityTypeId,
            ClearActivity = clearActivity ? true : null
        };

        // Check connectivity first
        if (!_isOnline)
        {
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Saved offline - will sync when online" });
            return;
        }

        // Try server sync
        try
        {
            var response = await _apiClient.UpdateTimelineLocationAsync(locationId, request);

            if (response != null && response.Success)
            {
                // Success - no need to store rollback data
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            // Null or failed response - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, revert local changes using original values
            await RevertLocalEntryFromValuesAsync(locationId, originalValues);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected changes: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Deletes a timeline location with optimistic UI pattern.
    /// Also deletes from LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task DeleteLocationAsync(TimelineEntryIdentity identity)
    {
        var localEntry = await ResolveMutationEntryAsync(identity);
        var locationId = identity.ServerId!.Value;
        await EnsureInitializedAsync();

        // Retain the authorized source for both rollback and queue replacement.
        var deletedEntryJson = GetDeletedEntryJson(localEntry);
        await ApplyLocalEntryDeleteAsync(localEntry);

        if (!_isOnline)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson, localEntry);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeleteTimelineLocationAsync(locationId);

            if (success)
            {
                // Success - no rollback needed
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson, localEntry);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, restore local entry from JSON
            await RestoreDeletedEntryAsync(deletedEntryJson);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson, localEntry);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson, localEntry);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson, localEntry);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();
        // Inline CanSync expression - SQLite-net can't translate computed properties
        return await _database!.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.AuthorityError == null && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
            .CountAsync();
    }

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();
    }

    #endregion

    #region Helpers

    private static bool IsClientError(HttpRequestException ex)
    {
        // Check if it's a 4xx status code
        return ex.StatusCode.HasValue &&
               (int)ex.StatusCode.Value >= 400 &&
               (int)ex.StatusCode.Value < 500;
    }

    #endregion
}
