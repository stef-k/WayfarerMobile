using System.Text;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for app-specific diagnostics including location queue, tile cache,
/// tracking status, and navigation state.
/// </summary>
public class AppDiagnosticService
{
    private readonly ILogger<AppDiagnosticService> _logger;
    private readonly ILocationBridge _locationBridge;
    private readonly ISettingsService _settingsService;
    private readonly ILocationQueueRepository _locationQueueRepository;
    private readonly LiveTileCacheService _liveTileCache;
    private readonly IPermissionsService _permissionsService;
    private readonly RouteCacheService _routeCacheService;

    /// <summary>
    /// Initializes a new instance of the AppDiagnosticService class.
    /// </summary>
    public AppDiagnosticService(
        ILogger<AppDiagnosticService> logger,
        ILocationBridge locationBridge,
        ISettingsService settingsService,
        ILocationQueueRepository locationQueueRepository,
        LiveTileCacheService liveTileCache,
        IPermissionsService permissionsService,
        RouteCacheService routeCacheService)
    {
        _logger = logger;
        _locationBridge = locationBridge;
        _settingsService = settingsService;
        _locationQueueRepository = locationQueueRepository;
        _liveTileCache = liveTileCache;
        _permissionsService = permissionsService;
        _routeCacheService = routeCacheService;
    }

    #region Location Queue Diagnostics

    /// <summary>
    /// Gets diagnostics for the location sync queue.
    /// </summary>
    public async Task<LocationQueueDiagnostics> GetLocationQueueDiagnosticsAsync()
    {
        try
        {
            var pendingCount = await _locationQueueRepository.GetPendingCountAsync();
            var retryingCount = await _locationQueueRepository.GetRetryingCountAsync();
            var syncedCount = await _locationQueueRepository.GetSyncedLocationCountAsync();
            var rejectedCount = await _locationQueueRepository.GetRejectedLocationCountAsync();
            var oldestPending = await _locationQueueRepository.GetOldestPendingLocationAsync();
            var lastSynced = await _locationQueueRepository.GetLastSyncedLocationAsync();

            return new LocationQueueDiagnostics
            {
                PendingCount = pendingCount,
                RetryingCount = retryingCount,
                SyncedCount = syncedCount,
                RejectedCount = rejectedCount,
                TotalCount = pendingCount + syncedCount + rejectedCount,
                OldestPendingTimestamp = oldestPending?.Timestamp,
                LastSyncedTimestamp = lastSynced?.LastSyncAttempt,
                QueueHealthStatus = CalculateQueueHealth(pendingCount),
                IsTrackingEnabled = _settingsService.TimelineTrackingEnabled,
                IsServerConfigured = _settingsService.IsConfigured
            };
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting location queue diagnostics");
            return new LocationQueueDiagnostics { QueueHealthStatus = "Database Error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location queue diagnostics");
            return new LocationQueueDiagnostics { QueueHealthStatus = "Error" };
        }
    }

    private static string CalculateQueueHealth(int pending)
    {
        if (pending > 1000) return "Warning"; // Large backlog
        return "Healthy";
    }

    /// <summary>
    /// Gets comprehensive queue status for Settings display.
    /// </summary>
    public async Task<QueueStatusInfo> GetQueueStatusAsync()
    {
        var totalCount = await _locationQueueRepository.GetTotalCountAsync();
        var queueLimit = _settingsService.QueueLimitMaxLocations;

        var allPendingCount = await _locationQueueRepository.GetPendingCountAsync();
        var retryingCount = await _locationQueueRepository.GetRetryingCountAsync();
        var syncingCount = await _locationQueueRepository.GetSyncingCountAsync();
        var syncedCount = await _locationQueueRepository.GetSyncedLocationCountAsync();
        var rejectedCount = await _locationQueueRepository.GetRejectedLocationCountAsync();

        var pendingCount = allPendingCount - retryingCount;

        var oldestPending = await _locationQueueRepository.GetOldestPendingLocationAsync();
        var newestPending = await _locationQueueRepository.GetNewestPendingLocationAsync();
        var lastSynced = await _locationQueueRepository.GetLastSyncedLocationAsync();

        return new QueueStatusInfo
        {
            TotalCount = totalCount,
            PendingCount = pendingCount,
            RetryingCount = retryingCount,
            SyncingCount = syncingCount,
            SyncedCount = syncedCount,
            RejectedCount = rejectedCount,
            QueueLimit = queueLimit,
            OldestPendingTimestamp = oldestPending?.Timestamp,
            NewestPendingTimestamp = newestPending?.Timestamp,
            LastSyncedTimestamp = lastSynced?.Timestamp,
            UsagePercent = queueLimit > 0 ? (double)totalCount / queueLimit * 100 : 0
        };
    }

