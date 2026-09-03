using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service interface for trip-based navigation using the local routing graph.
/// Provides route calculation, progress tracking, and rerouting.
/// </summary>
/// <remarks>
/// Navigation priority:
/// 1. User-defined segments (from trip data)
/// 2. Direct route (straight line with bearing/distance)
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
    /// Stops the active navigation session without unloading trip data.
    /// </summary>
    void StopNavigation();

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
    /// Calculates a route to a specific place using saved Segment geometry or Direct guidance.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Destination place ID.</param>
    /// <returns>The calculated route or null if no route found.</returns>
    NavigationRoute? CalculateRouteToPlace(double currentLat, double currentLon, string destinationPlaceId,
        bool activate = true);

    /// <summary>
    /// Calculates a route to a specific place using saved Segment geometry or Direct guidance.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Destination place ID.</param>
    /// <returns>The calculated route or null if no route found.</returns>
    /// <remarks>
    /// Navigation priority:
    /// 1. User-defined segments (always preferred)
    /// 2. Direct route (straight-line fallback)
    /// </remarks>
    Task<NavigationRoute?> CalculateRouteToPlaceAsync(
        double currentLat, double currentLon,
        string destinationPlaceId);

    /// <summary>
    /// Calculates a route to arbitrary coordinates (not requiring a loaded trip).
    /// Builds Direct straight-line guidance without contacting a routing provider.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destLat">Destination latitude.</param>
    /// <param name="destLon">Destination longitude.</param>
    /// <param name="destName">Destination name for display.</param>
    /// <param name="profile">Routing profile (foot, car, bike). Default is foot.</param>
    /// <param name="activate">Whether to replace the active navigation route.</param>
    /// <returns>The Direct route.</returns>
    Task<NavigationRoute> CalculateRouteToCoordinatesAsync(
        double currentLat, double currentLon,
        double destLat, double destLon,
        string destName,
        string profile = "foot",
        bool activate = true);

    /// <summary>Installs a route selected by a coordinator.</summary>
    void ActivateRoute(NavigationRoute route);

    /// <summary>
    /// Calculates a route to the next place in sequence.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <returns>The calculated route or null if no next place.</returns>
    NavigationRoute? CalculateRouteToNextPlace(double currentLat, double currentLon, bool activate = true);

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
