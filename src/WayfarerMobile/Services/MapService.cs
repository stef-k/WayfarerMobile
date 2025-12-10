using Microsoft.Extensions.Logging;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
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
    private readonly List<MPoint> _trackPoints = new();
    private List<NavigationWaypoint>? _currentRouteWaypoints;
    private readonly Dictionary<string, string> _iconImageCache = new();
    private readonly ILogger<MapService>? _logger;
    private readonly LocationIndicatorService? _indicatorService;

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
    /// Creates a new instance of MapService.
    /// </summary>
    public MapService()
    {
    }

    /// <summary>
    /// Creates a new instance of MapService with logging and indicator service.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="indicatorService">The location indicator service.</param>
    public MapService(ILogger<MapService> logger, LocationIndicatorService indicatorService)
    {
        _logger = logger;
        _indicatorService = indicatorService;
    }

    #region Properties

    /// <summary>
    /// Gets the configured map instance.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    #endregion

    #region Map Creation

    /// <summary>
    /// Creates and configures the map with OpenStreetMap tiles.
    /// </summary>
    private Map CreateMap()
    {
        var map = new Map
        {
            CRS = "EPSG:3857" // Web Mercator
        };

        // Add OpenStreetMap tile layer
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

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
    /// Creates the style for the location marker.
    /// </summary>
    /// <param name="hexColor">The marker color in hex format.</param>
    private static IStyle CreateLocationMarkerStyle(string hexColor = "#4285F4")
    {
        var color = ParseColor(hexColor);
        return new SymbolStyle
        {
            SymbolScale = 0.5,
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

        // Add to track
        AddTrackPoint(point);

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
    /// Google Maps style - cone width varies with compass calibration quality.
    /// </summary>
    /// <param name="center">Center point in map coordinates.</param>
    /// <param name="bearingDegrees">Bearing in degrees (0-360, 0 = North).</param>
    /// <param name="coneAngleDegrees">Cone spread angle in degrees (30° = well calibrated, 90° = needs calibration).</param>
    private static Polygon CreateHeadingCone(MPoint center, double bearingDegrees, double coneAngleDegrees = 45.0)
    {
        // Cone dimensions in map units (meters in Web Mercator at equator)
        const double coneLength = 40.0;      // Length from center to tip

        // Calculate base width from cone angle (trigonometry)
        // For a cone with angle θ and length L, base width = 2 * L * tan(θ/2)
        var halfAngleRad = (coneAngleDegrees / 2) * Math.PI / 180;
        var coneBaseWidth = 2 * coneLength * Math.Tan(halfAngleRad);

        // Clamp base width to reasonable range (15-80 map units)
        coneBaseWidth = Math.Clamp(coneBaseWidth, 15.0, 80.0);

        // Convert bearing to radians (bearing 0 = North, clockwise)
        // In map coordinates: X = East, Y = North
        // So bearing 0 (North) = 90° in standard math (pointing up +Y)
        var angleRad = (90 - bearingDegrees) * Math.PI / 180;

        // Calculate tip point (in direction of bearing)
        var tipX = center.X + coneLength * Math.Cos(angleRad);
        var tipY = center.Y + coneLength * Math.Sin(angleRad);

        // Calculate base points (perpendicular to bearing direction)
        var perpAngle1 = angleRad + Math.PI / 2;
        var perpAngle2 = angleRad - Math.PI / 2;
        var halfBase = coneBaseWidth / 2;

        var base1X = center.X + halfBase * Math.Cos(perpAngle1);
        var base1Y = center.Y + halfBase * Math.Sin(perpAngle1);

        var base2X = center.X + halfBase * Math.Cos(perpAngle2);
        var base2Y = center.Y + halfBase * Math.Sin(perpAngle2);

        // Create cone polygon (triangle): tip -> base1 -> base2 -> tip
        var coordinates = new[]
        {
            new Coordinate(tipX, tipY),
            new Coordinate(base1X, base1Y),
            new Coordinate(base2X, base2Y),
            new Coordinate(tipX, tipY) // Close the ring
        };

        var ring = new LinearRing(coordinates);
        return new Polygon(ring);
    }

    /// <summary>
    /// Creates the style for the heading cone (darker blue with white outline).
    /// </summary>
    private static IStyle CreateHeadingConeStyle()
    {
        return new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(255, 13, 71, 161)), // Darker blue (#0D47A1)
            Outline = new Pen(Color.White, 1)
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
        if (_map == null)
            return;

        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);

        _map.Navigator.CenterOn(point);

        if (zoomLevel.HasValue)
        {
            _map.Navigator.ZoomTo(zoomLevel.Value);
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
    /// Sets the default zoom level for street-level viewing.
    /// </summary>
    public void SetDefaultZoom()
    {
        _map?.Navigator.ZoomTo(2); // Approximately street level
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
        catch
        {
            // Ignore parsing errors
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
