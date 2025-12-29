using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service interface for downloading and managing offline trips.
/// </summary>
public interface ITripDownloadService : IDisposable
{
    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when cache usage reaches warning level (80%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <summary>
    /// Event raised when cache usage reaches critical level (90%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <summary>
    /// Event raised when cache limit is reached (100%).
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    /// <summary>
    /// Event raised when a download completes successfully.
    /// </summary>
    event EventHandler<DownloadTerminalEventArgs>? DownloadCompleted;

    /// <summary>
    /// Event raised when a download fails.
    /// </summary>
    event EventHandler<DownloadTerminalEventArgs>? DownloadFailed;

    /// <summary>
    /// Event raised when a download is paused.
    /// </summary>
    event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <summary>
    /// Downloads a trip for offline access (metadata and places).
    /// </summary>
    /// <param name="tripSummary">The trip summary to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The downloaded trip entity or null if failed.</returns>
    Task<DownloadedTripEntity?> DownloadTripAsync(
        TripSummary tripSummary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all downloaded trips.
    /// </summary>
    /// <returns>List of downloaded trip entities.</returns>
    Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync();

    /// <summary>
    /// Checks if a trip is downloaded.
    /// </summary>
    /// <param name="tripId">The server-side trip ID.</param>
    /// <returns>True if downloaded, false otherwise.</returns>
    Task<bool> IsTripDownloadedAsync(Guid tripId);

    /// <summary>
    /// Deletes a downloaded trip and its associated data.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    Task DeleteTripAsync(Guid tripServerId);

    /// <summary>
    /// Deletes only the cached map tiles for a trip, keeping trip data intact.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Number of tiles deleted.</returns>
    Task<int> DeleteTripTilesAsync(Guid tripServerId);

    /// <summary>
    /// Pauses an active download.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>True if paused successfully.</returns>
    Task<bool> PauseDownloadAsync(int tripId);

    /// <summary>
    /// Resumes a paused download.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if resume was successful.</returns>
    Task<bool> ResumeDownloadAsync(int tripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an active download.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="cleanup">Whether to clean up downloaded tiles.</param>
    /// <returns>True if cancelled successfully.</returns>
    Task<bool> CancelDownloadAsync(int tripId, bool cleanup = false);

    /// <summary>
    /// Gets paused downloads that can be resumed.
    /// </summary>
    /// <returns>List of paused download states.</returns>
    Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync();

    /// <summary>
    /// Syncs a downloaded trip with the server.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="forceSync">If true, sync regardless of version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated trip entity or null if sync failed.</returns>
    Task<DownloadedTripEntity?> SyncTripAsync(
        Guid tripServerId,
        bool forceSync = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the current trip cache limit.
    /// </summary>
    /// <returns>Result with current usage and limit information.</returns>
    Task<CacheLimitCheckResult> CheckTripCacheLimitAsync();

    /// <summary>
    /// Checks if there's enough cache quota for a trip download.
    /// </summary>
    /// <param name="boundingBox">The trip's bounding box.</param>
    /// <returns>Result with quota details and tile count.</returns>
    Task<CacheQuotaCheckResult> CheckCacheQuotaForTripAsync(BoundingBox? boundingBox);

    /// <summary>
    /// Estimates the download size for a trip.
    /// </summary>
    /// <param name="tileCount">Number of tiles to download.</param>
    /// <returns>Estimated size in bytes.</returns>
    long EstimateDownloadSize(int tileCount);

    /// <summary>
    /// Estimates the tile count for a trip based on its bounding box.
    /// </summary>
    /// <param name="boundingBox">The trip's bounding box.</param>
    /// <returns>Estimated number of tiles.</returns>
    int EstimateTileCount(BoundingBox? boundingBox);

    /// <summary>
    /// Gets offline trip details including places and segments.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Trip details or null if not found.</returns>
    Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId);

    /// <summary>
    /// Gets offline places for a trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip places.</returns>
    Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId);

    /// <summary>
    /// Gets offline segments for a trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip segments.</returns>
    Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId);

    /// <summary>
    /// Updates the name of a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="newName">The new name.</param>
    Task UpdateTripNameAsync(Guid tripServerId, string newName);

    /// <summary>
    /// Updates the notes of a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="notes">The new notes (HTML).</param>
    Task UpdateTripNotesAsync(Guid tripServerId, string? notes);

    /// <summary>
    /// Checks if a download is paused for a specific trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>True if download is paused.</returns>
    Task<bool> IsDownloadPausedAsync(int tripId);

    /// <summary>
    /// Gets a cached tile file path if it exists.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>File path if exists, null otherwise.</returns>
    string? GetCachedTilePath(int tripId, int zoom, int x, int y);

    /// <summary>
    /// Cleans up orphaned temporary files from interrupted downloads.
    /// </summary>
    /// <returns>Number of temp files cleaned up.</returns>
    int CleanupOrphanedTempFiles();
}

/// <summary>
/// Event args for download progress updates.
/// </summary>
public record DownloadProgressEventArgs
{
    /// <summary>
    /// Gets the trip ID being downloaded.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; init; }

