using Microsoft.Extensions.Logging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.Tiling.Layers;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Services.TileCache;
using Color = Mapsui.Styles.Color;
using Map = Mapsui.Map;
using Brush = Mapsui.Styles.Brush;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing the map display and location markers.
/// Supports smooth heading calculation, pulsing animation, and navigation state colors.
/// </summary>
public class MapService : IDisposable
{
    #region Constants

    private const string LocationLayerName = "CurrentLocation";
    private const string TrackLayerName = "Track";
    private const string GroupMembersLayerName = "GroupMembers";
    private const string TripPlacesLayerName = "TripPlaces";
    private const string NavigationRouteLayerName = "NavigationRoute";
    private const string NavigationRouteCompletedLayerName = "NavigationRouteCompleted";
    private const string TripSegmentsLayerName = "TripSegments";

    /// <summary>
    /// Animation frame interval in milliseconds (~60 FPS).
    /// </summary>
    private const int AnimationIntervalMs = 16;

    #endregion

    #region Fields

    private Map? _map;
    private WritableLayer? _locationLayer;
    private WritableLayer? _trackLayer;
    private WritableLayer? _groupMembersLayer;
    private WritableLayer? _tripPlacesLayer;
    private WritableLayer? _navigationRouteLayer;
    private WritableLayer? _navigationRouteCompletedLayer;
    private WritableLayer? _tripSegmentsLayer;
    private WritableLayer? _droppedPinLayer;
    private readonly List<MPoint> _trackPoints = new();
    private List<NavigationWaypoint>? _currentRouteWaypoints;
    private readonly Dictionary<string, string> _iconImageCache = new();
    private readonly ILogger<MapService>? _logger;
    private readonly LocationIndicatorService? _indicatorService;
    private readonly WayfarerTileSource? _tileSource;

    // Feature reuse for location indicator
    private GeometryFeature? _accuracyFeature;
    private GeometryFeature? _headingFeature;
    private GeometryFeature? _markerFeature;
    private MPoint? _lastMapPoint;
    private double _lastAccuracy;
    private double _lastHeading = -1;

    // Animation
    private Timer? _animationTimer;
    private bool _animationEnabled;
    private bool _disposed;

    #endregion

    /// <summary>
    /// Creates a new instance of MapService with all dependencies.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="indicatorService">The location indicator service.</param>
    /// <param name="tileSource">The custom tile source for offline caching.</param>
    public MapService(
        ILogger<MapService> logger,
        LocationIndicatorService indicatorService,
        WayfarerTileSource tileSource)
    {
        _logger = logger;
        _indicatorService = indicatorService;
        _tileSource = tileSource;
    }

    #region Properties

    /// <summary>
    /// Gets the configured map instance.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    #endregion

    #region Map Creation

    /// <summary>
    /// Creates and configures the map with tile layer (cached or online).
    /// </summary>
    private Map CreateMap()
    {
        var map = new Map
        {
            CRS = "EPSG:3857" // Web Mercator
        };

        // Add tile layer - use custom cached source if available, otherwise fallback to OSM
        // Only ONE tile layer to avoid rendering conflicts
        if (_tileSource != null)
        {
            var tileLayer = new TileLayer(_tileSource)
            {
                Name = "WayfarerTiles"
            };
            map.Layers.Add(tileLayer);
            _logger?.LogInformation("Map using WayfarerTileSource with offline caching");
        }
        else
        {
            map.Layers.Add(OpenStreetMap.CreateTileLayer());
            _logger?.LogWarning("Map using default OSM tiles - offline caching disabled");
        }

        // Add track layer (below location marker)
        _trackLayer = new WritableLayer
        {
            Name = TrackLayerName,
            Style = CreateTrackStyle()
        };
        map.Layers.Add(_trackLayer);

        // Add current location layer
        _locationLayer = new WritableLayer
        {
            Name = LocationLayerName,
            Style = null // Style is set per feature
        };
        map.Layers.Add(_locationLayer);

        // Add group members layer
        _groupMembersLayer = new WritableLayer
        {
            Name = GroupMembersLayerName,
            Style = null // Style is set per feature
        };
        map.Layers.Add(_groupMembersLayer);

        // Add trip segments layer (below place markers)
        _tripSegmentsLayer = new WritableLayer
        {
            Name = TripSegmentsLayerName,
            Style = null // Style is set per feature based on transport mode
        };
        map.Layers.Add(_tripSegmentsLayer);

        // Add trip places layer
        _tripPlacesLayer = new WritableLayer
        {
            Name = TripPlacesLayerName,
            Style = null // Style is set per feature
        };
        map.Layers.Add(_tripPlacesLayer);

        // Add navigation route completed layer (below active route)
        _navigationRouteCompletedLayer = new WritableLayer
        {
            Name = NavigationRouteCompletedLayerName,
            Style = CreateNavigationRouteCompletedStyle()
        };
        map.Layers.Add(_navigationRouteCompletedLayer);

        // Add navigation route layer (remaining route)
        _navigationRouteLayer = new WritableLayer
        {
            Name = NavigationRouteLayerName,
            Style = CreateNavigationRouteStyle()
        };
        map.Layers.Add(_navigationRouteLayer);

        // Add dropped pin layer (for drop pin mode)
        _droppedPinLayer = new WritableLayer
        {
            Name = "DroppedPin",
            Style = null // Style is set per feature
        };
        map.Layers.Add(_droppedPinLayer);

        return map;
    }

