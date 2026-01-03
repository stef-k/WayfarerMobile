using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using BatchDownloadResult = WayfarerMobile.Core.Interfaces.BatchDownloadResult;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Orchestrates batch tile downloads with parallel execution, pause/resume,
/// and cache limit enforcement.
/// Implements IDisposable to properly clean up HttpClient resources.
/// </summary>
public sealed class TileDownloadOrchestrator : ITileDownloadOrchestrator
{
    #region Dependencies

    private readonly ITileDownloadService _tileDownloadService;
    private readonly ITripTileRepository _tripTileRepository;
    private readonly IDownloadStateRepository _downloadStateRepository;
    private readonly ICacheLimitEnforcer _cacheLimitEnforcer;
    private readonly IDownloadStateManager _downloadStateManager;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TileDownloadOrchestrator> _logger;

    #endregion

    #region Constants

    // Download state save intervals
    private const int StateSaveIntervalTiles = 25;
    private const int CacheLimitCheckIntervalTiles = 100;
    private const int StorageCheckIntervalTiles = 200;

    // Tile download retry configuration
    private const int MaxTileRetries = 2;
    private const int RetryDelayMs = 1000;

    // PNG file signature (first 8 bytes)
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Absolute maximum tile count to prevent memory exhaustion (regardless of cache size)
    private const int AbsoluteMaxTileCount = 150000;

    #endregion

    #region Fields

    // Per-trip warning flags - tracks if warning/critical events have been raised
    // Keyed by local trip ID (int) - available after trip entity is saved
    private readonly ConcurrentDictionary<int, TripWarningState> _tripWarningStates = new();

    // Shared HttpClient for tile downloads (avoids socket exhaustion)
    private readonly HttpClient _tileHttpClient;

    // Disposal tracking
    private bool _disposed;

    #endregion

    #region Properties

    // Configurable settings (read from ISettingsService)
    private int MaxConcurrentDownloads => _settingsService.MaxConcurrentTileDownloads;

    /// <summary>
    /// Maximum tile count derived from MaxTripCacheSizeMB setting.
    /// Capped at AbsoluteMaxTileCount to prevent memory exhaustion.
    /// </summary>
    private int MaxTileCount
    {
        get
        {
            // Calculate max tiles based on cache size setting
            var maxCacheBytes = (long)_settingsService.MaxTripCacheSizeMB * 1024 * 1024;
            var calculatedMax = (int)(maxCacheBytes / TileCacheConstants.EstimatedTileSizeBytes);

            // Cap at absolute maximum to prevent memory issues
            return Math.Min(calculatedMax, AbsoluteMaxTileCount);
        }
    }

    #endregion

    #region Events

    /// <inheritdoc/>
    public event EventHandler<TileDownloadProgressEventArgs>? ProgressChanged;

    /// <inheritdoc/>
    public event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TileDownloadOrchestrator.
    /// </summary>
    /// <param name="tileDownloadService">Service for downloading individual tiles.</param>
    /// <param name="tripTileRepository">Repository for trip tile operations.</param>
    /// <param name="downloadStateRepository">Repository for download state operations.</param>
    /// <param name="cacheLimitEnforcer">Cache limit enforcer.</param>
    /// <param name="downloadStateManager">Download state manager.</param>
    /// <param name="settingsService">Settings service.</param>
    /// <param name="logger">Logger instance.</param>
    public TileDownloadOrchestrator(
        ITileDownloadService tileDownloadService,
        ITripTileRepository tripTileRepository,
        IDownloadStateRepository downloadStateRepository,
        ICacheLimitEnforcer cacheLimitEnforcer,
        IDownloadStateManager downloadStateManager,
        ISettingsService settingsService,
        ILogger<TileDownloadOrchestrator> logger)
    {
        _tileDownloadService = tileDownloadService;
        _tripTileRepository = tripTileRepository;
        _downloadStateRepository = downloadStateRepository;
        _cacheLimitEnforcer = cacheLimitEnforcer;
        _downloadStateManager = downloadStateManager;
        _settingsService = settingsService;
        _logger = logger;

        // Initialize shared HttpClient with appropriate timeout
        _tileHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(TileCacheConstants.TileTimeoutMs) };
        _tileHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");

