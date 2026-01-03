using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for offline area and polygon operations.
/// Manages areas/regions and their polygons (zones) for downloaded trips.
/// </summary>
public class AreaRepository : RepositoryBase, IAreaRepository
{
    /// <summary>
    /// Creates a new instance of AreaRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public AreaRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    #region Area Operations

    /// <inheritdoc />
    public async Task<List<OfflineAreaEntity>> GetOfflineAreasAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflineAreaEntity>()
            .Where(a => a.TripId == tripId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SaveOfflineAreasAsync(int tripId, IEnumerable<OfflineAreaEntity> areas)
    {
        var db = await GetConnectionAsync();

        // Clear existing areas for this trip
        await db.ExecuteAsync("DELETE FROM OfflineAreas WHERE TripId = ?", tripId);

        // Insert new areas
        foreach (var area in areas)
        {
            area.TripId = tripId;
            await db.InsertAsync(area);
        }
    }

    /// <inheritdoc />
    public async Task<OfflineAreaEntity?> GetOfflineAreaByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflineAreaEntity>()
            .Where(a => a.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task UpdateOfflineAreaAsync(OfflineAreaEntity area)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(area);
    }

    /// <inheritdoc />
    public async Task DeleteOfflineAreaByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflineAreas WHERE ServerId = ?", serverId.ToString());
    }

    /// <inheritdoc />
    public async Task InsertOfflineAreaAsync(OfflineAreaEntity area)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(area);
    }

    /// <inheritdoc />
    public async Task DeleteAreasForTripAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflineAreas WHERE TripId = ?", tripId);
    }

    #endregion

    #region Polygon Operations

    /// <inheritdoc />
    public async Task<List<OfflinePolygonEntity>> GetOfflinePolygonsAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePolygonEntity>()
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SaveOfflinePolygonsAsync(int tripId, IEnumerable<OfflinePolygonEntity> polygons)
    {
        var db = await GetConnectionAsync();

        // Clear existing polygons for this trip
        await db.ExecuteAsync("DELETE FROM OfflinePolygons WHERE TripId = ?", tripId);

        // Insert new polygons
        foreach (var polygon in polygons)
        {
            polygon.TripId = tripId;
            await db.InsertAsync(polygon);
        }
    }

    /// <inheritdoc />
    public async Task<OfflinePolygonEntity?> GetOfflinePolygonByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflinePolygonEntity>()
            .Where(p => p.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task UpdateOfflinePolygonAsync(OfflinePolygonEntity polygon)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(polygon);
    }

    /// <inheritdoc />
    public async Task DeletePolygonsForTripAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflinePolygons WHERE TripId = ?", tripId);
    }

    #endregion
}
