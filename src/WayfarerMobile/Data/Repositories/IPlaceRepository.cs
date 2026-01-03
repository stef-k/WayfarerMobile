using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for offline place operations.
/// Manages places for downloaded trips.
/// </summary>
public interface IPlaceRepository
{
    /// <summary>
    /// Gets all places for a downloaded trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of places ordered by sort order.</returns>
    Task<List<OfflinePlaceEntity>> GetOfflinePlacesAsync(int tripId);

    /// <summary>
    /// Saves offline places for a trip (replaces existing).
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="places">The places to save.</param>
    Task SaveOfflinePlacesAsync(int tripId, IEnumerable<OfflinePlaceEntity> places);

    /// <summary>
    /// Gets an offline place by server ID.
    /// </summary>
    /// <param name="serverId">The server-side place ID.</param>
    /// <returns>The place or null if not found.</returns>
    Task<OfflinePlaceEntity?> GetOfflinePlaceByServerIdAsync(Guid serverId);

    /// <summary>
    /// Updates an offline place.
    /// </summary>
    /// <param name="place">The place to update.</param>
    Task UpdateOfflinePlaceAsync(OfflinePlaceEntity place);

    /// <summary>
    /// Deletes an offline place by server ID.
    /// </summary>
    /// <param name="serverId">The server-side place ID.</param>
    Task DeleteOfflinePlaceByServerIdAsync(Guid serverId);

    /// <summary>
    /// Inserts a new offline place.
    /// </summary>
    /// <param name="place">The place to insert.</param>
    Task InsertOfflinePlaceAsync(OfflinePlaceEntity place);

    /// <summary>
    /// Deletes all places for a trip.
    /// Used for cascade delete operations.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeletePlacesForTripAsync(int tripId);
}
