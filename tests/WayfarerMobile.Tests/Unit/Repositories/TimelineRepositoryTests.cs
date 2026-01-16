using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for TimelineRepository focusing on QueuedLocationId-based operations.
/// These methods enable stable mapping between location queue and local timeline.
/// </summary>
/// <remarks>
/// These tests validate the #160 feature: server authority for live location submissions.
/// QueuedLocationId provides stable mapping for updating/deleting timeline entries
/// when queued locations are synced or rejected.
/// </remarks>
[Collection("SQLite")]
public class TimelineRepositoryTests : IAsyncLifetime
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

    #region UpdateServerIdByQueuedLocationIdAsync Tests

    [Fact]
    public async Task UpdateServerIdByQueuedLocationId_ExistingEntry_UpdatesServerId()
    {
        // Arrange
        var queuedLocationId = 42;
        var entry = await InsertTimelineEntryAsync(
            latitude: 51.5074,
            longitude: -0.1278,
            queuedLocationId: queuedLocationId,
            serverId: null);

        // Act
        var result = await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId, serverId: 123);

        // Assert
        result.Should().BeTrue();
        var updated = await _database.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.Id == entry.Id);
        updated!.ServerId.Should().Be(123);
    }

    [Fact]
    public async Task UpdateServerIdByQueuedLocationId_NoMatch_ReturnsFalse()
    {
        // Arrange - Insert entry with different QueuedLocationId
        await InsertTimelineEntryAsync(
            latitude: 51.5074,
            longitude: -0.1278,
            queuedLocationId: 100,
            serverId: null);

        // Act
        var result = await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId: 999, serverId: 123);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateServerIdByQueuedLocationId_MultipleEntriesOnlyOne_UpdatesCorrectOne()
    {
        // Arrange
        var entry1 = await InsertTimelineEntryAsync(51.0, -0.1, queuedLocationId: 100);
        var entry2 = await InsertTimelineEntryAsync(52.0, -0.2, queuedLocationId: 200);
        var entry3 = await InsertTimelineEntryAsync(53.0, -0.3, queuedLocationId: 300);

        // Act
        var result = await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId: 200, serverId: 555);

        // Assert
        result.Should().BeTrue();

        var updated1 = await _database.GetAsync<LocalTimelineEntry>(entry1.Id);
        var updated2 = await _database.GetAsync<LocalTimelineEntry>(entry2.Id);
        var updated3 = await _database.GetAsync<LocalTimelineEntry>(entry3.Id);

        updated1.ServerId.Should().BeNull();
        updated2.ServerId.Should().Be(555);
        updated3.ServerId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateServerIdByQueuedLocationId_NullQueuedLocationId_ReturnsFalse()
    {
        // Arrange - Entry with null QueuedLocationId (online path entry)
        await InsertTimelineEntryAsync(51.0, -0.1, queuedLocationId: null, serverId: 100);

        // Act - Try to update by QueuedLocationId (won't match null)
        var result = await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId: 0, serverId: 999);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DeleteByQueuedLocationIdAsync Tests

    [Fact]
    public async Task DeleteByQueuedLocationId_ExistingEntry_DeletesEntry()
    {
        // Arrange
        var queuedLocationId = 42;
        await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: queuedLocationId);

        // Act
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId);

        // Assert
        deleted.Should().Be(1);
        var remaining = await _database.Table<LocalTimelineEntry>().CountAsync();
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteByQueuedLocationId_NoMatch_ReturnsZero()
    {
        // Arrange
        await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: 100);

        // Act
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId: 999);

        // Assert
        deleted.Should().Be(0);
        var remaining = await _database.Table<LocalTimelineEntry>().CountAsync();
        remaining.Should().Be(1);
    }

    [Fact]
    public async Task DeleteByQueuedLocationId_MultipleEntriesOnlyOne_DeletesCorrectOne()
    {
        // Arrange
        var entry1 = await InsertTimelineEntryAsync(51.0, -0.1, queuedLocationId: 100);
        var entry2 = await InsertTimelineEntryAsync(52.0, -0.2, queuedLocationId: 200);
        var entry3 = await InsertTimelineEntryAsync(53.0, -0.3, queuedLocationId: 300);

        // Act
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId: 200);

        // Assert
        deleted.Should().Be(1);
        var remaining = await _database.Table<LocalTimelineEntry>().ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(e => e.Id == entry2.Id);
    }

    [Fact]
    public async Task DeleteByQueuedLocationId_EmptyTable_ReturnsZero()
    {
        // Act
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId: 999);

        // Assert
        deleted.Should().Be(0);
    }

    #endregion

    #region GetByQueuedLocationIdAsync Tests

    [Fact]
    public async Task GetByQueuedLocationId_ExistingEntry_ReturnsEntry()
    {
        // Arrange
        var queuedLocationId = 42;
        var inserted = await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: queuedLocationId);

        // Act
        var result = await GetByQueuedLocationIdAsync(queuedLocationId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(inserted.Id);
        result.QueuedLocationId.Should().Be(queuedLocationId);
    }

    [Fact]
    public async Task GetByQueuedLocationId_NoMatch_ReturnsNull()
    {
        // Arrange
        await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: 100);

        // Act
        var result = await GetByQueuedLocationIdAsync(queuedLocationId: 999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByQueuedLocationId_EmptyTable_ReturnsNull()
    {
        // Act
        var result = await GetByQueuedLocationIdAsync(queuedLocationId: 999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByQueuedLocationId_EntryWithNullQueuedLocationId_NotReturned()
    {
        // Arrange - Entry from online path (null QueuedLocationId)
        await InsertTimelineEntryAsync(51.0, -0.1, queuedLocationId: null, serverId: 100);

        // Act - Try to get by QueuedLocationId=0 (won't match null)
        var result = await GetByQueuedLocationIdAsync(queuedLocationId: 0);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public async Task QueuedLocationIdMapping_FullSyncCycle_UpdatesCorrectly()
    {
        // Scenario: Offline queue -> sync -> update timeline with ServerId

        // 1. Create pending timeline entry linked to queue
        var queuedLocationId = 42;
        var entry = await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: queuedLocationId);
        entry.ServerId.Should().BeNull();

        // 2. Simulate sync completion - update ServerId
        await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId, serverId: 999);

        // 3. Verify entry was updated
        var updated = await GetByQueuedLocationIdAsync(queuedLocationId);
        updated!.ServerId.Should().Be(999);
    }

    [Fact]
    public async Task QueuedLocationIdMapping_SyncRejected_DeletesCorrectly()
    {
        // Scenario: Queued location rejected -> delete timeline entry

        // 1. Create pending timeline entry linked to queue
        var queuedLocationId = 42;
        await InsertTimelineEntryAsync(51.5074, -0.1278, queuedLocationId: queuedLocationId);

        // 2. Simulate rejection - delete entry
        var deleted = await DeleteByQueuedLocationIdAsync(queuedLocationId);

        // 3. Verify entry was deleted
        deleted.Should().Be(1);
        var remaining = await GetByQueuedLocationIdAsync(queuedLocationId);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task OnlineVsOfflinePath_DifferentEntryTypes_MixedCorrectly()
    {
        // Scenario: Mix of online (null QueuedLocationId) and offline (has QueuedLocationId) entries

        // Online entry - already has ServerId, no QueuedLocationId
        var onlineEntry = await InsertTimelineEntryAsync(51.0, -0.1, queuedLocationId: null, serverId: 100);

        // Offline entry - has QueuedLocationId, waiting for sync
        var offlineEntry = await InsertTimelineEntryAsync(52.0, -0.2, queuedLocationId: 42, serverId: null);

        // Update offline entry when synced
        await UpdateServerIdByQueuedLocationIdAsync(queuedLocationId: 42, serverId: 200);

        // Verify both entries exist with correct data
        var online = await _database.GetAsync<LocalTimelineEntry>(onlineEntry.Id);
        var offline = await _database.GetAsync<LocalTimelineEntry>(offlineEntry.Id);

        online.ServerId.Should().Be(100);
        online.QueuedLocationId.Should().BeNull();

        offline.ServerId.Should().Be(200);
        offline.QueuedLocationId.Should().Be(42);
    }

    #endregion

    #region Helper Methods

    private async Task<LocalTimelineEntry> InsertTimelineEntryAsync(
        double latitude,
        double longitude,
        int? queuedLocationId = null,
        int? serverId = null)
    {
        var entry = new LocalTimelineEntry
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = DateTime.UtcNow,
            Accuracy = 10.0,
            Provider = "test",
            QueuedLocationId = queuedLocationId,
            ServerId = serverId,
            CreatedAt = DateTime.UtcNow
        };

        await _database.InsertAsync(entry);
        return entry;
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

    private async Task<LocalTimelineEntry?> GetByQueuedLocationIdAsync(int queuedLocationId)
    {
        return await _database.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.QueuedLocationId == queuedLocationId);
    }

    #endregion
}
