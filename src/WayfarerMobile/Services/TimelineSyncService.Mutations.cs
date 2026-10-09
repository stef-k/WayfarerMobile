using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

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
        // The service gate also excludes merging or replacing work being delivered by the drain.
        await _drainLock.WaitAsync();
        try
        {
            var localEntry = await ResolveMutationEntryAsync(identity);
            await EnsureInitializedAsync();
            var mutation = await PersistUpdateMutationAsync(identity.ServerId!.Value, localEntry,
                latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, activityTypeName);
            if (!_isOnline)
            {
                SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Saved offline - will sync when online" });
                return;
            }
            await DeliverImmediateMutationAsync(mutation);
        }
        finally
        {
            _drainLock.Release();
        }
    }

    /// <summary>
    /// Deletes a timeline location with optimistic UI pattern.
    /// Also deletes from LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task DeleteLocationAsync(TimelineEntryIdentity identity)
    {
        await _drainLock.WaitAsync();
        try
        {
            var localEntry = await ResolveMutationEntryAsync(identity);
            await EnsureInitializedAsync();
            var mutation = await PersistDeleteMutationAsync(identity.ServerId!.Value, localEntry);
            if (!_isOnline)
            {
                SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Deleted offline - will sync when online" });
                return;
            }
            await DeliverImmediateMutationAsync(mutation);
        }
        finally
        {
            _drainLock.Release();
        }
    }

    /// <summary>Delivers committed intent through the same processor as background retries.</summary>
    private async Task DeliverImmediateMutationAsync(PendingTimelineMutation mutation)
    {
        var result = await ProcessMutationAsync(mutation, CancellationToken.None, immediate: true);
        if (result == DrainAttemptResult.Failed)
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"{mutation.LastError} - will retry"
            });
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
