using Microsoft.Extensions.Logging;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Nts.Extensions;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Interfaces;
using Map = Mapsui.Map;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Font = Mapsui.Styles.Font;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Service for displaying cache coverage as visual overlay on the map.
/// Shows circles indicating tile cache coverage at different zoom levels.
/// Uses user-configured prefetch radius from settings.
/// </summary>
public class CacheOverlayService
{
    private const string CacheOverlayLayerName = "CacheOverlay";
    private const string CacheLabelsLayerName = "CacheLabels";

    private readonly ILogger<CacheOverlayService> _logger;
    private readonly LiveTileCacheService _liveTileCache;
    private readonly ISettingsService _settingsService;
    private readonly string _liveCacheDirectory;
    private readonly string _tripCacheDirectory;
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
        LiveTileCacheService liveTileCache,
        ISettingsService settingsService)
    {
        _logger = logger;
        _liveTileCache = liveTileCache;
        _settingsService = settingsService;
        _liveCacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "live");
        _tripCacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "trips");
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

            // Create overlay layer - IMPORTANT: Set Style to null to prevent default style override
            _overlayLayer = new WritableLayer { Name = CacheOverlayLayerName, Style = null };
            _labelsLayer = new WritableLayer { Name = CacheLabelsLayerName, Style = null };

            // Use centralized zoom levels (8-17)
            var zoomLevels = TileCacheConstants.AllZoomLevels;

            // Calculate all coverages in parallel for speed (direct file checks, no DB)
            var coverageTasks = zoomLevels.Select(zoom =>
                Task.Run(() => (zoom, coverage: CalculateCoverageFast(latitude, longitude, zoom)))).ToArray();

            var results = await Task.WhenAll(coverageTasks);

            // Create features from results
            foreach (var (zoom, coverage) in results)
            {
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

            // Zoom out to fit all circles (largest is zoom level 10)
            var largestRadius = GetRadiusForZoom(10);
            var (centerX, centerY) = SphericalMercator.FromLonLat(longitude, latitude);
            var extent = new MRect(
                centerX - largestRadius * 1.2,
                centerY - largestRadius * 1.2,
                centerX + largestRadius * 1.2,
                centerY + largestRadius * 1.2);

            map.Navigator.ZoomToBox(extent, MBoxFit.Fit);

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

    /// <summary>
    /// Fast coverage calculation using direct file checks (no DB updates).
    /// Runs synchronously on background thread for parallel execution.
    /// </summary>
    private CoverageResult CalculateCoverageFast(double lat, double lon, int zoom)
    {
        var (centerX, centerY) = LatLonToTile(lat, lon, zoom);
        int radius = _settingsService.LiveCachePrefetchRadius;
        int maxTiles = 1 << zoom;

        // Cache trip directories once
        string[]? tripDirs = null;
        if (Directory.Exists(_tripCacheDirectory))
        {
            tripDirs = Directory.GetDirectories(_tripCacheDirectory);
        }

        int total = 0;
        int cached = 0;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = centerX + dx;
                int y = centerY + dy;

                if (x < 0 || x >= maxTiles || y < 0 || y >= maxTiles)
                    continue;

                total++;

                // Direct file check - much faster than GetCachedTileAsync
                var livePath = Path.Combine(_liveCacheDirectory, zoom.ToString(), x.ToString(), $"{y}.png");
                if (File.Exists(livePath))
                {
                    cached++;
                    continue;
                }

                // Check trip caches
                if (tripDirs != null)
                {
                    foreach (var tripDir in tripDirs)
                    {
                        var tripPath = Path.Combine(tripDir, zoom.ToString(), x.ToString(), $"{y}.png");
                        if (File.Exists(tripPath))
                        {
                            cached++;
                            break;
                        }
                    }
                }
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

            // Create circle geometry with fewer segments for faster rendering
            var factory = new GeometryFactory();
            var center = factory.CreatePoint(new Coordinate(x, y));
            var circle = center.Buffer(radius, 16); // Reduced from 32 to 16 segments

            // Use ONE consistent blue color for all circles (Material Blue #2196F3)
            // For VectorStyle, transparency MUST be in color alpha (Opacity property is for bitmaps only)
            var fillColor = Color.FromArgb(40, 33, 150, 243);   // ~15% opacity fill
            var strokeColor = Color.FromArgb(120, 33, 150, 243); // ~47% opacity stroke

            var style = new VectorStyle
            {
                Fill = new Brush(fillColor),
                Outline = new Pen(strokeColor, 2)
            };
            // NOTE: Do NOT set style.Opacity - it only works for bitmaps, not vector shapes

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
        // Radius decreases with higher zoom levels (covers all prefetch zoom levels 8-17)
        return zoom switch
        {
            8 => 12000,
            9 => 9000,
            10 => 6000,
            11 => 4500,
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
