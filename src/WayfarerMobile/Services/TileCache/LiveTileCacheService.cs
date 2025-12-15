using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Service for caching map tiles during live browsing with LRU eviction.
/// </summary>
public class LiveTileCacheService
{
    #region Constants

    private const int TileTimeoutMs = 10000;
    private const int DefaultMaxCacheSizeMB = 500;

    #endregion

    #region Fields

    private readonly DatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _downloadLock = new(2);
    private readonly string _cacheDirectory;

    #endregion

    #region Events

    /// <summary>
    /// Event raised periodically during prefetch with progress. Args: (downloaded, total).
    /// </summary>
    public event EventHandler<(int Downloaded, int Total)>? PrefetchProgress;

    /// <summary>
    /// Event raised when prefetch operation completes. Argument is number of tiles downloaded.
    /// </summary>
    public event EventHandler<int>? PrefetchCompleted;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of LiveTileCacheService.
    /// </summary>
    public LiveTileCacheService(
        DatabaseService databaseService,
        ISettingsService settingsService)
    {
        _databaseService = databaseService;
        _settingsService = settingsService;
        _cacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "live");
        Directory.CreateDirectory(_cacheDirectory);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(TileTimeoutMs) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets a cached tile if available.
    /// </summary>
    /// <param name="z">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>Tile file info if cached, null otherwise.</returns>
    public async Task<FileInfo?> GetCachedTileAsync(int z, int x, int y)
    {
        var filePath = GetTileFilePath(z, x, y);
        if (!File.Exists(filePath))
            return null;

        // Update access time in database for LRU tracking
        await UpdateTileAccessAsync(z, x, y);

        return new FileInfo(filePath);
    }

    /// <summary>
    /// Gets a tile from cache or downloads if not available.
    /// </summary>
    /// <param name="z">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>Tile file info if available, null otherwise.</returns>
    public async Task<FileInfo?> GetOrDownloadTileAsync(int z, int x, int y)
    {
        // Check cache first
        var cached = await GetCachedTileAsync(z, x, y);
        if (cached != null)
            return cached;

        // Download if online
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return null;

        await _downloadLock.WaitAsync();
        try
        {
            // Double-check cache after acquiring lock
            cached = await GetCachedTileAsync(z, x, y);
            if (cached != null)
                return cached;

            return await DownloadTileAsync(z, x, y);
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    /// <summary>
    /// Prefetches tiles around a location for smooth panning.
    /// Uses configured prefetch radius and max concurrent downloads from settings.
    /// Zoom levels are ordered by importance: current view (15), then adjacent (14, 16), then rest.
    /// </summary>
    /// <param name="latitude">Center latitude.</param>
    /// <param name="longitude">Center longitude.</param>
    public async Task PrefetchAroundLocationAsync(double latitude, double longitude)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return;

        // Use configured settings
        int radius = _settingsService.LiveCachePrefetchRadius;
        int maxConcurrent = _settingsService.MaxConcurrentTileDownloads;

        // Use centralized zoom levels (8-17) ordered by importance
        int[] zoomLevels = TileCacheConstants.AllZoomLevels;

        // Collect all tile coordinates to download
        var tilesToFetch = new List<(int zoom, int x, int y)>();

        foreach (var zoom in zoomLevels)
        {
            var (centerX, centerY) = LatLonToTile(latitude, longitude, zoom);
            int maxTiles = 1 << zoom;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    var x = centerX + dx;
                    var y = centerY + dy;

                    // Validate both X and Y coordinates
                    if (x < 0 || x >= maxTiles || y < 0 || y >= maxTiles)
                        continue;

                    // Only add if not already cached (check file exists, no DB hit)
                    var filePath = GetTileFilePath(zoom, x, y);
                    if (!File.Exists(filePath))
                    {
                        tilesToFetch.Add((zoom, x, y));
                    }
                }
            }
        }

        if (tilesToFetch.Count == 0)
        {
            // No tiles to fetch, but still notify (status might need refresh)
            PrefetchCompleted?.Invoke(this, 0);
            return;
        }

        // Use semaphore to limit concurrent downloads (respects server/battery)
        // Don't use 'using' - manage lifetime explicitly to avoid race condition
        var semaphore = new SemaphoreSlim(maxConcurrent);
        int downloadedCount = 0;
        int processedCount = 0;
        int totalToFetch = tilesToFetch.Count;
        int lastProgressReport = 0;
        const int progressInterval = 10; // Report every 10 tiles

        try
        {
            var tasks = tilesToFetch.Select(async tile =>
            {
                var success = await PrefetchTileWithThrottleAsync(semaphore, tile.zoom, tile.x, tile.y);
                if (success)
                    Interlocked.Increment(ref downloadedCount);

                var processed = Interlocked.Increment(ref processedCount);

                // Fire progress event every N tiles
                if (processed - lastProgressReport >= progressInterval)
                {
                    lastProgressReport = processed;
                    PrefetchProgress?.Invoke(this, (downloadedCount, totalToFetch));
                }
            }).ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            semaphore.Dispose();
        }

