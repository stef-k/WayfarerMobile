using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for downloaded trip operations.
/// Manages trip metadata and coordinates cascade deletes.
/// </summary>
public interface ITripRepository
{
    /// <summary>
    /// Gets all downloaded trips.
    /// </summary>
    /// <returns>List of trips ordered by download date descending.</returns>
    Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync();

    /// <summary>
    /// Gets a downloaded trip by server ID.
    /// </summary>
    /// <param name="serverId">The server-side trip ID.</param>
    /// <returns>The trip or null if not found.</returns>
    Task<DownloadedTripEntity?> GetDownloadedTripByServerIdAsync(Guid serverId);

    /// <summary>
    /// Gets a downloaded trip by local ID.
    /// </summary>
    /// <param name="id">The local trip ID.</param>
    /// <returns>The trip or null if not found.</returns>
    Task<DownloadedTripEntity?> GetDownloadedTripAsync(int id);

    /// <summary>
    /// Saves a downloaded trip (insert or update).
    /// </summary>
    /// <param name="trip">The trip to save.</param>
    /// <returns>The trip's local ID.</returns>
    Task<int> SaveDownloadedTripAsync(DownloadedTripEntity trip);

    /// <summary>
    /// Deletes a downloaded trip and all associated data.
    /// Performs cascade delete of tiles, places, segments, and areas.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeleteDownloadedTripAsync(int tripId);

    /// <summary>
    /// Gets the total size of all completed downloaded trips.
    /// </summary>
    /// <returns>Total size in bytes.</returns>
    Task<long> GetTotalTripCacheSizeAsync();
}
