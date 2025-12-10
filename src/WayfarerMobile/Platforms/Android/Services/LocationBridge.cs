using Android.Content;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Bridge between Android LocationTrackingService and MAUI/C# events.
/// Uses static callbacks instead of LocalBroadcastManager for modern, efficient communication.
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

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Registered for callbacks");
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

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Unregistered from callbacks");
    }

    #endregion

    #region ILocationBridge Implementation

    /// <summary>
    /// Starts location tracking by sending a START command to the service.
    /// </summary>
    public Task StartAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionStart);

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Start command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops location tracking by sending a STOP command to the service.
    /// </summary>
    public Task StopAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionStop);
        context.StartService(intent);

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Stop command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses location tracking by sending a PAUSE command to the service.
    /// </summary>
    public Task PauseAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionPause);
        context.StartService(intent);

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Pause command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes location tracking by sending a RESUME command to the service.
    /// </summary>
    public Task ResumeAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionResume);
        context.StartService(intent);

        System.Diagnostics.Debug.WriteLine("[LocationBridge] Resume command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the performance mode for location updates.
    /// </summary>
    public Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));

        var action = mode switch
        {
            PerformanceMode.HighPerformance => LocationTrackingService.ActionSetHighPerformance,
            _ => LocationTrackingService.ActionSetNormal
        };

        intent.SetAction(action);
        context.StartService(intent);

        CurrentMode = mode;
        System.Diagnostics.Debug.WriteLine($"[LocationBridge] Performance mode set to {mode}");
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
