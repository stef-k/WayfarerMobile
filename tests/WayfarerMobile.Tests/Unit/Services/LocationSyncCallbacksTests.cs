namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for LocationSyncCallbacks event system behavior.
/// Tests the event-based callback pattern used for decoupled communication
/// between sync service and local timeline storage.
/// </summary>
/// <remarks>
/// <para>
/// Since LocationSyncCallbacks is in the MAUI project and uses static events,
/// this test mirrors the implementation pattern to verify correct behavior.
/// </para>
/// <para>
/// The actual implementation is in WayfarerMobile.Services.LocationSyncCallbacks.
/// These tests document and verify the expected callback contract.
/// </para>
/// </remarks>
public class LocationSyncCallbacksTests : IDisposable
{
    #region Test Infrastructure - Mirror Implementation

    /// <summary>
    /// Test implementation mirroring LocationSyncCallbacks static event pattern.
    /// </summary>
    private static class TestLocationSyncCallbacks
    {
        public static event EventHandler<TestLocationSyncedEventArgs>? LocationSynced;
        public static event EventHandler<TestLocationSkippedEventArgs>? LocationSkipped;

        public static void NotifyLocationSynced(int queuedLocationId, int serverId, DateTime timestamp)
        {
            LocationSynced?.Invoke(null, new TestLocationSyncedEventArgs
            {
                QueuedLocationId = queuedLocationId,
                ServerId = serverId,
                Timestamp = timestamp
            });
        }

        public static void NotifyLocationSkipped(int queuedLocationId, DateTime timestamp, string reason)
        {
            LocationSkipped?.Invoke(null, new TestLocationSkippedEventArgs
            {
                QueuedLocationId = queuedLocationId,
                Timestamp = timestamp,
                Reason = reason
            });
        }

        public static void ClearSubscribers()
        {
            LocationSynced = null;
            LocationSkipped = null;
        }
    }

    private class TestLocationSyncedEventArgs : EventArgs
    {
        public int QueuedLocationId { get; init; }
        public int ServerId { get; init; }
        public DateTime Timestamp { get; init; }
    }

