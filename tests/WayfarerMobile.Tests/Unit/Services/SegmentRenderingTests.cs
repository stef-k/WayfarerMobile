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

    [Theory]
    [InlineData("walking")]
    [InlineData("ferry")]
    [InlineData("flight")]
    public void PreviouslyDashedModes_HaveAnUnbrokenBase(string mode)
    {
        var (segment, _, viewport) = Train(10);
        segment.TransportMode = mode;
        using var layer = new WritableLayer { Style = null };
        _service.UpdateTripSegments(layer, [segment]);
        var feature = Assert.Single(layer.GetFeatures());
        var style = Assert.IsType<VectorStyle>(Assert.Single(feature.Styles));
        Assert.NotNull(style.Line);
        Assert.Equal(PenStyle.Solid, style.Line.PenStyle);
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
            Assert.Equal(Color.Black, styles[0].Line!.Color);
            Assert.Equal(Color.White, styles[1].Line!.Color);
            Assert.Equal(4, styles[0].Line!.Width);
            Assert.Equal(2, styles[1].Line!.Width);
            var screen = arm.Coordinates.Select(p => viewport.WorldToScreen(p.X, p.Y)).ToArray();
            Assert.InRange(screen.Max(p => p.X) - screen.Min(p => p.X) + 4, 0, 24);
            Assert.InRange(screen.Max(p => p.Y) - screen.Min(p => p.Y) + 4, 0, 24);
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
        Assert.Equal(Color.FromArgb(220, 156, 39, 176), style.Line.Color);
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

    private static SKBitmap Render(Viewport viewport, ILayer[] layers)
    {
        using var map = new Mapsui.Map();
        using var stream = new MapRenderer().RenderToBitmapStream(viewport, layers, map.RenderService, Color.White);
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
