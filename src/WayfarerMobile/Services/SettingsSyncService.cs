using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service responsible for syncing application settings and activity types with the server.
/// Syncs on app startup and resume, with a minimum interval of 6 hours between syncs.
/// Since only the location service runs 24/7 (not the app), we sync opportunistically
/// when the user opens or returns to the app.
/// </summary>
public class SettingsSyncService
{
    private readonly IApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly IActivitySyncService _activitySyncService;
    private readonly ILogger<SettingsSyncService> _logger;

    /// <summary>
    /// Minimum interval between syncs: 6 hours (4 times per day max).
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Preference key for tracking last settings sync time.
    /// </summary>
    private const string LastSettingsSyncKey = "last_settings_sync";

    /// <summary>
    /// Event raised when settings are updated from server.
    /// </summary>
    public event EventHandler<SettingsUpdatedEventArgs>? SettingsUpdated;

    /// <summary>
    /// Creates a new instance of SettingsSyncService.
    /// </summary>
    public SettingsSyncService(
        IApiClient apiClient,
        ISettingsService settingsService,
        IActivitySyncService activitySyncService,
        ILogger<SettingsSyncService> logger)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _activitySyncService = activitySyncService;
        _logger = logger;
    }

    /// <summary>
    /// Syncs settings if due. Call this on app startup and resume.
    /// Will only actually sync if 6+ hours have passed since last sync.
    /// </summary>
    public async Task SyncIfDueAsync()
    {
        await SyncSettingsAsync(force: false);
    }

    /// <summary>
    /// Syncs settings from the server if due (based on last sync time).
    /// </summary>
    /// <param name="force">If true, syncs regardless of last sync time.</param>
    /// <returns>True if settings were updated.</returns>
    public async Task<bool> SyncSettingsAsync(bool force = false)
    {
        if (!_settingsService.IsConfigured)
        {
            _logger.LogDebug("Settings sync skipped - not configured");
            return false;
        }

        // Check if sync is due (unless forced)
        if (!force && !IsSyncDue())
        {
            _logger.LogDebug("Settings sync skipped - not due yet");
            return false;
        }

        try
        {
            _logger.LogInformation("Syncing settings from server...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var serverSettings = await _apiClient.GetSettingsAsync(cts.Token);

            if (serverSettings == null)
            {
                _logger.LogWarning("Failed to fetch settings from server");
                return false;
            }

            // Track what changed
            var oldTimeThreshold = _settingsService.LocationTimeThresholdMinutes;
            var oldDistanceThreshold = _settingsService.LocationDistanceThresholdMeters;

            // Update local settings
            if (serverSettings.LocationTimeThresholdMinutes > 0)
            {
                _settingsService.LocationTimeThresholdMinutes = serverSettings.LocationTimeThresholdMinutes;
            }

            if (serverSettings.LocationDistanceThresholdMeters > 0)
            {
                _settingsService.LocationDistanceThresholdMeters = serverSettings.LocationDistanceThresholdMeters;
            }

            // Record sync time
            Preferences.Set(LastSettingsSyncKey, DateTime.UtcNow.Ticks);

            var thresholdsChanged =
                oldTimeThreshold != _settingsService.LocationTimeThresholdMinutes ||
                oldDistanceThreshold != _settingsService.LocationDistanceThresholdMeters;

            _logger.LogInformation(
                "Settings synced: Time={Time}min, Distance={Distance}m (changed: {Changed})",
                _settingsService.LocationTimeThresholdMinutes,
                _settingsService.LocationDistanceThresholdMeters,
                thresholdsChanged);

            // Notify listeners if thresholds changed
            if (thresholdsChanged)
            {
                SettingsUpdated?.Invoke(this, new SettingsUpdatedEventArgs
                {
                    TimeThresholdMinutes = _settingsService.LocationTimeThresholdMinutes,
                    DistanceThresholdMeters = _settingsService.LocationDistanceThresholdMeters
                });
            }

            // Also sync activity types
            try
            {
                await _activitySyncService.SyncWithServerAsync();
                _logger.LogDebug("Activity types synced during settings sync");
            }
            catch (Exception actEx)
            {
                _logger.LogWarning(actEx, "Failed to sync activity types during settings sync");
                // Don't fail the whole sync if activities fail
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing settings from server");
            return false;
        }
    }

    /// <summary>
    /// Checks if a settings sync is due based on the last sync time.
    /// </summary>
    private bool IsSyncDue()
    {
        var lastSyncTicks = Preferences.Get(LastSettingsSyncKey, 0L);
        if (lastSyncTicks == 0)
            return true; // Never synced

        var lastSync = new DateTime(lastSyncTicks, DateTimeKind.Utc);
        var timeSinceSync = DateTime.UtcNow - lastSync;

        return timeSinceSync >= SyncInterval;
    }

    /// <summary>
    /// Gets the time until the next scheduled sync.
    /// </summary>
    public TimeSpan GetTimeUntilNextSync()
    {
        var lastSyncTicks = Preferences.Get(LastSettingsSyncKey, 0L);
        if (lastSyncTicks == 0)
            return TimeSpan.Zero;

        var lastSync = new DateTime(lastSyncTicks, DateTimeKind.Utc);
        var nextSync = lastSync + SyncInterval;
        var timeUntil = nextSync - DateTime.UtcNow;

        return timeUntil > TimeSpan.Zero ? timeUntil : TimeSpan.Zero;
    }

}

/// <summary>
/// Event args for settings updated event.
/// </summary>
public class SettingsUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the updated time threshold in minutes.
    /// </summary>
    public int TimeThresholdMinutes { get; set; }

    /// <summary>
    /// Gets or sets the updated distance threshold in meters.
    /// </summary>
    public int DistanceThresholdMeters { get; set; }
}
