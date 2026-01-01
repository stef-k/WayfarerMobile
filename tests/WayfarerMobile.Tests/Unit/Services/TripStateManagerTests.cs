using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TripStateManager.
/// Tests thread-safe state management and event firing for current trip changes.
/// </summary>
/// <remarks>
/// TripStateManager replaces the static MainViewModel.CurrentLoadedTripId pattern
/// with a proper DI-injectable, testable service. This is part of Phase 0 infrastructure
/// for the refactoring effort (Issue #93).
///
/// Note: This test file includes a local copy of TripStateManager since the main
/// WayfarerMobile project targets MAUI platforms (android/ios) which cannot be directly
/// referenced from a pure .NET test project.
/// </remarks>
public class TripStateManagerTests
{
    #region Test Setup

    private readonly TripStateManager _service;
    private readonly ILogger<TripStateManager> _logger;

    public TripStateManagerTests()
    {
        _logger = NullLogger<TripStateManager>.Instance;
        _service = new TripStateManager(_logger);
    }

    #endregion

    #region Initial State Tests

    [Fact]
    public void Constructor_InitialState_HasNoLoadedTrip()
    {
        // Assert
        _service.HasLoadedTrip.Should().BeFalse("because no trip has been loaded");
        _service.CurrentLoadedTripId.Should().BeNull("because no trip has been loaded");
        _service.CurrentLoadedTripName.Should().BeNull("because no trip has been loaded");
    }

    #endregion

    #region SetCurrentTrip Tests

    [Fact]
    public void SetCurrentTrip_WithValidId_SetsCurrentTrip()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        var tripName = "Test Trip";

        // Act
        _service.SetCurrentTrip(tripId, tripName);

        // Assert
        _service.HasLoadedTrip.Should().BeTrue();
        _service.CurrentLoadedTripId.Should().Be(tripId);
        _service.CurrentLoadedTripName.Should().Be(tripName);
    }

    [Fact]
    public void SetCurrentTrip_WithNull_ClearsCurrentTrip()
    {
        // Arrange - first load a trip
        var tripId = Guid.NewGuid();
        _service.SetCurrentTrip(tripId, "Test Trip");

        // Act
        _service.SetCurrentTrip(null, null);

        // Assert
        _service.HasLoadedTrip.Should().BeFalse();
        _service.CurrentLoadedTripId.Should().BeNull();
        _service.CurrentLoadedTripName.Should().BeNull();
    }

    [Fact]
    public void SetCurrentTrip_SameId_DoesNotRaiseEvent()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        _service.SetCurrentTrip(tripId, "Test Trip");

        var eventRaised = false;
        _service.CurrentTripChanged += (_, _) => eventRaised = true;

        // Act - set same ID again
        _service.SetCurrentTrip(tripId, "Test Trip");

        // Assert
        eventRaised.Should().BeFalse("because the trip ID did not change");
    }

    [Fact]
    public void SetCurrentTrip_SameIdDifferentName_UpdatesNameWithoutEvent()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        _service.SetCurrentTrip(tripId, "Original Name");

        var eventRaised = false;
        _service.CurrentTripChanged += (_, _) => eventRaised = true;

        // Act - set same ID with different name
        _service.SetCurrentTrip(tripId, "New Name");

        // Assert
        eventRaised.Should().BeFalse("because the trip ID did not change");
        _service.CurrentLoadedTripName.Should().Be("New Name", "because name should still be updated");
    }

    [Fact]
    public void SetCurrentTrip_DifferentId_RaisesEvent()
    {
        // Arrange
        var tripId1 = Guid.NewGuid();
        var tripId2 = Guid.NewGuid();
        _service.SetCurrentTrip(tripId1, "Trip 1");

        TripChangedEventArgs? eventArgs = null;
        _service.CurrentTripChanged += (_, args) => eventArgs = args;

        // Act
        _service.SetCurrentTrip(tripId2, "Trip 2");

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.PreviousTripId.Should().Be(tripId1);
        eventArgs.PreviousTripName.Should().Be("Trip 1");
        eventArgs.NewTripId.Should().Be(tripId2);
        eventArgs.NewTripName.Should().Be("Trip 2");
        eventArgs.WasSwitched.Should().BeTrue();
    }

    #endregion

    #region ClearCurrentTrip Tests

    [Fact]
    public void ClearCurrentTrip_WhenTripLoaded_ClearsTrip()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        _service.SetCurrentTrip(tripId, "Test Trip");

        // Act
        _service.ClearCurrentTrip();

        // Assert
        _service.HasLoadedTrip.Should().BeFalse();
        _service.CurrentLoadedTripId.Should().BeNull();
    }

    [Fact]
    public void ClearCurrentTrip_WhenTripLoaded_RaisesEventWithWasCleared()
    {
        // Arrange
        var tripId = Guid.NewGuid();
        _service.SetCurrentTrip(tripId, "Test Trip");

        TripChangedEventArgs? eventArgs = null;
        _service.CurrentTripChanged += (_, args) => eventArgs = args;

        // Act
        _service.ClearCurrentTrip();

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.WasCleared.Should().BeTrue();
        eventArgs.HasTrip.Should().BeFalse();
        eventArgs.PreviousTripId.Should().Be(tripId);
        eventArgs.NewTripId.Should().BeNull();
    }

    [Fact]
    public void ClearCurrentTrip_WhenNoTrip_DoesNotRaiseEvent()
    {
        // Arrange - no trip loaded
        var eventRaised = false;
        _service.CurrentTripChanged += (_, _) => eventRaised = true;

        // Act
        _service.ClearCurrentTrip();

        // Assert
        eventRaised.Should().BeFalse("because there was no trip to clear");
    }

    #endregion

    #region TripChangedEventArgs Tests

    [Fact]
    public void TripChangedEventArgs_WasLoaded_WhenFirstTripLoaded()
    {
        // Arrange
        TripChangedEventArgs? eventArgs = null;
        _service.CurrentTripChanged += (_, args) => eventArgs = args;

        // Act
        _service.SetCurrentTrip(Guid.NewGuid(), "New Trip");

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.WasLoaded.Should().BeTrue();
        eventArgs.WasCleared.Should().BeFalse();
        eventArgs.WasSwitched.Should().BeFalse();
    }

    [Fact]
    public void TripChangedEventArgs_WasSwitched_WhenChangingTrips()
    {
        // Arrange
        _service.SetCurrentTrip(Guid.NewGuid(), "Trip 1");

        TripChangedEventArgs? eventArgs = null;
        _service.CurrentTripChanged += (_, args) => eventArgs = args;

        // Act
        _service.SetCurrentTrip(Guid.NewGuid(), "Trip 2");

        // Assert
        eventArgs.Should().NotBeNull();
        eventArgs!.WasSwitched.Should().BeTrue();
        eventArgs.WasLoaded.Should().BeFalse();
        eventArgs.WasCleared.Should().BeFalse();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task SetCurrentTrip_ConcurrentCalls_MaintainsConsistentState()
    {
        // Arrange
        var tripIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();
        var tasks = new List<Task>();

        // Act - fire many concurrent updates
        foreach (var tripId in tripIds)
        {
            tasks.Add(Task.Run(() => _service.SetCurrentTrip(tripId, $"Trip {tripId}")));
        }

        await Task.WhenAll(tasks);

        // Assert - state should be consistent (one of the trip IDs)
        _service.HasLoadedTrip.Should().BeTrue();
        _service.CurrentLoadedTripId.Should().NotBeNull();
        tripIds.Should().Contain(_service.CurrentLoadedTripId!.Value);
    }

    [Fact]
    public async Task Properties_ConcurrentReads_DoNotThrow()
    {
        // Arrange
        _service.SetCurrentTrip(Guid.NewGuid(), "Test Trip");
        var tasks = new List<Task>();

        // Act - fire many concurrent reads while also writing
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                _ = _service.CurrentLoadedTripId;
                _ = _service.CurrentLoadedTripName;
                _ = _service.HasLoadedTrip;
            }));
            tasks.Add(Task.Run(() => _service.SetCurrentTrip(Guid.NewGuid(), $"Trip {i}")));
        }

        // Assert - should complete without throwing
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    #endregion
}

