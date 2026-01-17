using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for offline place operations.
/// Manages places for downloaded trips.
/// </summary>
public class PlaceRepository : RepositoryBase, IPlaceRepository
{
    /// <summary>
    /// Creates a new instance of PlaceRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public PlaceRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<List<OfflinePlaceEntity>> GetOfflinePlacesAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SaveOfflinePlacesAsync(int tripId, IEnumerable<OfflinePlaceEntity> places)
    {
        var db = await GetConnectionAsync();

        // Clear existing places for this trip
        await db.ExecuteAsync("DELETE FROM OfflinePlaces WHERE TripId = ?", tripId);

        // Set TripId and bulk insert
        var placeList = places.ToList();
        foreach (var place in placeList)
        {
            place.TripId = tripId;
        }

        if (placeList.Count > 0)
        {
            await db.InsertAllAsync(placeList);
        }
    }

    /// <inheritdoc />
    public async Task<OfflinePlaceEntity?> GetOfflinePlaceByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaceEntity>()
            .Where(p => p.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task UpdateOfflinePlaceAsync(OfflinePlaceEntity place)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(place);
    }

    /// <inheritdoc />
    public async Task DeleteOfflinePlaceByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflinePlaces WHERE ServerId = ?", serverId.ToString());
    }

    /// <inheritdoc />
    public async Task InsertOfflinePlaceAsync(OfflinePlaceEntity place)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(place);
    }

    /// <inheritdoc />
    public async Task DeletePlacesForTripAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflinePlaces WHERE TripId = ?", tripId);
    }

    /// <inheritdoc />
    public async Task<List<OfflinePlaceEntity>> GetOfflinePlacesByRegionIdAsync(Guid regionId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePlaceEntity>()
            .Where(p => p.RegionId == regionId)
            .ToListAsync();
    }
}
