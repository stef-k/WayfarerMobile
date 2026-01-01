using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of ITripNavigationService for testing.
/// Provides configurable navigation state and route responses.
/// </summary>
public class MockTripNavigationService : ITripNavigationService
{
    private TripDetails? _loadedTrip;
    private NavigationRoute? _activeRoute;
    private NavigationRoute? _nextRouteToReturn;
    private TripNavigationState _currentState = new();
    private readonly List<TripPlace> _places = new();

    /// <inheritdoc/>
    public event EventHandler<TripNavigationState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<string>? Rerouted;

    /// <inheritdoc/>
    public event EventHandler<string>? InstructionAnnounced;

    /// <inheritdoc/>
    public bool IsTripLoaded => _loadedTrip != null;

    /// <inheritdoc/>
    public Guid? CurrentTripId => _loadedTrip?.Id;

    /// <inheritdoc/>
    public NavigationRoute? ActiveRoute => _activeRoute;

    /// <summary>
    /// Gets the loaded trip, if any.
    /// </summary>
    public TripDetails? LoadedTrip => _loadedTrip;

    /// <summary>
    /// Gets the count of LoadTrip calls.
    /// </summary>
    public int LoadTripCallCount { get; private set; }

    /// <summary>
    /// Gets the count of UnloadTrip calls.
    /// </summary>
    public int UnloadTripCallCount { get; private set; }

    /// <summary>
    /// Sets the route to return from route calculation methods.
    /// </summary>
    public void SetNextRouteToReturn(NavigationRoute? route) => _nextRouteToReturn = route;

    /// <summary>
    /// Sets the current navigation state.
    /// </summary>
    public void SetCurrentState(TripNavigationState state) => _currentState = state;

    /// <summary>
    /// Sets the active route.
    /// </summary>
    public void SetActiveRoute(NavigationRoute? route) => _activeRoute = route;

    /// <summary>
    /// Sets the places to return from GetTripPlaces.
    /// </summary>
    public void SetPlaces(IEnumerable<TripPlace> places)
    {
        _places.Clear();
        _places.AddRange(places);
    }

    /// <summary>
    /// Raises the StateChanged event.
    /// </summary>
    public void RaiseStateChanged(TripNavigationState state)
    {
        _currentState = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Raises the Rerouted event.
    /// </summary>
    public void RaiseRerouted(string reason) => Rerouted?.Invoke(this, reason);

    /// <summary>
    /// Raises the InstructionAnnounced event.
    /// </summary>
    public void RaiseInstructionAnnounced(string instruction) =>
        InstructionAnnounced?.Invoke(this, instruction);

    /// <inheritdoc/>
    public bool LoadTrip(TripDetails trip)
    {
        LoadTripCallCount++;
        _loadedTrip = trip;
        _places.Clear();
        _places.AddRange(trip.Regions.SelectMany(r => r.Places));
        return true;
    }

    /// <inheritdoc/>
    public void UnloadTrip()
    {
        UnloadTripCallCount++;
        _loadedTrip = null;
        _activeRoute = null;
        _places.Clear();
    }

    /// <inheritdoc/>
    public NavigationRoute? CalculateRouteToPlace(double currentLat, double currentLon,
        string destinationPlaceId)
    {
        _activeRoute = _nextRouteToReturn;
        return _activeRoute;
    }

    /// <inheritdoc/>
    public Task<NavigationRoute?> CalculateRouteToPlaceAsync(double currentLat, double currentLon,
        string destinationPlaceId, bool fetchFromOsrm = true)
    {
        _activeRoute = _nextRouteToReturn;
        return Task.FromResult(_activeRoute);
    }

    /// <inheritdoc/>
    public Task<NavigationRoute> CalculateRouteToCoordinatesAsync(double currentLat, double currentLon,
        double destLat, double destLon, string destName, string profile = "foot")
    {
        var route = _nextRouteToReturn ?? new NavigationRoute
        {
            DestinationName = destName,
            TotalDistanceMeters = 1000,
            EstimatedDuration = TimeSpan.FromSeconds(600),
            IsDirectRoute = true
        };
        _activeRoute = route;
        return Task.FromResult(route);
    }

    /// <inheritdoc/>
    public NavigationRoute? CalculateRouteToNextPlace(double currentLat, double currentLon)
    {
        _activeRoute = _nextRouteToReturn;
        return _activeRoute;
    }

    /// <inheritdoc/>
    public TripNavigationState UpdateLocation(double currentLat, double currentLon)
    {
        return _currentState;
    }

    /// <inheritdoc/>
    public IEnumerable<TripPlace> GetTripPlaces() => _places;

    /// <summary>
    /// Resets the mock state.
    /// </summary>
    public void Reset()
    {
        _loadedTrip = null;
        _activeRoute = null;
        _nextRouteToReturn = null;
        _currentState = new TripNavigationState();
        _places.Clear();
        LoadTripCallCount = 0;
        UnloadTripCallCount = 0;
    }
}
