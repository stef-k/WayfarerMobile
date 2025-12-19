using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for MapBuilder.
/// Tests map creation, layer management, navigation route display, and utility methods.
/// </summary>
public class MapBuilderTests
{
    #region Test Setup

    private readonly TestMapBuilder _mapBuilder;
    private readonly ILogger<TestMapBuilder> _logger;

    public MapBuilderTests()
    {
        _logger = NullLogger<TestMapBuilder>.Instance;
        _mapBuilder = new TestMapBuilder(_logger);
    }

    #endregion

    #region CreateMap Tests

    [Fact]
    public void CreateMap_ReturnsMap_WithCorrectCRS()
    {
        var map = _mapBuilder.CreateMap();

        map.Should().NotBeNull();
        map.CRS.Should().Be("EPSG:3857");
    }

    [Fact]
    public void CreateMap_IncludesTileLayer()
    {
        var map = _mapBuilder.CreateMap();

        map.Layers.Should().Contain(l => l.Name == "Tiles");
    }

    [Fact]
    public void CreateMap_WithNoAdditionalLayers_HasOnlyTileLayer()
    {
        var map = _mapBuilder.CreateMap();

        map.Layers.Should().HaveCount(1);
    }

    [Fact]
    public void CreateMap_WithAdditionalLayers_AddsAllLayers()
    {
        var layer1 = new MapBuilderTestWritableLayer { Name = "Layer1" };
        var layer2 = new MapBuilderTestWritableLayer { Name = "Layer2" };

        var map = _mapBuilder.CreateMap(layer1, layer2);

        map.Layers.Should().HaveCount(3); // Tiles + 2 additional
        map.Layers.Should().Contain(l => l.Name == "Layer1");
        map.Layers.Should().Contain(l => l.Name == "Layer2");
    }

    [Fact]
    public void CreateMap_LayersAddedInOrder()
    {
        var layer1 = new MapBuilderTestWritableLayer { Name = "First" };
        var layer2 = new MapBuilderTestWritableLayer { Name = "Second" };
        var layer3 = new MapBuilderTestWritableLayer { Name = "Third" };

        var map = _mapBuilder.CreateMap(layer1, layer2, layer3);

        var layers = map.Layers.ToList();
        layers[0].Name.Should().Be("Tiles");
        layers[1].Name.Should().Be("First");
        layers[2].Name.Should().Be("Second");
        layers[3].Name.Should().Be("Third");
    }

    #endregion

    #region CreateLayer Tests

    [Fact]
    public void CreateLayer_ReturnsLayerWithCorrectName()
    {
        var layer = _mapBuilder.CreateLayer("TestLayer");

        layer.Name.Should().Be("TestLayer");
    }

    [Theory]
    [InlineData("GroupMembers")]
    [InlineData("TripPlaces")]
    [InlineData("TimelineLocations")]
    [InlineData("DroppedPin")]
    [InlineData("NavigationRoute")]
    public void CreateLayer_VariousNames_ReturnsNamedLayer(string name)
    {
        var layer = _mapBuilder.CreateLayer(name);

        layer.Name.Should().Be(name);
    }

    #endregion

    #region UpdateNavigationRoute Tests

    [Fact]
    public void UpdateNavigationRoute_EmptyWaypoints_ReturnsEmptyPoints()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute { Waypoints = new List<MapBuilderTestNavigationWaypoint>() };

