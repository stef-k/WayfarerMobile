using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Shared callback handler for communication between platform location services and the UI layer.
/// This provides a platform-independent way to receive location updates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Memory Leak Warning:</b> This class uses static events. Subscribers MUST unsubscribe
/// when they are disposed or go out of scope, otherwise they will be kept alive indefinitely
/// by the static event handlers, causing memory leaks.
/// </para>
/// <para>
/// <b>Correct usage pattern:</b>
/// <code>
/// // In constructor or initialization:
/// LocationServiceCallbacks.LocationReceived += OnLocationReceived;
///
/// // In Dispose or cleanup:
/// LocationServiceCallbacks.LocationReceived -= OnLocationReceived;
/// </code>
/// </para>
/// <para>
/// ViewModels should unsubscribe in their <c>Cleanup()</c> or <c>OnDisappearing()</c> methods.
/// Services should unsubscribe in their <c>Dispose()</c> methods.
/// </para>
/// </remarks>
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
    /// Used for UI updates (map, current position display).
    /// </summary>
    public static event EventHandler<LocationData>? LocationReceived;

    /// <summary>
    /// Event raised when a location is queued for sync.
    /// Used by LocalTimelineStorageService to store entries with correct coordinates.
    /// This may differ from LocationReceived when Android uses best-wake-sample optimization.
    /// </summary>
    public static event EventHandler<LocationData>? LocationQueued;

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
    /// Event raised when location thresholds are updated from the server.
    /// Platform-specific location services should subscribe to this event to update their filters.
    /// </summary>
    public static event EventHandler<ThresholdsUpdatedEventArgs>? ThresholdsUpdated;

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
            try
            {
                LocationReceived?.Invoke(null, location);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] LocationReceived subscriber exception: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Notifies listeners that a location was queued for sync.
    /// Called by platform-specific location services after successful queue.
    /// </summary>
    /// <param name="location">The location data that was queued (may differ from broadcast).</param>
    public static void NotifyLocationQueued(LocationData location)
    {
        // Ensure we're on the main thread for UI updates
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                LocationQueued?.Invoke(null, location);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] LocationQueued subscriber exception: {ex.Message}");
            }
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
            try
            {
                StateChanged?.Invoke(null, state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] StateChanged subscriber exception: {ex.Message}");
            }
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
            try
            {
                CheckInPerformed?.Invoke(null, new CheckInEventArgs(success, errorMessage));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] CheckInPerformed subscriber exception: {ex.Message}");
            }
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
            try
            {
                PauseRequested?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] PauseRequested subscriber exception: {ex.Message}");
            }
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
            try
            {
                ResumeRequested?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] ResumeRequested subscriber exception: {ex.Message}");
            }
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
            try
            {
                StopRequested?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] StopRequested subscriber exception: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Notifies platform services that location thresholds have been updated.
    /// Called by SettingsSyncService after syncing new thresholds from the server.
    /// </summary>
    /// <param name="timeMinutes">New time threshold in minutes.</param>
    /// <param name="distanceMeters">New distance threshold in meters.</param>
    /// <param name="accuracyMeters">New accuracy threshold in meters.</param>
    public static void NotifyThresholdsUpdated(int timeMinutes, int distanceMeters, int accuracyMeters)
    {
        // Platform services may be on background threads, so use MainThread
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                ThresholdsUpdated?.Invoke(null, new ThresholdsUpdatedEventArgs(timeMinutes, distanceMeters, accuracyMeters));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationServiceCallbacks] ThresholdsUpdated subscriber exception: {ex.Message}");
            }
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

/// <summary>
/// Event arguments for threshold update events.
/// </summary>
public class ThresholdsUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the new time threshold in minutes.
    /// </summary>
    public int TimeThresholdMinutes { get; }

    /// <summary>
    /// Gets the new distance threshold in meters.
    /// </summary>
    public int DistanceThresholdMeters { get; }

    /// <summary>
    /// Gets the new accuracy threshold in meters.
    /// </summary>
    public int AccuracyThresholdMeters { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThresholdsUpdatedEventArgs"/> class.
    /// </summary>
    /// <param name="timeMinutes">New time threshold in minutes.</param>
    /// <param name="distanceMeters">New distance threshold in meters.</param>
    /// <param name="accuracyMeters">New accuracy threshold in meters.</param>
    public ThresholdsUpdatedEventArgs(int timeMinutes, int distanceMeters, int accuracyMeters)
    {
        TimeThresholdMinutes = timeMinutes;
        DistanceThresholdMeters = distanceMeters;
        AccuracyThresholdMeters = accuracyMeters;
    }
}
