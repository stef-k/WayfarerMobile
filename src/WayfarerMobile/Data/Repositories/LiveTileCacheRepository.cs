using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for live tile cache operations.
/// Manages temporary map tile caching with LRU eviction.
/// </summary>
public class LiveTileCacheRepository : RepositoryBase, ILiveTileCacheRepository
{
    /// <summary>
    /// Creates a new instance of LiveTileCacheRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public LiveTileCacheRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<LiveTileEntity?> GetLiveTileAsync(string id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <inheritdoc />
    public async Task SaveLiveTileAsync(LiveTileEntity tile)
    {
        var db = await GetConnectionAsync();
        await db.InsertOrReplaceAsync(tile);
    }

    /// <inheritdoc />
    public async Task UpdateLiveTileAccessAsync(string id)
    {
        var db = await GetConnectionAsync();
        var tile = await db.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tile != null)
        {
            tile.LastAccessedAt = DateTime.UtcNow;
            tile.AccessCount++;
            await db.UpdateAsync(tile);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetLiveTileCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LiveTileEntity>().CountAsync();
    }

    /// <inheritdoc />
    public async Task<long> GetLiveCacheSizeAsync()
    {
        var db = await GetConnectionAsync();
        var result = await db.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");
        return result;
    }

    /// <inheritdoc />
    public async Task<List<LiveTileEntity>> GetOldestLiveTilesAsync(int count)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task DeleteLiveTileAsync(string id)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM LiveTiles WHERE Id = ?", id);
    }

    /// <inheritdoc />
    public async Task ClearLiveTilesAsync()
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM LiveTiles");
    }
}