        var points = _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        points.Should().BeEmpty();
    }

    [Fact]
    public void UpdateNavigationRoute_SingleWaypoint_ReturnsEmptyPoints()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 }
            }
        };

        var points = _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        points.Should().BeEmpty();
    }

    [Fact]
    public void UpdateNavigationRoute_TwoWaypoints_ReturnsPoints()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 }
            }
        };

        var points = _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        points.Should().HaveCount(2);
    }

    [Fact]
    public void UpdateNavigationRoute_AddsFeatureToRouteLayer()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 }
            }
        };

        _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        routeLayer.FeatureCount.Should().Be(1);
        routeLayer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateNavigationRoute_ClearsBothLayersFirst()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 }
            }
        };

        _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        routeLayer.ClearCount.Should().Be(1);
        completedLayer.ClearCount.Should().Be(1);
    }

    [Fact]
    public void UpdateNavigationRoute_MultipleWaypoints_ReturnsAllPoints()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 },
                new() { Latitude = 40.7128, Longitude = -74.0060 },
                new() { Latitude = 35.6762, Longitude = 139.6503 }
            }
        };

        var points = _mapBuilder.UpdateNavigationRoute(routeLayer, completedLayer, route);

        points.Should().HaveCount(4);
    }

    #endregion

    #region UpdateNavigationRouteProgress Tests

    [Fact]
    public void UpdateNavigationRouteProgress_EmptyWaypoints_DoesNothing()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute { Waypoints = new List<MapBuilderTestNavigationWaypoint>() };

        _mapBuilder.UpdateNavigationRouteProgress(routeLayer, completedLayer, route, 51.5074, -0.1278);

        routeLayer.ClearCount.Should().Be(0);
        completedLayer.ClearCount.Should().Be(0);
    }

    [Fact]
    public void UpdateNavigationRouteProgress_ValidRoute_ClearsBothLayers()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 }
            }
        };

        _mapBuilder.UpdateNavigationRouteProgress(routeLayer, completedLayer, route, 51.5074, -0.1278);

        routeLayer.ClearCount.Should().Be(1);
        completedLayer.ClearCount.Should().Be(1);
    }

    [Fact]
    public void UpdateNavigationRouteProgress_AtStart_NoCompletedPortion()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 51.5174, Longitude = -0.1378 },
                new() { Latitude = 51.5274, Longitude = -0.1478 }
            }
        };

        _mapBuilder.UpdateNavigationRouteProgress(routeLayer, completedLayer, route, 51.5074, -0.1278);

        // At start, no completed portion
        completedLayer.FeatureCount.Should().Be(0);
        // But should have remaining route
        routeLayer.FeatureCount.Should().Be(1);
    }

    [Fact]
    public void UpdateNavigationRouteProgress_CallsDataHasChanged()
    {
        var routeLayer = new MapBuilderTestWritableLayer { Name = "Route" };
        var completedLayer = new MapBuilderTestWritableLayer { Name = "Completed" };
        var route = new MapBuilderTestNavigationRoute
        {
            Waypoints = new List<MapBuilderTestNavigationWaypoint>
            {
                new() { Latitude = 51.5074, Longitude = -0.1278 },
                new() { Latitude = 48.8566, Longitude = 2.3522 }
            }
        };

        _mapBuilder.UpdateNavigationRouteProgress(routeLayer, completedLayer, route, 51.5074, -0.1278);

        routeLayer.DataChangedCount.Should().Be(1);
        completedLayer.DataChangedCount.Should().Be(1);
    }

    #endregion

    #region ZoomToPoints Tests

    [Fact]
    public void ZoomToPoints_EmptyList_DoesNothing()
    {
        var map = _mapBuilder.CreateMap();
        var points = new List<MapBuilderTestMPoint>();

        _mapBuilder.ZoomToPoints(map, points);

        map.ZoomToBoxCalled.Should().BeFalse();
        map.CenterOnCalled.Should().BeFalse();
    }

    [Fact]
    public void ZoomToPoints_SinglePoint_CentersOnPoint()
    {
        var map = _mapBuilder.CreateMap();
        var points = new List<MapBuilderTestMPoint> { new(100, 200) };

        _mapBuilder.ZoomToPoints(map, points);

        map.CenterOnCalled.Should().BeTrue();
        map.ZoomToBoxCalled.Should().BeFalse();
    }

    [Fact]
    public void ZoomToPoints_TwoPoints_ZoomsToBox()
    {
        var map = _mapBuilder.CreateMap();
        var points = new List<MapBuilderTestMPoint> { new(100, 200), new(300, 400) };

        _mapBuilder.ZoomToPoints(map, points);

        map.ZoomToBoxCalled.Should().BeTrue();
    }

    [Fact]
    public void ZoomToPoints_CalculatesExtentWithPadding()
    {
        var map = _mapBuilder.CreateMap();
        var points = new List<MapBuilderTestMPoint>
        {
            new(0, 0),
            new(100, 100)
        };

        _mapBuilder.ZoomToPoints(map, points, 0.2);

        // With 20% padding on extent of 100, padding = 20
        // So extent should be -20 to 120
        map.LastExtent.Should().NotBeNull();
        map.LastExtent!.MinX.Should().BeApproximately(-20, 0.001);
        map.LastExtent.MinY.Should().BeApproximately(-20, 0.001);
        map.LastExtent.MaxX.Should().BeApproximately(120, 0.001);
        map.LastExtent.MaxY.Should().BeApproximately(120, 0.001);
    }

    #endregion

    #region CenterOnLocation Tests

    [Fact]
    public void CenterOnLocation_ConvertsToWebMercator()
    {
        var map = _mapBuilder.CreateMap();

        _mapBuilder.CenterOnLocation(map, 51.5074, -0.1278);

        map.CenterOnCalled.Should().BeTrue();
        // Should not be the raw lat/lon values
        map.LastCenterPoint.Should().NotBeNull();
        map.LastCenterPoint!.X.Should().NotBe(-0.1278);
        map.LastCenterPoint.Y.Should().NotBe(51.5074);
    }

    [Fact]
    public void CenterOnLocation_WithZoomLevel_SetsResolution()
    {
        var map = _mapBuilder.CreateMap();

        _mapBuilder.CenterOnLocation(map, 51.5074, -0.1278, zoomLevel: 15);

        map.ZoomToResolutionCalled.Should().BeTrue();
    }

    [Fact]
    public void CenterOnLocation_WithoutZoomLevel_DoesNotChangeResolution()
    {
        var map = _mapBuilder.CreateMap();

        _mapBuilder.CenterOnLocation(map, 51.5074, -0.1278);

        map.ZoomToResolutionCalled.Should().BeFalse();
    }

    #endregion

    #region GetLayer Tests

    [Fact]
    public void GetLayer_ExistingLayer_ReturnsLayer()
    {
        var map = _mapBuilder.CreateMap();
        var customLayer = new MapBuilderTestWritableLayer { Name = "CustomLayer" };
        map.AddLayer(customLayer);

        var layer = _mapBuilder.GetLayer(map, "CustomLayer");

        layer.Should().NotBeNull();
        layer!.Name.Should().Be("CustomLayer");
    }

    [Fact]
    public void GetLayer_NonExistentLayer_ReturnsNull()
    {
        var map = _mapBuilder.CreateMap();

        var layer = _mapBuilder.GetLayer(map, "NonExistent");

        layer.Should().BeNull();
    }

    [Fact]
    public void GetLayer_TilesLayer_ReturnsNull()
    {
        var map = _mapBuilder.CreateMap();

        // Tiles layer is a TileLayer, not WritableLayer
        var layer = _mapBuilder.GetLayer(map, "Tiles");

        layer.Should().BeNull();
    }

    #endregion

    #region GetViewportBounds Tests

    [Fact]
    public void GetViewportBounds_NullExtent_ReturnsNull()
    {
        var map = _mapBuilder.CreateMap();
        map.SetViewport(null); // No extent set

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().BeNull();
    }

    [Fact]
    public void GetViewportBounds_ValidExtent_ReturnsLonLatBounds()
    {
        var map = _mapBuilder.CreateMap();
        // Set viewport with Web Mercator coordinates
        // These represent approximately -10 to 10 degrees lon and -5 to 5 degrees lat
        map.SetViewport(new MapBuilderTestExtent
        {
            MinX = -1113194.91, // ~-10 degrees lon
            MinY = -557305.26,  // ~-5 degrees lat
            MaxX = 1113194.91,  // ~10 degrees lon
            MaxY = 557305.26    // ~5 degrees lat
        }, resolution: 156543.03392 / Math.Pow(2, 10)); // Zoom level 10

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().NotBeNull();
        bounds!.Value.MinLon.Should().BeApproximately(-10, 0.1);
        bounds.Value.MaxLon.Should().BeApproximately(10, 0.1);
        bounds.Value.MinLat.Should().BeApproximately(-5, 0.1);
        bounds.Value.MaxLat.Should().BeApproximately(5, 0.1);
    }

    [Fact]
    public void GetViewportBounds_ReturnsCorrectZoomLevel()
    {
        var map = _mapBuilder.CreateMap();
        // Set viewport at zoom level 15
        var zoom15Resolution = 156543.03392 / Math.Pow(2, 15);
        map.SetViewport(new MapBuilderTestExtent
        {
            MinX = 0, MinY = 0, MaxX = 100, MaxY = 100
        }, resolution: zoom15Resolution);

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().NotBeNull();
        bounds!.Value.ZoomLevel.Should().BeApproximately(15, 0.1);
    }

    [Fact]
    public void GetViewportBounds_AtEquator_ReturnsCorrectBounds()
    {
        var map = _mapBuilder.CreateMap();
        // Origin (0,0 in Web Mercator = 0,0 in lat/lon)
        map.SetViewport(new MapBuilderTestExtent
        {
            MinX = -100000, MinY = -100000, MaxX = 100000, MaxY = 100000
        }, resolution: 1000);

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().NotBeNull();
        // At equator, small extent should be symmetric
        bounds!.Value.MinLon.Should().BeApproximately(-bounds.Value.MaxLon, 0.01);
        bounds.Value.MinLat.Should().BeApproximately(-bounds.Value.MaxLat, 0.01);
    }

    [Fact]
    public void GetViewportBounds_HighZoom_ReturnsHighZoomLevel()
    {
        var map = _mapBuilder.CreateMap();
        // Set viewport at zoom level 18 (street level)
        var zoom18Resolution = 156543.03392 / Math.Pow(2, 18);
        map.SetViewport(new MapBuilderTestExtent
        {
            MinX = 0, MinY = 0, MaxX = 10, MaxY = 10
        }, resolution: zoom18Resolution);

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().NotBeNull();
        bounds!.Value.ZoomLevel.Should().BeApproximately(18, 0.1);
    }

    [Fact]
    public void GetViewportBounds_LowZoom_ReturnsLowZoomLevel()
    {
        var map = _mapBuilder.CreateMap();
        // Set viewport at zoom level 2 (continental)
        var zoom2Resolution = 156543.03392 / Math.Pow(2, 2);
        map.SetViewport(new MapBuilderTestExtent
        {
            MinX = -10000000, MinY = -10000000, MaxX = 10000000, MaxY = 10000000
        }, resolution: zoom2Resolution);

        var bounds = _mapBuilder.GetViewportBounds(map);

        bounds.Should().NotBeNull();
        bounds!.Value.ZoomLevel.Should().BeApproximately(2, 0.1);
    }

    #endregion
}

