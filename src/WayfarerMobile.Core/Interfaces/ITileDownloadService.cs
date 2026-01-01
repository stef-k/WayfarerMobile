using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service responsible for downloading individual map tiles.
/// Handles HTTP operations, rate limiting, retries, and storage validation.
/// </summary>
/// <remarks>
/// This service focuses on the mechanics of tile downloading:
/// - Individual tile fetching with retry logic
/// - Rate limiting to avoid server throttling
/// - Network availability checking
/// - Storage space validation
/// - PNG signature validation
/// - Temporary file cleanup
///
/// Does NOT handle:
/// - Download orchestration (use TripDownloadService)
/// - State persistence (use DownloadStateManager)
/// - Cache limits (use CacheLimitEnforcer)
/// </remarks>
public interface ITileDownloadService
{
    /// <summary>
    /// Gets the minimum delay in milliseconds between tile requests.
    /// Used for rate limiting to prevent server throttling.
    /// </summary>
    int MinRequestDelayMs { get; }

    /// <summary>
    /// Downloads a single tile to the specified path.
    /// </summary>
    /// <param name="tileUrl">The URL to download the tile from.</param>
    /// <param name="savePath">The local path to save the tile to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing success status, bytes downloaded, and any error.</returns>
    Task<TileDownloadResult> DownloadTileAsync(
        string tileUrl,
        string savePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a tile with automatic retry on failure.
    /// </summary>
    /// <param name="tileUrl">The URL to download the tile from.</param>
    /// <param name="savePath">The local path to save the tile to.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing success status, bytes downloaded, and any error.</returns>
    Task<TileDownloadResult> DownloadTileWithRetryAsync(
        string tileUrl,
        string savePath,
        int maxRetries = 3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enforces rate limiting between tile requests.
    /// Call before each download to prevent server throttling.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnforceRateLimitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if network is currently available for downloading.
    /// </summary>
    /// <returns>True if network is available.</returns>
    bool IsNetworkAvailable();

    /// <summary>
    /// Waits for network to become available.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if network became available within timeout.</returns>
    Task<bool> WaitForNetworkAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if there is sufficient storage space for downloads.
    /// </summary>
    /// <param name="requiredMB">Minimum required space in megabytes.</param>
    /// <returns>True if sufficient storage is available.</returns>
    bool HasSufficientStorage(long requiredMB = 50);

    /// <summary>
    /// Gets the cache directory path for a trip's tiles.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>The directory path for the trip's cached tiles.</returns>
    string GetTileCacheDirectory(int tripId);

    /// <summary>
    /// Calculates the tile coordinates needed to cover a bounding box at specified zoom levels.
    /// </summary>
    /// <param name="boundingBox">The geographic bounding box.</param>
    /// <param name="minZoom">Minimum zoom level.</param>
    /// <param name="maxZoom">Maximum zoom level.</param>
    /// <returns>List of tile coordinates to download.</returns>
    List<TileCoordinate> CalculateTilesForBoundingBox(
        BoundingBox boundingBox,
        int minZoom,
        int maxZoom);

    /// <summary>
    /// Gets the recommended maximum zoom level based on bounding box area.
    /// Smaller areas can use higher zoom levels.
    /// </summary>
    /// <param name="boundingBox">The geographic bounding box.</param>
    /// <returns>Recommended maximum zoom level (10-17).</returns>
    int GetRecommendedMaxZoom(BoundingBox boundingBox);

    /// <summary>
    /// Converts latitude/longitude to tile coordinates at a given zoom level.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <returns>Tile X and Y coordinates.</returns>
    (int X, int Y) LatLonToTile(double latitude, double longitude, int zoom);

    /// <summary>
    /// Cleans up orphaned temporary files older than the specified age.
    /// </summary>
    /// <param name="maxAgeHours">Maximum age in hours before cleanup.</param>
    /// <returns>Number of files cleaned up.</returns>
    Task<int> CleanupOrphanedTempFilesAsync(int maxAgeHours = 24);

    /// <summary>
    /// Validates that a file contains a valid PNG image.
    /// </summary>
    /// <param name="filePath">Path to the file to validate.</param>
    /// <returns>True if the file has a valid PNG signature.</returns>
    bool IsValidPng(string filePath);
}

/// <summary>
/// Result of a tile download operation.
/// </summary>
public record TileDownloadResult
{
    /// <summary>
    /// Gets whether the download succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the number of bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// Gets the error message if download failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Gets whether the failure was due to network issues (retriable).
    /// </summary>
    public bool IsNetworkError { get; init; }

    /// <summary>
    /// Gets whether the tile was skipped (e.g., already exists).
    /// </summary>
    public bool WasSkipped { get; init; }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static TileDownloadResult Succeeded(long bytes) =>
        new() { Success = true, BytesDownloaded = bytes };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static TileDownloadResult Failed(string error, bool isNetworkError = false) =>
        new() { Success = false, Error = error, IsNetworkError = isNetworkError };

    /// <summary>
    /// Creates a skipped result.
    /// </summary>
    public static TileDownloadResult Skipped() =>
        new() { Success = true, WasSkipped = true };
}
