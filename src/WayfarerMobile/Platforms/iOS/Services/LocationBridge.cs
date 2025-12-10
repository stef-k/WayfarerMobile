using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of ILocationBridge.
/// Bridges between the iOS LocationTrackingService and the MAUI UI layer.
/// </summary>
public class LocationBridge : ILocationBridge, IDisposable
{
    #region Fields

    private bool _isRegistered;
    private bool _disposed;
    private TrackingState _currentState = TrackingState.NotInitialized;
    private PerformanceMode _currentMode = PerformanceMode.Normal;
    private readonly object _lock = new();

    #endregion

    #region Events

    /// <summary>
    /// Raised when a new location is received from the tracking service.
    /// </summary>
    public event EventHandler<LocationData>? LocationReceived;

    /// <summary>
    /// Raised when the tracking state changes.
    /// </summary>
    public event EventHandler<TrackingState>? StateChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current tracking state.
    /// </summary>
    public TrackingState CurrentState
    {
        get { lock (_lock) return _currentState; }
        private set { lock (_lock) _currentState = value; }
    }

    /// <summary>
    /// Gets the current performance mode.
    /// </summary>
    public PerformanceMode CurrentMode
    {
        get { lock (_lock) return _currentMode; }
        private set { lock (_lock) _currentMode = value; }
    }

    /// <summary>
    /// Gets the last received location, if any.
    /// </summary>
    public LocationData? LastLocation { get; private set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of LocationBridge and registers for callbacks.
    /// </summary>
    public LocationBridge()
    {
        Register();
    }

    #endregion

    #region Callback Handlers

    /// <summary>
    /// Handles location updates from the service.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        LastLocation = location;
        LocationReceived?.Invoke(this, location);
    }

    /// <summary>
    /// Handles state changes from the service.
    /// </summary>
    private void OnStateChanged(object? sender, TrackingState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }

    #endregion

    #region Registration

    /// <summary>
    /// Registers for callbacks from the location service.
    /// </summary>
    private void Register()
    {
        if (_isRegistered)
            return;

        LocationServiceCallbacks.LocationReceived += OnLocationReceived;
        LocationServiceCallbacks.StateChanged += OnStateChanged;
        _isRegistered = true;

        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Registered for callbacks");
    }

    /// <summary>
    /// Unregisters from callbacks.
    /// </summary>
    private void Unregister()
    {
        if (!_isRegistered)
            return;

        LocationServiceCallbacks.LocationReceived -= OnLocationReceived;
        LocationServiceCallbacks.StateChanged -= OnStateChanged;
        _isRegistered = false;

        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Unregistered from callbacks");
    }

    #endregion

    #region ILocationBridge Implementation

    /// <summary>
    /// Starts location tracking.
    /// </summary>
    public Task StartAsync()
    {
        LocationTrackingService.Instance.Start();
        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Start command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops location tracking.
    /// </summary>
    public Task StopAsync()
    {
        LocationTrackingService.Instance.Stop();
        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Stop command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses location tracking.
    /// </summary>
    public Task PauseAsync()
    {
        LocationTrackingService.Instance.Pause();
        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Pause command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes location tracking from paused state.
    /// </summary>
    public Task ResumeAsync()
    {
        LocationTrackingService.Instance.Resume();
        System.Diagnostics.Debug.WriteLine("[iOS LocationBridge] Resume command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the performance mode for location updates.
    /// </summary>
    public Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        LocationTrackingService.Instance.SetPerformanceMode(mode);
        CurrentMode = mode;
        System.Diagnostics.Debug.WriteLine($"[iOS LocationBridge] Performance mode set to {mode}");
        return Task.CompletedTask;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes resources and unregisters from callbacks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Unregister();
        _disposed = true;
    }

    #endregion
}
