using Microsoft.Extensions.Logging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Font = Mapsui.Styles.Font;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Service for displaying cache coverage as visual overlay on the map.
/// Shows circles indicating tile cache coverage at different zoom levels.
/// </summary>
public class CacheOverlayService
{
    private const string CacheOverlayLayerName = "CacheOverlay";
    private const string CacheLabelsLayerName = "CacheLabels";

    private readonly ILogger<CacheOverlayService> _logger;
    private readonly LiveTileCacheService _liveTileCache;
    private WritableLayer? _overlayLayer;
    private WritableLayer? _labelsLayer;

    /// <summary>
    /// Gets whether the cache overlay is currently visible.
    /// </summary>
    public bool IsVisible { get; private set; }

    /// <summary>
    /// Creates a new instance of CacheOverlayService.
    /// </summary>
    public CacheOverlayService(
        ILogger<CacheOverlayService> logger,
        LiveTileCacheService liveTileCache)
    {
        _logger = logger;
        _liveTileCache = liveTileCache;
    }

    /// <summary>
    /// Toggles the cache overlay visibility.
    /// </summary>
    /// <param name="map">The Mapsui map instance.</param>
    /// <param name="latitude">Current latitude.</param>
    /// <param name="longitude">Current longitude.</param>
    /// <returns>True if overlay is now visible, false if hidden.</returns>
    public async Task<bool> ToggleOverlayAsync(Map map, double latitude, double longitude)
    {
        if (IsVisible)
        {
            HideOverlay(map);
            return false;
        }
        else
        {
            await ShowOverlayAsync(map, latitude, longitude);
            return true;
        }
    }

    /// <summary>
    /// Shows the cache overlay on the map.
    /// </summary>
    public async Task ShowOverlayAsync(Map map, double latitude, double longitude)
    {
        try
        {
            // Remove existing overlay first
            HideOverlay(map);

            // Create overlay layer
            _overlayLayer = new WritableLayer { Name = CacheOverlayLayerName };
            _labelsLayer = new WritableLayer { Name = CacheLabelsLayerName };

            // Calculate and display cache coverage for each zoom level
            var zoomLevels = new[] { 12, 13, 14, 15, 16, 17 };

            foreach (var zoom in zoomLevels)
            {
                var coverage = await CalculateCoverageAsync(latitude, longitude, zoom);
                var feature = CreateCoverageFeature(latitude, longitude, zoom, coverage);
                if (feature != null)
                {
                    _overlayLayer.Add(feature);
                }

                var labelFeature = CreateLabelFeature(latitude, longitude, zoom, coverage);
                if (labelFeature != null)
                {
                    _labelsLayer.Add(labelFeature);
                }
            }

            // Add layers to map
            map.Layers.Add(_overlayLayer);
            map.Layers.Add(_labelsLayer);

            IsVisible = true;
            _logger.LogInformation("Cache overlay displayed for location {Lat}, {Lon}", latitude, longitude);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing cache overlay");
        }
    }

    /// <summary>
    /// Hides the cache overlay from the map.
    /// </summary>
    public void HideOverlay(Map map)
    {
        try
        {
            var overlayLayer = map.Layers.FirstOrDefault(l => l.Name == CacheOverlayLayerName);
            if (overlayLayer != null)
            {
                map.Layers.Remove(overlayLayer);
            }

            var labelsLayer = map.Layers.FirstOrDefault(l => l.Name == CacheLabelsLayerName);
            if (labelsLayer != null)
            {
                map.Layers.Remove(labelsLayer);
            }

            _overlayLayer = null;
            _labelsLayer = null;
            IsVisible = false;

            _logger.LogInformation("Cache overlay hidden");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hiding cache overlay");
        }
    }

    /// <summary>
    /// Updates the cache overlay for a new location.
    /// </summary>
    public async Task UpdateOverlayAsync(Map map, double latitude, double longitude)
    {
        if (!IsVisible) return;

        await ShowOverlayAsync(map, latitude, longitude);
    }

