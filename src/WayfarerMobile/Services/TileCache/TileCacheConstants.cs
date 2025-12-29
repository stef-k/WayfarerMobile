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
    ///
    /// For zoom 8-17, tile counts grow exponentially with zoom level:
    /// - z17 alone is ~75% of all tiles in z8-z17 range
    /// - z16+z17 together are ~94% of all tiles
    ///
    /// This means the "overall average" is dominated by z16-z17 tile sizes.
    ///
    /// Typical OSM-style PNG tile sizes (256×256):
    /// - Dense urban z16-z17: 70-90 KB
    /// - Suburban: 20-40 KB
    /// - Rural/ocean: 5-15 KB
    /// - Global average (OSM ops): ~18 KB (includes all empty ocean/rural)
    ///
    /// Since trip downloads are typically urban/suburban areas, and z16-z17
    /// dominate the tile count, 80 KB provides a realistic estimate that
    /// won't under-promise storage requirements to users.
    /// </summary>
    public const long EstimatedTileSizeBytes = 80000; // ~80KB (realistic urban average, z16-z17 weighted)

    /// <summary>
    /// Default tile request timeout in milliseconds.
    /// </summary>
    public const int TileTimeoutMs = 10000;
}
