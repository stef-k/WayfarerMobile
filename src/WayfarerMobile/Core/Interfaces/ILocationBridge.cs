using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Bridge between the platform-specific location service and the MAUI UI layer.
/// Translates service broadcasts to C# events and sends commands to the service.
/// </summary>
public interface ILocationBridge
{
    /// <summary>
    /// Event raised when a new location is received from the tracking service.
    /// </summary>
    event EventHandler<LocationData>? LocationReceived;

    /// <summary>
    /// Event raised when the tracking state changes.
    /// </summary>
    event EventHandler<TrackingState>? StateChanged;

    /// <summary>
    /// Gets the current tracking state.
    /// </summary>
    TrackingState CurrentState { get; }

    /// <summary>
    /// Gets the current performance mode.
    /// </summary>
    PerformanceMode CurrentMode { get; }

    /// <summary>
    /// Gets the most recent location received.
    /// </summary>
    LocationData? LastLocation { get; }

    /// <summary>
    /// Starts the location tracking service.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the location tracking service.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Pauses the location tracking (service stays alive, GPS stops).
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Resumes the location tracking from paused state.
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Sets the GPS polling performance mode.
    /// </summary>
    /// <param name="mode">The performance mode to set.</param>
    Task SetPerformanceModeAsync(PerformanceMode mode);
}
