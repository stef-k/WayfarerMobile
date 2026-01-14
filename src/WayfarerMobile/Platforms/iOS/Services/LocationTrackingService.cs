using CoreLocation;
using Foundation;
using UIKit;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of background location tracking using Core Location.
/// Uses CLLocationManager with delegate methods for native iOS location services.
/// </summary>
public sealed class LocationTrackingService : NSObject, ICLLocationManagerDelegate
{
    #region Singleton

    private static readonly Lazy<LocationTrackingService> _instance =
        new(() => new LocationTrackingService());

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static LocationTrackingService Instance => _instance.Value;

    #endregion

    #region Fields

    private CLLocationManager? _locationManager;
    private TrackingState _currentState = TrackingState.NotInitialized;
    private PerformanceMode _currentMode = PerformanceMode.Normal;
    private LocationData? _lastLocation;
    private DatabaseService? _database;
    private ThresholdFilter? _thresholdFilter;

    // Filtering state
    private DateTime _lastLocationTime = DateTime.MinValue;
    private const double MinTimeBetweenUpdatesSeconds = 5.0;
    private const double MinDistanceMeters = 10.0;

    // Timeline tracking control
    private bool _timelineTrackingEnabled = true;

    #endregion

    #region Constructor

    private LocationTrackingService()
    {
        InitializeDatabase();
        InitializeThresholdFilter();
        SubscribeToCallbacks();
    }

    /// <summary>
    /// Subscribes to notification action callbacks.
    /// </summary>
    private void SubscribeToCallbacks()
    {
        LocationServiceCallbacks.PauseRequested += OnPauseRequested;
        LocationServiceCallbacks.ResumeRequested += OnResumeRequested;
        LocationServiceCallbacks.StopRequested += OnStopRequested;
        LocationServiceCallbacks.ThresholdsUpdated += OnThresholdsUpdated;
    }

    /// <summary>
    /// Handles threshold updates from server sync via LocationServiceCallbacks.
    /// </summary>
    private void OnThresholdsUpdated(object? sender, ThresholdsUpdatedEventArgs e)
    {
        UpdateThresholds(e.TimeThresholdMinutes, e.DistanceThresholdMeters, e.AccuracyThresholdMeters);
    }

    /// <summary>
    /// Handles pause request from notification.
    /// </summary>
    private void OnPauseRequested(object? sender, EventArgs e)
    {
        Pause();
        _ = TrackingNotificationService.Instance.UpdateNotificationAsync(true);
    }

    /// <summary>
    /// Handles resume request from notification.
    /// </summary>
    private void OnResumeRequested(object? sender, EventArgs e)
    {
        Resume();
        _ = TrackingNotificationService.Instance.UpdateNotificationAsync(false);
    }

    /// <summary>
    /// Handles stop request from notification.
    /// </summary>
    private void OnStopRequested(object? sender, EventArgs e)
    {
        Stop();
        _ = TrackingNotificationService.Instance.HideTrackingNotificationAsync();
    }

