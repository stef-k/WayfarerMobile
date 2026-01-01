using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of ITripStateManager for testing.
/// Captures all calls and provides configurable behavior.
/// </summary>
public class MockTripStateManager : ITripStateManager
{
    private Guid? _currentTripId;
    private string? _currentTripName;
    private readonly List<(Guid? TripId, string? TripName)> _setCurrentTripCalls = new();
    private int _clearCallCount;

    /// <inheritdoc/>
    public Guid? CurrentLoadedTripId => _currentTripId;

    /// <inheritdoc/>
    public string? CurrentLoadedTripName => _currentTripName;

    /// <inheritdoc/>
    public bool HasLoadedTrip => _currentTripId.HasValue;

    /// <inheritdoc/>
    public event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

    /// <summary>
    /// Gets all calls to SetCurrentTrip.
    /// </summary>
    public IReadOnlyList<(Guid? TripId, string? TripName)> SetCurrentTripCalls => _setCurrentTripCalls;

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
    public void ClearCurrentTrip()
    {
        _clearCallCount++;
        SetCurrentTrip(null, null);
    }

    /// <summary>
    /// Resets the mock state.
    /// </summary>
    public void Reset()
    {
        _currentTripId = null;
        _currentTripName = null;
        _setCurrentTripCalls.Clear();
        _clearCallCount = 0;
    }
}
