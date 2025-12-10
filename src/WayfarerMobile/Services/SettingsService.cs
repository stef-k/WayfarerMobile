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
    private const string KeyServerUrl = "server_url";
    private const string KeyApiToken = "api_token";
    private const string KeyLocationTimeThreshold = "location_time_threshold";
    private const string KeyLocationDistanceThreshold = "location_distance_threshold";
    private const string KeyLastSyncTime = "last_sync_time";
    private const string KeyUserId = "user_id";
    private const string KeyUserEmail = "user_email";
    private const string KeyDarkModeEnabled = "dark_mode_enabled";
    private const string KeyMapOfflineCacheEnabled = "map_offline_cache_enabled";
    private const string KeyMaxConcurrentTileDownloads = "max_concurrent_tile_downloads";
    private const string KeyMinTileRequestDelayMs = "min_tile_request_delay_ms";
    private const string KeyMaxLiveCacheSizeMB = "max_live_cache_size_mb";
    private const string KeyMaxTripCacheSizeMB = "max_trip_cache_size_mb";

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
    /// Gets or sets the server URL for API calls.
    /// </summary>
    public string? ServerUrl
    {
        get => Preferences.Get(KeyServerUrl, null as string);
        set => Preferences.Set(KeyServerUrl, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the API authentication token.
    /// </summary>
    public string? ApiToken
    {
        get => Preferences.Get(KeyApiToken, null as string);
        set => Preferences.Set(KeyApiToken, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the minimum time between logged locations (from server config).
    /// </summary>
    public int LocationTimeThresholdMinutes
    {
        get => Preferences.Get(KeyLocationTimeThreshold, 1);
        set => Preferences.Set(KeyLocationTimeThreshold, value);
    }

    /// <summary>
    /// Gets or sets the minimum distance between logged locations (from server config).
    /// </summary>
    public int LocationDistanceThresholdMeters
    {
        get => Preferences.Get(KeyLocationDistanceThreshold, 50);
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
    /// Gets or sets whether dark mode is enabled.
    /// </summary>
    public bool DarkModeEnabled
    {
        get => Preferences.Get(KeyDarkModeEnabled, false);
        set => Preferences.Set(KeyDarkModeEnabled, value);
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
    /// </summary>
    public void ClearAuth()
    {
        ApiToken = null;
        UserId = null;
        UserEmail = null;
    }

    #endregion
}
