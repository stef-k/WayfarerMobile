using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for location queue operations.
/// Manages GPS location capture, sync queue, and purge operations.
/// </summary>
public class LocationQueueRepository : RepositoryBase, ILocationQueueRepository
{
    private const int MaxQueuedLocations = 25000;
    private const int MaxBatchSize = 500; // SQLite parameter limit safety margin

    /// <summary>
    /// Creates a new instance of LocationQueueRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public LocationQueueRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    #region Queue Operations

    /// <inheritdoc />
    public async Task QueueLocationAsync(LocationData location)
    {
        var db = await GetConnectionAsync();

        var queued = new QueuedLocation
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.Altitude,
            Accuracy = location.Accuracy,
            Speed = location.Speed,
            Bearing = location.Bearing,
            Timestamp = location.Timestamp,
            Provider = location.Provider,
            SyncStatus = SyncStatus.Pending
        };

        await db.InsertAsync(queued);

        // Cleanup old locations if queue is too large
        await CleanupOldLocationsAsync(db);
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetPendingLocationsAsync(int limit = 100)
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> GetOldestPendingForDrainAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetLocationsForDateAsync(DateTime date)
    {
        var db = await GetConnectionAsync();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();

        return await db.Table<QueuedLocation>()
            .Where(l => l.Timestamp >= startOfDay && l.Timestamp < endOfDay)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetAllQueuedLocationsAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    #endregion

    #region Sync Status Operations

    /// <inheritdoc />
    public async Task MarkLocationSyncedAsync(int id)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Synced, id);
    }

    /// <inheritdoc />
    public async Task<int> MarkLocationsSyncedAsync(IEnumerable<int> ids)
    {
        var db = await GetConnectionAsync();

        var idList = ids as List<int> ?? ids.ToList();
        if (idList.Count == 0)
            return 0;

        // Process in batches to avoid SQLite parameter limits
        var totalUpdated = 0;
        foreach (var batch in idList.Chunk(MaxBatchSize))
        {
            var batchList = batch.ToList();
            var placeholders = string.Join(",", Enumerable.Repeat("?", batchList.Count));
            var query = $"UPDATE QueuedLocations SET SyncStatus = ? WHERE Id IN ({placeholders})";

            // Build parameters: first is SyncStatus, rest are IDs
            var parameters = new object[batchList.Count + 1];
            parameters[0] = (int)SyncStatus.Synced;
            for (var i = 0; i < batchList.Count; i++)
            {
                parameters[i + 1] = batchList[i];
            }

            totalUpdated += await db.ExecuteAsync(query, parameters);
        }

        return totalUpdated;
    }

    /// <inheritdoc />
    public async Task MarkLocationFailedAsync(int id, string error)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncAttempts = SyncAttempts + 1,
                  LastSyncAttempt = ?,
                  LastError = ?
              WHERE Id = ?",
            DateTime.UtcNow, error, id);
    }

    /// <inheritdoc />
    public async Task MarkLocationRejectedAsync(int id, string reason)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET IsRejected = 1,
                  RejectionReason = ?,
                  SyncStatus = ?,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            reason, (int)SyncStatus.Synced, DateTime.UtcNow, id);
    }

    /// <inheritdoc />
    public async Task MarkLocationSyncingAsync(int id)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Syncing, id);
    }

    /// <inheritdoc />
    public async Task IncrementRetryCountAsync(int id)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncAttempts = SyncAttempts + 1, LastSyncAttempt = ? WHERE Id = ?",
            DateTime.UtcNow, id);
    }

    /// <inheritdoc />
    public async Task ResetLocationToPendingAsync(int id)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            (int)SyncStatus.Pending, DateTime.UtcNow, id);
    }

    /// <inheritdoc />
    public async Task<int> ResetStuckLocationsAsync()
    {
        var db = await GetConnectionAsync();

        return await db.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE SyncStatus = ?",
            (int)SyncStatus.Pending, (int)SyncStatus.Syncing);
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> ClaimPendingLocationsAsync(int limit)
    {
        var db = await GetConnectionAsync();

        // Step 1: Get candidate pending locations (oldest first)
        var pendingLocations = await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();

        if (pendingLocations.Count == 0)
            return [];

        // Step 2: Claim each location individually with atomic check-and-set
        // This ensures we only process locations WE successfully claimed,
        // preventing race conditions with QueueDrainService
        var claimedIds = new List<int>();
        foreach (var location in pendingLocations)
        {
            var updated = await db.ExecuteAsync(
                "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, location.Id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                claimedIds.Add(location.Id);
            }
        }

        if (claimedIds.Count == 0)
            return [];

        // Step 3: Fetch fresh copies of claimed locations from database
        // Using raw SQL to avoid LINQ Contains() translation issues
        var placeholders = string.Join(",", Enumerable.Repeat("?", claimedIds.Count));
        var selectQuery = $"SELECT * FROM QueuedLocations WHERE Id IN ({placeholders}) ORDER BY Timestamp";
        return await db.QueryAsync<QueuedLocation>(selectQuery, claimedIds.Cast<object>().ToArray());
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> ClaimOldestPendingLocationAsync()
    {
        var db = await GetConnectionAsync();

        // Step 1: Get the oldest pending location
        var location = await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();

        if (location == null)
            return null;

        // Step 2: Atomically claim it (only if still Pending)
        var updated = await db.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ? AND SyncStatus = ?",
            (int)SyncStatus.Syncing, location.Id, (int)SyncStatus.Pending);

        if (updated == 0)
        {
            // Another service claimed it first - return null
            return null;
        }

        // Step 3: Fetch fresh copy from database
        return await db.Table<QueuedLocation>()
            .Where(l => l.Id == location.Id)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<int> ResetLocationsBatchToPendingAsync(IEnumerable<int> ids)
    {
        var db = await GetConnectionAsync();

        var idList = ids as List<int> ?? ids.ToList();
        if (idList.Count == 0)
            return 0;

        // Process in batches to avoid SQLite parameter limits
        var totalUpdated = 0;
        foreach (var batch in idList.Chunk(MaxBatchSize))
        {
            var batchList = batch.ToList();
            var placeholders = string.Join(",", Enumerable.Repeat("?", batchList.Count));
            var query = $"UPDATE QueuedLocations SET SyncStatus = ? WHERE Id IN ({placeholders})";

            var parameters = new object[batchList.Count + 1];
            parameters[0] = (int)SyncStatus.Pending;
            for (var i = 0; i < batchList.Count; i++)
                parameters[i + 1] = batchList[i];

            totalUpdated += await db.ExecuteAsync(query, parameters);
        }

        return totalUpdated;
    }

    #endregion

    #region Cleanup Operations

    /// <inheritdoc />
    public async Task<int> PurgeSyncedLocationsAsync(int daysOld = 7)
    {
        var db = await GetConnectionAsync();

        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var deletedSynced = await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Synced, cutoff);

        // Purge rejected locations older than 2 days
        var rejectedCutoff = DateTime.UtcNow.AddDays(-2);
        var deletedRejected = await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE IsRejected = 1 AND CreatedAt < ?",
            rejectedCutoff);

        // Safety valve: purge very old pending locations (300 days)
        var pendingCutoff = DateTime.UtcNow.AddDays(-300);
        var deletedOldPending = await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Pending, pendingCutoff);

        // Purge failed locations older than 3 days
        var failedCutoff = DateTime.UtcNow.AddDays(-3);
        var deletedFailed = await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Failed, failedCutoff);

        return deletedSynced + deletedRejected + deletedOldPending + deletedFailed;
    }

    /// <inheritdoc />
    public async Task<int> ClearPendingQueueAsync()
    {
        var db = await GetConnectionAsync();

        return await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ?",
            (int)SyncStatus.Pending);
    }

    /// <inheritdoc />
    public async Task<int> ClearSyncedQueueAsync()
    {
        var db = await GetConnectionAsync();

        return await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ?",
            (int)SyncStatus.Synced);
    }

    /// <inheritdoc />
    public async Task<int> ClearAllQueueAsync()
    {
        var db = await GetConnectionAsync();

        return await db.ExecuteAsync("DELETE FROM QueuedLocations");
    }

    /// <summary>
    /// Cleans up old locations if queue is too large.
    /// </summary>
    private async Task CleanupOldLocationsAsync(SQLiteAsyncConnection db)
    {
        var count = await db.Table<QueuedLocation>().CountAsync();
        if (count > MaxQueuedLocations)
        {
            await db.ExecuteAsync(
                "DELETE FROM QueuedLocations WHERE Id IN (SELECT Id FROM QueuedLocations WHERE SyncStatus = ? ORDER BY Timestamp LIMIT ?)",
                (int)SyncStatus.Synced, count - MaxQueuedLocations + 1000);
        }
    }

    #endregion

    #region Diagnostic Queries

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .CountAsync();
    }

    /// <inheritdoc />
    public Task<int> GetPendingLocationCountAsync()
        => GetPendingCountAsync(); // Delegate to avoid duplicate implementation

    /// <inheritdoc />
    public async Task<int> GetRejectedLocationCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.IsRejected)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetSyncedLocationCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetFailedLocationCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Failed)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> GetOldestPendingLocationAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> GetLastSyncedLocationAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .OrderByDescending(l => l.LastSyncAttempt)
            .FirstOrDefaultAsync();
    }

    #endregion
}
