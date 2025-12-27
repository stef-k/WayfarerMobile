using System.Text.Json;
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
    // High: 1s for real-time map updates when app visible
    // Normal: 95s for background - balances battery vs server's 2-min threshold
    // PowerSaver: 5min for critical battery situations
    private const long HighPerformanceIntervalMs = 1000;
    private const long NormalIntervalMs = 95000;
    private const long PowerSaverIntervalMs = 300000;

    // Location filtering
    private const float MinAccuracyMeters = 100f;

    // Accuracy threshold for considering a location a "real GPS fix"
    // WiFi/Cell typically gives 50-100m, GPS gives < 20m
    private const float GpsFixAccuracyThreshold = 50f;

    // Maximum time to stay in HighAccuracy mode waiting for GPS fix (seconds)
    // After this timeout, accept best available location and switch to Balanced
    // Prevents infinite HighAccuracy mode when GPS is unavailable (e.g., indoors)
    private const int HighAccuracyTimeoutSeconds = 120;

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

    // Hybrid GPS priority for Normal mode: tracks when we last used high accuracy
    // to ensure we get precise GPS fixes before sync while saving battery most of the time
    private DateTime _lastHighAccuracyTime = DateTime.MinValue;
    private DateTime _highAccuracyStartTime = DateTime.MinValue;
    private bool _currentlyUsingHighAccuracy;

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
    /// <remarks>
    /// Uses hybrid priority for Normal mode:
    /// - Most of the time: Balanced (WiFi/Cell) to save battery
    /// - Periodically: High Accuracy (GPS) to get precise fix before sync
    /// </remarks>
    private void StartFusedLocationUpdates()
    {
        if (_fusedClient == null || _fusedCallback == null)
            return;

        var interval = GetCurrentInterval();
        var priority = GetCurrentPriority();
        var wasUsingHighAccuracy = _currentlyUsingHighAccuracy;
        _currentlyUsingHighAccuracy = priority == Priority.PriorityHighAccuracy;

        // Track when we TRANSITION to HighAccuracy (for timeout purposes)
        // Don't reset if we're restarting while already in HighAccuracy
        if (_currentlyUsingHighAccuracy && !wasUsingHighAccuracy)
        {
            _highAccuracyStartTime = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine(
                "[LocationTrackingService] Entered HighAccuracy mode, starting timeout timer");
        }

        var request = new global::Android.Gms.Location.LocationRequest.Builder(priority, interval)
            .SetMinUpdateIntervalMillis(interval / 2)
            .SetMaxUpdateDelayMillis(interval * 2)
            .Build();

        _fusedClient.RequestLocationUpdates(request, _fusedCallback, Looper.MainLooper);

        var priorityName = priority == Priority.PriorityHighAccuracy ? "HighAccuracy" : "Balanced";
        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Fused location updates started (interval: {interval}ms, priority: {priorityName})");
    }

    /// <summary>
    /// Gets the current GPS priority based on performance mode.
    /// </summary>
    /// <remarks>
    /// Priority strategy:
    /// - HighPerformance: Always high accuracy (GPS active, real-time updates)
    /// - Normal: Hybrid - balanced most of the time, high accuracy periodically
    /// - PowerSaver: Always balanced (WiFi/Cell, minimal GPS)
    /// </remarks>
    private int GetCurrentPriority()
    {
        return _performanceMode switch
        {
            PerformanceMode.HighPerformance => Priority.PriorityHighAccuracy,
            PerformanceMode.PowerSaver => Priority.PriorityBalancedPowerAccuracy,
            _ => GetNormalModePriority()
        };
    }

    /// <summary>
    /// Determines priority for Normal mode using hybrid approach.
    /// Uses high accuracy when approaching the sync threshold to ensure
    /// precise GPS fix before data syncs to server.
    /// </summary>
    /// <remarks>
    /// The time threshold is server-configured (default 5 min, can be as low as 2 min).
    /// Hybrid approach only makes sense when threshold > 1.5x interval, otherwise
    /// we use high accuracy for every request to guarantee GPS fix per sync period.
    /// </remarks>
    private int GetNormalModePriority()
    {
        // Get the time threshold from settings (server-configured, default 5 minutes)
        var timeThresholdMinutes = Preferences.Get("location_time_threshold", 5);
        var thresholdSeconds = timeThresholdMinutes * 60;
        var intervalSeconds = NormalIntervalMs / 1000; // 95 seconds

        // If threshold is too short for hybrid approach (less than 1.5x interval),
        // always use high accuracy to guarantee GPS fix per sync period
        if (thresholdSeconds < intervalSeconds * 1.5)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Normal mode: using HighAccuracy (threshold {thresholdSeconds}s too short for hybrid)");
            return Priority.PriorityHighAccuracy;
        }

        // Calculate time since last high accuracy request
        var timeSinceHighAccuracy = DateTime.UtcNow - _lastHighAccuracyTime;

        // Use high accuracy when within one interval of threshold
        // This ensures we get at least one GPS fix per sync period
        var bufferSeconds = intervalSeconds;
        var highAccuracyNeeded = timeSinceHighAccuracy.TotalSeconds >= (thresholdSeconds - bufferSeconds);

        if (highAccuracyNeeded)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LocationTrackingService] Normal mode: using HighAccuracy (last GPS fix: {timeSinceHighAccuracy.TotalSeconds:F0}s ago, threshold: {thresholdSeconds}s)");
            return Priority.PriorityHighAccuracy;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[LocationTrackingService] Normal mode: using Balanced (last GPS fix: {timeSinceHighAccuracy.TotalSeconds:F0}s ago)");
        return Priority.PriorityBalancedPowerAccuracy;
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

        // In Normal mode with hybrid priority: check if we need to switch priority
        // - After HighAccuracy fix → switch to Balanced (save battery)
        // - When approaching threshold → switch to HighAccuracy (get GPS fix)
        CheckAndAdjustPriority(locationData);
    }

    /// <summary>
    /// Checks if we should switch GPS priority based on current state.
    /// Implements the hybrid priority cycling for Normal mode.
    /// </summary>
    /// <param name="location">The location that was just received.</param>
    private void CheckAndAdjustPriority(LocationData location)
    {
        // Only applies to Normal mode with FusedLocationProvider
        // Fallback LocationManager doesn't support dynamic priority switching
        if (_performanceMode != PerformanceMode.Normal || !_hasPlayServices)
            return;

        // When using HighAccuracy, check for good fix OR timeout
        if (_currentlyUsingHighAccuracy)
        {
            var accuracy = location.Accuracy ?? float.MaxValue;
            var timeInHighAccuracy = DateTime.UtcNow - _highAccuracyStartTime;

            if (accuracy < GpsFixAccuracyThreshold)
            {
                // Good GPS fix acquired
                _lastHighAccuracyTime = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine(
                    $"[LocationTrackingService] Good GPS fix acquired ({accuracy:F0}m < {GpsFixAccuracyThreshold}m threshold)");
            }
            else if (timeInHighAccuracy.TotalSeconds >= HighAccuracyTimeoutSeconds)
            {
                // Timeout - accept what we have and move on
                // This prevents staying in HighAccuracy forever when GPS is unavailable (e.g., indoors)
                _lastHighAccuracyTime = DateTime.UtcNow;
                System.Diagnostics.Debug.WriteLine(
                    $"[LocationTrackingService] HighAccuracy timeout ({timeInHighAccuracy.TotalSeconds:F0}s >= {HighAccuracyTimeoutSeconds}s), " +
                    $"accepting {accuracy:F0}m location");
            }
            else
            {
                // Still waiting for GPS fix
                System.Diagnostics.Debug.WriteLine(
                    $"[LocationTrackingService] Waiting for GPS fix ({accuracy:F0}m >= {GpsFixAccuracyThreshold}m, " +
                    $"time in HighAccuracy: {timeInHighAccuracy.TotalSeconds:F0}s/{HighAccuracyTimeoutSeconds}s)");
            }
        }

        var shouldUseHighAccuracy = GetNormalModePriority() == Priority.PriorityHighAccuracy;

        // Case 1: Currently using HighAccuracy, got fix or timed out → switch to Balanced
        if (_currentlyUsingHighAccuracy && !shouldUseHighAccuracy)
        {
            System.Diagnostics.Debug.WriteLine(
                "[LocationTrackingService] Switching to Balanced priority");
            RestartLocationUpdates();
        }
        // Case 2: Currently using Balanced, approaching threshold → switch to HighAccuracy
        else if (!_currentlyUsingHighAccuracy && shouldUseHighAccuracy)
        {
            System.Diagnostics.Debug.WriteLine(
                "[LocationTrackingService] Approaching sync threshold, switching to HighAccuracy priority");
            RestartLocationUpdates();
        }
    }

    /// <summary>
    /// Restarts location updates with current priority settings.
    /// </summary>
    private void RestartLocationUpdates()
    {
        lock (_lock)
        {
            if (_currentState == TrackingState.Active)
            {
                StopLocationUpdates();
                StartLocationUpdates();
            }
        }
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
}
