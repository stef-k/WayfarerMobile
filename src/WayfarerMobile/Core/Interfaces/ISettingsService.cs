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
    /// Clears all settings (for logout/reset).
    /// </summary>
    void Clear();
}
