namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for timeline sync service.
/// Provides optimistic UI pattern for timeline location operations with offline support.
/// </summary>
public interface ITimelineSyncService
{
    /// <summary>
    /// Event raised when sync is rejected by server.
    /// </summary>
    event EventHandler<SyncFailureEventArgs>? SyncRejected;

    /// <summary>
    /// Event raised when sync is queued for offline retry.
    /// </summary>
    event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <summary>
    /// Event raised when sync completes successfully.
    /// </summary>
    event EventHandler<SyncSuccessEventArgs>? SyncCompleted;

    /// <summary>
    /// Update a timeline location with optimistic UI pattern.
    /// </summary>
    /// <param name="locationId">The location ID to update.</param>
    /// <param name="latitude">New latitude (optional).</param>
    /// <param name="longitude">New longitude (optional).</param>
    /// <param name="localTimestamp">New timestamp (optional).</param>
    /// <param name="notes">New notes HTML (optional).</param>
    /// <param name="includeNotes">Whether to include notes in update.</param>
    /// <param name="activityTypeId">New activity type ID for server (optional).</param>
    /// <param name="clearActivity">Whether to clear the activity.</param>
    /// <param name="activityTypeName">Activity name for optimistic local update (optional).</param>
    Task UpdateLocationAsync(
        int locationId,
        double? latitude = null,
        double? longitude = null,
        DateTime? localTimestamp = null,
        string? notes = null,
        bool includeNotes = false,
        int? activityTypeId = null,
        bool clearActivity = false,
        string? activityTypeName = null);

    /// <summary>
    /// Delete a timeline location with optimistic UI pattern.
    /// </summary>
    Task DeleteLocationAsync(int locationId);

    /// <summary>
    /// Process pending mutations when online.
    /// </summary>
    Task ProcessPendingMutationsAsync();

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Clear rejected mutations.
    /// </summary>
    Task ClearRejectedMutationsAsync();
}
