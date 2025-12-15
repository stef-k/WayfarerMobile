namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Centralized constants for tile cache configuration.
/// All tile-related services should use these constants for consistency.
/// </summary>
public static class TileCacheConstants
{
    /// <summary>
    /// Minimum zoom level for tile caching (zoomed out, region view).
    /// </summary>
    public const int MinZoomLevel = 8;

    /// <summary>
    /// Maximum zoom level for tile caching (zoomed in, street detail).
    /// </summary>
    public const int MaxZoomLevel = 17;

    /// <summary>
    /// All zoom levels for full tile operations (prefetch, full status check, trip download).
    /// Ordered by priority: most commonly used navigation zoom levels first.
    /// Range: 8-17 (10 zoom levels total).
    /// </summary>
    public static readonly int[] AllZoomLevels = { 15, 14, 16, 13, 12, 11, 10, 9, 8, 17 };

    /// <summary>
    /// Quick check zoom levels for fast cache status verification.
    /// Only checks the most commonly viewed zoom levels.
    /// </summary>
    public static readonly int[] QuickCheckZoomLevels = { 15, 14, 16 };

    /// <summary>
    /// Estimated tile size in bytes for download calculations.
    /// Uses a conservative estimate weighted toward dense urban areas:
    /// - Dense urban: 50-80 KB
    /// - Suburban: 20-35 KB
    /// - Rural: 5-15 KB
    /// Value of 40 KB provides realistic worst-case estimates for storage planning.
    /// </summary>
    public const long EstimatedTileSizeBytes = 40000; // ~40KB (conservative, weighted toward urban)

    /// <summary>
    /// Default tile request timeout in milliseconds.
    /// </summary>
    public const int TileTimeoutMs = 10000;
}