    /// <summary>
    /// Creates the style for the track line.
    /// </summary>
    private static IStyle CreateTrackStyle()
    {
        return new VectorStyle
        {
            Line = new Pen(Color.FromArgb(180, 66, 133, 244), 4) // Google Blue with transparency
        };
    }

    /// <summary>
    /// Creates the style for the active navigation route (remaining portion).
    /// </summary>
    private static IStyle CreateNavigationRouteStyle()
    {
        return new VectorStyle
        {
            Line = new Pen(Color.FromArgb(255, 66, 133, 244), 6) // Google Blue, solid
            {
                PenStyle = PenStyle.Solid,
                PenStrokeCap = PenStrokeCap.Round,
                StrokeJoin = StrokeJoin.Round
            }
        };
    }

    /// <summary>
    /// Creates the style for the completed portion of the navigation route.
    /// </summary>
    private static IStyle CreateNavigationRouteCompletedStyle()
    {
        return new VectorStyle
        {
            Line = new Pen(Color.FromArgb(150, 158, 158, 158), 6) // Gray, semi-transparent
            {
                PenStyle = PenStyle.Solid,
                PenStrokeCap = PenStrokeCap.Round,
                StrokeJoin = StrokeJoin.Round
            }
        };
    }

    /// <summary>
    /// Creates the style for the location marker (Google Maps style blue dot).
    /// </summary>
    /// <param name="hexColor">The marker color in hex format.</param>
    private static IStyle CreateLocationMarkerStyle(string hexColor = "#4285F4")
    {
        var color = ParseColor(hexColor);
        return new SymbolStyle
        {
            SymbolScale = 0.6, // Smaller dot similar to Google Maps
            Fill = new Brush(color),
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    /// <summary>
    /// Creates the style for the accuracy circle with optional pulse effect.
    /// </summary>
    /// <param name="hexColor">The circle color in hex format.</param>
    /// <param name="pulseScale">Pulse scale factor (0.85 to 1.15).</param>
    private static IStyle CreateAccuracyCircleStyle(string hexColor = "#4285F4", double pulseScale = 1.0)
    {
        var color = ParseColor(hexColor);

        // Adjust alpha based on pulse scale for breathing effect
        var alpha = (int)(40 + (pulseScale - 1.0) * 100); // 35-45 range
        alpha = Math.Clamp(alpha, 30, 60);

        return new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(alpha, color.R, color.G, color.B)),
            Outline = new Pen(Color.FromArgb(100, color.R, color.G, color.B), 1)
        };
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the map service resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAnimation();
        ClearLocation();
        _iconImageCache.Clear();
    }

    #endregion

    #region Location Updates

    /// <summary>
    /// Updates the current location marker on the map with smooth heading and animation support.
    /// </summary>
    /// <param name="location">The current location.</param>
    /// <param name="centerMap">Whether to center the map on the location.</param>
    public void UpdateLocation(LocationData location, bool centerMap = false)
    {
        if (_locationLayer == null || _map == null)
            return;

        // Convert to Web Mercator
        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);
        _lastMapPoint = point;

        // Calculate best heading using LocationIndicatorService if available
        double heading = location.Bearing ?? -1;
        if (_indicatorService != null)
        {
            heading = _indicatorService.CalculateBestHeading(location);
        }

        // Get accuracy
        var accuracy = location.Accuracy ?? 0;
        _lastAccuracy = accuracy;
        _lastHeading = heading;

        // Update features using reuse pattern
        UpdateLocationFeatures(point, accuracy, heading);

        // Track line disabled - clutters the map without adding value
        // User's historical track is not useful for real-time navigation
        // AddTrackPoint(point);

        // Center map if requested
        if (centerMap)
        {
            CenterOnLocation(location);
        }