    /// <summary>
    /// Initializes the database service.
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            _database = new DatabaseService();
            Console.WriteLine("[iOS LocationService] Database service created");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocationService] Database init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the threshold filter with server-configured thresholds.
    /// </summary>
    private void InitializeThresholdFilter()
    {
        _thresholdFilter = new ThresholdFilter();

        // Load timeline tracking setting
        _timelineTrackingEnabled = Preferences.Get("timeline_tracking_enabled", true);

        // Load server thresholds for location filtering
        // Defaults match SettingsService: 5 min / 15 m / 50 m accuracy
        var timeThreshold = Preferences.Get("location_time_threshold", 5);
        var distanceThreshold = Preferences.Get("location_distance_threshold", 15);
        var accuracyThreshold = Preferences.Get("location_accuracy_threshold", 50);

        _thresholdFilter.UpdateThresholds(timeThreshold, distanceThreshold, accuracyThreshold);

        Console.WriteLine($"[iOS LocationService] Thresholds: {timeThreshold}min / {distanceThreshold}m / {accuracyThreshold}m accuracy");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts location tracking.
    /// </summary>
    public void Start()
    {
        if (_currentState == TrackingState.Active)
            return;

        try
        {
            _locationManager = new CLLocationManager
            {
                Delegate = this,
                DesiredAccuracy = CLLocation.AccuracyBest,
                DistanceFilter = MinDistanceMeters,
                PausesLocationUpdatesAutomatically = false
            };

            // Enable background updates on iOS 9+
            if (UIDevice.CurrentDevice.CheckSystemVersion(9, 0))
            {
                _locationManager.AllowsBackgroundLocationUpdates = true;
            }

            // Show blue status bar indicator when app is in background (iOS 11+)
            if (UIDevice.CurrentDevice.CheckSystemVersion(11, 0))
            {
                _locationManager.ShowsBackgroundLocationIndicator = true;
            }

            // Request always authorization for background tracking
            _locationManager.RequestAlwaysAuthorization();

            // Start updates
            _locationManager.StartUpdatingLocation();

            // Also start significant location changes for battery efficiency
            if (CLLocationManager.SignificantLocationChangeMonitoringAvailable)
            {
                _locationManager.StartMonitoringSignificantLocationChanges();
            }

            _currentState = TrackingState.Active;
            LocationServiceCallbacks.NotifyStateChanged(_currentState);

            Console.WriteLine("[iOS LocationService] Started");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocationService] Start failed: {ex.Message}");
            _currentState = TrackingState.Error;
            LocationServiceCallbacks.NotifyStateChanged(_currentState);
        }
    }

    /// <summary>
    /// Stops location tracking.
    /// </summary>
    public void Stop()
    {
        if (_locationManager == null)
            return;

        try
        {
            _locationManager.StopUpdatingLocation();
            _locationManager.StopMonitoringSignificantLocationChanges();

            if (UIDevice.CurrentDevice.CheckSystemVersion(9, 0))
            {
                _locationManager.AllowsBackgroundLocationUpdates = false;
            }

            // Hide blue status bar indicator
            if (UIDevice.CurrentDevice.CheckSystemVersion(11, 0))
            {
                _locationManager.ShowsBackgroundLocationIndicator = false;
            }

            _locationManager.Delegate = null!;
            _locationManager = null;

            _currentState = TrackingState.Ready;
            LocationServiceCallbacks.NotifyStateChanged(_currentState);

            Console.WriteLine("[iOS LocationService] Stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocationService] Stop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Pauses location tracking.
    /// </summary>
    public void Pause()
    {
        if (_locationManager == null || _currentState != TrackingState.Active)
            return;

        _locationManager.StopUpdatingLocation();
        _currentState = TrackingState.Paused;
        LocationServiceCallbacks.NotifyStateChanged(_currentState);

        Console.WriteLine("[iOS LocationService] Paused");
    }

    /// <summary>
    /// Resumes location tracking from paused state.
    /// </summary>
    public void Resume()
    {
        if (_locationManager == null || _currentState != TrackingState.Paused)
            return;

        _locationManager.StartUpdatingLocation();
        _currentState = TrackingState.Active;
        LocationServiceCallbacks.NotifyStateChanged(_currentState);

        Console.WriteLine("[iOS LocationService] Resumed");
    }

    /// <summary>
    /// Sets the performance mode.
    /// </summary>
    public void SetPerformanceMode(PerformanceMode mode)
    {
        _currentMode = mode;

        if (_locationManager == null)
            return;

        // Adjust accuracy and distance filter based on mode
        if (mode == PerformanceMode.HighPerformance)
        {
            _locationManager.DesiredAccuracy = CLLocation.AccuracyBestForNavigation;
            _locationManager.DistanceFilter = 5.0;
        }
        else
        {
            _locationManager.DesiredAccuracy = CLLocation.AccuracyBest;
            _locationManager.DistanceFilter = MinDistanceMeters;
        }

        Console.WriteLine($"[iOS LocationService] Performance mode: {mode}");
    }

    /// <summary>
    /// Updates the location filtering thresholds from server configuration.
    /// </summary>
    /// <param name="timeMinutes">Minimum time between logged locations.</param>
    /// <param name="distanceMeters">Minimum distance between logged locations.</param>
    /// <param name="accuracyMeters">Maximum acceptable GPS accuracy in meters.</param>
    public void UpdateThresholds(int timeMinutes, int distanceMeters, int accuracyMeters)
    {
        _thresholdFilter?.UpdateThresholds(timeMinutes, distanceMeters, accuracyMeters);
        Console.WriteLine($"[iOS LocationService] Thresholds updated: {timeMinutes}min / {distanceMeters}m / {accuracyMeters}m accuracy");
    }

    /// <summary>
    /// Sets whether timeline tracking (server logging) is enabled.
    /// </summary>
    public void SetTimelineTrackingEnabled(bool enabled)
    {
        _timelineTrackingEnabled = enabled;
        Console.WriteLine($"[iOS LocationService] Timeline tracking: {enabled}");
    }

    #endregion

    #region CLLocationManagerDelegate

    /// <summary>
    /// Called when new locations are available.
    /// </summary>
    [Export("locationManager:didUpdateLocations:")]
    public void LocationsUpdated(CLLocationManager manager, CLLocation[] locations)
    {
        if (locations.Length == 0)
            return;

        // Get the most recent location
        var clLocation = locations[^1];

        // Apply time-based filtering (coarse filter for high-frequency updates)
        var now = DateTime.UtcNow;
        if ((now - _lastLocationTime).TotalSeconds < MinTimeBetweenUpdatesSeconds)
        {
            return;
        }

        // Convert to our LocationData model
        var location = new LocationData
        {
            Latitude = clLocation.Coordinate.Latitude,
            Longitude = clLocation.Coordinate.Longitude,
            Accuracy = clLocation.HorizontalAccuracy >= 0 ? clLocation.HorizontalAccuracy : null,
            Altitude = clLocation.Altitude,
            Speed = clLocation.Speed >= 0 ? clLocation.Speed : null,
            Bearing = clLocation.Course >= 0 ? clLocation.Course : null,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(
                (long)clLocation.Timestamp.SecondsSinceReferenceDate + 978307200).UtcDateTime,
            Provider = "ios-gps"
        };

        _lastLocation = location;
        _lastLocationTime = now;

        // Notify listeners via static callbacks (always - for map updates, etc.)
        LocationServiceCallbacks.NotifyLocationReceived(location);

        // Apply threshold filter before queuing for server sync
        // Only queue if timeline tracking is enabled and location passes filter
        if (_timelineTrackingEnabled && _thresholdFilter != null)
        {
            if (_thresholdFilter.TryLog(location))
            {
                _ = QueueLocationAsync(location);
                Console.WriteLine($"[iOS LocationService] Queued: {location.Latitude:F6}, {location.Longitude:F6}, accuracy: {location.Accuracy:F1}m");
            }
            else
            {
                Console.WriteLine($"[iOS LocationService] Filtered: {location.Latitude:F6}, {location.Longitude:F6}, accuracy: {location.Accuracy:F1}m (below threshold)");
            }
        }
        else if (!_timelineTrackingEnabled)
        {
            Console.WriteLine($"[iOS LocationService] Skipped: timeline tracking disabled");
        }
    }

    /// <summary>
    /// Called when location manager fails.
    /// </summary>
    [Export("locationManager:didFailWithError:")]
    public void Failed(CLLocationManager manager, NSError error)
    {
        Console.WriteLine($"[iOS LocationService] Error: {error.LocalizedDescription}");

        if (error.Code == (long)CLError.Denied)
        {
            _currentState = TrackingState.PermissionsDenied;
            LocationServiceCallbacks.NotifyStateChanged(_currentState);
        }
    }

    /// <summary>
    /// Called when authorization status changes.
    /// </summary>
    [Export("locationManager:didChangeAuthorizationStatus:")]
    public void AuthorizationChanged(CLLocationManager manager, CLAuthorizationStatus status)
    {
        Console.WriteLine($"[iOS LocationService] Auth status: {status}");

        switch (status)
        {
            case CLAuthorizationStatus.AuthorizedAlways:
            case CLAuthorizationStatus.AuthorizedWhenInUse:
                if (_currentState == TrackingState.PermissionsNeeded ||
                    _currentState == TrackingState.PermissionsDenied)
                {
                    _currentState = TrackingState.Ready;
                    LocationServiceCallbacks.NotifyStateChanged(_currentState);
                }
                break;

            case CLAuthorizationStatus.Denied:
            case CLAuthorizationStatus.Restricted:
                _currentState = TrackingState.PermissionsDenied;
                LocationServiceCallbacks.NotifyStateChanged(_currentState);
                break;
        }
    }

    #endregion

    #region Database

    /// <summary>
    /// Queues a location for sync to the server.
    /// </summary>
    private async Task QueueLocationAsync(LocationData location)
    {
        if (_database == null)
            return;

        try
        {
            await _database.QueueLocationAsync(location);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocationService] Queue failed: {ex.Message}");
        }
    }

    #endregion
}
