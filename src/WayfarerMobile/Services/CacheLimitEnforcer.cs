using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Enforces cache size limits for trip tile downloads.
/// Provides limit checking, quota estimation, and threshold notifications.
/// </summary>
public sealed class CacheLimitEnforcer : ICacheLimitEnforcer
{
    private readonly ISettingsService _settingsService;
    private readonly DatabaseService _databaseService;
    private readonly ITileDownloadService _tileDownloadService;
    private readonly ILogger<CacheLimitEnforcer> _logger;

    // Cached limit check state for parallel download efficiency
    private readonly SemaphoreSlim _cacheLimitCheckLock = new(1, 1);
    private volatile CachedLimitState? _cachedLimitState;

    // Thresholds
    private const double DefaultWarningThreshold = 80.0;
    private const double DefaultCriticalThreshold = 90.0;
    private const double CacheWindowSeconds = 2.0;

    /// <inheritdoc/>
    public long EstimatedTileSizeBytes => TileCacheConstants.EstimatedTileSizeBytes;

    /// <inheritdoc/>
    public double WarningThresholdPercent => DefaultWarningThreshold;

    /// <inheritdoc/>
    public double CriticalThresholdPercent => DefaultCriticalThreshold;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    /// <summary>
    /// Creates a new instance of CacheLimitEnforcer.
    /// </summary>
    public CacheLimitEnforcer(
        ISettingsService settingsService,
        DatabaseService databaseService,
        ITileDownloadService tileDownloadService,
        ILogger<CacheLimitEnforcer> logger)
    {
        _settingsService = settingsService;
        _databaseService = databaseService;
        _tileDownloadService = tileDownloadService;
        _logger = logger;
    }

    #region Limit Checking

    /// <inheritdoc/>
    public async Task<CacheLimitCheckResult> CheckLimitAsync()
    {
        var currentSize = await _databaseService.GetTripCacheSizeAsync();
        var maxSizeMB = _settingsService.MaxTripCacheSizeMB;
        var maxSizeBytes = (long)maxSizeMB * 1024 * 1024;
        var currentSizeMB = currentSize / (1024.0 * 1024.0);
        var usagePercent = maxSizeBytes > 0 ? (currentSize * 100.0 / maxSizeBytes) : 0;

        return new CacheLimitCheckResult
        {
            CurrentSizeBytes = currentSize,
            CurrentSizeMB = currentSizeMB,
            MaxSizeMB = maxSizeMB,
            UsagePercent = usagePercent,
            IsLimitReached = currentSize >= maxSizeBytes,
            IsWarningLevel = usagePercent >= WarningThresholdPercent && usagePercent < 100
        };
    }

