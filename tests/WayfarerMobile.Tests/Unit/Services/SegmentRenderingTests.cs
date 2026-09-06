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
