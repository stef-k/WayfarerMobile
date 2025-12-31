using System.Text.Json;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Util;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Bridge between Android LocationTrackingService and MAUI/C# events.
/// Uses static callbacks instead of LocalBroadcastManager for modern, efficient communication.
/// Persists last location for instant availability on cold start.
/// </summary>
public class LocationBridge : ILocationBridge, IDisposable
{
    #region Constants

    private const string LastLocationKey = "last_known_location";
    private const string LogTag = "WayfarerLocation";

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
    /// Loads persisted location for instant availability, then tries to get
    /// a fresher location from FusedLocationProviderClient.
    /// </summary>
    public LocationBridge()
    {
        // 1. Load persisted location for instant availability (synchronous)
        LastLocation = LoadPersistedLocation();

        Register();

        // 2. Try to get fresher location from FusedLocationProviderClient (async, non-blocking)
        _ = TryGetFusedLastLocationAsync();

        Log.Debug(LogTag, $"LocationBridge initialized, persisted location: {(LastLocation != null ? "available" : "none")}");
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

        Log.Debug(LogTag, "LocationBridge registered for callbacks");
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

        Log.Debug(LogTag, "LocationBridge unregistered from callbacks");
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

        Log.Debug(LogTag, "LocationBridge start command sent");
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

        Log.Debug(LogTag, "LocationBridge stop command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses location tracking by sending a PAUSE command to the service.
    /// Uses StartService (not StartForegroundService) - service handles state internally.
    /// </summary>
    public Task PauseAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionPause);
        context.StartService(intent);

        Log.Debug(LogTag, "LocationBridge pause command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes location tracking by sending a RESUME command to the service.
    /// Uses StartService (not StartForegroundService) - service handles state internally.
    /// </summary>
    public Task ResumeAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));
        intent.SetAction(LocationTrackingService.ActionResume);
        context.StartService(intent);

        Log.Debug(LogTag, "LocationBridge resume command sent");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the performance mode for location updates.
    /// Uses StartService (not StartForegroundService) to avoid starting service if not running.
    /// If service is running, it receives the mode change. If not running, command is ignored.
    /// </summary>
    public Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        CurrentMode = mode;

        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(LocationTrackingService));

        var action = mode switch
        {
            PerformanceMode.HighPerformance => LocationTrackingService.ActionSetHighPerformance,
            _ => LocationTrackingService.ActionSetNormal
        };

        intent.SetAction(action);

        // Use StartService (not StartForegroundService) - this delivers intent to running service
        // but does NOT start the service if it's not running
        context.StartService(intent);

        Log.Debug(LogTag, $"LocationBridge performance mode set to {mode}");
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

    #region Fused Location

    /// <summary>
    /// Tries to get the last known location from FusedLocationProviderClient.
    /// This is faster than waiting for GPS fix and may be fresher than persisted location.
    /// </summary>
    private async Task TryGetFusedLastLocationAsync()
    {
        try
        {
            var context = global::Android.App.Application.Context;

            // Check if Google Play Services is available
            var availability = GoogleApiAvailability.Instance;
            var resultCode = availability.IsGooglePlayServicesAvailable(context);
            if (resultCode != ConnectionResult.Success)
            {
                Log.Debug(LogTag, "LocationBridge: Google Play Services not available for quick location");
                return;
            }

            var fusedClient = LocationServices.GetFusedLocationProviderClient(context);
            var location = await fusedClient.GetLastLocationAsync();

            if (location != null)
            {
                var locationData = new LocationData
                {
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    Altitude = location.HasAltitude ? location.Altitude : null,
                    Accuracy = location.HasAccuracy ? location.Accuracy : null,
                    Speed = location.HasSpeed ? location.Speed : null,
                    Bearing = location.HasBearing ? location.Bearing : null,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time).UtcDateTime,
                    Provider = "fused-quick"
                };

                // Only update if fresher than persisted location
                if (LastLocation == null || locationData.Timestamp > LastLocation.Timestamp)
                {
                    LastLocation = locationData;
                    PersistLocation(locationData);

                    // Notify listeners of the quick location
                    LocationReceived?.Invoke(this, locationData);

                    Log.Debug(LogTag,
                        $"LocationBridge got quick fused location: {locationData.Latitude:F6}, {locationData.Longitude:F6} " +
                        $"(age: {(DateTime.UtcNow - locationData.Timestamp).TotalSeconds:F0}s)");
                }
            }
            else
            {
                Log.Debug(LogTag, "LocationBridge: FusedLocationProvider returned null");
            }
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"LocationBridge failed to get fused last location: {ex.Message}");
        }
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
