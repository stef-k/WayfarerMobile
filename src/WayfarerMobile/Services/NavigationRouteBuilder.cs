using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Navigation;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Builds navigation routes from various sources (OSRM, cache, path, direct).
/// </summary>
public class NavigationRouteBuilder : INavigationRouteBuilder
{
    private readonly ILogger<NavigationRouteBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationRouteBuilder"/> class.
    /// </summary>
    public NavigationRouteBuilder(ILogger<NavigationRouteBuilder> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public NavigationRoute BuildFromSegmentPath(
        List<string> path,
        double startLat, double startLon,
        TripNavigationGraph graph)
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
            if (graph.Nodes.TryGetValue(path[i], out var node))
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
                    var edge = graph.GetEdgeBetween(path[i], path[i + 1]);
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

    /// <inheritdoc/>
    public NavigationRoute BuildFromCachedRoute(
        CachedRoute cached,
        double startLat, double startLon,
        NavigationNode destination)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = PolylineDecoder.Decode(cached.Geometry);

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
            TotalDistanceMeters = cached.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(cached.DurationSeconds)
        };
    }

    /// <inheritdoc/>
    public NavigationRoute BuildFromOsrmResponse(
        OsrmRouteResult osrm,
        double startLat, double startLon,
        NavigationNode destination)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = PolylineDecoder.Decode(osrm.Geometry);

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
            TotalDistanceMeters = osrm.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(osrm.DurationSeconds)
        };
    }

    /// <inheritdoc/>
    public NavigationRoute BuildFromOsrmCoordinates(
        OsrmRouteResult osrm,
        double startLat, double startLon,
        double destLat, double destLon,
        string destName)
    {
        var waypoints = new List<NavigationWaypoint>();

        // Decode the polyline to get all route points
        var routePoints = PolylineDecoder.Decode(osrm.Geometry);

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
        var steps = osrm.Steps.Select(s => new NavigationStep
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
            TotalDistanceMeters = osrm.DistanceMeters,
            EstimatedDuration = TimeSpan.FromSeconds(osrm.DurationSeconds),
            IsDirectRoute = false
        };
    }

    /// <inheritdoc/>
    public NavigationRoute BuildDirectRoute(
        double startLat, double startLon,
        NavigationNode destination)
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

    /// <inheritdoc/>
    public NavigationRoute BuildDirectRouteToCoordinates(
        double startLat, double startLon,
        double destLat, double destLon,
        string destName,
        string profile = "foot")
    {
        var distance = GeoMath.CalculateDistance(startLat, startLon, destLat, destLon);
        var bearing = GeoMath.CalculateBearing(startLat, startLon, destLat, destLon);
        var direction = GetCardinalDirection(bearing);
        var distanceText = FormatDistance(distance);

        // Get speed based on profile (m/s)
        var speed = GetSpeedForProfile(profile);
        var durationSeconds = distance / speed;

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
                DurationSeconds = durationSeconds,
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
            EstimatedDuration = TimeSpan.FromSeconds(durationSeconds),
            IsDirectRoute = true,
            InitialBearing = bearing
        };
    }

    /// <summary>
    /// Gets the average speed in m/s for a routing profile.
    /// </summary>
    /// <param name="profile">The routing profile (foot, car, bike).</param>
    /// <returns>Speed in meters per second.</returns>
    private static double GetSpeedForProfile(string profile)
    {
        return profile.ToLowerInvariant() switch
        {
            "car" => 13.9,   // ~50 km/h (accounting for urban traffic, stops)
            "bike" => 4.2,   // ~15 km/h (average cycling speed)
            "foot" => 1.4,   // ~5 km/h (walking speed)
            _ => 1.4         // Default to walking
        };
    }

    #region Private Helpers

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
    /// Calculates the total distance along a list of waypoints.
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
