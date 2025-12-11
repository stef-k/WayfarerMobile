using Android.App;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Platforms.Android.Receivers;
using WayfarerMobile.Services;
using Location = Android.Locations.Location;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Android foreground service that owns GPS acquisition, filtering, and location logging.
/// Uses Google Play Services FusedLocationProviderClient when available (better accuracy),
/// falls back to standard LocationManager for devices without Play Services.
/// </summary>
[Service(
    Name = "com.wayfarer.mobile.LocationTrackingService",
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeLocation,
    Exported = false)]
public class LocationTrackingService : Service, global::Android.Locations.ILocationListener
{
    #region Constants

    /// <summary>Notification channel ID for tracking notifications.</summary>
    public const string ChannelId = "wayfarer_tracking_channel";

    /// <summary>Notification ID for the foreground service.</summary>
    public const int NotificationId = 1001;

    /// <summary>Action to start tracking.</summary>
    public const string ActionStart = "com.wayfarer.mobile.ACTION_START";

    /// <summary>Action to stop tracking and service.</summary>
    public const string ActionStop = "com.wayfarer.mobile.ACTION_STOP";

    /// <summary>Action to pause tracking (service stays alive).</summary>
    public const string ActionPause = "com.wayfarer.mobile.ACTION_PAUSE";

    /// <summary>Action to resume tracking from paused state.</summary>
    public const string ActionResume = "com.wayfarer.mobile.ACTION_RESUME";

    /// <summary>Action to set high performance mode (1 second updates).</summary>
    public const string ActionSetHighPerformance = "com.wayfarer.mobile.ACTION_SET_HIGH_PERFORMANCE";

    /// <summary>Action to set normal mode (server-configured interval).</summary>
    public const string ActionSetNormal = "com.wayfarer.mobile.ACTION_SET_NORMAL";

    /// <summary>Action for check-in from notification (handled by NotificationActionReceiver).</summary>
    public const string ActionCheckIn = "com.wayfarer.mobile.ACTION_CHECK_IN";

    // Performance mode intervals (milliseconds)
    private const long HighPerformanceIntervalMs = 1000;
    private const long NormalIntervalMs = 60000;
    private const long PowerSaverIntervalMs = 300000;

    // Location filtering
    private const float MinAccuracyMeters = 100f;

    #endregion

    #region Fields

    // Google Play Services (primary - better accuracy)
    private IFusedLocationProviderClient? _fusedClient;
    private LocationCallback? _fusedCallback;
    private bool _hasPlayServices;

    // Fallback: Standard Android LocationManager
    private LocationManager? _locationManager;

    private NotificationManager? _notificationManager;
    private DatabaseService? _databaseService;
    private TrackingState _currentState = TrackingState.NotInitialized;
    private PerformanceMode _performanceMode = PerformanceMode.Normal;
    private LocationData? _lastLocation;
    private ThresholdFilter? _thresholdFilter;
    private int _locationCount;
    private bool _timelineTrackingEnabled = true;
    private readonly object _lock = new();

    #endregion

    #region Service Lifecycle

    /// <summary>
    /// Called when the service is created.
    /// </summary>
    public override void OnCreate()
    {
        base.OnCreate();

        _notificationManager = (NotificationManager?)GetSystemService(NotificationService);
        _databaseService = new DatabaseService();
        _thresholdFilter = new ThresholdFilter();

        // Load timeline tracking setting (controls whether locations are logged to server)
        _timelineTrackingEnabled = Preferences.Get("timeline_tracking_enabled", true);
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] TimelineTrackingEnabled: {_timelineTrackingEnabled}");

        // Load server thresholds for location filtering (respects server configuration)
        var timeThreshold = Preferences.Get("location_time_threshold", 1);
        var distanceThreshold = Preferences.Get("location_distance_threshold", 50);
        _thresholdFilter.UpdateThresholds(timeThreshold, distanceThreshold);
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Thresholds: {timeThreshold}min / {distanceThreshold}m");

        // Check for Google Play Services availability
        _hasPlayServices = GoogleApiAvailability.Instance
            .IsGooglePlayServicesAvailable(this) == ConnectionResult.Success;

        if (_hasPlayServices)
        {
            // Use Google's FusedLocationProvider - better accuracy through sensor fusion
            _fusedClient = LocationServices.GetFusedLocationProviderClient(this);
            _fusedCallback = new FusedLocationCallback(this);
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Using Google Play Services FusedLocationProvider");
        }
        else
        {
            // Fallback to standard LocationManager for devices without Play Services
            _locationManager = (LocationManager?)GetSystemService(LocationService);
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Using fallback LocationManager (no Play Services)");
        }