    #endregion

    #region Tile Cache Diagnostics

    /// <summary>
    /// Gets diagnostics for all tile caches.
    /// </summary>
    public async Task<TileCacheDiagnostics> GetTileCacheDiagnosticsAsync()
    {
        try
        {
            var liveTileCount = await _liveTileCache.GetTotalCachedFilesAsync();
            var liveCacheSize = await _liveTileCache.GetTotalCacheSizeBytesAsync();

            return new TileCacheDiagnostics
            {
                LiveCacheTileCount = liveTileCount,
                LiveCacheSizeBytes = liveCacheSize,
                LiveCacheSizeMB = liveCacheSize / (1024.0 * 1024.0),
                LiveCacheMaxSizeMB = _settingsService.MaxLiveCacheSizeMB,
                LiveCacheUsagePercent = _settingsService.MaxLiveCacheSizeMB > 0
                    ? (liveCacheSize / (1024.0 * 1024.0)) / _settingsService.MaxLiveCacheSizeMB * 100
                    : 0,
                CacheHealthStatus = CalculateCacheHealth(
                    liveCacheSize / (1024.0 * 1024.0),
                    _settingsService.MaxLiveCacheSizeMB)
            };
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting tile cache diagnostics");
            return new TileCacheDiagnostics { CacheHealthStatus = "Database Error" };
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File I/O error getting tile cache diagnostics");
            return new TileCacheDiagnostics { CacheHealthStatus = "File Error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tile cache diagnostics");
            return new TileCacheDiagnostics { CacheHealthStatus = "Error" };
        }
    }

    private static string CalculateCacheHealth(double currentSizeMB, int maxSizeMB)
    {
        if (maxSizeMB <= 0) return "Unknown";
        var usage = currentSizeMB / maxSizeMB;
        if (usage >= 0.95) return "Full";
        if (usage >= 0.8) return "Warning";
        return "Healthy";
    }

    #endregion

    #region Tracking Diagnostics

