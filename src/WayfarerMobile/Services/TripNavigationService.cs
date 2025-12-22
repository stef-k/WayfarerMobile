using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Navigation;
using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for trip-based navigation using the local routing graph.
/// Provides route calculation, progress tracking, and rerouting.
/// </summary>
/// <remarks>
/// Navigation priority:
/// 1. User-defined segments (from trip data)
/// 2. Cached OSRM route (if still valid - same destination, within 50m of origin, &lt; 5 min old)
/// 3. Fetched routes (from OSRM when online)
/// 4. Direct route (straight line with bearing/distance)
/// </remarks>
public class TripNavigationService
{
    private readonly ILogger<TripNavigationService> _logger;
    private readonly OsrmRoutingService _osrmService;
    private readonly RouteCacheService _routeCacheService;
    private readonly INavigationAudioService _audioService;

    private TripNavigationGraph? _currentGraph;
    private TripDetails? _currentTrip;
    private NavigationRoute? _activeRoute;
    private string? _destinationPlaceId;

    /// <summary>
    /// Event raised when navigation state changes.
    /// </summary>
    public event EventHandler<TripNavigationState>? StateChanged;

    /// <summary>
    /// Event raised when rerouting occurs.
    /// </summary>
    public event EventHandler<string>? Rerouted;

    /// <summary>
    /// Event raised when a navigation instruction should be announced.
    /// </summary>
    public event EventHandler<string>? InstructionAnnounced;

    /// <summary>
    /// Gets whether a trip is loaded for navigation.
    /// </summary>
    public bool IsTripLoaded => _currentGraph != null;

    /// <summary>
    /// Gets the ID of the currently loaded trip, or null if no trip is loaded.
    /// </summary>
    public Guid? CurrentTripId => _currentTrip?.Id;

    // Turn announcement tracking
    private string? _lastAnnouncedWaypoint;
    private DateTime _lastAnnouncementTime = DateTime.MinValue;
    private const double TurnAnnouncementDistanceMeters = 100; // Announce when within 100m of waypoint
    private const int MinAnnouncementIntervalSeconds = 15;

    /// <summary>
    /// Gets the current navigation route.
    /// </summary>
    public NavigationRoute? ActiveRoute => _activeRoute;

    /// <summary>
    /// Creates a new instance of TripNavigationService.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="osrmService">The OSRM routing service.</param>
    /// <param name="routeCacheService">The route cache service.</param>
    /// <param name="audioService">The navigation audio service.</param>
    public TripNavigationService(
        ILogger<TripNavigationService> logger,
        OsrmRoutingService osrmService,
        RouteCacheService routeCacheService,
        INavigationAudioService audioService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(osrmService);
        ArgumentNullException.ThrowIfNull(routeCacheService);
        ArgumentNullException.ThrowIfNull(audioService);

        _logger = logger;
        _osrmService = osrmService;
        _routeCacheService = routeCacheService;
        _audioService = audioService;
    }

