using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for trip download state operations.
/// Manages pause/resume state for trip downloads.
/// </summary>
public class DownloadStateRepository : RepositoryBase, IDownloadStateRepository
{
    /// <summary>
    /// Creates a new instance of DownloadStateRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public DownloadStateRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<TripDownloadStateEntity?> GetDownloadStateAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == tripId);
    }

    /// <inheritdoc />
    public async Task<TripDownloadStateEntity?> GetDownloadStateByServerIdAsync(Guid tripServerId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripServerId == tripServerId);
    }

    /// <inheritdoc />
    public async Task SaveDownloadStateAsync(TripDownloadStateEntity state)
    {
        var db = await GetConnectionAsync();
        state.LastSaveTime = DateTime.UtcNow;
        await db.InsertOrReplaceAsync(state);
    }

    /// <inheritdoc />
    public async Task DeleteDownloadStateAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM TripDownloadStates WHERE TripId = ?", tripId);
    }

    /// <inheritdoc />
    public async Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync()
    {
        var db = await GetConnectionAsync();
        // Include InProgress status to handle downloads interrupted by app kill/crash.
        // Cancelled status is excluded because those get cleaned up.
        return await db.Table<TripDownloadStateEntity>()
            .Where(s => s.Status == DownloadStateStatus.Paused ||
                       s.Status == DownloadStateStatus.LimitReached ||
                       s.Status == DownloadStateStatus.InProgress)
            .OrderByDescending(s => s.PausedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<TripDownloadStateEntity>> GetActiveDownloadStatesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<TripDownloadStateEntity>()
            .Where(s => s.Status != DownloadStateStatus.Cancelled)
            .ToListAsync();
    }
}