    /// <summary>
    /// Gets comprehensive tracking diagnostics.
    /// </summary>
    public async Task<TrackingDiagnostics> GetTrackingDiagnosticsAsync()
    {
        try
        {
            var hasForeground = await _permissionsService.IsLocationPermissionGrantedAsync();
            var hasBackground = await _permissionsService.IsBackgroundLocationPermissionGrantedAsync();
            var lastLocation = _locationBridge.LastLocation;

            return new TrackingDiagnostics
            {
                HasForegroundPermission = hasForeground,
                HasBackgroundPermission = hasBackground,
                TrackingState = _locationBridge.CurrentState.ToString(),
                PerformanceMode = _locationBridge.CurrentMode.ToString(),
                IsTrackingEnabled = _settingsService.TimelineTrackingEnabled,
                LastLocationTimestamp = lastLocation?.Timestamp,
                LastLocationAccuracy = lastLocation?.Accuracy,
                LastLocationLatitude = lastLocation?.Latitude,
                LastLocationLongitude = lastLocation?.Longitude,
                TimeThresholdMinutes = _settingsService.LocationTimeThresholdMinutes,
                DistanceThresholdMeters = _settingsService.LocationDistanceThresholdMeters,
                AccuracyThresholdMeters = _settingsService.LocationAccuracyThresholdMeters,
                TrackingHealthStatus = CalculateTrackingHealth(hasForeground, hasBackground, _locationBridge.CurrentState.ToString())
            };
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Feature not supported getting tracking diagnostics");
            return new TrackingDiagnostics { TrackingHealthStatus = "Not Supported" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tracking diagnostics");
            return new TrackingDiagnostics { TrackingHealthStatus = "Error" };
        }
    }

    private static string CalculateTrackingHealth(bool foreground, bool background, string state)
    {
        if (!foreground) return "Critical";
        if (!background) return "Warning";
        if (state == "Active") return "Healthy";
        return "Idle";
    }

    #endregion

    #region Navigation Diagnostics

    /// <summary>
    /// Gets navigation and route cache diagnostics.
    /// Note: Route cache doesn't expose raw cached route - only validates on retrieval.
    /// </summary>
    public Task<NavigationDiagnostics> GetNavigationDiagnosticsAsync()
    {
        try
        {
            // Note: RouteCacheService only validates and returns routes via GetValidRoute()
            // which requires current location and destination. For diagnostics we just
            // report that route caching is available.
            return Task.FromResult(new NavigationDiagnostics
            {
                HasCachedRoute = false, // Cannot determine without location context
                CachedRouteDestination = null,
                CachedRouteWaypointCount = 0,
                CachedRouteDistance = null,
                CachedRouteDuration = null,
                CachedRouteTimestamp = null,
                CacheAgeSeconds = 0,
                IsCacheValid = false
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid state getting navigation diagnostics");
            return Task.FromResult(new NavigationDiagnostics());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting navigation diagnostics");
            return Task.FromResult(new NavigationDiagnostics());
        }
    }

    #endregion

    #region Full Report

    /// <summary>
    /// Generates a comprehensive diagnostic report.
    /// </summary>
    public async Task<string> GenerateFullReportAsync()
    {
        var report = new StringBuilder();

        report.AppendLine("WAYFARER APP DIAGNOSTIC REPORT");
        report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine(new string('=', 60));

        // Location Queue
        var queueDiag = await GetLocationQueueDiagnosticsAsync();
        report.AppendLine("\nLOCATION QUEUE:");
        report.AppendLine($"  Status: {queueDiag.QueueHealthStatus}");
        report.AppendLine($"  Pending: {queueDiag.PendingCount}");
        report.AppendLine($"  Retrying: {queueDiag.RetryingCount}");
        report.AppendLine($"  Synced: {queueDiag.SyncedCount}");
        report.AppendLine($"  Rejected: {queueDiag.RejectedCount}");
        if (queueDiag.OldestPendingTimestamp.HasValue)
            report.AppendLine($"  Oldest Pending: {queueDiag.OldestPendingTimestamp:g}");
        if (queueDiag.LastSyncedTimestamp.HasValue)
            report.AppendLine($"  Last Synced: {queueDiag.LastSyncedTimestamp:g}");

        // Tile Cache
        var cacheDiag = await GetTileCacheDiagnosticsAsync();
        report.AppendLine("\nTILE CACHE:");
        report.AppendLine($"  Status: {cacheDiag.CacheHealthStatus}");
        report.AppendLine($"  Live Cache: {cacheDiag.LiveCacheTileCount} tiles ({cacheDiag.LiveCacheSizeMB:F1} MB / {cacheDiag.LiveCacheMaxSizeMB} MB)");

        // Tracking
        var trackingDiag = await GetTrackingDiagnosticsAsync();
        report.AppendLine("\nTRACKING:");
        report.AppendLine($"  Status: {trackingDiag.TrackingHealthStatus}");
        report.AppendLine($"  State: {trackingDiag.TrackingState}");
        report.AppendLine($"  Mode: {trackingDiag.PerformanceMode}");
        report.AppendLine($"  Foreground Permission: {(trackingDiag.HasForegroundPermission ? "OK" : "MISSING")}");
        report.AppendLine($"  Background Permission: {(trackingDiag.HasBackgroundPermission ? "OK" : "MISSING")}");
        if (trackingDiag.LastLocationTimestamp.HasValue)
        {
            report.AppendLine($"  Last Location: {trackingDiag.LastLocationLatitude:F6}, {trackingDiag.LastLocationLongitude:F6}");
            report.AppendLine($"  Last Location Time: {trackingDiag.LastLocationTimestamp:HH:mm:ss}");
            report.AppendLine($"  Accuracy: {trackingDiag.LastLocationAccuracy:F1}m");
        }

        // Navigation
        var navDiag = await GetNavigationDiagnosticsAsync();
        report.AppendLine("\nNAVIGATION:");
        report.AppendLine($"  Has Cached Route: {navDiag.HasCachedRoute}");
        if (navDiag.HasCachedRoute)
        {
            report.AppendLine($"  Destination: {navDiag.CachedRouteDestination}");
            report.AppendLine($"  Waypoints: {navDiag.CachedRouteWaypointCount}");
            report.AppendLine($"  Distance: {navDiag.CachedRouteDistance:F0}m");
            report.AppendLine($"  Cache Age: {navDiag.CacheAgeSeconds:F0}s");
            report.AppendLine($"  Cache Valid: {navDiag.IsCacheValid}");
        }

        report.AppendLine(new string('=', 60));
        return report.ToString();
    }

    #endregion
}

#region Diagnostic Models

/// <summary>
/// Location queue diagnostic information.
/// </summary>
public class LocationQueueDiagnostics
{
    public int PendingCount { get; set; }
    public int RetryingCount { get; set; }
    public int SyncedCount { get; set; }
    public int RejectedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTime? OldestPendingTimestamp { get; set; }
    public DateTime? LastSyncedTimestamp { get; set; }
    public string QueueHealthStatus { get; set; } = "Unknown";
    public bool IsTrackingEnabled { get; set; }
    public bool IsServerConfigured { get; set; }
}

/// <summary>
/// Tile cache diagnostic information.
/// </summary>
public class TileCacheDiagnostics
{
    public int LiveCacheTileCount { get; set; }
    public long LiveCacheSizeBytes { get; set; }
    public double LiveCacheSizeMB { get; set; }
    public int LiveCacheMaxSizeMB { get; set; }
    public double LiveCacheUsagePercent { get; set; }
    public string CacheHealthStatus { get; set; } = "Unknown";
}

/// <summary>
/// Tracking diagnostic information.
/// </summary>
public class TrackingDiagnostics
{
    public bool HasForegroundPermission { get; set; }
    public bool HasBackgroundPermission { get; set; }
    public string TrackingState { get; set; } = "Unknown";
    public string PerformanceMode { get; set; } = "Unknown";
    public bool IsTrackingEnabled { get; set; }
    public DateTimeOffset? LastLocationTimestamp { get; set; }
    public double? LastLocationAccuracy { get; set; }
    public double? LastLocationLatitude { get; set; }
    public double? LastLocationLongitude { get; set; }
    public int TimeThresholdMinutes { get; set; }
    public int DistanceThresholdMeters { get; set; }
    public int AccuracyThresholdMeters { get; set; }
    public string TrackingHealthStatus { get; set; } = "Unknown";
}

/// <summary>
/// Navigation diagnostic information.
/// </summary>
public class NavigationDiagnostics
{
    public bool HasCachedRoute { get; set; }
    public string? CachedRouteDestination { get; set; }
    public int CachedRouteWaypointCount { get; set; }
    public double? CachedRouteDistance { get; set; }
    public double? CachedRouteDuration { get; set; }
    public DateTime? CachedRouteTimestamp { get; set; }
    public double CacheAgeSeconds { get; set; }
    public bool IsCacheValid { get; set; }
}

#endregion