        _locationLayer.DataHasChanged();
    }

    /// <summary>
    /// Updates location features with reuse pattern for better performance.
    /// </summary>
    private void UpdateLocationFeatures(MPoint point, double accuracy, double heading)
    {
        if (_locationLayer == null)
            return;

        // Get colors based on navigation state
        var indicatorColor = _indicatorService?.GetIndicatorColor() ?? "#4285F4";
        var pulseScale = _indicatorService?.PulseScale ?? 1.0;

        // Update or create accuracy circle
        if (accuracy > 0)
        {
            var scaledAccuracy = accuracy * pulseScale;
            var accuracyCircle = CreateAccuracyCircle(point, scaledAccuracy);

            if (_accuracyFeature == null)
            {
                _accuracyFeature = new GeometryFeature(accuracyCircle)
                {
                    Styles = new[] { CreateAccuracyCircleStyle(indicatorColor, pulseScale) }
                };
                _locationLayer.Add(_accuracyFeature);
            }
            else
            {
                _accuracyFeature.Geometry = accuracyCircle;
                _accuracyFeature.Styles = new[] { CreateAccuracyCircleStyle(indicatorColor, pulseScale) };
            }
        }
        else if (_accuracyFeature != null)
        {
            _locationLayer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }

        // Update or create heading cone (width varies with compass calibration quality)
        if (heading >= 0 && heading < 360)
        {
            // Get cone angle from indicator service (30° = well calibrated, 90° = needs calibration)
            var coneAngle = _indicatorService?.ConeAngle ?? 45.0;
            var headingCone = CreateHeadingCone(point, heading, coneAngle);

            if (_headingFeature == null)
            {
                _headingFeature = new GeometryFeature(headingCone)
                {
                    Styles = new[] { CreateHeadingConeStyle() }
                };
                _locationLayer.Add(_headingFeature);
            }
            else
            {
                _headingFeature.Geometry = headingCone;
            }
        }
        else if (_headingFeature != null)
        {
            _locationLayer.TryRemove(_headingFeature);
            _headingFeature = null;
        }

        // Update or create location marker
        var markerPoint = new Point(point.X, point.Y);

        if (_markerFeature == null)
        {
            _markerFeature = new GeometryFeature(markerPoint)
            {
                Styles = new[] { CreateLocationMarkerStyle(indicatorColor) }
            };
            _locationLayer.Add(_markerFeature);
        }
        else
        {
            _markerFeature.Geometry = markerPoint;
            _markerFeature.Styles = new[] { CreateLocationMarkerStyle(indicatorColor) };
        }
    }

    /// <summary>
    /// Starts the pulsing animation for the location indicator.
    /// Call this when navigation starts or tracking is active.
    /// </summary>
    public void StartAnimation()
    {
        if (_animationEnabled || _disposed)
            return;

        _animationEnabled = true;

        if (_indicatorService != null)
        {
            _indicatorService.IsNavigating = true;
        }

        _animationTimer = new Timer(OnAnimationTick, null, 0, AnimationIntervalMs);
        _logger?.LogDebug("Location indicator animation started");
    }

    /// <summary>
    /// Stops the pulsing animation.
    /// </summary>
    public void StopAnimation()
    {
        if (!_animationEnabled)
            return;

        _animationEnabled = false;

        if (_indicatorService != null)
        {
            _indicatorService.IsNavigating = false;
        }

        _animationTimer?.Dispose();
        _animationTimer = null;

        // Reset to static display
        if (_lastMapPoint != null)
        {
            UpdateLocationFeatures(_lastMapPoint, _lastAccuracy, _lastHeading);
            _locationLayer?.DataHasChanged();
        }

        _logger?.LogDebug("Location indicator animation stopped");
    }

    /// <summary>
    /// Sets the navigation route state (affects indicator color).
    /// </summary>
    /// <param name="isOnRoute">Whether currently on the navigation route.</param>
    public void SetNavigationState(bool isOnRoute)
    {
        if (_indicatorService != null)
        {
            _indicatorService.IsOnRoute = isOnRoute;
        }
    }

    /// <summary>
    /// Animation tick handler - updates pulsing effect.
    /// </summary>
    private void OnAnimationTick(object? state)
    {
        if (!_animationEnabled || _lastMapPoint == null)
            return;

        // Update indicator service animation state
        _indicatorService?.UpdateAnimation();

        // Update features with new pulse scale
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_lastMapPoint != null)
            {
                UpdateLocationFeatures(_lastMapPoint, _lastAccuracy, _lastHeading);
                _locationLayer?.DataHasChanged();
            }
        });
    }

    /// <summary>
    /// Shows the last known location with a gray indicator (GPS unavailable/stale).
    /// Call this when no fresh GPS data is available but we have a cached location.
    /// </summary>
    /// <returns>True if last known location was displayed, false if none available.</returns>
    public bool ShowLastKnownLocation()
    {
        if (_indicatorService?.LastKnownLocation == null || _locationLayer == null)
            return false;

        var location = _indicatorService.LastKnownLocation;
        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);
        _lastMapPoint = point;

        // Clear existing features first
        if (_accuracyFeature != null)
        {
            _locationLayer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            _locationLayer.TryRemove(_headingFeature);
            _headingFeature = null;
        }

        // Show gray marker for stale location
        var markerPoint = new Point(point.X, point.Y);
        var grayColor = "#9E9E9E"; // Material Gray 500

        if (_markerFeature == null)
        {
            _markerFeature = new GeometryFeature(markerPoint)
            {
                Styles = new[] { CreateLocationMarkerStyle(grayColor) }
            };
            _locationLayer.Add(_markerFeature);
        }
        else
        {
            _markerFeature.Geometry = markerPoint;
            _markerFeature.Styles = new[] { CreateLocationMarkerStyle(grayColor) };
        }

        _locationLayer.DataHasChanged();

        _logger?.LogDebug("Showing last known location (stale): {Location}, age: {Age:F0}s",
            location, _indicatorService.SecondsSinceLastUpdate);

        return true;
    }

    /// <summary>
    /// Gets whether the current location is stale (no GPS updates recently).
    /// </summary>
    public bool IsLocationStale => _indicatorService?.IsLocationStale ?? false;

    /// <summary>
    /// Gets the time since last location update in seconds.
    /// </summary>
    public double SecondsSinceLastUpdate => _indicatorService?.SecondsSinceLastUpdate ?? double.MaxValue;

    /// <summary>
    /// Clears the location indicator features.
    /// </summary>
    public void ClearLocation()
    {
        if (_accuracyFeature != null)
        {
            _locationLayer?.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            _locationLayer?.TryRemove(_headingFeature);
            _headingFeature = null;
        }
        if (_markerFeature != null)
        {
            _locationLayer?.TryRemove(_markerFeature);
            _markerFeature = null;
        }

        _lastMapPoint = null;
        _lastAccuracy = 0;
        _lastHeading = -1;

        _indicatorService?.Reset();
        _locationLayer?.DataHasChanged();
    }

    /// <summary>
    /// Creates an accuracy circle geometry.
    /// </summary>
    private static Polygon CreateAccuracyCircle(MPoint center, double radiusMeters)
    {
        const int segments = 36;
        var coordinates = new Coordinate[segments + 1];

        // Convert radius from meters to map units (approximate at this latitude)
        var radiusInMapUnits = radiusMeters;

        for (int i = 0; i < segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            var px = center.X + radiusInMapUnits * Math.Cos(angle);
            var py = center.Y + radiusInMapUnits * Math.Sin(angle);
            coordinates[i] = new Coordinate(px, py);
        }
        coordinates[segments] = coordinates[0]; // Close the ring

        var ring = new LinearRing(coordinates);
        return new Polygon(ring);
    }

    /// <summary>
    /// Creates a heading cone (wedge) pointing in the direction of travel.
    /// Google Maps style - open cone that widens away from the center, indicating
    /// approximate heading direction with uncertainty (not an exact arrow).
    /// </summary>
    /// <param name="center">Center point in map coordinates.</param>
    /// <param name="bearingDegrees">Bearing in degrees (0-360, 0 = North).</param>
    /// <param name="coneAngleDegrees">Cone spread angle in degrees (30° = well calibrated, 90° = needs calibration).</param>
    private static Polygon CreateHeadingCone(MPoint center, double bearingDegrees, double coneAngleDegrees = 45.0)
    {
        // Cone dimensions in map units (meters in Web Mercator at equator)
        const double coneLength = 35.0;       // Length from center to outer arc
        const double innerRadius = 12.0;      // Inner radius (gap from center dot)
        const int arcSegments = 12;           // Smoothness of the arc

        // Convert bearing to radians (bearing 0 = North, clockwise)
        // In map coordinates: X = East, Y = North
        // So bearing 0 (North) = 90° in standard math (pointing up +Y)
        var centerAngleRad = (90 - bearingDegrees) * Math.PI / 180;
        var halfConeAngleRad = (coneAngleDegrees / 2) * Math.PI / 180;

        // Build the cone shape: inner arc -> outer arc -> close
        var coordinates = new List<Coordinate>();

        // Start angle and end angle for the cone
        var startAngle = centerAngleRad - halfConeAngleRad;
        var endAngle = centerAngleRad + halfConeAngleRad;

        // Inner arc (from start to end, close to center)
        for (int i = 0; i <= arcSegments; i++)
        {
            var angle = startAngle + (endAngle - startAngle) * i / arcSegments;
            var x = center.X + innerRadius * Math.Cos(angle);
            var y = center.Y + innerRadius * Math.Sin(angle);
            coordinates.Add(new Coordinate(x, y));
        }

        // Outer arc (from end back to start, further from center)
        for (int i = arcSegments; i >= 0; i--)
        {
            var angle = startAngle + (endAngle - startAngle) * i / arcSegments;
            var x = center.X + coneLength * Math.Cos(angle);
            var y = center.Y + coneLength * Math.Sin(angle);
            coordinates.Add(new Coordinate(x, y));
        }

        // Close the ring
        coordinates.Add(coordinates[0]);

        var ring = new LinearRing(coordinates.ToArray());
        return new Polygon(ring);
    }

    /// <summary>
    /// Creates the style for the heading cone (semi-transparent blue, Google Maps style).
    /// </summary>
    private static IStyle CreateHeadingConeStyle()
    {
        return new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(80, 66, 133, 244)), // Semi-transparent Google Blue
            Outline = null // No outline for cleaner look
        };
    }

    #endregion

    #region Track Management

    /// <summary>
    /// Adds a point to the track line.
    /// </summary>
    private void AddTrackPoint(MPoint point)
    {
        _trackPoints.Add(point);

        // Update track line if we have at least 2 points
        if (_trackPoints.Count >= 2 && _trackLayer != null)
        {
            _trackLayer.Clear();

            var coordinates = _trackPoints
                .Select(p => new Coordinate(p.X, p.Y))
                .ToArray();

            var lineString = new LineString(coordinates);
            _trackLayer.Add(new GeometryFeature(lineString));
            _trackLayer.DataHasChanged();
        }
    }

    /// <summary>
    /// Clears the track history.
    /// </summary>
    public void ClearTrack()
    {
        _trackPoints.Clear();
        _trackLayer?.Clear();
        _trackLayer?.DataHasChanged();
    }

    #endregion

    #region Map Navigation

    /// <summary>
    /// Centers the map on the specified location.
    /// </summary>
    /// <param name="location">The location to center on.</param>
    /// <param name="zoomLevel">Optional zoom level (resolution).</param>
    public void CenterOnLocation(LocationData location, double? zoomLevel = null)
    {
        if (_map?.Navigator == null)
        {
            _logger?.LogWarning("Cannot center - map or navigator is null");
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);

        try
        {
            // Use resolution index 17 for street level (approximately web map zoom 17)
            if (zoomLevel.HasValue)
            {
                _map.Navigator.CenterOnAndZoomTo(point, zoomLevel.Value);
            }
            else if (_map.Navigator.Resolutions != null && _map.Navigator.Resolutions.Count > 17)
            {
                // Default to street level zoom when centering on location
                _map.Navigator.CenterOnAndZoomTo(point, _map.Navigator.Resolutions[17]);
            }
            else if (_map.Navigator.Resolutions != null && _map.Navigator.Resolutions.Count > 0)
            {
                // Fallback to middle resolution if 17 doesn't exist
                var midIndex = _map.Navigator.Resolutions.Count / 2;
                _map.Navigator.CenterOnAndZoomTo(point, _map.Navigator.Resolutions[midIndex]);
            }
            else
            {
                _map.Navigator.CenterOn(point);
            }

            _logger?.LogDebug("Centered map on {Lat:F6}, {Lon:F6}", location.Latitude, location.Longitude);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error centering map");
            // Fallback to simple center
            _map.Navigator.CenterOn(point);
        }
    }

    /// <summary>
    /// Zooms to show the entire track.
    /// </summary>
    public void ZoomToTrack()
    {
        if (_map == null || _trackPoints.Count < 2)
            return;

        var minX = _trackPoints.Min(p => p.X);
        var maxX = _trackPoints.Max(p => p.X);
        var minY = _trackPoints.Min(p => p.Y);
        var maxY = _trackPoints.Max(p => p.Y);

        // Add padding
        var padding = Math.Max(maxX - minX, maxY - minY) * 0.1;
        var extent = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);

        _map.Navigator.ZoomToBox(extent);
    }

    /// <summary>
    /// Resets the map rotation to north (0 degrees).
    /// </summary>
    public void ResetMapRotation()
    {
        _map?.Navigator.RotateTo(0);
    }

    /// <summary>
    /// Sets the default zoom level for street-level viewing.
    /// </summary>
    public void SetDefaultZoom()
    {
        // Use resolution index 15 for city overview when no location available
        // Index 15 corresponds approximately to web map zoom level 15
        if (_map?.Navigator.Resolutions != null && _map.Navigator.Resolutions.Count > 15)
        {
            _map.Navigator.ZoomTo(_map.Navigator.Resolutions[15]);
        }
    }

    #endregion

    #region Group Members

    /// <summary>
    /// Updates the group member markers on the map.
    /// </summary>
    /// <param name="members">The list of group members with their locations.</param>
    public void UpdateGroupMembers(IEnumerable<GroupMemberLocation> members)
    {
        if (_groupMembersLayer == null || _map == null)
            return;

        _groupMembersLayer.Clear();

        var points = new List<MPoint>();

        foreach (var member in members)
        {
            if (member.Latitude == 0 && member.Longitude == 0)
                continue;

            var (x, y) = SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
            var point = new MPoint(x, y);
            points.Add(point);

            // Parse member color
            var color = ParseColor(member.ColorHex);

            // Add marker
            var markerPoint = new Point(point.X, point.Y);
            _groupMembersLayer.Add(new GeometryFeature(markerPoint)
            {
                Styles = new[] { CreateMemberMarkerStyle(color) }
            });
        }

        _groupMembersLayer.DataHasChanged();

        // Auto-zoom to fit all members if there are multiple
        if (points.Count > 1)
        {
            ZoomToPoints(points);
        }
        else if (points.Count == 1)
        {
            _map.Navigator.CenterOn(points[0]);
        }
    }

    /// <summary>
    /// Clears all group member markers.
    /// </summary>
    public void ClearGroupMembers()
    {
        _groupMembersLayer?.Clear();
        _groupMembersLayer?.DataHasChanged();
    }

    /// <summary>
    /// Creates a marker style with the specified color.
    /// </summary>
    private static IStyle CreateMemberMarkerStyle(Color color)
    {
        return new SymbolStyle
        {
            SymbolScale = 0.6,
            Fill = new Brush(color),
            Outline = new Pen(Color.White, 2),
            SymbolType = SymbolType.Ellipse
        };
    }

    /// <summary>
    /// Parses a hex color string to a Mapsui Color.
    /// </summary>
    private static Color ParseColor(string? hexColor)
    {
        if (string.IsNullOrEmpty(hexColor))
            return Color.FromArgb(255, 66, 133, 244); // Default blue

        try
        {
            hexColor = hexColor.TrimStart('#');
            if (hexColor.Length == 6)
            {
                var r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                var g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                var b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch (FormatException)
        {
            // Invalid hex format - use default color
        }
        catch (OverflowException)
        {
            // Hex value overflow - use default color
        }

        return Color.FromArgb(255, 66, 133, 244); // Default blue
    }

    /// <summary>
    /// Zooms to fit all specified points.
    /// </summary>
    private void ZoomToPoints(List<MPoint> points)
    {
        if (_map == null || points.Count < 2)
            return;

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        // Add padding
        var padding = Math.Max(maxX - minX, maxY - minY) * 0.2;
        var extent = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);

        _map.Navigator.ZoomToBox(extent);
    }

    #endregion

    #region Navigation Route

    /// <summary>
    /// Displays a navigation route on the map.
    /// </summary>
    /// <param name="route">The navigation route to display.</param>
    public void ShowNavigationRoute(NavigationRoute route)
    {
        if (_navigationRouteLayer == null || _map == null || route.Waypoints.Count < 2)
            return;

        _currentRouteWaypoints = route.Waypoints;
        ClearNavigationRoute();

        // Convert waypoints to map coordinates
        var coordinates = route.Waypoints
            .Select(w =>
            {
                var (x, y) = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
                return new Coordinate(x, y);
            })
            .ToArray();

        // Create line string for the route
        var lineString = new LineString(coordinates);
        _navigationRouteLayer.Add(new GeometryFeature(lineString));
        _navigationRouteLayer.DataHasChanged();

        _logger?.LogDebug("Displayed navigation route with {WaypointCount} waypoints", route.Waypoints.Count);
    }

    /// <summary>
    /// Updates the navigation route display to show progress.
    /// Completed portion shown in gray, remaining in blue.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    public void UpdateNavigationRouteProgress(double currentLat, double currentLon)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null ||
            _currentRouteWaypoints == null || _currentRouteWaypoints.Count < 2)
            return;

        // Find the nearest point on the route
        var (nearestIndex, _) = FindNearestWaypointIndex(currentLat, currentLon);

        if (nearestIndex < 0)
            return;

        // Clear existing route display
        _navigationRouteLayer.Clear();
        _navigationRouteCompletedLayer.Clear();

        // Convert current position to map coordinates
        var (currentX, currentY) = SphericalMercator.FromLonLat(currentLon, currentLat);

        // Build completed portion (start to current position)
        if (nearestIndex > 0)
        {
            var completedCoords = new List<Coordinate>();
            for (int i = 0; i <= nearestIndex; i++)
            {
                var w = _currentRouteWaypoints[i];
                var (x, y) = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
                completedCoords.Add(new Coordinate(x, y));
            }
            // Add current position as last point of completed
            completedCoords.Add(new Coordinate(currentX, currentY));

            if (completedCoords.Count >= 2)
            {
                var completedLine = new LineString(completedCoords.ToArray());
                _navigationRouteCompletedLayer.Add(new GeometryFeature(completedLine));
            }
        }

        // Build remaining portion (current position to end)
        var remainingCoords = new List<Coordinate> { new(currentX, currentY) };
        for (int i = nearestIndex; i < _currentRouteWaypoints.Count; i++)
        {
            var w = _currentRouteWaypoints[i];
            var (x, y) = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
            remainingCoords.Add(new Coordinate(x, y));
        }

        if (remainingCoords.Count >= 2)
        {
            var remainingLine = new LineString(remainingCoords.ToArray());
            _navigationRouteLayer.Add(new GeometryFeature(remainingLine));
        }

        _navigationRouteLayer.DataHasChanged();
        _navigationRouteCompletedLayer.DataHasChanged();
    }

    /// <summary>
    /// Finds the index of the nearest waypoint to the given position.
    /// </summary>
    private (int index, double distance) FindNearestWaypointIndex(double lat, double lon)
    {
        if (_currentRouteWaypoints == null || _currentRouteWaypoints.Count == 0)
            return (-1, double.MaxValue);

        int nearestIndex = 0;
        double nearestDistance = double.MaxValue;

        for (int i = 0; i < _currentRouteWaypoints.Count; i++)
        {
            var w = _currentRouteWaypoints[i];
            var distance = CalculateDistanceMeters(lat, lon, w.Latitude, w.Longitude);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return (nearestIndex, nearestDistance);
    }

    /// <summary>
    /// Calculates distance between two points in meters using Haversine formula.
    /// </summary>
    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMeters = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    /// <summary>
    /// Clears the navigation route from the map.
    /// </summary>
    public void ClearNavigationRoute()
    {
        _navigationRouteLayer?.Clear();
        _navigationRouteCompletedLayer?.Clear();
        _navigationRouteLayer?.DataHasChanged();
        _navigationRouteCompletedLayer?.DataHasChanged();
        _currentRouteWaypoints = null;

        _logger?.LogDebug("Cleared navigation route from map");
    }

    /// <summary>
    /// Gets whether a navigation route is currently displayed.
    /// </summary>
    public bool HasNavigationRoute => _currentRouteWaypoints != null && _currentRouteWaypoints.Count > 0;

    /// <summary>
    /// Zooms the map to show the entire navigation route.
    /// </summary>
    public void ZoomToNavigationRoute()
    {
        if (_map == null || _currentRouteWaypoints == null || _currentRouteWaypoints.Count < 2)
            return;

        var points = _currentRouteWaypoints
            .Select(w =>
            {
                var (x, y) = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
                return new MPoint(x, y);
            })
            .ToList();

        ZoomToPoints(points);
    }

    #endregion

    #region Trip Segments

    /// <summary>
    /// Updates the trip segments on the map.
    /// Segments are drawn as polylines between places, with different styles per transport mode.
    /// </summary>
    /// <param name="segments">The list of trip segments with geometry.</param>
    public void UpdateTripSegments(IEnumerable<TripSegment> segments)
    {
        if (_tripSegmentsLayer == null || _map == null)
            return;

        _tripSegmentsLayer.Clear();

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

                _tripSegmentsLayer.Add(new GeometryFeature(lineString)
                {
                    Styles = new[] { style }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to decode segment geometry for segment {SegmentId}", segment.Id);
            }
        }

        _tripSegmentsLayer.DataHasChanged();
        _logger?.LogDebug("Displayed {SegmentCount} trip segments", segments.Count());
    }

    /// <summary>
    /// Clears all trip segments from the map.
    /// </summary>
    public void ClearTripSegments()
    {
        _tripSegmentsLayer?.Clear();
        _tripSegmentsLayer?.DataHasChanged();
    }

    /// <summary>
    /// Creates a style for a segment based on transport mode.
    /// </summary>
    /// <param name="transportMode">The transport mode (driving, walking, cycling, transit, etc.).</param>
    private static IStyle CreateSegmentStyle(string? transportMode)
    {
        // Define colors per transport mode
        var (color, width, dashPattern) = GetSegmentStyleParameters(transportMode?.ToLowerInvariant());

        var pen = new Pen(color, width)
        {
            PenStrokeCap = PenStrokeCap.Round,
            StrokeJoin = StrokeJoin.Round
        };

        // Apply dash pattern for certain modes
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

    #region Dropped Pin

    /// <summary>
    /// Shows a dropped pin marker at the specified location.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void ShowDroppedPin(double latitude, double longitude)
    {
        if (_droppedPinLayer == null || _map == null)
            return;

        // Clear any existing pin
        _droppedPinLayer.Clear();

        // Convert to Web Mercator
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var point = new Point(x, y);

        // Create red pin marker
        var pinFeature = new GeometryFeature(point)
        {
            Styles = new[] { CreateDroppedPinStyle() }
        };

        _droppedPinLayer.Add(pinFeature);
        _droppedPinLayer.DataHasChanged();

        _logger?.LogDebug("Dropped pin at {Lat:F6}, {Lon:F6}", latitude, longitude);
    }

    /// <summary>
    /// Clears the dropped pin marker.
    /// </summary>
    public void ClearDroppedPin()
    {
        _droppedPinLayer?.Clear();
        _droppedPinLayer?.DataHasChanged();
    }

    /// <summary>
    /// Creates the style for a dropped pin (red marker).
    /// </summary>
    private static IStyle CreateDroppedPinStyle()
    {
        return new SymbolStyle
        {
            SymbolScale = 0.7,
            Fill = new Brush(Color.FromArgb(255, 244, 67, 54)), // Material Red
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    #endregion

    #region Map Utilities

    /// <summary>
    /// Refreshes the map display. Call this after the map control becomes visible again.
    /// </summary>
    public void RefreshMap()
    {
        _map?.RefreshGraphics();
        _locationLayer?.DataHasChanged();
        _trackLayer?.DataHasChanged();
        _droppedPinLayer?.DataHasChanged();

        _logger?.LogDebug("Map refreshed");
    }

    /// <summary>
    /// Ensures the map is properly initialized with default view.
    /// </summary>
    public void EnsureInitialized()
    {
        // Force map creation if not already done
        _ = Map;

        // Set default zoom if no location
        if (_lastMapPoint == null)
        {
            SetDefaultZoom();
        }
    }

    #endregion

    #region Trip Places

    /// <summary>
    /// Updates the trip place markers on the map.
    /// </summary>
    /// <param name="places">The list of trip places.</param>
    public async Task UpdateTripPlacesAsync(IEnumerable<TripPlace> places)
    {
        if (_tripPlacesLayer == null || _map == null)
            return;

        _tripPlacesLayer.Clear();

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
            _tripPlacesLayer.Add(new GeometryFeature(markerPoint)
            {
                Styles = new[] { style }
            });
        }

        _tripPlacesLayer.DataHasChanged();

        // Auto-zoom to fit all places if there are multiple
        if (points.Count > 1)
        {
            ZoomToPoints(points);
        }
        else if (points.Count == 1)
        {
            _map.Navigator.CenterOn(points[0]);
        }
    }

    /// <summary>
    /// Clears all trip place markers.
    /// </summary>
    public void ClearTripPlaces()
    {
        _tripPlacesLayer?.Clear();
        _tripPlacesLayer?.DataHasChanged();
    }

    /// <summary>
    /// Creates a marker style for a place with custom icon.
    /// </summary>
    /// <param name="iconName">The icon name.</param>
    /// <param name="markerColor">The marker color.</param>
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
            _logger?.LogWarning(ex, "Failed to load icon {Icon} with color {Color}", iconName, markerColor);
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
        var color = ParseColor(hexColor);
        return new SymbolStyle
        {
            SymbolScale = 0.6,
            Fill = new Brush(color),
            Outline = new Pen(Color.White, 2),
            SymbolType = SymbolType.Ellipse
        };
    }

    #endregion
}

/// <summary>
/// Represents a group member's location for map display.
/// </summary>
public class GroupMemberLocation
{
    /// <summary>
    /// Gets or sets the member's user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the marker color in hex format.
    /// </summary>
    public string? ColorHex { get; set; }

    /// <summary>
    /// Gets or sets whether this is a live location.
    /// </summary>
    public bool IsLive { get; set; }
}