#region Test Infrastructure

/// <summary>
/// Base interface for test layers providing Name property.
/// </summary>
internal interface IMapBuilderTestLayer
{
    string Name { get; set; }
}

internal class MapBuilderTestWritableLayer : IMapBuilderTestLayer
{
    private readonly List<object> _features = new();

    public string Name { get; set; } = string.Empty;
    public int FeatureCount => _features.Count;
    public int ClearCount { get; private set; }
    public int DataChangedCount { get; private set; }

    public void Add(object feature)
    {
        _features.Add(feature);
    }

    public void Clear()
    {
        _features.Clear();
        ClearCount++;
    }

    public void DataHasChanged()
    {
        DataChangedCount++;
    }
}

internal class MapBuilderTestTileLayer : IMapBuilderTestLayer
{
    public string Name { get; set; } = "Tiles";
}

internal class MapBuilderTestMPoint
{
    public double X { get; }
    public double Y { get; }

    public MapBuilderTestMPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

internal class MapBuilderTestExtent
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
}

internal class MapBuilderTestMap
{
    private readonly List<IMapBuilderTestLayer> _layers = new();

    public string CRS { get; set; } = "EPSG:3857";
    public IEnumerable<IMapBuilderTestLayer> Layers => _layers;
    public bool ZoomToBoxCalled { get; private set; }
    public bool CenterOnCalled { get; private set; }
    public bool ZoomToResolutionCalled { get; private set; }
    public MapBuilderTestMPoint? LastCenterPoint { get; private set; }
    public MapBuilderTestExtent? LastExtent { get; private set; }

