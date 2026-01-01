using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Enforces cache size limits for trip tile downloads.
/// Provides limit checking, quota estimation, and threshold notifications.
/// </summary>
/// <remarks>
/// This service focuses on cache quota management:
/// - Cache size monitoring and limit checking
/// - Download size estimation
/// - Quota availability checking for new downloads
/// - Warning/critical/limit threshold events
///
/// Does NOT handle:
/// - Actual tile downloading (use ITileDownloadService)
/// - Download state management (use IDownloadStateManager)
/// - Download orchestration (use TripDownloadService)
/// </remarks>
public interface ICacheLimitEnforcer
{
    #region Constants

    /// <summary>
    /// Gets the estimated size per tile in bytes.
    /// Used for download size estimation.
    /// </summary>
    long EstimatedTileSizeBytes { get; }

    /// <summary>
    /// Gets the warning threshold percentage (default: 80%).
    /// </summary>
    double WarningThresholdPercent { get; }

    /// <summary>
    /// Gets the critical threshold percentage (default: 90%).
    /// </summary>
    double CriticalThresholdPercent { get; }

    #endregion

    #region Events

    /// <summary>
    /// Raised when cache usage reaches warning level (80%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <summary>
    /// Raised when cache usage reaches critical level (90%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <summary>
    /// Raised when cache limit is reached (100%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    #endregion

    #region Limit Checking

    /// <summary>
    /// Checks the current trip cache limit status.
    /// </summary>
    /// <returns>Result with current usage and limit status.</returns>
    Task<CacheLimitCheckResult> CheckLimitAsync();

    /// <summary>
    /// Gets a cached limit check result to avoid frequent database hits.
    /// Returns cached result if checked within the cache window (2 seconds).
    /// Thread-safe for parallel download scenarios.
    /// </summary>
    /// <returns>Cached or fresh limit check result.</returns>
    Task<CacheLimitCheckResult> GetCachedLimitCheckAsync();

    /// <summary>
    /// Invalidates the cached limit check, forcing a fresh check on next call.
    /// </summary>
    void InvalidateCache();

    #endregion

    #region Size Estimation

    /// <summary>
    /// Estimates the download size for a given tile count.
    /// </summary>
    /// <param name="tileCount">Number of tiles to download.</param>
    /// <returns>Estimated size in bytes.</returns>
    long EstimateDownloadSize(int tileCount);

    /// <summary>
    /// Estimates the tile count for a bounding box.
    /// Uses the tile download service's calculation methods.
    /// </summary>
    /// <param name="boundingBox">The geographic bounding box.</param>
    /// <returns>Estimated number of tiles.</returns>
    int EstimateTileCount(BoundingBox boundingBox);

    #endregion

    #region Quota Checking

    /// <summary>
    /// Checks if there's enough cache quota for a download.
    /// </summary>
    /// <param name="estimatedBytes">Estimated download size in bytes.</param>
    /// <returns>Result with quota details.</returns>
    Task<CacheQuotaCheckResult> CheckQuotaAsync(long estimatedBytes);

    /// <summary>
    /// Checks if there's enough cache quota for a trip download.
    /// </summary>
    /// <param name="boundingBox">The trip's bounding box.</param>
    /// <returns>Result with quota details and tile count.</returns>
    Task<CacheQuotaCheckResult> CheckQuotaForTripAsync(BoundingBox boundingBox);

    #endregion

    #region Threshold Notifications

    /// <summary>
    /// Checks the current usage and raises appropriate events if thresholds are crossed.
    /// Call this periodically during downloads.
    /// </summary>
    /// <param name="tripId">The trip ID for event context.</param>
    /// <param name="tripName">The trip name for event context.</param>
    /// <returns>The limit check result.</returns>
    Task<CacheLimitCheckResult> CheckAndNotifyAsync(int tripId, string tripName);

    #endregion
}
