using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Thread-safe implementation of <see cref="ITripStateManager"/>.
/// Manages the currently loaded trip state across the application.
/// Registered as singleton in DI.
/// </summary>
public class TripStateManager : ITripStateManager
{
    private readonly ILogger<TripStateManager> _logger;
    private readonly object _lock = new();

    private Guid? _currentTripId;
    private string? _currentTripName;
    private TripDetails? _loadedTrip;

    /// <inheritdoc/>
    public Guid? CurrentLoadedTripId
    {
        get { lock (_lock) return _currentTripId; }
    }

    /// <inheritdoc/>
    public string? CurrentLoadedTripName
    {
        get { lock (_lock) return _currentTripName; }
    }

    /// <inheritdoc/>
    public bool HasLoadedTrip
    {
        get { lock (_lock) return _currentTripId.HasValue; }
    }

    /// <inheritdoc/>
    public TripDetails? LoadedTrip
    {
        get { lock (_lock) return _loadedTrip; }
    }

    /// <inheritdoc/>
    public event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

    /// <inheritdoc/>
    public event EventHandler<LoadedTripChangedEventArgs>? LoadedTripChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="TripStateManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public TripStateManager(ILogger<TripStateManager> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void SetCurrentTrip(Guid? tripId, string? tripName = null)
    {
        TripChangedEventArgs? eventArgs = null;

        lock (_lock)
        {
            // Skip if no change
            if (_currentTripId == tripId)
            {
                // Update name if provided and different
                if (tripName != null && _currentTripName != tripName)
                {
                    _currentTripName = tripName;
                    _logger.LogDebug("TripStateManager: Updated trip name to {TripName}", tripName);
                }
                return;
            }

            // Capture previous state
            var previousId = _currentTripId;
            var previousName = _currentTripName;

            // Update state
            _currentTripId = tripId;
            _currentTripName = tripName;

            _logger.LogDebug(
                "TripStateManager: Trip changed from {PreviousTripId} to {NewTripId} ({TripName})",
                previousId,
                tripId,
                tripName ?? "(no name)");

            // Create event args while still holding lock
            eventArgs = new TripChangedEventArgs(previousId, previousName, tripId, tripName);
        }

        // Fire event outside lock to prevent deadlocks
        if (eventArgs != null)
        {
            RaiseCurrentTripChanged(eventArgs);
        }
    }

    /// <inheritdoc/>
    public void ClearCurrentTrip()
    {
        SetLoadedTrip(null);
    }

    /// <inheritdoc/>
    public void SetLoadedTrip(TripDetails? trip)
    {
        LoadedTripChangedEventArgs? tripEventArgs = null;
        TripChangedEventArgs? idEventArgs = null;

        lock (_lock)
        {
            // Capture previous state
            var previousTrip = _loadedTrip;
            var previousId = _currentTripId;
            var previousName = _currentTripName;

            // Determine new ID and name from trip
            var newId = trip?.Id;
            var newName = trip?.Name;

            // Check if trip actually changed
            var tripChanged = !ReferenceEquals(previousTrip, trip) &&
                              (previousTrip?.Id != trip?.Id);

            var idChanged = previousId != newId;

            if (!tripChanged && !idChanged)
            {
                return;
            }

            // Update state
            _loadedTrip = trip;
            _currentTripId = newId;
            _currentTripName = newName;

            _logger.LogDebug(
                "TripStateManager: Trip changed from {PreviousTripId} to {NewTripId} ({TripName})",
                previousTrip?.Id,
                trip?.Id,
                trip?.Name ?? "(null)");

            // Create event args while still holding lock
            if (tripChanged)
            {
                tripEventArgs = new LoadedTripChangedEventArgs(previousTrip, trip);
            }

            if (idChanged)
            {
                idEventArgs = new TripChangedEventArgs(previousId, previousName, newId, newName);
            }
        }

        // Fire events outside lock to prevent deadlocks
        if (idEventArgs != null)
        {
            RaiseCurrentTripChanged(idEventArgs);
        }

        if (tripEventArgs != null)
        {
            RaiseLoadedTripChanged(tripEventArgs);
        }
    }

    /// <summary>
    /// Raises the CurrentTripChanged event on the main thread.
    /// </summary>
    private void RaiseCurrentTripChanged(TripChangedEventArgs args)
    {
        var handler = CurrentTripChanged;
        if (handler == null)
            return;

        // Always dispatch to main thread for UI safety
        if (MainThread.IsMainThread)
        {
            handler.Invoke(this, args);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    handler.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CurrentTripChanged event handler");
                }
            });
        }
    }

    /// <summary>
    /// Raises the LoadedTripChanged event on the main thread.
    /// </summary>
    private void RaiseLoadedTripChanged(LoadedTripChangedEventArgs args)
    {
        var handler = LoadedTripChanged;
        if (handler == null)
            return;

        // Always dispatch to main thread for UI safety
        if (MainThread.IsMainThread)
        {
            handler.Invoke(this, args);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    handler.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in LoadedTripChanged event handler");
                }
            });
        }
    }
}