#region Local Copy of TripStateManager (for testing)

/// <summary>
/// Local test copy of TripStateManager.
/// The real implementation in WayfarerMobile.Services cannot be referenced because
/// it's in a MAUI platform-specific project. This copy omits the MainThread.BeginInvokeOnMainThread
/// call since tests don't have a MAUI dispatcher.
/// </summary>
public class TripStateManager : ITripStateManager
{
    private readonly ILogger<TripStateManager> _logger;
    private readonly object _lock = new();

    private Guid? _currentTripId;
    private string? _currentTripName;

    public Guid? CurrentLoadedTripId
    {
        get { lock (_lock) return _currentTripId; }
    }

    public string? CurrentLoadedTripName
    {
        get { lock (_lock) return _currentTripName; }
    }

    public bool HasLoadedTrip
    {
        get { lock (_lock) return _currentTripId.HasValue; }
    }

    public event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

    public TripStateManager(ILogger<TripStateManager> logger)
    {
        _logger = logger;
    }

    public void SetCurrentTrip(Guid? tripId, string? tripName = null)
    {
        TripChangedEventArgs? eventArgs = null;

        lock (_lock)
        {
            if (_currentTripId == tripId)
            {
                if (tripName != null && _currentTripName != tripName)
                {
                    _currentTripName = tripName;
                }
                return;
            }

            var previousId = _currentTripId;
            var previousName = _currentTripName;

            _currentTripId = tripId;
            _currentTripName = tripName;

            eventArgs = new TripChangedEventArgs(previousId, previousName, tripId, tripName);
        }

        // In production, this is dispatched to MainThread
        // In tests, we invoke directly since there's no MAUI dispatcher
        if (eventArgs != null)
        {
            try
            {
                CurrentTripChanged?.Invoke(this, eventArgs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CurrentTripChanged event handler");
            }
        }
    }

    public void ClearCurrentTrip()
    {
        SetCurrentTrip(null, null);
    }
}

#endregion
