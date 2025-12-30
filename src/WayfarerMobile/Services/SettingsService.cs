using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing application settings using MAUI Preferences.
/// </summary>
public class SettingsService : ISettingsService
{
    #region Keys

    private const string KeyIsFirstRun = "is_first_run";
    private const string KeyTimelineTrackingEnabled = "timeline_tracking_enabled";
    private const string KeyBackgroundTrackingEnabled = "background_tracking_enabled";
    private const string KeyServerUrl = "server_url";
    private const string KeyApiToken = "api_token";
    private const string KeyLocationTimeThreshold = "location_time_threshold";
    private const string KeyLocationDistanceThreshold = "location_distance_threshold";
    private const string KeyLastSyncTime = "last_sync_time";
    private const string KeyThemePreference = "theme_preference";
    private const string KeyLanguagePreference = "language_preference";
    private const string KeyKeepScreenOn = "keep_screen_on";
    private const string KeyMapOfflineCacheEnabled = "map_offline_cache_enabled";
    private const string KeyMaxConcurrentTileDownloads = "max_concurrent_tile_downloads";
    private const string KeyMinTileRequestDelayMs = "min_tile_request_delay_ms";
    private const string KeyMaxLiveCacheSizeMB = "max_live_cache_size_mb";
    private const string KeyMaxTripCacheSizeMB = "max_trip_cache_size_mb";
    private const string KeyTileServerUrl = "tile_server_url";
    private const string KeyLiveCachePrefetchRadius = "live_cache_prefetch_radius";
    private const string KeyPrefetchDistanceThreshold = "prefetch_distance_threshold";

    // Navigation settings
    private const string KeyNavigationAudioEnabled = "navigation_audio_enabled";
    private const string KeyNavigationVolume = "navigation_volume";
    private const string KeyNavigationLanguage = "navigation_language";
    private const string KeyNavigationVibrationEnabled = "navigation_vibration_enabled";
    private const string KeyAutoRerouteEnabled = "auto_reroute_enabled";
    private const string KeyDistanceUnits = "distance_units";
    private const string KeyLastTransportMode = "last_transport_mode";

    // Groups settings
    private const string KeyLastSelectedGroupId = "last_selected_group_id";
    private const string KeyLastSelectedGroupName = "last_selected_group_name";
    private const string KeyGroupsLegendExpanded = "groups_legend_expanded";

    // Visit notification settings
    private const string KeyVisitNotificationsEnabled = "visit_notifications_enabled";
    private const string KeyVisitNotificationStyle = "visit_notification_style";
    private const string KeyVisitVoiceAnnouncementEnabled = "visit_voice_announcement_enabled";

    // Queue sync reference point
    private const string KeyLastSyncedLatitude = "last_synced_latitude";
    private const string KeyLastSyncedLongitude = "last_synced_longitude";
    private const string KeyLastSyncedTimestamp = "last_synced_timestamp";

    #endregion

    #region ISettingsService Implementation

    /// <summary>
    /// Gets or sets whether this is the first run of the app.
    /// </summary>
    public bool IsFirstRun
    {
        get => Preferences.Get(KeyIsFirstRun, true);
        set => Preferences.Set(KeyIsFirstRun, value);
    }

    /// <summary>
    /// Gets or sets whether timeline tracking is enabled (server logging).
    /// When disabled, GPS still works for live location/navigation but locations are not sent to server.
    /// Default is false for privacy reasons - user must explicitly enable.
    /// </summary>
    public bool TimelineTrackingEnabled
    {
        get => Preferences.Get(KeyTimelineTrackingEnabled, false);
        set => Preferences.Set(KeyTimelineTrackingEnabled, value);
    }

    /// <summary>
    /// Gets or sets whether the user chose 24/7 background tracking during onboarding.
    /// When true, the app expects background location permission to be granted.
    /// If permission is revoked, the health check will redirect to onboarding.
    /// Default is false - user must explicitly enable during onboarding.
    /// </summary>
    public bool BackgroundTrackingEnabled
    {
        get => Preferences.Get(KeyBackgroundTrackingEnabled, false);
        set => Preferences.Set(KeyBackgroundTrackingEnabled, value);
    }

