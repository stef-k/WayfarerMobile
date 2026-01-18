namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for timeline sync service.
/// Provides optimistic UI pattern for timeline location operations with offline support.
/// Implements background processing via timer and drain loop for autonomous syncing.
/// </summary>
public interface ITimelineSyncService : IDisposable
{
    /// <summary>
    /// Gets whether the drain loop is currently running.
    /// Used by callers to avoid unnecessary <see cref="StartDrainLoop"/> calls.
    /// </summary>
    bool IsDrainLoopRunning { get; }

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
    /// Get count of pending mutations.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Clear rejected mutations.
    /// </summary>
    Task ClearRejectedMutationsAsync();

    /// <summary>
    /// Starts the timeline sync service.
    /// Initializes timer-based processing and connectivity subscription.
    /// Should be called after authentication is configured.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the timeline sync service.
    /// Unsubscribes from connectivity and disposes timers.
    /// </summary>
    void Stop();

    /// <summary>
    /// Starts the drain loop if not already running.
    /// Safe to call frequently - uses atomic guard to prevent concurrent loops.
    /// Called by background location services to piggyback on location wakeups.
    /// </summary>
    /// <remarks>
    /// CRITICAL: This method is called from background location services.
    /// It MUST be completely fire-and-forget and NEVER throw exceptions.
    /// </remarks>
    void StartDrainLoop();

    /// <summary>
    /// Triggers an immediate drain cycle outside the normal timer schedule.
    /// Used by AppLifecycleService to flush pending mutations on suspend/resume.
    /// </summary>
    /// <remarks>
    /// This is a best-effort operation that respects rate limits.
    /// If the service is not started or disposed, returns immediately.
    /// </remarks>
    Task TriggerDrainAsync();
}
