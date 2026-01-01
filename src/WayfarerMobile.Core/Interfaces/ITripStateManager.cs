namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Manages the currently loaded trip state across the application.
/// Replaces static MainViewModel.CurrentLoadedTripId for proper DI and testability.
/// Thread-safe implementation required.
/// </summary>
public interface ITripStateManager
{
    /// <summary>
    /// Gets the ID of the currently loaded trip, or null if no trip is loaded.
    /// </summary>
    Guid? CurrentLoadedTripId { get; }

    /// <summary>
    /// Gets the name of the currently loaded trip, or null if no trip is loaded.
    /// </summary>
    string? CurrentLoadedTripName { get; }

    /// <summary>
    /// Gets whether a trip is currently loaded.
    /// </summary>
    bool HasLoadedTrip { get; }

    /// <summary>
    /// Event raised when the current trip changes.
    /// Always raised on the main thread.
    /// </summary>
    event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

    /// <summary>
    /// Sets the currently loaded trip.
    /// </summary>
    /// <param name="tripId">The trip ID to set as current.</param>
    /// <param name="tripName">Optional trip name for display purposes.</param>
    void SetCurrentTrip(Guid? tripId, string? tripName = null);

    /// <summary>
    /// Clears the currently loaded trip.
    /// Equivalent to SetCurrentTrip(null, null).
    /// </summary>
    void ClearCurrentTrip();
}

/// <summary>
/// Event arguments for trip state changes.
/// </summary>
public class TripChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous trip ID, or null if no trip was loaded.
    /// </summary>
    public Guid? PreviousTripId { get; }

    /// <summary>
    /// Gets the previous trip name, or null if no trip was loaded.
    /// </summary>
    public string? PreviousTripName { get; }

    /// <summary>
    /// Gets the new trip ID, or null if trip was cleared.
    /// </summary>
    public Guid? NewTripId { get; }

    /// <summary>
    /// Gets the new trip name, or null if trip was cleared.
    /// </summary>
    public string? NewTripName { get; }

    /// <summary>
    /// Gets whether a trip is now loaded.
    /// </summary>
    public bool HasTrip => NewTripId.HasValue;

    /// <summary>
    /// Gets whether the trip was cleared (had a trip, now doesn't).
    /// </summary>
    public bool WasCleared => PreviousTripId.HasValue && !NewTripId.HasValue;

    /// <summary>
    /// Gets whether a new trip was loaded (didn't have one, now does).
    /// </summary>
    public bool WasLoaded => !PreviousTripId.HasValue && NewTripId.HasValue;

    /// <summary>
    /// Gets whether the trip was switched (had a different trip before).
    /// </summary>
    public bool WasSwitched => PreviousTripId.HasValue && NewTripId.HasValue && PreviousTripId != NewTripId;

    /// <summary>
    /// Initializes a new instance of the <see cref="TripChangedEventArgs"/> class.
    /// </summary>
    /// <param name="previousTripId">The previous trip ID.</param>
    /// <param name="previousTripName">The previous trip name.</param>
    /// <param name="newTripId">The new trip ID.</param>
    /// <param name="newTripName">The new trip name.</param>
    public TripChangedEventArgs(
        Guid? previousTripId,
        string? previousTripName,
        Guid? newTripId,
        string? newTripName)
    {
        PreviousTripId = previousTripId;
        PreviousTripName = previousTripName;
        NewTripId = newTripId;
        NewTripName = newTripName;
    }
}