        // Wire up cache limit events from the enforcer to our events
        _cacheLimitEnforcer.CacheWarning += (s, e) => CacheWarning?.Invoke(this, e);
        _cacheLimitEnforcer.CacheCritical += (s, e) => CacheCritical?.Invoke(this, e);
        _cacheLimitEnforcer.CacheLimitReached += (s, e) => CacheLimitReached?.Invoke(this, e);
    }

    #endregion

    #region Core Operations

    /// <inheritdoc/>
    public async Task<BatchDownloadResult> DownloadTilesAsync(
        int tripId,
        Guid tripServerId,
        string tripName,
        List<TileCoordinate> tiles,
        int initialCompleted,
        int totalTiles,
        long initialBytes,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        // Early return for empty tile list
        if (tiles.Count == 0)
        {
            return new BatchDownloadResult(
                TotalBytes: initialBytes,
                TilesDownloaded: 0,
                WasPaused: false,
                WasLimitReached: false);
        }

        // Clear any stale stop request from previous cancel/pause
        // This allows re-downloading a trip that was previously cancelled
        _downloadStateManager.ClearStopRequest(tripId);

        // Thread-safe counters for parallel downloads
        long totalBytes = initialBytes;
        int processed = 0; // Tiles processed this session (success or fail)
        int tilesDownloadedThisSession = 0;
        int lastStateSaveProcessed = 0;
        var tileCacheDir = _tileDownloadService.GetTileCacheDirectory(tripId);
        var failedTiles = new ConcurrentBag<TileCoordinate>();
        var succeededIndices = new ConcurrentDictionary<int, bool>(); // Track which tile indices succeeded

        // Ensure cache directory exists
        Directory.CreateDirectory(tileCacheDir);

        // Check cache limit at start (before downloading any tiles)
        var initialLimitCheck = await _cacheLimitEnforcer.CheckLimitAsync();
        if (initialLimitCheck.IsLimitReached)
        {
            var eventArgs = new CacheLimitEventArgs
            {
                TripId = tripId,
                TripName = tripName,
                CurrentUsageMB = initialLimitCheck.CurrentSizeMB,
                MaxSizeMB = initialLimitCheck.MaxSizeMB,
                UsagePercent = initialLimitCheck.UsagePercent,
                Level = CacheLimitLevel.LimitReached
            };
            CacheLimitReached?.Invoke(this, eventArgs);

            await SaveDownloadStateAsync(tripId, tripServerId, tripName, tiles, initialCompleted, totalTiles, totalBytes,
                DownloadPauseReason.CacheLimitReached, DownloadStateStatus.LimitReached);
            _logger.LogWarning("Cache limit already reached before download for trip {TripId}", tripId);

            return new BatchDownloadResult(
                TotalBytes: totalBytes,
                TilesDownloaded: 0,
                WasPaused: false,
                WasLimitReached: true);
        }

        // Track stop reason for parallel download
        var stopReason = new ParallelDownloadStopReason();
        var progressLock = new object();
        var lastProgressReport = 0;

        // Get concurrency setting
        var maxConcurrency = MaxConcurrentDownloads;
        _logger.LogInformation("Starting parallel tile download for trip {TripId}: {TileCount} tiles, concurrency {Concurrency}",
            tripId, tiles.Count, maxConcurrency);

        // Use Parallel.ForEachAsync for parallel downloads with controlled concurrency
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(
                tiles.Select((tile, index) => (tile, index)),
                parallelOptions,
                async (item, ct) =>
                {
                    // Check if we should stop (pause, cancel, limit reached)
                    if (stopReason.ShouldStop)
                        return;

                    // Check stop request (pause or cancel)
                    if (_downloadStateManager.TryGetStopReason(tripId, out var requestedStopReason))
                    {
                        stopReason.SetPaused(requestedStopReason);
                        return;
                    }

                    // Check network (only first tile in batch to avoid thrashing)
                    if (item.index % maxConcurrency == 0 && !_tileDownloadService.IsNetworkAvailable())
                    {
                        stopReason.SetPaused(DownloadPauseReason.NetworkLost);
                        _logger.LogWarning("Network lost during download for trip {TripId}", tripId);
                        return;
                    }

                    // Download the tile
                    var bytes = await DownloadTileWithRetryAsync(tripId, item.tile, tileCacheDir, ct);

                    if (bytes > 0)
                    {
                        Interlocked.Add(ref totalBytes, bytes);
                        Interlocked.Increment(ref tilesDownloadedThisSession);
                        succeededIndices[item.index] = true; // Track successful tile index
                    }
                    else
                    {
                        failedTiles.Add(item.tile);
                    }

                    var currentProcessed = Interlocked.Increment(ref processed);
                    var currentCompleted = initialCompleted + currentProcessed;

                    // Thread-safe progress reporting (throttled to avoid UI overload)
                    lock (progressLock)
                    {
                        if (currentCompleted - lastProgressReport >= maxConcurrency || currentCompleted == totalTiles)
                        {
                            lastProgressReport = currentCompleted;
                            var tilesToDownload = totalTiles - initialCompleted;
                            // Convert to double first to avoid integer overflow for large tile counts
                            int percent = tilesToDownload > 0
                                ? Math.Min(95, 55 + (int)(((double)currentCompleted - initialCompleted) * 40.0 / tilesToDownload))
                                : 95;
                            RaiseProgress(tripId, currentCompleted, totalTiles, percent, $"Downloading tiles: {currentCompleted}/{totalTiles}");
                        }
                    }

                    // Periodic cache limit check (synchronized across parallel threads)
                    if (currentCompleted % CacheLimitCheckIntervalTiles == 0)
                    {
                        var limitResult = await _cacheLimitEnforcer.GetCachedLimitCheckAsync();

                        // Raise warning/critical events using per-trip state
                        if (_tripWarningStates.TryGetValue(tripId, out var warningState))
                        {
                            if (limitResult.UsagePercent >= 90 && limitResult.UsagePercent < 100)
                            {
                                if (warningState.TrySetCriticalRaised())
                                {
                                    RaiseEventSafe(CacheCritical, new CacheLimitEventArgs
                                    {
                                        TripId = tripId,
                                        TripName = tripName,
                                        CurrentUsageMB = limitResult.CurrentSizeMB,
                                        MaxSizeMB = limitResult.MaxSizeMB,
                                        UsagePercent = limitResult.UsagePercent,
                                        Level = CacheLimitLevel.Critical
                                    });
                                }
                            }
                            else if (limitResult.UsagePercent >= 80 && limitResult.UsagePercent < 90)
                            {
                                if (warningState.TrySetWarningRaised())
                                {
                                    RaiseEventSafe(CacheWarning, new CacheLimitEventArgs
                                    {
                                        TripId = tripId,
                                        TripName = tripName,
                                        CurrentUsageMB = limitResult.CurrentSizeMB,
                                        MaxSizeMB = limitResult.MaxSizeMB,
                                        UsagePercent = limitResult.UsagePercent,
                                        Level = CacheLimitLevel.Warning
                                    });
                                }
                            }
                        }

                        if (limitResult.IsLimitReached)
                        {
                            stopReason.SetLimitReached();
                        }
                    }

                    // Periodic storage check
                    if (currentCompleted % StorageCheckIntervalTiles == 0)
                    {
                        if (!_tileDownloadService.HasSufficientStorage())
                        {
                            stopReason.SetPaused(DownloadPauseReason.StorageLow);
                            _logger.LogWarning("Storage low during download for trip {TripId}", tripId);
                        }
                    }

                    // Periodic state save
                    var lastSave = Volatile.Read(ref lastStateSaveProcessed);
                    if (currentProcessed - lastSave >= StateSaveIntervalTiles)
                    {
                        // Only one thread saves state at a time
                        if (Interlocked.CompareExchange(ref lastStateSaveProcessed, currentProcessed, lastSave) == lastSave)
                        {
                            // Take atomic snapshot of succeeded indices for consistent remaining calculation
                            // Remaining = all tiles not in the succeeded set (includes unprocessed + failed)
                            var succeededSnapshot = succeededIndices.Keys.ToHashSet();
                            var remaining = tiles.Where((_, idx) => !succeededSnapshot.Contains(idx)).ToList();
                            await SaveDownloadStateAsync(tripId, tripServerId, tripName, remaining, initialCompleted + succeededSnapshot.Count, totalTiles,
                                Interlocked.Read(ref totalBytes), DownloadPauseReason.PeriodicSave, DownloadStateStatus.InProgress);
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Check if this was a pause request (which also cancels CTS) or actual cancel
            // This handles the race where CTS cancellation fires before workers see _downloadStateManager
            if (_downloadStateManager.TryGetStopReason(tripId, out var requestedReason) &&
                requestedReason == DownloadStopReason.UserPause)
            {
                stopReason.SetPaused(DownloadPauseReason.UserPause);
            }
            else
            {
                stopReason.SetPaused(DownloadPauseReason.UserCancel);
            }
        }

        // Handle stop reason if download was interrupted
        var finalProcessed = Volatile.Read(ref processed);
        var finalSucceeded = succeededIndices.Count;
        var finalBytes = Interlocked.Read(ref totalBytes);

        if (stopReason.ShouldStop)
        {
            // Remaining = tiles not yet processed + failed tiles (not succeeded)
            var remainingTiles = tiles.Where((_, idx) => !succeededIndices.ContainsKey(idx)).ToList();

            var actualCompleted = initialCompleted + finalSucceeded;

            if (stopReason.WasLimitReached)
            {
                var limitResult = await _cacheLimitEnforcer.CheckLimitAsync();
                var eventArgs = new CacheLimitEventArgs
                {
                    TripId = tripId,
                    TripName = tripName,
                    CurrentUsageMB = limitResult.CurrentSizeMB,
                    MaxSizeMB = limitResult.MaxSizeMB,
                    UsagePercent = limitResult.UsagePercent,
                    Level = CacheLimitLevel.LimitReached
                };
                CacheLimitReached?.Invoke(this, eventArgs);

                await SaveDownloadStateAsync(tripId, tripServerId, tripName, remainingTiles, actualCompleted, totalTiles, finalBytes,
                    DownloadPauseReason.CacheLimitReached, DownloadStateStatus.LimitReached);
            }
            else if (stopReason.PauseReason != DownloadPauseReason.UserCancel)
            {
                // Save state for pause (resumable) - but NOT for cancel
                // CancelDownloadAsync already deleted state and set trip.Status = Cancelled
                await SaveDownloadStateAsync(tripId, tripServerId, tripName, remainingTiles, actualCompleted, totalTiles, finalBytes,
                    stopReason.PauseReason, DownloadStateStatus.Paused);
            }
            // For UserCancel: don't save state or update trip status
            // CancelDownloadAsync already handled cleanup

            _logger.LogInformation("Download stopped for trip {TripId}: {Completed}/{Total} tiles (processed {Processed}), Reason: {Reason}",
                tripId, actualCompleted, totalTiles, finalProcessed, stopReason.PauseReason);

            // Raise download paused event
            var pauseReasonType = stopReason.PauseReason switch
            {
                DownloadPauseReason.UserPause => DownloadPauseReasonType.UserRequest,
                DownloadPauseReason.UserCancel => DownloadPauseReasonType.UserCancel,
                DownloadPauseReason.NetworkLost => DownloadPauseReasonType.NetworkLost,
                DownloadPauseReason.StorageLow => DownloadPauseReasonType.StorageLow,
                DownloadPauseReason.CacheLimitReached => DownloadPauseReasonType.CacheLimitReached,
                _ => DownloadPauseReasonType.UserRequest
            };

            DownloadPaused?.Invoke(this, new DownloadPausedEventArgs
            {
                TripId = tripId,
                TripServerId = tripServerId,
                TripName = tripName,
                Reason = pauseReasonType,
                TilesCompleted = actualCompleted,
                TotalTiles = totalTiles,
                CanResume = stopReason.PauseReason != DownloadPauseReason.UserCancel
            });

            return new BatchDownloadResult(
                TotalBytes: finalBytes,
                TilesDownloaded: Volatile.Read(ref tilesDownloadedThisSession),
                WasPaused: stopReason.WasPaused,
                WasLimitReached: stopReason.WasLimitReached);
        }

        // Retry failed tiles once at the end (sequentially to avoid overwhelming server)
        var failedTilesList = failedTiles.ToList();
        if (failedTilesList.Count > 0)
        {
            _logger.LogInformation("Retrying {Count} failed tiles for trip {TripId}", failedTilesList.Count, tripId);
            foreach (var tile in failedTilesList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var bytes = await DownloadTileWithRetryAsync(tripId, tile, tileCacheDir, cancellationToken);
                if (bytes > 0)
                {
                    Interlocked.Add(ref totalBytes, bytes);
                    Interlocked.Increment(ref tilesDownloadedThisSession);
                }
            }
        }

        // Clean up download state on successful completion
        await _downloadStateRepository.DeleteDownloadStateAsync(tripId);

        return new BatchDownloadResult(
            TotalBytes: Interlocked.Read(ref totalBytes),
            TilesDownloaded: Volatile.Read(ref tilesDownloadedThisSession),
            WasPaused: false,
            WasLimitReached: false);
    }

    /// <summary>
    /// Downloads a tile with retry logic.
    /// </summary>
    private async Task<long> DownloadTileWithRetryAsync(
        int tripId,
        TileCoordinate tile,
        string cacheDir,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= MaxTileRetries; attempt++)
        {
            var bytes = await DownloadTileAsync(tripId, tile, cacheDir, cancellationToken);
            if (bytes > 0)
                return bytes;

            if (attempt < MaxTileRetries)
            {
                _logger.LogDebug("Retry {Attempt}/{Max} for tile {TileId}", attempt + 1, MaxTileRetries, tile.Id);
                await Task.Delay(RetryDelayMs * (attempt + 1), cancellationToken); // Exponential backoff
            }
        }

        _logger.LogWarning("All retries failed for tile {TileId}", tile.Id);
        return 0;
    }

    /// <summary>
    /// Downloads a single tile with atomic file writes, rate limiting, and network monitoring.
    /// </summary>
    private async Task<long> DownloadTileAsync(
        int tripId,
        TileCoordinate tile,
        string cacheDir,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(cacheDir, $"{tile.Zoom}", $"{tile.X}", $"{tile.Y}.png");
        var tempPath = filePath + ".tmp";

        try
        {
            var url = tile.GetTileUrl(_settingsService.TileServerUrl);

            // Skip if already exists and has content
            if (File.Exists(filePath))
            {
                var existingSize = new FileInfo(filePath).Length;
                if (existingSize > 0)
                    return existingSize;
            }

            // Check network before download
            if (!_tileDownloadService.IsNetworkAvailable())
            {
                _logger.LogDebug("Waiting for network before downloading tile {TileId}...", tile.Id);
                if (!await _tileDownloadService.WaitForNetworkAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    _logger.LogWarning("Network not available for tile {TileId}", tile.Id);
                    return 0;
                }
            }

            // Enforce rate limiting
            await _tileDownloadService.EnforceRateLimitAsync(cancellationToken);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);

            // Download tile with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var response = await _tileHttpClient.GetAsync(url, combinedCts.Token);

            // Handle rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Get retry-after header if available, otherwise use default backoff
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                _logger.LogWarning("Rate limited (429) for tile {TileId}, waiting {RetryAfter}s", tile.Id, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, cancellationToken);
                return 0;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download tile {TileId}: {StatusCode}", tile.Id, response.StatusCode);
                return 0;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(combinedCts.Token);
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Empty tile data for {TileId}", tile.Id);
                return 0;
            }

            // Verify PNG integrity - check file signature
            if (!IsValidPng(bytes))
            {
                _logger.LogWarning("Invalid PNG data for tile {TileId} (signature mismatch)", tile.Id);
                return 0;
            }

            // Atomic write: temp file then move with overwrite (fixes race condition)
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);

            // Save to database
            var tileEntity = new TripTileEntity
            {
                Id = $"{tripId}/{tile.Zoom}/{tile.X}/{tile.Y}",
                TripId = tripId,
                Zoom = tile.Zoom,
                X = tile.X,
                Y = tile.Y,
                FilePath = filePath,
                FileSizeBytes = bytes.Length,
                DownloadedAt = DateTime.UtcNow
            };
            await _tripTileRepository.SaveTripTileAsync(tileEntity);

            return bytes.Length;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean up temp file on cancellation
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Network error - wait and continue
            _logger.LogWarning(ex, "Network error downloading tile {TileId}", tile.Id);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            // Clean up temp file on error
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            _logger.LogWarning(ex, "Error downloading tile {TileId}", tile.Id);
            return 0;
        }
    }

    #endregion

    #region Tile Calculation

    /// <inheritdoc/>
    public List<TileCoordinate> CalculateTilesForBoundingBox(BoundingBox bbox)
    {
        var tiles = new List<TileCoordinate>();

        // Calculate area to determine appropriate max zoom
        var areaSquareDegrees = (bbox.North - bbox.South) * (bbox.East - bbox.West);
        var recommendedMaxZoom = GetRecommendedMaxZoom(areaSquareDegrees);

        int minZoom = TileCacheConstants.MinZoomLevel;
        var effectiveMaxZoom = Math.Min(recommendedMaxZoom, TileCacheConstants.MaxZoomLevel);

        _logger.LogInformation("Area: {Area:F2} sq degrees, using zoom levels {Min}-{Max}",
            areaSquareDegrees, minZoom, effectiveMaxZoom);

        for (int zoom = minZoom; zoom <= effectiveMaxZoom; zoom++)
        {
            var (minX, maxY) = _tileDownloadService.LatLonToTile(bbox.North, bbox.West, zoom);
            var (maxX, minY) = _tileDownloadService.LatLonToTile(bbox.South, bbox.East, zoom);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    tiles.Add(new TileCoordinate { Zoom = zoom, X = x, Y = y });

                    // Enforce maximum tile count to prevent memory exhaustion
                    if (tiles.Count >= MaxTileCount)
                    {
                        _logger.LogWarning("Tile count limit reached ({MaxTiles}), truncating at zoom {Zoom}",
                            MaxTileCount, zoom);
                        return tiles;
                    }
                }
            }
        }

        return tiles;
    }

    /// <inheritdoc/>
    public int GetRecommendedMaxZoom(double areaSquareDegrees)
    {
        return areaSquareDegrees switch
        {
            > 100 => 12,   // Very large area (multiple countries) - low detail only
            > 25 => 13,    // Large area (country/large region) - medium detail
            > 5 => 14,     // Medium area (state/province) - good detail
            > 1 => 15,     // Small area (city) - high detail
            > 0.1 => 16,   // Very small area (neighborhood) - very high detail
            _ => 17        // Tiny area - maximum detail
        };
    }

    #endregion

    #region Tile Cache Access

    /// <inheritdoc/>
    public string? GetCachedTilePath(int tripId, int zoom, int x, int y)
    {
        ThrowIfDisposed();

        var filePath = Path.Combine(_tileDownloadService.GetTileCacheDirectory(tripId), $"{zoom}", $"{x}", $"{y}.png");
        return File.Exists(filePath) ? filePath : null;
    }

    #endregion

    #region State Management

    /// <inheritdoc/>
    public void InitializeWarningState(int tripId)
    {
        _tripWarningStates[tripId] = new TripWarningState();
    }

    /// <inheritdoc/>
    public void ClearWarningState(int tripId)
    {
        _tripWarningStates.TryRemove(tripId, out _);
    }

    /// <summary>
    /// Saves the current download state for later resumption.
    /// </summary>
    private async Task SaveDownloadStateAsync(
        int tripId,
        Guid tripServerId,
        string tripName,
        List<TileCoordinate> remainingTiles,
        int completedCount,
        int totalCount,
        long downloadedBytes,
        string interruptionReason,
        string status = DownloadStateStatus.Paused)
    {
        var state = new TripDownloadStateEntity
        {
            TripId = tripId,
            TripServerId = tripServerId,
            TripName = tripName,
            RemainingTilesJson = JsonSerializer.Serialize(remainingTiles),
            CompletedTileCount = completedCount,
            TotalTileCount = totalCount,
            DownloadedBytes = downloadedBytes,
            Status = status,
            InterruptionReason = interruptionReason,
            PausedAt = DateTime.UtcNow
        };

        await _downloadStateRepository.SaveDownloadStateAsync(state);
        _logger.LogInformation("Saved download state for trip {TripId}: {Completed}/{Total} tiles, status: {Status}, reason: {Reason}",
            tripId, completedCount, totalCount, status, interruptionReason);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Raises the progress changed event.
    /// </summary>
    private void RaiseProgress(int tripId, int completedTiles, int totalTiles, int percent, string message)
    {
        RaiseEventSafe(ProgressChanged, new TileDownloadProgressEventArgs
        {
            TripId = tripId,
            CompletedTiles = completedTiles,
            TotalTiles = totalTiles,
            ProgressPercent = percent,
            StatusMessage = message
        });
    }

    /// <summary>
    /// Safely raises an event, catching and logging any subscriber exceptions.
    /// </summary>
    private void RaiseEventSafe<T>(EventHandler<T>? eventHandler, T args) where T : class
    {
        if (eventHandler == null)
            return;

        try
        {
            eventHandler.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event handler threw exception for {EventType}", typeof(T).Name);
        }
    }

    /// <summary>
    /// Verifies that the byte array contains a valid PNG file by checking the file signature.
    /// </summary>
    /// <param name="bytes">The byte array to verify.</param>
    /// <returns>True if the data starts with a valid PNG signature, false otherwise.</returns>
    private static bool IsValidPng(byte[] bytes)
    {
        if (bytes.Length < PngSignature.Length)
            return false;

        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (bytes[i] != PngSignature[i])
                return false;
        }

        return true;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Clear warning states
        _tripWarningStates.Clear();

        // Dispose managed resources
        _tileHttpClient?.Dispose();

        _disposed = true;
    }

    /// <summary>
    /// Throws ObjectDisposedException if the service has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TileDownloadOrchestrator));
        }
    }

    #endregion
}