    // Cached values to avoid blocking SecureStorage calls on main thread
    private string? _cachedServerUrl;
    private string? _cachedApiToken;
    private bool _serverUrlLoaded;
    private bool _apiTokenLoaded;

    /// <summary>
    /// Pre-loads secure settings from SecureStorage into memory cache.
    /// Call this at app startup to avoid blocking on first access.
    /// </summary>
    public async Task PreloadSecureSettingsAsync()
    {
        if (!_serverUrlLoaded)
        {
            try
            {
                _cachedServerUrl = await SecureStorage.Default.GetAsync(KeyServerUrl);
            }
            catch (Exception ex)
            {
                // SecureStorage can fail after data clear - treat as empty
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load ServerUrl from SecureStorage: {ex.Message}");
                _cachedServerUrl = null;
            }
            _serverUrlLoaded = true;
        }
        if (!_apiTokenLoaded)
        {
            try
            {
                _cachedApiToken = await SecureStorage.Default.GetAsync(KeyApiToken);
            }
            catch (Exception ex)
            {
                // SecureStorage can fail after data clear - treat as empty
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load ApiToken from SecureStorage: {ex.Message}");
                _cachedApiToken = null;
            }
            _apiTokenLoaded = true;
        }
    }

    /// <summary>
    /// Gets or sets the server URL for API calls.
    /// Cached in memory to avoid SecureStorage deadlocks on Android.
    /// </summary>
    public string? ServerUrl
    {
        get
        {
            if (!_serverUrlLoaded)
            {
                try
                {
                    // First access - load from SecureStorage on background thread
                    _cachedServerUrl = Task.Run(async () => await SecureStorage.Default.GetAsync(KeyServerUrl)).Result;
                }
                catch (Exception ex)
                {
                    // SecureStorage can fail after data clear - treat as empty
                    System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load ServerUrl: {ex.Message}");
                    _cachedServerUrl = null;
                }
                _serverUrlLoaded = true;
            }
            return _cachedServerUrl;
        }
        set
        {
            _cachedServerUrl = value;
            _serverUrlLoaded = true;
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyServerUrl);
            }
            else
            {
                Task.Run(async () => await SecureStorage.Default.SetAsync(KeyServerUrl, value));
            }
        }
    }

    /// <summary>
    /// Gets or sets the API authentication token.
    /// Cached in memory to avoid SecureStorage deadlocks on Android.
    /// </summary>
    public string? ApiToken
    {
        get
        {
            if (!_apiTokenLoaded)
            {
                try
                {
                    // First access - load from SecureStorage on background thread
                    _cachedApiToken = Task.Run(async () => await SecureStorage.Default.GetAsync(KeyApiToken)).Result;
                }
                catch (Exception ex)
                {
                    // SecureStorage can fail after data clear - treat as empty
                    System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load ApiToken: {ex.Message}");
                    _cachedApiToken = null;
                }
                _apiTokenLoaded = true;
            }
            return _cachedApiToken;
        }
        set
        {
            _cachedApiToken = value;
            _apiTokenLoaded = true;
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyApiToken);
            }
            else
            {
                Task.Run(async () => await SecureStorage.Default.SetAsync(KeyApiToken, value));
            }
        }
    }

    /// <summary>
    /// Gets or sets the minimum time between logged locations (from server config).
    /// Default: 5 minutes.
    /// </summary>
    public int LocationTimeThresholdMinutes
    {
        get => Preferences.Get(KeyLocationTimeThreshold, 5);
        set => Preferences.Set(KeyLocationTimeThreshold, value);
    }

    /// <summary>
    /// Gets or sets the minimum distance between logged locations (from server config).
    /// Default: 15 meters.
    /// </summary>
    public int LocationDistanceThresholdMeters
    {
        get => Preferences.Get(KeyLocationDistanceThreshold, 15);
        set => Preferences.Set(KeyLocationDistanceThreshold, value);
    }

    /// <summary>
    /// Gets whether the app is properly configured (has server URL and token).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(ApiToken);

    #endregion

    #region Additional Settings

    /// <summary>
    /// Gets or sets the last successful sync time.
    /// </summary>
    public DateTime? LastSyncTime
    {
        get
        {
            var ticks = Preferences.Get(KeyLastSyncTime, 0L);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
        set => Preferences.Set(KeyLastSyncTime, value?.Ticks ?? 0L);
    }

    /// <summary>
    /// Gets or sets the theme preference: "System", "Light", or "Dark".
    /// Default is "System" which follows the device's theme setting.
    /// </summary>
    public string ThemePreference
    {
        get => Preferences.Get(KeyThemePreference, "System");
        set => Preferences.Set(KeyThemePreference, value ?? "System");
    }

    /// <summary>
    /// Gets or sets whether to keep the screen on while the app is in the foreground.
    /// Default is false - user must explicitly enable to prevent accidental battery drain.
    /// </summary>
    public bool KeepScreenOn
    {
        get => Preferences.Get(KeyKeepScreenOn, false);
        set => Preferences.Set(KeyKeepScreenOn, value);
    }

    /// <summary>
    /// Gets or sets the navigation voice guidance language preference.
    /// This is used for turn-by-turn voice navigation, not for changing the app display language.
    /// "System" means use device default language.
    /// Otherwise, a culture code like "en", "fr", "de", etc.
    /// </summary>
    public string LanguagePreference
    {
        get => Preferences.Get(KeyLanguagePreference, "System");
        set => Preferences.Set(KeyLanguagePreference, value ?? "System");
    }

    /// <summary>
    /// Gets or sets whether offline map caching is enabled.
    /// </summary>
    public bool MapOfflineCacheEnabled
    {
        get => Preferences.Get(KeyMapOfflineCacheEnabled, true);
        set => Preferences.Set(KeyMapOfflineCacheEnabled, value);
    }

    /// <summary>
    /// Gets or sets the maximum concurrent tile downloads (1-4, default 2).
    /// </summary>
    public int MaxConcurrentTileDownloads
    {
        get => Preferences.Get(KeyMaxConcurrentTileDownloads, 2);
        set => Preferences.Set(KeyMaxConcurrentTileDownloads, Math.Clamp(value, 1, 4));
    }

    /// <summary>
    /// Gets or sets the minimum delay between tile requests in milliseconds (50-5000, default 100).
    /// </summary>
    public int MinTileRequestDelayMs
    {
        get => Preferences.Get(KeyMinTileRequestDelayMs, 100);
        set => Preferences.Set(KeyMinTileRequestDelayMs, Math.Clamp(value, 50, 5000));
    }

    /// <summary>
    /// Gets or sets the maximum size of the live tile cache in megabytes (100-2000, default 500).
    /// Live cache stores tiles from normal map browsing.
    /// </summary>
    public int MaxLiveCacheSizeMB
    {
        get => Preferences.Get(KeyMaxLiveCacheSizeMB, 500);
        set => Preferences.Set(KeyMaxLiveCacheSizeMB, Math.Clamp(value, 100, 2000));
    }

    /// <summary>
    /// Gets or sets the maximum size of the trip tile cache in megabytes (500-5000, default 2000).
    /// Trip cache stores tiles downloaded for offline trip use.
    /// </summary>
    public int MaxTripCacheSizeMB
    {
        get => Preferences.Get(KeyMaxTripCacheSizeMB, 2000);
        set => Preferences.Set(KeyMaxTripCacheSizeMB, Math.Clamp(value, 500, 5000));
    }

    /// <summary>
    /// Default tile server URL (OpenStreetMap).
    /// </summary>
    public const string DefaultTileServerUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    /// <summary>
    /// Gets or sets the custom tile server URL.
    /// Must contain {z}, {x}, {y} placeholders.
    /// Default: OpenStreetMap tile server.
    /// </summary>
    public string TileServerUrl
    {
        get => Preferences.Get(KeyTileServerUrl, DefaultTileServerUrl);
        set => Preferences.Set(KeyTileServerUrl, ValidateTileServerUrl(value));
    }

    /// <summary>
    /// Validates a tile server URL, returning the default if invalid.
    /// </summary>
    private static string ValidateTileServerUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return DefaultTileServerUrl;

        // Must contain required placeholders
        if (!url.Contains("{z}") || !url.Contains("{x}") || !url.Contains("{y}"))
            return DefaultTileServerUrl;

        // Must be a valid URL (basic check)
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            return DefaultTileServerUrl;

        return url;
    }

    /// <summary>
    /// Prefetch radius in tiles for live cache around user location.
    /// Radius of N means (2N+1)x(2N+1) grid of tiles per zoom level.
    /// Default: 5 (11x11 grid). Range: 1-10 tiles.
    /// </summary>
    public int LiveCachePrefetchRadius
    {
        get => Preferences.Get(KeyLiveCachePrefetchRadius, 5);
        set => Preferences.Set(KeyLiveCachePrefetchRadius, Math.Clamp(value, 1, 10));
    }

    /// <summary>
    /// Independent distance threshold for tile prefetching (in meters).
    /// This is separate from location logging threshold.
    /// Default: 500 meters - only prefetch when user has moved significantly.
    /// Range: 100-2000 meters.
    /// </summary>
    public int PrefetchDistanceThresholdMeters
    {
        get => Preferences.Get(KeyPrefetchDistanceThreshold, 500);
        set => Preferences.Set(KeyPrefetchDistanceThreshold, Math.Clamp(value, 100, 2000));
    }

    #endregion

    #region Navigation Settings

    /// <summary>
    /// Gets or sets whether navigation audio announcements are enabled.
    /// </summary>
    public bool NavigationAudioEnabled
    {
        get => Preferences.Get(KeyNavigationAudioEnabled, true);
        set => Preferences.Set(KeyNavigationAudioEnabled, value);
    }

    /// <summary>
    /// Gets or sets the navigation audio volume (0.0 to 1.0).
    /// </summary>
    public float NavigationVolume
    {
        get => Preferences.Get(KeyNavigationVolume, 1.0f);
        set => Preferences.Set(KeyNavigationVolume, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// Gets or sets the navigation audio language (e.g., "en-US", "fr-FR").
    /// Empty string means use device default.
    /// </summary>
    public string NavigationLanguage
    {
        get => Preferences.Get(KeyNavigationLanguage, string.Empty);
        set => Preferences.Set(KeyNavigationLanguage, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets whether vibration feedback is enabled during navigation.
    /// </summary>
    public bool NavigationVibrationEnabled
    {
        get => Preferences.Get(KeyNavigationVibrationEnabled, true);
        set => Preferences.Set(KeyNavigationVibrationEnabled, value);
    }

    /// <summary>
    /// Gets or sets whether automatic rerouting is enabled when off-route.
    /// </summary>
    public bool AutoRerouteEnabled
    {
        get => Preferences.Get(KeyAutoRerouteEnabled, true);
        set => Preferences.Set(KeyAutoRerouteEnabled, value);
    }

    /// <summary>
    /// Gets or sets the distance units ("kilometers" or "miles").
    /// </summary>
    public string DistanceUnits
    {
        get => Preferences.Get(KeyDistanceUnits, "kilometers");
        set => Preferences.Set(KeyDistanceUnits, value == "miles" ? "miles" : "kilometers");
    }

    /// <summary>
    /// Gets or sets the last used transport mode for navigation.
    /// </summary>
    public string LastTransportMode
    {
        get => Preferences.Get(KeyLastTransportMode, "walk");
        set => Preferences.Set(KeyLastTransportMode, value ?? "walk");
    }

    #endregion

    #region Groups Settings

    /// <summary>
    /// Gets or sets the last selected group ID.
    /// </summary>
    public string? LastSelectedGroupId
    {
        get => Preferences.Get(KeyLastSelectedGroupId, null as string);
        set => Preferences.Set(KeyLastSelectedGroupId, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the last selected group name.
    /// </summary>
    public string? LastSelectedGroupName
    {
        get => Preferences.Get(KeyLastSelectedGroupName, null as string);
        set => Preferences.Set(KeyLastSelectedGroupName, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets whether the groups legend is expanded.
    /// </summary>
    public bool GroupsLegendExpanded
    {
        get => Preferences.Get(KeyGroupsLegendExpanded, true);
        set => Preferences.Set(KeyGroupsLegendExpanded, value);
    }

    #endregion

    #region Battery Settings

    private const string KeyAutoPauseTrackingOnCriticalBattery = "auto_pause_tracking_critical_battery";
    private const string KeyShowBatteryWarnings = "show_battery_warnings";

    /// <summary>
    /// Gets or sets whether tracking should auto-pause when battery is critical (below 10%).
    /// </summary>
    public bool AutoPauseTrackingOnCriticalBattery
    {
        get => Preferences.Get(KeyAutoPauseTrackingOnCriticalBattery, false);
        set => Preferences.Set(KeyAutoPauseTrackingOnCriticalBattery, value);
    }

    /// <summary>
    /// Gets or sets whether battery warnings should be shown during tracking.
    /// </summary>
    public bool ShowBatteryWarnings
    {
        get => Preferences.Get(KeyShowBatteryWarnings, true);
        set => Preferences.Set(KeyShowBatteryWarnings, value);
    }

    #endregion

    #region Visit Notification Settings

    /// <summary>
    /// Gets or sets whether visit notifications are enabled.
    /// When enabled, the app subscribes to SSE visit events and notifies
    /// when the user arrives at a trip place.
    /// Default: false (opt-in feature).
    /// </summary>
    public bool VisitNotificationsEnabled
    {
        get => Preferences.Get(KeyVisitNotificationsEnabled, false);
        set => Preferences.Set(KeyVisitNotificationsEnabled, value);
    }

    /// <summary>
    /// Gets or sets the visit notification style.
    /// Values: "notification", "voice", "both"
    /// Default: "notification"
    /// </summary>
    public string VisitNotificationStyle
    {
        get => Preferences.Get(KeyVisitNotificationStyle, "notification");
        set
        {
            // Validate to only accept known values
            var validValue = value switch
            {
                "voice" => "voice",
                "both" => "both",
                _ => "notification"
            };
            Preferences.Set(KeyVisitNotificationStyle, validValue);
        }
    }

    /// <summary>
    /// Gets or sets whether voice announcements are enabled for visit notifications.
    /// Uses the same language and volume settings as navigation audio.
    /// Default: false
    /// </summary>
    public bool VisitVoiceAnnouncementEnabled
    {
        get => Preferences.Get(KeyVisitVoiceAnnouncementEnabled, false);
        set => Preferences.Set(KeyVisitVoiceAnnouncementEnabled, value);
    }

    #endregion

    #region Queue Sync Reference Point

    /// <summary>
    /// Lock object for thread-safe sync reference updates.
    /// </summary>
    private readonly object _syncReferenceLock = new();

    /// <summary>
    /// Gets or sets the latitude of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    public double? LastSyncedLatitude
    {
        get
        {
            if (!Preferences.ContainsKey(KeyLastSyncedLatitude))
                return null;
            return Preferences.Get(KeyLastSyncedLatitude, 0.0);
        }
        set
        {
            if (value.HasValue)
                Preferences.Set(KeyLastSyncedLatitude, value.Value);
            else
                Preferences.Remove(KeyLastSyncedLatitude);
        }
    }

    /// <summary>
    /// Gets or sets the longitude of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    public double? LastSyncedLongitude
    {
        get
        {
            if (!Preferences.ContainsKey(KeyLastSyncedLongitude))
                return null;
            return Preferences.Get(KeyLastSyncedLongitude, 0.0);
        }
        set
        {
            if (value.HasValue)
                Preferences.Set(KeyLastSyncedLongitude, value.Value);
            else
                Preferences.Remove(KeyLastSyncedLongitude);
        }
    }

    /// <summary>
    /// Gets or sets the timestamp of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    public DateTime? LastSyncedTimestamp
    {
        get
        {
            if (!Preferences.ContainsKey(KeyLastSyncedTimestamp))
                return null;
            var ticks = Preferences.Get(KeyLastSyncedTimestamp, 0L);
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
        set
        {
            if (value.HasValue)
                Preferences.Set(KeyLastSyncedTimestamp, value.Value.Ticks);
            else
                Preferences.Remove(KeyLastSyncedTimestamp);
        }
    }

    /// <summary>
    /// Checks if a valid sync reference point exists.
    /// Returns false on first sync (no previous reference).
    /// </summary>
    public bool HasValidSyncReference()
    {
        lock (_syncReferenceLock)
        {
            return LastSyncedLatitude.HasValue &&
                   LastSyncedLongitude.HasValue &&
                   LastSyncedTimestamp.HasValue;
        }
    }

    /// <summary>
    /// Updates the sync reference point after a successful sync.
    /// Called only when server accepts a location.
    /// </summary>
    /// <param name="latitude">Latitude of synced location.</param>
    /// <param name="longitude">Longitude of synced location.</param>
    /// <param name="timestampUtc">Timestamp of synced location (must be DateTimeKind.Utc).</param>
    public void UpdateLastSyncedLocation(double latitude, double longitude, DateTime timestampUtc)
    {
        lock (_syncReferenceLock)
        {
            LastSyncedLatitude = latitude;
            LastSyncedLongitude = longitude;
            LastSyncedTimestamp = timestampUtc;
        }
    }

    /// <summary>
    /// Clears the sync reference point.
    /// Called on logout or when reference is stale (>30 days).
    /// </summary>
    public void ClearSyncReference()
    {
        lock (_syncReferenceLock)
        {
            LastSyncedLatitude = null;
            LastSyncedLongitude = null;
            LastSyncedTimestamp = null;
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Clears all settings (for logout/reset).
    /// </summary>
    public void Clear()
    {
        Preferences.Clear();
        // Reset first run to false since app was used
        IsFirstRun = false;
    }

    /// <summary>
    /// Clears authentication data only.
    /// Removes sensitive data from SecureStorage and clears sync reference.
    /// </summary>
    public void ClearAuth()
    {
        SecureStorage.Default.Remove(KeyApiToken);
        SecureStorage.Default.Remove(KeyServerUrl);

        // Clear cached values
        _cachedApiToken = null;
        _cachedServerUrl = null;
        _apiTokenLoaded = true;
        _serverUrlLoaded = true;

        // Clear sync reference - new account will have different timeline
        ClearSyncReference();
    }

    /// <summary>
    /// Resets all settings to defaults. Used for recovery when app state is corrupted
    /// (e.g., after user clears app data from Android Settings).
    /// </summary>
    public void ResetToDefaults()
    {
        System.Diagnostics.Debug.WriteLine("[SettingsService] Resetting all settings to defaults");

        try
        {
            // Clear all preferences
            Preferences.Clear();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to clear Preferences: {ex.Message}");
        }

        try
        {
            // Clear secure storage
            SecureStorage.Default.RemoveAll();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to clear SecureStorage: {ex.Message}");
        }

        // Reset cached values
        _cachedServerUrl = null;
        _cachedApiToken = null;
        _serverUrlLoaded = true;
        _apiTokenLoaded = true;

        // Ensure IsFirstRun is true so onboarding will show
        try
        {
            Preferences.Set(KeyIsFirstRun, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to set IsFirstRun: {ex.Message}");
        }

        System.Diagnostics.Debug.WriteLine("[SettingsService] Reset complete - app will show onboarding");
    }

    #endregion
}
