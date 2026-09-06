using System.Collections.Concurrent;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
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
    public string TripAreasLayerName => "TripAreas";

    /// <inheritdoc />
    public string TripSegmentsLayerName => "TripSegments";

    /// <inheritdoc />
    public string PlaceSelectionLayerName => "PlaceSelection";
    public string SegmentBadgesLayerName => "SelectedSegmentBadges";
    public string SegmentChevronsLayerName => "SelectedSegmentChevrons";

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
    public void UpdateTripAreas(WritableLayer layer, IEnumerable<TripArea> areas)
    {
        layer.Clear();

        var areaList = areas.ToList();
        _logger.LogDebug("UpdateTripAreas called with {Count} areas", areaList.Count);

        var areaCount = 0;
        foreach (var area in areaList)
        {
            _logger.LogDebug("Processing area '{Name}': GeometryGeoJson={HasGeo}, Boundary={BoundaryCount} pts, Fill={Fill}",
                area.Name,
                !string.IsNullOrEmpty(area.GeometryGeoJson) ? $"yes({area.GeometryGeoJson.Length} chars)" : "null",
                area.Boundary?.Count ?? 0,
                area.FillColor ?? "null");

            if (area.Boundary == null || area.Boundary.Count < 3)
            {
                _logger.LogWarning("Skipping area '{Name}': insufficient boundary points ({Count})",
                    area.Name, area.Boundary?.Count ?? 0);
                continue;
            }

            try
            {
                // Log first few boundary coordinates
                if (area.Boundary.Count > 0)
                {
                    var first = area.Boundary[0];
                    _logger.LogDebug("Area '{Name}' first coord: Lat={Lat}, Lon={Lon}",
                        area.Name, first.Latitude, first.Longitude);
                }

                // Convert boundary coordinates to map coordinates
                var coordinates = area.Boundary
                    .Select(p =>
                    {
                        var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                        return new Coordinate(x, y);
                    })
                    .ToList();

                _logger.LogDebug("Area '{Name}' converted to {Count} map coordinates", area.Name, coordinates.Count);

                // Close the polygon if not already closed
                if (!coordinates[0].Equals(coordinates[^1]))
                {
                    coordinates.Add(coordinates[0]);
                }

                // Create polygon
                var linearRing = new LinearRing(coordinates.ToArray());
                var polygon = new Polygon(linearRing);

                _logger.LogDebug("Area '{Name}' polygon valid: {IsValid}, area: {Area}",
                    area.Name, polygon.IsValid, polygon.Area);

                // Create style with fill and stroke colors
                var style = CreateAreaStyle(area.FillColor, area.StrokeColor);

                var feature = new GeometryFeature(polygon)
                {
                    Styles = new[] { style }
                };

                // Add properties for tap identification
                feature["AreaId"] = area.Id;
                feature["Name"] = area.Name ?? "";

                layer.Add(feature);
                areaCount++;
                _logger.LogDebug("Successfully added area '{Name}' to layer", area.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create polygon for area {AreaId}", area.Id);
            }
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {AreaCount} trip area polygons", areaCount);
    }

    /// <inheritdoc />
    public void ClearTripAreas(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    /// <inheritdoc />
    public void UpdateTripSegments(WritableLayer layer, IEnumerable<TripSegment> segments,
        IReadOnlyCollection<TripPlace>? places = null)
    {
        layer.Clear();

        var segmentList = segments.ToList();
        _logger.LogDebug("UpdateTripSegments called with {Count} segments", segmentList.Count);

        var segmentCount = 0;
        foreach (var segment in segmentList)
        {
            var parseResult = TripSegmentGeometryParser.Parse(segment.Geometry);
            var coordinates = parseResult.Coordinates;
            if (!parseResult.IsSuccess)
            {
                var connection = SegmentDisplayGeometry.ResolveStraightConnection(segment, places ?? []);
                if (connection is null)
                {
                    _logger.LogWarning("Skipping segment {SegmentId}: geometry failure {Failure}", segment.Id, parseResult.Failure);
                    continue;
                }
                coordinates = connection.Geometry.Select(p => (p.Latitude, p.Longitude)).ToList();
            }

            try
            {
                // Convert to map coordinates
                var mapCoordinates = coordinates
                    .Select(p =>
                    {
                        var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
                        return new Coordinate(x, y);
                    })
                    .ToArray();

                // Create line feature with transport mode style
                var lineString = new LineString(mapCoordinates);
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

    /// <inheritdoc />
    public void UpdateSelectedSegmentDecorations(
        WritableLayer badgeLayer,
        WritableLayer chevronLayer,
        TripSegment? segment,
        IReadOnlyCollection<TripPlace> places,
        Viewport viewport,
        bool navigationActive)
    {
        badgeLayer.Clear();
        chevronLayer.Clear();
        if (segment == null || navigationActive)
        {
            badgeLayer.DataHasChanged();
            chevronLayer.DataHasChanged();
            return;
        }

        var parsed = TripSegmentGeometryParser.Parse(segment.Geometry);
        if (!parsed.IsSuccess && parsed.Failure != SegmentGeometryFailure.Empty)
        {
            badgeLayer.DataHasChanged();
            chevronLayer.DataHasChanged();
            return;
        }
        var geometry = parsed.IsSuccess
            ? parsed.Coordinates.Select(point => new SegmentCoordinate(point.Latitude, point.Longitude)).ToList()
            : null;
        var resolution = parsed.IsSuccess
            ? SegmentAnchorResolver.Resolve(segment, places, geometry)
            : SegmentDisplayGeometry.ResolveStraightConnection(segment, places);
        if (resolution is null || !resolution.IsValid)
        {
            _logger.LogWarning("Skipping Segment decorations for {SegmentId}: {Failure}", segment.Id, resolution?.Failure);
            badgeLayer.DataHasChanged();
            chevronLayer.DataHasChanged();
            return;
        }

        var badges = SegmentDecorationProjector.CreateBadges(resolution);
        var screenPositions = badges.ToDictionary(badge => badge.PlaceId, badge =>
        {
            var world = SphericalMercator.FromLonLat(badge.Longitude, badge.Latitude);
            var screen = viewport.WorldToScreen(world.x, world.y);
            return (X: screen.X, Y: screen.Y - 34);
        });
        var visibleBadges = SegmentDecorationProjector.RetainVisibleBadges(badges, badge =>
        {
            var screen = screenPositions[badge.PlaceId];
            var size = SegmentBadgeImage.Measure(badge.Label);
            return (
                screen.X,
                screen.Y,
                size.Width,
                size.Height);
        });
        foreach (var badge in visibleBadges)
        {
            var (x, y) = SphericalMercator.FromLonLat(badge.Longitude, badge.Latitude);
            var feature = new GeometryFeature(new Point(x, y))
            {
                Styles = [SegmentBadgeImage.Create(badge.Label)]
            };
            badgeLayer.Add(feature);
        }

        AddSelectedSegmentChevrons(chevronLayer, resolution, places, viewport);
        badgeLayer.DataHasChanged();
        chevronLayer.DataHasChanged();
    }

    private static void AddSelectedSegmentChevrons(
        WritableLayer chevronLayer, SegmentAnchorResolution resolution,
        IReadOnlyCollection<TripPlace> places, Viewport viewport)
    {
        var projected = resolution.Geometry.Select(point =>
        {
            var world = SphericalMercator.FromLonLat(point.Longitude, point.Latitude);
            var screen = viewport.WorldToScreen(world.x, world.y);
            return new ProjectedRoutePoint(screen.X, screen.Y);
        }).ToList();
        var markerPositions = places.Select(place =>
        {
            var world = SphericalMercator.FromLonLat(place.Longitude, place.Latitude);
            return viewport.WorldToScreen(world.x, world.y);
        }).ToList();
        foreach (var chevron in SegmentChevronPlacer.Place(projected, Math.Sqrt(125) + 2))
        {
            // Open 10 x 10 logical-unit arms; round 4-unit casing bounds the rendered envelope.
            var angle = chevron.AngleDegrees * Math.PI / 180;
            var backX = chevron.X - Math.Cos(angle) * 10;
            var backY = chevron.Y - Math.Sin(angle) * 10;
            var normalX = -Math.Sin(angle) * 5;
            var normalY = Math.Cos(angle) * 5;
            var arm = new[]
            {
                new MPoint(backX + normalX, backY + normalY),
                new MPoint(chevron.X, chevron.Y),
                new MPoint(backX - normalX, backY - normalY)
            };
            // Include casing and 24-unit clearance around Place tips, also on folded routes.
            // The upper envelope covers the 28 x 45 icon at scale 1.1 and offset Y=-16.
            var left = arm.Min(p => p.X) - 2;
            var right = arm.Max(p => p.X) + 2;
            var top = arm.Min(p => p.Y) - 2;
            var bottom = arm.Max(p => p.Y) + 2;
            if (markerPositions.Any(p => left < p.X + 24 && right > p.X - 24 &&
                    top < p.Y + 24 && bottom > p.Y - 48)) continue;
            var coordinates = arm.Select(p =>
            {
                // Inverse projection lets Mapsui apply map rotation exactly once.
                var world = viewport.ScreenToWorld(p.X, p.Y);
                return new Coordinate(world.X, world.Y);
            }).ToArray();
            chevronLayer.Add(new GeometryFeature(new LineString(coordinates))
            {
                Styles = [CreateChevronStroke(Color.White, 4), CreateChevronStroke(Color.FromString("#852D10"), 2)]
            });
        }
    }

    private static VectorStyle CreateChevronStroke(Color color, double width) => new()
    {
        Line = new Pen(color, width) { PenStrokeCap = PenStrokeCap.Round, StrokeJoin = StrokeJoin.Round }
    };

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

    #region Area Styles

    /// <summary>
    /// Creates a style for an area polygon with fill and stroke colors.
    /// </summary>
    private static IStyle CreateAreaStyle(string? fillColor, string? strokeColor)
    {
        // Parse fill color with transparency for semi-transparent polygons
        var fillHex = fillColor ?? "#4285F4"; // Default blue
        var strokeHex = strokeColor ?? fillHex;

        var fill = MapsuiColorHelper.ParseHexColor(fillHex);
        var stroke = MapsuiColorHelper.ParseHexColor(strokeHex);

        // Make fill semi-transparent if not already
        if (fill.A > 100)
        {
            fill = Color.FromArgb(80, fill.R, fill.G, fill.B);
        }

        return new VectorStyle
        {
            Fill = new Brush(fill),
            Outline = new Pen(stroke, 2)
            {
                PenStrokeCap = PenStrokeCap.Round,
                StrokeJoin = StrokeJoin.Round
            }
        };
    }

    #endregion

    #region Segment Styles

    /// <summary>
    /// Creates a style for a segment based on transport mode.
    /// </summary>
    private static IStyle CreateSegmentStyle(string? transportMode)
    {
        var (color, width) = GetSegmentStyleParameters(transportMode?.ToLowerInvariant());

        var pen = new Pen(color, width)
        {
            PenStrokeCap = PenStrokeCap.Round,
            StrokeJoin = StrokeJoin.Round
        };

        return new VectorStyle
        {
            Line = pen
        };
    }

    /// <summary>
    /// Gets style parameters for a given transport mode.
    /// </summary>
    private static (Color color, double width) GetSegmentStyleParameters(string? mode)
    {
        return mode switch
        {
            // Driving - blue solid line
            "driving" or "car" => (Color.FromArgb(220, 66, 133, 244), 4),

            // Walking - green solid line
            "walking" or "walk" or "foot" => (Color.FromArgb(220, 76, 175, 80), 3),

            // Cycling - orange solid line
            "cycling" or "bicycle" or "bike" => (Color.FromArgb(220, 255, 152, 0), 3),

            // Transit/Public transport - purple solid line
            "transit" or "bus" or "train" or "subway" => (Color.FromArgb(220, 156, 39, 176), 4),

            // Ferry/Boat - teal solid line
            "ferry" or "boat" => (Color.FromArgb(220, 0, 150, 136), 3),

            // Flight - light blue solid line
            "flight" or "plane" or "air" => (Color.FromArgb(180, 3, 169, 244), 2),

            // Default - gray solid line
            _ => (Color.FromArgb(200, 158, 158, 158), 3)
        };
    }

    #endregion

    #region Place Selection

    /// <inheritdoc />
    public void UpdatePlaceSelection(WritableLayer layer, TripPlace? place)
    {
        layer.Clear();

        if (place == null || (place.Latitude == 0 && place.Longitude == 0))
        {
            layer.DataHasChanged();
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(place.Longitude, place.Latitude);
        var point = new Point(x, y);

        // Create a ring style around the selected place using primary app color (#e45243)
        // Ring offset matches marker offset (-16) to align with marker tip
        var style = new SymbolStyle
        {
            SymbolScale = 1.0,
            Offset = new Offset(0, -16),
            Fill = new Brush(Color.Transparent),
            Outline = new Pen(Color.FromArgb(220, 228, 82, 67), 3)  // Primary color ring (#e45243)
            {
                PenStrokeCap = PenStrokeCap.Round
            },
            SymbolType = SymbolType.Ellipse
        };

        var feature = new GeometryFeature(point)
        {
            Styles = new[] { style }
        };

        // Add PlaceId so tapping on the ring still selects the same place
        feature["PlaceId"] = place.Id;

        layer.Add(feature);
        layer.DataHasChanged();
        _logger.LogDebug("Updated place selection ring for place {PlaceId}", place.Id);
    }

    /// <inheritdoc />
    public void ClearPlaceSelection(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    #endregion

}
