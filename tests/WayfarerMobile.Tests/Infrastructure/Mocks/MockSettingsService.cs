using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of ISettingsService for unit tests.
/// All properties are mutable for test setup.
/// </summary>
public class MockSettingsService : ISettingsService
{
    // Reference point state for atomic operations
    private readonly object _syncLock = new();

    #region Core Settings

    public bool IsFirstRun { get; set; } = false;
    public bool TimelineTrackingEnabled { get; set; } = false;
    public bool BackgroundTrackingEnabled { get; set; } = false;
    public string? ServerUrl { get; set; } = "https://test.example.com";
    public string? ApiToken { get; set; } = "test-token";
    public int LocationTimeThresholdMinutes { get; set; } = 5;
    public int LocationDistanceThresholdMeters { get; set; } = 100;

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ServerUrl) && !string.IsNullOrEmpty(ApiToken);

    #endregion

    #region Tile Cache Settings

    public int MaxConcurrentTileDownloads { get; set; } = 2;
    public int MinTileRequestDelayMs { get; set; } = 100;
    public int MaxLiveCacheSizeMB { get; set; } = 500;
    public int MaxTripCacheSizeMB { get; set; } = 2000;
    public string TileServerUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public int LiveCachePrefetchRadius { get; set; } = 5;
    public int PrefetchDistanceThresholdMeters { get; set; } = 500;

    #endregion

    #region Navigation Settings

    public bool NavigationAudioEnabled { get; set; } = true;
    public float NavigationVolume { get; set; } = 0.8f;
    public string NavigationLanguage { get; set; } = string.Empty;
    public bool NavigationVibrationEnabled { get; set; } = true;
    public bool AutoRerouteEnabled { get; set; } = true;
    public string DistanceUnits { get; set; } = "kilometers";
    public string LastTransportMode { get; set; } = "driving";

    #endregion

    #region Groups Settings

    public string? LastSelectedGroupId { get; set; }
    public string? LastSelectedGroupName { get; set; }
    public bool GroupsLegendExpanded { get; set; } = true;

    #endregion

    #region UI Settings

    public string ThemePreference { get; set; } = "System";
    public bool KeepScreenOn { get; set; } = false;
    public string LanguagePreference { get; set; } = "System";
    public bool MapOfflineCacheEnabled { get; set; } = true;

    #endregion

    #region Battery Settings

    public bool ShowBatteryWarnings { get; set; } = true;
    public bool AutoPauseTrackingOnCriticalBattery { get; set; } = true;

    #endregion

    #region Visit Notification Settings

    public bool VisitNotificationsEnabled { get; set; } = false;
    public string VisitNotificationStyle { get; set; } = "notification";
    public bool VisitVoiceAnnouncementEnabled { get; set; } = false;

    #endregion

    #region Sync

    public DateTime? LastSyncTime { get; set; }
    public double? LastSyncedLatitude { get; set; }
    public double? LastSyncedLongitude { get; set; }
    public DateTime? LastSyncedTimestamp { get; set; }

    public bool HasValidSyncReference()
    {
        lock (_syncLock)
        {
            return LastSyncedLatitude.HasValue &&
                   LastSyncedLongitude.HasValue &&
                   LastSyncedTimestamp.HasValue;
        }
    }

    public bool TryGetSyncReference(out double latitude, out double longitude, out DateTime timestamp)
    {
        lock (_syncLock)
        {
            if (HasValidSyncReference())
            {
                latitude = LastSyncedLatitude!.Value;
                longitude = LastSyncedLongitude!.Value;
                timestamp = LastSyncedTimestamp!.Value;
                return true;
            }

            latitude = 0;
            longitude = 0;
            timestamp = default;
            return false;
        }
    }

    public void UpdateLastSyncedLocation(double latitude, double longitude, DateTime timestampUtc)
    {
        lock (_syncLock)
        {
            LastSyncedLatitude = latitude;
            LastSyncedLongitude = longitude;
            LastSyncedTimestamp = timestampUtc;
            LastSyncTime = DateTime.UtcNow;
        }
    }

    public void ClearSyncReference()
    {
        lock (_syncLock)
        {
            LastSyncedLatitude = null;
            LastSyncedLongitude = null;
            LastSyncedTimestamp = null;
        }
    }

    #endregion

    #region Clear Methods

    public void Clear()
    {
        // Reset to defaults
        ServerUrl = null;
        ApiToken = null;
        IsFirstRun = true;
        TimelineTrackingEnabled = false;
        BackgroundTrackingEnabled = false;
        LastSyncTime = null;
        ClearSyncReference();
    }

    public void ClearAuth()
    {
        ServerUrl = null;
        ApiToken = null;
    }

    public void ResetToDefaults()
    {
        IsFirstRun = false;
        TimelineTrackingEnabled = false;
        BackgroundTrackingEnabled = false;
        ServerUrl = null;
        ApiToken = null;
        LocationTimeThresholdMinutes = 5;
        LocationDistanceThresholdMeters = 100;
        MaxConcurrentTileDownloads = 2;
        MinTileRequestDelayMs = 100;
        MaxLiveCacheSizeMB = 500;
        MaxTripCacheSizeMB = 2000;
        TileServerUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        LiveCachePrefetchRadius = 5;
        PrefetchDistanceThresholdMeters = 500;
        NavigationAudioEnabled = true;
        NavigationVolume = 0.8f;
        NavigationLanguage = string.Empty;
        NavigationVibrationEnabled = true;
        AutoRerouteEnabled = true;
        DistanceUnits = "kilometers";
        LastTransportMode = "driving";
        LastSelectedGroupId = null;
        LastSelectedGroupName = null;
        GroupsLegendExpanded = true;
        ThemePreference = "System";
        KeepScreenOn = false;
        LanguagePreference = "System";
        MapOfflineCacheEnabled = true;
        ShowBatteryWarnings = true;
        AutoPauseTrackingOnCriticalBattery = true;
        VisitNotificationsEnabled = false;
        VisitNotificationStyle = "notification";
        VisitVoiceAnnouncementEnabled = false;
        LastSyncTime = null;
        ClearSyncReference();
    }

    #endregion
}
