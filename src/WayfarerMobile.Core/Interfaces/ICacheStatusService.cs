using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for checking cache status and managing cache overlay visualization.
/// </summary>
public interface ICacheStatusService
{
    /// <summary>
    /// Gets whether the cache overlay is currently visible on the map.
    /// </summary>
    bool IsOverlayVisible { get; }

    /// <summary>
    /// Gets a quick cache status summary for a location.
    /// Used for the header indicator display.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    /// <returns>Quick status summary with coverage and indicator color.</returns>
    Task<CacheStatusSummaryResult> GetQuickStatusAsync(double latitude, double longitude);

    /// <summary>
    /// Gets detailed cache status information for a location.
    /// Includes per-zoom coverage breakdown and cache source details.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    /// <returns>Detailed cache status information.</returns>
    Task<DetailedCacheStatusResult> GetDetailedStatusAsync(double latitude, double longitude);

    /// <summary>
    /// Toggles the cache overlay visualization on the map.
    /// </summary>
    /// <param name="latitude">Current location latitude.</param>
    /// <param name="longitude">Current location longitude.</param>
    /// <returns>True if overlay is now visible, false if hidden.</returns>
    Task<bool> ToggleOverlayAsync(double latitude, double longitude);

    /// <summary>
    /// Shows the cache overlay at the specified location.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    Task ShowOverlayAsync(double latitude, double longitude);

    /// <summary>
    /// Hides the cache overlay from the map.
    /// </summary>
    void HideOverlay();

    /// <summary>
    /// Invalidates all cached status results.
    /// Call when tiles are downloaded or deleted to force a fresh check.
    /// </summary>
    void InvalidateCache();
}

/// <summary>
/// Quick cache status summary result from the service.
/// </summary>
public class CacheStatusSummaryResult
{
    /// <summary>
    /// Gets or sets the coverage status.
    /// </summary>
    public CacheCoverageStatus Status { get; set; } = CacheCoverageStatus.Unknown;

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    /// Gets or sets whether network is available.
    /// </summary>
    public bool HasNetwork { get; set; }

    /// <summary>
    /// Gets or sets the tooltip text.
    /// </summary>
    public string TooltipText { get; set; } = "Cache";
}

/// <summary>
/// Detailed cache status result from the service.
/// </summary>
public class DetailedCacheStatusResult
{
    /// <summary>
    /// Gets or sets the coverage status.
    /// </summary>
    public CacheCoverageStatus Status { get; set; } = CacheCoverageStatus.Unknown;

    /// <summary>
    /// Gets or sets the overall coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    /// Gets or sets the total tiles checked.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the cached tiles count.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the live cache tiles count.
    /// </summary>
    public int LiveCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the trip cache tiles count.
    /// </summary>
    public int TripCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the local area cache size in bytes.
    /// </summary>
    public long LocalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total live cache size in bytes.
    /// </summary>
    public long LiveCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total trip cache size in bytes.
    /// </summary>
    public long TripCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total app cache size in bytes.
    /// </summary>
    public long TotalAppSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total live tile count.
    /// </summary>
    public int LiveTileCount { get; set; }

    /// <summary>
    /// Gets or sets the active trip name if in trip area.
    /// </summary>
    public string? ActiveTripName { get; set; }

    /// <summary>
    /// Gets or sets the number of downloaded trips.
    /// </summary>
    public int DownloadedTripCount { get; set; }

    /// <summary>
    /// Gets or sets the coverage per zoom level.
    /// </summary>
    public List<ZoomCoverageResult> ZoomCoverage { get; set; } = new();

    /// <summary>
    /// Gets or sets when this status was checked.
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Gets or sets whether network is available.
    /// </summary>
    public bool HasNetwork { get; set; }
}

/// <summary>
/// Zoom level coverage result.
/// </summary>
public class ZoomCoverageResult
{
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// Gets or sets the total tiles at this zoom.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the cached tiles at this zoom.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the live cache tiles at this zoom.
    /// </summary>
    public int LiveTiles { get; set; }

    /// <summary>
    /// Gets or sets the trip cache tiles at this zoom.
    /// </summary>
    public int TripTiles { get; set; }

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercent { get; set; }
}
