using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Bridges the static LocationSyncCallbacks to the ILocationSyncEventBridge interface.
/// Allows Core services to observe sync events without depending on platform-specific callbacks.
/// </summary>
public class LocationSyncEventBridge : ILocationSyncEventBridge, IDisposable
{
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<LocationSyncedBridgeEventArgs>? LocationSynced;

    /// <summary>
    /// Creates a new instance and subscribes to LocationSyncCallbacks.
    /// </summary>
    public LocationSyncEventBridge()
    {
        LocationSyncCallbacks.LocationSynced += OnLocationSynced;
    }

    private void OnLocationSynced(object? sender, LocationSyncedEventArgs e)
    {
        // Bridge to Core-compatible event args
        LocationSynced?.Invoke(this, new LocationSyncedBridgeEventArgs
        {
            ServerId = e.ServerId,
            Timestamp = e.Timestamp
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        LocationSyncCallbacks.LocationSynced -= OnLocationSynced;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