    /// <summary>
    /// Gets the status message.
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Event args for cache limit events (warning, critical, limit reached).
/// </summary>
public record CacheLimitEventArgs
{
    /// <summary>
    /// Gets the trip ID being downloaded.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the trip name for display.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current cache usage in MB.
    /// </summary>
    public double CurrentUsageMB { get; init; }

    /// <summary>
    /// Gets the maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// Gets the usage percentage (0-100+).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Gets the level of the warning.
    /// </summary>
    public CacheLimitLevel Level { get; init; }
}

/// <summary>
/// Cache limit warning levels.
/// </summary>
public enum CacheLimitLevel
{
    /// <summary>Warning level (80%).</summary>
    Warning,

    /// <summary>Critical level (90%).</summary>
    Critical,

    /// <summary>Limit reached (100%).</summary>
    LimitReached
}

/// <summary>
/// Result of checking trip cache limit.
/// </summary>
public record CacheLimitCheckResult
{
    /// <summary>
    /// Gets the current cache size in bytes.
    /// </summary>
    public long CurrentSizeBytes { get; init; }

    /// <summary>
    /// Gets the current cache size in MB.
    /// </summary>
    public double CurrentSizeMB { get; init; }

    /// <summary>
    /// Gets the maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// Gets the usage percentage (0-100+).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Gets whether the limit has been reached.
    /// </summary>
    public bool IsLimitReached { get; init; }

    /// <summary>
    /// Gets whether we're at warning level (80-99%).
    /// </summary>
    public bool IsWarningLevel { get; init; }
}

/// <summary>
/// Result of checking cache quota for a new download.
/// </summary>
public record CacheQuotaCheckResult
{
    /// <summary>
    /// Number of tiles to download.
    /// </summary>
    public int TileCount { get; init; }

    /// <summary>
    /// Estimated download size in bytes.
    /// </summary>
    public long EstimatedSizeBytes { get; init; }

    /// <summary>
    /// Estimated download size in MB.
    /// </summary>
    public double EstimatedSizeMB { get; init; }

    /// <summary>
    /// Available cache quota in bytes.
    /// </summary>
    public long AvailableBytes { get; init; }

    /// <summary>
    /// Available cache quota in MB.
    /// </summary>
    public double AvailableMB { get; init; }

    /// <summary>
    /// Current cache usage in MB.
    /// </summary>
    public double CurrentUsageMB { get; init; }

    /// <summary>
    /// Maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// True if there's enough quota for the download.
    /// </summary>
    public bool HasSufficientQuota { get; init; }

    /// <summary>
    /// Amount by which the download would exceed the limit (in MB).
    /// Zero if within quota.
    /// </summary>
    public double WouldExceedBy { get; init; }
}

/// <summary>
/// Event args for terminal download events (completed or failed).
/// </summary>
public record DownloadTerminalEventArgs
{
    /// <summary>
    /// Gets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the server trip ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of tiles downloaded.
    /// </summary>
    public int TilesDownloaded { get; init; }

    /// <summary>
    /// Gets the total bytes downloaded.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Gets the error message (for failed downloads).
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Event args for download paused events.
/// </summary>
public record DownloadPausedEventArgs
{
    /// <summary>
    /// Gets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the server trip ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the reason for pausing.
    /// </summary>
    public DownloadPauseReasonType Reason { get; init; }

    /// <summary>
    /// Gets the number of tiles completed so far.
    /// </summary>
    public int TilesCompleted { get; init; }

    /// <summary>
    /// Gets the total number of tiles.
    /// </summary>
    public int TotalTiles { get; init; }

    /// <summary>
    /// Gets whether the download can be resumed.
    /// </summary>
    public bool CanResume { get; init; } = true;
}

/// <summary>
/// Reason for pausing a download.
/// </summary>
public enum DownloadPauseReasonType
{
    /// <summary>User requested pause.</summary>
    UserRequest,

    /// <summary>User cancelled the download.</summary>
    UserCancel,

    /// <summary>Network connection lost.</summary>
    NetworkLost,

    /// <summary>Device storage is low.</summary>
    StorageLow,

    /// <summary>Trip cache limit reached.</summary>
    CacheLimitReached
}
