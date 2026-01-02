using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for trip content operations: metadata fetch, sync, and offline retrieval.
/// Handles all trip data operations without tile management.
/// </summary>
public interface ITripContentService
{
    /// <summary>
    /// Checks if a downloaded trip needs updating based on server version.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>True if update is available, false otherwise.</returns>
    Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId);

    /// <summary>
    /// Gets all downloaded trips that need syncing.
    /// </summary>
    /// <returns>List of trip entities that have updates available.</returns>
    Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync();

    /// <summary>
    /// Syncs trip metadata with the server (updates places, segments, areas).
    /// Does not handle tile downloads - returns whether bounding box changed.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="forceSync">If true, sync regardless of version.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (updated trip entity or null, whether bounding box changed significantly).</returns>
    Task<(DownloadedTripEntity? Trip, bool BoundingBoxChanged)> SyncTripMetadataAsync(
        Guid tripServerId,
        bool forceSync = false,
        IProgress<DownloadProgressEventArgs>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs metadata for all downloaded trips.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of trips successfully synced.</returns>
    Task<int> SyncAllTripsMetadataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets complete offline trip details for navigation.
    /// Returns a TripDetails object populated from offline storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Complete trip details or null if not downloaded.</returns>
    Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId);

    /// <summary>
    /// Gets offline places for a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip places.</returns>
    Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId);

    /// <summary>
    /// Gets offline segments for a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip segments.</returns>
    Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId);

    /// <summary>
    /// Checks if bounding box has changed significantly (more than ~1km at equator).
    /// </summary>
    /// <param name="trip">The local trip entity.</param>
    /// <param name="serverBoundingBox">The server bounding box.</param>
    /// <returns>True if bounding box changed significantly.</returns>
    bool HasBoundingBoxChangedSignificantly(DownloadedTripEntity trip, BoundingBox serverBoundingBox);
}
