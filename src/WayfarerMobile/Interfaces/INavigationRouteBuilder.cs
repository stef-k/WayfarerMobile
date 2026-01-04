using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Navigation;
using WayfarerMobile.Services;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Builds navigation routes from various sources (OSRM, cache, path, direct).
/// </summary>
public interface INavigationRouteBuilder
{
    /// <summary>
    /// Builds a navigation route from a path of node IDs using the navigation graph.
    /// </summary>
    /// <param name="path">List of node IDs forming the path.</param>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="graph">The navigation graph containing node details.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildFromSegmentPath(
        List<string> path,
        double startLat, double startLon,
        TripNavigationGraph graph);

    /// <summary>
    /// Builds a navigation route from cached OSRM route data.
    /// </summary>
    /// <param name="cached">The cached route data.</param>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="destination">The destination node.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildFromCachedRoute(
        CachedRoute cached,
        double startLat, double startLon,
        NavigationNode destination);

    /// <summary>
    /// Builds a navigation route from an OSRM response to a navigation node.
    /// </summary>
    /// <param name="osrm">The OSRM route result.</param>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="destination">The destination node.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildFromOsrmResponse(
        OsrmRouteResult osrm,
        double startLat, double startLon,
        NavigationNode destination);

    /// <summary>
    /// Builds a navigation route from an OSRM response to coordinates.
    /// </summary>
    /// <param name="osrm">The OSRM route result.</param>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="destLat">Destination latitude.</param>
    /// <param name="destLon">Destination longitude.</param>
    /// <param name="destName">Destination name for display.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildFromOsrmCoordinates(
        OsrmRouteResult osrm,
        double startLat, double startLon,
        double destLat, double destLon,
        string destName);

    /// <summary>
    /// Builds a direct route (straight line) to a navigation node.
    /// </summary>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="destination">The destination node.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildDirectRoute(
        double startLat, double startLon,
        NavigationNode destination);

    /// <summary>
    /// Builds a direct route (straight line) to coordinates.
    /// </summary>
    /// <param name="startLat">Starting latitude.</param>
    /// <param name="startLon">Starting longitude.</param>
    /// <param name="destLat">Destination latitude.</param>
    /// <param name="destLon">Destination longitude.</param>
    /// <param name="destName">Destination name for display.</param>
    /// <param name="profile">Routing profile for ETA calculation (foot, car, bike). Default is foot.</param>
    /// <returns>The constructed navigation route.</returns>
    NavigationRoute BuildDirectRouteToCoordinates(
        double startLat, double startLon,
        double destLat, double destLon,
        string destName,
        string profile = "foot");
}
