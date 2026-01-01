namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for checking tile cache status at a location.
/// Subscribes to LocationBridge and updates cache status when location changes.
/// Uses user-configured prefetch radius from settings.
/// </summary>
public interface ICacheStatusService
{
    /// <summary>
    /// Event raised when cache status changes ("green", "yellow", or "red").
    /// </summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Gets the current cache status ("green", "yellow", or "red").
    /// </summary>
    string CurrentStatus { get; }

    /// <summary>
    /// Gets the last detailed cache info (may be null if not yet checked).
    /// </summary>
    DetailedCacheInfo? LastDetailedInfo { get; }

    /// <summary>
    /// Forces an immediate cache status refresh.
    /// Call this after tile downloads complete.
    /// </summary>
    Task ForceRefreshAsync();

    /// <summary>
    /// Gets detailed cache information for current location.
    /// Call this when user taps the cache indicator.
    /// Always does a fresh scan and updates the quick status indicator.
    /// </summary>
    /// <returns>Detailed cache information.</returns>
    Task<DetailedCacheInfo> GetDetailedCacheInfoAsync();

    /// <summary>
    /// Gets detailed cache information for a specific location.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    /// <returns>Detailed cache information.</returns>
    Task<DetailedCacheInfo> GetDetailedCacheInfoAsync(double latitude, double longitude);

    /// <summary>
    /// Formats cache status for display in alert.
    /// </summary>
    /// <param name="info">The detailed cache info to format.</param>
    /// <returns>Formatted status message.</returns>
    string FormatStatusMessage(DetailedCacheInfo info);
}

/// <summary>
/// Detailed cache information for a location.
/// </summary>
public class DetailedCacheInfo
{
    /// <summary>
    /// Gets or sets the status text ("Excellent", "Good", "Partial", "Poor", "None", "Error").
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Gets or sets the total number of cached tiles.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the total number of tiles checked.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of tiles from live cache.
    /// </summary>
    public int LiveCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the number of tiles from trip cache.
    /// </summary>
    public int TripCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the total size of cached tiles in bytes.
    /// </summary>
    public long LocalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the per-zoom level coverage details.
    /// </summary>
    public List<ZoomLevelCoverage> ZoomLevelDetails { get; set; } = new();

    /// <summary>
    /// Gets or sets when this info was last updated.
    /// </summary>
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Cache coverage for a specific zoom level.
/// </summary>
public class ZoomLevelCoverage
{
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public int ZoomLevel { get; set; }

    /// <summary>
    /// Gets or sets the total tiles at this zoom level.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the cached tiles at this zoom level.
    /// </summary>
    public int CachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the live cache tiles at this zoom level.
    /// </summary>
    public int LiveCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the trip cache tiles at this zoom level.
    /// </summary>
    public int TripCachedTiles { get; set; }

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    public double CoveragePercentage { get; set; }

    /// <summary>
    /// Gets or sets the primary cache source ("Live", "Trip", or "None").
    /// </summary>
    public string PrimaryCacheSource { get; set; } = "None";
}