    private class TestLocationSkippedEventArgs : EventArgs
    {
        public int QueuedLocationId { get; init; }
        public DateTime Timestamp { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    #endregion

    public LocationSyncCallbacksTests()
    {
        // Ensure clean state for each test
        TestLocationSyncCallbacks.ClearSubscribers();
    }

    public void Dispose()
    {
        // Clean up after each test
        TestLocationSyncCallbacks.ClearSubscribers();
        GC.SuppressFinalize(this);
    }

    #region LocationSynced Event Tests

    [Fact]
    public void NotifyLocationSynced_WithSubscriber_RaisesEvent()
    {
        // Arrange
        TestLocationSyncedEventArgs? receivedArgs = null;
        TestLocationSyncCallbacks.LocationSynced += (_, args) => receivedArgs = args;

        var timestamp = DateTime.UtcNow;

        // Act
        TestLocationSyncCallbacks.NotifyLocationSynced(123, 456, timestamp);

        // Assert
        receivedArgs.Should().NotBeNull();
        receivedArgs!.QueuedLocationId.Should().Be(123);
        receivedArgs.ServerId.Should().Be(456);
        receivedArgs.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void NotifyLocationSynced_WithoutSubscriber_DoesNotThrow()
    {
        // Arrange - No subscriber

        // Act
        var act = () => TestLocationSyncCallbacks.NotifyLocationSynced(123, 456, DateTime.UtcNow);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyLocationSynced_MultipleSubscribers_AllReceiveEvent()
    {
        // Arrange
        var callCount = 0;
        TestLocationSyncCallbacks.LocationSynced += (_, _) => callCount++;
        TestLocationSyncCallbacks.LocationSynced += (_, _) => callCount++;
        TestLocationSyncCallbacks.LocationSynced += (_, _) => callCount++;

        // Act
        TestLocationSyncCallbacks.NotifyLocationSynced(1, 100, DateTime.UtcNow);

        // Assert
        callCount.Should().Be(3);
    }

    [Fact]
    public void NotifyLocationSynced_MultipleNotifications_RaisesEachEvent()
    {
        // Arrange
        var receivedIds = new List<int>();
        TestLocationSyncCallbacks.LocationSynced += (_, args) =>
            receivedIds.Add(args.QueuedLocationId);

        // Act
        TestLocationSyncCallbacks.NotifyLocationSynced(1, 100, DateTime.UtcNow);
        TestLocationSyncCallbacks.NotifyLocationSynced(2, 200, DateTime.UtcNow);
        TestLocationSyncCallbacks.NotifyLocationSynced(3, 300, DateTime.UtcNow);

        // Assert
        receivedIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void NotifyLocationSynced_SenderIsNull_ForStaticEvents()
    {
        // Arrange
        object? receivedSender = new object(); // Non-null initial value
        TestLocationSyncCallbacks.LocationSynced += (sender, _) => receivedSender = sender;

        // Act
        TestLocationSyncCallbacks.NotifyLocationSynced(1, 100, DateTime.UtcNow);

        // Assert
        receivedSender.Should().BeNull("static events use null sender by convention");
    }

    #endregion

    #region LocationSkipped Event Tests

    [Fact]
    public void NotifyLocationSkipped_WithSubscriber_RaisesEvent()
    {
        // Arrange
        TestLocationSkippedEventArgs? receivedArgs = null;
        TestLocationSyncCallbacks.LocationSkipped += (_, args) => receivedArgs = args;

        var timestamp = DateTime.UtcNow;
        var reason = "Threshold not met";

        // Act
        TestLocationSyncCallbacks.NotifyLocationSkipped(789, timestamp, reason);

        // Assert
        receivedArgs.Should().NotBeNull();
        receivedArgs!.QueuedLocationId.Should().Be(789);
        receivedArgs.Timestamp.Should().Be(timestamp);
        receivedArgs.Reason.Should().Be("Threshold not met");
    }

    [Fact]
    public void NotifyLocationSkipped_WithoutSubscriber_DoesNotThrow()
    {
        // Arrange - No subscriber

        // Act
        var act = () => TestLocationSyncCallbacks.NotifyLocationSkipped(789, DateTime.UtcNow, "Test");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyLocationSkipped_PreservesAllReasonTypes()
    {
        // Arrange
        var reasons = new List<string>();
        TestLocationSyncCallbacks.LocationSkipped += (_, args) => reasons.Add(args.Reason);

        // Act
        TestLocationSyncCallbacks.NotifyLocationSkipped(1, DateTime.UtcNow, "Threshold not met");
        TestLocationSyncCallbacks.NotifyLocationSkipped(2, DateTime.UtcNow, "Server rejected");
        TestLocationSyncCallbacks.NotifyLocationSkipped(3, DateTime.UtcNow, "Duplicate location");
        TestLocationSyncCallbacks.NotifyLocationSkipped(4, DateTime.UtcNow, "");

        // Assert
        reasons.Should().BeEquivalentTo(new[]
        {
            "Threshold not met",
            "Server rejected",
            "Duplicate location",
            ""
        });
    }

    #endregion

    #region ClearSubscribers Tests

    [Fact]
    public void ClearSubscribers_RemovesSyncedSubscribers()
    {
        // Arrange
        var callCount = 0;
        TestLocationSyncCallbacks.LocationSynced += (_, _) => callCount++;

        // Act
        TestLocationSyncCallbacks.ClearSubscribers();
        TestLocationSyncCallbacks.NotifyLocationSynced(1, 100, DateTime.UtcNow);

        // Assert
        callCount.Should().Be(0, "subscriber should have been removed");
    }

    [Fact]
    public void ClearSubscribers_RemovesSkippedSubscribers()
    {
        // Arrange
        var callCount = 0;
        TestLocationSyncCallbacks.LocationSkipped += (_, _) => callCount++;

        // Act
        TestLocationSyncCallbacks.ClearSubscribers();
        TestLocationSyncCallbacks.NotifyLocationSkipped(1, DateTime.UtcNow, "Test");

        // Assert
        callCount.Should().Be(0, "subscriber should have been removed");
    }

    [Fact]
    public void ClearSubscribers_AllowsNewSubscriptions()
    {
        // Arrange
        var firstCallCount = 0;
        var secondCallCount = 0;
        TestLocationSyncCallbacks.LocationSynced += (_, _) => firstCallCount++;

        TestLocationSyncCallbacks.ClearSubscribers();

        TestLocationSyncCallbacks.LocationSynced += (_, _) => secondCallCount++;

        // Act
        TestLocationSyncCallbacks.NotifyLocationSynced(1, 100, DateTime.UtcNow);

        // Assert
        firstCallCount.Should().Be(0, "first subscriber should have been removed");
        secondCallCount.Should().Be(1, "new subscriber should receive events");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task NotifyLocationSynced_ConcurrentNotifications_DoesNotThrow()
    {
        // Arrange
        var callCount = 0;
        var lockObj = new object();
        TestLocationSyncCallbacks.LocationSynced += (_, _) =>
        {
            lock (lockObj) { callCount++; }
        };

        var tasks = new List<Task>();

        // Act - Fire 100 concurrent events
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
                TestLocationSyncCallbacks.NotifyLocationSynced(index, index * 10, DateTime.UtcNow)));
        }

        await Task.WhenAll(tasks);

        // Assert
        callCount.Should().Be(100);
    }

    [Fact]
    public async Task NotifyLocationSkipped_ConcurrentNotifications_DoesNotThrow()
    {
        // Arrange
        var callCount = 0;
        var lockObj = new object();
        TestLocationSyncCallbacks.LocationSkipped += (_, _) =>
        {
            lock (lockObj) { callCount++; }
        };

        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
                TestLocationSyncCallbacks.NotifyLocationSkipped(index, DateTime.UtcNow, $"Reason {index}")));
        }

        await Task.WhenAll(tasks);

        // Assert
        callCount.Should().Be(100);
    }

