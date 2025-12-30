using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Services;

/// <summary>
/// Service for managing SQLite database operations including location queue and settings.
/// </summary>
public class DatabaseService : IAsyncDisposable
{
    #region Constants

    private const string DatabaseFilename = "wayfarer.db3";
    private const int MaxQueuedLocations = 25000;

    private static readonly SQLiteOpenFlags DbFlags =
        SQLiteOpenFlags.ReadWrite |
        SQLiteOpenFlags.Create |
        SQLiteOpenFlags.SharedCache;

    #endregion

    #region Fields

    private SQLiteAsyncConnection? _database;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    #endregion

    #region Properties

    /// <summary>
    /// Gets the database file path.
    /// </summary>
    public static string DatabasePath =>
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);

    /// <summary>
    /// Gets the database connection for sync services.
    /// Prefer using dedicated methods where available.
    /// </summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        await EnsureInitializedAsync();
        return _database!;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Ensures the database is initialized.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            _database = new SQLiteAsyncConnection(DatabasePath, DbFlags);

            // Create tables
            await _database.CreateTableAsync<QueuedLocation>();
            await _database.CreateTableAsync<AppSetting>();
            await _database.CreateTableAsync<DownloadedTripEntity>();
            await _database.CreateTableAsync<TripTileEntity>();
            await _database.CreateTableAsync<OfflinePlaceEntity>();
            await _database.CreateTableAsync<OfflineSegmentEntity>();
            await _database.CreateTableAsync<OfflineAreaEntity>();
            await _database.CreateTableAsync<OfflinePolygonEntity>();
            await _database.CreateTableAsync<LiveTileEntity>();
            await _database.CreateTableAsync<ActivityType>();
            await _database.CreateTableAsync<LocalTimelineEntry>();
            await _database.CreateTableAsync<TripDownloadStateEntity>();

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[DatabaseService] Initialized: {DatabasePath}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    #endregion

    #region Location Queue

    /// <summary>
    /// Queues a location for server synchronization.
    /// </summary>
    /// <param name="location">The location data to queue.</param>
    public async Task QueueLocationAsync(LocationData location)
    {
        await EnsureInitializedAsync();

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

        await _database!.InsertAsync(queued);
        System.Diagnostics.Debug.WriteLine($"[DatabaseService] Location queued: {location}");

        // Cleanup old locations if queue is too large
        await CleanupOldLocationsAsync();
    }

    /// <summary>
    /// Gets all pending locations for synchronization.
    /// Excludes rejected locations (they should not be retried).
    /// Valid locations retry until 300-day purge regardless of attempt count.
    /// </summary>
    /// <param name="limit">Maximum number of locations to retrieve.</param>
    /// <returns>List of pending locations.</returns>
    public async Task<List<QueuedLocation>> GetPendingLocationsAsync(int limit = 100)
    {
        await EnsureInitializedAsync();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Marks a location as successfully synced.
    /// Uses single UPDATE statement for efficiency.
    /// </summary>
    /// <param name="id">The location ID.</param>
    public async Task MarkLocationSyncedAsync(int id)
    {
        await EnsureInitializedAsync();

        await _database!.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Synced, id);
    }

    /// <summary>
    /// Marks multiple locations as successfully synced in a single batch operation.
    /// Uses single UPDATE statement with IN clause for efficiency.
    /// </summary>
    /// <param name="ids">The location IDs to mark as synced.</param>
    /// <returns>The number of rows affected.</returns>
    public async Task<int> MarkLocationsSyncedAsync(IEnumerable<int> ids)
    {
        await EnsureInitializedAsync();

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

        return await _database!.ExecuteAsync(query, parameters);
    }

    /// <summary>
    /// Records a sync failure for diagnostics. Location stays Pending for retry.
    /// Valid locations retry until 300-day purge regardless of attempt count.
    /// SyncAttempts is only for diagnostics, not a retry limit.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="error">The error message.</param>
    public async Task MarkLocationFailedAsync(int id, string error)
    {
        await EnsureInitializedAsync();

        // Increment attempts counter for diagnostics but keep status as Pending
        // Valid locations should retry until 300-day purge, not 5 attempts
        await _database!.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncAttempts = SyncAttempts + 1,
                  LastSyncAttempt = ?,
                  LastError = ?
              WHERE Id = ?",
            DateTime.UtcNow, error, id);
    }

    /// <summary>
    /// Increments the retry count for a location without marking it as failed.
    /// Used for transient failures that may succeed on retry.
    /// Uses single UPDATE statement for efficiency.
    /// </summary>
    /// <param name="id">The location ID.</param>
    public async Task IncrementRetryCountAsync(int id)
    {
        await EnsureInitializedAsync();

        await _database!.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncAttempts = SyncAttempts + 1, LastSyncAttempt = ? WHERE Id = ?",
            DateTime.UtcNow, id);
    }

    /// <summary>
    /// Marks a location as rejected (by client threshold check or server).
    /// Rejected locations should not be retried.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="reason">The rejection reason (e.g., "Client: Time 2min &lt; 5min threshold" or "Server: HTTP 400").</param>
    public async Task MarkLocationRejectedAsync(int id, string reason)
    {
        await EnsureInitializedAsync();

        await _database!.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET IsRejected = 1,
                  RejectionReason = ?,
                  SyncStatus = ?,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            reason, (int)SyncStatus.Synced, DateTime.UtcNow, id);
    }

    /// <summary>
    /// Marks a location as currently syncing.
    /// Used to prevent duplicate processing when lock is released.
    /// </summary>
    /// <param name="id">The location ID.</param>
    public async Task MarkLocationSyncingAsync(int id)
    {
        await EnsureInitializedAsync();

        await _database!.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Syncing, id);
    }

    /// <summary>
    /// Resets a location back to pending status for retry after transient failures.
    /// Used when server returns 5xx or network errors - these are not permanent rejections.
    /// Does NOT increment SyncAttempts since transient failures shouldn't count toward max retries.
    /// Valid location data should be retried indefinitely until server is reachable.
    /// </summary>
    /// <param name="id">The location ID.</param>
    public async Task ResetLocationToPendingAsync(int id)
    {
        await EnsureInitializedAsync();

        await _database!.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncStatus = ?,
                  LastSyncAttempt = ?
              WHERE Id = ?",
            (int)SyncStatus.Pending, DateTime.UtcNow, id);
    }

    /// <summary>
    /// Gets the oldest pending location for queue drain processing.
    /// Excludes rejected locations only - valid locations retry until 300-day purge.
    /// Returns locations ordered by timestamp (oldest first) for chronological processing.
    /// </summary>
    /// <returns>The oldest pending location or null if queue is empty.</returns>
    public async Task<QueuedLocation?> GetOldestPendingForDrainAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Resets locations stuck in "Syncing" status back to "Pending".
    /// Called on app startup to recover from crashes during sync.
    /// </summary>
    /// <returns>Number of locations reset.</returns>
    public async Task<int> ResetStuckLocationsAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE SyncStatus = ?",
            (int)SyncStatus.Pending, (int)SyncStatus.Syncing);
    }

    /// <summary>
    /// Removes synced locations older than the specified days.
    /// Also purges rejected and failed locations with appropriate retention periods.
    /// </summary>
    /// <param name="daysOld">Number of days old.</param>
    public async Task<int> PurgeSyncedLocationsAsync(int daysOld = 7)
    {
        await EnsureInitializedAsync();

        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var deletedSynced = await _database!.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Synced, cutoff);

        // Purge rejected locations older than 2 days
        var rejectedCutoff = DateTime.UtcNow.AddDays(-2);
        var deletedRejected = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE IsRejected = 1 AND CreatedAt < ?",
            rejectedCutoff);

        // Safety valve: purge very old pending locations
        // With 25k queue capacity and ~96 locations/day max, queue holds ~260 days of data.
        // Use 300 days as cutoff to exceed queue capacity and allow ample time for offline scenarios.
        var pendingCutoff = DateTime.UtcNow.AddDays(-300);
        var deletedOldPending = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Pending, pendingCutoff);

        // Purge failed locations (max attempts exceeded) older than 3 days
        var failedCutoff = DateTime.UtcNow.AddDays(-3);
        var deletedFailed = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Failed, failedCutoff);

        return deletedSynced + deletedRejected + deletedOldPending + deletedFailed;
    }

    /// <summary>
    /// Gets the count of pending locations that can be synced.
    /// Excludes rejected locations to match drain logic.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending &&
                       !l.IsRejected)
            .CountAsync();
    }

    /// <summary>
    /// Clears all pending locations from the queue.
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    public async Task<int> ClearPendingQueueAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ?",
            (int)SyncStatus.Pending);
    }

    /// <summary>
    /// Clears all synced locations from the queue.
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    public async Task<int> ClearSyncedQueueAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ?",
            (int)SyncStatus.Synced);
    }

    /// <summary>
    /// Clears all locations from the queue (pending, synced, and failed).
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    public async Task<int> ClearAllQueueAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.ExecuteAsync("DELETE FROM QueuedLocations");
    }

    /// <summary>
    /// Gets all locations for a specific date.
    /// </summary>
    /// <param name="date">The date to retrieve locations for.</param>
    /// <returns>List of locations for that date.</returns>
    public async Task<List<QueuedLocation>> GetLocationsForDateAsync(DateTime date)
    {
        await EnsureInitializedAsync();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.Timestamp >= startOfDay && l.Timestamp < endOfDay)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Cleans up old locations if queue is too large.
    /// </summary>
    private async Task CleanupOldLocationsAsync()
    {
        var count = await _database!.Table<QueuedLocation>().CountAsync();
        if (count > MaxQueuedLocations)
        {
            // Remove oldest synced locations first
            await _database.ExecuteAsync(
                "DELETE FROM QueuedLocations WHERE Id IN (SELECT Id FROM QueuedLocations WHERE SyncStatus = ? ORDER BY Timestamp LIMIT ?)",
                (int)SyncStatus.Synced, count - MaxQueuedLocations + 1000);

            System.Diagnostics.Debug.WriteLine("[DatabaseService] Cleaned up old synced locations");
        }
    }

    #endregion

    #region Settings

    /// <summary>
    /// Gets a setting value by key.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="defaultValue">Default value if not found.</param>
    /// <returns>The setting value or default.</returns>
    public async Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
    {
        await EnsureInitializedAsync();

        var setting = await _database!.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);

        if (setting?.Value == null)
            return defaultValue;

        try
        {
            if (typeof(T) == typeof(string))
                return (T)(object)setting.Value;

            if (typeof(T) == typeof(bool))
                return (T)(object)bool.Parse(setting.Value);

            if (typeof(T) == typeof(int))
                return (T)(object)int.Parse(setting.Value);

            if (typeof(T) == typeof(double))
                return (T)(object)double.Parse(setting.Value);

            return defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Sets a setting value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value to store.</param>
    public async Task SetSettingAsync<T>(string key, T value)
    {
        await EnsureInitializedAsync();

        var setting = await _database!.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);

        var stringValue = value?.ToString();

        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = key,
                Value = stringValue
            };
            await _database.InsertAsync(setting);
        }
        else
        {
            setting.Value = stringValue;
            setting.LastModified = DateTime.UtcNow;
            await _database.UpdateAsync(setting);
        }
    }

    #endregion

    #region Downloaded Trips

    /// <summary>
    /// Gets all downloaded trips.
    /// </summary>
    public async Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<DownloadedTripEntity>()
            .OrderByDescending(t => t.DownloadedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets a downloaded trip by server ID.
    /// </summary>
    public async Task<DownloadedTripEntity?> GetDownloadedTripByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.ServerId == serverId);
    }

    /// <summary>
    /// Gets a downloaded trip by local ID.
    /// </summary>
    public async Task<DownloadedTripEntity?> GetDownloadedTripAsync(int id)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Saves a downloaded trip (insert or update).
    /// </summary>
    public async Task<int> SaveDownloadedTripAsync(DownloadedTripEntity trip)
    {
        await EnsureInitializedAsync();
        trip.UpdatedAt = DateTime.UtcNow;

        if (trip.Id == 0)
        {
            await _database!.InsertAsync(trip);
        }
        else
        {
            await _database!.UpdateAsync(trip);
        }
        return trip.Id;
    }

    /// <summary>
    /// Deletes a downloaded trip and all associated data.
    /// </summary>
    public async Task DeleteDownloadedTripAsync(int tripId)
    {
        await EnsureInitializedAsync();

        // Delete associated tiles
        await _database!.ExecuteAsync(
            "DELETE FROM TripTiles WHERE TripId = ?", tripId);

        // Delete associated places
        await _database!.ExecuteAsync(
            "DELETE FROM OfflinePlaces WHERE TripId = ?", tripId);

        // Delete associated segments
        await _database!.ExecuteAsync(
            "DELETE FROM OfflineSegments WHERE TripId = ?", tripId);

        // Delete associated areas
        await _database!.ExecuteAsync(
            "DELETE FROM OfflineAreas WHERE TripId = ?", tripId);

        // Delete the trip
        await _database!.ExecuteAsync(
            "DELETE FROM DownloadedTrips WHERE Id = ?", tripId);
    }

    /// <summary>
    /// Deletes only the cached map tiles for a trip, keeping trip data intact.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of file paths that were deleted from database.</returns>
    public async Task<List<string>> DeleteTripTilesAsync(int tripId)
    {
        await EnsureInitializedAsync();

        // Get tile file paths before deleting
        var tiles = await _database!.Table<TripTileEntity>()
            .Where(t => t.TripId == tripId)
            .ToListAsync();

        var filePaths = tiles.Select(t => t.FilePath).ToList();

        // Delete tiles from database
        await _database!.ExecuteAsync(
            "DELETE FROM TripTiles WHERE TripId = ?", tripId);

        return filePaths;
    }

    /// <summary>
    /// Gets the total size of all downloaded trips.
    /// </summary>
    public async Task<long> GetTotalTripCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        var result = await _database!.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(TotalSizeBytes), 0) FROM DownloadedTrips WHERE Status = ?",
            TripDownloadStatus.Complete);
        return result;
    }

    #endregion

    #region Offline Places

    /// <summary>
    /// Gets all places for a downloaded trip.
    /// </summary>
    public async Task<List<OfflinePlaceEntity>> GetOfflinePlacesAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflinePlaceEntity>()
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Saves offline places for a trip.
    /// Uses bulk insert for efficiency.
    /// </summary>
    public async Task SaveOfflinePlacesAsync(int tripId, IEnumerable<OfflinePlaceEntity> places)
    {
        await EnsureInitializedAsync();

        // Clear existing places for this trip
        await _database!.ExecuteAsync(
            "DELETE FROM OfflinePlaces WHERE TripId = ?", tripId);

        // Set TripId and bulk insert
        var placeList = places.ToList();
        foreach (var place in placeList)
        {
            place.TripId = tripId;
        }

        if (placeList.Count > 0)
        {
            await _database.InsertAllAsync(placeList);
        }
    }

    #endregion

    #region Offline Segments

    /// <summary>
    /// Gets all segments for a downloaded trip.
    /// </summary>
    public async Task<List<OfflineSegmentEntity>> GetOfflineSegmentsAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflineSegmentEntity>()
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Saves offline segments for a trip.
    /// Uses bulk insert for efficiency.
    /// </summary>
    public async Task SaveOfflineSegmentsAsync(int tripId, IEnumerable<OfflineSegmentEntity> segments)
    {
        await EnsureInitializedAsync();

        // Clear existing segments for this trip
        await _database!.ExecuteAsync(
            "DELETE FROM OfflineSegments WHERE TripId = ?", tripId);

        // Set TripId and bulk insert
        var segmentList = segments.ToList();
        foreach (var segment in segmentList)
        {
            segment.TripId = tripId;
        }

        if (segmentList.Count > 0)
        {
            await _database.InsertAllAsync(segmentList);
        }
    }

    /// <summary>
    /// Gets a segment by origin and destination.
    /// </summary>
    public async Task<OfflineSegmentEntity?> GetOfflineSegmentAsync(int tripId, Guid originId, Guid destinationId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflineSegmentEntity>()
            .Where(s => s.TripId == tripId && s.OriginId == originId && s.DestinationId == destinationId)
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Offline Areas

    /// <summary>
    /// Gets all areas/regions for a downloaded trip.
    /// </summary>
    public async Task<List<OfflineAreaEntity>> GetOfflineAreasAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflineAreaEntity>()
            .Where(a => a.TripId == tripId)
            .OrderBy(a => a.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Saves offline areas for a trip.
    /// </summary>
    public async Task SaveOfflineAreasAsync(int tripId, IEnumerable<OfflineAreaEntity> areas)
    {
        await EnsureInitializedAsync();

        // Clear existing areas for this trip
        await _database!.ExecuteAsync(
            "DELETE FROM OfflineAreas WHERE TripId = ?", tripId);

        // Insert new areas
        foreach (var area in areas)
        {
            area.TripId = tripId;
            await _database.InsertAsync(area);
        }
    }

    /// <summary>
    /// Gets offline polygons (TripArea zones) for a trip.
    /// </summary>
    public async Task<List<OfflinePolygonEntity>> GetOfflinePolygonsAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflinePolygonEntity>()
            .Where(p => p.TripId == tripId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Saves offline polygons (TripArea zones) for a trip.
    /// </summary>
    public async Task SaveOfflinePolygonsAsync(int tripId, IEnumerable<OfflinePolygonEntity> polygons)
    {
        await EnsureInitializedAsync();

        // Clear existing polygons for this trip
        await _database!.ExecuteAsync(
            "DELETE FROM OfflinePolygons WHERE TripId = ?", tripId);

        // Insert new polygons
        foreach (var polygon in polygons)
        {
            polygon.TripId = tripId;
            await _database.InsertAsync(polygon);
        }
    }

    #endregion

    #region Individual Offline Entity Operations

    /// <summary>
    /// Gets an offline place by server ID.
    /// </summary>
    public async Task<OfflinePlaceEntity?> GetOfflinePlaceByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflinePlaceEntity>()
            .Where(p => p.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates an offline place.
    /// </summary>
    public async Task UpdateOfflinePlaceAsync(OfflinePlaceEntity place)
    {
        await EnsureInitializedAsync();
        await _database!.UpdateAsync(place);
    }

    /// <summary>
    /// Deletes an offline place by server ID.
    /// </summary>
    public async Task DeleteOfflinePlaceByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync(
            "DELETE FROM OfflinePlaces WHERE ServerId = ?", serverId.ToString());
    }

    /// <summary>
    /// Gets an offline area/region by server ID.
    /// </summary>
    public async Task<OfflineAreaEntity?> GetOfflineAreaByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflineAreaEntity>()
            .Where(a => a.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates an offline area/region.
    /// </summary>
    public async Task UpdateOfflineAreaAsync(OfflineAreaEntity area)
    {
        await EnsureInitializedAsync();
        await _database!.UpdateAsync(area);
    }

    /// <summary>
    /// Deletes an offline area/region by server ID.
    /// </summary>
    public async Task DeleteOfflineAreaByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync(
            "DELETE FROM OfflineAreas WHERE ServerId = ?", serverId.ToString());
    }

    /// <summary>
    /// Inserts a new offline place.
    /// </summary>
    public async Task InsertOfflinePlaceAsync(OfflinePlaceEntity place)
    {
        await EnsureInitializedAsync();
        await _database!.InsertAsync(place);
    }

    /// <summary>
    /// Inserts a new offline area/region.
    /// </summary>
    public async Task InsertOfflineAreaAsync(OfflineAreaEntity area)
    {
        await EnsureInitializedAsync();
        await _database!.InsertAsync(area);
    }

    /// <summary>
    /// Gets an offline segment by server ID.
    /// </summary>
    public async Task<OfflineSegmentEntity?> GetOfflineSegmentByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflineSegmentEntity>()
            .Where(s => s.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates an offline segment.
    /// </summary>
    public async Task UpdateOfflineSegmentAsync(OfflineSegmentEntity segment)
    {
        await EnsureInitializedAsync();
        await _database!.UpdateAsync(segment);
    }

    /// <summary>
    /// Gets an offline polygon by server ID.
    /// </summary>
    public async Task<OfflinePolygonEntity?> GetOfflinePolygonByServerIdAsync(Guid serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<OfflinePolygonEntity>()
            .Where(p => p.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates an offline polygon.
    /// </summary>
    public async Task UpdateOfflinePolygonAsync(OfflinePolygonEntity polygon)
    {
        await EnsureInitializedAsync();
        await _database!.UpdateAsync(polygon);
    }

    #endregion

    #region Trip Tiles

    /// <summary>
    /// Gets a tile for a specific trip.
    /// </summary>
    public async Task<TripTileEntity?> GetTripTileAsync(int tripId, int zoom, int x, int y)
    {
        await EnsureInitializedAsync();
        var id = $"{tripId}/{zoom}/{x}/{y}";
        return await _database!.Table<TripTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Saves a trip tile.
    /// </summary>
    public async Task SaveTripTileAsync(TripTileEntity tile)
    {
        await EnsureInitializedAsync();
        await _database!.InsertOrReplaceAsync(tile);
    }

    /// <summary>
    /// Gets the count of tiles for a trip.
    /// </summary>
    public async Task<int> GetTripTileCountAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripTileEntity>()
            .Where(t => t.TripId == tripId)
            .CountAsync();
    }

    #endregion

    #region Trip Download State (Pause/Resume)

    /// <summary>
    /// Gets a download state for a trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>The download state or null if not found.</returns>
    public async Task<TripDownloadStateEntity?> GetDownloadStateAsync(int tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripId == tripId);
    }

    /// <summary>
    /// Gets a download state by server trip ID.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>The download state or null if not found.</returns>
    public async Task<TripDownloadStateEntity?> GetDownloadStateByServerIdAsync(Guid tripServerId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripDownloadStateEntity>()
            .FirstOrDefaultAsync(s => s.TripServerId == tripServerId);
    }

    /// <summary>
    /// Saves a download state (insert or replace).
    /// </summary>
    /// <param name="state">The download state to save.</param>
    public async Task SaveDownloadStateAsync(TripDownloadStateEntity state)
    {
        await EnsureInitializedAsync();
        state.LastSaveTime = DateTime.UtcNow;
        await _database!.InsertOrReplaceAsync(state);
    }

    /// <summary>
    /// Deletes a download state for a trip.
    /// Called when download completes or is cancelled with cleanup.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    public async Task DeleteDownloadStateAsync(int tripId)
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync(
            "DELETE FROM TripDownloadStates WHERE TripId = ?", tripId);
    }

    /// <summary>
    /// Gets all paused download states.
    /// Used to show resumable downloads in UI.
    /// </summary>
    /// <returns>List of paused download states.</returns>
    public async Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripDownloadStateEntity>()
            .Where(s => s.Status == DownloadStateStatus.Paused ||
                       s.Status == DownloadStateStatus.LimitReached)
            .OrderByDescending(s => s.PausedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all active download states (in progress or paused).
    /// </summary>
    /// <returns>List of active download states.</returns>
    public async Task<List<TripDownloadStateEntity>> GetActiveDownloadStatesAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripDownloadStateEntity>()
            .Where(s => s.Status != DownloadStateStatus.Cancelled)
            .ToListAsync();
    }

    #endregion

    #region Live Tiles

    /// <summary>
    /// Gets a live cached tile by ID.
    /// </summary>
    public async Task<LiveTileEntity?> GetLiveTileAsync(string id)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <summary>
    /// Saves a live tile (insert or replace).
    /// </summary>
    public async Task SaveLiveTileAsync(LiveTileEntity tile)
    {
        await EnsureInitializedAsync();
        await _database!.InsertOrReplaceAsync(tile);
    }

    /// <summary>
    /// Updates the last access time for a live tile.
    /// </summary>
    public async Task UpdateLiveTileAccessAsync(string id)
    {
        await EnsureInitializedAsync();
        var tile = await _database!.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tile != null)
        {
            tile.LastAccessedAt = DateTime.UtcNow;
            tile.AccessCount++;
            await _database.UpdateAsync(tile);
        }
    }

    /// <summary>
    /// Gets the count of live cached tiles.
    /// </summary>
    public async Task<int> GetLiveTileCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LiveTileEntity>().CountAsync();
    }

    /// <summary>
    /// Gets the total size of live cached tiles.
    /// </summary>
    public async Task<long> GetLiveCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        var result = await _database!.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");
        return result;
    }

    /// <summary>
    /// Gets the oldest live tiles for LRU eviction.
    /// </summary>
    public async Task<List<LiveTileEntity>> GetOldestLiveTilesAsync(int count)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// Deletes a live tile by ID.
    /// </summary>
    public async Task DeleteLiveTileAsync(string id)
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync(
            "DELETE FROM LiveTiles WHERE Id = ?", id);
    }

    /// <summary>
    /// Clears all live tiles.
    /// </summary>
    public async Task ClearLiveTilesAsync()
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync("DELETE FROM LiveTiles");
    }

    #endregion

    #region Diagnostic Queries

    /// <summary>
    /// Gets the count of pending locations that can be synced (for diagnostics).
    /// Excludes rejected locations to match drain logic.
    /// </summary>
    public async Task<int> GetPendingLocationCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .CountAsync();
    }

    /// <summary>
    /// Gets the count of rejected locations (for diagnostics).
    /// These are locations rejected by client threshold filters or server.
    /// </summary>
    public async Task<int> GetRejectedLocationCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.IsRejected)
            .CountAsync();
    }

    /// <summary>
    /// Gets the count of synced locations for diagnostics.
    /// </summary>
    public async Task<int> GetSyncedLocationCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .CountAsync();
    }

    /// <summary>
    /// Gets the count of failed locations for diagnostics.
    /// </summary>
    public async Task<int> GetFailedLocationCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Failed)
            .CountAsync();
    }

    /// <summary>
    /// Gets the oldest pending location for diagnostics.
    /// </summary>
    public async Task<QueuedLocation?> GetOldestPendingLocationAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Gets the last synced location for diagnostics.
    /// </summary>
    public async Task<QueuedLocation?> GetLastSyncedLocationAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .OrderByDescending(l => l.LastSyncAttempt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Gets all queued locations for export, ordered by timestamp descending.
    /// </summary>
    /// <returns>All queued locations regardless of sync status.</returns>
    public async Task<List<QueuedLocation>> GetAllQueuedLocationsAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Gets the total count of trip tiles across all trips.
    /// </summary>
    public async Task<int> GetTripTileCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<TripTileEntity>().CountAsync();
    }

    /// <summary>
    /// Gets the total size of trip tile cache in bytes.
    /// Uses SQL aggregation to avoid loading all entities into memory.
    /// </summary>
    public async Task<long> GetTripCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM TripTiles");
    }

    #endregion

    #region Local Timeline

    /// <summary>
    /// Inserts a new local timeline entry.
    /// </summary>
    /// <param name="entry">The entry to insert.</param>
    /// <returns>The inserted entry's ID.</returns>
    public async Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        await EnsureInitializedAsync();
        await _database!.InsertAsync(entry);
        return entry.Id;
    }

    /// <summary>
    /// Updates an existing local timeline entry.
    /// </summary>
    /// <param name="entry">The entry to update.</param>
    public async Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        await EnsureInitializedAsync();
        await _database!.UpdateAsync(entry);
    }

    /// <summary>
    /// Gets a local timeline entry by ID.
    /// </summary>
    /// <param name="id">The local ID.</param>
    /// <returns>The entry or null if not found.</returns>
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// Gets a local timeline entry by server ID.
    /// </summary>
    /// <param name="serverId">The server ID.</param>
    /// <returns>The entry or null if not found.</returns>
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.ServerId == serverId);
    }

    /// <summary>
    /// Gets a local timeline entry by timestamp (for matching during sync).
    /// Uses a tolerance window to handle minor timestamp differences.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>The entry or null if not found.</returns>
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(
        DateTime timestamp,
        int toleranceSeconds = 2)
    {
        await EnsureInitializedAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        return await _database!.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= minTime && e.Timestamp <= maxTime)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Gets all local timeline entries for a specific date.
    /// </summary>
    /// <param name="date">The date to retrieve entries for.</param>
    /// <returns>List of entries for that date, ordered by timestamp descending.</returns>
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date)
    {
        await EnsureInitializedAsync();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();

        return await _database!.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startOfDay && e.Timestamp < endOfDay)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all local timeline entries within a date range.
    /// </summary>
    /// <param name="fromDate">Start date (inclusive).</param>
    /// <param name="toDate">End date (inclusive).</param>
    /// <returns>List of entries in the range, ordered by timestamp descending.</returns>
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        await EnsureInitializedAsync();

        var startTime = fromDate.Date.ToUniversalTime();
        var endTime = toDate.Date.AddDays(1).ToUniversalTime();

        return await _database!.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startTime && e.Timestamp < endTime)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Gets all local timeline entries for export.
    /// </summary>
    /// <returns>All entries ordered by timestamp descending.</returns>
    public async Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <summary>
    /// Deletes a local timeline entry by ID.
    /// </summary>
    /// <param name="id">The local ID.</param>
    public async Task DeleteLocalTimelineEntryAsync(int id)
    {
        await EnsureInitializedAsync();
        await _database!.ExecuteAsync(
            "DELETE FROM LocalTimelineEntries WHERE Id = ?", id);
    }

    /// <summary>
    /// Deletes a local timeline entry by timestamp (for removing skipped entries).
    /// Uses a tolerance window to handle minor timestamp differences.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>Number of entries deleted.</returns>
    public async Task<int> DeleteLocalTimelineEntryByTimestampAsync(
        DateTime timestamp,
        int toleranceSeconds = 2)
    {
        await EnsureInitializedAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        return await _database!.ExecuteAsync(
            "DELETE FROM LocalTimelineEntries WHERE Timestamp >= ? AND Timestamp <= ?",
            minTime, maxTime);
    }

    /// <summary>
    /// Gets the total count of local timeline entries.
    /// </summary>
    /// <returns>The count of entries.</returns>
    public async Task<int> GetLocalTimelineEntryCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LocalTimelineEntry>().CountAsync();
    }

    /// <summary>
    /// Gets the most recent local timeline entry.
    /// Used by LocalTimelineFilter to initialize last stored location.
    /// </summary>
    /// <returns>The most recent entry or null if none exist.</returns>
    public async Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Updates the ServerId for a local timeline entry matched by timestamp.
    /// Used when sync confirms a location was stored on server.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="serverId">The server-assigned ID.</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>True if an entry was updated.</returns>
    public async Task<bool> UpdateLocalTimelineServerIdAsync(
        DateTime timestamp,
        int serverId,
        int toleranceSeconds = 2)
    {
        await EnsureInitializedAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        var affected = await _database!.ExecuteAsync(
            "UPDATE LocalTimelineEntries SET ServerId = ? WHERE Timestamp >= ? AND Timestamp <= ? AND ServerId IS NULL",
            serverId, minTime, maxTime);

        return affected > 0;
    }

    /// <summary>
    /// Clears all local timeline entries.
    /// Use with caution - this deletes all local timeline history.
    /// </summary>
    /// <returns>Number of entries deleted.</returns>
    public async Task<int> ClearAllLocalTimelineEntriesAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.ExecuteAsync("DELETE FROM LocalTimelineEntries");
    }

    /// <summary>
    /// Bulk inserts local timeline entries.
    /// Used for import operations.
    /// </summary>
    /// <param name="entries">The entries to insert.</param>
    /// <returns>Number of entries inserted.</returns>
    public async Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> entries)
    {
        await EnsureInitializedAsync();
        var entryList = entries.ToList();
        if (entryList.Count == 0)
            return 0;

        await _database!.InsertAllAsync(entryList);
        return entryList.Count;
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Disposes the database connection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
            _database = null;
        }
        _initLock.Dispose();
    }

    #endregion
}
