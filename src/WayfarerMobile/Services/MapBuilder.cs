using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services.TileCache;
using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Pen = Mapsui.Styles.Pen;

namespace WayfarerMobile.Services;

/// <summary>
/// Injectable service for creating map instances and managing map layers.
/// Provides shared functionality for all ViewModels that own their own Map instance.
/// Registered as Transient so each ViewModel gets its own instance.
/// </summary>
public class MapBuilder : IMapBuilder
{
    private readonly ILogger<MapBuilder> _logger;
    private readonly WayfarerTileSource _tileSource;

    /// <summary>
    /// Creates a new instance of MapBuilder.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="tileSource">The tile source for map tiles.</param>
    public MapBuilder(ILogger<MapBuilder> logger, WayfarerTileSource tileSource)
    {
        _logger = logger;
        _tileSource = tileSource;
    }

    #region Map Creation

    /// <inheritdoc />
    public Map CreateMap(params WritableLayer[] additionalLayers)
    {
        var map = new Map
        {
            CRS = "EPSG:3857" // Web Mercator
        };

        // Add tile layer using injected tile source
        map.Layers.Add(new TileLayer(_tileSource) { Name = "Tiles" });

        // Add any additional layers in order (first = bottom, last = top)
        // Z-order: Tiles -> additionalLayers[0] -> ... -> additionalLayers[n]
        // Example order: segments (bottom) -> routes -> places -> dropped pins -> location (top)
        foreach (var layer in additionalLayers)
        {
            map.Layers.Add(layer);
        }

        _logger.LogDebug("Created new Map instance with {LayerCount} layers", map.Layers.Count);

        return map;
    }

    /// <inheritdoc />
    public WritableLayer CreateLayer(string name)
    {
        return new WritableLayer
        {
            Name = name,
            Style = null // Style is set per feature
        };
    }

    #endregion

    #region Navigation Route

    /// <inheritdoc />
    public List<MPoint> UpdateNavigationRoute(
        WritableLayer routeLayer,
        WritableLayer completedLayer,
        NavigationRoute route)
    {
        routeLayer.Clear();
        completedLayer.Clear();

        var points = new List<MPoint>();

        if (route.Waypoints == null || route.Waypoints.Count < 2)
        {
            routeLayer.DataHasChanged();
            completedLayer.DataHasChanged();
            return points;
        }

        // Convert waypoints to Mapsui coordinates
        var lineCoords = route.Waypoints
            .Select(w => SphericalMercator.FromLonLat(w.Longitude, w.Latitude))
            .Select(p =>
            {
                var mpoint = new MPoint(p.x, p.y);
                points.Add(mpoint);
                return new Coordinate(p.x, p.y);
            })
            .ToArray();

        var lineString = new LineString(lineCoords);

        var feature = new GeometryFeature(lineString)
        {
            Styles = new[] { CreateNavigationRouteStyle() }
        };

        routeLayer.Add(feature);
        routeLayer.DataHasChanged();
        completedLayer.DataHasChanged();

        _logger.LogDebug("Added navigation route with {WaypointCount} waypoints", route.Waypoints.Count);

        return points;
    }

    /// <inheritdoc />
    public void UpdateNavigationRouteProgress(
        WritableLayer routeLayer,
        WritableLayer completedLayer,
        NavigationRoute route,
        double currentLat,
        double currentLon)
    {
        if (route.Waypoints == null || route.Waypoints.Count < 2)
            return;

        // Find nearest waypoint
        var nearestIndex = FindNearestWaypointIndex(route.Waypoints, currentLat, currentLon);

        // Clear both layers
        routeLayer.Clear();
        completedLayer.Clear();

        // Add completed portion (start to current position)
        if (nearestIndex > 0)
        {
            var completedCoords = route.Waypoints
                .Take(nearestIndex + 1)
                .Select(w => SphericalMercator.FromLonLat(w.Longitude, w.Latitude))
                .Select(p => new Coordinate(p.x, p.y))
                .ToArray();

            if (completedCoords.Length >= 2)
            {
                var completedLine = new LineString(completedCoords);
                var completedFeature = new GeometryFeature(completedLine)
                {
                    Styles = new[] { CreateNavigationRouteCompletedStyle() }
                };
                completedLayer.Add(completedFeature);
            }
        }

        // Add remaining portion (current position to end)
        var remainingCoords = route.Waypoints
            .Skip(nearestIndex)
            .Select(w => SphericalMercator.FromLonLat(w.Longitude, w.Latitude))
            .Select(p => new Coordinate(p.x, p.y))
            .ToArray();

        if (remainingCoords.Length >= 2)
        {
            var remainingLine = new LineString(remainingCoords);
            var remainingFeature = new GeometryFeature(remainingLine)
            {
                Styles = new[] { CreateNavigationRouteStyle() }
            };
            routeLayer.Add(remainingFeature);
        }

        routeLayer.DataHasChanged();
        completedLayer.DataHasChanged();
    }

