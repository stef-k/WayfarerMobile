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
/// <para>
/// <b>Memory Leak Warning:</b> This class uses static events. Subscribers MUST unsubscribe
/// when they are disposed or go out of scope, otherwise they will be kept alive indefinitely
/// by the static event handlers, causing memory leaks.
/// </para>
/// <para>
/// <b>Correct usage pattern:</b>
/// <code>
/// // In constructor or initialization:
/// LocationSyncCallbacks.LocationSynced += OnLocationSynced;
///
/// // In Dispose or cleanup:
/// LocationSyncCallbacks.LocationSynced -= OnLocationSynced;
/// </code>
/// </para>
/// <para>
/// ViewModels should unsubscribe in their <c>Cleanup()</c> or <c>OnDisappearing()</c> methods.
/// Services should unsubscribe in their <c>Dispose()</c> methods.
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
    /// Called by <c>LocationSyncService</c> or <c>QueueDrainService</c> after successful sync.
    /// </summary>
    /// <param name="queuedLocationId">The local queued location ID.</param>
    /// <param name="serverId">The server-assigned location ID.</param>
    /// <param name="timestamp">The location timestamp (UTC).</param>
    /// <param name="latitude">The location latitude.</param>
    /// <param name="longitude">The location longitude.</param>
    /// <remarks>
    /// Event is dispatched to the main thread for UI safety.
    /// </remarks>
    public static void NotifyLocationSynced(
        int queuedLocationId,
        int serverId,
        DateTime timestamp,
        double latitude,
        double longitude)
    {
        var args = new LocationSyncedEventArgs
        {
            QueuedLocationId = queuedLocationId,
            ServerId = serverId,
            Timestamp = timestamp,
            Latitude = latitude,
            Longitude = longitude
        };

        // Dispatch to main thread for UI safety (consistent with LocationServiceCallbacks)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                LocationSynced?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                // Prevent subscriber exceptions from crashing the app
                System.Diagnostics.Debug.WriteLine($"[LocationSyncCallbacks] LocationSynced subscriber exception: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Notifies listeners that a location sync was skipped by the server.
    /// Called by <c>LocationSyncService</c> or <c>QueueDrainService</c> when server returns skipped status.
    /// </summary>
    /// <param name="queuedLocationId">The local queued location ID.</param>
    /// <param name="timestamp">The location timestamp (UTC).</param>
    /// <param name="latitude">The location latitude.</param>
    /// <param name="longitude">The location longitude.</param>
    /// <param name="reason">The reason for skipping (e.g., "Threshold not met").</param>
    /// <remarks>
    /// Event is dispatched to the main thread for UI safety.
    /// </remarks>
    public static void NotifyLocationSkipped(
        int queuedLocationId,
        DateTime timestamp,
        double latitude,
        double longitude,
        string reason)
    {
        var args = new LocationSkippedEventArgs
        {
            QueuedLocationId = queuedLocationId,
            Timestamp = timestamp,
            Latitude = latitude,
            Longitude = longitude,
            Reason = reason
        };

        // Dispatch to main thread for UI safety (consistent with LocationServiceCallbacks)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                LocationSkipped?.Invoke(null, args);
            }
            catch (Exception ex)
            {
                // Prevent subscriber exceptions from crashing the app
                System.Diagnostics.Debug.WriteLine($"[LocationSyncCallbacks] LocationSkipped subscriber exception: {ex.Message}");
            }
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

    /// <summary>
    /// Gets the location latitude.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the location longitude.
    /// </summary>
    public double Longitude { get; init; }
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
    /// Gets the location latitude.
    /// </summary>
    public double Latitude { get; init; }

    /// <summary>
    /// Gets the location longitude.
    /// </summary>
    public double Longitude { get; init; }

    /// <summary>
    /// Gets the reason the location was skipped.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}