    // Viewport state for GetViewportBounds testing
    public MapBuilderTestExtent? ViewportExtent { get; private set; }
    public double ViewportResolution { get; private set; } = 1.0;

    public void AddTileLayer(string name)
    {
        _layers.Add(new MapBuilderTestTileLayer { Name = name });
    }

    public void AddLayer(MapBuilderTestWritableLayer layer)
    {
        _layers.Add(layer);
    }

    public void ZoomToBox(MapBuilderTestExtent extent)
    {
        ZoomToBoxCalled = true;
        LastExtent = extent;
    }

    public void CenterOn(MapBuilderTestMPoint point)
    {
        CenterOnCalled = true;
        LastCenterPoint = point;
    }

    public void ZoomToResolution(double resolution)
    {
        ZoomToResolutionCalled = true;
    }

    /// <summary>
    /// Sets the viewport extent and resolution for testing GetViewportBounds.
    /// </summary>
    public void SetViewport(MapBuilderTestExtent? extent, double resolution = 1.0)
    {
        ViewportExtent = extent;
        ViewportResolution = resolution;
    }
}

internal class MapBuilderTestNavigationWaypoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

internal class MapBuilderTestNavigationRoute
{
    public List<MapBuilderTestNavigationWaypoint> Waypoints { get; set; } = new();
}