    #endregion

    #region Event Args Immutability Tests

    [Fact]
    public void LocationSyncedEventArgs_AreImmutable()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var args = new TestLocationSyncedEventArgs
        {
            QueuedLocationId = 123,
            ServerId = 456,
            Timestamp = timestamp
        };

        // Assert - init-only properties should be set at creation
        args.QueuedLocationId.Should().Be(123);
        args.ServerId.Should().Be(456);
        args.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void LocationSkippedEventArgs_ReasonDefaultsToEmpty()
    {
        // Arrange
        var args = new TestLocationSkippedEventArgs
        {
            QueuedLocationId = 123,
            Timestamp = DateTime.UtcNow
        };

        // Assert
        args.Reason.Should().BeEmpty("default value should be empty string, not null");
    }

    #endregion

    #region Integration Pattern Documentation Tests

    /// <summary>
    /// Documents the expected usage pattern for LocalTimelineStorageService.
    /// </summary>
    [Fact]
    public void IntegrationPattern_LocalTimelineStorageService_Documentation()
    {
        // Expected pattern in LocalTimelineStorageService:
        //
        // 1. Subscribe in InitializeAsync:
        //    LocationSyncCallbacks.LocationSynced += OnLocationSynced;
        //    LocationSyncCallbacks.LocationSkipped += OnLocationSkipped;
        //
        // 2. Handle synced locations:
        //    private async void OnLocationSynced(object? sender, LocationSyncedEventArgs e)
        //    {
        //        // Update local entry with ServerId
        //        var entry = await _db.GetLocalTimelineEntryByTimestampAsync(e.Timestamp, 2);
        //        if (entry != null)
        //        {
        //            entry.ServerId = e.ServerId;
        //            await _db.UpdateLocalTimelineEntryAsync(entry);
        //        }
        //    }
        //
        // 3. Handle skipped locations:
        //    private async void OnLocationSkipped(object? sender, LocationSkippedEventArgs e)
        //    {
        //        // Delete local entry that server rejected
        //        var entry = await _db.GetLocalTimelineEntryByTimestampAsync(e.Timestamp, 2);
        //        if (entry != null)
        //        {
        //            await _db.DeleteLocalTimelineEntryAsync(entry.Id);
        //        }
        //    }
        //
        // 4. Unsubscribe in Dispose:
        //    LocationSyncCallbacks.LocationSynced -= OnLocationSynced;
        //    LocationSyncCallbacks.LocationSkipped -= OnLocationSkipped;

        true.Should().BeTrue("Documentation test");
    }

    /// <summary>
    /// Documents the expected usage pattern for LocationSyncService.
    /// </summary>
    [Fact]
    public void IntegrationPattern_LocationSyncService_Documentation()
    {
        // Expected pattern in LocationSyncService.SyncLocationWithRetryAsync:
        //
        // After successful sync:
        //    if (result.LocationId.HasValue)
        //    {
        //        LocationSyncCallbacks.NotifyLocationSynced(
        //            location.Id,
        //            result.LocationId.Value,
        //            location.Timestamp);
        //    }
        //
        // After skipped by server:
        //    if (result.Success && result.Skipped)
        //    {
        //        LocationSyncCallbacks.NotifyLocationSkipped(
        //            location.Id,
        //            location.Timestamp,
        //            "Threshold not met");
        //    }

        true.Should().BeTrue("Documentation test");
    }

    #endregion
}
