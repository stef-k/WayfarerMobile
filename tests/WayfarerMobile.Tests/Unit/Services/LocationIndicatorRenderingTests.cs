using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Rendering.Skia;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using SkiaSharp;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using Xunit.Abstractions;

namespace WayfarerMobile.Tests.Unit.Rendering;

public class LocationIndicatorRenderingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedRenderer_MovesPolygonsAndUpdatesHeadingAndRadiusAtCurrentAnchor(bool transformed)
    {
        using var indicator = new LocationIndicatorService(NullLogger<LocationIndicatorService>.Instance);
        using var service = new LocationLayerService(NullLogger<LocationLayerService>.Instance, indicator);
        using var layer = new WritableLayer { Style = null };
        using var map = new Mapsui.Map();
        var renderer = new MapRenderer();
        var viewport = new Viewport(110, 0, 1, 0, 640, 400);
        if (transformed) viewport = viewport with { CenterX = 95, CenterY = 15, Resolution = 0.8, Rotation = 37 };
        var a = LocationAt(0);
        var b = LocationAt(220);

        // All frames retain the layer, renderer AND RenderService/cache at the same viewport.
        service.UpdateLocation(layer, a);
        AssertAnchors(layer, service, 0, 25);
        using var first = Render("a");
        AssertPaint(first, viewport, 0, 0, true); // Dot.
        AssertPaint(first, viewport, -20, 0, true); // Accuracy only.
        AssertPaint(first, viewport, 0, 40, true); // Cone only (outside accuracy).

        service.UpdateLocation(layer, b);
        AssertAnchors(layer, service, 220, 25);
        using var moved = Render("b");
        AssertPaint(moved, viewport, 220, 0, true);
        // Report each polygon independently before asserting: a dot-only pass is insufficient.
        output.WriteLine($"B accuracy pixel: {Pixel(moved, viewport, 200, 0)}; cone: {Pixel(moved, viewport, 220, 40)}");
        AssertPaint(moved, viewport, 200, 0, true);
        AssertPaint(moved, viewport, 220, 40, true);
        AssertOldPositionEmpty(moved, viewport);

        // Same accepted B coordinates, new GPS heading and radius through production state methods.
        b.Bearing = 90;
        b.Accuracy = 35;
        service.UpdateLocation(layer, b);
        Assert.Equal(90, service.LastHeading);
        AssertAnchors(layer, service, 220, 35);
        using var changed = Render("b-heading-radius");
        AssertPaint(changed, viewport, 220, 0, true);
        AssertPaint(changed, viewport, 190, 0, true); // Expanded circle, outside old radius.
        AssertPaint(changed, viewport, 260, 0, true); // Rotated cone, outside new radius.
        AssertPaint(changed, viewport, 220, 40, false); // Old cone gone.
        AssertOldPositionEmpty(changed, viewport);

        SKBitmap Render(string frame)
        {
            using var stream = renderer.RenderToBitmapStream(viewport, [layer], map.RenderService, Mapsui.Styles.Color.White);
            var bitmap = SKBitmap.Decode(stream.ToArray());
            var directory = Environment.GetEnvironmentVariable("LOCATION_RENDER_EVIDENCE");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var file = File.Create(Path.Combine(directory, $"{transformed}-{frame}.png"));
                data.SaveTo(file);
            }
            return bitmap;
        }
    }

    private static LocationData LocationAt(double x)
    {
        var (longitude, latitude) = SphericalMercator.ToLonLat(x, 0);
        return new LocationData { Longitude = longitude, Latitude = latitude, Accuracy = 25, Bearing = 0, Speed = 2 };
    }

    private void AssertAnchors(WritableLayer layer, LocationLayerService service, double x, double radius)
    {
        var features = layer.GetFeatures().Cast<GeometryFeature>().ToArray();
        Assert.Equal(3, features.Length);
        var dot = Assert.IsType<Point>(features.Single(f => f.Geometry is Point).Geometry);
        Assert.Equal(x, dot.X, 6);
        Assert.Equal(0, dot.Y, 6);
        Assert.Equal(x, service.LastMapPoint!.X, 6);
        var polygons = features.Where(f => f.Geometry is Polygon).Select(f => (Polygon)f.Geometry!).ToArray();
        var circle = polygons.Single(p => p.NumPoints == 37);
        Assert.Equal(dot.X, circle.EnvelopeInternal.Centre.X, 6);
        Assert.Equal(dot.Y, circle.EnvelopeInternal.Centre.Y, 6);
        Assert.All(circle.Coordinates, c => Assert.Equal(radius, c.Distance(dot.Coordinate), 6));
        var cone = polygons.Single(p => p.NumPoints == 27);
        // Midpoints of the inner and outer arcs lie on the same radial ray.
        // Extrapolate their 14/50-unit radii to the construction origin, not the centroid.
        var inner = cone.Coordinates[6];
        var outer = cone.Coordinates[19];
        Assert.Equal(dot.X, (50 * inner.X - 14 * outer.X) / 36, 6);
        Assert.Equal(dot.Y, (50 * inner.Y - 14 * outer.Y) / 36, 6);
        output.WriteLine($"Geometry anchors agree at ({dot.X:F3}, {dot.Y:F3}), radius {radius}; cone construction origin verified.");
    }

    private static SKColor Pixel(SKBitmap bitmap, Viewport viewport, double x, double y)
    {
        var point = viewport.WorldToScreen(x, y);
        return bitmap.GetPixel((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    private static void AssertPaint(SKBitmap bitmap, Viewport viewport, double x, double y, bool painted)
    {
        Assert.Equal(painted, Pixel(bitmap, viewport, x, y) != SKColors.White);
    }

    private static void AssertOldPositionEmpty(SKBitmap bitmap, Viewport viewport)
    {
        // A's whole footprint, with margin, is disjoint from every legitimate B shape.
        for (var x = -60; x <= 60; x++)
            for (var y = -60; y <= 60; y++)
                AssertPaint(bitmap, viewport, x, y, false);
    }
}
