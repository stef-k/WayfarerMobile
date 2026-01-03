using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for trip tile cache operations.
/// Manages downloaded map tiles for offline trip viewing.
/// </summary>
public interface ITripTileRepository
{
    /// <summary>
    /// Gets a tile for a specific trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>The tile or null if not found.</returns>
    Task<TripTileEntity?> GetTripTileAsync(int tripId, int zoom, int x, int y);

    /// <summary>
    /// Saves a trip tile (insert or replace).
    /// </summary>
    /// <param name="tile">The tile to save.</param>
    Task SaveTripTileAsync(TripTileEntity tile);

    /// <summary>
    /// Gets the count of tiles for a specific trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>The count of tiles for the trip.</returns>
    Task<int> GetTripTileCountAsync(int tripId);

    /// <summary>
    /// Deletes all tiles for a trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of file paths that were deleted from database.</returns>
    Task<List<string>> DeleteTripTilesAsync(int tripId);

    /// <summary>
    /// Gets the total count of trip tiles across all trips.
    /// </summary>
    /// <returns>Total tile count.</returns>
    Task<int> GetTotalTripTileCountAsync();

    /// <summary>
    /// Gets the total size of trip tile cache in bytes.
    /// </summary>
    /// <returns>Total size in bytes.</returns>
    Task<long> GetTripCacheSizeAsync();
}