#region Helper Classes

/// <summary>
/// Per-trip warning state to track if warning/critical events have been raised.
/// Prevents duplicate warnings when multiple trips download concurrently.
/// </summary>
/// <remarks>
/// <para>Thread Safety: This class uses Interlocked for atomic compare-and-swap operations,
/// ensuring only one thread can transition each flag from false to true.</para>
/// <para>Lifecycle: Created when a download starts, cleaned up when download completes
/// (success, failure, or cancellation).</para>
/// </remarks>
internal class TripWarningState
{
    private int _warningRaised;
    private int _criticalRaised;

    /// <summary>
    /// Whether the warning event (80%) has been raised for this trip.
    /// </summary>
    public bool WarningRaised => _warningRaised == 1;

    /// <summary>
    /// Whether the critical event (90%) has been raised for this trip.
    /// </summary>
    public bool CriticalRaised => _criticalRaised == 1;

    /// <summary>
    /// Atomically sets the warning flag if not already set.
    /// </summary>
    /// <returns>True if this call set the flag, false if already set.</returns>
    public bool TrySetWarningRaised() =>
        Interlocked.CompareExchange(ref _warningRaised, 1, 0) == 0;

    /// <summary>
    /// Atomically sets the critical flag if not already set.
    /// </summary>
    /// <returns>True if this call set the flag, false if already set.</returns>
    public bool TrySetCriticalRaised() =>
        Interlocked.CompareExchange(ref _criticalRaised, 1, 0) == 0;
}

