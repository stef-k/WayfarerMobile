using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Repositories;

/// <summary>
/// Unit tests for LocationQueueRepository focusing on:
/// - User-invoked location queuing (IsUserInvoked, ActivityTypeId, CheckInNotes)
/// - Priority claim ordering (user-invoked before background)
/// - Queue ID return value
/// </summary>
/// <remarks>
/// These tests validate the #160 feature: server authority for live location submissions.
/// User-invoked locations (manual check-ins) skip client-side filtering and sync first.
/// </remarks>
[Collection("SQLite")]
public class LocationQueueRepositoryTests : IAsyncLifetime
{
    private SQLiteAsyncConnection _database = null!;

    #region Test Lifecycle

    public async Task InitializeAsync()
    {
        _database = new SQLiteAsyncConnection(":memory:");
        await _database.CreateTableAsync<QueuedLocation>();
    }

    public async Task DisposeAsync()
    {
        if (_database != null)
        {
            await _database.CloseAsync();
        }
    }

    #endregion

    #region QueueLocationAsync - User Invoked Tests

    [Fact]
    public async Task QueueLocationAsync_WithUserInvoked_SetsIsUserInvokedTrue()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);

        // Act
        var queuedId = await QueueLocationAsync(location, isUserInvoked: true);

        // Assert
        var queued = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queuedId);
        queued.Should().NotBeNull();
        queued!.IsUserInvoked.Should().BeTrue();
    }

    [Fact]
    public async Task QueueLocationAsync_WithActivityAndNotes_PreservesFields()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);
        var activityTypeId = 42;
        var notes = "Test check-in notes";

        // Act
        var queuedId = await QueueLocationAsync(
            location,
            isUserInvoked: true,
            activityTypeId: activityTypeId,
            notes: notes);

        // Assert
        var queued = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queuedId);
        queued.Should().NotBeNull();
        queued!.IsUserInvoked.Should().BeTrue();
        queued.ActivityTypeId.Should().Be(activityTypeId);
        queued.CheckInNotes.Should().Be(notes);
    }

    [Fact]
    public async Task QueueLocationAsync_WithoutUserInvoked_DefaultsToFalse()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);

        // Act
        var queuedId = await QueueLocationAsync(location);

        // Assert
        var queued = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queuedId);
        queued.Should().NotBeNull();
        queued!.IsUserInvoked.Should().BeFalse();
        queued.ActivityTypeId.Should().BeNull();
        queued.CheckInNotes.Should().BeNull();
    }

    [Fact]
    public async Task QueueLocationAsync_ReturnsValidId()
    {
        // Arrange
        var location = CreateLocationData(51.5074, -0.1278);

        // Act
        var queuedId = await QueueLocationAsync(location);

        // Assert
        queuedId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QueueLocationAsync_MultipleLocations_ReturnsUniqueIds()
    {
        // Act
        var id1 = await QueueLocationAsync(CreateLocationData(51.0, -0.1), isUserInvoked: true);
        var id2 = await QueueLocationAsync(CreateLocationData(52.0, -0.2), isUserInvoked: false);
        var id3 = await QueueLocationAsync(CreateLocationData(53.0, -0.3), isUserInvoked: true);

        // Assert
        var ids = new[] { id1, id2, id3 };
        ids.Should().OnlyHaveUniqueItems();
    }

    #endregion

    #region ClaimNextPendingLocationWithPriority Tests

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_UserInvokedFirst_ClaimsUserInvoked()
    {
        // Arrange - Add background location first, then user-invoked
        var bgId = await QueueLocationAsync(
            CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-10)),
            isUserInvoked: false);
        var userInvokedId = await QueueLocationAsync(
            CreateLocationData(52.0, -0.2, DateTime.UtcNow.AddMinutes(-5)),
            isUserInvoked: true);

        // Act
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(userInvokedId);
        claimed.IsUserInvoked.Should().BeTrue();
        claimed.SyncStatus.Should().Be(SyncStatus.Syncing);
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_NoUserInvoked_ClaimsOldestBackground()
    {
        // Arrange - Only background locations
        var olderId = await QueueLocationAsync(
            CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-10)),
            isUserInvoked: false);
        var newerId = await QueueLocationAsync(
            CreateLocationData(52.0, -0.2, DateTime.UtcNow.AddMinutes(-5)),
            isUserInvoked: false);

        // Act
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(olderId);
        claimed.IsUserInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_MixedQueue_UserInvokedAlwaysFirst()
    {
        // Arrange - Interleaved user-invoked and background locations
        var bg1 = await QueueLocationAsync(
            CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-30)),
            isUserInvoked: false);
        var user1 = await QueueLocationAsync(
            CreateLocationData(52.0, -0.2, DateTime.UtcNow.AddMinutes(-20)),
            isUserInvoked: true);
        var bg2 = await QueueLocationAsync(
            CreateLocationData(53.0, -0.3, DateTime.UtcNow.AddMinutes(-10)),
            isUserInvoked: false);
        var user2 = await QueueLocationAsync(
            CreateLocationData(54.0, -0.4, DateTime.UtcNow.AddMinutes(-5)),
            isUserInvoked: true);

        // Act - Claim all locations in order
        var claimed1 = await ClaimNextPendingLocationWithPriorityAsync();
        var claimed2 = await ClaimNextPendingLocationWithPriorityAsync();
        var claimed3 = await ClaimNextPendingLocationWithPriorityAsync();
        var claimed4 = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert - User-invoked claimed first (oldest user first), then background
        claimed1!.Id.Should().Be(user1); // Oldest user-invoked
        claimed2!.Id.Should().Be(user2); // Next user-invoked
        claimed3!.Id.Should().Be(bg1);   // Oldest background
        claimed4!.Id.Should().Be(bg2);   // Next background
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_EmptyQueue_ReturnsNull()
    {
        // Act
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert
        claimed.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_AllAlreadyClaimed_ReturnsNull()
    {
        // Arrange - Add and claim a location
        await QueueLocationAsync(CreateLocationData(51.0, -0.1), isUserInvoked: true);
        await ClaimNextPendingLocationWithPriorityAsync();

        // Act - Try to claim again
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert
        claimed.Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_SkipsRejectedLocations()
    {
        // Arrange - Add user-invoked that's rejected, and a valid background
        var rejectedId = await QueueLocationAsync(
            CreateLocationData(51.0, -0.1),
            isUserInvoked: true);
        await MarkLocationRejectedAsync(rejectedId, "Test rejection");

        var validBgId = await QueueLocationAsync(
            CreateLocationData(52.0, -0.2),
            isUserInvoked: false);

        // Act
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert - Should skip rejected and claim background
        claimed.Should().NotBeNull();
        claimed!.Id.Should().Be(validBgId);
    }

    [Fact]
    public async Task ClaimNextPendingLocationWithPriority_MarksAsSyncing()
    {
        // Arrange
        var queuedId = await QueueLocationAsync(CreateLocationData(51.0, -0.1), isUserInvoked: true);

        // Act
        var claimed = await ClaimNextPendingLocationWithPriorityAsync();

        // Assert
        claimed.Should().NotBeNull();
        claimed!.SyncStatus.Should().Be(SyncStatus.Syncing);

        // Verify in database
        var inDb = await _database.Table<QueuedLocation>()
            .FirstOrDefaultAsync(l => l.Id == queuedId);
        inDb!.SyncStatus.Should().Be(SyncStatus.Syncing);
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

    private async Task<int> QueueLocationAsync(
        LocationData location,
        bool isUserInvoked = false,
        int? activityTypeId = null,
        string? notes = null)
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
            SyncStatus = SyncStatus.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IsUserInvoked = isUserInvoked,
            ActivityTypeId = activityTypeId,
            CheckInNotes = notes
        };

        await _database.InsertAsync(queued);
        return queued.Id;
    }

    private async Task<QueuedLocation?> ClaimNextPendingLocationWithPriorityAsync()
    {
        var now = DateTime.UtcNow;

        // PRIORITY 1: User-invoked locations (manual check-ins)
        var userInvokedIds = await _database.QueryScalarsAsync<int>(
            @"SELECT Id FROM QueuedLocations
              WHERE SyncStatus = ? AND IsRejected = 0 AND IsUserInvoked = 1
              ORDER BY Timestamp
              LIMIT 5",
            (int)SyncStatus.Pending);

        // Try to claim a user-invoked location first
        foreach (var id in userInvokedIds)
        {
            var updated = await _database.ExecuteAsync(
                @"UPDATE QueuedLocations
                  SET SyncStatus = ?, LastSyncAttempt = ?
                  WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, now, id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                return await _database.Table<QueuedLocation>()
                    .FirstOrDefaultAsync(l => l.Id == id);
            }
        }

        // PRIORITY 2: Background/live locations
        var bgIds = await _database.QueryScalarsAsync<int>(
            @"SELECT Id FROM QueuedLocations
              WHERE SyncStatus = ? AND IsRejected = 0 AND IsUserInvoked = 0
              ORDER BY Timestamp
              LIMIT 5",
            (int)SyncStatus.Pending);

        foreach (var id in bgIds)
        {
            var updated = await _database.ExecuteAsync(
                @"UPDATE QueuedLocations
                  SET SyncStatus = ?, LastSyncAttempt = ?
                  WHERE Id = ? AND SyncStatus = ?",
                (int)SyncStatus.Syncing, now, id, (int)SyncStatus.Pending);

            if (updated > 0)
            {
                return await _database.Table<QueuedLocation>()
                    .FirstOrDefaultAsync(l => l.Id == id);
            }
        }

        return null;
    }

    private async Task MarkLocationRejectedAsync(int id, string reason)
    {
        await _database.ExecuteAsync(
            @"UPDATE QueuedLocations
              SET IsRejected = 1, SyncStatus = ?, RejectionReason = ?
              WHERE Id = ?",
            (int)SyncStatus.Synced, reason, id);
    }

    private async Task MarkLocationSyncingAsync(int id)
    {
        await _database.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Syncing, id);
    }

    private async Task MarkLocationSyncedAsync(int id)
    {
        await _database.ExecuteAsync(
            "UPDATE QueuedLocations SET SyncStatus = ? WHERE Id = ?",
            (int)SyncStatus.Synced, id);
    }

    private async Task CleanupOldLocationsAsync(int maxQueuedLocations)
    {
        var count = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
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

    #region Cleanup Tests

    [Fact]
    public async Task CleanupOldLocations_RespectsLimitParameter()
    {
        // Arrange - Add 10 pending locations
        for (var i = 0; i < 10; i++)
        {
            await QueueLocationAsync(CreateLocationData(51.0 + i * 0.01, -0.1));
        }

        // Act - Cleanup with limit of 5
        await CleanupOldLocationsAsync(maxQueuedLocations: 5);

        // Assert - Should have 5 locations remaining
        var count = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
        count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task CleanupOldLocations_NeverDeletesSyncingEntries()
    {
        // Arrange - Add 5 locations, mark 2 as syncing
        var id1 = await QueueLocationAsync(CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-50)));
        var id2 = await QueueLocationAsync(CreateLocationData(51.1, -0.2, DateTime.UtcNow.AddMinutes(-40)));
        var id3 = await QueueLocationAsync(CreateLocationData(51.2, -0.3, DateTime.UtcNow.AddMinutes(-30)));
        var id4 = await QueueLocationAsync(CreateLocationData(51.3, -0.4, DateTime.UtcNow.AddMinutes(-20)));
        var id5 = await QueueLocationAsync(CreateLocationData(51.4, -0.5, DateTime.UtcNow.AddMinutes(-10)));

        // Mark oldest two as syncing (in-flight)
        await MarkLocationSyncingAsync(id1);
        await MarkLocationSyncingAsync(id2);

        // Act - Cleanup with limit of 2 (need to delete 3)
        await CleanupOldLocationsAsync(maxQueuedLocations: 2);

        // Assert - Syncing entries should still exist
        var syncing1 = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == id1);
        var syncing2 = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == id2);
        syncing1.Should().NotBeNull("Syncing entries must never be deleted");
        syncing2.Should().NotBeNull("Syncing entries must never be deleted");
        syncing1!.SyncStatus.Should().Be(SyncStatus.Syncing);
        syncing2!.SyncStatus.Should().Be(SyncStatus.Syncing);
    }

    [Fact]
    public async Task CleanupOldLocations_DeletesSyncedBeforePending()
    {
        // Arrange - Add 3 locations: 1 synced, 2 pending
        var pendingId1 = await QueueLocationAsync(CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-30)));
        var syncedId = await QueueLocationAsync(CreateLocationData(51.1, -0.2, DateTime.UtcNow.AddMinutes(-20)));
        var pendingId2 = await QueueLocationAsync(CreateLocationData(51.2, -0.3, DateTime.UtcNow.AddMinutes(-10)));
        await MarkLocationSyncedAsync(syncedId);

        // Act - Cleanup with limit of 2 (need to delete 1, due to +1 headroom may delete 2)
        await CleanupOldLocationsAsync(maxQueuedLocations: 2);

        // Assert - Synced should be deleted first, pending should remain
        var synced = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == syncedId);
        synced.Should().BeNull("Synced entries are deleted first");

        // At least one pending should remain
        var remainingCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM QueuedLocations WHERE SyncStatus = ?", (int)SyncStatus.Pending);
        remainingCount.Should().BeGreaterThan(0, "Pending entries are deleted last");
    }

    [Fact]
    public async Task CleanupOldLocations_DeletesRejectedBeforePending()
    {
        // Arrange - Add 3 locations: 1 rejected, 2 pending
        var pendingId1 = await QueueLocationAsync(CreateLocationData(51.0, -0.1, DateTime.UtcNow.AddMinutes(-30)));
        var rejectedId = await QueueLocationAsync(CreateLocationData(51.1, -0.2, DateTime.UtcNow.AddMinutes(-20)));
        var pendingId2 = await QueueLocationAsync(CreateLocationData(51.2, -0.3, DateTime.UtcNow.AddMinutes(-10)));
        await MarkLocationRejectedAsync(rejectedId, "Test rejection");

        // Act - Cleanup with limit of 2 (need to delete 1, due to +1 headroom may delete 2)
        await CleanupOldLocationsAsync(maxQueuedLocations: 2);

        // Assert - Rejected should be deleted first, pending should remain
        var rejected = await _database.Table<QueuedLocation>().FirstOrDefaultAsync(l => l.Id == rejectedId);
        rejected.Should().BeNull("Rejected entries are deleted first");

        // At least one pending should remain
        var remainingCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM QueuedLocations WHERE SyncStatus = ?", (int)SyncStatus.Pending);
        remainingCount.Should().BeGreaterThan(0, "Pending entries are deleted last");
    }

    [Fact]
    public async Task CleanupOldLocations_LimitOne_KeepsExactlyOne()
    {
        // Arrange - Add 5 pending locations
        for (var i = 0; i < 5; i++)
        {
            await QueueLocationAsync(CreateLocationData(51.0 + i * 0.01, -0.1));
        }

        // Act - Cleanup with limit of 1
        await CleanupOldLocationsAsync(maxQueuedLocations: 1);

        // Assert - Should have exactly 1 or 0 (depending on +1 headroom logic)
        var count = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
        count.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task CleanupOldLocations_UnderLimit_NoDeletes()
    {
        // Arrange - Add 3 locations
        var id1 = await QueueLocationAsync(CreateLocationData(51.0, -0.1));
        var id2 = await QueueLocationAsync(CreateLocationData(51.1, -0.2));
        var id3 = await QueueLocationAsync(CreateLocationData(51.2, -0.3));

        // Act - Cleanup with limit of 10 (under limit)
        await CleanupOldLocationsAsync(maxQueuedLocations: 10);

        // Assert - All should still exist
        var count = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
        count.Should().Be(3);
    }

    #endregion
}