    /// <summary>
    /// Loads a trip for navigation, building the routing graph.
    /// </summary>
    /// <param name="trip">The trip details.</param>
    /// <returns>True if the trip was loaded successfully.</returns>
    public bool LoadTrip(TripDetails trip)
    {
        try
        {
            _currentTrip = trip;
            _currentGraph = BuildNavigationGraph(trip);

            _logger.LogInformation(
                "Loaded trip '{TripName}' for navigation: {NodeCount} places, {EdgeCount} edges",
                trip.Name, _currentGraph.Nodes.Count, _currentGraph.Edges.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trip for navigation");
            return false;
        }
    }

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    public void UnloadTrip()
    {
        _currentGraph = null;
        _currentTrip = null;
        _activeRoute = null;
        _destinationPlaceId = null;
    }

    /// <summary>
    /// Calculates a route to a specific place (synchronous, no OSRM fetch).
    /// Use <see cref="CalculateRouteToPlaceAsync"/> for full routing with OSRM support.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Destination place ID.</param>
    /// <returns>The calculated route or null if no route found.</returns>
    public NavigationRoute? CalculateRouteToPlace(double currentLat, double currentLon, string destinationPlaceId)
    {
        if (_currentGraph == null)
        {
            _logger.LogWarning("No trip loaded for navigation");
            return null;
        }

        if (!_currentGraph.Nodes.TryGetValue(destinationPlaceId, out var destination))
        {
            _logger.LogWarning("Destination place {PlaceId} not found in trip", destinationPlaceId);
            return null;
        }

        _destinationPlaceId = destinationPlaceId;

        // Priority 1: Check for user-defined segment route
        if (_currentGraph.IsWithinSegmentRoutingRange(currentLat, currentLon))
        {
            var nearestNode = _currentGraph.FindNearestNode(currentLat, currentLon);
            if (nearestNode != null)
            {
                var path = _currentGraph.FindPath(nearestNode.Id, destinationPlaceId);
                if (path.Count > 0)
                {
                    _activeRoute = BuildRouteFromPath(path, currentLat, currentLon);
                    _logger.LogDebug("Using segment route with {WaypointCount} waypoints", _activeRoute?.Waypoints.Count);
                    return _activeRoute;
                }
            }
        }

        // Priority 3: Direct navigation (bearing + distance)
        _activeRoute = BuildDirectRoute(currentLat, currentLon, destination);
        _logger.LogDebug("Using direct route to {Destination}", destination.Name);
        return _activeRoute;
    }

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
    public async Task<NavigationRoute?> CalculateRouteToPlaceAsync(
        double currentLat, double currentLon,
        string destinationPlaceId,
        bool fetchFromOsrm = true)
    {
        if (_currentGraph == null)
        {
            _logger.LogWarning("No trip loaded for navigation");
            return null;
        }

        if (!_currentGraph.Nodes.TryGetValue(destinationPlaceId, out var destination))
        {
            _logger.LogWarning("Destination place {PlaceId} not found in trip", destinationPlaceId);
            return null;
        }

        _destinationPlaceId = destinationPlaceId;

        // Priority 1: Check for user-defined segment route
        if (_currentGraph.IsWithinSegmentRoutingRange(currentLat, currentLon))
        {
            var nearestNode = _currentGraph.FindNearestNode(currentLat, currentLon);
            if (nearestNode != null)
            {
                var path = _currentGraph.FindPath(nearestNode.Id, destinationPlaceId);
                if (path.Count > 0)
                {
                    _activeRoute = BuildRouteFromPath(path, currentLat, currentLon);
                    _logger.LogDebug("Using segment route with {WaypointCount} waypoints", _activeRoute?.Waypoints.Count);
                    return _activeRoute;
                }
            }
        }

        // Priority 2: Check for valid cached route
        var cachedRoute = _routeCacheService.GetValidRoute(currentLat, currentLon, destinationPlaceId);
        if (cachedRoute != null)
        {
            _activeRoute = BuildRouteFromCache(cachedRoute, currentLat, currentLon, destination);
            _logger.LogInformation(
                "Using cached route to {Destination}: {Distance:F1}km",
                destination.Name, cachedRoute.DistanceMeters / 1000);
            return _activeRoute;
        }

        // Priority 3: Try OSRM if enabled
        if (fetchFromOsrm)
        {
            var osrmRoute = await _osrmService.GetRouteAsync(
                currentLat, currentLon,
                destination.Latitude, destination.Longitude,
                "foot"); // Default to walking

            if (osrmRoute != null)
            {
                // Cache the fetched route
                _routeCacheService.SaveRoute(new CachedRoute
                {
                    DestinationPlaceId = destinationPlaceId,
                    DestinationName = destination.Name,
                    OriginLatitude = currentLat,
                    OriginLongitude = currentLon,
                    Geometry = osrmRoute.Geometry,
                    DistanceMeters = osrmRoute.DistanceMeters,
                    DurationSeconds = osrmRoute.DurationSeconds,
                    Source = osrmRoute.Source,
                    FetchedAtUtc = DateTime.UtcNow
                });

                _activeRoute = BuildRouteFromOsrm(osrmRoute, currentLat, currentLon, destination);
                _logger.LogInformation(
                    "Using OSRM route to {Destination}: {Distance:F1}km",
                    destination.Name, osrmRoute.DistanceMeters / 1000);
                return _activeRoute;
            }
        }

        // Priority 4: Direct navigation (bearing + distance)
        _activeRoute = BuildDirectRoute(currentLat, currentLon, destination);
        _logger.LogDebug("Using direct route to {Destination}", destination.Name);
        return _activeRoute;
    }

    /// <summary>
    /// Builds a navigation route from cached route data.
    /// </summary>
    private NavigationRoute BuildRouteFromCache(
        CachedRoute cachedRoute,
        double startLat, double startLon,
        NavigationNode destination)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = PolylineDecoder.Decode(cachedRoute.Geometry);

        // Add start
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = startLat,
            Longitude = startLon,
            Name = "Current Location",
            Type = WaypointType.Start
        });

        // Add intermediate route points (skip first and last as they're start/destination)
        foreach (var point in routePoints.Skip(1).Take(routePoints.Count - 2))
        {
            waypoints.Add(new NavigationWaypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                Type = WaypointType.RoutePoint
            });
        }

        // Add destination
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            Name = destination.Name,
            Type = WaypointType.Destination,
            PlaceId = destination.Id
        });

        return new NavigationRoute
        {
            Waypoints = waypoints,
            DestinationName = destination.Name,
            TotalDistanceMeters = cachedRoute.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(cachedRoute.DurationSeconds)
        };
    }

    /// <summary>
    /// Builds a navigation route from OSRM response.
    /// </summary>
    private NavigationRoute BuildRouteFromOsrm(
        OsrmRouteResult osrmRoute,
        double startLat, double startLon,
        NavigationNode destination)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = PolylineDecoder.Decode(osrmRoute.Geometry);

        // Add start
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = startLat,
            Longitude = startLon,
            Name = "Current Location",
            Type = WaypointType.Start
        });

        // Add intermediate route points (skip first and last as they're start/destination)
        foreach (var point in routePoints.Skip(1).Take(routePoints.Count - 2))
        {
            waypoints.Add(new NavigationWaypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                Type = WaypointType.RoutePoint
            });
        }

        // Add destination
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = destination.Latitude,
            Longitude = destination.Longitude,
            Name = destination.Name,
            Type = WaypointType.Destination,
            PlaceId = destination.Id
        });

        return new NavigationRoute
        {
            Waypoints = waypoints,
            DestinationName = destination.Name,
            TotalDistanceMeters = osrmRoute.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(osrmRoute.DurationSeconds)
        };
    }

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
    public async Task<NavigationRoute> CalculateRouteToCoordinatesAsync(
        double currentLat, double currentLon,
        double destLat, double destLon,
        string destName,
        string profile = "foot")
    {
        _logger.LogInformation("Calculating route to {Name} at {Lat},{Lon}", destName, destLat, destLon);

        // Try OSRM first
        try
        {
            var osrmRoute = await _osrmService.GetRouteAsync(
                currentLat, currentLon,
                destLat, destLon,
                profile);

            if (osrmRoute != null)
            {
                var route = BuildRouteFromOsrmCoordinates(osrmRoute, currentLat, currentLon, destLat, destLon, destName);
                _logger.LogInformation("Using OSRM route to {Name}: {Distance:F1}km", destName, osrmRoute.DistanceMeters / 1000);
                _activeRoute = route;
                return route;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OSRM routing failed, falling back to direct route");
        }

        // Fallback to direct route (straight line)
        _logger.LogInformation("Using direct route to {Name}", destName);
        var directRoute = BuildDirectRouteToCoordinates(currentLat, currentLon, destLat, destLon, destName);
        _activeRoute = directRoute;
        return directRoute;
    }

    /// <summary>
    /// Builds a navigation route from OSRM response to coordinates.
    /// </summary>
    private NavigationRoute BuildRouteFromOsrmCoordinates(
        OsrmRouteResult osrmRoute,
        double startLat, double startLon,
        double destLat, double destLon,
        string destName)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = Core.Helpers.PolylineDecoder.Decode(osrmRoute.Geometry);

        // Add start
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = startLat,
            Longitude = startLon,
            Name = "Current Location",
            Type = WaypointType.Start
        });

        // Add intermediate route points (skip first and last as they're start/destination)
        foreach (var point in routePoints.Skip(1).Take(routePoints.Count - 2))
        {
            waypoints.Add(new NavigationWaypoint
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                Type = WaypointType.RoutePoint
            });
        }

        // Add destination
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = destLat,
            Longitude = destLon,
            Name = destName,
            Type = WaypointType.Destination
        });

        // Convert OSRM steps to NavigationSteps
        var steps = osrmRoute.Steps.Select(s => new NavigationStep
        {
            Instruction = s.Instruction,
            DistanceMeters = s.DistanceMeters,
            DurationSeconds = s.DurationSeconds,
            ManeuverType = s.ManeuverType,
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            StreetName = s.StreetName
        }).ToList();

        return new NavigationRoute
        {
            Waypoints = waypoints,
            Steps = steps,
            DestinationName = destName,
            TotalDistanceMeters = osrmRoute.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(osrmRoute.DurationSeconds),
            IsDirectRoute = false
        };
    }

    /// <summary>
    /// Builds a direct route (straight line) to coordinates.
    /// </summary>
    private NavigationRoute BuildDirectRouteToCoordinates(
        double startLat, double startLon,
        double destLat, double destLon,
        string destName)
    {
        var distance = GeoMath.CalculateDistance(startLat, startLon, destLat, destLon);
        var bearing = GeoMath.CalculateBearing(startLat, startLon, destLat, destLon);
        var direction = GetCardinalDirection(bearing);
        var distanceText = FormatDistance(distance);

        // Create instruction for straight-line navigation
        var instruction = string.IsNullOrEmpty(destName) || destName == "Dropped Pin"
            ? $"Head {direction}, {distanceText}"
            : $"Head {direction}, {distanceText} to {destName}";

        var steps = new List<NavigationStep>
        {
            new()
            {
                Instruction = instruction,
                DistanceMeters = distance,
                DurationSeconds = distance / 1.4,
                ManeuverType = "depart",
                Latitude = startLat,
                Longitude = startLon
            },
            new()
            {
                Instruction = "You have arrived",
                DistanceMeters = 0,
                DurationSeconds = 0,
                ManeuverType = "arrive",
                Latitude = destLat,
                Longitude = destLon
            }
        };

        return new NavigationRoute
        {
            Waypoints = new List<NavigationWaypoint>
            {
                new() { Latitude = startLat, Longitude = startLon, Name = "Current Location", Type = WaypointType.Start },
                new() { Latitude = destLat, Longitude = destLon, Name = destName, Type = WaypointType.Destination }
            },
            Steps = steps,
            DestinationName = destName,
            TotalDistanceMeters = distance,
            EstimatedDuration = TimeSpan.FromSeconds(distance / 1.4), // Walking speed ~5km/h
            IsDirectRoute = true,
            InitialBearing = bearing
        };
    }

    /// <summary>
    /// Gets the cardinal direction name from a bearing.
    /// </summary>
    private static string GetCardinalDirection(double bearing)
    {
        // Normalize bearing to 0-360
        bearing = ((bearing % 360) + 360) % 360;

        return bearing switch
        {
            >= 337.5 or < 22.5 => "north",
            >= 22.5 and < 67.5 => "northeast",
            >= 67.5 and < 112.5 => "east",
            >= 112.5 and < 157.5 => "southeast",
            >= 157.5 and < 202.5 => "south",
            >= 202.5 and < 247.5 => "southwest",
            >= 247.5 and < 292.5 => "west",
            _ => "northwest"
        };
    }

    /// <summary>
    /// Formats a distance in meters to a human-readable string.
    /// </summary>
    private static string FormatDistance(double meters)
    {
        if (meters >= 1000)
            return $"{meters / 1000:F1} km";
        return $"{meters:F0} m";
    }

    /// <summary>
    /// Calculates a route to the next place in sequence.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <returns>The calculated route or null if no next place.</returns>
    public NavigationRoute? CalculateRouteToNextPlace(double currentLat, double currentLon)
    {
        var nextPlace = GetNextPlaceInSequence(currentLat, currentLon);
        if (nextPlace == null)
        {
            _logger.LogDebug("No next place in sequence");
            return null;
        }

        return CalculateRouteToPlace(currentLat, currentLon, nextPlace.Id.ToString());
    }

    /// <summary>
    /// Updates navigation state with current location.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <returns>The updated navigation state.</returns>
    public TripNavigationState UpdateLocation(double currentLat, double currentLon)
    {
        var state = new TripNavigationState();

        if (_activeRoute == null || _currentGraph == null)
        {
            state.Status = NavigationStatus.NoRoute;
            return state;
        }

        // Calculate distance to destination
        var destinationWaypoint = _activeRoute.Waypoints.LastOrDefault();
        if (destinationWaypoint != null)
        {
            state.DistanceToDestinationMeters = GeoMath.CalculateDistance(
                currentLat, currentLon,
                destinationWaypoint.Latitude, destinationWaypoint.Longitude);

            state.BearingToDestination = GeoMath.CalculateBearing(
                currentLat, currentLon,
                destinationWaypoint.Latitude, destinationWaypoint.Longitude);
        }

        // Check for arrival
        if (state.DistanceToDestinationMeters <= TripNavigationGraph.SegmentRoutingThresholdMeters)
        {
            state.Status = NavigationStatus.Arrived;
            state.Message = $"You have arrived at {_activeRoute.DestinationName}";
            StateChanged?.Invoke(this, state);
            return state;
        }

        // Check if off-route
        var currentEdge = GetCurrentEdge(currentLat, currentLon);
        if (currentEdge != null && _currentGraph.IsOffRoute(currentLat, currentLon, currentEdge))
        {
            state.Status = NavigationStatus.OffRoute;
            state.Message = "You are off route. Recalculating...";

            // Trigger reroute
            if (_destinationPlaceId != null)
            {
                var newRoute = CalculateRouteToPlace(currentLat, currentLon, _destinationPlaceId);
                if (newRoute != null)
                {
                    Rerouted?.Invoke(this, "Route recalculated");
                    state.Status = NavigationStatus.OnRoute;
                    state.Message = "Route recalculated";
                }
            }

            StateChanged?.Invoke(this, state);
            return state;
        }

        // On route - calculate progress
        state.Status = NavigationStatus.OnRoute;
        state.DistanceToNextWaypointMeters = CalculateDistanceToNextWaypoint(currentLat, currentLon);
        state.NextWaypointName = GetNextWaypointName(currentLat, currentLon);
        state.CurrentInstruction = GetCurrentInstruction(currentLat, currentLon);
        state.ProgressPercent = CalculateRouteProgress(currentLat, currentLon);

        // Estimate time remaining (walking speed 5 km/h)
        state.EstimatedTimeRemaining = TimeSpan.FromSeconds(state.DistanceToDestinationMeters / 1.4);

        // Check for turn announcements
        CheckForTurnAnnouncement(currentLat, currentLon, state);

        StateChanged?.Invoke(this, state);
        return state;
    }

    /// <summary>
    /// Checks if a turn announcement should be made and announces it.
    /// </summary>
    private void CheckForTurnAnnouncement(double lat, double lon, TripNavigationState state)
    {
        if (_activeRoute == null)
            return;

        // Don't announce too frequently
        var timeSinceLastAnnouncement = DateTime.UtcNow - _lastAnnouncementTime;
        if (timeSinceLastAnnouncement.TotalSeconds < MinAnnouncementIntervalSeconds)
            return;

        // For routes with OSRM steps, use step-based announcements
        if (_activeRoute.Steps.Count > 0 && !_activeRoute.IsDirectRoute)
        {
            CheckForStepAnnouncement(lat, lon);
            return;
        }

        // For direct routes or routes without steps, use waypoint-based announcements
        // Find next waypoint with a name (places, not route points)
        var nextWaypoint = _activeRoute.Waypoints
            .Where(w => !string.IsNullOrEmpty(w.Name) && w.Type != WaypointType.RoutePoint)
            .FirstOrDefault(w =>
            {
                var distance = GeoMath.CalculateDistance(lat, lon, w.Latitude, w.Longitude);
                return distance > 20 && distance <= TurnAnnouncementDistanceMeters; // Between 20-100m
            });

        if (nextWaypoint != null && nextWaypoint.Name != _lastAnnouncedWaypoint)
        {
            // Get the edge to this waypoint for transport mode info
            var transportMode = GetTransportModeToWaypoint(nextWaypoint);
            var announcement = BuildTurnAnnouncement(nextWaypoint, state.DistanceToNextWaypointMeters, transportMode);

            AnnounceInstruction(announcement, state.DistanceToNextWaypointMeters);
            _lastAnnouncedWaypoint = nextWaypoint.Name;
        }
    }

    /// <summary>
    /// Checks for step-based turn announcements (OSRM routes).
    /// </summary>
    private void CheckForStepAnnouncement(double lat, double lon)
    {
        if (_activeRoute?.Steps == null || _activeRoute.Steps.Count == 0)
            return;

        // Find the next step that we're approaching
        foreach (var step in _activeRoute.Steps)
        {
            // Skip "arrive" steps - those are announced differently
            if (step.ManeuverType == "arrive")
                continue;

            var distance = GeoMath.CalculateDistance(lat, lon, step.Latitude, step.Longitude);

            // Announce when within announcement range but not too close
            if (distance > 20 && distance <= TurnAnnouncementDistanceMeters)
            {
                var stepKey = $"{step.ManeuverType}:{step.Latitude:F5},{step.Longitude:F5}";
                if (stepKey != _lastAnnouncedWaypoint)
                {
                    AnnounceInstruction(step.Instruction, distance);
                    _lastAnnouncedWaypoint = stepKey;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Gets the transport mode for reaching a waypoint.
    /// </summary>
    private string? GetTransportModeToWaypoint(NavigationWaypoint waypoint)
    {
        if (_activeRoute == null || _currentGraph == null || string.IsNullOrEmpty(waypoint.PlaceId))
            return null;

        // Find the previous waypoint with a place ID
        var waypointsWithPlace = _activeRoute.Waypoints
            .Where(w => !string.IsNullOrEmpty(w.PlaceId))
            .ToList();

        var waypointIndex = waypointsWithPlace.FindIndex(w => w.PlaceId == waypoint.PlaceId);
        if (waypointIndex <= 0)
            return null;

        var previousWaypoint = waypointsWithPlace[waypointIndex - 1];
        var edge = _currentGraph.GetEdgeBetween(previousWaypoint.PlaceId!, waypoint.PlaceId!);

        return edge?.TransportMode;
    }

    /// <summary>
    /// Builds a turn announcement based on waypoint and transport mode.
    /// </summary>
    private static string BuildTurnAnnouncement(NavigationWaypoint waypoint, double distanceMeters, string? transportMode)
    {
        var distanceText = distanceMeters >= 1000
            ? $"{distanceMeters / 1000:F1} kilometers"
            : $"{distanceMeters:F0} meters";

        var actionVerb = transportMode?.ToLowerInvariant() switch
        {
            "walk" or "walking" => "Walk to",
            "drive" or "driving" or "car" => "Drive to",
            "transit" or "bus" or "train" => "Take transit to",
            "bike" or "bicycle" or "cycling" => "Cycle to",
            "ferry" or "boat" => "Take the ferry to",
            _ => "Head to"
        };

        if (waypoint.Type == WaypointType.Destination)
        {
            return $"{actionVerb} your destination, {waypoint.Name}. {distanceText} remaining.";
        }

        return $"In {distanceText}, {actionVerb.ToLowerInvariant()} {waypoint.Name}.";
    }

    /// <summary>
    /// Announces a navigation instruction using the audio service.
    /// </summary>
    private void AnnounceInstruction(string instruction, double distanceMeters = 0)
    {
        _lastAnnouncementTime = DateTime.UtcNow;
        _logger.LogDebug("Turn announcement: {Instruction} at {Distance:F0}m", instruction, distanceMeters);
        InstructionAnnounced?.Invoke(this, instruction);

        // Use audio service for proper settings integration
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await _audioService.AnnounceStepInstructionAsync(instruction, distanceMeters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce step instruction");
            }
        });
    }

    /// <summary>
    /// Gets all places in the current trip.
    /// </summary>
    public IEnumerable<TripPlace> GetTripPlaces()
    {
        return _currentTrip?.AllPlaces ?? Enumerable.Empty<TripPlace>();
    }

    #region Private Methods

    /// <summary>
    /// Builds a navigation graph from trip details.
    /// </summary>
    private TripNavigationGraph BuildNavigationGraph(TripDetails trip)
    {
        var graph = new TripNavigationGraph { TripId = trip.Id };

        // Add all places as nodes
        foreach (var region in trip.Regions)
        {
            foreach (var place in region.Places)
            {
                graph.AddNode(new NavigationNode
                {
                    Id = place.Id.ToString(),
                    Name = place.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    Type = NavigationNodeType.Place,
                    SortOrder = place.SortOrder,
                    Notes = place.Notes,
                    IconName = place.Icon
                });
            }
        }

        // Add segments as edges
        foreach (var segment in trip.Segments)
        {
            var edge = new NavigationEdge
            {
                FromNodeId = (segment.OriginId ?? Guid.Empty).ToString(),
                ToNodeId = (segment.DestinationId ?? Guid.Empty).ToString(),
                TransportMode = segment.TransportMode ?? "walking",
                DistanceKm = segment.DistanceKm ?? 0,
                DurationMinutes = (int)(segment.DurationMinutes ?? 0),
                EdgeType = NavigationEdgeType.UserSegment
            };

            // Decode route geometry if available
            if (!string.IsNullOrEmpty(segment.Geometry))
            {
                edge.RouteGeometry = PolylineDecoder.Decode(segment.Geometry);
            }

            graph.AddEdge(edge);
        }

        // No fallback connections - if no segment exists, use direct route
        // This matches old app behavior: honest navigation with bearing + distance

        return graph;
    }

    /// <summary>
    /// Builds a route from a path of node IDs.
    /// </summary>
    private NavigationRoute BuildRouteFromPath(List<string> path, double startLat, double startLon)
    {
        var route = new NavigationRoute();
        var waypoints = new List<NavigationWaypoint>();

        // Add starting position
        waypoints.Add(new NavigationWaypoint
        {
            Latitude = startLat,
            Longitude = startLon,
            Name = "Current Location",
            Type = WaypointType.Start
        });

        // Add path waypoints
        for (int i = 0; i < path.Count; i++)
        {
            if (_currentGraph!.Nodes.TryGetValue(path[i], out var node))
            {
                waypoints.Add(new NavigationWaypoint
                {
                    Latitude = node.Latitude,
                    Longitude = node.Longitude,
                    Name = node.Name,
                    Type = i == path.Count - 1 ? WaypointType.Destination : WaypointType.Waypoint,
                    PlaceId = node.Id
                });

                // Add edge waypoints if detailed geometry exists
                if (i < path.Count - 1)
                {
                    var edge = _currentGraph.GetEdgeBetween(path[i], path[i + 1]);
                    if (edge?.HasDetailedGeometry == true && edge.RouteGeometry != null)
                    {
                        foreach (var point in edge.RouteGeometry.Skip(1).Take(edge.RouteGeometry.Count - 2))
                        {
                            waypoints.Add(new NavigationWaypoint
                            {
                                Latitude = point.Latitude,
                                Longitude = point.Longitude,
                                Type = WaypointType.RoutePoint
                            });
                        }
                    }
                }
            }
        }

        route.Waypoints = waypoints;
        route.DestinationName = waypoints.LastOrDefault()?.Name ?? "Unknown";
        route.TotalDistanceMeters = CalculateTotalDistance(waypoints);
        route.EstimatedDuration = TimeSpan.FromSeconds(route.TotalDistanceMeters / 1.4);

        return route;
    }

    /// <summary>
    /// Builds a direct route to a destination.
    /// </summary>
    private NavigationRoute BuildDirectRoute(double startLat, double startLon, NavigationNode destination)
    {
        var distance = GeoMath.CalculateDistance(startLat, startLon, destination.Latitude, destination.Longitude);

        return new NavigationRoute
        {
            Waypoints = new List<NavigationWaypoint>
            {
                new() { Latitude = startLat, Longitude = startLon, Name = "Current Location", Type = WaypointType.Start },
                new() { Latitude = destination.Latitude, Longitude = destination.Longitude, Name = destination.Name, Type = WaypointType.Destination, PlaceId = destination.Id }
            },
            DestinationName = destination.Name,
            TotalDistanceMeters = distance,
            EstimatedDuration = TimeSpan.FromSeconds(distance / 1.4)
        };
    }

    /// <summary>
    /// Gets the next place in the trip sequence.
    /// </summary>
    private TripPlace? GetNextPlaceInSequence(double currentLat, double currentLon)
    {
        if (_currentTrip == null)
            return null;

        var places = _currentTrip.AllPlaces.OrderBy(p => p.SortOrder).ToList();
        if (places.Count == 0)
            return null;

        // Find the nearest place
        var nearest = places.MinBy(p =>
            GeoMath.CalculateDistance(currentLat, currentLon, p.Latitude, p.Longitude));

        if (nearest == null)
            return places.First();

        // Return the next place in sequence
        var currentIndex = places.IndexOf(nearest);
        return currentIndex < places.Count - 1 ? places[currentIndex + 1] : null;
    }

    /// <summary>
    /// Gets the current edge based on position.
    /// </summary>
    private NavigationEdge? GetCurrentEdge(double lat, double lon)
    {
        if (_activeRoute == null || _currentGraph == null)
            return null;

        // Find the two nearest waypoints and get the edge between them
        var waypointsWithPlace = _activeRoute.Waypoints
            .Where(w => !string.IsNullOrEmpty(w.PlaceId))
            .ToList();

        for (int i = 0; i < waypointsWithPlace.Count - 1; i++)
        {
            var from = waypointsWithPlace[i];
            var to = waypointsWithPlace[i + 1];
            var edge = _currentGraph.GetEdgeBetween(from.PlaceId!, to.PlaceId!);
            if (edge != null)
                return edge;
        }

        return null;
    }

    /// <summary>
    /// Calculates distance to the next waypoint.
    /// </summary>
    private double CalculateDistanceToNextWaypoint(double lat, double lon)
    {
        if (_activeRoute == null)
            return 0;

        // Find the next waypoint we haven't passed
        foreach (var waypoint in _activeRoute.Waypoints.Skip(1))
        {
            var distance = GeoMath.CalculateDistance(lat, lon, waypoint.Latitude, waypoint.Longitude);
            if (distance > TripNavigationGraph.SegmentRoutingThresholdMeters)
                return distance;
        }

        return 0;
    }

    /// <summary>
    /// Gets the next waypoint name.
    /// </summary>
    private string? GetNextWaypointName(double lat, double lon)
    {
        if (_activeRoute == null)
            return null;

        foreach (var waypoint in _activeRoute.Waypoints.Skip(1))
        {
            var distance = GeoMath.CalculateDistance(lat, lon, waypoint.Latitude, waypoint.Longitude);
            if (distance > TripNavigationGraph.SegmentRoutingThresholdMeters && !string.IsNullOrEmpty(waypoint.Name))
                return waypoint.Name;
        }

        return null;
    }

    /// <summary>
    /// Gets the current navigation instruction based on route steps.
    /// </summary>
    private string GetCurrentInstruction(double lat, double lon)
    {
        if (_activeRoute == null)
            return "Continue to destination";

        // If we have steps, find the current/next step
        if (_activeRoute.Steps.Count > 0)
        {
            var currentStep = FindCurrentStep(lat, lon);
            if (currentStep != null)
            {
                return currentStep.Instruction;
            }
        }

        // Fallback to basic instruction
        var nextName = GetNextWaypointName(lat, lon);
        var distance = CalculateDistanceToNextWaypoint(lat, lon);

        if (string.IsNullOrEmpty(nextName))
            return "Continue to destination";

        return distance >= 1000
            ? $"Head towards {nextName} ({distance / 1000:F1} km)"
            : $"Head towards {nextName} ({distance:F0} m)";
    }

    /// <summary>
    /// Finds the current step based on user position.
    /// Returns the step the user is currently on or approaching.
    /// </summary>
    private NavigationStep? FindCurrentStep(double lat, double lon)
    {
        if (_activeRoute == null || _activeRoute.Steps.Count == 0)
            return null;

        // For direct routes, just return the first step until arrival
        if (_activeRoute.IsDirectRoute)
        {
            var destDistance = GeoMath.CalculateDistance(lat, lon,
                _activeRoute.Waypoints.Last().Latitude,
                _activeRoute.Waypoints.Last().Longitude);

            // If very close to destination, show arrival
            if (destDistance < 30)
                return _activeRoute.Steps.LastOrDefault();

            return _activeRoute.Steps.FirstOrDefault();
        }

        // For OSRM routes, find the nearest step we haven't passed yet
        NavigationStep? currentStep = null;
        double minDistance = double.MaxValue;

        for (int i = 0; i < _activeRoute.Steps.Count; i++)
        {
            var step = _activeRoute.Steps[i];
            var distance = GeoMath.CalculateDistance(lat, lon, step.Latitude, step.Longitude);

            // Check if this step is ahead of us (not behind)
            // A step is "current" if we're within ~100m of it
            if (distance < 100 && distance < minDistance)
            {
                minDistance = distance;
                currentStep = step;
            }
        }

        // If we found a nearby step, return it
        if (currentStep != null)
            return currentStep;

        // Otherwise, find the next step ahead of us
        // by finding the step with smallest distance that's still ahead
        for (int i = 0; i < _activeRoute.Steps.Count - 1; i++)
        {
            var step = _activeRoute.Steps[i];
            var nextStep = _activeRoute.Steps[i + 1];

            var distToStep = GeoMath.CalculateDistance(lat, lon, step.Latitude, step.Longitude);
            var distToNext = GeoMath.CalculateDistance(lat, lon, nextStep.Latitude, nextStep.Longitude);

            // If we're closer to the current step than the next, this is our current step
            if (distToStep < distToNext)
            {
                // Return the next step's instruction (what's coming up)
                return nextStep;
            }
        }

        // Default to first step
        return _activeRoute.Steps.FirstOrDefault();
    }

    /// <summary>
    /// Calculates route progress as a percentage.
    /// </summary>
    private double CalculateRouteProgress(double lat, double lon)
    {
        if (_activeRoute == null || _activeRoute.TotalDistanceMeters <= 0)
            return 0;

        var destination = _activeRoute.Waypoints.LastOrDefault();
        if (destination == null)
            return 0;

        var remainingDistance = GeoMath.CalculateDistance(lat, lon, destination.Latitude, destination.Longitude);
        var progress = 100 * (1 - remainingDistance / _activeRoute.TotalDistanceMeters);

        return Math.Clamp(progress, 0, 100);
    }

    /// <summary>
    /// Calculates total distance of waypoints.
    /// </summary>
    private static double CalculateTotalDistance(List<NavigationWaypoint> waypoints)
    {
        double total = 0;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            total += GeoMath.CalculateDistance(
                waypoints[i].Latitude, waypoints[i].Longitude,
                waypoints[i + 1].Latitude, waypoints[i + 1].Longitude);
        }
        return total;
    }

    #endregion
}
