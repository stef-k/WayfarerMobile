namespace WayfarerMobile.Services;

/// <summary>
/// Event-based callback system for location sync operations.
/// Enables decoupled communication between sync service and local timeline storage.
/// </summary>
/// <remarks>
/// <para>
/// This follows the same pattern as <see cref="LocationServiceCallbacks"/> for consistency.
/// </para>
/// <para>
/// Subscribers (e.g., <c>LocalTimelineStorageService</c>) listen for sync events
/// to update local timeline storage without modifying the sync service directly.
/// </para>
/// </remarks>
public static class LocationSyncCallbacks
{
    /// <summary>
    /// Event raised when a location is successfully synced to the server.
    /// The server has accepted and stored the location with a unique ID.
    /// </summary>
    public static event EventHandler<LocationSyncedEventArgs>? LocationSynced;

    /// <summary>
    /// Event raised when a location sync was skipped by the server.
    /// The server received the location but did not store it (thresholds not met).
    /// </summary>
    public static event EventHandler<LocationSkippedEventArgs>? LocationSkipped;

    /// <summary>
    /// Notifies listeners that a location was successfully synced to the server.
    /// Called by <c>LocationSyncService</c> after successful sync.
    /// </summary>
    /// <param name="queuedLocationId">The local queued location ID.</param>
    /// <param name="serverId">The server-assigned location ID.</param>
    /// <param name="timestamp">The location timestamp (UTC).</param>
    public static void NotifyLocationSynced(int queuedLocationId, int serverId, DateTime timestamp)
    {
        LocationSynced?.Invoke(null, new LocationSyncedEventArgs
        {
            QueuedLocationId = queuedLocationId,
            ServerId = serverId,
            Timestamp = timestamp
        });
    }

    /// <summary>
    /// Notifies listeners that a location sync was skipped by the server.
    /// Called by <c>LocationSyncService</c> when server returns skipped status.
    /// </summary>
    /// <param name="queuedLocationId">The local queued location ID.</param>
    /// <param name="timestamp">The location timestamp (UTC).</param>
    /// <param name="reason">The reason for skipping (e.g., "Threshold not met").</param>
    public static void NotifyLocationSkipped(int queuedLocationId, DateTime timestamp, string reason)
    {
        LocationSkipped?.Invoke(null, new LocationSkippedEventArgs
        {
            QueuedLocationId = queuedLocationId,
            Timestamp = timestamp,
            Reason = reason
        });
    }

    /// <summary>
    /// Clears all event subscribers.
    /// Used for testing to ensure clean state between tests.
    /// </summary>
    internal static void ClearSubscribers()
    {
        LocationSynced = null;
        LocationSkipped = null;
    }
}

/// <summary>
/// Event arguments for successful location sync events.
/// </summary>
public class LocationSyncedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the local queued location ID.
    /// </summary>
    public int QueuedLocationId { get; init; }

    /// <summary>
    /// Gets the server-assigned location ID.
    /// Used to link local entries with server records for reconciliation.
    /// </summary>
    public int ServerId { get; init; }

    /// <summary>
    /// Gets the location timestamp (UTC).
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// Event arguments for skipped location sync events.
/// </summary>
public class LocationSkippedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the local queued location ID.
    /// </summary>
    public int QueuedLocationId { get; init; }

    /// <summary>
    /// Gets the location timestamp (UTC).
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the reason the location was skipped.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}
