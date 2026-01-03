using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Coordinates synchronization of downloaded trips with the server.
/// Handles checking for updates, syncing metadata, and re-downloading tiles when needed.
/// </summary>
public interface ITripSyncCoordinator
{
    /// <summary>
    /// Event raised when sync progress changes.
    /// </summary>
    /// <remarks>
    /// Thread Safety: This event may be raised from background threads. Subscribers
    /// must marshal UI updates to the main thread using <c>MainThread.BeginInvokeOnMainThread</c>.
    /// </remarks>
    event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Checks if a downloaded trip has updates available on the server.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>True if update is available, false otherwise.</returns>
    Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId);

    /// <summary>
    /// Syncs a downloaded trip with the server (updates places, segments, areas
    /// and re-downloads tiles if bounding box changed).
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
    /// Gets all downloaded trips that need syncing.
    /// </summary>
    /// <returns>List of trip entities that have updates available.</returns>
    Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync();

    /// <summary>
    /// Syncs all downloaded trips that have updates available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of trips successfully synced.</returns>
    Task<int> SyncAllTripsAsync(CancellationToken cancellationToken = default);
}
