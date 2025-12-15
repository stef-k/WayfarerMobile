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
    private const string KeyUserId = "user_id";
    private const string KeyUserEmail = "user_email";
    private const string KeyThemePreference = "theme_preference";
    private const string KeyLanguagePreference = "language_preference";
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
    /// </summary>
    public bool TimelineTrackingEnabled
    {
        get => Preferences.Get(KeyTimelineTrackingEnabled, true);
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

    /// <summary>
    /// Gets or sets the server URL for API calls.
    /// Stored in SecureStorage for enhanced security.
    /// </summary>
    public string? ServerUrl
    {
        get => SecureStorage.Default.GetAsync(KeyServerUrl).GetAwaiter().GetResult();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyServerUrl);
            }
            else
            {
                SecureStorage.Default.SetAsync(KeyServerUrl, value).GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>
    /// Gets or sets the API authentication token.
    /// Stored in SecureStorage for enhanced security.
    /// </summary>
    public string? ApiToken
    {
        get => SecureStorage.Default.GetAsync(KeyApiToken).GetAwaiter().GetResult();
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                SecureStorage.Default.Remove(KeyApiToken);
            }
            else
            {
                SecureStorage.Default.SetAsync(KeyApiToken, value).GetAwaiter().GetResult();
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
    /// Gets or sets the user ID from the server.
    /// </summary>
    public string? UserId
    {
        get => Preferences.Get(KeyUserId, null as string);
        set => Preferences.Set(KeyUserId, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the user email.
    /// </summary>
    public string? UserEmail
    {
        get => Preferences.Get(KeyUserEmail, null as string);
        set => Preferences.Set(KeyUserEmail, value ?? string.Empty);
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
    /// Removes sensitive data from SecureStorage.
    /// </summary>
    public void ClearAuth()
    {
        SecureStorage.Default.Remove(KeyApiToken);
        SecureStorage.Default.Remove(KeyServerUrl);
        UserId = null;
        UserEmail = null;
    }

    #endregion
}