        CreateNotificationChannel();

        System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Service created");
    }

    /// <summary>
    /// Called when a component starts the service.
    /// </summary>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action ?? ActionStart;

        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] OnStartCommand: {action}");

        switch (action)
        {
            case ActionStart:
                StartTracking();
                break;

            case ActionStop:
                StopTracking();
                StopSelf();
                break;

            case ActionPause:
                PauseTracking();
                break;

            case ActionResume:
                ResumeTracking();
                break;

            case ActionSetHighPerformance:
                SetPerformanceMode(PerformanceMode.HighPerformance);
                break;

            case ActionSetNormal:
                SetPerformanceMode(PerformanceMode.Normal);
                break;
        }

        // Return Sticky so Android restarts the service if killed
        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Called when the service is destroyed.
    /// </summary>
    public override void OnDestroy()
    {
        System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Service destroyed");

        StopLocationUpdates();
        _currentState = TrackingState.NotInitialized;
        SendStateChangeBroadcast();

        base.OnDestroy();
    }

    /// <summary>
    /// Required for bound services (not used).
    /// </summary>
    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    #endregion

    #region Tracking Control

    /// <summary>
    /// Starts location tracking.
    /// </summary>
    private void StartTracking()
    {
        lock (_lock)
        {
            if (_currentState == TrackingState.Active)
            {
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Already tracking");
                return;
            }

            _currentState = TrackingState.Starting;
            SendStateChangeBroadcast();

            // CRITICAL: Start foreground within 5 seconds of service start!
            var notification = CreateNotification("Starting...");
            StartForeground(NotificationId, notification);

            // Start location updates
            StartLocationUpdates();

            _currentState = TrackingState.Active;
            SendStateChangeBroadcast();
            UpdateNotification("Tracking active");

            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Tracking started");
        }
    }

    /// <summary>
    /// Stops location tracking and the service.
    /// </summary>
    private void StopTracking()
    {
        lock (_lock)
        {
            _currentState = TrackingState.Stopping;
            SendStateChangeBroadcast();

            StopLocationUpdates();
            StopForeground(StopForegroundFlags.Remove);

            _currentState = TrackingState.Ready;
            SendStateChangeBroadcast();

            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Tracking stopped");
        }
    }

    /// <summary>
    /// Pauses location tracking but keeps service alive.
    /// </summary>
    private void PauseTracking()
    {
        lock (_lock)
        {
            if (_currentState != TrackingState.Active)
                return;

            StopLocationUpdates();
            _currentState = TrackingState.Paused;
            SendStateChangeBroadcast();
            UpdateNotification("Tracking paused");

            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Tracking paused");
        }
    }

    /// <summary>
    /// Resumes location tracking from paused state.
    /// </summary>
    private void ResumeTracking()
    {
        lock (_lock)
        {
            if (_currentState != TrackingState.Paused)
                return;

            StartLocationUpdates();
            _currentState = TrackingState.Active;
            SendStateChangeBroadcast();
            UpdateNotification("Tracking active");

            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Tracking resumed");
        }
    }

    /// <summary>
    /// Sets the GPS polling performance mode.
    /// </summary>
    private void SetPerformanceMode(PerformanceMode mode)
    {
        lock (_lock)
        {
            if (_performanceMode == mode)
                return;

            _performanceMode = mode;
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Performance mode: {mode}");

            // Restart location updates with new interval
            if (_currentState == TrackingState.Active)
            {
                StopLocationUpdates();
                StartLocationUpdates();
            }
        }
    }

    #endregion

    #region Location Updates

    /// <summary>
    /// Starts requesting location updates using the best available provider.
    /// </summary>
    private void StartLocationUpdates()
    {
        try
        {
            if (_hasPlayServices && _fusedClient != null)
            {
                StartFusedLocationUpdates();
            }
            else
            {
                StartFallbackLocationUpdates();
            }
        }
        catch (Java.Lang.SecurityException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Permission denied: {ex.Message}");
            _currentState = TrackingState.PermissionsDenied;
            SendStateChangeBroadcast();
        }
    }

    /// <summary>
    /// Starts location updates using Google Play Services FusedLocationProvider.
    /// This provides the best accuracy by fusing GPS, WiFi, Cell, and sensors.
    /// </summary>
    private void StartFusedLocationUpdates()
    {
        if (_fusedClient == null || _fusedCallback == null)
            return;

        var interval = GetCurrentInterval();

        var request = new global::Android.Gms.Location.LocationRequest.Builder(Priority.PriorityHighAccuracy, interval)
            .SetMinUpdateIntervalMillis(interval / 2)
            .SetMaxUpdateDelayMillis(interval * 2)
            .Build();

        _fusedClient.RequestLocationUpdates(request, _fusedCallback, Looper.MainLooper);

        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Fused location updates started (interval: {interval}ms)");
    }

    /// <summary>
    /// Starts location updates using standard Android LocationManager.
    /// Used as fallback for devices without Google Play Services.
    /// </summary>
    private void StartFallbackLocationUpdates()
    {
        if (_locationManager == null)
            return;

        var interval = GetCurrentInterval();

        // Request GPS updates
        if (_locationManager.IsProviderEnabled(LocationManager.GpsProvider))
        {
            _locationManager.RequestLocationUpdates(
                LocationManager.GpsProvider,
                interval,
                0f,
                this);
        }

        // Also request network updates for redundancy
        if (_locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
        {
            _locationManager.RequestLocationUpdates(
                LocationManager.NetworkProvider,
                interval,
                0f,
                this);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Fallback location updates started (interval: {interval}ms)");
    }

    /// <summary>
    /// Stops requesting location updates.
    /// </summary>
    private void StopLocationUpdates()
    {
        try
        {
            if (_hasPlayServices && _fusedClient != null && _fusedCallback != null)
            {
                _fusedClient.RemoveLocationUpdatesAsync(_fusedCallback);
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Fused location updates stopped");
            }
            else if (_locationManager != null)
            {
                _locationManager.RemoveUpdates(this);
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Fallback location updates stopped");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Error stopping updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current polling interval based on performance mode.
    /// </summary>
    private long GetCurrentInterval()
    {
        return _performanceMode switch
        {
            PerformanceMode.HighPerformance => HighPerformanceIntervalMs,
            PerformanceMode.PowerSaver => PowerSaverIntervalMs,
            _ => NormalIntervalMs
        };
    }

    #endregion

    #region Location Callbacks

    /// <summary>
    /// Callback for FusedLocationProvider updates.
    /// </summary>
    private class FusedLocationCallback : LocationCallback
    {
        private readonly LocationTrackingService _service;

        public FusedLocationCallback(LocationTrackingService service)
        {
            _service = service;
        }

        public override void OnLocationResult(LocationResult result)
        {
            var location = result.LastLocation;
            if (location != null)
            {
                _service.ProcessLocation(location);
            }
        }
    }

    /// <summary>
    /// Called when a new location is received (ILocationListener - fallback).
    /// </summary>
    public void OnLocationChanged(Location location)
    {
        if (location != null)
        {
            ProcessLocation(location);
        }
    }

    /// <summary>
    /// Processes a location update from either provider.
    /// </summary>
    internal void ProcessLocation(Location location)
    {
        // Filter by accuracy
        if (location.HasAccuracy && location.Accuracy > MinAccuracyMeters)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Location rejected: accuracy {location.Accuracy}m > {MinAccuracyMeters}m");
            return;
        }

        var locationData = ConvertToLocationData(location);

        lock (_lock)
        {
            _lastLocation = locationData;
            _locationCount++;
        }

        // Broadcast to UI
        SendLocationBroadcast(locationData);

        // Log to queue if timeline tracking is enabled
        if (_timelineTrackingEnabled && _thresholdFilter != null)
        {
            if (_thresholdFilter.TryLog(locationData))
            {
                LogLocationToQueue(locationData);
            }
        }

        // Update notification
        UpdateNotification($"Last: {DateTime.Now:HH:mm:ss} ({_locationCount} pts)");

        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Location: {locationData.Latitude:F6}, {locationData.Longitude:F6} " +
            $"(accuracy: {locationData.Accuracy:F1}m, provider: {locationData.Provider})");
    }

    /// <summary>
    /// Called when the provider is disabled (ILocationListener - fallback).
    /// </summary>
    public void OnProviderDisabled(string provider)
    {
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Provider disabled: {provider}");
    }

    /// <summary>
    /// Called when the provider is enabled (ILocationListener - fallback).
    /// </summary>
    public void OnProviderEnabled(string provider)
    {
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Provider enabled: {provider}");
    }

    /// <summary>
    /// Called when the provider status changes (ILocationListener - fallback).
    /// </summary>
    public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras)
    {
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Provider {provider} status: {status}");
    }

    #endregion

    #region Location Processing

    /// <summary>
    /// Converts an Android Location to our LocationData model.
    /// </summary>
    private static LocationData ConvertToLocationData(Location location)
    {
        // Determine provider source
        var provider = location.Provider ?? "unknown";
        if (provider == "fused")
        {
            provider = "gps+wifi+cell"; // Google's fusion combines these
        }

        return new LocationData
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.HasAltitude ? location.Altitude : null,
            Accuracy = location.HasAccuracy ? location.Accuracy : null,
            Speed = location.HasSpeed ? location.Speed : null,
            Bearing = location.HasBearing ? location.Bearing : null,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time).UtcDateTime,
            Provider = provider
        };
    }

    /// <summary>
    /// Logs a location to the sync queue (SQLite).
    /// </summary>
    private async void LogLocationToQueue(LocationData location)
    {
        if (_databaseService == null)
            return;

        try
        {
            await _databaseService.QueueLocationAsync(location);
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Queued for sync: {location}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Failed to queue location: {ex.Message}");
        }
    }

    #endregion

    #region Callbacks

    /// <summary>
    /// Notifies listeners of a location update using static callbacks.
    /// </summary>
    private void SendLocationBroadcast(LocationData location)
    {
        LocationServiceCallbacks.NotifyLocationReceived(location);
    }

    /// <summary>
    /// Notifies listeners of a state change using static callbacks.
    /// </summary>
    private void SendStateChangeBroadcast()
    {
        LocationServiceCallbacks.NotifyStateChanged(_currentState);
    }

    #endregion

    #region Notifications

    /// <summary>
    /// Creates the notification channel for Android 8+.
    /// </summary>
    private void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "Location Tracking",
            NotificationImportance.Low)
        {
            Description = "Shows when location tracking is active"
        };

        _notificationManager?.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Creates a notification for the foreground service.
    /// </summary>
    private Notification CreateNotification(string text)
    {
        // PendingIntent flags - Immutable required for Android 12+
        var pendingIntentFlags = PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent;

        // Intent to open the app when notification is tapped
        var openAppIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
        var openAppPendingIntent = PendingIntent.GetActivity(
            this, 0, openAppIntent, pendingIntentFlags);

        // Check In action (broadcast to NotificationActionReceiver)
        var checkInIntent = new Intent(this, typeof(NotificationActionReceiver));
        checkInIntent.SetAction(NotificationActionReceiver.ActionCheckIn);
        var checkInPendingIntent = PendingIntent.GetBroadcast(
            this, 1, checkInIntent, pendingIntentFlags);

        // Pause/Resume action (broadcast to NotificationActionReceiver)
        var isPaused = _currentState == TrackingState.Paused;
        var pauseIntent = new Intent(this, typeof(NotificationActionReceiver));
        pauseIntent.SetAction(NotificationActionReceiver.ActionPauseResume);
        pauseIntent.PutExtra("is_paused", isPaused);
        var pausePendingIntent = PendingIntent.GetBroadcast(
            this, 2, pauseIntent, pendingIntentFlags);
        var pauseText = isPaused ? "Resume" : "Pause";

        // Stop action (broadcast to NotificationActionReceiver)
        var stopIntent = new Intent(this, typeof(NotificationActionReceiver));
        stopIntent.SetAction(NotificationActionReceiver.ActionStop);
        var stopPendingIntent = PendingIntent.GetBroadcast(
            this, 3, stopIntent, pendingIntentFlags);

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("WayfarerMobile");
        builder.SetContentText(text);
        builder.SetSmallIcon(Resource.Drawable.ic_notification);
        builder.SetOngoing(true);
        builder.SetContentIntent(openAppPendingIntent);
        builder.AddAction(0, "Check In", checkInPendingIntent);
        builder.AddAction(0, pauseText, pausePendingIntent);
        builder.AddAction(0, "Stop", stopPendingIntent);
        builder.SetPriority(NotificationCompat.PriorityLow);

        return builder.Build()!;
    }

    /// <summary>
    /// Updates the notification text.
    /// </summary>
    private void UpdateNotification(string text)
    {
        var notification = CreateNotification(text);
        _notificationManager?.Notify(NotificationId, notification);
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Sets whether timeline tracking (server logging) is enabled.
    /// </summary>
    public void SetTimelineTrackingEnabled(bool enabled)
    {
        _timelineTrackingEnabled = enabled;
    }

    /// <summary>
    /// Updates the location filtering thresholds from server configuration.
    /// </summary>
    /// <param name="timeMinutes">Minimum time between logged locations.</param>
    /// <param name="distanceMeters">Minimum distance between logged locations.</param>
    public void UpdateThresholds(int timeMinutes, int distanceMeters)
    {
        _thresholdFilter?.UpdateThresholds(timeMinutes, distanceMeters);
        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Thresholds updated: {timeMinutes}min / {distanceMeters}m");
    }


    #endregion
}
