namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Bridge interface for location sync events.
/// Allows Core services to observe sync events without depending on platform callbacks.
/// </summary>
public interface ILocationSyncEventBridge
{
    /// <summary>
    /// Event raised when a location is successfully synced to the server.
    /// </summary>
    event EventHandler<LocationSyncedBridgeEventArgs>? LocationSynced;
}

/// <summary>
/// Event arguments for location sync events (Core-compatible).
/// </summary>
public class LocationSyncedBridgeEventArgs : EventArgs
{
    /// <summary>
    /// Gets the server-assigned location ID.
    /// </summary>
    public int ServerId { get; init; }

    /// <summary>
    /// Gets the location timestamp (UTC).
    /// </summary>
    public DateTime Timestamp { get; init; }
}
