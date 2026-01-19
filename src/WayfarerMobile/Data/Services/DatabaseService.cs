using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Services;

/// <summary>
/// Core database service providing initialization, connection management, and platform-specific operations.
/// Domain-specific operations have been extracted to dedicated repositories:
/// <list type="bullet">
///   <item><see cref="WayfarerMobile.Data.Repositories.ILocationQueueRepository"/> - Location queue operations</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.ITimelineRepository"/> - Timeline entries</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.ITripRepository"/> - Downloaded trips</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.IPlaceRepository"/> - Offline places</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.ISegmentRepository"/> - Offline segments</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.IAreaRepository"/> - Offline areas and polygons</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.ITripTileRepository"/> - Trip tiles</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.ILiveTileCacheRepository"/> - Live tile cache</item>
///   <item><see cref="WayfarerMobile.Data.Repositories.IDownloadStateRepository"/> - Download state</item>
/// </list>
/// </summary>
public class DatabaseService : IAsyncDisposable
{
    #region Constants

    private const string DatabaseFilename = "wayfarer.db3";
    private const int CurrentSchemaVersion = 3; // Increment when schema changes
    private const string SchemaVersionKey = "db_schema_version";

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
    /// Gets the database connection for services that need direct access.
    /// Repositories should use the connection factory instead.
    /// </summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        await EnsureInitializedAsync();
        return _database!;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Ensures the database is initialized with all required tables.
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

            // Create tables (this also adds new columns to existing tables)
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

            // Run migrations
            await RunMigrationsAsync();

            _initialized = true;
            Console.WriteLine($"[DatabaseService] Initialized: {DatabasePath}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    #endregion

    #region Migrations

    /// <summary>
    /// Runs all pending database migrations.
    /// </summary>
    private async Task RunMigrationsAsync()
    {
        var currentVersion = await GetSchemaVersionAsync();
        Console.WriteLine($"[DatabaseService] Current schema version: {currentVersion}, target: {CurrentSchemaVersion}");

        if (currentVersion >= CurrentSchemaVersion)
            return;

        // Run migrations in order
        // Note: Version 2 migration removed - no users to migrate from legacy Status field

        // Version 3: Add composite index for efficient claim queries
        if (currentVersion < 3)
        {
            await MigrateToVersion3Async();
        }

        // Update schema version
        await SetSchemaVersionAsync(CurrentSchemaVersion);
        Console.WriteLine($"[DatabaseService] Migration complete. Schema version: {CurrentSchemaVersion}");
    }

    /// <summary>
    /// Migration to version 3: Add composite index for efficient location claim queries.
    /// </summary>
    private async Task MigrateToVersion3Async()
    {
        Console.WriteLine("[DatabaseService] Running migration to version 3: Adding composite index");

        // Create composite index for efficient claim queries:
        // WHERE SyncStatus = Pending AND IsRejected = 0 ORDER BY Timestamp
        // This dramatically improves ClaimPendingLocationsAsync performance
        await _database!.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS IX_QueuedLocations_SyncStatus_IsRejected_Timestamp
              ON QueuedLocations (SyncStatus, IsRejected, Timestamp)");

        // Also add index for ServerConfirmed recovery queries
        await _database.ExecuteAsync(
            @"CREATE INDEX IF NOT EXISTS IX_QueuedLocations_ServerConfirmed
              ON QueuedLocations (ServerConfirmed) WHERE ServerConfirmed = 1");

        Console.WriteLine("[DatabaseService] Version 3 migration complete");
    }

    /// <summary>
    /// Gets the current schema version from settings.
    /// </summary>
    private async Task<int> GetSchemaVersionAsync()
    {
        var setting = await _database!.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == SchemaVersionKey);

        if (setting?.Value == null)
            return 1; // Original schema

        return int.TryParse(setting.Value, out var version) ? version : 1;
    }

    /// <summary>
    /// Sets the schema version in settings.
    /// </summary>
    private async Task SetSchemaVersionAsync(int version)
    {
        var setting = await _database!.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == SchemaVersionKey);

        if (setting == null)
        {
            setting = new AppSetting
            {
                Key = SchemaVersionKey,
                Value = version.ToString()
            };
            await _database.InsertAsync(setting);
        }
        else
        {
            setting.Value = version.ToString();
            setting.LastModified = DateTime.UtcNow;
            await _database.UpdateAsync(setting);
        }
    }