    /// <summary>
    /// Creates the style for the active navigation route (blue).
    /// </summary>
    private static IStyle CreateNavigationRouteStyle()
    {
        return new VectorStyle
        {
            Line = new Pen(Color.FromArgb(255, 33, 150, 243), 6) // Material Blue
            {
                PenStyle = PenStyle.Solid,
                PenStrokeCap = PenStrokeCap.Round,
                StrokeJoin = StrokeJoin.Round
            }
        };
    }

    /// <summary>
    /// Creates the style for the completed portion of navigation route (gray).
    /// </summary>
    private static IStyle CreateNavigationRouteCompletedStyle()
    {
        return new VectorStyle
        {
            Line = new Pen(Color.FromArgb(180, 158, 158, 158), 6) // Gray semi-transparent
            {
                PenStyle = PenStyle.Solid,
                PenStrokeCap = PenStrokeCap.Round,
                StrokeJoin = StrokeJoin.Round
            }
        };
    }

    /// <summary>
    /// Finds the index of the nearest waypoint to the current position.
    /// </summary>
    private static int FindNearestWaypointIndex(
        IReadOnlyList<NavigationWaypoint> waypoints,
        double lat,
        double lon)
    {
        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var distance = GeoMath.CalculateDistance(lat, lon, waypoints[i].Latitude, waypoints[i].Longitude);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    #endregion

    #region Utility Methods

    /// <inheritdoc />
    public void ZoomToPoints(Map map, List<MPoint> points, double paddingPercent = 0.2)
    {
        if (points.Count < 2)
        {
            if (points.Count == 1)
            {
                map.Navigator.CenterOn(points[0]);
            }
            return;
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        var padding = Math.Max(maxX - minX, maxY - minY) * paddingPercent;
        var extent = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);

        map.Navigator.ZoomToBox(extent);
    }

    /// <inheritdoc />
    public void CenterOnLocation(Map map, double latitude, double longitude, int? zoomLevel = null)
    {
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        map.Navigator.CenterOn(new MPoint(x, y));

        if (zoomLevel.HasValue && map.Navigator.Resolutions?.Count > zoomLevel.Value)
        {
            map.Navigator.ZoomTo(map.Navigator.Resolutions[zoomLevel.Value]);
        }
    }

    /// <inheritdoc />
    public (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? GetViewportBounds(Map map)
    {
        var viewport = map.Navigator.Viewport;
        var extent = viewport.ToExtent();
        if (extent == null)
            return null;

        var minLonLat = SphericalMercator.ToLonLat(extent.MinX, extent.MinY);
        var maxLonLat = SphericalMercator.ToLonLat(extent.MaxX, extent.MaxY);

        var resolution = viewport.Resolution;
        var zoomLevel = CalculateZoomLevel(resolution);

        return (minLonLat.lon, minLonLat.lat, maxLonLat.lon, maxLonLat.lat, zoomLevel);
    }

    /// <summary>
    /// Calculates the approximate web map zoom level from Mapsui resolution.
    /// </summary>
    private static double CalculateZoomLevel(double resolution)
    {
        const double maxResolution = 156543.03392; // Zoom level 0
        return Math.Log2(maxResolution / resolution);
    }

    /// <inheritdoc />
    public WritableLayer? GetLayer(Map map, string layerName)
    {
        return map.Layers.FirstOrDefault(l => l.Name == layerName) as WritableLayer;
    }

    #endregion
}
