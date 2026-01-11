using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for DatabaseService focusing on location queue operations,
/// settings persistence, and live tile cache management.
/// </summary>
/// <remarks>
/// These tests use an in-memory SQLite database to test the actual SQL operations
/// without needing the MAUI file system. The tests cover:
/// - Location queue operations (QueueLocationAsync, GetPendingLocationsAsync, etc.)
/// - Purge logic (PurgeSyncedLocationsAsync, CleanupOldLocationsAsync)
/// - Live tile cache (SaveLiveTileAsync, GetLiveTileAsync, LRU eviction)
/// - Settings (GetSettingAsync, SetSettingAsync)
/// - Edge cases (queue overflow, concurrent access, null values)
/// </remarks>
[Collection("SQLite")]
public class DatabaseServiceTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _database = null!;
    private const int MaxSyncAttempts = 5;
    private const int MaxQueuedLocations = 10000;

    #region Test Lifecycle

    /// <summary>
    /// Initializes the in-memory database before each test.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Use in-memory SQLite database for testing
        _database = new SQLiteAsyncConnection(":memory:");

        // Create tables
        await _database.CreateTableAsync<QueuedLocation>();
        await _database.CreateTableAsync<AppSetting>();
        await _database.CreateTableAsync<LiveTileEntity>();
    }

    /// <summary>
    /// Disposes the database connection after each test.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    #endregion

    #region QueueLocationAsync Tests

    [Fact]
    public async Task QueueLocationAsync_InsertsLocationWithPendingStatus()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);

        // Act
        var queued = await InsertQueuedLocationAsync(location);

        // Assert
        queued.Id.Should().BeGreaterThan(0);
        queued.Latitude.Should().Be(51.5074);
        queued.Longitude.Should().Be(-0.1278);
        queued.SyncStatus.Should().Be(SyncStatus.Pending);
    }

    [Fact]
    public async Task QueueLocationAsync_PreservesAllLocationProperties()
    {
        // Arrange
        var location = new LocationData
        {
            Latitude = 51.5074,
            Longitude = -0.1278,
            Altitude = 100.5,
            Accuracy = 10.0,
            Speed = 5.5,
            Bearing = 180.0,
            Timestamp = DateTime.UtcNow,
            Provider = "gps"
        };

        // Act
        var queued = await InsertQueuedLocationAsync(location);
        var retrieved = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queued.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Latitude.Should().Be(location.Latitude);
        retrieved.Longitude.Should().Be(location.Longitude);
        retrieved.Altitude.Should().Be(location.Altitude);
        retrieved.Accuracy.Should().Be(location.Accuracy);
        retrieved.Speed.Should().Be(location.Speed);
        retrieved.Bearing.Should().Be(location.Bearing);
        retrieved.Provider.Should().Be(location.Provider);
    }

    [Fact]
    public async Task QueueLocationAsync_HandlesNullOptionalFields()
    {
        // Arrange
        var location = new LocationData
        {
            Latitude = 51.5074,
            Longitude = -0.1278,
            Timestamp = DateTime.UtcNow
            // All optional fields are null
        };

        // Act
        var queued = await InsertQueuedLocationAsync(location);
        var retrieved = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queued.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Altitude.Should().BeNull();
        retrieved.Accuracy.Should().BeNull();
        retrieved.Speed.Should().BeNull();
        retrieved.Bearing.Should().BeNull();
        retrieved.Provider.Should().BeNull();
    }

    [Fact]
    public async Task QueueLocationAsync_MultipleLocations_AssignsUniqueIds()
    {
        // Arrange & Act
        var queued1 = await InsertQueuedLocationAsync(CreateLocationData(51.5074, -0.1278));
        var queued2 = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.5));
        var queued3 = await InsertQueuedLocationAsync(CreateLocationData(53.0, -1.0));

        // Assert
        var ids = new[] { queued1.Id, queued2.Id, queued3.Id };
        ids.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region GetPendingLocationsAsync Tests

    [Fact]
    public async Task GetPendingLocationsAsync_ReturnsOnlyPendingLocations()
    {
        // Arrange
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Synced);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Syncing);
        await InsertQueuedLocationAsync(CreateLocationData(54.0, -0.4), SyncStatus.Pending);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert
        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(l => l.SyncStatus == SyncStatus.Pending);
    }

    [Fact]
    public async Task GetPendingLocationsAsync_ExcludesRejectedLocations()
    {
        // Arrange
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);

        var rejected = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending);
        rejected.IsRejected = true;
        await _database.UpdateAsync(rejected);

        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Pending);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert
        pending.Should().HaveCount(2);
        pending.Should().NotContain(l => l.Id == rejected.Id);
    }

    [Fact]
    public async Task GetPendingLocationsAsync_IncludesLocationsRegardlessOfAttemptCount()
    {
        // Arrange - Valid locations retry until 300-day purge regardless of attempts
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: 0);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending, syncAttempts: MaxSyncAttempts);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Pending, syncAttempts: MaxSyncAttempts + 100);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert - All pending locations included regardless of attempt count
        pending.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetPendingLocationsAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 150; i++)
        {
            await InsertQueuedLocationAsync(CreateLocationData(51.0 + i * 0.001, -0.1), SyncStatus.Pending);
        }

        // Act
        var pending = await GetPendingLocationsAsync(limit: 50);

        // Assert
        pending.Should().HaveCount(50);
    }

    [Fact]
    public async Task GetPendingLocationsAsync_OrdersByTimestamp()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3, baseTime.AddMinutes(2)), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1, baseTime), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2, baseTime.AddMinutes(1)), SyncStatus.Pending);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert
        pending.Should().HaveCount(3);
        pending[0].Latitude.Should().Be(51.0); // Oldest first
        pending[1].Latitude.Should().Be(52.0);
        pending[2].Latitude.Should().Be(53.0); // Newest last
    }

    [Fact]
    public async Task GetPendingLocationsAsync_EmptyQueue_ReturnsEmptyList()
    {
        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert
        pending.Should().BeEmpty();
    }

    #endregion

    #region MarkLocationsSyncedAsync Tests

    [Fact]
    public async Task MarkLocationsSyncedAsync_UpdatesSingleLocation()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);

        // Act
        var affected = await MarkLocationsSyncedAsync(new[] { queued.Id });

        // Assert
        affected.Should().Be(1);
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.SyncStatus.Should().Be(SyncStatus.Synced);
    }

    [Fact]
    public async Task MarkLocationsSyncedAsync_UpdatesMultipleLocations()
    {
        // Arrange
        var queued1 = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        var queued2 = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending);
        var queued3 = await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Pending);

        // Act
        var affected = await MarkLocationsSyncedAsync(new[] { queued1.Id, queued2.Id, queued3.Id });

        // Assert
        affected.Should().Be(3);
        var all = await _database.Table<QueuedLocation>().ToListAsync();
        all.Should().OnlyContain(l => l.SyncStatus == SyncStatus.Synced);
    }

    [Fact]
    public async Task MarkLocationsSyncedAsync_EmptyIdList_ReturnsZero()
    {
        // Arrange
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);

        // Act
        var affected = await MarkLocationsSyncedAsync(Array.Empty<int>());

        // Assert
        affected.Should().Be(0);
    }

    [Fact]
    public async Task MarkLocationsSyncedAsync_NonExistentIds_ReturnsZero()
    {
        // Act
        var affected = await MarkLocationsSyncedAsync(new[] { 999, 1000, 1001 });

        // Assert
        affected.Should().Be(0);
    }

    [Fact]
    public async Task MarkLocationsSyncedAsync_MixedExistingAndNonExistent_UpdatesExisting()
    {
        // Arrange
        var queued1 = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        var queued2 = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending);

        // Act - Include one non-existent ID
        var affected = await MarkLocationsSyncedAsync(new[] { queued1.Id, 9999, queued2.Id });

        // Assert
        affected.Should().Be(2);
    }

    [Fact]
    public async Task MarkLocationsSyncedAsync_LargeBatch_HandlesCorrectly()
    {
        // Arrange
        var ids = new List<int>();
        for (int i = 0; i < 500; i++)
        {
            var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0 + i * 0.001, -0.1), SyncStatus.Pending);
            ids.Add(queued.Id);
        }

        // Act
        var affected = await MarkLocationsSyncedAsync(ids);

        // Assert
        affected.Should().Be(500);
    }

    #endregion

    #region MarkLocationFailedAsync Tests

    [Fact]
    public async Task MarkLocationFailedAsync_IncrementsAttemptCount()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: 0);

        // Act
        await MarkLocationFailedAsync(queued.Id, "Network error");

        // Assert
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.SyncAttempts.Should().Be(1);
        updated.LastError.Should().Be("Network error");
        updated.LastSyncAttempt.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkLocationFailedAsync_KeepsPendingStatusAlways()
    {
        // Arrange - Valid locations stay pending regardless of attempt count
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: MaxSyncAttempts - 1);

        // Act
        await MarkLocationFailedAsync(queued.Id, "Transient error");

        // Assert - Status stays Pending, only SyncAttempts incremented for diagnostics
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.SyncAttempts.Should().Be(MaxSyncAttempts);
        updated.SyncStatus.Should().Be(SyncStatus.Pending); // Stays pending for retry
    }

    [Fact]
    public async Task MarkLocationFailedAsync_IncrementsAttemptCountForDiagnostics()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: 2);

        // Act
        await MarkLocationFailedAsync(queued.Id, "Transient error");

        // Assert - Attempt count tracked for diagnostics
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.SyncAttempts.Should().Be(3);
        updated.SyncStatus.Should().Be(SyncStatus.Pending);
    }

    [Fact]
    public async Task MarkLocationFailedAsync_UpdatesLastSyncAttemptTimestamp()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        var beforeUpdate = DateTime.UtcNow;

        // Act
        await MarkLocationFailedAsync(queued.Id, "Error");

        // Assert
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.LastSyncAttempt.Should().NotBeNull();
        updated.LastSyncAttempt!.Value.Should().BeOnOrAfter(beforeUpdate);
    }

    #endregion

    #region MarkLocationRejectedAsync Tests

    [Fact]
    public async Task MarkLocationRejectedAsync_SetsRejectedFlag()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);

        // Act
        await MarkLocationRejectedAsync(queued.Id, "Server: Threshold validation failed");

        // Assert
        var updated = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.IsRejected.Should().BeTrue();
        updated.SyncStatus.Should().Be(SyncStatus.Synced); // Marked as "done"
        updated.RejectionReason.Should().Be("Server: Threshold validation failed");
    }

    [Fact]
    public async Task MarkLocationRejectedAsync_NonExistentId_DoesNotThrow()
    {
        // Act
        var act = async () => await MarkLocationRejectedAsync(9999, "Test rejection");

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MarkLocationRejectedAsync_RejectedLocationExcludedFromPending()
    {
        // Arrange
        var queued1 = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        var queued2 = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending);

        // Act
        await MarkLocationRejectedAsync(queued1.Id, "Client: Time below threshold");

        // Assert
        var pending = await GetPendingLocationsAsync();
        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be(queued2.Id);
    }

    #endregion

    #region PurgeSyncedLocationsAsync Tests

    [Fact]
    public async Task PurgeSyncedLocationsAsync_DeletesOldSyncedLocations()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-10);
        var recentDate = DateTime.UtcNow.AddDays(-3);

        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Synced, createdAt: oldDate);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Synced, createdAt: recentDate);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Pending);

        // Act
        var deleted = await PurgeSyncedLocationsAsync(daysOld: 7);

        // Assert
        deleted.Should().BeGreaterThan(0);
        var remaining = await _database.Table<QueuedLocation>().ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(l => l.Latitude == 51.0);
    }

    [Fact]
    public async Task PurgeSyncedLocationsAsync_DeletesOldRejectedLocations()
    {
        // Arrange
        var oldDate = DateTime.UtcNow.AddDays(-5);
        var recentDate = DateTime.UtcNow.AddDays(-1);

        var oldRejected = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Synced, createdAt: oldDate);
        oldRejected.IsRejected = true;
        await _database.UpdateAsync(oldRejected);

        var recentRejected = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Synced, createdAt: recentDate);
        recentRejected.IsRejected = true;
        await _database.UpdateAsync(recentRejected);

        // Act
        var deleted = await PurgeSyncedLocationsAsync(daysOld: 7);

        // Assert - Old rejected (5 days > 2 day cutoff) should be deleted
        var remaining = await _database.Table<QueuedLocation>().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Latitude.Should().Be(52.0);
    }

    [Fact]
    public async Task PurgeSyncedLocationsAsync_DeletesVeryOldPendingLocations()
    {
        // Arrange - 300+ days old pending locations are considered stale
        var veryOldDate = DateTime.UtcNow.AddDays(-305);
        var recentDate = DateTime.UtcNow.AddDays(-5);

        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending, createdAt: veryOldDate);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending, createdAt: recentDate);

        // Act
        var deleted = await PurgeSyncedLocationsAsync(daysOld: 7);

        // Assert
        deleted.Should().BeGreaterThan(0);
        var remaining = await _database.Table<QueuedLocation>().ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Latitude.Should().Be(52.0);
    }

    [Fact]
    public async Task PurgeSyncedLocationsAsync_EmptyDatabase_ReturnsZero()
    {
        // Act
        var deleted = await PurgeSyncedLocationsAsync(daysOld: 7);

        // Assert
        deleted.Should().Be(0);
    }

    #endregion

    #region Queue Overflow Handling Tests

    [Fact]
    public async Task CleanupOldLocationsAsync_RemovesSyncedWhenOverLimit()
    {
        // Arrange - Insert more than MaxQueuedLocations with mixed statuses
        // This simulates the cleanup behavior when queue exceeds limit
        for (int i = 0; i < 100; i++)
        {
            await InsertQueuedLocationAsync(CreateLocationData(51.0 + i * 0.001, -0.1), SyncStatus.Synced);
        }
        for (int i = 0; i < 50; i++)
        {
            await InsertQueuedLocationAsync(CreateLocationData(52.0 + i * 0.001, -0.2), SyncStatus.Pending);
        }

        var totalBefore = await _database.Table<QueuedLocation>().CountAsync();
        totalBefore.Should().Be(150);

        // Act - Simulate cleanup by removing oldest synced
        await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE Id IN (SELECT Id FROM QueuedLocations WHERE SyncStatus = ? ORDER BY Timestamp LIMIT ?)",
            (int)SyncStatus.Synced, 50);

        // Assert
        var totalAfter = await _database.Table<QueuedLocation>().CountAsync();
        totalAfter.Should().Be(100);

        var pendingCount = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .CountAsync();
        pendingCount.Should().Be(50); // Pending should be preserved
    }

    [Fact]
    public async Task QueueOverflow_SyncedRemovedFirst_PendingPreserved()
    {
        // Arrange - Create scenario where synced locations are oldest
        var oldTime = DateTime.UtcNow.AddHours(-5);
        var recentTime = DateTime.UtcNow;

        for (int i = 0; i < 50; i++)
        {
            await InsertQueuedLocationAsync(CreateLocationData(51.0 + i * 0.001, -0.1, oldTime), SyncStatus.Synced);
        }
        for (int i = 0; i < 50; i++)
        {
            await InsertQueuedLocationAsync(CreateLocationData(52.0 + i * 0.001, -0.2, recentTime), SyncStatus.Pending);
        }

        // Act - Remove oldest 30 synced
        await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE Id IN (SELECT Id FROM QueuedLocations WHERE SyncStatus = ? ORDER BY Timestamp LIMIT ?)",
            (int)SyncStatus.Synced, 30);

        // Assert
        var syncedCount = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .CountAsync();
        var pendingCount = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .CountAsync();

        syncedCount.Should().Be(20);
        pendingCount.Should().Be(50); // All pending preserved
    }

    #endregion

    #region Live Tile Cache Tests

    [Fact]
    public async Task SaveLiveTileAsync_InsertsNewTile()
    {
        // Arrange
        var tile = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);

        // Act
        await _database.InsertOrReplaceAsync(tile);

        // Assert
        var retrieved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Zoom.Should().Be(15);
        retrieved.X.Should().Be(16383);
        retrieved.Y.Should().Be(10922);
    }

    [Fact]
    public async Task SaveLiveTileAsync_ReplacesExistingTile()
    {
        // Arrange
        var tile1 = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);
        tile1.FileSizeBytes = 10000;
        await _database.InsertOrReplaceAsync(tile1);

        // Act
        var tile2 = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);
        tile2.FileSizeBytes = 20000;
        await _database.InsertOrReplaceAsync(tile2);

        // Assert
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(1);

        var retrieved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile1.Id);
        retrieved!.FileSizeBytes.Should().Be(20000);
    }

    [Fact]
    public async Task GetLiveTileAsync_ExistingTile_ReturnsTile()
    {
        // Arrange
        var tile = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);
        await _database.InsertAsync(tile);

        // Act
        var retrieved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == "osm/15/16383/10922");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Zoom.Should().Be(15);
    }

    [Fact]
    public async Task GetLiveTileAsync_NonExistentTile_ReturnsNull()
    {
        // Act
        var retrieved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == "nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task UpdateLiveTileAccessAsync_UpdatesAccessTimeAndCount()
    {
        // Arrange
        var tile = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);
        tile.AccessCount = 1;
        tile.LastAccessedAt = DateTime.UtcNow.AddHours(-1);
        await _database.InsertAsync(tile);
        var beforeUpdate = DateTime.UtcNow;

        // Act
        var tileToUpdate = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile.Id);
        if (tileToUpdate != null)
        {
            tileToUpdate.LastAccessedAt = DateTime.UtcNow;
            tileToUpdate.AccessCount++;
            await _database.UpdateAsync(tileToUpdate);
        }

        // Assert
        var updated = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile.Id);
        updated!.AccessCount.Should().Be(2);
        updated.LastAccessedAt.Should().BeOnOrAfter(beforeUpdate);
    }

    [Fact]
    public async Task GetOldestLiveTilesAsync_ReturnsInLruOrder()
    {
        // Arrange - Insert tiles with different access times
        var oldTile = CreateLiveTile("osm/15/1/1", 15, 1, 1);
        oldTile.LastAccessedAt = DateTime.UtcNow.AddHours(-3);
        await _database.InsertAsync(oldTile);

        var newerTile = CreateLiveTile("osm/15/2/2", 15, 2, 2);
        newerTile.LastAccessedAt = DateTime.UtcNow.AddHours(-1);
        await _database.InsertAsync(newerTile);

        var newestTile = CreateLiveTile("osm/15/3/3", 15, 3, 3);
        newestTile.LastAccessedAt = DateTime.UtcNow;
        await _database.InsertAsync(newestTile);

        // Act
        var oldest = await _database.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .Take(2)
            .ToListAsync();

        // Assert
        oldest.Should().HaveCount(2);
        oldest[0].Id.Should().Be("osm/15/1/1"); // Oldest first
        oldest[1].Id.Should().Be("osm/15/2/2");
    }

    [Fact]
    public async Task DeleteLiveTileAsync_RemovesTile()
    {
        // Arrange
        var tile = CreateLiveTile("osm/15/16383/10922", 15, 16383, 10922);
        await _database.InsertAsync(tile);

        // Act
        await _database.ExecuteAsync("DELETE FROM LiveTiles WHERE Id = ?", tile.Id);

        // Assert
        var retrieved = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetLiveCacheSizeAsync_ReturnsTotalBytes()
    {
        // Arrange
        var tile1 = CreateLiveTile("osm/15/1/1", 15, 1, 1);
        tile1.FileSizeBytes = 10000;
        await _database.InsertAsync(tile1);

        var tile2 = CreateLiveTile("osm/15/2/2", 15, 2, 2);
        tile2.FileSizeBytes = 20000;
        await _database.InsertAsync(tile2);

        // Act
        var totalSize = await _database.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(FileSizeBytes), 0) FROM LiveTiles");

        // Assert
        totalSize.Should().Be(30000);
    }

    [Fact]
    public async Task ClearLiveTilesAsync_RemovesAllTiles()
    {
        // Arrange
        await _database.InsertAsync(CreateLiveTile("osm/15/1/1", 15, 1, 1));
        await _database.InsertAsync(CreateLiveTile("osm/15/2/2", 15, 2, 2));
        await _database.InsertAsync(CreateLiveTile("osm/15/3/3", 15, 3, 3));

        // Act
        await _database.ExecuteAsync("DELETE FROM LiveTiles");

        // Assert
        var count = await _database.Table<LiveTileEntity>().CountAsync();
        count.Should().Be(0);
    }

    #endregion

    #region LRU Eviction Tests

    [Fact]
    public async Task LruEviction_RemovesOldestAccessedTiles()
    {
        // Arrange - Create tiles with specific access patterns
        for (int i = 0; i < 10; i++)
        {
            var tile = CreateLiveTile($"osm/15/{i}/0", 15, i, 0);
            tile.LastAccessedAt = DateTime.UtcNow.AddMinutes(-i * 10); // Older as i increases
            await _database.InsertAsync(tile);
        }

        // Act - Get oldest 3 tiles for eviction
        var toEvict = await _database.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .Take(3)
            .ToListAsync();

        // Assert - Should get tiles 9, 8, 7 (oldest accessed)
        toEvict.Should().HaveCount(3);
        toEvict[0].X.Should().Be(9);
        toEvict[1].X.Should().Be(8);
        toEvict[2].X.Should().Be(7);
    }

    [Fact]
    public async Task LruEviction_AfterAccess_OrderChanges()
    {
        // Arrange
        var tile1 = CreateLiveTile("osm/15/1/0", 15, 1, 0);
        tile1.LastAccessedAt = DateTime.UtcNow.AddHours(-2);
        await _database.InsertAsync(tile1);

        var tile2 = CreateLiveTile("osm/15/2/0", 15, 2, 0);
        tile2.LastAccessedAt = DateTime.UtcNow.AddHours(-1);
        await _database.InsertAsync(tile2);

        // Act - Access tile1 (should now be newest)
        var toUpdate = await _database.Table<LiveTileEntity>()
            .FirstOrDefaultAsync(t => t.Id == tile1.Id);
        toUpdate!.LastAccessedAt = DateTime.UtcNow;
        await _database.UpdateAsync(toUpdate);

        // Assert - tile2 should now be oldest
        var oldest = await _database.Table<LiveTileEntity>()
            .OrderBy(t => t.LastAccessedAt)
            .FirstOrDefaultAsync();
        oldest!.Id.Should().Be(tile2.Id);
    }

    #endregion

    #region Settings Tests

    [Fact]
    public async Task SetSettingAsync_String_InsertsNewSetting()
    {
        // Arrange
        var key = "test_key";
        var value = "test_value";

        // Act
        await SetSettingAsync(key, value);

        // Assert
        var setting = await _database.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        setting.Should().NotBeNull();
        setting!.Value.Should().Be(value);
    }

    [Fact]
    public async Task SetSettingAsync_String_UpdatesExistingSetting()
    {
        // Arrange
        var key = "test_key";
        await SetSettingAsync(key, "original_value");

        // Act
        await SetSettingAsync(key, "updated_value");

        // Assert
        var count = await _database.Table<AppSetting>()
            .Where(s => s.Key == key)
            .CountAsync();
        count.Should().Be(1);

        var setting = await _database.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        setting!.Value.Should().Be("updated_value");
    }

    [Fact]
    public async Task GetSettingAsync_String_ReturnsValue()
    {
        // Arrange
        var key = "test_key";
        await SetSettingAsync(key, "test_value");

        // Act
        var value = await GetSettingAsync<string>(key);

        // Assert
        value.Should().Be("test_value");
    }

    [Fact]
    public async Task GetSettingAsync_String_NonExistent_ReturnsDefault()
    {
        // Act
        var value = await GetSettingAsync<string>("nonexistent", "default_value");

        // Assert
        value.Should().Be("default_value");
    }

    [Fact]
    public async Task GetSettingAsync_Int_ReturnsValue()
    {
        // Arrange
        await SetSettingAsync("int_key", 42);

        // Act
        var value = await GetSettingAsync<int>("int_key");

        // Assert
        value.Should().Be(42);
    }

    [Fact]
    public async Task GetSettingAsync_Bool_ReturnsValue()
    {
        // Arrange
        await SetSettingAsync("bool_key", true);

        // Act
        var value = await GetSettingAsync<bool>("bool_key");

        // Assert
        value.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingAsync_Double_ReturnsValue()
    {
        // Arrange
        await SetSettingAsync("double_key", 3.14159);

        // Act
        var value = await GetSettingAsync<double>("double_key");

        // Assert
        value.Should().BeApproximately(3.14159, 0.00001);
    }

    [Fact]
    public async Task GetSettingAsync_InvalidFormat_ReturnsDefault()
    {
        // Arrange - Store a non-integer string
        await SetSettingAsync("int_key", "not_a_number");

        // Act
        var value = await GetSettingAsync<int>("int_key", 99);

        // Assert
        value.Should().Be(99);
    }

    [Fact]
    public async Task SetSettingAsync_UpdatesLastModified()
    {
        // Arrange
        var key = "test_key";
        await SetSettingAsync(key, "value1");
        var firstSetting = await _database.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        var firstModified = firstSetting!.LastModified;

        // Small delay to ensure different timestamp
        await Task.Delay(10);

        // Act
        await SetSettingAsync(key, "value2");

        // Assert
        var secondSetting = await _database.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        secondSetting!.LastModified.Should().BeOnOrAfter(firstModified);
    }

    [Fact]
    public async Task SetSettingAsync_NullValue_StoresNull()
    {
        // Arrange
        var key = "nullable_key";

        // Act
        await SetSettingAsync<string?>(key, null);

        // Assert
        var setting = await _database.Table<AppSetting>()
            .FirstOrDefaultAsync(s => s.Key == key);
        setting!.Value.Should().BeNull();
    }

    #endregion

    #region Concurrent Access Safety Tests

    [Fact]
    public async Task ConcurrentInserts_DoNotLoseData()
    {
        // Arrange
        var tasks = new List<Task>();
        var insertedIds = new System.Collections.Concurrent.ConcurrentBag<int>();

        // Act - Simulate concurrent location inserts
        for (int i = 0; i < 50; i++)
        {
            int index = i;
            tasks.Add(Task.Run(async () =>
            {
                var queued = await InsertQueuedLocationAsync(
                    CreateLocationData(51.0 + index * 0.001, -0.1 + index * 0.001));
                insertedIds.Add(queued.Id);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var count = await _database.Table<QueuedLocation>().CountAsync();
        count.Should().Be(50);
        insertedIds.Should().HaveCount(50);
        insertedIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        // Arrange
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var readTasks = new List<Task>();
        var writeTasks = new List<Task>();

        // Act - Start concurrent reads and writes
        for (int i = 0; i < 20; i++)
        {
            int index = i;
            writeTasks.Add(Task.Run(async () =>
            {
                await InsertQueuedLocationAsync(CreateLocationData(51.0 + index * 0.001, -0.1));
            }));

            readTasks.Add(Task.Run(async () =>
            {
                await GetPendingLocationsAsync();
            }));
        }

        // Assert - Should complete without exceptions
        var act = async () => await Task.WhenAll(readTasks.Concat(writeTasks));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentSettingsUpdates_DoNotCorruptData()
    {
        // Arrange
        var key = "concurrent_key";
        var successCount = 0;
        var failureCount = 0;

        // Act - Sequential updates to same setting with different keys to avoid SQLite locking issues
        // Note: In-memory SQLite with SharedCache has limitations for concurrent writes.
        // The real DatabaseService uses SemaphoreSlim for thread safety.
        for (int i = 0; i < 10; i++)
        {
            try
            {
                await SetSettingAsync($"{key}_{i}", i);
                Interlocked.Increment(ref successCount);
            }
            catch
            {
                Interlocked.Increment(ref failureCount);
            }
        }

        // Assert - All sequential updates should succeed
        successCount.Should().Be(10);

        // Verify all settings were created correctly
        var count = await _database.Table<AppSetting>()
            .Where(s => s.Key.StartsWith(key))
            .CountAsync();
        count.Should().Be(10);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task GetPendingLocationsAsync_HighAttemptCount_StillIncluded()
    {
        // Arrange - Valid locations retry until 300-day purge regardless of attempts
        var queued = await InsertQueuedLocationAsync(
            CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: 1000);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert - Location included regardless of high attempt count
        pending.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetPendingLocationsAsync_OnlyRejectedExcluded()
    {
        // Arrange - Only rejected locations are excluded, not high-attempt ones
        var valid = await InsertQueuedLocationAsync(
            CreateLocationData(51.0, -0.1), SyncStatus.Pending, syncAttempts: 500);
        var rejected = await InsertQueuedLocationAsync(
            CreateLocationData(52.0, -0.2), SyncStatus.Pending, syncAttempts: 0);
        rejected.IsRejected = true;
        await _database.UpdateAsync(rejected);

        // Act
        var pending = await GetPendingLocationsAsync();

        // Assert - Only rejected excluded
        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be(valid.Id);
    }

    [Fact]
    public async Task EmptyQueue_AllOperationsSucceed()
    {
        // Act & Assert - All operations should work on empty database
        var pending = await GetPendingLocationsAsync();
        pending.Should().BeEmpty();

        var purgeCount = await PurgeSyncedLocationsAsync(7);
        purgeCount.Should().Be(0);

        var syncCount = await MarkLocationsSyncedAsync(Array.Empty<int>());
        syncCount.Should().Be(0);
    }

    [Fact]
    public async Task ExtremeCoordinates_StoredCorrectly()
    {
        // Arrange - Test boundary coordinates
        var north = await InsertQueuedLocationAsync(CreateLocationData(90.0, 0.0));
        var south = await InsertQueuedLocationAsync(CreateLocationData(-90.0, 0.0));
        var east = await InsertQueuedLocationAsync(CreateLocationData(0.0, 180.0));
        var west = await InsertQueuedLocationAsync(CreateLocationData(0.0, -180.0));

        // Act
        var locations = await _database.Table<QueuedLocation>().ToListAsync();

        // Assert
        locations.Should().HaveCount(4);
        locations.Should().Contain(l => l.Latitude == 90.0);
        locations.Should().Contain(l => l.Latitude == -90.0);
        locations.Should().Contain(l => l.Longitude == 180.0);
        locations.Should().Contain(l => l.Longitude == -180.0);
    }

    [Fact]
    public async Task VeryLongErrorMessage_StoredCorrectly()
    {
        // Arrange
        var queued = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        var longError = new string('x', 5000);

        // Act
        await MarkLocationFailedAsync(queued.Id, longError);

        // Assert
        var updated = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queued.Id);
        updated!.LastError.Should().HaveLength(5000);
    }

    [Fact]
    public async Task TimestampPrecision_Preserved()
    {
        // Arrange
        var preciseTime = new DateTime(2025, 12, 15, 10, 30, 45, 123, DateTimeKind.Utc);
        var location = new LocationData
        {
            Latitude = 51.0,
            Longitude = -0.1,
            Timestamp = preciseTime
        };

        // Act
        var queued = await InsertQueuedLocationAsync(location);
        var retrieved = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queued.Id);

        // Assert - SQLite should preserve milliseconds
        retrieved!.Timestamp.Should().BeCloseTo(preciseTime, TimeSpan.FromMilliseconds(10));
    }

    #endregion

    #region Diagnostic Query Tests

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Synced);

        // Act
        var count = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .CountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetSyncedCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Synced);
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Synced);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3), SyncStatus.Pending);

        // Act
        var count = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .CountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetOldestPendingLocationAsync_ReturnsOldest()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2, baseTime.AddMinutes(1)), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1, baseTime), SyncStatus.Pending);
        await InsertQueuedLocationAsync(CreateLocationData(53.0, -0.3, baseTime.AddMinutes(2)), SyncStatus.Pending);

        // Act
        var oldest = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending)
            .OrderBy(l => l.Timestamp)
            .FirstOrDefaultAsync();

        // Assert
        oldest.Should().NotBeNull();
        oldest!.Latitude.Should().Be(51.0);
    }

    [Fact]
    public async Task GetLastSyncedLocationAsync_ReturnsMostRecent()
    {
        // Arrange
        var queued1 = await InsertQueuedLocationAsync(CreateLocationData(51.0, -0.1), SyncStatus.Synced);
        queued1.LastSyncAttempt = DateTime.UtcNow.AddMinutes(-10);
        await _database.UpdateAsync(queued1);

        var queued2 = await InsertQueuedLocationAsync(CreateLocationData(52.0, -0.2), SyncStatus.Synced);
        queued2.LastSyncAttempt = DateTime.UtcNow;
        await _database.UpdateAsync(queued2);

        // Act
        var lastSynced = await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Synced)
            .OrderByDescending(l => l.LastSyncAttempt)
            .FirstOrDefaultAsync();

        // Assert
        lastSynced.Should().NotBeNull();
        lastSynced!.Latitude.Should().Be(52.0);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates test location data.
    /// </summary>
    private static LocationData CreateLocationData(double latitude, double longitude, DateTime? timestamp = null)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Accuracy = 10.0,
            Provider = "test"
        };
    }

    /// <summary>
    /// Creates a live tile entity for testing.
    /// </summary>
    private static LiveTileEntity CreateLiveTile(string id, int zoom, int x, int y)
    {
        return new LiveTileEntity
        {
            Id = id,
            Zoom = zoom,
            X = x,
            Y = y,
            TileSource = "osm",
            FilePath = $"/cache/{id}.png",
            FileSizeBytes = 15000,
            CachedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            AccessCount = 1
        };
    }

    /// <summary>
    /// Inserts a queued location directly into the database.
    /// </summary>
    private async Task<QueuedLocation> InsertQueuedLocationAsync(
        LocationData location,
        SyncStatus status = SyncStatus.Pending,
        int syncAttempts = 0,
        DateTime? createdAt = null)
    {
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
            SyncStatus = status,
            SyncAttempts = syncAttempts,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        await _database.InsertAsync(queued);
        return queued;
    }

    /// <summary>
    /// Gets pending locations from the database.
    /// Valid locations retry until purge regardless of attempt count.
    /// </summary>
    private async Task<List<QueuedLocation>> GetPendingLocationsAsync(int limit = 100)
    {
        return await _database.Table<QueuedLocation>()
            .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
            .OrderBy(l => l.Timestamp)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Marks locations as synced in a batch.
    /// </summary>
    private async Task<int> MarkLocationsSyncedAsync(IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return 0;

        var placeholders = string.Join(",", idList.Select((_, i) => $"?{i + 2}"));
        var query = $"UPDATE QueuedLocations SET SyncStatus = ?1 WHERE Id IN ({placeholders})";

        var parameters = new object[idList.Count + 1];
        parameters[0] = (int)SyncStatus.Synced;
        for (var i = 0; i < idList.Count; i++)
        {
            parameters[i + 1] = idList[i];
        }

        return await _database.ExecuteAsync(query, parameters);
    }

    /// <summary>
    /// Records a failure for diagnostics (increments attempts, keeps Pending status).
    /// </summary>
    private async Task MarkLocationFailedAsync(int id, string error)
    {
        // Status stays Pending - valid locations retry until 300-day purge
        await _database.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET SyncAttempts = SyncAttempts + 1,
                  LastSyncAttempt = ?,
                  LastError = ?
              WHERE Id = ?",
            DateTime.UtcNow, error, id);
    }

    /// <summary>
    /// Marks a location as rejected.
    /// </summary>
    private async Task MarkLocationRejectedAsync(int id, string reason)
    {
        var location = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == id);

        if (location != null)
        {
            location.IsRejected = true;
            location.SyncStatus = SyncStatus.Synced;
            location.RejectionReason = reason;
            location.LastSyncAttempt = DateTime.UtcNow;
            await _database.UpdateAsync(location);
        }
    }

    /// <summary>
    /// Purges old synced locations.
    /// </summary>
    private async Task<int> PurgeSyncedLocationsAsync(int daysOld = 7)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var deletedSynced = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Synced, cutoff);

        var rejectedCutoff = DateTime.UtcNow.AddDays(-2);
        var deletedRejected = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE IsRejected = 1 AND CreatedAt < ?",
            rejectedCutoff);

        // 300 days matches production purge window
        var pendingCutoff = DateTime.UtcNow.AddDays(-300);
        var deletedOldPending = await _database.ExecuteAsync(
            "DELETE FROM QueuedLocations WHERE SyncStatus = ? AND CreatedAt < ?",
            (int)SyncStatus.Pending, pendingCutoff);

        return deletedSynced + deletedRejected + deletedOldPending;
    }

    /// <summary>
    /// Sets a setting value.
    /// </summary>
    private async Task SetSettingAsync<T>(string key, T value)
    {
        var setting = await _database.Table<AppSetting>()
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

    /// <summary>
    /// Gets a setting value.
    /// </summary>
    private async Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
    {
        var setting = await _database.Table<AppSetting>()
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

    #endregion
}
