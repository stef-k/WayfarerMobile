using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Models;
using Map = Mapsui.Map;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for creating and managing map instances and layers.
/// Each ViewModel that needs a map should inject this to create its own isolated Map instance.
/// Feature-specific layer operations are handled by dedicated layer services.
/// </summary>
public interface IMapBuilder
{
    #region Map Creation

    /// <summary>
    /// Creates a new Map instance with the Wayfarer tile source (injected).
    /// Each ViewModel should call this to get its own isolated map.
    /// </summary>
    /// <param name="additionalLayers">
    /// Optional additional layers to add after tile layer.
    /// Layers are rendered in order (first = bottom, last = top).
    /// Example z-order: segments -> routes -> places -> dropped pins -> location indicator.
    /// </param>
    /// <returns>A configured Map instance.</returns>
    Map CreateMap(params WritableLayer[] additionalLayers);

    /// <summary>
    /// Creates a WritableLayer with the specified name.
    /// </summary>
    /// <param name="name">The layer name.</param>
    /// <returns>A new WritableLayer instance.</returns>
    WritableLayer CreateLayer(string name);

    #endregion

    #region Navigation Route

    /// <summary>
    /// Updates the navigation route on the specified layers.
    /// </summary>
    /// <param name="routeLayer">The layer for the remaining route (blue).</param>
    /// <param name="completedLayer">The layer for the completed route (gray).</param>
    /// <param name="route">The navigation route to display.</param>
    /// <returns>List of points for optional zoom-to-fit.</returns>
    List<MPoint> UpdateNavigationRoute(
        WritableLayer routeLayer,
        WritableLayer completedLayer,
        NavigationRoute route);

    /// <summary>
    /// Updates the navigation route progress, showing completed and remaining portions.
    /// </summary>
    /// <param name="routeLayer">The layer for the remaining route (blue).</param>
    /// <param name="completedLayer">The layer for the completed route (gray).</param>
    /// <param name="route">The navigation route.</param>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    void UpdateNavigationRouteProgress(
        WritableLayer routeLayer,
        WritableLayer completedLayer,
        NavigationRoute route,
        double currentLat,
        double currentLon);

    #endregion

    #region Utility Methods

    /// <summary>
    /// Zooms the map to fit a list of points with padding.
    /// </summary>
    /// <param name="map">The map to zoom.</param>
    /// <param name="points">The points to fit.</param>
    /// <param name="paddingPercent">Padding as percentage of extent (default 20%).</param>
    void ZoomToPoints(Map map, List<MPoint> points, double paddingPercent = 0.2);

    /// <summary>
    /// Centers the map on a specific location.
    /// </summary>
    /// <param name="map">The map to center.</param>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="zoomLevel">Optional zoom level (0-20 web map scale).</param>
    void CenterOnLocation(Map map, double latitude, double longitude, int? zoomLevel = null);

    /// <summary>
    /// Gets the current viewport bounds of the map.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns>Tuple of (MinLon, MinLat, MaxLon, MaxLat, ZoomLevel) or null if not available.</returns>
    (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? GetViewportBounds(Map map);

    /// <summary>
    /// Gets a layer by name from the map, or null if not found.
    /// </summary>
    /// <param name="map">The map to search.</param>
    /// <param name="layerName">The layer name to find.</param>
    /// <returns>The layer if found, otherwise null.</returns>
    WritableLayer? GetLayer(Map map, string layerName);

    #endregion
}
