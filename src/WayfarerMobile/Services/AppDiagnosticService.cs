using System.Text;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;
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
    private readonly DatabaseService _databaseService;
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
        DatabaseService databaseService,
        LiveTileCacheService liveTileCache,
        IPermissionsService permissionsService,
        RouteCacheService routeCacheService)
    {
        _logger = logger;
        _locationBridge = locationBridge;
        _settingsService = settingsService;
        _databaseService = databaseService;
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
            var pendingCount = await _databaseService.GetPendingLocationCountAsync();
            var syncedCount = await _databaseService.GetSyncedLocationCountAsync();
            var rejectedCount = await _databaseService.GetRejectedLocationCountAsync();
            var oldestPending = await _databaseService.GetOldestPendingLocationAsync();
            var lastSynced = await _databaseService.GetLastSyncedLocationAsync();

            return new LocationQueueDiagnostics
            {
                PendingCount = pendingCount,
                SyncedCount = syncedCount,
                RejectedCount = rejectedCount,
                TotalCount = pendingCount + syncedCount + rejectedCount,
                OldestPendingTimestamp = oldestPending?.Timestamp,
                LastSyncedTimestamp = lastSynced?.LastSyncAttempt,
                QueueHealthStatus = CalculateQueueHealth(pendingCount, rejectedCount),
                IsTrackingEnabled = _settingsService.TimelineTrackingEnabled,
                IsServerConfigured = _settingsService.IsConfigured
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location queue diagnostics");
            return new LocationQueueDiagnostics { QueueHealthStatus = "Error" };
        }
    }

    private static string CalculateQueueHealth(int pending, int rejected)
    {
        if (rejected > 50) return "Warning"; // Many rejections may indicate threshold issues
        if (pending > 1000) return "Warning"; // Large backlog
        return "Healthy";
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

            // Get trip tile cache info from database
            var tripTileCount = await _databaseService.GetTripTileCountAsync();
            var tripCacheSize = await _databaseService.GetTripCacheSizeAsync();
            var downloadedTrips = await _databaseService.GetDownloadedTripsAsync();

            return new TileCacheDiagnostics
            {
                LiveCacheTileCount = liveTileCount,
                LiveCacheSizeBytes = liveCacheSize,
                LiveCacheSizeMB = liveCacheSize / (1024.0 * 1024.0),
                LiveCacheMaxSizeMB = _settingsService.MaxLiveCacheSizeMB,
                LiveCacheUsagePercent = _settingsService.MaxLiveCacheSizeMB > 0
                    ? (liveCacheSize / (1024.0 * 1024.0)) / _settingsService.MaxLiveCacheSizeMB * 100
                    : 0,
                TripCacheTileCount = tripTileCount,
                TripCacheSizeBytes = tripCacheSize,
                TripCacheSizeMB = tripCacheSize / (1024.0 * 1024.0),
                TripCacheMaxSizeMB = _settingsService.MaxTripCacheSizeMB,
                DownloadedTripCount = downloadedTrips.Count,
                DownloadedTrips = downloadedTrips.Select(t => new TripCacheInfo
                {
                    TripId = t.ServerId.ToString(),
                    Name = t.Name,
                    Status = t.Status,
                    DownloadedAt = t.DownloadedAt
                }).ToList(),
                TotalCacheSizeMB = (liveCacheSize + tripCacheSize) / (1024.0 * 1024.0),
                CacheHealthStatus = CalculateCacheHealth(
                    liveCacheSize / (1024.0 * 1024.0),
                    _settingsService.MaxLiveCacheSizeMB)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tile cache diagnostics");
            return new TileCacheDiagnostics { CacheHealthStatus = "Error" };
        }
    }

    /// <summary>
    /// Gets cache coverage status for current location.
    /// </summary>
    public async Task<CacheCoverageInfo> GetCacheCoverageAsync(double latitude, double longitude)
    {
        try
        {
            var zoomLevels = TileCacheConstants.AllZoomLevels;
            var coverageByZoom = new Dictionary<int, ZoomLevelCoverage>();

            foreach (var zoom in zoomLevels)
            {
                var coverage = await CalculateZoomLevelCoverageAsync(latitude, longitude, zoom);
                coverageByZoom[zoom] = coverage;
            }

            var totalTiles = coverageByZoom.Values.Sum(c => c.TotalTiles);
            var cachedTiles = coverageByZoom.Values.Sum(c => c.CachedTiles);
            var overallCoverage = totalTiles > 0 ? (double)cachedTiles / totalTiles : 0;

            return new CacheCoverageInfo
            {
                Latitude = latitude,
                Longitude = longitude,
                CoverageByZoom = coverageByZoom,
                OverallCoveragePercent = overallCoverage * 100,
                TotalTilesNeeded = totalTiles,
                TotalTilesCached = cachedTiles,
                CoverageStatus = overallCoverage >= 0.9 ? "Excellent"
                    : overallCoverage >= 0.5 ? "Good"
                    : overallCoverage >= 0.2 ? "Partial"
                    : "Poor"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache coverage");
            return new CacheCoverageInfo { CoverageStatus = "Error" };
        }
    }

    private async Task<ZoomLevelCoverage> CalculateZoomLevelCoverageAsync(double lat, double lon, int zoom)
    {
        var (centerX, centerY) = LatLonToTile(lat, lon, zoom);
        const int radius = 3; // Check 7x7 grid

        int totalTiles = 0;
        int cachedTiles = 0;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (x < 0 || y < 0 || y >= (1 << zoom))
                    continue;

                totalTiles++;
                var tile = await _liveTileCache.GetCachedTileAsync(zoom, x, y);
                if (tile != null)
                    cachedTiles++;
            }
        }

        return new ZoomLevelCoverage
        {
            ZoomLevel = zoom,
            TotalTiles = totalTiles,
            CachedTiles = cachedTiles,
            CoveragePercent = totalTiles > 0 ? (double)cachedTiles / totalTiles * 100 : 0
        };
    }

    private static (int x, int y) LatLonToTile(double lat, double lon, int zoom)
    {
        int x = (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));
        return (x, y);
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
                TrackingHealthStatus = CalculateTrackingHealth(hasForeground, hasBackground, _locationBridge.CurrentState.ToString())
            };
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
        report.AppendLine($"  Trip Cache: {cacheDiag.TripCacheTileCount} tiles ({cacheDiag.TripCacheSizeMB:F1} MB)");
        report.AppendLine($"  Downloaded Trips: {cacheDiag.DownloadedTripCount}");
        foreach (var trip in cacheDiag.DownloadedTrips.Take(5))
            report.AppendLine($"    - {trip.Name} ({trip.Status})");

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
    public int TripCacheTileCount { get; set; }
    public long TripCacheSizeBytes { get; set; }
    public double TripCacheSizeMB { get; set; }
    public int TripCacheMaxSizeMB { get; set; }
    public int DownloadedTripCount { get; set; }
    public List<TripCacheInfo> DownloadedTrips { get; set; } = new();
    public double TotalCacheSizeMB { get; set; }
    public string CacheHealthStatus { get; set; } = "Unknown";
}

/// <summary>
/// Trip cache information.
/// </summary>
public class TripCacheInfo
{
    public string TripId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
}

/// <summary>
/// Cache coverage information for a location.
/// </summary>
public class CacheCoverageInfo
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public Dictionary<int, ZoomLevelCoverage> CoverageByZoom { get; set; } = new();
    public double OverallCoveragePercent { get; set; }
    public int TotalTilesNeeded { get; set; }
    public int TotalTilesCached { get; set; }
    public string CoverageStatus { get; set; } = "Unknown";
}

/// <summary>
/// Zoom level coverage information.
/// </summary>
public class ZoomLevelCoverage
{
    public int ZoomLevel { get; set; }
    public int TotalTiles { get; set; }
    public int CachedTiles { get; set; }
    public double CoveragePercent { get; set; }
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
