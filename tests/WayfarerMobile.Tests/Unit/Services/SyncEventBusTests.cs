using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for SyncEventBus.
/// Tests centralized sync event publishing and subscription.
/// </summary>
/// <remarks>
/// Part of Phase 0 infrastructure for refactoring (Issue #93).
/// </remarks>
public class SyncEventBusTests
{
    private readonly SyncEventBus _bus;
    private readonly ILogger<SyncEventBus> _logger;

    public SyncEventBusTests()
    {
        _logger = NullLogger<SyncEventBus>.Instance;
        _bus = new SyncEventBus(_logger);
    }

    #region SyncSucceeded Tests

    [Fact]
    public void PublishSyncSuccess_RaisesEvent()
    {
        // Arrange
        SyncSuccessEventArgs? received = null;
        _bus.SyncSucceeded += (_, e) => received = e;

        var args = new SyncSuccessEventArgs { EntityId = Guid.NewGuid() };

        // Act
        _bus.PublishSyncSuccess(args);

        // Assert
        received.Should().NotBeNull();
        received!.EntityId.Should().Be(args.EntityId);
    }

    #endregion

    #region SyncFailed Tests

    [Fact]
    public void PublishSyncFailure_RaisesEvent()
    {
        // Arrange
        SyncFailureEventArgs? received = null;
        _bus.SyncFailed += (_, e) => received = e;

        var args = new SyncFailureEventArgs
        {
            EntityId = Guid.NewGuid(),
            ErrorMessage = "Network error",
            IsClientError = false
        };

        // Act
        _bus.PublishSyncFailure(args);

        // Assert
        received.Should().NotBeNull();
        received!.ErrorMessage.Should().Be("Network error");
        received.IsClientError.Should().BeFalse();
    }

    #endregion

    #region SyncQueued Tests

    [Fact]
    public void PublishSyncQueued_RaisesEvent()
    {
        // Arrange
        SyncQueuedEventArgs? received = null;
        _bus.SyncQueued += (_, e) => received = e;

        var args = new SyncQueuedEventArgs
        {
            EntityId = Guid.NewGuid(),
            Message = "Queued for offline sync"
        };

        // Act
        _bus.PublishSyncQueued(args);

        // Assert
        received.Should().NotBeNull();
        received!.Message.Should().Be("Queued for offline sync");
    }

    #endregion

    #region EntityCreated Tests

    [Fact]
    public void PublishEntityCreated_RaisesEvent()
    {
        // Arrange
        EntityCreatedEventArgs? received = null;
        _bus.EntityCreated += (_, e) => received = e;

        var tempId = Guid.NewGuid();
        var serverId = Guid.NewGuid();
        var args = new EntityCreatedEventArgs
        {
            TempClientId = tempId,
            ServerId = serverId,
            EntityType = "Place"
        };

        // Act
        _bus.PublishEntityCreated(args);

        // Assert
        received.Should().NotBeNull();
        received!.TempClientId.Should().Be(tempId);
        received.ServerId.Should().Be(serverId);
        received.EntityType.Should().Be("Place");
    }

    #endregion

    #region TripsUpdated Tests

