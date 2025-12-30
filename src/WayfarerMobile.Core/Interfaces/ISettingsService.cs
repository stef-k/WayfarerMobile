namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets or sets whether this is the first run of the app.
    /// </summary>
    bool IsFirstRun { get; set; }

    /// <summary>
    /// Gets or sets whether timeline tracking is enabled (server logging).
    /// </summary>
    bool TimelineTrackingEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether the user chose 24/7 background tracking during onboarding.
    /// When true, the app expects background location permission to be granted.
    /// If permission is revoked, the health check will redirect to onboarding.
    /// </summary>
    bool BackgroundTrackingEnabled { get; set; }

    /// <summary>
    /// Gets or sets the server URL for API calls.
    /// </summary>
    string? ServerUrl { get; set; }

    /// <summary>
    /// Gets or sets the API authentication token.
    /// </summary>
    string? ApiToken { get; set; }

    /// <summary>
    /// Gets or sets the minimum time between logged locations (from server).
    /// </summary>
    int LocationTimeThresholdMinutes { get; set; }

    /// <summary>
    /// Gets or sets the minimum distance between logged locations (from server).
    /// </summary>
    int LocationDistanceThresholdMeters { get; set; }

    /// <summary>
    /// Gets whether the app is properly configured (has server URL and token).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Gets or sets the maximum concurrent tile downloads (1-4, default 2).
    /// </summary>
    int MaxConcurrentTileDownloads { get; set; }

    /// <summary>
    /// Gets or sets the minimum delay between tile requests in milliseconds (50-5000, default 100).
    /// </summary>
    int MinTileRequestDelayMs { get; set; }

    /// <summary>
    /// Gets or sets the maximum size of the live tile cache in megabytes (100-2000, default 500).
    /// Live cache stores tiles from normal map browsing.
    /// </summary>
    int MaxLiveCacheSizeMB { get; set; }

    /// <summary>
    /// Gets or sets the maximum size of the trip tile cache in megabytes (500-5000, default 2000).
    /// Trip cache stores tiles downloaded for offline trip use.
    /// </summary>
    int MaxTripCacheSizeMB { get; set; }

    /// <summary>
    /// Gets or sets the custom tile server URL.
    /// Default: OpenStreetMap tile server.
    /// </summary>
    string TileServerUrl { get; set; }

    /// <summary>
    /// Prefetch radius in tiles for live cache around user location.
    /// Radius of N means (2N+1)x(2N+1) grid of tiles per zoom level.
    /// Default: 5 (11x11 grid). Range: 1-10 tiles.
    /// </summary>
    int LiveCachePrefetchRadius { get; set; }

    /// <summary>
    /// Independent distance threshold for tile prefetching (in meters).
    /// This is separate from location logging threshold.
    /// Default: 500 meters - only prefetch when user has moved significantly.
    /// </summary>
    int PrefetchDistanceThresholdMeters { get; set; }

    #region Navigation Settings

    /// <summary>
    /// Gets or sets whether navigation audio announcements are enabled.
    /// </summary>
    bool NavigationAudioEnabled { get; set; }

    /// <summary>
    /// Gets or sets the navigation audio volume (0.0 to 1.0).
    /// </summary>
    float NavigationVolume { get; set; }

    /// <summary>
    /// Gets or sets the navigation audio language (e.g., "en-US", "fr-FR").
    /// Empty string means use device default.
    /// </summary>
    string NavigationLanguage { get; set; }

    /// <summary>
    /// Gets or sets whether vibration feedback is enabled during navigation.
    /// </summary>
    bool NavigationVibrationEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether automatic rerouting is enabled when off-route.
    /// </summary>
    bool AutoRerouteEnabled { get; set; }

    /// <summary>
    /// Gets or sets the distance units ("kilometers" or "miles").
    /// </summary>
    string DistanceUnits { get; set; }

    /// <summary>
    /// Gets or sets the last used transport mode for navigation.
    /// </summary>
    string LastTransportMode { get; set; }

    #endregion

    #region Groups Settings

    /// <summary>
    /// Gets or sets the last selected group ID.
    /// </summary>
    string? LastSelectedGroupId { get; set; }

    /// <summary>
    /// Gets or sets the last selected group name.
    /// </summary>
    string? LastSelectedGroupName { get; set; }

    /// <summary>
    /// Gets or sets whether the groups legend is expanded.
    /// </summary>
    bool GroupsLegendExpanded { get; set; }

    #endregion

    #region UI Settings

    /// <summary>
    /// Gets or sets the theme preference: "System", "Light", or "Dark".
    /// "System" follows the device's theme setting.
    /// </summary>
    string ThemePreference { get; set; }

    /// <summary>
    /// Gets or sets whether to keep the screen on while the app is in the foreground.
    /// Prevents the device from going to sleep/screensaver mode during use.
    /// </summary>
    bool KeepScreenOn { get; set; }

    /// <summary>
    /// Gets or sets the navigation voice guidance language preference.
    /// This is used for turn-by-turn voice navigation, not for changing the app display language.
    /// "System" means use device default language.
    /// Otherwise, a culture code like "en", "fr", "de", etc.
    /// </summary>
    string LanguagePreference { get; set; }

    /// <summary>
    /// Gets or sets whether offline map caching is enabled.
    /// </summary>
    bool MapOfflineCacheEnabled { get; set; }

    #endregion

    #region Battery Settings

    /// <summary>
    /// Gets or sets whether battery warnings are shown.
    /// </summary>
    bool ShowBatteryWarnings { get; set; }

    /// <summary>
    /// Gets or sets whether tracking auto-pauses on critical battery.
    /// </summary>
    bool AutoPauseTrackingOnCriticalBattery { get; set; }

    #endregion

    #region Visit Notification Settings

    /// <summary>
    /// Gets or sets whether visit notifications are enabled.
    /// When enabled, the app will subscribe to SSE visit events and notify
    /// when the user arrives at a trip place.
    /// Default: false (opt-in feature).
    /// </summary>
    bool VisitNotificationsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the visit notification style.
    /// Values: "notification", "voice", "both"
    /// Default: "notification"
    /// </summary>
    string VisitNotificationStyle { get; set; }

    /// <summary>
    /// Gets or sets whether voice announcements are enabled for visit notifications.
    /// Uses the same language and volume settings as navigation audio.
    /// Default: false
    /// </summary>
    bool VisitVoiceAnnouncementEnabled { get; set; }

    #endregion

    #region Sync

    /// <summary>
    /// Gets or sets the last sync time.
    /// </summary>
    DateTime? LastSyncTime { get; set; }

    #endregion

    #region Queue Sync Reference Point

    /// <summary>
    /// Gets or sets the latitude of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    double? LastSyncedLatitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    double? LastSyncedLongitude { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last successfully synced location.
    /// Used as reference point for threshold calculations in queue drain.
    /// </summary>
    DateTime? LastSyncedTimestamp { get; set; }

    /// <summary>
    /// Checks if a valid sync reference point exists.
    /// Returns false on first sync (no previous reference).
    /// </summary>
    bool HasValidSyncReference();

    /// <summary>
    /// Updates the sync reference point after a successful sync.
    /// Called only when server accepts a location.
    /// </summary>
    /// <param name="latitude">Latitude of synced location.</param>
    /// <param name="longitude">Longitude of synced location.</param>
    /// <param name="timestampUtc">Timestamp of synced location (must be DateTimeKind.Utc).</param>
    void UpdateLastSyncedLocation(double latitude, double longitude, DateTime timestampUtc);

    /// <summary>
    /// Clears the sync reference point.
    /// Called on logout or when reference is stale (>30 days).
    /// </summary>
    void ClearSyncReference();

    #endregion

    /// <summary>
    /// Clears all settings (for logout/reset).
    /// </summary>
    void Clear();

    /// <summary>
    /// Clears authentication data only (server URL and token).
    /// </summary>
    void ClearAuth();

    /// <summary>
    /// Resets all settings to defaults. Used for recovery when app state is corrupted
    /// (e.g., after user clears app data from Android Settings).
    /// </summary>
    void ResetToDefaults();
}
