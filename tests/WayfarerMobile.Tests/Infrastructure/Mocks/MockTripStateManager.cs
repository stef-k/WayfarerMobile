using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of ITripStateManager for testing.
/// Captures all calls and provides configurable behavior.
/// </summary>
public class MockTripStateManager : ITripStateManager
{
    private Guid? _currentTripId;
    private string? _currentTripName;
    private TripDetails? _loadedTrip;
    private readonly List<(Guid? TripId, string? TripName)> _setCurrentTripCalls = new();
    private readonly List<TripDetails?> _setLoadedTripCalls = new();
    private int _clearCallCount;

    /// <inheritdoc/>
    public Guid? CurrentLoadedTripId => _currentTripId;

    /// <inheritdoc/>
    public string? CurrentLoadedTripName => _currentTripName;

    /// <inheritdoc/>
    public bool HasLoadedTrip => _currentTripId.HasValue;

    /// <inheritdoc/>
    public TripDetails? LoadedTrip => _loadedTrip;

    /// <inheritdoc/>
    public event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

    /// <inheritdoc/>
    public event EventHandler<LoadedTripChangedEventArgs>? LoadedTripChanged;

    /// <summary>
    /// Gets all calls to SetCurrentTrip.
    /// </summary>
    public IReadOnlyList<(Guid? TripId, string? TripName)> SetCurrentTripCalls => _setCurrentTripCalls;

    /// <summary>
    /// Gets all calls to SetLoadedTrip.
    /// </summary>
    public IReadOnlyList<TripDetails?> SetLoadedTripCalls => _setLoadedTripCalls;

    /// <summary>
    /// Gets the number of times ClearCurrentTrip was called.
    /// </summary>
    public int ClearCallCount => _clearCallCount;

    /// <inheritdoc/>
    public void SetCurrentTrip(Guid? tripId, string? tripName = null)
    {
        _setCurrentTripCalls.Add((tripId, tripName));

        var previousId = _currentTripId;
        var previousName = _currentTripName;

        _currentTripId = tripId;
        _currentTripName = tripName;

        CurrentTripChanged?.Invoke(this, new TripChangedEventArgs(
            previousId, previousName, tripId, tripName));
    }

    /// <inheritdoc/>
    public void SetLoadedTrip(TripDetails? trip)
    {
        _setLoadedTripCalls.Add(trip);

        var previousTrip = _loadedTrip;
        var previousId = _currentTripId;
        var previousName = _currentTripName;

        _loadedTrip = trip;
        _currentTripId = trip?.Id;
        _currentTripName = trip?.Name;

        // Fire events
        if (previousId != _currentTripId)
        {
            CurrentTripChanged?.Invoke(this, new TripChangedEventArgs(
                previousId, previousName, _currentTripId, _currentTripName));
        }

        if (previousTrip?.Id != trip?.Id)
        {
            LoadedTripChanged?.Invoke(this, new LoadedTripChangedEventArgs(previousTrip, trip));
        }
    }

    /// <inheritdoc/>
    public void ClearCurrentTrip()
    {
        _clearCallCount++;
        SetLoadedTrip(null);
    }

    /// <summary>
    /// Resets the mock state.
    /// </summary>
    public void Reset()
    {
        _currentTripId = null;
        _currentTripName = null;
        _loadedTrip = null;
        _setCurrentTripCalls.Clear();
        _setLoadedTripCalls.Clear();
        _clearCallCount = 0;
    }
}