/// <summary>
/// Thread-safe helper for tracking stop reasons during parallel tile downloads.
/// </summary>
internal class ParallelDownloadStopReason
{
    private int _shouldStop;
    private int _wasPaused;
    private int _wasLimitReached;
    private object? _pauseReason;

    /// <summary>
    /// Whether the download should stop.
    /// </summary>
    public bool ShouldStop => Volatile.Read(ref _shouldStop) == 1;

    /// <summary>
    /// Whether stopped due to pause (user, network, storage).
    /// </summary>
    public bool WasPaused => Volatile.Read(ref _wasPaused) == 1;

    /// <summary>
    /// Whether stopped due to cache limit reached.
    /// </summary>
    public bool WasLimitReached => Volatile.Read(ref _wasLimitReached) == 1;

    /// <summary>
    /// Gets the pause reason if paused. Thread-safe read.
    /// </summary>
    public string PauseReason => Volatile.Read(ref _pauseReason) as string ?? string.Empty;

    /// <summary>
    /// Sets the stop reason to paused with the given reason.
    /// Only the first call takes effect. Thread-safe.
    /// </summary>
    public void SetPaused(string reason)
    {
        if (Interlocked.CompareExchange(ref _shouldStop, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _pauseReason, reason);
            Interlocked.Exchange(ref _wasPaused, 1);
        }
    }

    /// <summary>
    /// Sets the stop reason to cache limit reached.
    /// Only the first call takes effect. Thread-safe.
    /// </summary>
    public void SetLimitReached()
    {
        if (Interlocked.CompareExchange(ref _shouldStop, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _pauseReason, DownloadPauseReason.CacheLimitReached);
            Interlocked.Exchange(ref _wasLimitReached, 1);
        }
    }
}

#endregion
