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

    private const string TileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
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
    /// </summary>
    /// <param name="latitude">Center latitude.</param>
    /// <param name="longitude">Center longitude.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="radius">Number of tiles around center.</param>
    public async Task PrefetchAroundLocationAsync(double latitude, double longitude, int zoom = 15, int radius = 2)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return;

        var (centerX, centerY) = LatLonToTile(latitude, longitude, zoom);

        var tasks = new List<Task>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                var x = centerX + dx;
                var y = centerY + dy;
                if (x >= 0 && y >= 0)
                {
                    tasks.Add(GetOrDownloadTileAsync(zoom, x, y));
                }
            }
        }

        await Task.WhenAll(tasks);
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
            var url = TileUrlTemplate
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
