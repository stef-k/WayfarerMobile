using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Helpers;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for location queue operations.
/// Manages GPS location capture, sync queue, and purge operations.
/// </summary>
public class LocationQueueRepository : RepositoryBase, ILocationQueueRepository
{
    private readonly ISettingsService _settingsService;
    private const int MaxBatchSize = 500; // SQLite parameter limit safety margin

    /// <summary>
    /// Creates a new instance of LocationQueueRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    /// <param name="settingsService">Settings service for queue limit configuration.</param>
    public LocationQueueRepository(
        Func<Task<SQLiteAsyncConnection>> connectionFactory,
        ISettingsService settingsService)
        : base(connectionFactory)
    {
        _settingsService = settingsService;
    }

    #region Queue Operations

    /// <inheritdoc />
    public async Task<int> QueueLocationAsync(
        LocationData location,
        bool isUserInvoked = false,
        int? activityTypeId = null,
        string? notes = null)
    {
        // Validate coordinates to prevent corrupted data
        if (!IsValidCoordinate(location.Latitude, location.Longitude))
        {
            throw new ArgumentException(
                $"Invalid coordinates: Lat={location.Latitude}, Lon={location.Longitude}. " +
                "Coordinates must be finite numbers within valid ranges.");
        }

        var db = await GetConnectionAsync();

        var queued = new QueuedLocation
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = SanitizeOptionalDouble(location.Altitude),
            Accuracy = SanitizeOptionalDouble(location.Accuracy),
            Speed = SanitizeOptionalDouble(location.Speed),
            Bearing = SanitizeOptionalDouble(location.Bearing),
            Timestamp = location.Timestamp,
            Provider = location.Provider,
            SyncStatus = SyncStatus.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("N"), // Unique key for idempotent sync
            IsUserInvoked = isUserInvoked,
            ActivityTypeId = activityTypeId,
            CheckInNotes = notes,
            // Metadata fields for diagnostics and export
            Source = isUserInvoked ? "mobile-checkin" : "mobile-log",
            TimeZoneId = DeviceMetadataHelper.GetTimeZoneId(),
            AppVersion = DeviceMetadataHelper.GetAppVersion(),
            AppBuild = DeviceMetadataHelper.GetAppBuild(),
            DeviceModel = DeviceMetadataHelper.GetDeviceModel(),
            OsVersion = DeviceMetadataHelper.GetOsVersion(),
            BatteryLevel = DeviceMetadataHelper.GetBatteryLevel(),
            IsCharging = DeviceMetadataHelper.GetIsCharging()
        };

        await db.InsertAsync(queued);

        // Cleanup old locations if queue is too large (uses user-configured limit)
        await CleanupOldLocationsAsync(db, _settingsService.QueueLimitMaxLocations);

