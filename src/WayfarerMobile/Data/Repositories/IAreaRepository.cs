using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for offline area and polygon operations.
/// Manages areas/regions and their polygons (zones) for downloaded trips.
/// </summary>
public interface IAreaRepository
{
    #region Area Operations

    /// <summary>
    /// Gets all areas/regions for a downloaded trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of areas ordered by sort order.</returns>
    Task<List<OfflineAreaEntity>> GetOfflineAreasAsync(int tripId);

    /// <summary>
    /// Saves offline areas for a trip (replaces existing).
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="areas">The areas to save.</param>
    Task SaveOfflineAreasAsync(int tripId, IEnumerable<OfflineAreaEntity> areas);

    /// <summary>
    /// Gets an offline area/region by server ID.
    /// </summary>
    /// <param name="serverId">The server-side area ID.</param>
    /// <returns>The area or null if not found.</returns>
    Task<OfflineAreaEntity?> GetOfflineAreaByServerIdAsync(Guid serverId);

    /// <summary>
    /// Updates an offline area/region.
    /// </summary>
    /// <param name="area">The area to update.</param>
    Task UpdateOfflineAreaAsync(OfflineAreaEntity area);

    /// <summary>
    /// Deletes an offline area/region by server ID.
    /// </summary>
    /// <param name="serverId">The server-side area ID.</param>
    Task DeleteOfflineAreaByServerIdAsync(Guid serverId);

    /// <summary>
    /// Inserts a new offline area/region.
    /// </summary>
    /// <param name="area">The area to insert.</param>
    Task InsertOfflineAreaAsync(OfflineAreaEntity area);

    /// <summary>
    /// Deletes all areas for a trip.
    /// Used for cascade delete operations.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeleteAreasForTripAsync(int tripId);

    #endregion

    #region Polygon Operations

    /// <summary>
    /// Gets offline polygons (TripArea zones) for a trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of polygons ordered by sort order.</returns>
    Task<List<OfflinePolygonEntity>> GetOfflinePolygonsAsync(int tripId);

    /// <summary>
    /// Saves offline polygons (TripArea zones) for a trip (replaces existing).
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="polygons">The polygons to save.</param>
    Task SaveOfflinePolygonsAsync(int tripId, IEnumerable<OfflinePolygonEntity> polygons);

    /// <summary>
    /// Gets an offline polygon by server ID.
    /// </summary>
    /// <param name="serverId">The server-side polygon ID.</param>
    /// <returns>The polygon or null if not found.</returns>
    Task<OfflinePolygonEntity?> GetOfflinePolygonByServerIdAsync(Guid serverId);

    /// <summary>
    /// Updates an offline polygon.
    /// </summary>
    /// <param name="polygon">The polygon to update.</param>
    Task UpdateOfflinePolygonAsync(OfflinePolygonEntity polygon);

    /// <summary>
    /// Deletes all polygons for a trip.
    /// Used for cascade delete operations.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeletePolygonsForTripAsync(int tripId);

    #endregion
}
