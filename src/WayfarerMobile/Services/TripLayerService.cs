using System.Collections.Concurrent;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Shared.Utilities;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing trip-related map layers.
/// Handles trip place markers and segment polylines.
/// Registered as Singleton - has icon cache state.
/// </summary>
/// <remarks>
/// Icon cache is dynamic - icons are loaded on-demand from app resources.
/// Cache grows based on unique icon/color combinations actually used.
/// Each entry is a base64-encoded PNG (~1-2KB per icon on disk).
/// Typical usage: 50-100 unique combinations = ~5-10MB in memory.
/// Cache is cleared when app closes (not persisted).
/// </remarks>
public class TripLayerService : ITripLayerService
{
    private readonly ILogger<TripLayerService> _logger;

    /// <summary>
    /// Thread-safe cache for colorized icon base64 strings.
    /// Key format: "{color}/{icon}" (e.g., "bg-blue/marker").
    /// Dynamic - grows based on icons actually used during session.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _iconImageCache = new();

    /// <summary>
    /// Creates a new instance of TripLayerService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public TripLayerService(ILogger<TripLayerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string TripPlacesLayerName => "TripPlaces";

    /// <inheritdoc />
    public string TripSegmentsLayerName => "TripSegments";

    /// <inheritdoc />
    public async Task<List<MPoint>> UpdateTripPlacesAsync(WritableLayer layer, IEnumerable<TripPlace> places)
    {
        layer.Clear();

        var points = new List<MPoint>();

        foreach (var place in places)
        {
            if (place.Latitude == 0 && place.Longitude == 0)
                continue;

            var (x, y) = SphericalMercator.FromLonLat(place.Longitude, place.Latitude);
            var point = new MPoint(x, y);
            points.Add(point);

            // Try to get custom icon style, fallback to colored marker
            var style = await CreatePlaceMarkerStyleAsync(place.Icon, place.MarkerColor);

            // Add marker
            var markerPoint = new Point(point.X, point.Y);
            var feature = new GeometryFeature(markerPoint)
            {
                Styles = new[] { style }
            };

            // Add properties for tap identification
            feature["PlaceId"] = place.Id;
            feature["Name"] = place.Name ?? "";

            layer.Add(feature);
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {PlaceCount} trip place markers", points.Count);

        return points;
    }

    /// <inheritdoc />
    public void ClearTripPlaces(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    /// <inheritdoc />
    public void UpdateTripSegments(WritableLayer layer, IEnumerable<TripSegment> segments)
    {
        layer.Clear();

        var segmentCount = 0;
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Geometry))
                continue;

            try
            {
                // Decode polyline to coordinates
                var points = PolylineDecoder.Decode(segment.Geometry);
                if (points.Count < 2)
                    continue;

                // Convert to map coordinates
                var coordinates = points
                    .Select(p =>
                    {
                        var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                        return new Coordinate(x, y);
                    })
                    .ToArray();

                // Create line feature with transport mode style
                var lineString = new LineString(coordinates);
                var style = CreateSegmentStyle(segment.TransportMode);

                var feature = new GeometryFeature(lineString)
                {
                    Styles = new[] { style }
                };

                // Add properties for identification
                feature["SegmentId"] = segment.Id;
                feature["TransportMode"] = segment.TransportMode ?? "";

                layer.Add(feature);
                segmentCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode segment geometry for segment {SegmentId}", segment.Id);
            }
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {SegmentCount} trip segments", segmentCount);
    }

