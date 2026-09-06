using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using SkiaSharp;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using Xunit.Abstractions;

namespace WayfarerMobile.Tests.Unit.Rendering;

public class SegmentRenderingTests(ITestOutputHelper output)
{
    private readonly TripLayerService _service = new(NullLogger<TripLayerService>.Instance);

    [Fact]
    public void MissingApiGeometry_AfterOfflineReconstruction_RetainsOrdinaryLineAndUnavailableCounts()
    {
        // Original missing-line evidence is retained at checkpoint 8ed0ff4; now prove the approved exception.
        var (_, places, viewport) = Train(10);
        var json = $$"""
            {"id":"11111111-1111-1111-1111-111111111111","mode":"train",
             "fromPlaceId":"{{places[0].Id}}","toPlaceId":"{{places[1].Id}}",
             "routeJson":null,"waypoints":[],"hasCustomRoute":false}
            """;
        var downloaded = System.Text.Json.JsonSerializer.Deserialize<TripSegment>(json)!;
        var restored = WayfarerMobile.Core.Helpers.OfflineSegmentWaypointMapper.Reconstruct(
            downloaded.Id, downloaded.OriginId!.Value, downloaded.DestinationId!.Value,
            downloaded.Geometry, SegmentWaypointJson.Serialize(downloaded.Waypoints), downloaded.HasCustomRoute);
        Assert.Null(restored.Geometry);
        Assert.Empty(restored.Waypoints);
        using var lines = new WritableLayer { Style = null };
        using var cues = new WritableLayer { Style = null };
        using var badges = new WritableLayer { Style = null };
        _service.UpdateTripSegments(lines, [restored], places);
        var line = Assert.IsType<GeometryFeature>(Assert.Single(lines.GetFeatures()));
        Assert.Equal(Color.FromArgb(220, 13, 110, 253),
            Assert.IsType<VectorStyle>(Assert.Single(line.Styles)).Line!.Color);
        var geometry = Assert.IsType<LineString>(line.Geometry);
        var start = viewport.WorldToScreen(geometry.StartPoint.X, geometry.StartPoint.Y);
        var end = viewport.WorldToScreen(geometry.EndPoint.X, geometry.EndPoint.Y);
        using var ordinary = Render(viewport, [lines]);
        for (var x = (int)start.X + 3; x < (int)end.X - 3; x++)
            Assert.NotEqual(SKColors.White, ordinary.GetPixel(x, 200));
        _service.UpdateSelectedSegmentDecorations(badges, cues, restored, places, viewport, false);
        Assert.Same(line, Assert.Single(lines.GetFeatures()));
        using var selected = Render(viewport, [lines, cues, badges]);
        Assert.Equal(SKColor.Parse("#0057b8"), selected.GetPixel((int)start.X + 7, 166));
        Assert.Equal(ordinary.GetPixel(270, 200), selected.GetPixel(270, 200));
        Assert.Equal(ordinary.GetPixel(270, 201), selected.GetPixel(270, 201));
        Assert.Equal(2, badges.GetFeatures().Count());
        foreach (var badge in badges.GetFeatures())
        {
            var style = Assert.IsType<ImageStyle>(Assert.Single(badge.Styles));
            Assert.Equal(0.5, style.SymbolScale);
            Assert.NotNull(style.Image);
            using var image = SKBitmap.Decode(Convert.FromBase64String(style.Image.Source["base64-content://".Length..]));
            Assert.Equal(48, image.Width);
            Assert.Equal(48, image.Height);
            Assert.Equal(SKColor.Parse("#0057b8"), image.GetPixel(24, 8));
            Assert.Contains(image.Pixels, pixel => pixel == SKColors.White);
        }
        Assert.NotEmpty(cues.GetFeatures());
        var details = WayfarerMobile.Core.Helpers.SegmentPresentationProjector.Project(restored, places);
        Assert.Equal("Waypoint count unavailable", details.WaypointCountText);
        Assert.Equal("Route points unavailable", details.RoutePointCountText);
        Assert.Equal("Straight endpoint connection — route geometry unavailable", details.GeometryDescription);
        Assert.Null(restored.Geometry);
    }