    /// <inheritdoc/>
    public async Task<CacheLimitCheckResult> GetCachedLimitCheckAsync()
    {
        // Fast path: single volatile read for atomic access
        var cachedState = _cachedLimitState;
        if (cachedState != null && (DateTime.UtcNow - cachedState.CheckTime).TotalSeconds < CacheWindowSeconds)
        {
            return cachedState.Result;
        }

        // Slow path: acquire lock and check
        await _cacheLimitCheckLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            cachedState = _cachedLimitState;
            if (cachedState != null && (DateTime.UtcNow - cachedState.CheckTime).TotalSeconds < CacheWindowSeconds)
            {
                return cachedState.Result;
            }

            // Actually perform the check
            var result = await CheckLimitAsync();
            // Atomic write via single reference assignment
            _cachedLimitState = new CachedLimitState(result, DateTime.UtcNow);
            return result;
        }
        finally
        {
            _cacheLimitCheckLock.Release();
        }
    }

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        _cachedLimitState = null;
    }

    #endregion

    #region Size Estimation

    /// <inheritdoc/>
    public long EstimateDownloadSize(int tileCount)
    {
        return tileCount * EstimatedTileSizeBytes;
    }

    /// <inheritdoc/>
    public int EstimateTileCount(BoundingBox boundingBox)
    {
        var maxZoom = _tileDownloadService.GetRecommendedMaxZoom(boundingBox);
        var tiles = _tileDownloadService.CalculateTilesForBoundingBox(
            boundingBox,
            TileCacheConstants.MinZoomLevel,
            maxZoom);
        return tiles.Count;
    }

    #endregion

    #region Quota Checking

    /// <inheritdoc/>
    public async Task<CacheQuotaCheckResult> CheckQuotaAsync(long estimatedBytes)
    {
        var limitResult = await CheckLimitAsync();
        var maxSizeBytes = (long)limitResult.MaxSizeMB * 1024 * 1024;
        var availableBytes = maxSizeBytes - limitResult.CurrentSizeBytes;
        var estimatedMB = estimatedBytes / (1024.0 * 1024.0);
        var availableMB = availableBytes / (1024.0 * 1024.0);

        return new CacheQuotaCheckResult
        {
            EstimatedSizeBytes = estimatedBytes,
            EstimatedSizeMB = estimatedMB,
            AvailableBytes = availableBytes,
            AvailableMB = availableMB,
            CurrentUsageMB = limitResult.CurrentSizeMB,
            MaxSizeMB = limitResult.MaxSizeMB,
            HasSufficientQuota = availableBytes >= estimatedBytes,
            WouldExceedBy = estimatedBytes > availableBytes
                ? (estimatedBytes - availableBytes) / (1024.0 * 1024.0)
                : 0
        };
    }

    /// <inheritdoc/>
    public async Task<CacheQuotaCheckResult> CheckQuotaForTripAsync(BoundingBox boundingBox)
    {
        var tileCount = EstimateTileCount(boundingBox);
        var estimatedBytes = EstimateDownloadSize(tileCount);
        var result = await CheckQuotaAsync(estimatedBytes);
        return result with { TileCount = tileCount };
    }

    #endregion

    #region Threshold Notifications

    /// <inheritdoc/>
    public async Task<CacheLimitCheckResult> CheckAndNotifyAsync(int tripId, string tripName)
    {
        var result = await GetCachedLimitCheckAsync();

        var eventArgs = new CacheLimitEventArgs
        {
            TripId = tripId,
            TripName = tripName,
            CurrentUsageMB = result.CurrentSizeMB,
            MaxSizeMB = result.MaxSizeMB,
            UsagePercent = result.UsagePercent
        };

        if (result.IsLimitReached)
        {
            eventArgs = eventArgs with { Level = CacheLimitLevel.LimitReached };
            RaiseEventSafe(CacheLimitReached, eventArgs);
            _logger.LogWarning("Cache limit reached for trip {TripId}: {UsageMB:F1}/{MaxMB} MB",
                tripId, result.CurrentSizeMB, result.MaxSizeMB);
        }
        else if (result.UsagePercent >= CriticalThresholdPercent)
        {
            eventArgs = eventArgs with { Level = CacheLimitLevel.Critical };
            RaiseEventSafe(CacheCritical, eventArgs);
            _logger.LogWarning("Cache at critical level for trip {TripId}: {Percent:F1}%",
                tripId, result.UsagePercent);
        }
        else if (result.UsagePercent >= WarningThresholdPercent)
        {
            eventArgs = eventArgs with { Level = CacheLimitLevel.Warning };
            RaiseEventSafe(CacheWarning, eventArgs);
            _logger.LogInformation("Cache at warning level for trip {TripId}: {Percent:F1}%",
                tripId, result.UsagePercent);
        }

        return result;
    }

    /// <summary>
    /// Safely raises an event without throwing exceptions.
    /// </summary>
    private void RaiseEventSafe<T>(EventHandler<T>? handler, T args)
    {
        try
        {
            handler?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error raising cache limit event");
        }
    }

    #endregion

    /// <summary>
    /// Immutable wrapper for cache limit check state.
    /// </summary>
    private sealed record CachedLimitState(CacheLimitCheckResult Result, DateTime CheckTime);
}
