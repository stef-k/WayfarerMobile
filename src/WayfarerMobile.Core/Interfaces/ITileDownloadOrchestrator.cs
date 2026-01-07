using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Orchestrates batch tile downloads with parallel execution, pause/resume,
/// and cache limit enforcement. Handles the mechanics of downloading tiles
/// for offline trip storage.
/// </summary>
/// <remarks>
/// <para>This service focuses on tile download orchestration:</para>
/// <list type="bullet">
/// <item>Parallel batch downloads with configurable concurrency</item>
/// <item>Pause/resume with state checkpointing</item>
/// <item>Cache limit checking and threshold notifications</item>
/// <item>Retry logic and network monitoring</item>
/// <item>PNG validation and atomic file writes</item>
/// </list>
/// <para>Does NOT handle:</para>
/// <list type="bullet">
/// <item>Trip lifecycle management (use ITripDownloadService)</item>
/// <item>Individual tile HTTP operations (uses ITileDownloadService)</item>
/// <item>Cache limit policy (uses ICacheLimitEnforcer)</item>
/// <item>Download state persistence (uses IDownloadStateManager)</item>
/// </list>
/// </remarks>
public interface ITileDownloadOrchestrator : IDisposable
{
    #region Events

    /// <summary>
    /// Raised when download progress changes (tile count updates).
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// <para>Throttling: Progress is reported at intervals to avoid UI overload.</para>
    /// </remarks>
    event EventHandler<TileDownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Raised when a download is paused (user request, network loss, storage low, or cache limit).
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// </remarks>
    event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <summary>
    /// Raised when cache warning threshold is crossed during download (80%).
    /// </summary>
    /// <remarks>
    /// <para>Deduplication: This event is raised only once per trip download.</para>
    /// </remarks>
    event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <summary>
    /// Raised when cache critical threshold is crossed during download (90%).
    /// </summary>
    /// <remarks>
    /// <para>Deduplication: This event is raised only once per trip download.</para>
    /// </remarks>
    event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <summary>
    /// Raised when cache limit is reached during download (100%).
    /// </summary>
    /// <remarks>
    /// <para>Download State: When this event fires, the download is paused with state saved.
    /// Users can resume by increasing cache limit or freeing space.</para>
    /// </remarks>
    event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    #endregion

    #region Core Operations

    /// <summary>
    /// Downloads tiles for a trip with parallel execution, state saving,
    /// and pause/resume support.
    /// </summary>
    /// <param name="tripId">Local trip ID for state tracking.</param>
    /// <param name="tripServerId">Server trip ID for coordination.</param>
    /// <param name="tripName">Trip name for event context.</param>
    /// <param name="tiles">List of tiles to download.</param>
    /// <param name="initialCompleted">Tiles already completed (for resume).</param>
    /// <param name="totalTiles">Total tiles in the download.</param>
    /// <param name="initialBytes">Bytes already downloaded (for resume).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing download statistics and stop reason.</returns>
    Task<BatchDownloadResult> DownloadTilesAsync(
        int tripId,
        Guid tripServerId,
        string tripName,
        List<TileCoordinate> tiles,
        int initialCompleted,
        int totalTiles,
        long initialBytes,
        CancellationToken cancellationToken);

    #endregion

    #region Tile Calculation

    /// <summary>
    /// Calculates all tile coordinates needed for a bounding box.
    /// Uses intelligent zoom level selection based on area size.
    /// Enforces maximum tile count to prevent memory exhaustion.
    /// </summary>
    /// <param name="boundingBox">The geographic bounding box.</param>
    /// <returns>List of tile coordinates (capped at max tile count).</returns>
    List<TileCoordinate> CalculateTilesForBoundingBox(BoundingBox boundingBox);

    /// <summary>
    /// Gets the recommended maximum zoom level based on area size.
    /// Prevents excessive downloads for very large areas.
    /// </summary>
    /// <param name="areaSquareDegrees">Area in square degrees.</param>
    /// <returns>Recommended maximum zoom level (12-17).</returns>
    int GetRecommendedMaxZoom(double areaSquareDegrees);

    #endregion

    #region Tile Cache Access

    /// <summary>
    /// Gets the file path of a cached tile if it exists.
    /// </summary>
    /// <param name="tripId">Local trip ID.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>File path if exists, null otherwise.</returns>
    string? GetCachedTilePath(int tripId, int zoom, int x, int y);

    #endregion

    #region State Management

    /// <summary>
    /// Initializes per-trip warning state for a new download.
    /// Call when starting a download to enable warning deduplication.
    /// </summary>
    /// <param name="tripId">Local trip ID.</param>
    void InitializeWarningState(int tripId);

    /// <summary>
    /// Clears per-trip warning state. Call when download completes or is cancelled.
    /// </summary>
    /// <param name="tripId">Local trip ID.</param>
    void ClearWarningState(int tripId);

    #endregion
}

/// <summary>
/// Progress event specific to tile downloads.
/// </summary>
public record TileDownloadProgressEventArgs
{
    /// <summary>
    /// Gets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the number of tiles completed.
    /// </summary>
    public int CompletedTiles { get; init; }

    /// <summary>
    /// Gets the total number of tiles.
    /// </summary>
    public int TotalTiles { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; init; }

    /// <summary>
    /// Gets the bytes downloaded so far.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Gets the status message.
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;

    /// <summary>
    /// Gets the downloaded size formatted for display (e.g., "12.5 MB").
    /// </summary>
    public string DownloadedSizeText => FormatBytes(DownloadedBytes);

    /// <summary>
    /// Formats bytes as human-readable size.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return string.Empty;
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

/// <summary>
/// Result of a batch tile download operation for a trip.
/// </summary>
/// <param name="TotalBytes">Total bytes downloaded (including any previously downloaded).</param>
/// <param name="TilesDownloaded">Number of tiles successfully downloaded in this session.</param>
/// <param name="WasPaused">Whether the download was paused (user, network, storage).</param>
/// <param name="WasLimitReached">Whether the download was stopped due to cache limit.</param>
public record BatchDownloadResult(
    long TotalBytes,
    int TilesDownloaded,
    bool WasPaused,
    bool WasLimitReached);
