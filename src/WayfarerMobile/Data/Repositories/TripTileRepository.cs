using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for trip tile cache operations.
/// Manages downloaded map tiles for offline trip viewing.
/// </summary>
public class TripTileRepository : RepositoryBase, ITripTileRepository
{
    /// <summary>
    /// Creates a new instance of TripTileRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public TripTileRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<TripTileEntity?> GetTripTileAsync(int tripId, int zoom, int x, int y)
    {
        var db = await GetConnectionAsync();
        var id = $"{tripId}/{zoom}/{x}/{y}";
        return await db.Table<TripTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <inheritdoc />
    public async Task SaveTripTileAsync(TripTileEntity tile)
    {
        var db = await GetConnectionAsync();
        await db.InsertOrReplaceAsync(tile);
    }

    /// <inheritdoc />
    public async Task<int> GetTripTileCountAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<TripTileEntity>()
            .Where(t => t.TripId == tripId)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<List<string>> DeleteTripTilesAsync(int tripId)
    {
        var db = await GetConnectionAsync();

        // Get tile file paths before deleting
        var tiles = await db.Table<TripTileEntity>()
            .Where(t => t.TripId == tripId)
            .ToListAsync();

        var filePaths = tiles.Select(t => t.FilePath).ToList();

        // Delete tiles from database
        await db.ExecuteAsync("DELETE FROM TripTiles WHERE TripId = ?", tripId);

        return filePaths;
    }

    /// <inheritdoc />
    public async Task<int> GetTotalTripTileCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<TripTileEntity>().CountAsync();
    }

    /// <inheritdoc />
    public async Task<long> GetTripCacheSizeAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM TripTiles");
    }
}
