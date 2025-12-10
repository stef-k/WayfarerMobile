using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Shared callback handler for communication between platform location services and the UI layer.
/// This provides a platform-independent way to receive location updates.
/// </summary>
public static class LocationServiceCallbacks
{
    /// <summary>
    /// Event raised when a new location is received from GPS.
    /// </summary>
    public static event EventHandler<LocationData>? LocationReceived;

    /// <summary>
    /// Event raised when the tracking state changes.
    /// </summary>
    public static event EventHandler<TrackingState>? StateChanged;

    /// <summary>
    /// Notifies listeners of a new location.
    /// Called by platform-specific location services.
    /// </summary>
    /// <param name="location">The location data.</param>
    public static void NotifyLocationReceived(LocationData location)
    {
        // Ensure we're on the main thread for UI updates
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LocationReceived?.Invoke(null, location);
        });
    }

    /// <summary>
    /// Notifies listeners of a state change.
    /// Called by platform-specific location services.
    /// </summary>
    /// <param name="state">The new tracking state.</param>
    public static void NotifyStateChanged(TrackingState state)
    {
        // Ensure we're on the main thread for UI updates
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StateChanged?.Invoke(null, state);
        });
    }
}