    [Theory]
    [InlineData("missing endpoint")]
    [InlineData("invalid endpoint")]
    [InlineData("custom route")]
    [InlineData("intermediate entries")]
    public void AbsentGeometry_WithoutApprovedEndpointContract_HasNoConnection(string unavailable)
    {
        var (segment, places, viewport) = Train(10);
        segment.Geometry = null;
        segment.HasCustomRoute = unavailable == "custom route";
        if (unavailable == "missing endpoint") segment.DestinationId = Guid.NewGuid();
        if (unavailable == "invalid endpoint") places[1].Latitude = double.NaN;
        if (unavailable == "intermediate entries")
            segment.Waypoints = [new TripSegmentWaypoint { PlaceId = Guid.NewGuid(), Position = 0 }];
        using var lines = new WritableLayer { Style = null };
        using var cues = new WritableLayer { Style = null };
        using var badges = new WritableLayer { Style = null };
        _service.UpdateTripSegments(lines, [segment], places);
        _service.UpdateSelectedSegmentDecorations(badges, cues, segment, places, viewport, false);
        Assert.Empty(lines.GetFeatures());
        Assert.Empty(cues.GetFeatures());
        Assert.Null(WayfarerMobile.Core.Helpers.SegmentPresentationProjector.Project(segment, places).GeometryDescription);
        Assert.Null(segment.Geometry);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"type\":\"MultiLineString\",\"coordinates\":[[[1,1],[1.04,1]]]}")]
    public void RejectedGeometry_IsNeverReplacedByEndpointConnection(string geometry)
    {
        var (segment, places, viewport) = Train(10);
        segment.HasCustomRoute = false;
        segment.Geometry = geometry;
        using var lines = new WritableLayer { Style = null };
        using var cues = new WritableLayer { Style = null };
        using var badges = new WritableLayer { Style = null };
        _service.UpdateTripSegments(lines, [segment], places);
        _service.UpdateSelectedSegmentDecorations(badges, cues, segment, places, viewport, false);
        Assert.Empty(lines.GetFeatures());
        Assert.Empty(cues.GetFeatures());
        Assert.Null(WayfarerMobile.Core.Helpers.SegmentPresentationProjector.Project(segment, places).GeometryDescription);
        Assert.Equal(geometry, segment.Geometry);
    }

    [Theory]
    [InlineData("walking")]
    [InlineData("driving")]
    [InlineData("train")]
    [InlineData("cycling")]
    [InlineData("unknown")]
    [InlineData("ferry")]
    [InlineData("flight")]
    public void AllModes_HaveAnUnbrokenBlueBase(string mode)
    {
        var (segment, _, viewport) = Train(10);
        segment.TransportMode = mode;
        using var layer = new WritableLayer { Style = null };
        _service.UpdateTripSegments(layer, [segment]);
        var feature = Assert.Single(layer.GetFeatures());
        var style = Assert.IsType<VectorStyle>(Assert.Single(feature.Styles));
        Assert.NotNull(style.Line);
        Assert.Equal(PenStyle.Solid, style.Line.PenStyle);
        Assert.Equal(Color.FromArgb(220, 13, 110, 253), style.Line.Color);
        using var bitmap = Render(viewport, [layer]);
        for (var x = 101; x < 539; x++) Assert.NotEqual(SKColors.White, bitmap.GetPixel(x, 200));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 37)]
    public void BentRoute_OpenContrastingCuesFollowResolvedTangent(bool reversed, double rotation)
    {
        var (segment, places, viewport) = Train(10);
        viewport = viewport with { Rotation = rotation };
        segment.Geometry = reversed
            ? """{"type":"LineString","coordinates":[[1.04,1],[1.02,1.01],[1,1]]}"""
            : """{"type":"LineString","coordinates":[[1,1],[1.02,1.01],[1.04,1]]}""";
        if (reversed) (segment.OriginId, segment.DestinationId) = (segment.DestinationId, segment.OriginId);
        var via = new TripPlace { Id = Guid.NewGuid(), Latitude = 1.01, Longitude = 1.02 };
        places = [.. places, via];
        segment.Waypoints = [new TripSegmentWaypoint { PlaceId = via.Id, Position = 0, RouteVertexIndex = 1 }];
        using var lines = new WritableLayer { Style = null };
        using var cues = new WritableLayer { Style = null };
        using var badges = new WritableLayer { Style = null };
        _service.UpdateTripSegments(lines, [segment]);
        _service.UpdateSelectedSegmentDecorations(badges, cues, segment, places, viewport, false);
        var route = Assert.IsType<LineString>(Assert.IsType<GeometryFeature>(Assert.Single(lines.GetFeatures())).Geometry);
        var tips = new List<MPoint>();
        Assert.InRange(cues.GetFeatures().Count(), 1, 8);
        foreach (var feature in cues.GetFeatures())
        {
            var arm = Assert.IsType<LineString>(Assert.IsType<GeometryFeature>(feature).Geometry);
            Assert.Equal(3, arm.NumPoints);
            Assert.False(arm.IsClosed);
            var styles = feature.Styles.Cast<VectorStyle>().ToArray();
            Assert.Equal(2, styles.Length);
            Assert.Equal(Color.White, styles[0].Line!.Color);
            Assert.Equal(Color.FromString("#852D10"), styles[1].Line!.Color);
            Assert.Equal(4, styles[0].Line!.Width);
            Assert.Equal(2, styles[1].Line!.Width);
            var screen = arm.Coordinates.Select(p => viewport.WorldToScreen(p.X, p.Y)).ToArray();
            Assert.InRange(screen.Max(p => p.X) - screen.Min(p => p.X) + 4, 0, 24);
            Assert.InRange(screen.Max(p => p.Y) - screen.Min(p => p.Y) + 4, 0, 24);
            foreach (var place in places)
            {
                var world = SphericalMercator.FromLonLat(place.Longitude, place.Latitude);
                var marker = viewport.WorldToScreen(world.x, world.y);
                Assert.False(screen.Min(p => p.X) - 2 < marker.X + 24 && screen.Max(p => p.X) + 2 > marker.X - 24 &&
                    screen.Min(p => p.Y) - 2 < marker.Y + 24 && screen.Max(p => p.Y) + 2 > marker.Y - 48);
            }
            var tip = arm.GetPointN(1);
            Assert.True(route.Distance(tip) < 0.000001, "Tip must stay on its own route");
            var edge = Enumerable.Range(1, route.NumPoints - 1)
                .Select(i => new LineString([route.GetCoordinateN(i - 1), route.GetCoordinateN(i)]))
                .MinBy(line => line.Distance(tip))!;
            var dx = edge.EndPoint.X - edge.StartPoint.X;
            var dy = edge.EndPoint.Y - edge.StartPoint.Y;
            var cueX = tip.X - (arm.StartPoint.X + arm.EndPoint.X) / 2;
            var cueY = tip.Y - (arm.StartPoint.Y + arm.EndPoint.Y) / 2;
            Assert.True(dx * cueX + dy * cueY > 0, "Cue must follow the resolved local tangent");
            Assert.InRange(Math.Abs(dx * cueY - dy * cueX) / Math.Sqrt(dx * dx + dy * dy), 0, 0.000001);
            Assert.All(tips, prior => Assert.True(Math.Sqrt(Math.Pow(prior.X - screen[1].X, 2) + Math.Pow(prior.Y - screen[1].Y, 2)) >= 72));
            tips.Add(new MPoint(screen[1].X, screen[1].Y));
        }
        using var bitmap = Render(viewport, [lines, cues, badges]);
        SaveObservation(bitmap, $"bent-{reversed}-{rotation}");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public void TwoPointTrain_OrdinaryStrokeRendersBeforeSelection(double resolution)
    {
        var (segment, places, viewport) = Train(resolution);
        using var lines = new WritableLayer { Name = "TripSegments", Style = null };
        using var cues = new WritableLayer { Name = "SelectedSegmentChevrons", Style = null };
        using var badges = new WritableLayer { Name = "SelectedSegmentBadges", Style = null };
        _service.UpdateTripSegments(lines, [segment]);
        var feature = Assert.IsType<GeometryFeature>(Assert.Single(lines.GetFeatures()));
        var geometry = Assert.IsType<LineString>(feature.Geometry);
        var style = Assert.IsType<VectorStyle>(Assert.Single(feature.Styles));
        Assert.NotNull(style.Line);
        Assert.Equal(2, geometry.NumPoints);
        Assert.Equal(4, style.Line.Width);
        Assert.Equal(Color.FromArgb(220, 13, 110, 253), style.Line.Color);
        using var ordinary = Render(viewport, [lines]);
        var start = viewport.WorldToScreen(geometry.StartPoint.X, geometry.StartPoint.Y);
        var end = viewport.WorldToScreen(geometry.EndPoint.X, geometry.EndPoint.Y);
        for (var x = (int)start.X + 3; x < (int)end.X - 3; x++)
            Assert.NotEqual(SKColors.White, ordinary.GetPixel(x, 200));
        output.WriteLine($"Geometry {geometry}; resolution {resolution}; viewport 640x400, rotation 0, density 1; screen {start} to {end}; alpha {style.Line.Color.A}, width {style.Line.Width}, pen {style.Line.PenStyle}, layer opacity {lines.Opacity}, style opacity {style.Opacity}.");
        SaveObservation(ordinary, $"ordinary-{resolution}");
        _service.UpdateSelectedSegmentDecorations(badges, cues, segment, places, viewport, false);
        Assert.NotEmpty(cues.GetFeatures());
        using var selected = Render(viewport, [lines, cues, badges]);
        SaveObservation(selected, $"selected-{resolution}");
        using var dark = Render(viewport, [lines, cues, badges], Color.FromArgb(255, 32, 32, 32));
        SaveObservation(dark, $"selected-dark-{resolution}");
        var cue = Assert.IsType<LineString>(Assert.IsType<GeometryFeature>(cues.GetFeatures().First()).Geometry);
        var projected = cue.Coordinates.Select(p => viewport.WorldToScreen(p.X, p.Y)).ToArray();
        Assert.Equal(10, projected.Max(p => p.X) - projected.Min(p => p.X), 6);
        Assert.Equal(10, projected.Max(p => p.Y) - projected.Min(p => p.Y), 6);
        AssertCueContrast(selected, projected[1].X, projected[1].Y);
        AssertCueContrast(dark, projected[1].X, projected[1].Y);
        output.WriteLine($"Selected layer order: TripSegments, SelectedSegmentChevrons, SelectedSegmentBadges; cue count {cues.GetFeatures().Count()}. Production Places remain above these layers.");
    }

    private static (TripSegment Segment, TripPlace[] Places, Viewport Viewport) Train(double resolution)
    {
        var start = new TripPlace { Id = Guid.NewGuid(), Latitude = 1, Longitude = 1 };
        var end = new TripPlace { Id = Guid.NewGuid(), Latitude = 1, Longitude = 1.04 };
        var center = SphericalMercator.FromLonLat(1.02, 1);
        return (new TripSegment
        {
            Id = Guid.NewGuid(), OriginId = start.Id, DestinationId = end.Id,
            TransportMode = "train", HasCustomRoute = true,
            Geometry = """{"type":"LineString","coordinates":[[1,1],[1.04,1]]}"""
        }, [start, end], new Viewport(center.x, center.y, resolution, 0, 640, 400));
    }

    private static void AssertCueContrast(SKBitmap bitmap, double tipX, double tipY)
    {
        var pixels = new List<SKColor>();
        for (var x = (int)tipX - 11; x <= tipX; x++)
            for (var y = (int)tipY - 6; y <= (int)tipY + 6; y++)
                pixels.Add(bitmap.GetPixel(x, y));
        Assert.Contains(pixels, p => p.Red is > 100 and < 160 && p.Green is > 20 and < 70 && p.Blue < 40);
        Assert.Contains(pixels, p => p.Red > 230 && p.Green > 230 && p.Blue > 230);
    }

    private static SKBitmap Render(Viewport viewport, ILayer[] layers, Color? background = null)
    {
        using var map = new Mapsui.Map();
        var sources = layers.OfType<WritableLayer>().SelectMany(layer => layer.GetFeatures())
            .SelectMany(feature => feature.Styles).OfType<ImageStyle>().Select(style => style.Image!.Source).ToHashSet();
        var images = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
            Mapsui.Styles.Image.SourceToSourceId.Where(entry => sources.Contains(entry.Key)));
        map.RenderService.ImageSourceCache.FetchAllImageDataAsync(images).GetAwaiter().GetResult();
        using var stream = new MapRenderer().RenderToBitmapStream(viewport, layers, map.RenderService, background ?? Color.White);
        return SKBitmap.Decode(stream.ToArray());
    }

    private static void SaveObservation(SKBitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("SEGMENT_RENDER_EVIDENCE");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        data.SaveTo(file);
    }
}
