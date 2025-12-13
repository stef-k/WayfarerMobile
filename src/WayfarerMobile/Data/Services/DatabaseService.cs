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
    private const int MaxSyncAttempts = 5;
    private const int MaxQueuedLocations = 10000;

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
            await _database.CreateTableAsync<LiveTileEntity>();
            await _database.CreateTableAsync<ActivityType>();

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
    /// Excludes server rejected locations (they should not be retried).
    /// </summary>
    /// <param name="limit">Maximum number of locations to retrieve.</param>
    /// <returns>List of pending locations.</returns>
    public async Task<List<QueuedLocation>> GetPendingLocationsAsync(int limit = 100)
    {
        await EnsureInitializedAsync();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending &&
                       l.SyncAttempts < MaxSyncAttempts &&
                       !l.IsServerRejected)
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
    /// Marks a location sync as failed.
    /// Uses single UPDATE statement for efficiency.
    /// Updates SyncStatus to Failed if SyncAttempts reaches MaxSyncAttempts.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="error">The error message.</param>
    public async Task MarkLocationFailedAsync(int id, string error)
    {
        await EnsureInitializedAsync();

        // Increment attempts and conditionally set status to Failed if max attempts reached
        await _database!.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncAttempts = SyncAttempts + 1,
                  LastSyncAttempt = ?,
                  LastError = ?,
                  SyncStatus = CASE WHEN SyncAttempts + 1 >= ? THEN ? ELSE SyncStatus END
              WHERE Id = ?",
            DateTime.UtcNow, error, MaxSyncAttempts, (int)SyncStatus.Failed, id);
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
    /// Marks a location as server rejected (threshold validation, etc.).
    /// Server rejected locations should not be retried - different from technical failures.
    /// Lesson learned: Use dedicated field instead of storing metadata in error messages.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="reason">The rejection reason.</param>
    public async Task MarkLocationServerRejectedAsync(int id, string reason)
    {
        await EnsureInitializedAsync();

        var location = await _database!.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location != null)
        {
            location.IsServerRejected = true;
            location.SyncStatus = SyncStatus.Synced; // Mark as "done" - don't retry
            location.LastError = reason;
            location.LastSyncAttempt = DateTime.UtcNow;
            await _database.UpdateAsync(location);
        }
    }

    /// <summary>
    /// Removes synced locations older than the specified days.
    /// </summary>
    /// <param name="daysOld">Number of days old.</param>
    public async Task<int> PurgeSyncedLocationsAsync(int daysOld = 7)
    {
        await EnsureInitializedAsync();

        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var deletedSynced = await _database!.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Synced, cutoff);

        // Also purge server-rejected locations older than 2 days
        var rejectedCutoff = DateTime.UtcNow.AddDays(-2);
        var deletedRejected = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE IsServerRejected = 1 AND CreatedAt < ?",
            rejectedCutoff);

        // Safety valve: purge very old pending locations (30+ days offline is unlikely to sync)
        var pendingCutoff = DateTime.UtcNow.AddDays(-30);
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
    /// Gets the count of pending locations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();

        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .CountAsync();
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
    /// Gets the count of pending locations for diagnostics.
    /// </summary>
    public async Task<int> GetPendingLocationCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
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
