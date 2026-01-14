using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Locations;
using Android.OS;
using Android.Runtime;
using Android.Util;
using AndroidX.Core.App;
using SQLite;
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
    // Normal: Dynamic based on threshold (sleep/wake optimization)
    // PowerSaver: 5min for critical battery situations
    private const long HighPerformanceIntervalMs = 1000;
    private const long PowerSaverIntervalMs = 300000;

    // Sleep/wake optimization intervals for Normal mode
    // Wake: 1s interval to collect many GPS samples (GPS already at max power anyway)
    // Sleep: Long interval (threshold - buffer) to save battery
    private const long WakePhaseIntervalMs = 1000;   // 1s - match HighPerformance, gather all samples
    private const long MinSleepIntervalMs = 60000;   // 60s - minimum sleep interval
    private const long StoredSampleIntervalMs = 30000; // 30s - poll for TryLog when sample stored (no GPS)
    private const int WakeBufferSeconds = 100;       // Wake up 100s before threshold (enough for cold start GPS)
    private const int StaleSampleBufferSeconds = 5;  // Extra buffer before clearing stale sample

    // Two-tier accuracy thresholds for wake phase:
    // 1. Excellent GPS (≤20m): Stop early, log immediately - no need to wait
    // 2. Timeout fallback (≤100m): At cutoff, accept best available sample
    private const float ExcellentGpsThreshold = 20f;    // Early stop - excellent GPS fix
    private const float MinAccuracyMeters = 100f;       // Timeout fallback - accept cell/WiFi if needed

    // Maximum time to stay in HighAccuracy mode waiting for GPS fix (seconds)
    // After this timeout, accept best available location and switch to Balanced
    // Prevents infinite HighAccuracy mode when GPS is unavailable (e.g., indoors)
    // With 90s wake buffer: 90s before threshold + 90s after = 180s max
    private const int HighAccuracyTimeoutSeconds = 180;

    // Android log tag for adb logcat filtering
    private const string LogTag = "WayfarerLocation";

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

    // Wake phase timing: tracks when we entered HighAccuracy mode for timeout calculation
    private DateTime _highAccuracyStartTime = DateTime.MinValue;
    private bool _currentlyUsingHighAccuracy;

    // Best location seen during wake phase (for timeout fallback)
    private Location? _bestWakePhaseLocation;
    private float _bestWakePhaseAccuracy = float.MaxValue;

    // Approach phase flag: prevents repeated RestartLocationUpdates during approach
    private bool _inApproachPhase;

    // Stationary user cooldown: prevents oscillation between HighAccuracy and Balanced
    // When timeout triggers without a successful log (stationary user), we enter cooldown
    // to stay in Balanced for a full threshold period before allowing HighAccuracy again
    private DateTime _stationaryCooldownUntil = DateTime.MinValue;

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
            Log.Info(LogTag, "OnCreate starting - initiating foreground");

            _notificationManager = (NotificationManager?)GetSystemService(NotificationService);
            if (_notificationManager == null)
            {
                Log.Warn(LogTag, "NotificationManager is null!");
            }

            CreateNotificationChannel();
            Log.Debug(LogTag, "Notification channel created");

            // CRITICAL: Start foreground IMMEDIATELY to satisfy Android's 5-second requirement.
            var notification = CreateNotification("Initializing...");
            Log.Debug(LogTag, "Notification created, calling StartForeground");

            StartForeground(NotificationId, notification);
            _currentState = TrackingState.Ready;
            SendStateChangeBroadcast();
            Log.Info(LogTag, "Foreground started successfully in OnCreate");
        }
        catch (Java.Lang.SecurityException ex)
        {
            Log.Error(LogTag, $"Security error in Phase 1: {ex.Message}");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(LogTag, $"Invalid operation in Phase 1: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"CRITICAL ERROR in Phase 1: {ex.Message}");
            Log.Error(LogTag, $"Stack trace: {ex.StackTrace}");
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
        Log.Debug(LogTag, $"TimelineTrackingEnabled: {_timelineTrackingEnabled}");

        // Load server thresholds for location filtering (respects server configuration)
        // Defaults match SettingsService: 5 min / 15 m / 50 m accuracy
        var timeThreshold = Preferences.Get("location_time_threshold", 5);
        var distanceThreshold = Preferences.Get("location_distance_threshold", 15);
        var accuracyThreshold = Preferences.Get("location_accuracy_threshold", 50);
        _thresholdFilter.UpdateThresholds(timeThreshold, distanceThreshold, accuracyThreshold);
        Log.Debug(LogTag, $"Thresholds: {timeThreshold}min / {distanceThreshold}m / {accuracyThreshold}m accuracy");

        // Subscribe to threshold updates from server sync
        LocationServiceCallbacks.ThresholdsUpdated += OnThresholdsUpdated;

        // Check for Google Play Services availability (can be slow - system IPC call)
        _hasPlayServices = GoogleApiAvailability.Instance
            .IsGooglePlayServicesAvailable(this) == ConnectionResult.Success;

        if (_hasPlayServices)
        {
            // Use Google's FusedLocationProvider - better accuracy through sensor fusion
            _fusedClient = LocationServices.GetFusedLocationProviderClient(this);
            _fusedCallback = new FusedLocationCallback(this);
            Log.Info(LogTag, "Using Google Play Services FusedLocationProvider");
        }
        else
        {
            // Fallback to standard LocationManager for devices without Play Services
            _locationManager = (LocationManager?)GetSystemService(LocationService);
            Log.Info(LogTag, "Using fallback LocationManager (no Play Services)");
        }

        Log.Info(LogTag, "Service created");
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

        Log.Debug(LogTag, $"OnStartCommand: {action}");

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
                Log.Info(LogTag, "StartForeground called in OnStartCommand");
            }
            catch (Java.Lang.SecurityException ex)
            {
                Log.Error(LogTag, $"Security error in OnStartCommand StartForeground: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                Log.Error(LogTag, $"Invalid operation in OnStartCommand StartForeground: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error(LogTag, $"Error in OnStartCommand StartForeground: {ex.Message}");
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
        Log.Info(LogTag, "Service destroying - stopping foreground");

        // Unsubscribe from static events to prevent memory leaks and duplicate handlers
        LocationServiceCallbacks.ThresholdsUpdated -= OnThresholdsUpdated;

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
            Log.Info(LogTag, "Foreground stopped");
        }
        catch (Java.Lang.IllegalStateException ex)
        {
            Log.Warn(LogTag, $"Service not in foreground state: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Error stopping foreground: {ex.Message}");
        }

        _currentState = TrackingState.NotInitialized;
        SendStateChangeBroadcast();

        Log.Info(LogTag, "Service destroyed");

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
                Log.Debug(LogTag, "Already tracking, broadcasting current state for new clients");
                SendStateChangeBroadcast();
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

            Log.Info(LogTag, "Tracking started");
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

            Log.Info(LogTag, "Tracking stopped");
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

            Log.Info(LogTag, "Tracking paused");
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

            Log.Info(LogTag, "Tracking resumed");
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
            Log.Debug(LogTag, $"Performance mode: {mode}");

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
            Log.Error(LogTag, $"Permission denied: {ex.Message}");
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

        // IMPORTANT: Order matters! Priority determines _currentlyUsingHighAccuracy,
        // which affects interval calculation. So: priority → state → interval
        var priority = GetCurrentPriority();
        var wasUsingHighAccuracy = _currentlyUsingHighAccuracy;

        // Only track high accuracy state for Normal mode with sleep/wake optimization
        // For short thresholds (≤ 2 min), we're always in HighAccuracy but don't use
        // wake phase logic (timeout, best location tracking) since there's no sleep phase
        var timeThresholdMinutes = Preferences.Get("location_time_threshold", 5);
        var usingSleepWakeOptimization = timeThresholdMinutes * 60 > 120;

        _currentlyUsingHighAccuracy = _performanceMode == PerformanceMode.Normal
            && priority == Priority.PriorityHighAccuracy
            && usingSleepWakeOptimization;

        // Now calculate interval (depends on updated _currentlyUsingHighAccuracy)
        var interval = GetCurrentInterval();

        // Track when we TRANSITION to HighAccuracy (for timeout purposes)
        // Don't reset if we're restarting while already in HighAccuracy
        if (_currentlyUsingHighAccuracy && !wasUsingHighAccuracy)
        {
            _highAccuracyStartTime = DateTime.UtcNow;
            _bestWakePhaseLocation = null;
            _bestWakePhaseAccuracy = float.MaxValue;
            Log.Info(LogTag, "Entered HighAccuracy mode, starting timeout timer");
        }

        var request = new global::Android.Gms.Location.LocationRequest.Builder(priority, interval)
            .SetMinUpdateIntervalMillis(interval / 2)
            .SetMaxUpdateDelayMillis(interval * 2)
            .Build();

        _fusedClient.RequestLocationUpdates(request, _fusedCallback, Looper.MainLooper);

        var priorityName = priority == Priority.PriorityHighAccuracy ? "HighAccuracy (wake)" : "Balanced (sleep)";
        var intervalSec = interval / 1000;
        Log.Info(LogTag, $"Mode: {priorityName}, interval: {intervalSec}s");
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
    /// Determines priority for Normal mode using sleep/wake approach.
    /// Uses high accuracy when approaching the sync threshold to ensure
    /// precise GPS fix before data syncs to server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses ThresholdFilter as single source of truth for timing.
    /// This ensures wake/sleep cycle is synchronized with actual logging.
    /// </para>
    /// <para>
    /// The time threshold is server-configured (default 5 min, can be as low as 2 min).
    /// For short thresholds (≤ 2 min), always use high accuracy.
    /// For longer thresholds, sleep in Balanced mode and wake to HighAccuracy before sync.
    /// </para>
    /// </remarks>
    private int GetNormalModePriority()
    {
        // Get the time threshold from settings (server-configured, default 5 minutes)
        var timeThresholdMinutes = Preferences.Get("location_time_threshold", 5);
        var thresholdSeconds = timeThresholdMinutes * 60;

        // Safety check: clear stale stored samples to prevent getting stuck
        // Max age = threshold + timeout + buffer (no reason to overlap into next cycle)
        if (_bestWakePhaseLocation != null)
        {
            var sampleAge = (DateTime.UtcNow - _highAccuracyStartTime).TotalSeconds;
            var maxSampleAge = thresholdSeconds + HighAccuracyTimeoutSeconds + StaleSampleBufferSeconds;
            if (sampleAge > maxSampleAge)
            {
                Log.Warn(LogTag, $"Clearing stale stored sample (age: {sampleAge:F0}s > max: {maxSampleAge}s)");
                _bestWakePhaseLocation = null;
                _bestWakePhaseAccuracy = float.MaxValue;
            }
        }

        // If we have an excellent stored sample, stay in Balanced
        // GPS work is done, just waiting for TryLog() at threshold time
        if (_bestWakePhaseLocation != null && _bestWakePhaseAccuracy <= ExcellentGpsThreshold)
        {
            return Priority.PriorityBalancedPowerAccuracy;
        }

        // For short thresholds (≤ 2 min), always use high accuracy
        // Sleep/wake optimization doesn't make sense for very short thresholds
        if (thresholdSeconds <= 120)
        {
            return Priority.PriorityHighAccuracy;
        }

        // BATTERY FIX: Prevent indefinite HighAccuracy for stationary users
        // With AND logic (time + distance), a stationary user will never satisfy distance,
        // so GetSecondsUntilNextLog() keeps returning <= 0. Without this check, they'd
        // stay in HighAccuracy forever, draining battery.
        //
        // Solution: When timeout triggers without a log, enter a cooldown period where
        // we stay in Balanced for a full threshold period. This prevents oscillation
        // between HighAccuracy and Balanced (180s HA → brief Balanced → 180s HA → ...).

        // Check if we're still in cooldown from a previous stationary timeout
        if (DateTime.UtcNow < _stationaryCooldownUntil)
        {
            var cooldownRemaining = (_stationaryCooldownUntil - DateTime.UtcNow).TotalSeconds;
            Log.Debug(LogTag, $"In stationary cooldown, {cooldownRemaining:F0}s remaining");
            return Priority.PriorityBalancedPowerAccuracy;
        }

        // Check if currently in HighAccuracy and timeout has expired
        if (_currentlyUsingHighAccuracy)
        {
            var timeInHighAccuracy = (DateTime.UtcNow - _highAccuracyStartTime).TotalSeconds;
            if (timeInHighAccuracy >= HighAccuracyTimeoutSeconds)
            {
                // Enter cooldown: stay in Balanced for a full threshold period
                // This ensures the device sleeps properly before the next wake cycle
                _stationaryCooldownUntil = DateTime.UtcNow.AddSeconds(thresholdSeconds);
                Log.Info(LogTag, $"HighAccuracy timeout ({timeInHighAccuracy:F0}s), entering {thresholdSeconds}s cooldown");
                return Priority.PriorityBalancedPowerAccuracy;
            }
        }

        // Query ThresholdFilter for time until next log (single source of truth)
        var secondsUntilNextLog = _thresholdFilter?.GetSecondsUntilNextLog();

        // If no previous log (first run) or log is due/overdue, use high accuracy
        if (secondsUntilNextLog == null || secondsUntilNextLog <= 0)
        {
            return Priority.PriorityHighAccuracy;
        }

        // Wake up WakeBufferSeconds before the next log is due
        if (secondsUntilNextLog <= WakeBufferSeconds)
        {
            return Priority.PriorityHighAccuracy;
        }

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

        Log.Info(LogTag, $"Fallback location updates started (interval: {interval}ms)");
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
                Log.Info(LogTag, "Fused location updates stopped");
            }
            else if (_locationManager != null)
            {
                _locationManager.RemoveUpdates(this);
                Log.Info(LogTag, "Fallback location updates stopped");
            }
        }
        catch (Java.Lang.IllegalArgumentException ex)
        {
            Log.Warn(LogTag, $"Location listener not registered: {ex.Message}");
        }
        catch (Java.Lang.SecurityException ex)
        {
            Log.Error(LogTag, $"Security error stopping updates: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Error stopping updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the current polling interval based on performance mode.
    /// For Normal mode, uses sleep/wake optimization:
    /// - Wake phase (HighAccuracy): Short interval for quick GPS acquisition
    /// - Sleep phase (Balanced): Long interval based on threshold to save battery
    /// </summary>
    private long GetCurrentInterval()
    {
        return _performanceMode switch
        {
            PerformanceMode.HighPerformance => HighPerformanceIntervalMs,
            PerformanceMode.PowerSaver => PowerSaverIntervalMs,
            _ => GetNormalModeInterval()
        };
    }

    /// <summary>
    /// Gets the interval for Normal mode based on current priority phase.
    /// Wake phase uses short interval, sleep phase uses time until next log.
    /// </summary>
    /// <remarks>
    /// Uses ThresholdFilter as single source of truth for timing.
    /// Sleep interval is calculated to wake up exactly WakeBufferSeconds before next log.
    /// </remarks>
    private long GetNormalModeInterval()
    {
        // Get threshold from settings
        var timeThresholdMinutes = Preferences.Get("location_time_threshold", 5);
        var thresholdSeconds = timeThresholdMinutes * 60;

        // If we have an excellent stored sample (and it's not stale), use moderate interval
        // GPS work is done, just waiting for TryLog() - poll periodically to catch threshold
        // Note: stale sample check is done in GetNormalModePriority() which is called first
        if (_bestWakePhaseLocation != null && _bestWakePhaseAccuracy <= ExcellentGpsThreshold)
        {
            return StoredSampleIntervalMs;
        }

        // For short thresholds (≤ 2 min), use fixed short interval
        // Sleep/wake optimization doesn't make sense for very short thresholds
        if (thresholdSeconds <= 120)
        {
            return WakePhaseIntervalMs; // 1s - always quick polling
        }

        // Wake phase (HighAccuracy): Use short interval for quick GPS acquisition
        if (_currentlyUsingHighAccuracy)
        {
            return WakePhaseIntervalMs; // 1s
        }

        // Sleep phase: Calculate interval based on time until next log
        var secondsUntilNextLog = _thresholdFilter?.GetSecondsUntilNextLog();

        // If no data or log is due, use short interval to get a location quickly
        if (secondsUntilNextLog == null || secondsUntilNextLog <= WakeBufferSeconds)
        {
            return WakePhaseIntervalMs;
        }

        // Two-phase sleep for deterministic wake timing:
        // 1. Far from threshold (> 2x buffer): Use long interval
        // 2. Approaching threshold (buffer to 2x buffer): Use short interval to catch wake window
        if (secondsUntilNextLog.Value <= WakeBufferSeconds * 2)
        {
            // Approaching wake window - use short interval to ensure we catch it
            return WakePhaseIntervalMs; // 30s
        }

        // Far from threshold - sleep until we're within 2x buffer
        var sleepIntervalMs = (long)((secondsUntilNextLog.Value - WakeBufferSeconds * 2) * 1000);

        // Ensure minimum sleep interval
        return Math.Max(sleepIntervalMs, MinSleepIntervalMs);
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
        var accuracy = location.HasAccuracy ? location.Accuracy : float.MaxValue;

        // During wake phase, track the best location we've seen (even if rejected)
        // This is used as fallback when timeout expires
        if (_currentlyUsingHighAccuracy && _performanceMode == PerformanceMode.Normal)
        {
            if (accuracy < _bestWakePhaseAccuracy)
            {
                _bestWakePhaseLocation = location;
                _bestWakePhaseAccuracy = accuracy;
                Log.Debug(LogTag, $"Best wake location updated: {accuracy:F0}m");
            }
        }

        // Check if we're in wake phase and timeout has expired
        var timeoutExpired = false;
        if (_currentlyUsingHighAccuracy && _performanceMode == PerformanceMode.Normal)
        {
            var timeInHighAccuracy = DateTime.UtcNow - _highAccuracyStartTime;
            if (timeInHighAccuracy.TotalSeconds >= HighAccuracyTimeoutSeconds)
            {
                timeoutExpired = true;
                // Use best location we collected, not necessarily current one
                if (_bestWakePhaseLocation != null && _bestWakePhaseAccuracy < accuracy)
                {
                    location = _bestWakePhaseLocation;
                    accuracy = _bestWakePhaseAccuracy;
                    Log.Warn(LogTag, $"Timeout! Using best collected: {accuracy:F0}m");
                }
                else
                {
                    Log.Warn(LogTag, $"Timeout! Using current: {accuracy:F0}m");
                }
            }
        }

        // Accuracy filtering for wake phase:
        // 1. Excellent GPS (≤20m): Store it, switch to Balanced (GPS off), wait for TryLog
        // 2. Threshold passed: Proceed to TryLog with best available (any accuracy)
        // 3. Otherwise: Keep collecting samples until threshold or timeout
        var shouldSwitchToBalanced = false;
        if (_currentlyUsingHighAccuracy && _performanceMode == PerformanceMode.Normal && !timeoutExpired)
        {
            var secondsUntilLog = _thresholdFilter?.GetSecondsUntilNextLog() ?? 0;

            if (accuracy <= ExcellentGpsThreshold)
            {
                // Excellent GPS found - trigger early GPS shutoff
                Log.Info(LogTag, $"Excellent GPS: {accuracy:F0}m, switching to Balanced (early stop)");
                shouldSwitchToBalanced = true;
            }
            else if (secondsUntilLog <= 0)
            {
                // Threshold passed - proceed to TryLog with whatever accuracy we have
                Log.Info(LogTag, $"Threshold passed with {accuracy:F0}m GPS, proceeding to log");
            }
            else
            {
                // Threshold not reached - keep collecting for better GPS
                Log.Debug(LogTag, $"Wake phase: {accuracy:F0}m, waiting for better ({secondsUntilLog:F0}s to threshold)");
                return;
            }
        }
        else if (!_currentlyUsingHighAccuracy && _performanceMode == PerformanceMode.Normal)
        {
            // In Balanced mode (after early stop or during sleep)
            // Proceed to TryLog which will use stored best sample if available
            if (accuracy > MinAccuracyMeters)
            {
                Log.Debug(LogTag, $"Balanced mode: {accuracy:F0}m, will use stored sample if available");
            }
        }
        else
        {
            // Timeout expired or other modes - proceed to TryLog
            // At timeout, we must log whatever we have (any accuracy)
            if (accuracy > MinAccuracyMeters)
            {
                Log.Info(LogTag, $"Timeout/fallback: proceeding with {accuracy:F0}m accuracy");
            }
            // Don't return - always proceed to TryLog
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
            // Always prefer stored best sample over current location
            // This handles both: wake phase logging AND post-early-stop logging from Balanced mode
            var locationToLog = locationData;
            if (_bestWakePhaseLocation != null && _bestWakePhaseAccuracy < (locationData.Accuracy ?? float.MaxValue))
            {
                locationToLog = ConvertToLocationData(_bestWakePhaseLocation);
                // Use current time for threshold check - sample accuracy is from earlier, but we're logging NOW
                // Without this, ThresholdFilter compares sample's capture time vs last log, which never advances
                locationToLog.Timestamp = DateTime.UtcNow;
                Log.Info(LogTag, $"Using stored best sample: {_bestWakePhaseAccuracy:F0}m vs current {locationData.Accuracy:F0}m");
            }

            if (_thresholdFilter.TryLog(locationToLog))
            {
                LogLocationToQueue(locationToLog);
                Log.Info(LogTag, $"LOGGED: {locationToLog.Accuracy:F0}m accuracy");

                // Clear stored sample after successful log
                _bestWakePhaseLocation = null;
                _bestWakePhaseAccuracy = float.MaxValue;

                // Clear stationary cooldown - user is moving, allow normal wake/sleep
                if (_stationaryCooldownUntil > DateTime.MinValue)
                {
                    Log.Info(LogTag, "Cleared stationary cooldown - user is moving");
                    _stationaryCooldownUntil = DateTime.MinValue;
                }
            }
        }

        // Update notification with useful info
        var timelineStatus = _timelineTrackingEnabled ? "Timeline: ON" : "Timeline: OFF";
        var accuracyText = locationData.Accuracy.HasValue ? $"±{locationData.Accuracy:F0}m" : "";
        UpdateNotification($"{timelineStatus} {accuracyText}".Trim());

        Log.Info(LogTag, $"Location: {locationData.Latitude:F6}, {locationData.Longitude:F6} ({locationData.Accuracy:F0}m)");

        // Early GPS shutoff: excellent sample found, switch to Balanced immediately
        if (shouldSwitchToBalanced)
        {
            Log.Info(LogTag, "Early stop: Switching to Balanced, stored sample will be used at threshold time");
            _currentlyUsingHighAccuracy = false;
            RestartLocationUpdates();
            return;
        }

        // In Normal mode with hybrid priority: check if we need to switch priority
        // - After HighAccuracy fix → switch to Balanced (save battery)
        // - When approaching threshold → switch to HighAccuracy (get GPS fix)
        CheckAndAdjustPriority(locationData);
    }

    /// <summary>
    /// Checks if we should switch GPS priority based on current state.
    /// Implements the hybrid priority cycling for Normal mode.
    /// </summary>
    /// <remarks>
    /// Priority switching is now driven by ThresholdFilter timing:
    /// - Wake (HighAccuracy): When within WakeBufferSeconds of next log
    /// - Sleep (Balanced): After logging, when next log is far away
    /// The switch to sleep happens automatically after logging because
    /// GetSecondsUntilNextLog() returns a fresh threshold value.
    /// </remarks>
    /// <param name="location">The location that was just received.</param>
    private void CheckAndAdjustPriority(LocationData location)
    {
        // Only applies to Normal mode with FusedLocationProvider
        // Fallback LocationManager doesn't support dynamic priority switching
        if (_performanceMode != PerformanceMode.Normal || !_hasPlayServices)
            return;

        var secondsUntilLog = _thresholdFilter?.GetSecondsUntilNextLog() ?? 0;

        // Log wake phase status for debugging
        if (_currentlyUsingHighAccuracy)
        {
            var accuracy = location.Accuracy ?? float.MaxValue;
            var timeInHighAccuracy = DateTime.UtcNow - _highAccuracyStartTime;

            if (accuracy <= ExcellentGpsThreshold)
            {
                Log.Info(LogTag, $"Excellent GPS ({accuracy:F0}m), log in {secondsUntilLog:F0}s");
            }
            else if (timeInHighAccuracy.TotalSeconds >= HighAccuracyTimeoutSeconds)
            {
                Log.Warn(LogTag, $"Timeout ({timeInHighAccuracy.TotalSeconds:F0}s), best: {_bestWakePhaseAccuracy:F0}m");
            }
            else
            {
                Log.Info(LogTag, $"Waiting GPS ({accuracy:F0}m, {timeInHighAccuracy.TotalSeconds:F0}s/{HighAccuracyTimeoutSeconds}s, log in {secondsUntilLog:F0}s)");
            }
        }

        var shouldUseHighAccuracy = GetNormalModePriority() == Priority.PriorityHighAccuracy;

        // Case 1: Currently using HighAccuracy, threshold passed and logged → switch to Balanced
        if (_currentlyUsingHighAccuracy && !shouldUseHighAccuracy)
        {
            Log.Info(LogTag, "Logged, switching to Balanced (sleep)");
            _inApproachPhase = false; // Reset for next cycle
            RestartLocationUpdates();
        }
        // Case 2: Currently using Balanced, approaching threshold → switch to HighAccuracy
        else if (!_currentlyUsingHighAccuracy && shouldUseHighAccuracy)
        {
            Log.Info(LogTag, "Approaching threshold, switching to HighAccuracy (wake)");
            _inApproachPhase = false; // Exiting approach phase into wake
            RestartLocationUpdates();
        }
        // Case 3: In Balanced but entering "approach" phase (2x buffer) → restart for shorter interval (once)
        else if (!_currentlyUsingHighAccuracy && !_inApproachPhase &&
                 secondsUntilLog <= WakeBufferSeconds * 2 && secondsUntilLog > WakeBufferSeconds)
        {
            Log.Info(LogTag, $"Entering approach phase ({secondsUntilLog:F0}s to log), shortening interval");
            _inApproachPhase = true; // Mark as entered, don't restart again
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
        Log.Info(LogTag, $"Provider disabled: {provider}");
    }

    /// <summary>
    /// Called when the provider is enabled (ILocationListener - fallback).
    /// </summary>
    public void OnProviderEnabled(string provider)
    {
        Log.Info(LogTag, $"Provider enabled: {provider}");
    }

    /// <summary>
    /// Called when the provider status changes (ILocationListener - fallback).
    /// </summary>
    public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras)
    {
        Log.Debug(LogTag, $"Provider {provider} status: {status}");
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
            Log.Debug(LogTag, $"Queued for sync: {location}");

            // Notify that location was queued - used by LocalTimelineStorageService
            // to store with correct coordinates (may differ from broadcast when using best-wake-sample)
            LocationServiceCallbacks.NotifyLocationQueued(location);
        }
        catch (SQLiteException ex)
        {
            Log.Error(LogTag, $"Database error queuing location: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Failed to queue location: {ex.Message}");
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
    /// <param name="accuracyMeters">Maximum acceptable GPS accuracy in meters.</param>
    public void UpdateThresholds(int timeMinutes, int distanceMeters, int accuracyMeters)
    {
        _thresholdFilter?.UpdateThresholds(timeMinutes, distanceMeters, accuracyMeters);
        Log.Debug(LogTag, $"Thresholds updated: {timeMinutes}min / {distanceMeters}m / {accuracyMeters}m accuracy");
    }

    /// <summary>
    /// Handles threshold updates from server sync via LocationServiceCallbacks.
    /// </summary>
    private void OnThresholdsUpdated(object? sender, ThresholdsUpdatedEventArgs e)
    {
        UpdateThresholds(e.TimeThresholdMinutes, e.DistanceThresholdMeters, e.AccuracyThresholdMeters);
    }

    #endregion
}