    [Fact]
    public void PublishTripsUpdated_RaisesEvent()
    {
        // Arrange
        TripsUpdatedEventArgs? received = null;
        _bus.TripsUpdated += (_, e) => received = e;

        var tripIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var args = new TripsUpdatedEventArgs
        {
            UpdateType = TripsUpdateType.Added,
            AffectedTripIds = tripIds,
            Source = "Sync"
        };

        // Act
        _bus.PublishTripsUpdated(args);

        // Assert
        received.Should().NotBeNull();
        received!.UpdateType.Should().Be(TripsUpdateType.Added);
        received.AffectedTripIds.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TripsUpdateType.FullRefresh)]
    [InlineData(TripsUpdateType.Added)]
    [InlineData(TripsUpdateType.Modified)]
    [InlineData(TripsUpdateType.Deleted)]
    public void PublishTripsUpdated_AllUpdateTypes_Work(TripsUpdateType updateType)
    {
        // Arrange
        TripsUpdatedEventArgs? received = null;
        _bus.TripsUpdated += (_, e) => received = e;

        // Act
        _bus.PublishTripsUpdated(new TripsUpdatedEventArgs { UpdateType = updateType });

        // Assert
        received!.UpdateType.Should().Be(updateType);
    }

    #endregion

    #region TripDataChanged Tests

    [Fact]
    public void PublishTripDataChanged_RaisesEvent()
    {
        // Arrange
        TripDataChangedEventArgs? received = null;
        _bus.TripDataChanged += (_, e) => received = e;

        var tripId = Guid.NewGuid();
        var args = new TripDataChangedEventArgs
        {
            TripId = tripId,
            ChangeType = TripDataChangeType.Places,
            Context = "New place added"
        };

        // Act
        _bus.PublishTripDataChanged(args);

        // Assert
        received.Should().NotBeNull();
        received!.TripId.Should().Be(tripId);
        received.ChangeType.Should().Be(TripDataChangeType.Places);
    }

    [Theory]
    [InlineData(TripDataChangeType.Metadata)]
    [InlineData(TripDataChangeType.Places)]
    [InlineData(TripDataChangeType.Segments)]
    [InlineData(TripDataChangeType.Downloaded)]
    [InlineData(TripDataChangeType.DownloadDeleted)]
    [InlineData(TripDataChangeType.Notes)]
    public void PublishTripDataChanged_AllChangeTypes_Work(TripDataChangeType changeType)
    {
        // Arrange
        TripDataChangedEventArgs? received = null;
        _bus.TripDataChanged += (_, e) => received = e;

        // Act
        _bus.PublishTripDataChanged(new TripDataChangedEventArgs
        {
            TripId = Guid.NewGuid(),
            ChangeType = changeType
        });

        // Assert
        received!.ChangeType.Should().Be(changeType);
    }

    #endregion

    #region ConnectivityChanged Tests

    [Fact]
    public void PublishConnectivityChanged_RaisesEvent()
    {
        // Arrange
        SyncConnectivityEventArgs? received = null;
        _bus.ConnectivityChanged += (_, e) => received = e;

        var args = new SyncConnectivityEventArgs
        {
            IsConnected = false,
            Reason = "Network lost",
            PendingOperations = 5
        };

        // Act
        _bus.PublishConnectivityChanged(args);

        // Assert
        received.Should().NotBeNull();
        received!.IsConnected.Should().BeFalse();
        received.Reason.Should().Be("Network lost");
        received.PendingOperations.Should().Be(5);
    }

    #endregion

    #region Multiple Subscribers Tests

    [Fact]
    public void MultipleSubscribers_AllReceiveEvents()
    {
        // Arrange
        var received1 = false;
        var received2 = false;
        var received3 = false;

        _bus.SyncSucceeded += (_, _) => received1 = true;
        _bus.SyncSucceeded += (_, _) => received2 = true;
        _bus.SyncSucceeded += (_, _) => received3 = true;

        // Act
        _bus.PublishSyncSuccess(new SyncSuccessEventArgs { EntityId = Guid.NewGuid() });

        // Assert
        received1.Should().BeTrue();
        received2.Should().BeTrue();
        received3.Should().BeTrue();
    }

    [Fact]
    public void NoSubscribers_DoesNotThrow()
    {
        // Act & Assert - should not throw even with no subscribers
        var act = () => _bus.PublishSyncSuccess(new SyncSuccessEventArgs { EntityId = Guid.NewGuid() });
        act.Should().NotThrow();
    }

    #endregion
}

#region Local Copy of SyncEventBus (for testing)

/// <summary>
/// Local test copy of SyncEventBus.
/// Omits MainThread dispatch since tests don't have MAUI dispatcher.
/// </summary>
public class SyncEventBus : ISyncEventBus
{
    private readonly ILogger<SyncEventBus> _logger;

    public event EventHandler<SyncSuccessEventArgs>? SyncSucceeded;
    public event EventHandler<SyncFailureEventArgs>? SyncFailed;
    public event EventHandler<SyncQueuedEventArgs>? SyncQueued;
    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;
    public event EventHandler<TripsUpdatedEventArgs>? TripsUpdated;
    public event EventHandler<TripDataChangedEventArgs>? TripDataChanged;
    public event EventHandler<SyncConnectivityEventArgs>? ConnectivityChanged;

    public SyncEventBus(ILogger<SyncEventBus> logger)
    {
        _logger = logger;
    }

    public void PublishSyncSuccess(SyncSuccessEventArgs args) => SyncSucceeded?.Invoke(this, args);
    public void PublishSyncFailure(SyncFailureEventArgs args) => SyncFailed?.Invoke(this, args);
    public void PublishSyncQueued(SyncQueuedEventArgs args) => SyncQueued?.Invoke(this, args);
    public void PublishEntityCreated(EntityCreatedEventArgs args) => EntityCreated?.Invoke(this, args);
    public void PublishTripsUpdated(TripsUpdatedEventArgs args) => TripsUpdated?.Invoke(this, args);
    public void PublishTripDataChanged(TripDataChangedEventArgs args) => TripDataChanged?.Invoke(this, args);
    public void PublishConnectivityChanged(SyncConnectivityEventArgs args) => ConnectivityChanged?.Invoke(this, args);
}

#endregion
