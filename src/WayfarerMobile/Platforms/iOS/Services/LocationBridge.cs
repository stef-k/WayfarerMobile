using System.Text.Json;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of ILocationBridge.
/// Bridges between the iOS LocationTrackingService and the MAUI UI layer.
/// Persists last location for instant availability on cold start.
/// </summary>
public class LocationBridge : ILocationBridge, IDisposable
{
    #region Constants

    private const string LastLocationKey = "last_known_location";

    #endregion

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
    /// Loads persisted location for instant availability.
    /// </summary>
    public LocationBridge()
    {
        // Load persisted location for instant availability
        LastLocation = LoadPersistedLocation();

        Register();

        Console.WriteLine($"[iOS LocationBridge] Initialized, persisted location: {(LastLocation != null ? "available" : "none")}");
    }

    #endregion

    #region Callback Handlers

    /// <summary>
    /// Handles location updates from the service.
    /// Persists location for instant availability on next app start.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        LastLocation = location;
        PersistLocation(location);
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

        Console.WriteLine("[iOS LocationBridge] Registered for callbacks");
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

        Console.WriteLine("[iOS LocationBridge] Unregistered from callbacks");
    }

    #endregion

    #region ILocationBridge Implementation

    /// <summary>
    /// Starts location tracking.
    /// </summary>
    public Task StartAsync()
    {
        LocationTrackingService.Instance.Start();
        Console.WriteLine("[iOS LocationBridge] Start command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops location tracking.
    /// </summary>
    public Task StopAsync()
    {
        LocationTrackingService.Instance.Stop();
        Console.WriteLine("[iOS LocationBridge] Stop command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses location tracking.
    /// </summary>
    public Task PauseAsync()
    {
        LocationTrackingService.Instance.Pause();
        Console.WriteLine("[iOS LocationBridge] Pause command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes location tracking from paused state.
    /// </summary>
    public Task ResumeAsync()
    {
        LocationTrackingService.Instance.Resume();
        Console.WriteLine("[iOS LocationBridge] Resume command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the performance mode for location updates.
    /// </summary>
    public Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        LocationTrackingService.Instance.SetPerformanceMode(mode);
        CurrentMode = mode;
        Console.WriteLine($"[iOS LocationBridge] Performance mode set to {mode}");
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

    #region Location Persistence

    /// <summary>
    /// Persists location to preferences for instant availability on cold start.
    /// </summary>
    private static void PersistLocation(LocationData location)
    {
        try
        {
            var json = JsonSerializer.Serialize(location);
            Preferences.Set(LastLocationKey, json);
        }
        catch
        {
            // Non-critical, ignore persistence errors
        }
    }

    /// <summary>
    /// Loads persisted location from preferences.
    /// </summary>
    private static LocationData? LoadPersistedLocation()
    {
        try
        {
            var json = Preferences.Get(LastLocationKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<LocationData>(json);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