        return queued.Id;
    }

    /// <summary>
    /// Validates that latitude and longitude are valid, finite numbers within range.
    /// </summary>
    private static bool IsValidCoordinate(double latitude, double longitude)
    {
        // Check for NaN, Infinity
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) ||
            double.IsNaN(longitude) || double.IsInfinity(longitude))
        {
            return false;
        }

        // Check valid ranges
        if (latitude < -90 || latitude > 90 ||
            longitude < -180 || longitude > 180)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sanitizes optional double values, replacing NaN/Infinity with null.
    /// </summary>
    private static double? SanitizeOptionalDouble(double? value)
    {
        if (value == null)
            return null;

        if (double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;

        return value;
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetLocationsForDateAsync(DateTime date)
    {
        var db = await GetConnectionAsync();

        // Normalize input date to ensure consistent UTC comparison with stored timestamps.
        // - If date is UTC: use as-is (midnight UTC to midnight UTC next day)
        // - If date is Local or Unspecified: treat as local time and convert to UTC
        //   This gives the user's local day boundaries in UTC for proper filtering.
        var localDate = date.Kind == DateTimeKind.Utc
            ? date.ToLocalTime().Date
            : date.Date;

        // Get UTC boundaries for the local day
        var startOfDay = DateTime.SpecifyKind(localDate, DateTimeKind.Local).ToUniversalTime();
        var endOfDay = DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Local).ToUniversalTime();

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

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetAllQueuedLocationsForExportAsync()
    {
        var db = await GetConnectionAsync();
        return await db.QueryAsync<QueuedLocation>(
            "SELECT * FROM QueuedLocations ORDER BY Timestamp ASC, Id ASC");
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
    public async Task MarkServerConfirmedAsync(int id, int? serverId = null)
    {
        var db = await GetConnectionAsync();

        // Mark as ServerConfirmed BEFORE updating to Synced
        // This ensures crash recovery can complete the transition
        // Also store ServerId for local timeline reconciliation on crash recovery
        await db.ExecuteAsync(
            "UPDATE QueuedLocations SET ServerConfirmed = 1, ServerId = ? WHERE Id = ?",
            serverId, id);
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
            // batch is already int[] from Chunk()
            var placeholders = string.Join(",", Enumerable.Repeat("?", batch.Length));
            var query = $"UPDATE QueuedLocations SET SyncStatus = ? WHERE Id IN ({placeholders})";

            // Build parameters: first is SyncStatus, rest are IDs
            var parameters = new object[batch.Length + 1];
            parameters[0] = (int)SyncStatus.Synced;
            for (var i = 0; i < batch.Length; i++)
            {
                parameters[i + 1] = batch[i];
            }

            totalUpdated += await db.ExecuteAsync(query, parameters);
        }

        return totalUpdated;
    }

    /// <inheritdoc />
    public async Task MarkLocationFailedAsync(int id, string error)
    {
        var db = await GetConnectionAsync();

        // Reset SyncStatus to Pending so location can be retried
        // Previously only tracked error without reset, requiring batch reset or periodic cleanup
        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = CASE WHEN ServerConfirmed = 1 THEN ? ELSE ? END,
                  SyncAttempts = SyncAttempts + CASE WHEN ServerConfirmed = 1 THEN 0 ELSE 1 END,
                  LastSyncAttempt = ?,
                  LastError = ?
              WHERE Id = ?",
            (int)SyncStatus.Synced, (int)SyncStatus.Pending, DateTime.UtcNow, error, id);
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
    public async Task IncrementRetryCountAsync(int id)
    {
        var db = await GetConnectionAsync();

        // Reset SyncStatus to Pending so location can be retried
        // Also increment attempt count for diagnostics
        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = CASE WHEN ServerConfirmed = 1 THEN ? ELSE ? END,
                  SyncAttempts = SyncAttempts + CASE WHEN ServerConfirmed = 1 THEN 0 ELSE 1 END,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            (int)SyncStatus.Synced, (int)SyncStatus.Pending, DateTime.UtcNow, id);
    }

    /// <inheritdoc />
    public async Task ResetLocationToPendingAsync(int id)
    {
        var db = await GetConnectionAsync();

        await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = CASE WHEN ServerConfirmed = 1 THEN ? ELSE ? END,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            (int)SyncStatus.Synced, (int)SyncStatus.Pending, DateTime.UtcNow, id);
    }

    /// <inheritdoc />
    public async Task<int> ResetStuckLocationsAsync()
    {
        var db = await GetConnectionAsync();

        // First, mark ServerConfirmed locations as Synced (crash recovery - server has them)
        // This prevents duplicate sync attempts for locations the server already received
        var confirmedCount = await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?
              WHERE SyncStatus = ? AND ServerConfirmed = 1",
            (int)SyncStatus.Synced, (int)SyncStatus.Syncing);

        // Then reset remaining stuck locations (not ServerConfirmed) to Pending for retry
        var resetCount = await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?
              WHERE SyncStatus = ? AND ServerConfirmed = 0",
            (int)SyncStatus.Pending, (int)SyncStatus.Syncing);

        return confirmedCount + resetCount;
    }

    /// <inheritdoc />
    public async Task<int> ResetTimedOutSyncingLocationsAsync(int stuckThresholdMinutes = 30)
    {
        var db = await GetConnectionAsync();

        var cutoff = DateTime.UtcNow.AddMinutes(-stuckThresholdMinutes);

        // First, mark ServerConfirmed timed-out locations as Synced
        // Handle NULL LastSyncAttempt for legacy data (before tracking was added)
        var confirmedCount = await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?
              WHERE SyncStatus = ? AND ServerConfirmed = 1
                AND (LastSyncAttempt IS NULL OR LastSyncAttempt < ?)",
            (int)SyncStatus.Synced, (int)SyncStatus.Syncing, cutoff);

        // Then reset remaining timed-out locations to Pending
        // Handle NULL LastSyncAttempt for legacy data (before tracking was added)
        var resetCount = await db.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?
              WHERE SyncStatus = ? AND ServerConfirmed = 0
                AND (LastSyncAttempt IS NULL OR LastSyncAttempt < ?)",
            (int)SyncStatus.Pending, (int)SyncStatus.Syncing, cutoff);

        return confirmedCount + resetCount;
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> ClaimPendingLocationsAsync(int limit)
    {
        var db = await GetConnectionAsync();

        // Step 1: Get candidate IDs atomically with a snapshot query
        var candidateIds = await db.QueryScalarsAsync<int>(
            @"SELECT Id FROM QueuedLocations
              WHERE SyncStatus = ? AND IsRejected = 0
              ORDER BY Timestamp
              LIMIT ?",
            (int)SyncStatus.Pending, limit);

        if (candidateIds.Count == 0)
            return [];

        // Step 2: Claim each location individually with atomic check-and-set
        // This ensures we only track locations WE successfully claimed
        // (other services may claim between SELECT and UPDATE - that's fine)
        var claimedIds = new List<int>();
        var now = DateTime.UtcNow;

        foreach (var id in candidateIds)
        {
            // Atomic claim: only succeeds if row is still Pending
            var updated = await db.ExecuteAsync(
                @"UPDATE QueuedLocations
                  SET SyncStatus = ?, LastSyncAttempt = ?,
                      IdempotencyKey = COALESCE(IdempotencyKey, ?)
                  WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, now, Guid.NewGuid().ToString("N"), id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                claimedIds.Add(id);
            }
        }

        if (claimedIds.Count == 0)
            return [];

        // Step 3: Batch fetch claimed locations by exact IDs
        // Process in batches for SQLite parameter limits
        var results = new List<QueuedLocation>();
        foreach (var batch in claimedIds.Chunk(MaxBatchSize))
        {
            // batch is already int[] from Chunk()
            var placeholders = string.Join(",", Enumerable.Repeat("?", batch.Length));
            var fetchQuery = $"SELECT * FROM QueuedLocations WHERE Id IN ({placeholders}) ORDER BY Timestamp";
            var fetched = await db.QueryAsync<QueuedLocation>(fetchQuery, batch.Cast<object>().ToArray());
            results.AddRange(fetched);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> ClaimOldestPendingLocationAsync(int candidateLimit = 5)
    {
        var db = await GetConnectionAsync();

        if (candidateLimit <= 0)
            return null;

        // Step 1: Get a small batch of oldest pending IDs
        var candidateIds = await db.QueryScalarsAsync<int>(
            @"SELECT Id FROM QueuedLocations
              WHERE SyncStatus = ? AND IsRejected = 0
              ORDER BY Timestamp
              LIMIT ?",
            (int)SyncStatus.Pending, candidateLimit);

        if (candidateIds.Count == 0)
            return null;

        // Step 2: Atomically claim the first available candidate
        // Also update LastSyncAttempt for consistency with ClaimPendingLocationsAsync
        var now = DateTime.UtcNow;
        foreach (var id in candidateIds)
        {
            var updated = await db.ExecuteAsync(
                @"UPDATE QueuedLocations
                  SET SyncStatus = ?, LastSyncAttempt = ?,
                      IdempotencyKey = COALESCE(IdempotencyKey, ?)
                  WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, now, Guid.NewGuid().ToString("N"), id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                // Step 3: Fetch fresh copy from database
                return await db.Table<QueuedLocation>()
                    .Where(l => l.Id == id)
                    .FirstOrDefaultAsync();
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> ClaimNextPendingLocationWithPriorityAsync()
    {
        var db = await GetConnectionAsync();

        // PRIORITY 1: User-invoked locations (manual check-ins)
        var userInvokedIds = await db.QueryScalarsAsync<int>(
            @"SELECT Id FROM QueuedLocations
              WHERE SyncStatus = ? AND IsRejected = 0 AND IsUserInvoked = 1
              ORDER BY Timestamp
              LIMIT 5",
            (int)SyncStatus.Pending);

        var now = DateTime.UtcNow;

        // Try to claim a user-invoked location first
        foreach (var id in userInvokedIds)
        {
            var updated = await db.ExecuteAsync(
                @"UPDATE QueuedLocations
                  SET SyncStatus = ?, LastSyncAttempt = ?,
                      IdempotencyKey = COALESCE(IdempotencyKey, ?)
                  WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, now, Guid.NewGuid().ToString("N"), id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                return await db.Table<QueuedLocation>()
                    .Where(l => l.Id == id)
                    .FirstOrDefaultAsync();
            }
        }

        // PRIORITY 2: Background/live locations (use existing claim method)
        return await ClaimOldestPendingLocationAsync();
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
            // batch is already int[] from Chunk()
            var placeholders = string.Join(",", Enumerable.Repeat("?", batch.Length));
            var query = $"UPDATE QueuedLocations SET SyncStatus = CASE WHEN ServerConfirmed = 1 THEN ? ELSE ? END WHERE Id IN ({placeholders})";

            var parameters = new object[batch.Length + 2];
            parameters[0] = (int)SyncStatus.Synced;
            parameters[1] = (int)SyncStatus.Pending;
            for (var i = 0; i < batch.Length; i++)
                parameters[i + 2] = batch[i];

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

        return deletedSynced + deletedRejected + deletedOldPending;
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
    public async Task<int> ClearSyncedAndRejectedQueueAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE (SyncStatus = ? AND IsRejected = 0) OR IsRejected = 1",
            (int)SyncStatus.Synced);
    }

    /// <inheritdoc />
    public async Task<int> ClearAllQueueAsync()
    {
        var db = await GetConnectionAsync();

        return await db.ExecuteAsync("DELETE FROM QueuedLocations");
    }

    /// <inheritdoc />
    public async Task CleanupOldLocationsAsync(int maxQueuedLocations)
    {
        var db = await GetConnectionAsync();
        await CleanupOldLocationsAsync(db, maxQueuedLocations);
    }

    /// <summary>
    /// Cleans up old locations if queue is at capacity.
    /// Gradual deletion: removes entries to make room for new ones.
    /// Priority: 1) Oldest Synced, 2) Oldest Rejected, 3) Oldest Pending (last resort).
    /// Never removes Syncing entries (in-flight protection).
    /// </summary>
    private async Task CleanupOldLocationsAsync(SQLiteAsyncConnection db, int maxQueuedLocations)
    {
        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
        if (count < maxQueuedLocations)
            return;

        var toDelete = count - maxQueuedLocations + 1;

        // Delete oldest safe entries (synced or rejected) first
        var safeDeleted = await db.ExecuteAsync(@"
            DELETE FROM QueuedLocations WHERE Id IN (
                SELECT Id FROM QueuedLocations
                WHERE (SyncStatus = ? AND IsRejected = 0) OR IsRejected = 1
                ORDER BY Timestamp, Id
                LIMIT ?
            )", (int)SyncStatus.Synced, toDelete);

        if (safeDeleted >= toDelete)
            return;

        var remaining = toDelete - safeDeleted;

        // Last resort: delete oldest pending entries (not syncing)
        await db.ExecuteAsync(@"
            DELETE FROM QueuedLocations WHERE Id IN (
                SELECT Id FROM QueuedLocations
                WHERE SyncStatus = ? AND IsRejected = 0
                ORDER BY Timestamp, Id
                LIMIT ?
            )", (int)SyncStatus.Pending, remaining);
    }

    #endregion

    #region Diagnostic Queries

    /// <inheritdoc />
    public async Task<int> GetTotalCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
    }

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<int> GetSyncingCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM QueuedLocations WHERE SyncStatus = ?",
            (int)SyncStatus.Syncing);
    }

    /// <inheritdoc />
    public async Task<int> GetRetryingCountAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected && l.SyncAttempts > 0)
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

        // Exclude rejected locations (they have SyncStatus=Synced + IsRejected=true)
        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced && !l.IsRejected)
            .CountAsync();
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> GetOldestPendingLocationAsync()
    {
        var db = await GetConnectionAsync();

        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<QueuedLocation?> GetNewestPendingLocationAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderByDescending(l => l.Timestamp)
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

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetConfirmedEntriesWithServerIdAsync(DateTime? sinceTimestamp = null)
    {
        var db = await GetConnectionAsync();

        if (sinceTimestamp.HasValue)
        {
            return await db.Table<QueuedLocation>()
                .Where(l => l.ServerConfirmed && l.ServerId != null && l.Timestamp >= sinceTimestamp.Value)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
        }

        return await db.Table<QueuedLocation>()
            .Where(l => l.ServerConfirmed && l.ServerId != null)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<QueuedLocation>> GetNonRejectedEntriesForBackfillAsync(DateTime? sinceTimestamp = null)
    {
        var db = await GetConnectionAsync();

        if (sinceTimestamp.HasValue)
        {
            return await db.Table<QueuedLocation>()
                .Where(l => !l.IsRejected && l.Timestamp >= sinceTimestamp.Value)
                .OrderBy(l => l.Timestamp)
                .ToListAsync();
        }

        return await db.Table<QueuedLocation>()
            .Where(l => !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .ToListAsync();
    }

    #endregion
}
