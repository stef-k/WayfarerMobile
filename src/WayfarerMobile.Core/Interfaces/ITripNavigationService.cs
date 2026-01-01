using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service interface for trip-based navigation using the local routing graph.
/// Provides route calculation, progress tracking, and rerouting.
/// </summary>
/// <remarks>
/// Navigation priority:
/// 1. User-defined segments (from trip data)
/// 2. Cached OSRM route (if still valid - same destination, within 50m of origin, less than 5 min old)
/// 3. Fetched routes (from OSRM when online)
/// 4. Direct route (straight line with bearing/distance)
/// </remarks>
public interface ITripNavigationService
{
    /// <summary>
    /// Event raised when navigation state changes.
    /// </summary>
    event EventHandler<TripNavigationState>? StateChanged;

    /// <summary>
    /// Event raised when rerouting occurs.
    /// </summary>
    event EventHandler<string>? Rerouted;

    /// <summary>
    /// Event raised when a navigation instruction should be announced.
    /// </summary>
    event EventHandler<string>? InstructionAnnounced;

    /// <summary>
    /// Gets whether a trip is loaded for navigation.
    /// </summary>
    bool IsTripLoaded { get; }

    /// <summary>
    /// Gets the ID of the currently loaded trip, or null if no trip is loaded.
    /// </summary>
    Guid? CurrentTripId { get; }

    /// <summary>
    /// Gets the current navigation route.
    /// </summary>
    NavigationRoute? ActiveRoute { get; }

    /// <summary>
    /// Loads a trip for navigation, building the routing graph.
    /// </summary>
    /// <param name="trip">The trip details.</param>
    /// <returns>True if the trip was loaded successfully.</returns>
    bool LoadTrip(TripDetails trip);

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    void UnloadTrip();

    /// <summary>
    /// Calculates a route to a specific place (synchronous, no OSRM fetch).
    /// Use <see cref="CalculateRouteToPlaceAsync"/> for full routing with OSRM support.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Destination place ID.</param>
    /// <returns>The calculated route or null if no route found.</returns>
    NavigationRoute? CalculateRouteToPlace(double currentLat, double currentLon, string destinationPlaceId);

    /// <summary>
    /// Calculates a route to a specific place with OSRM fetching support.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Destination place ID.</param>
    /// <param name="fetchFromOsrm">Whether to fetch route from OSRM if no segment exists.</param>
    /// <returns>The calculated route or null if no route found.</returns>
    /// <remarks>
    /// Navigation priority:
    /// 1. User-defined segments (always preferred)
    /// 2. Cached OSRM route (if still valid)
    /// 3. OSRM-fetched routes (if online and fetchFromOsrm is true)
    /// 4. Direct route (straight line fallback)
    /// </remarks>
    Task<NavigationRoute?> CalculateRouteToPlaceAsync(
        double currentLat, double currentLon,
        string destinationPlaceId,
        bool fetchFromOsrm = true);

    /// <summary>
    /// Calculates a route to arbitrary coordinates (not requiring a loaded trip).
    /// Uses OSRM for routing when online, falls back to straight line when offline.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destLat">Destination latitude.</param>
    /// <param name="destLon">Destination longitude.</param>
    /// <param name="destName">Destination name for display.</param>
    /// <param name="profile">Routing profile (foot, car, bike). Default is foot.</param>
    /// <returns>The calculated route (OSRM or direct).</returns>
    Task<NavigationRoute> CalculateRouteToCoordinatesAsync(
        double currentLat, double currentLon,
        double destLat, double destLon,
        string destName,
        string profile = "foot");

    /// <summary>
    /// Calculates a route to the next place in sequence.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <returns>The calculated route or null if no next place.</returns>
    NavigationRoute? CalculateRouteToNextPlace(double currentLat, double currentLon);

    /// <summary>
    /// Updates navigation state with current location.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <returns>The updated navigation state.</returns>
    TripNavigationState UpdateLocation(double currentLat, double currentLon);

    /// <summary>
    /// Gets all places in the current trip.
    /// </summary>
    IEnumerable<TripPlace> GetTripPlaces();
}
