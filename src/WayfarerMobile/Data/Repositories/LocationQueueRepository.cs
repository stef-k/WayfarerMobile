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
        Console.WriteLine($"[LocationQueueRepository] Location queued: {location}");

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

        var idList = ids.ToList();
        if (idList.Count == 0)
            return 0;

        // Build parameterized query with IN clause
        var placeholders = string.Join(",", idList.Select((_, i) => $"?{i + 2}"));
        var query = $"UPDATE QueuedLocations SET SyncStatus = ?1 WHERE Id IN ({placeholders})";

        // Build parameters: first is SyncStatus, rest are IDs
        var parameters = new object[idList.Count + 1];
        parameters[0] = (int)SyncStatus.Synced;
        for (var i = 0; i < idList.Count; i++)
        {
            parameters[i + 1] = idList[i];
        }

        return await db.ExecuteAsync(query, parameters);
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

        // Step 1: Get IDs of pending locations (oldest first)
        var pendingLocations = await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();

        if (pendingLocations.Count == 0)
            return new List<QueuedLocation>();

        var pendingIds = pendingLocations.Select(l => l.Id).ToList();

        // Step 2: Atomically claim them (only if still Pending - prevents race with QueueDrainService)
        var placeholders = string.Join(",", pendingIds.Select((_, i) => $"?{i + 2}"));
        var updateQuery = $"UPDATE QueuedLocations SET SyncStatus = ?1 WHERE Id IN ({placeholders}) AND SyncStatus = ?{pendingIds.Count + 2}";

        var updateParams = new object[pendingIds.Count + 2];
        updateParams[0] = (int)SyncStatus.Syncing;
        for (var i = 0; i < pendingIds.Count; i++)
            updateParams[i + 1] = pendingIds[i];
        updateParams[pendingIds.Count + 1] = (int)SyncStatus.Pending;

        var claimedCount = await db.ExecuteAsync(updateQuery, updateParams);

        if (claimedCount == 0)
            return new List<QueuedLocation>();

        // Step 3: Return only the locations we successfully claimed (now Syncing)
        // If claimedCount < pendingIds.Count, some were taken by QueueDrainService
        if (claimedCount == pendingIds.Count)
        {
            // All claimed - update in-memory objects and return
            foreach (var loc in pendingLocations)
                loc.SyncStatus = SyncStatus.Syncing;
            return pendingLocations;
        }

        // Partial claim - re-fetch to get only the ones we actually claimed
        return await db.Table<QueuedLocation>()
            .Where(l => pendingIds.Contains(l.Id) && l.SyncStatus == SyncStatus.Syncing)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<int> ResetLocationsBatchToPendingAsync(IEnumerable<int> ids)
    {
        var db = await GetConnectionAsync();

        var idList = ids.ToList();
        if (idList.Count == 0)
            return 0;

        var placeholders = string.Join(",", idList.Select((_, i) => $"?{i + 2}"));
        var query = $"UPDATE QueuedLocations SET SyncStatus = ?1 WHERE Id IN ({placeholders})";

        var parameters = new object[idList.Count + 1];
        parameters[0] = (int)SyncStatus.Pending;
        for (var i = 0; i < idList.Count; i++)
            parameters[i + 1] = idList[i];

        return await db.ExecuteAsync(query, parameters);
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

            Console.WriteLine("[LocationQueueRepository] Cleaned up old synced locations");
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
    public async Task<int> GetPendingLocationCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .CountAsync();
    }

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
