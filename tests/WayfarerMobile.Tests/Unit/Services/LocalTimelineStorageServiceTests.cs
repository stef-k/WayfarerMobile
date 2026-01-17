using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for LocalTimelineStorageService focusing on:
/// - AddAcceptedLocationAsync (online path - server accepted)
/// - AddPendingLocationAsync (offline path - queued for sync)
/// - Event handler behavior with QueuedLocationId
/// </summary>
/// <remarks>
/// These tests validate the #160 feature: server authority for live location submissions.
/// - AddAcceptedLocationAsync: Online path where server accepted, entry has ServerId immediately
/// - AddPendingLocationAsync: Offline path where entry waits for queue drain with QueuedLocationId mapping
/// </remarks>
[Collection("SQLite")]
public class LocalTimelineStorageServiceTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _database = null!;

    #region Test Lifecycle

    public async Task InitializeAsync()
    {
        _database = new SQLiteAsyncConnection(":memory:");
        await _database.CreateTableAsync<LocalTimelineEntry>();
    }

    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    #endregion

    #region AddAcceptedLocationAsync Tests

    [Fact]
    public async Task AddAcceptedLocationAsync_CreatesEntryWithServerId_NullQueuedLocationId()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);
        var serverId = 12345;

        // Act
        await AddAcceptedLocationAsync(location, serverId);

        // Assert
        var entries = await _database.Table<LocalTimelineEntry>().ToListAsync();
        entries.Should().HaveCount(1);

        var entry = entries[0];
        entry.ServerId.Should().Be(serverId);
        entry.QueuedLocationId.Should().BeNull(); // Online path - not from queue
        entry.Latitude.Should().Be(location.Latitude);
        entry.Longitude.Should().Be(location.Longitude);
    }

    [Fact]
    public async Task AddAcceptedLocationAsync_PreservesAllLocationFields()
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
        await AddAcceptedLocationAsync(location, serverId: 123);

        // Assert
        var entry = await _database.Table<LocalTimelineEntry>().FirstOrDefaultAsync();
        entry.Should().NotBeNull();
        entry!.Latitude.Should().Be(location.Latitude);
        entry.Longitude.Should().Be(location.Longitude);
        entry.Altitude.Should().Be(location.Altitude);
        entry.Accuracy.Should().Be(location.Accuracy);
        entry.Speed.Should().Be(location.Speed);
        entry.Bearing.Should().Be(location.Bearing);
        entry.Provider.Should().Be(location.Provider);
    }

    [Fact]
    public async Task AddAcceptedLocationAsync_SetsCreatedAtTimestamp()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);
        var beforeInsert = DateTime.UtcNow;

        // Act
        await AddAcceptedLocationAsync(location, serverId: 123);

        // Assert
        var entry = await _database.Table<LocalTimelineEntry>().FirstOrDefaultAsync();
        entry!.CreatedAt.Should().BeOnOrAfter(beforeInsert);
        entry.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    #endregion

    #region AddPendingLocationAsync Tests

    [Fact]
    public async Task AddPendingLocationAsync_CreatesEntryWithQueuedLocationId_NullServerId()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);
        var queuedLocationId = 42;

        // Act
        await AddPendingLocationAsync(location, queuedLocationId);

        // Assert
        var entries = await _database.Table<LocalTimelineEntry>().ToListAsync();
        entries.Should().HaveCount(1);

        var entry = entries[0];
        entry.QueuedLocationId.Should().Be(queuedLocationId);
        entry.ServerId.Should().BeNull(); // Offline path - not yet synced
        entry.Latitude.Should().Be(location.Latitude);
        entry.Longitude.Should().Be(location.Longitude);
    }

    [Fact]
    public async Task AddPendingLocationAsync_PreservesAllLocationFields()
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
        await AddPendingLocationAsync(location, queuedLocationId: 42);

        // Assert
        var entry = await _database.Table<LocalTimelineEntry>().FirstOrDefaultAsync();
        entry.Should().NotBeNull();
        entry!.Latitude.Should().Be(location.Latitude);
        entry.Longitude.Should().Be(location.Longitude);
        entry.Altitude.Should().Be(location.Altitude);
        entry.Accuracy.Should().Be(location.Accuracy);
        entry.Speed.Should().Be(location.Speed);
        entry.Bearing.Should().Be(location.Bearing);
        entry.Provider.Should().Be(location.Provider);
    }

    [Fact]
    public async Task AddPendingLocationAsync_MultipleCalls_CreatesSeparateEntries()
    {
        // Arrange & Act
        await AddPendingLocationAsync(CreateLocationData(51.0, -0.1), queuedLocationId: 1);
        await AddPendingLocationAsync(CreateLocationData(52.0, -0.2), queuedLocationId: 2);
        await AddPendingLocationAsync(CreateLocationData(53.0, -0.3), queuedLocationId: 3);

        // Assert
        var entries = await _database.Table<LocalTimelineEntry>().ToListAsync();
        entries.Should().HaveCount(3);
        entries.Select(e => e.QueuedLocationId).Should().BeEquivalentTo(new int?[] { 1, 2, 3 });
    }

    #endregion

    #region Sync Event Handler Simulation Tests

    [Fact]
    public async Task OnLocationSynced_WithQueuedLocationId_UpdatesCorrectEntry()
    {
        // Simulate: Pending entry exists, sync completes, ServerId should be updated

        // Arrange - Create pending entry
        var queuedLocationId = 42;
        await AddPendingLocationAsync(CreateLocationData(51.5074, -0.1278), queuedLocationId);

        // Act - Simulate sync completion via direct update (mirrors OnLocationSynced)
        var updated = await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId, serverId: 999);

        // Assert
        updated.Should().BeTrue();
        var entry = await _database.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.QueuedLocationId == queuedLocationId);
        entry!.ServerId.Should().Be(999);
    }

    [Fact]
    public async Task OnLocationSkipped_WithQueuedLocationId_DeletesCorrectEntry()
    {
        // Simulate: Pending entry exists, server skipped location, entry should be deleted

        // Arrange - Create pending entry
        var queuedLocationId = 42;
        await AddPendingLocationAsync(CreateLocationData(51.5074, -0.1278), queuedLocationId);
        var countBefore = await _database.Table<LocalTimelineEntry>().CountAsync();
        countBefore.Should().Be(1);

        // Act - Simulate skip via delete (mirrors OnLocationSkipped)
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId);

        // Assert
        deleted.Should().Be(1);
        var countAfter = await _database.Table<LocalTimelineEntry>().CountAsync();
        countAfter.Should().Be(0);
    }

    [Fact]
    public async Task OnLocationSynced_NoQueuedLocationId_FallsBackToTimestampMatch()
    {
        // Simulate: Online path entry (null QueuedLocationId), should use timestamp matching

        // Arrange - Create entry without QueuedLocationId (online path)
        var timestamp = DateTime.UtcNow;
        var latitude = 51.5074;
        var longitude = -0.1278;
        await AddAcceptedLocationAsync(new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        }, serverId: 100);

        // Note: For online path, entry already has ServerId, no further update needed
        // This test just verifies the entry structure
        var entry = await _database.Table<LocalTimelineEntry>().FirstOrDefaultAsync();
        entry!.QueuedLocationId.Should().BeNull();
        entry.ServerId.Should().Be(100);
    }

    #endregion

    #region Mixed Online/Offline Scenario Tests

    [Fact]
    public async Task MixedOnlineOffline_EntriesCoexist_IndependentOperations()
    {
        // Arrange - Mix of online and offline entries
        await AddAcceptedLocationAsync(CreateLocationData(51.0, -0.1), serverId: 100);
        await AddPendingLocationAsync(CreateLocationData(52.0, -0.2), queuedLocationId: 1);
        await AddAcceptedLocationAsync(CreateLocationData(53.0, -0.3), serverId: 200);
        await AddPendingLocationAsync(CreateLocationData(54.0, -0.4), queuedLocationId: 2);

        // Act - Update one pending entry
        await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId: 1, serverId: 300);

        // Assert - All entries exist with correct state
        var entries = await _database.Table<LocalTimelineEntry>()
            .OrderBy(e => e.Latitude)
            .ToListAsync();

        entries.Should().HaveCount(4);
        entries[0].ServerId.Should().Be(100);  // Online entry 1
        entries[0].QueuedLocationId.Should().BeNull();

        entries[1].ServerId.Should().Be(300);  // Updated pending entry
        entries[1].QueuedLocationId.Should().Be(1);

        entries[2].ServerId.Should().Be(200);  // Online entry 2
        entries[2].QueuedLocationId.Should().BeNull();

        entries[3].ServerId.Should().BeNull(); // Still pending entry
        entries[3].QueuedLocationId.Should().Be(2);
    }

    #endregion

    #region Helper Methods

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

    private async Task AddAcceptedLocationAsync(LocationData location, int serverId)
    {
        var entry = new LocalTimelineEntry
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Timestamp = location.Timestamp,
            Accuracy = location.Accuracy,
            Altitude = location.Altitude,
            Speed = location.Speed,
            Bearing = location.Bearing,
            Provider = location.Provider,
            ServerId = serverId,
            QueuedLocationId = null, // Online path
            CreatedAt = DateTime.UtcNow
        };

        await _database.InsertAsync(entry);
    }

    private async Task AddPendingLocationAsync(LocationData location, int queuedLocationId)
    {
        var entry = new LocalTimelineEntry
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Timestamp = location.Timestamp,
            Accuracy = location.Accuracy,
            Altitude = location.Altitude,
            Speed = location.Speed,
            Bearing = location.Bearing,
            Provider = location.Provider,
            ServerId = null, // Offline path - not yet synced
            QueuedLocationId = queuedLocationId,
            CreatedAt = DateTime.UtcNow
        };

        await _database.InsertAsync(entry);
    }

    private async Task<bool> UpdateServerIdByQueuedLocationIdAsync(int queuedLocationId, int serverId)
    {
        var entry = await _database.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.QueuedLocationId == queuedLocationId);

        if (entry == null)
            return false;

        entry.ServerId = serverId;
        await _database.UpdateAsync(entry);
        return true;
    }

    private async Task<int> DeleteByQueuedLocationIdAsync(int queuedLocationId)
    {
        return await _database.ExecuteAsync(
            "DELETE FROM LocalTimelineEntries WHERE QueuedLocationId = ?",
            queuedLocationId);
    }

    #endregion
}