internal class TestMapBuilder
{
    private readonly ILogger<TestMapBuilder> _logger;

    public TestMapBuilder(ILogger<TestMapBuilder> logger)
    {
        _logger = logger;
    }

    public MapBuilderTestMap CreateMap(params MapBuilderTestWritableLayer[] additionalLayers)
    {
        var map = new MapBuilderTestMap
        {
            CRS = "EPSG:3857"
        };

        map.AddTileLayer("Tiles");

        foreach (var layer in additionalLayers)
        {
            map.AddLayer(layer);
        }

        return map;
    }

    public MapBuilderTestWritableLayer CreateLayer(string name)
    {
        return new MapBuilderTestWritableLayer { Name = name };
    }

    public List<MapBuilderTestMPoint> UpdateNavigationRoute(
        MapBuilderTestWritableLayer routeLayer,
        MapBuilderTestWritableLayer completedLayer,
        MapBuilderTestNavigationRoute route)
    {
        routeLayer.Clear();
        completedLayer.Clear();

        var points = new List<MapBuilderTestMPoint>();

        if (route.Waypoints == null || route.Waypoints.Count < 2)
        {
            routeLayer.DataHasChanged();
            completedLayer.DataHasChanged();
            return points;
        }

        foreach (var waypoint in route.Waypoints)
        {
            var x = waypoint.Longitude * 20037508.34 / 180;
            var y = Math.Log(Math.Tan((90 + waypoint.Latitude) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;
            points.Add(new MapBuilderTestMPoint(x, y));
        }

        routeLayer.Add(new { Type = "LineString", Points = points });
        routeLayer.DataHasChanged();
        completedLayer.DataHasChanged();

        return points;
    }

    public void UpdateNavigationRouteProgress(
        MapBuilderTestWritableLayer routeLayer,
        MapBuilderTestWritableLayer completedLayer,
        MapBuilderTestNavigationRoute route,
        double currentLat,
        double currentLon)
    {
        if (route.Waypoints == null || route.Waypoints.Count < 2)
            return;

        var nearestIndex = FindNearestWaypointIndex(route.Waypoints, currentLat, currentLon);

        routeLayer.Clear();
        completedLayer.Clear();

        // Add completed portion
        if (nearestIndex > 0)
        {
            var completedPoints = route.Waypoints.Take(nearestIndex + 1).ToList();
            if (completedPoints.Count >= 2)
            {
                completedLayer.Add(new { Type = "Completed", Points = completedPoints });
            }
        }

        // Add remaining portion
        var remainingPoints = route.Waypoints.Skip(nearestIndex).ToList();
        if (remainingPoints.Count >= 2)
        {
            routeLayer.Add(new { Type = "Remaining", Points = remainingPoints });
        }

        routeLayer.DataHasChanged();
        completedLayer.DataHasChanged();
    }

    private static int FindNearestWaypointIndex(List<MapBuilderTestNavigationWaypoint> waypoints, double lat, double lon)
    {
        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var dlat = waypoints[i].Latitude - lat;
            var dlon = waypoints[i].Longitude - lon;
            var distance = Math.Sqrt(dlat * dlat + dlon * dlon);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    public void ZoomToPoints(MapBuilderTestMap map, List<MapBuilderTestMPoint> points, double paddingPercent = 0.2)
    {
        if (points.Count < 2)
        {
            if (points.Count == 1)
            {
                map.CenterOn(points[0]);
            }
            return;
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        var padding = Math.Max(maxX - minX, maxY - minY) * paddingPercent;
        var extent = new MapBuilderTestExtent
        {
            MinX = minX - padding,
            MinY = minY - padding,
            MaxX = maxX + padding,
            MaxY = maxY + padding
        };

        map.ZoomToBox(extent);
    }

    public void CenterOnLocation(MapBuilderTestMap map, double latitude, double longitude, int? zoomLevel = null)
    {
        var x = longitude * 20037508.34 / 180;
        var y = Math.Log(Math.Tan((90 + latitude) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;

        map.CenterOn(new MapBuilderTestMPoint(x, y));

        if (zoomLevel.HasValue)
        {
            const double maxResolution = 156543.03392;
            var resolution = maxResolution / Math.Pow(2, zoomLevel.Value);
            map.ZoomToResolution(resolution);
        }
    }

    public MapBuilderTestWritableLayer? GetLayer(MapBuilderTestMap map, string layerName)
    {
        return map.Layers.OfType<MapBuilderTestWritableLayer>().FirstOrDefault(l => l.Name == layerName);
    }

    public (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? GetViewportBounds(MapBuilderTestMap map)
    {
        var extent = map.ViewportExtent;
        if (extent == null)
            return null;

        // Convert from Web Mercator to lon/lat
        var minLonLat = ToLonLat(extent.MinX, extent.MinY);
        var maxLonLat = ToLonLat(extent.MaxX, extent.MaxY);

        var zoomLevel = CalculateZoomLevel(map.ViewportResolution);

        return (minLonLat.lon, minLonLat.lat, maxLonLat.lon, maxLonLat.lat, zoomLevel);
    }

    /// <summary>
    /// Converts Web Mercator coordinates to lon/lat.
    /// </summary>
    private static (double lon, double lat) ToLonLat(double x, double y)
    {
        const double earthRadius = 6378137.0;
        var lon = x / earthRadius * (180.0 / Math.PI);
        var lat = (2.0 * Math.Atan(Math.Exp(y / earthRadius)) - Math.PI / 2.0) * (180.0 / Math.PI);
        return (lon, lat);
    }

    /// <summary>
    /// Calculates the approximate web map zoom level from resolution.
    /// </summary>
    private static double CalculateZoomLevel(double resolution)
    {
        const double maxResolution = 156543.03392; // Zoom level 0
        return Math.Log2(maxResolution / resolution);
    }
}

#endregion
