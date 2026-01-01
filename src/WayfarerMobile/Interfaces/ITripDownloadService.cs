using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

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