    private async Task<CoverageResult> CalculateCoverageAsync(double lat, double lon, int zoom)
    {
        var (centerX, centerY) = LatLonToTile(lat, lon, zoom);
        const int radius = 3;

        int total = 0;
        int cached = 0;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (x < 0 || y < 0 || y >= (1 << zoom))
                    continue;

                total++;
                var tile = await _liveTileCache.GetCachedTileAsync(zoom, x, y);
                if (tile != null)
                    cached++;
            }
        }

        return new CoverageResult
        {
            TotalTiles = total,
            CachedTiles = cached,
            CoveragePercent = total > 0 ? (double)cached / total : 0
        };
    }

    private GeometryFeature? CreateCoverageFeature(double lat, double lon, int zoom, CoverageResult coverage)
    {
        try
        {
            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            var radius = GetRadiusForZoom(zoom);

            // Create circle geometry
            var factory = new GeometryFactory();
            var center = factory.CreatePoint(new Coordinate(x, y));
            var circle = center.Buffer(radius, 32);

            // Determine color based on coverage
            var color = GetColorForCoverage(coverage.CoveragePercent);

            var style = new VectorStyle
            {
                Fill = new Brush(Color.FromArgb(64, color.R, color.G, color.B)),
                Outline = new Pen(Color.FromArgb(180, color.R, color.G, color.B), 2),
                Opacity = 0.6f
            };

            return new GeometryFeature(circle) { Styles = new[] { style } };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating coverage feature for zoom {Zoom}", zoom);
            return null;
        }
    }

    private GeometryFeature? CreateLabelFeature(double lat, double lon, int zoom, CoverageResult coverage)
    {
        try
        {
            var (x, y) = SphericalMercator.FromLonLat(lon, lat);
            var radius = GetRadiusForZoom(zoom);

            // Offset labels to avoid overlap
            var angle = (zoom - 12) * 60.0 * Math.PI / 180.0;
            var offsetX = Math.Cos(angle) * radius * 0.7;
            var offsetY = Math.Sin(angle) * radius * 0.7;

            var factory = new GeometryFactory();
            var labelPoint = factory.CreatePoint(new Coordinate(x + offsetX, y + offsetY));

            var labelStyle = new LabelStyle
            {
                Text = $"Z{zoom}: {coverage.CoveragePercent:P0}",
                ForeColor = Color.White,
                BackColor = new Brush(Color.FromArgb(200, 0, 0, 0)),
                Font = new Font { FontFamily = "Arial", Size = 12, Bold = true },
                HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
                VerticalAlignment = LabelStyle.VerticalAlignmentEnum.Center,
                Offset = new Offset(0, 0)
            };

            return new GeometryFeature(labelPoint) { Styles = new[] { labelStyle } };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating label feature for zoom {Zoom}", zoom);
            return null;
        }
    }

    private static double GetRadiusForZoom(int zoom)
    {
        // Radius decreases with higher zoom levels
        return zoom switch
        {
            12 => 3000,
            13 => 2000,
            14 => 1500,
            15 => 1000,
            16 => 700,
            17 => 500,
            _ => 1000
        };
    }

    private static Color GetColorForCoverage(double coverage)
    {
        return coverage switch
        {
            >= 0.9 => Color.FromArgb(255, 76, 175, 80),   // Green
            >= 0.5 => Color.FromArgb(255, 255, 193, 7),  // Yellow/Amber
            >= 0.2 => Color.FromArgb(255, 255, 152, 0),  // Orange
            _ => Color.FromArgb(255, 244, 67, 54)         // Red
        };
    }

    private static (int x, int y) LatLonToTile(double lat, double lon, int zoom)
    {
        int x = (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));
        return (x, y);
    }

    private class CoverageResult
    {
        public int TotalTiles { get; set; }
        public int CachedTiles { get; set; }
        public double CoveragePercent { get; set; }
    }
}
