using System.Security;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Service responsible for downloading individual map tiles.
/// Handles HTTP operations, rate limiting, retries, and storage validation.
/// </summary>
/// <remarks>
/// This service focuses on the mechanics of tile downloading without state management.
/// Use DownloadStateManager for pause/resume state and CacheLimitEnforcer for quota checks.
/// </remarks>
public sealed class TileDownloadService : ITileDownloadService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TileDownloadService> _logger;
    private readonly HttpClient _httpClient;

    // Rate limiting state
    private readonly object _rateLimitLock = new();
    private DateTime _lastRequestTime = DateTime.MinValue;

    // Constants
    private const int TileTimeoutMs = TileCacheConstants.TileTimeoutMs;
    private const int MaxTileRetries = 2;
    private const int RetryDelayMs = 1000;
    private const int TempFileMaxAgeHours = 1;
    private const long MinRequiredSpaceMB = 50;

    // PNG file signature (first 8 bytes)
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private bool _disposed;

    /// <inheritdoc/>
    public int MinRequestDelayMs => _settingsService.MinTileRequestDelayMs;

    /// <summary>
    /// Creates a new instance of TileDownloadService.
    /// </summary>
    public TileDownloadService(
        ISettingsService settingsService,
        ILogger<TileDownloadService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        // Initialize shared HttpClient with appropriate timeout
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(TileTimeoutMs) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");
    }

    /// <inheritdoc/>
    public async Task<TileDownloadResult> DownloadTileAsync(
        string tileUrl,
        string savePath,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var tempPath = savePath + ".tmp";

        try
        {
            // Skip if already exists and has content
            if (File.Exists(savePath))
            {
                var existingSize = new FileInfo(savePath).Length;
                if (existingSize > 0)
                    return TileDownloadResult.Skipped();
            }

            // Check network before download
            if (!IsNetworkAvailable())
            {
                _logger.LogDebug("Waiting for network before downloading tile...");
                if (!await WaitForNetworkAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    _logger.LogWarning("Network not available for tile download");
                    return TileDownloadResult.Failed("Network not available", isNetworkError: true);
                }
            }

            // Enforce rate limiting
            await EnforceRateLimitAsync(cancellationToken);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(savePath)!;
            Directory.CreateDirectory(dir);

            // Download tile with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var response = await _httpClient.GetAsync(tileUrl, combinedCts.Token);

            // Handle rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                _logger.LogWarning("Rate limited (429), waiting {RetryAfter}s", retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, cancellationToken);
                return TileDownloadResult.Failed($"Rate limited, retry after {retryAfter.TotalSeconds}s", isNetworkError: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download tile: {StatusCode}", response.StatusCode);
                return TileDownloadResult.Failed($"HTTP {(int)response.StatusCode}", isNetworkError: false);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(combinedCts.Token);
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Empty tile data received");
                return TileDownloadResult.Failed("Empty response");
            }

            // Verify PNG integrity
            if (!IsValidPng(bytes))
            {
                _logger.LogWarning("Invalid PNG data (signature mismatch)");
                return TileDownloadResult.Failed("Invalid PNG signature");
            }

            // Atomic write: temp file then move with overwrite
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, savePath, overwrite: true);

            return TileDownloadResult.Succeeded(bytes.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupTempFile(tempPath);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error downloading tile");
            CleanupTempFile(tempPath);
            return TileDownloadResult.Failed(ex.Message, isNetworkError: true);
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath);
            _logger.LogWarning(ex, "Error downloading tile");
            return TileDownloadResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<TileDownloadResult> DownloadTileWithRetryAsync(
        string tileUrl,
        string savePath,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        TileDownloadResult result = TileDownloadResult.Failed("No attempts made");

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelayMs * attempt, cancellationToken);
            }

            result = await DownloadTileAsync(tileUrl, savePath, cancellationToken);

            if (result.Success || result.WasSkipped)
                return result;

            // Don't retry non-network errors
            if (!result.IsNetworkError)
                break;

            _logger.LogDebug("Tile download attempt {Attempt}/{MaxAttempts} failed, retrying...",
                attempt + 1, maxRetries + 1);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task EnforceRateLimitAsync(CancellationToken cancellationToken = default)
    {
        TimeSpan waitTime;
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            var minimumDelay = TimeSpan.FromMilliseconds(MinRequestDelayMs);

            var earliestNextRequest = _lastRequestTime.Add(minimumDelay);

            if (now < earliestNextRequest)
            {
                waitTime = earliestNextRequest - now;
            }
            else
            {
                waitTime = TimeSpan.Zero;
            }

            // Reserve our slot
            _lastRequestTime = now.Add(waitTime);
        }

        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public bool IsNetworkAvailable()
    {
        return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    }

    /// <inheritdoc/>
    public async Task<bool> WaitForNetworkAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (IsNetworkAvailable())
            return true;

        var tcs = new TaskCompletionSource<bool>();

        void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                tcs.TrySetResult(true);
            }
        }

        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var registration = combined.Token.Register(() => tcs.TrySetResult(false));

            return await tcs.Task;
        }
        finally
        {
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        }
    }

    /// <inheritdoc/>
    public bool HasSufficientStorage(long requiredMB = 50)
    {
        try
        {
            var cacheDir = FileSystem.CacheDirectory;
            var pathRoot = Path.GetPathRoot(cacheDir);
            if (string.IsNullOrEmpty(pathRoot))
            {
                _logger.LogWarning("Could not determine path root, assuming sufficient storage");
                return true;
            }

            var driveInfo = new DriveInfo(pathRoot);
            var freeSpaceMB = driveInfo.AvailableFreeSpace / (1024 * 1024);

            _logger.LogDebug("Available storage: {FreeSpace} MB, required: {Required} MB", freeSpaceMB, requiredMB);
            return freeSpaceMB >= requiredMB;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error checking storage, assuming sufficient");
            return true;
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning(ex, "Security error checking storage, assuming sufficient");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking storage, assuming sufficient");
            return true;
        }
    }

    /// <inheritdoc/>
    public string GetTileCacheDirectory(int tripId)
    {
        return Path.Combine(FileSystem.CacheDirectory, "tiles", $"trip_{tripId}");
    }

    /// <inheritdoc/>
    public List<TileCoordinate> CalculateTilesForBoundingBox(
        BoundingBox boundingBox,
        int minZoom,
        int maxZoom)
    {
        var tiles = new List<TileCoordinate>();

        for (int zoom = minZoom; zoom <= maxZoom; zoom++)
        {
            var (minX, maxY) = LatLonToTile(boundingBox.North, boundingBox.West, zoom);
            var (maxX, minY) = LatLonToTile(boundingBox.South, boundingBox.East, zoom);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    tiles.Add(new TileCoordinate { Zoom = zoom, X = x, Y = y });
                }
            }
        }

        return tiles;
    }

    /// <inheritdoc/>
    public int GetRecommendedMaxZoom(BoundingBox boundingBox)
    {
        var areaSquareDegrees = (boundingBox.North - boundingBox.South) *
                                (boundingBox.East - boundingBox.West);

        return areaSquareDegrees switch
        {
            > 100 => 12,  // Very large area (multiple countries)
            > 25 => 13,   // Large area (country/large region)
            > 5 => 14,    // Medium area (state/province)
            > 1 => 15,    // Small area (city)
            > 0.1 => 16,  // Very small area (neighborhood)
            _ => 17       // Tiny area - maximum detail
        };
    }

    /// <inheritdoc/>
    public (int X, int Y) LatLonToTile(double latitude, double longitude, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((longitude + 180.0) / 360.0 * n);
        var latRad = latitude * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

        // Clamp to valid range
        x = Math.Max(0, Math.Min((int)n - 1, x));
        y = Math.Max(0, Math.Min((int)n - 1, y));

        return (x, y);
    }

    /// <inheritdoc/>
    public async Task<int> CleanupOrphanedTempFilesAsync(int maxAgeHours = 24)
    {
        ThrowIfDisposed();

        var cleanedCount = 0;
        try
        {
            var tilesRootDir = Path.Combine(FileSystem.CacheDirectory, "tiles");
            if (!Directory.Exists(tilesRootDir))
                return 0;

            var tempFiles = Directory.GetFiles(tilesRootDir, "*.tmp", SearchOption.AllDirectories);
            var maxAge = DateTime.UtcNow.AddHours(-maxAgeHours);

            foreach (var tempFile in tempFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(tempFile);
                    if (fileInfo.LastWriteTimeUtc < maxAge)
                    {
                        File.Delete(tempFile);
                        cleanedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error deleting temp file: {FilePath}", tempFile);
                }
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} orphaned temp files", cleanedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during temp file cleanup");
        }

        return await Task.FromResult(cleanedCount);
    }

    /// <inheritdoc/>
    public bool IsValidPng(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var header = new byte[PngSignature.Length];
            using var stream = File.OpenRead(filePath);
            if (stream.Read(header, 0, header.Length) < header.Length)
                return false;

            return IsValidPng(header);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that the byte array contains a valid PNG signature.
    /// </summary>
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

    /// <summary>
    /// Safely cleans up a temporary file.
    /// </summary>
    private static void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch { /* Ignore cleanup errors */ }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }
}
