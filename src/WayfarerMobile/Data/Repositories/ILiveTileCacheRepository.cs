using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for live tile cache operations.
/// Manages temporary map tile caching with LRU eviction.
/// </summary>
public interface ILiveTileCacheRepository
{
    /// <summary>
    /// Gets a live cached tile by ID.
    /// </summary>
    /// <param name="id">The tile ID.</param>
    /// <returns>The tile or null if not found.</returns>
    Task<LiveTileEntity?> GetLiveTileAsync(string id);

    /// <summary>
    /// Saves a live tile (insert or replace).
    /// </summary>
    /// <param name="tile">The tile to save.</param>
    Task SaveLiveTileAsync(LiveTileEntity tile);

    /// <summary>
    /// Updates the last access time for a live tile.
    /// </summary>
    /// <param name="id">The tile ID.</param>
    Task UpdateLiveTileAccessAsync(string id);

    /// <summary>
    /// Gets the count of live cached tiles.
    /// </summary>
    /// <returns>The count of tiles.</returns>
    Task<int> GetLiveTileCountAsync();

    /// <summary>
    /// Gets the total size of live cached tiles.
    /// </summary>
    /// <returns>Total size in bytes.</returns>
    Task<long> GetLiveCacheSizeAsync();

    /// <summary>
    /// Gets the oldest live tiles for LRU eviction.
    /// </summary>
    /// <param name="count">Maximum number of tiles to return.</param>
    /// <returns>List of tiles ordered by last access time.</returns>
    Task<List<LiveTileEntity>> GetOldestLiveTilesAsync(int count);

    /// <summary>
    /// Deletes a live tile by ID.
    /// </summary>
    /// <param name="id">The tile ID.</param>
    Task DeleteLiveTileAsync(string id);

    /// <summary>
    /// Clears all live tiles.
    /// </summary>
    Task ClearLiveTilesAsync();
}