    /// <inheritdoc />
    public void ClearTripSegments(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    #region Priority Icons Validation

    /// <summary>
    /// Cache of validated priority icons.
    /// </summary>
    private string[]? _validatedPriorityIcons;

    /// <inheritdoc />
    public async Task<string[]> GetValidatedPriorityIconsAsync(string? color = null)
    {
        if (_validatedPriorityIcons != null)
            return _validatedPriorityIcons;

        var validColor = IconCatalog.CoerceColor(color);
        var validIcons = new List<string>();

        foreach (var icon in IconCatalog.PriorityIconNames)
        {
            var resourcePath = IconCatalog.GetIconResourcePath(icon, validColor);
            if (await IconExistsAsync(resourcePath))
            {
                validIcons.Add(icon);
            }
        }

        _validatedPriorityIcons = validIcons.ToArray();
        _logger.LogDebug("Validated {Count}/{Total} priority icons exist",
            _validatedPriorityIcons.Length, IconCatalog.PriorityIconNames.Length);

        return _validatedPriorityIcons;
    }

    /// <summary>
    /// Checks if an icon resource exists in app package.
    /// </summary>
    private static async Task<bool> IconExistsAsync(string resourcePath)
    {
        try
        {
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
            return stream != null;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Place Marker Styles

    /// <summary>
    /// Creates a marker style for a place with custom icon.
    /// </summary>
    private async Task<IStyle> CreatePlaceMarkerStyleAsync(string? iconName, string? markerColor)
    {
        try
        {
            // Get valid icon and color
            var validIcon = IconCatalog.CoerceIcon(iconName);
            var validColor = IconCatalog.CoerceColor(markerColor);
            var cacheKey = $"{validColor}/{validIcon}";

            // Check cache for image source
            if (_iconImageCache.TryGetValue(cacheKey, out var cachedImageSource))
            {
                return new ImageStyle
                {
                    Image = cachedImageSource,
                    SymbolScale = 1.1,
                    Offset = new Offset(0, -16),
                    Opacity = 0.9f
                };
            }

            // Load icon from resources
            var resourcePath = IconCatalog.GetIconResourcePath(validIcon, validColor);
            using var stream = await FileSystem.Current.OpenAppPackageFileAsync(resourcePath);

            if (stream != null)
            {
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var bytes = memoryStream.ToArray();

                // Mapsui 5.0: Use base64-content:// scheme
                var base64 = Convert.ToBase64String(bytes);
                var imageSource = $"base64-content://{base64}";
                _iconImageCache[cacheKey] = imageSource;

                return new ImageStyle
                {
                    Image = imageSource,
                    SymbolScale = 1.1,
                    Offset = new Offset(0, -16),
                    Opacity = 0.9f
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load icon {Icon} with color {Color}", iconName, markerColor);
        }

        // Fallback to colored ellipse marker
        var hexColor = IconCatalog.GetHexColor(markerColor);
        return CreateColoredMarkerStyle(hexColor);
    }

    /// <summary>
    /// Creates a simple colored marker style as fallback.
    /// </summary>
    private static IStyle CreateColoredMarkerStyle(string hexColor)
    {
        var color = MapsuiColorHelper.ParseHexColor(hexColor);
        return new SymbolStyle
        {
            SymbolScale = 0.6,
            Fill = new Brush(color),
            Outline = new Pen(Color.White, 2),
            SymbolType = SymbolType.Ellipse
        };
    }

    #endregion

    #region Segment Styles

    /// <summary>
    /// Creates a style for a segment based on transport mode.
    /// </summary>
    private static IStyle CreateSegmentStyle(string? transportMode)
    {
        var (color, width, dashPattern) = GetSegmentStyleParameters(transportMode?.ToLowerInvariant());

        var pen = new Pen(color, width)
        {
            PenStrokeCap = PenStrokeCap.Round,
            StrokeJoin = StrokeJoin.Round
        };

        if (dashPattern != null)
        {
            pen.PenStyle = PenStyle.UserDefined;
            pen.DashArray = dashPattern;
        }

        return new VectorStyle
        {
            Line = pen
        };
    }

    /// <summary>
    /// Gets style parameters for a given transport mode.
    /// </summary>
    private static (Color color, double width, float[]? dashPattern) GetSegmentStyleParameters(string? mode)
    {
        return mode switch
        {
            // Driving - blue solid line
            "driving" or "car" => (Color.FromArgb(220, 66, 133, 244), 4, null),

            // Walking - green dashed line
            "walking" or "walk" or "foot" => (Color.FromArgb(220, 76, 175, 80), 3, new float[] { 8, 4 }),

            // Cycling - orange solid line
            "cycling" or "bicycle" or "bike" => (Color.FromArgb(220, 255, 152, 0), 3, null),

            // Transit/Public transport - purple solid line
            "transit" or "bus" or "train" or "subway" => (Color.FromArgb(220, 156, 39, 176), 4, null),

            // Ferry/Boat - teal dashed line
            "ferry" or "boat" => (Color.FromArgb(220, 0, 150, 136), 3, new float[] { 12, 6 }),

            // Flight - light blue dotted line
            "flight" or "plane" or "air" => (Color.FromArgb(180, 3, 169, 244), 2, new float[] { 4, 4 }),

            // Default - gray solid line
            _ => (Color.FromArgb(200, 158, 158, 158), 3, null)
        };
    }

    #endregion
}
