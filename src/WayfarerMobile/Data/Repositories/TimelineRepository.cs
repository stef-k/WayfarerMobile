using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for local timeline operations.
/// Manages local timeline entries for GPS location history display.
/// </summary>
public class TimelineRepository : RepositoryBase, ITimelineRepository
{
    /// <summary>
    /// Creates a new instance of TimelineRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public TimelineRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    #region CRUD Operations

    /// <inheritdoc />
    public async Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        var db = await GetConnectionAsync();
        await db.InsertAsync(entry);
        return entry.Id;
    }

    /// <inheritdoc />
    public async Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(entry);
    }

    /// <inheritdoc />
    public async Task DeleteLocalTimelineEntryAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM LocalTimelineEntries WHERE Id = ?", id);
    }

    /// <inheritdoc />
    public async Task<int> DeleteLocalTimelineEntryByTimestampAsync(
        DateTime timestamp,
        double latitude,
        double longitude,
        int toleranceSeconds = 2)
    {
        var db = await GetConnectionAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        return await db.ExecuteAsync(
            "DELETE FROM LocalTimelineEntries WHERE Timestamp >= ? AND Timestamp <= ? AND Latitude = ? AND Longitude = ?",
            minTime, maxTime, latitude, longitude);
    }

    #endregion

    #region Query Operations

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.ServerId == serverId);
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(
        DateTime timestamp,
        int toleranceSeconds = 2)
    {
        var db = await GetConnectionAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= minTime && e.Timestamp <= maxTime)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Range Queries

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date)
    {
        var db = await GetConnectionAsync();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startOfDay && e.Timestamp < endOfDay)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        var db = await GetConnectionAsync();

        var startTime = fromDate.Date.ToUniversalTime();
        var endTime = toDate.Date.AddDays(1).ToUniversalTime();

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startTime && e.Timestamp < endTime)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    #endregion

    #region Bulk Operations

    /// <inheritdoc />
    public async Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> entries)
    {
        var db = await GetConnectionAsync();
        var entryList = entries.ToList();
        if (entryList.Count == 0)
            return 0;

        await db.InsertAllAsync(entryList);
        return entryList.Count;
    }

    /// <inheritdoc />
    public async Task<int> ClearAllLocalTimelineEntriesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteAsync("DELETE FROM LocalTimelineEntries");
    }

    #endregion

    #region Sync Operations

    /// <inheritdoc />
    public async Task<bool> UpdateLocalTimelineServerIdAsync(
        DateTime timestamp,
        double latitude,
        double longitude,
        int serverId,
        int toleranceSeconds = 2)
    {
        var db = await GetConnectionAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        var affected = await db.ExecuteAsync(
            "UPDATE LocalTimelineEntries SET ServerId = ? WHERE Timestamp >= ? AND Timestamp <= ? AND Latitude = ? AND Longitude = ? AND ServerId IS NULL",
            serverId, minTime, maxTime, latitude, longitude);

        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<int> GetLocalTimelineEntryCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>().CountAsync();
    }

    #endregion
}
