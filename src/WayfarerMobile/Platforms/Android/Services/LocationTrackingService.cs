using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Timer = System.Threading.Timer;

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
    // High: 1s for real-time map updates when app visible
    // Normal: 95s for background - balances battery vs server's 2-min threshold
    // PowerSaver: 5min for critical battery situations
    private const long HighPerformanceIntervalMs = 1000;
    private const long NormalIntervalMs = 95000;
    private const long PowerSaverIntervalMs = 300000;

    // Location filtering
    private const float MinAccuracyMeters = 100f;

    // Settings sync - runs every hour, syncs if 6+ hours elapsed
    private const long SettingsSyncCheckIntervalMs = 3600000; // 1 hour
    private static readonly TimeSpan SettingsSyncInterval = TimeSpan.FromHours(6);
    private const string LastSettingsSyncKey = "foreground_service_settings_sync";
    private const int SettingsSyncTimeoutSeconds = 15;

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

    // Settings sync timer (completely isolated from location tracking)
    private Timer? _settingsSyncTimer;
    private volatile bool _settingsSyncInProgress;

    #endregion

    #region Service Lifecycle

    /// <summary>
    /// Called when the service is created. Initializes all service dependencies and starts foreground mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>CRITICAL: Foreground Service 5-Second Rule</strong>
    /// </para>
    /// <para>
    /// Android 8.0+ (API 26+) requires that after calling <c>Context.startForegroundService()</c>,
    /// the service MUST call <c>startForeground()</c> within 5 seconds, or Android will throw
    /// <c>RemoteServiceException</c> and crash the app. This is a strict system requirement.
    /// </para>
    /// <para>
    /// <strong>Initialization Order (DO NOT REORDER WITHOUT UNDERSTANDING):</strong>
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <strong>Phase 1 - Foreground Setup (MUST complete within 5 seconds):</strong>
    ///       <list type="bullet">
    ///         <item><c>_notificationManager</c> - Required by CreateNotification()</item>
    ///         <item><c>CreateNotificationChannel()</c> - Required before posting notifications on API 26+</item>
    ///         <item><c>StartForeground()</c> - MUST be called here, before any blocking operations</item>
    ///       </list>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Phase 2 - Service Dependencies (can take time, runs after foreground is established):</strong>
    ///       <list type="bullet">
    ///         <item><c>DatabaseService</c> - May involve I/O, table creation on first run</item>
    ///         <item><c>ThresholdFilter</c> - Fast, but logically belongs with database service</item>
    ///         <item>Preferences loading - Fast, reads from shared preferences</item>
    ///         <item><c>GoogleApiAvailability</c> check - Can be slow (system IPC call)</item>
    ///         <item><c>FusedLocationProviderClient</c> setup - Depends on Google Play check</item>
    ///         <item>Settings sync timer - Background operation, no rush</item>
    ///       </list>
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// <strong>Why This Order Matters:</strong>
    /// </para>
    /// <para>
    /// The Phase 2 operations (especially DatabaseService and GoogleApiAvailability) can take
    /// 100-500ms+ on slower devices or during first-run scenarios. If StartForeground() is called
    /// after these operations, the cumulative time may exceed 5 seconds, causing a crash.
    /// </para>
    /// <para>
    /// <strong>Location Tracking Dependencies:</strong>
    /// </para>
    /// <para>
    /// The actual location tracking (GPS updates) starts in <c>OnStartCommand()</c> → <c>StartTracking()</c>
    /// → <c>StartLocationUpdates()</c>. Android guarantees that <c>OnStartCommand()</c> is called only
    /// AFTER <c>OnCreate()</c> completes, so all Phase 2 dependencies (<c>_hasPlayServices</c>,
    /// <c>_fusedClient</c>, <c>_databaseService</c>, etc.) will be fully initialized before
    /// location updates begin.
    /// </para>
    /// </remarks>
    public override void OnCreate()
    {
        base.OnCreate();

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // PHASE 1: FOREGROUND SETUP - Must complete within 5 seconds of startForegroundService()
        // DO NOT add blocking operations before StartForeground() call!
        // ═══════════════════════════════════════════════════════════════════════════════════════

        try
        {
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] OnCreate starting - initiating foreground");

            _notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            if (_notificationManager == null)
            {
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] WARNING: NotificationManager is null!");
            }

            CreateNotificationChannel();
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Notification channel created");

            // CRITICAL: Start foreground IMMEDIATELY to satisfy Android's 5-second requirement.
            var notification = CreateNotification("Initializing...");
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Notification created, calling StartForeground");

            StartForeground(NotificationId, notification);
            _currentState = TrackingState.Ready;
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Foreground started successfully in OnCreate");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] CRITICAL ERROR in Phase 1: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Stack trace: {ex.StackTrace}");
            throw; // Re-throw to see the real error
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // PHASE 2: SERVICE DEPENDENCIES - Safe to take time now, foreground is established
        // These are needed by StartLocationUpdates() which runs later in OnStartCommand()
        // ═══════════════════════════════════════════════════════════════════════════════════════

        _databaseService = new DatabaseService();
        _thresholdFilter = new ThresholdFilter();

        // Load timeline tracking setting (controls whether locations are logged to server)
        _timelineTrackingEnabled = Preferences.Get("timeline_tracking_enabled", true);
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] TimelineTrackingEnabled: {_timelineTrackingEnabled}");

        // Load server thresholds for location filtering (respects server configuration)
        // Defaults match SettingsService: 5 min / 15 m
        var timeThreshold = Preferences.Get("location_time_threshold", 5);
        var distanceThreshold = Preferences.Get("location_distance_threshold", 15);
        _thresholdFilter.UpdateThresholds(timeThreshold, distanceThreshold);
        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Thresholds: {timeThreshold}min / {distanceThreshold}m");

        // Check for Google Play Services availability (can be slow - system IPC call)
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

        // Start settings sync timer (checks every hour, syncs if 6+ hours elapsed)
        // Completely isolated - failures never affect location tracking
        StartSettingsSyncTimer();

        System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Service created");
    }

    /// <summary>
    /// Called when a component starts the service.
    /// </summary>
    /// <remarks>
    /// Note: StartForeground() is already called in OnCreate(), so we don't need to call it here.
    /// Android guarantees OnCreate() completes before OnStartCommand() is called.
    /// </remarks>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action ?? ActionStart;

        System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] OnStartCommand: {action}");

        // CRITICAL: If started via startForegroundService(), we MUST call startForeground()
        // even if already in foreground. This handles:
        // 1. App calling StartAsync() when service is already running
        // 2. Android restarting a sticky service after it was killed
        // 3. BootReceiver starting the service
        if (action == ActionStart && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            try
            {
                var notification = CreateNotification(GetCurrentNotificationText());
                StartForeground(NotificationId, notification);
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] StartForeground called in OnStartCommand");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Error in OnStartCommand StartForeground: {ex.Message}");
            }
        }

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
    /// <remarks>
    /// We must call StopForeground() before the service is destroyed to properly
    /// signal to Android that we're done with foreground mode. This prevents
    /// Android from throwing RemoteServiceException if the service is unexpectedly
    /// destroyed while Android is still tracking the foreground state.
    /// </remarks>
    public override void OnDestroy()
    {
        System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Service destroying - stopping foreground");

        // Stop settings sync timer safely
        StopSettingsSyncTimer();

        StopLocationUpdates();

        // CRITICAL: Stop foreground mode before destruction to clear Android's tracking state.
        // Use RemoveNotification flag to remove the notification.
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
                #pragma warning disable CA1422 // Validate platform compatibility
                StopForeground(true);
                #pragma warning restore CA1422
            }
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Foreground stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationTrackingService] Error stopping foreground: {ex.Message}");
        }

        _currentState = TrackingState.NotInitialized;
        SendStateChangeBroadcast();

        System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Service destroyed");

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
    /// <remarks>
    /// Note: StartForeground() is already called in OnCreate(), so we only need to
    /// update the notification text here. The service is already in foreground mode.
    /// </remarks>
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

            // Update notification to show we're starting (foreground already established in OnCreate)
            UpdateNotification("Starting...");

            // Start location updates
            StartLocationUpdates();

            _currentState = TrackingState.Active;
            SendStateChangeBroadcast();
            var initialStatus = _timelineTrackingEnabled ? "Timeline: ON" : "Timeline: OFF";
            UpdateNotification($"{initialStatus} • Acquiring GPS...");

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
            var resumeStatus = _timelineTrackingEnabled ? "Timeline: ON" : "Timeline: OFF";
            UpdateNotification($"{resumeStatus} • Resuming...");

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

        // Persist for instant availability on app start (works even when app UI is closed)
        PersistLastLocation(locationData);

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

        // Update notification with useful info
        var timelineStatus = _timelineTrackingEnabled ? "Timeline: ON" : "Timeline: OFF";
        var accuracyText = locationData.Accuracy.HasValue ? $"±{locationData.Accuracy:F0}m" : "";
        UpdateNotification($"{timelineStatus} {accuracyText}".Trim());

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

    /// <summary>
    /// Persists location to preferences for instant availability on app start.
    /// Uses same key as LocationBridge so they share the persisted location.
    /// </summary>
    private static void PersistLastLocation(LocationData location)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(location);
            Preferences.Set("last_known_location", json);
        }
        catch
        {
            // Non-critical, ignore persistence errors
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
    /// Tapping the notification opens the app. Only action is Check In.
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

        var builder = new NotificationCompat.Builder(this, ChannelId);
        builder.SetContentTitle("WayfarerMobile");
        builder.SetContentText(text);
        builder.SetSmallIcon(Resource.Drawable.ic_notification);
        builder.SetOngoing(true);
        builder.SetContentIntent(openAppPendingIntent);
        builder.AddAction(0, "Check In", checkInPendingIntent);
        builder.SetPriority(NotificationCompat.PriorityLow);

        return builder.Build()!;
    }

    /// <summary>
    /// Gets the current notification text based on state.
    /// </summary>
    private string GetCurrentNotificationText()
    {
        return _currentState switch
        {
            TrackingState.Active => _timelineTrackingEnabled ? "Timeline: ON" : "Timeline: OFF",
            TrackingState.Paused => "Tracking paused",
            TrackingState.Ready => "Initializing...",
            _ => "Location tracking"
        };
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

    #region Settings Sync (Isolated - Never Affects Location Tracking)

    /// <summary>
    /// Response model for settings from server API.
    /// </summary>
    private class ServerSettingsResponse
    {
        [JsonPropertyName("location_time_threshold_minutes")]
        public int LocationTimeThresholdMinutes { get; set; }

        [JsonPropertyName("location_distance_threshold_meters")]
        public int LocationDistanceThresholdMeters { get; set; }
    }

    /// <summary>
    /// Starts the settings sync timer. Checks every hour, syncs if 6+ hours elapsed.
    /// </summary>
    private void StartSettingsSyncTimer()
    {
        try
        {
            // Timer fires after 5 minutes initially (give service time to stabilize),
            // then every hour to check if sync is due
            _settingsSyncTimer = new Timer(
                OnSettingsSyncTimerElapsed,
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMilliseconds(SettingsSyncCheckIntervalMs));

            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync timer started");
        }
        catch (Exception ex)
        {
            // Non-critical - location tracking continues without settings sync
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Failed to start settings sync timer: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the settings sync timer safely.
    /// </summary>
    private void StopSettingsSyncTimer()
    {
        try
        {
            _settingsSyncTimer?.Dispose();
            _settingsSyncTimer = null;
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync timer stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Error stopping settings sync timer: {ex.Message}");
        }
    }

    /// <summary>
    /// Timer callback - checks if sync is due and triggers if needed.
    /// Completely isolated - any failure is caught and logged, never propagates.
    /// </summary>
    private void OnSettingsSyncTimerElapsed(object? state)
    {
        // CRITICAL: Everything in try-catch - timer callbacks must never throw
        try
        {
            // Skip if already syncing
            if (_settingsSyncInProgress)
            {
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync already in progress, skipping");
                return;
            }

            // Check if sync is due
            if (!IsSettingsSyncDue())
            {
                return;
            }

            // Fire and forget - completely isolated async operation
            _ = ExecuteSettingsSyncSafelyAsync();
        }
        catch (Exception ex)
        {
            // NEVER let timer callback crash the service
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Settings sync timer error (safely caught): {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if settings sync is due based on last sync time.
    /// </summary>
    private static bool IsSettingsSyncDue()
    {
        try
        {
            var lastSyncTicks = Preferences.Get(LastSettingsSyncKey, 0L);
            if (lastSyncTicks == 0)
                return true; // Never synced from foreground service

            var lastSync = new DateTime(lastSyncTicks, DateTimeKind.Utc);
            var timeSinceSync = DateTime.UtcNow - lastSync;

            return timeSinceSync >= SettingsSyncInterval;
        }
        catch
        {
            return false; // If we can't read preferences, don't sync
        }
    }

    /// <summary>
    /// Executes settings sync with comprehensive error handling and timeout protection.
    /// This method is completely isolated - any failure is caught and logged.
    /// </summary>
    private async Task ExecuteSettingsSyncSafelyAsync()
    {
        // Mark as in progress
        _settingsSyncInProgress = true;

        try
        {
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Starting settings sync from foreground service");

            // Validate prerequisites
            var serverUrl = Preferences.Get("server_url", string.Empty);
            var apiToken = Preferences.Get("api_token", string.Empty);

            if (string.IsNullOrEmpty(serverUrl))
            {
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync skipped - no server URL configured");
                return;
            }

            // Create HTTP client with strict timeout
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(SettingsSyncTimeoutSeconds)
            };

            // Build request
            var requestUrl = $"{serverUrl.TrimEnd('/')}/api/settings";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            if (!string.IsNullOrEmpty(apiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            }

            // Execute with cancellation token for additional safety
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(SettingsSyncTimeoutSeconds));

            var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LocationTrackingService] Settings sync failed: HTTP {(int)response.StatusCode}");
                return;
            }

            // Parse response
            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<ServerSettingsResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (settings == null)
            {
                System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync failed: null response");
                return;
            }

            // Validate values before applying
            if (settings.LocationTimeThresholdMinutes <= 0 || settings.LocationDistanceThresholdMeters <= 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[LocationTrackingService] Settings sync: invalid values (time={settings.LocationTimeThresholdMinutes}, dist={settings.LocationDistanceThresholdMeters})");
                return;
            }

            // Apply settings
            ApplySettingsSafely(settings);

            // Record successful sync time
            Preferences.Set(LastSettingsSyncKey, DateTime.UtcNow.Ticks);

            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Settings synced: {settings.LocationTimeThresholdMinutes}min / {settings.LocationDistanceThresholdMeters}m");
        }
        catch (TaskCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[LocationTrackingService] Settings sync timed out");
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Settings sync network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Settings sync JSON error: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Catch-all for any unexpected errors
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Settings sync unexpected error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _settingsSyncInProgress = false;
        }
    }

    /// <summary>
    /// Applies synced settings safely to preferences and threshold filter.
    /// </summary>
    private void ApplySettingsSafely(ServerSettingsResponse settings)
    {
        try
        {
            // Update preferences (used by SettingsService and service restart)
            Preferences.Set("location_time_threshold", settings.LocationTimeThresholdMinutes);
            Preferences.Set("location_distance_threshold", settings.LocationDistanceThresholdMeters);

            // Update live threshold filter
            _thresholdFilter?.UpdateThresholds(
                settings.LocationTimeThresholdMinutes,
                settings.LocationDistanceThresholdMeters);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Error applying settings: {ex.Message}");
        }
    }

    #endregion
}
