using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Detailed cache status information for a specific location.
/// Includes coverage breakdown by zoom level and cache source.
/// </summary>
public class DetailedCacheStatus
{
    /// <summary>
    /// Gets or sets the overall coverage status.
    /// </summary>
    public CacheCoverageStatus Status { get; set; } = CacheCoverageStatus.Unknown;

    /// <summary>
    /// Gets or sets the overall coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    /// Gets or sets the total number of tiles checked.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of cached tiles.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of tiles from live cache.
    /// </summary>
    public int LiveCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of tiles from trip cache.
    /// </summary>
    public int TripCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the size of cached tiles for current location.
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
    /// Gets or sets the active trip name if user is in trip area.
    /// </summary>
    public string? ActiveTripName { get; set; }

    /// <summary>
    /// Gets or sets the number of downloaded trips.
    /// </summary>
    public int DownloadedTripCount { get; set; }

    /// <summary>
    /// Gets or sets the coverage information per zoom level.
    /// </summary>
    public List<ZoomCoverageStatus> ZoomCoverage { get; set; } = new();

    /// <summary>
    /// Gets or sets when this status was checked.
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// Gets or sets whether network is available.
    /// </summary>
    public bool HasNetwork { get; set; }

    /// <summary>
    /// Gets the formatted total size string.
    /// </summary>
    public string FormattedTotalSize => FormatSize(TotalAppSizeBytes);

    /// <summary>
    /// Gets the formatted live cache size string.
    /// </summary>
    public string FormattedLiveSize => FormatSize(LiveCacheSizeBytes);

    /// <summary>
    /// Gets the formatted trip cache size string.
    /// </summary>
    public string FormattedTripSize => FormatSize(TripCacheSizeBytes);

    /// <summary>
    /// Gets the formatted local size string.
    /// </summary>
    public string FormattedLocalSize => FormatSize(LocalSizeBytes);

    /// <summary>
    /// Gets the formatted coverage percentage string.
    /// </summary>
    public string FormattedCoverage => $"{CoveragePercent:P0}";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

/// <summary>
/// Cache coverage status for a specific zoom level.
/// </summary>
public class ZoomCoverageStatus
{
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// Gets or sets the total tiles checked at this zoom.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of cached tiles at this zoom.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of live cache tiles.
    /// </summary>
    public int LiveTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of trip cache tiles.
    /// </summary>
    public int TripTiles { get; set; }

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    /// Gets the formatted zoom label (e.g., "Z15").
    /// </summary>
    public string ZoomLabel => $"Z{Zoom}";

    /// <summary>
    /// Gets the formatted coverage percentage string.
    /// </summary>
    public string FormattedPercent => $"{CoveragePercent:P0}";

    /// <summary>
    /// Gets the indicator color based on coverage.
    /// </summary>
    public Color IndicatorColor => CoveragePercent switch
    {
        >= 0.90 => Colors.LimeGreen,
        >= 0.50 => Colors.Gold,
        >= 0.20 => Colors.Orange,
        _ => Colors.Red
    };

    /// <summary>
    /// Gets the coverage status for this zoom level.
    /// </summary>
    public CacheCoverageStatus Status => CoveragePercent switch
    {
        >= 0.90 => CacheCoverageStatus.Excellent,
        >= 0.70 => CacheCoverageStatus.Good,
        >= 0.40 => CacheCoverageStatus.Partial,
        > 0 => CacheCoverageStatus.Poor,
        _ => CacheCoverageStatus.None
    };
}

/// <summary>
/// Quick cache status summary for header indicator.
/// </summary>
public class CacheStatusSummary
{
    /// <summary>
    /// Gets or sets the overall coverage status.
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
    /// Gets or sets the tooltip text for the indicator.
    /// </summary>
    public string TooltipText { get; set; } = "Cache";

    /// <summary>
    /// Gets the indicator color for the summary.
    /// </summary>
    public Color IndicatorColor => Status switch
    {
        CacheCoverageStatus.Excellent => Colors.LimeGreen,
        CacheCoverageStatus.Good => Colors.LimeGreen,
        CacheCoverageStatus.Partial => Colors.Orange,
        CacheCoverageStatus.Poor => Colors.Red,
        CacheCoverageStatus.None => Colors.Red,
        _ => Colors.Gray
    };
}