        // Notify subscribers that prefetch completed
        PrefetchCompleted?.Invoke(this, downloadedCount);
    }

    /// <summary>
    /// Downloads a tile for prefetch with semaphore throttling.
    /// Bypasses the class-level _downloadLock to avoid double blocking.
    /// </summary>
    /// <returns>True if tile was successfully downloaded, false otherwise.</returns>
    private async Task<bool> PrefetchTileWithThrottleAsync(SemaphoreSlim semaphore, int zoom, int x, int y)
    {
        await semaphore.WaitAsync();
        try
        {
            // Check cache again (another task might have downloaded it)
            var filePath = GetTileFilePath(zoom, x, y);
            if (File.Exists(filePath))
                return false; // Already cached, not a new download

            // Download directly without going through GetOrDownloadTileAsync
            // (which has its own _downloadLock causing double blocking)
            var result = await DownloadTileAsync(zoom, x, y);
            return result != null;
        }
        catch (Exception ex)
        {
            // Silently handle prefetch failures - not critical
            System.Diagnostics.Debug.WriteLine($"[LiveTileCache] Prefetch failed {zoom}/{x}/{y}: {ex.Message}");
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Gets the total number of cached tiles.
    /// </summary>
    public async Task<int> GetTotalCachedFilesAsync()
    {
        return await _databaseService.GetLiveTileCountAsync();
    }

    /// <summary>
    /// Gets the total cache size in bytes.
    /// </summary>
    public async Task<long> GetTotalCacheSizeBytesAsync()
    {
        return await _databaseService.GetLiveCacheSizeAsync();
    }

    /// <summary>
    /// Clears all live cached tiles.
    /// </summary>
    public async Task ClearAllAsync()
    {
        try
        {
            // Delete files
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, recursive: true);
                Directory.CreateDirectory(_cacheDirectory);
            }

            // Clear database
            await _databaseService.ClearLiveTilesAsync();
            System.Diagnostics.Debug.WriteLine("[LiveTileCacheService] Cache cleared");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LiveTileCacheService] Error clearing cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Evicts least recently used tiles to stay within cache size limit.
    /// </summary>
    public async Task EvictLruTilesAsync()
    {
        var maxSizeMB = _settingsService.MaxLiveCacheSizeMB;
        var maxSizeBytes = (long)maxSizeMB * 1024 * 1024;

        var currentSize = await GetTotalCacheSizeBytesAsync();
        if (currentSize <= maxSizeBytes)
            return;

        // Get oldest tiles to evict
        var tilesToEvict = await _databaseService.GetOldestLiveTilesAsync(100);
        foreach (var tile in tilesToEvict)
        {
            try
            {
                if (File.Exists(tile.FilePath))
                    File.Delete(tile.FilePath);

                await _databaseService.DeleteLiveTileAsync(tile.Id);
                currentSize -= tile.FileSizeBytes;

                if (currentSize <= maxSizeBytes * 0.8) // Evict to 80% of max
                    break;
            }
            catch
            {
                // Continue evicting others
            }
        }

        System.Diagnostics.Debug.WriteLine($"[LiveTileCacheService] LRU eviction complete, new size: {currentSize / 1024 / 1024}MB");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Downloads a single tile.
    /// </summary>
    private async Task<FileInfo?> DownloadTileAsync(int z, int x, int y)
    {
        var filePath = GetTileFilePath(z, x, y);
        var tempPath = filePath + ".tmp";

        try
        {
            var url = _settingsService.TileServerUrl
                .Replace("{z}", z.ToString())
                .Replace("{x}", x.ToString())
                .Replace("{y}", y.ToString());

            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
                return null;

            // Atomic write
            await File.WriteAllBytesAsync(tempPath, bytes);
            File.Move(tempPath, filePath, overwrite: true);

            // Save to database
            var tileEntity = new LiveTileEntity
            {
                Id = $"{z}/{x}/{y}",
                Zoom = z,
                X = x,
                Y = y,
                TileSource = "osm",
                FilePath = filePath,
                FileSizeBytes = bytes.Length,
                CachedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };
            await _databaseService.SaveLiveTileAsync(tileEntity);

            // Trigger LRU eviction if needed
            _ = EvictLruTilesAsync();

            return new FileInfo(filePath);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            System.Diagnostics.Debug.WriteLine($"[LiveTileCacheService] Error downloading tile {z}/{x}/{y}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Updates the last access time for a tile (LRU tracking).
    /// </summary>
    private async Task UpdateTileAccessAsync(int z, int x, int y)
    {
        try
        {
            await _databaseService.UpdateLiveTileAccessAsync($"{z}/{x}/{y}");
        }
        catch
        {
            // Non-critical, ignore
        }
    }

    /// <summary>
    /// Gets the file path for a tile.
    /// </summary>
    private string GetTileFilePath(int z, int x, int y)
    {
        return Path.Combine(_cacheDirectory, z.ToString(), x.ToString(), $"{y}.png");
    }

    /// <summary>
    /// Converts lat/lon to tile coordinates.
    /// </summary>
    private static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

        x = Math.Max(0, Math.Min((int)n - 1, x));
        y = Math.Max(0, Math.Min((int)n - 1, y));

        return (x, y);
    }

    #endregion
}