    #endregion

    #region Location Queue (Platform Services)

    /// <summary>
    /// Queues a location for server synchronization.
    /// Used by platform-specific LocationTrackingService implementations that cannot use DI.
    /// For DI-enabled services, use <see cref="WayfarerMobile.Data.Repositories.ILocationQueueRepository"/>.
    /// </summary>
    /// <param name="location">The location data to queue.</param>
    /// <param name="maxQueuedLocations">Maximum queue size - read from Preferences using SettingsService.QueueLimitMaxLocationsKey.</param>
    /// <param name="isUserInvoked">True for manual check-ins (skip filtering, prioritize sync).</param>
    /// <param name="activityTypeId">Optional activity type ID (for user-invoked check-ins).</param>
    /// <param name="notes">Optional notes (for user-invoked check-ins).</param>
    /// <returns>The ID of the queued location.</returns>
    /// <exception cref="ArgumentException">Thrown when coordinates are invalid.</exception>
    public async Task<int> QueueLocationAsync(
        LocationData location,
        int maxQueuedLocations,
        bool isUserInvoked = false,
        int? activityTypeId = null,
        string? notes = null)
    {
        // Validate coordinates to prevent corrupted data (parity with LocationQueueRepository)
        if (!IsValidCoordinate(location.Latitude, location.Longitude))
        {
            throw new ArgumentException(
                $"Invalid coordinates: Lat={location.Latitude}, Lon={location.Longitude}. " +
                "Coordinates must be finite numbers within valid ranges.");
        }

        await EnsureInitializedAsync();

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
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IsUserInvoked = isUserInvoked,
            ActivityTypeId = activityTypeId,
            CheckInNotes = notes
        };

        await _database!.InsertAsync(queued);
        Console.WriteLine($"[DatabaseService] Location queued (IsUserInvoked={isUserInvoked}): {location}");

        // Cleanup old locations if queue is too large
        await EnforceQueueLimitAsync(maxQueuedLocations);

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

    /// <summary>
    /// Enforces queue limit by removing oldest safe entries, then oldest pending if needed.
    /// Never removes Syncing entries (in-flight protection).
    /// </summary>
    /// <param name="maxQueuedLocations">The maximum number of locations to keep.</param>
    /// <remarks>
    /// Note: This logic is duplicated in <see cref="Repositories.LocationQueueRepository.CleanupOldLocationsAsync(int)"/>.
    /// The duplication is intentional - DatabaseService serves platform services that can't use DI,
    /// while LocationQueueRepository serves DI-enabled services. Both are thread-safe via SQLite
    /// subquery-based deletion (concurrent calls safely delete different or same rows).
    /// </remarks>
    private async Task EnforceQueueLimitAsync(int maxQueuedLocations)
    {
        var count = await _database!.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
        if (count < maxQueuedLocations)
            return;

        var toDelete = count - maxQueuedLocations + 1;

        // Delete oldest safe entries (synced or rejected) first
        var safeDeleted = await _database.ExecuteAsync(@"
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
        await _database.ExecuteAsync(@"
            DELETE FROM QueuedLocations WHERE Id IN (
                SELECT Id FROM QueuedLocations
                WHERE SyncStatus = ? AND IsRejected = 0
                ORDER BY Timestamp, Id
                LIMIT ?
            )", (int)SyncStatus.Pending, remaining);
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
        catch (Exception ex)
        {
            // Log parse failures for diagnostics (previously silent)
            Console.WriteLine($"[DatabaseService] Failed to parse setting '{key}' as {typeof(T).Name}: {ex.Message}");
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
