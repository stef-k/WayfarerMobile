using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

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
    public event EventHandler<TripChangedEventArgs>? CurrentTripChanged;

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
        SetCurrentTrip(null, null);
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
}
