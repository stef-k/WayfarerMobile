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
    /// Stores the last received location for quick access (e.g., notification check-in).
    /// </summary>
    private static LocationData? _lastReceivedLocation;

    /// <summary>
    /// Gets the last received location from GPS.
    /// Used by notification actions to perform check-in without waiting for new location.
    /// </summary>
    public static LocationData? LastLocation => _lastReceivedLocation;

    /// <summary>
    /// Event raised when a new location is received from GPS.
    /// </summary>
    public static event EventHandler<LocationData>? LocationReceived;

    /// <summary>
    /// Event raised when the tracking state changes.
    /// </summary>
    public static event EventHandler<TrackingState>? StateChanged;

    /// <summary>
    /// Event raised when a check-in is performed from the notification.
    /// </summary>
    public static event EventHandler<CheckInEventArgs>? CheckInPerformed;

    /// <summary>
    /// Event raised when a pause is requested from the notification.
    /// Platform-specific location services should subscribe to this event.
    /// </summary>
    public static event EventHandler? PauseRequested;

    /// <summary>
    /// Event raised when a resume is requested from the notification.
    /// Platform-specific location services should subscribe to this event.
    /// </summary>
    public static event EventHandler? ResumeRequested;

    /// <summary>
    /// Event raised when a stop is requested from the notification.
    /// Platform-specific location services should subscribe to this event.
    /// </summary>
    public static event EventHandler? StopRequested;

    /// <summary>
    /// Notifies listeners of a new location.
    /// Called by platform-specific location services.
    /// </summary>
    /// <param name="location">The location data.</param>
    public static void NotifyLocationReceived(LocationData location)
    {
        // Store for quick access by notification actions
        _lastReceivedLocation = location;

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

    /// <summary>
    /// Notifies listeners that a check-in was performed from the notification.
    /// Called by the NotificationActionReceiver after check-in attempt.
    /// </summary>
    /// <param name="success">Whether the check-in was successful.</param>
    /// <param name="errorMessage">Error message if check-in failed.</param>
    public static void NotifyCheckInPerformed(bool success, string? errorMessage)
    {
        // Ensure we're on the main thread for UI updates
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CheckInPerformed?.Invoke(null, new CheckInEventArgs(success, errorMessage));
        });
    }

    /// <summary>
    /// Requests the location service to pause tracking.
    /// Called from notification quick actions.
    /// </summary>
    public static void RequestPause()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PauseRequested?.Invoke(null, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Requests the location service to resume tracking.
    /// Called from notification quick actions.
    /// </summary>
    public static void RequestResume()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ResumeRequested?.Invoke(null, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Requests the location service to stop tracking.
    /// Called from notification quick actions.
    /// </summary>
    public static void RequestStop()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StopRequested?.Invoke(null, EventArgs.Empty);
        });
    }
}

/// <summary>
/// Event arguments for check-in events.
/// </summary>
public class CheckInEventArgs : EventArgs
{
    /// <summary>
    /// Gets whether the check-in was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the error message if check-in failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckInEventArgs"/> class.
    /// </summary>
    /// <param name="success">Whether the check-in was successful.</param>
    /// <param name="errorMessage">Error message if check-in failed.</param>
    public CheckInEventArgs(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }
}
