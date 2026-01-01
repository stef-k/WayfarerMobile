using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
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
/// Service for managing the current user's location indicator on the map.
/// Handles the blue dot, accuracy circle, heading cone, and pulsing animation.
/// Registered as Singleton - maintains state for animation and heading smoothing.
/// </summary>
public class LocationLayerService : ILocationLayerService
{
    private readonly ILogger<LocationLayerService> _logger;
    private readonly LocationIndicatorService _indicatorService;

    // Feature reuse for location indicator
    // Note: Features are tied to a specific layer - if layer changes, features are recreated
    private GeometryFeature? _accuracyFeature;
    private GeometryFeature? _headingFeature;
    private GeometryFeature? _markerFeature;
    private WritableLayer? _currentFeatureLayer;
    private MPoint? _lastMapPoint;
    private double _lastAccuracy;
    private double _lastHeading = -1;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of LocationLayerService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="indicatorService">Service for heading smoothing and navigation colors.</param>
    public LocationLayerService(
        ILogger<LocationLayerService> logger,
        LocationIndicatorService indicatorService)
    {
        _logger = logger;
        _indicatorService = indicatorService;
    }

    /// <inheritdoc />
    public string LocationLayerName => "CurrentLocation";

    /// <inheritdoc />
    public bool IsLocationStale => _indicatorService.IsLocationStale;

    /// <inheritdoc />
    public double SecondsSinceLastUpdate => _indicatorService.SecondsSinceLastUpdate;

    /// <inheritdoc />
    public MPoint? LastMapPoint => _lastMapPoint;

    /// <inheritdoc />
    public double LastAccuracy => _lastAccuracy;

    /// <inheritdoc />
    public double LastHeading => _lastHeading;

    /// <inheritdoc />
    public void UpdateLocation(WritableLayer layer, LocationData location)
    {
        // Convert to Web Mercator
        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);
        _lastMapPoint = point;

        // Calculate best heading using LocationIndicatorService
        var heading = _indicatorService.CalculateBestHeading(location);

        // Get accuracy
        var accuracy = location.Accuracy ?? 0;
        _lastAccuracy = accuracy;
        _lastHeading = heading;

        // Update features using reuse pattern
        UpdateLocationFeatures(layer, point, accuracy, heading);

        layer.DataHasChanged();
    }

    /// <summary>
    /// Updates location features with reuse pattern for better performance.
    /// Features are tied to a specific layer - if layer changes, old features are discarded.
    /// </summary>
    private void UpdateLocationFeatures(WritableLayer layer, MPoint point, double accuracy, double heading)
    {
        // If layer changed, discard cached features (they belong to the old layer)
        if (_currentFeatureLayer != null && _currentFeatureLayer != layer)
        {
            _accuracyFeature = null;
            _headingFeature = null;
            _markerFeature = null;
            layer.Clear(); // Clear new layer in case it has stale features
        }
        _currentFeatureLayer = layer;

        // Get colors based on navigation state
        var indicatorColor = _indicatorService.GetIndicatorColor();
        var pulseScale = _indicatorService.PulseScale;

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
                layer.Add(_accuracyFeature);
            }
            else
            {
                _accuracyFeature.Geometry = accuracyCircle;
                _accuracyFeature.Styles = new[] { CreateAccuracyCircleStyle(indicatorColor, pulseScale) };
            }
        }
        else if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }

        // Update or create heading cone
        if (heading >= 0 && heading < 360)
        {
            var coneAngle = _indicatorService.ConeAngle;
            var headingCone = CreateHeadingCone(point, heading, coneAngle);

            if (_headingFeature == null)
            {
                _headingFeature = new GeometryFeature(headingCone)
                {
                    Styles = new[] { CreateHeadingConeStyle() }
                };
                layer.Add(_headingFeature);
            }
            else
            {
                _headingFeature.Geometry = headingCone;
            }
        }
        else if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
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
            layer.Add(_markerFeature);
        }
        else
        {
            _markerFeature.Geometry = markerPoint;
            _markerFeature.Styles = new[] { CreateLocationMarkerStyle(indicatorColor) };
        }
    }

    /// <inheritdoc />
    public void ClearLocation(WritableLayer layer)
    {
        if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
            _headingFeature = null;
        }
        if (_markerFeature != null)
        {
            layer.TryRemove(_markerFeature);
            _markerFeature = null;
        }

        _lastMapPoint = null;
        _lastAccuracy = 0;
        _lastHeading = -1;

        _indicatorService.Reset();
        layer.DataHasChanged();
    }

    /// <inheritdoc />
    public bool ShowLastKnownLocation(WritableLayer layer)
    {
        if (_indicatorService.LastKnownLocation == null)
            return false;

        var location = _indicatorService.LastKnownLocation;
        var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var point = new MPoint(x, y);
        _lastMapPoint = point;

        // Clear existing features first
        if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
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
            layer.Add(_markerFeature);
        }
        else
        {
            _markerFeature.Geometry = markerPoint;
            _markerFeature.Styles = new[] { CreateLocationMarkerStyle(grayColor) };
        }

        layer.DataHasChanged();

        _logger.LogDebug("Showing last known location (stale): {Location}, age: {Age:F0}s",
            location, _indicatorService.SecondsSinceLastUpdate);

        return true;
    }

    /// <inheritdoc />
    public void StopAnimation()
    {
        // No-op: Animation feature was never implemented.
        // Method kept for interface compatibility with cleanup code.
    }

    /// <inheritdoc />
    public void SetNavigationState(bool isOnRoute)
    {
        _indicatorService.IsOnRoute = isOnRoute;
    }

    #region Geometry Creation

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
    /// </summary>
    private static Polygon CreateHeadingCone(MPoint center, double bearingDegrees, double coneAngleDegrees = 45.0)
    {
        const double coneLength = 35.0;
        const double innerRadius = 12.0;
        const int arcSegments = 12;

        // Convert bearing to radians (bearing 0 = North, clockwise)
        var centerAngleRad = (90 - bearingDegrees) * Math.PI / 180;
        var halfConeAngleRad = (coneAngleDegrees / 2) * Math.PI / 180;

        var coordinates = new List<Coordinate>();

        var startAngle = centerAngleRad - halfConeAngleRad;
        var endAngle = centerAngleRad + halfConeAngleRad;

        // Inner arc
        for (int i = 0; i <= arcSegments; i++)
        {
            var angle = startAngle + (endAngle - startAngle) * i / arcSegments;
            var x = center.X + innerRadius * Math.Cos(angle);
            var y = center.Y + innerRadius * Math.Sin(angle);
            coordinates.Add(new Coordinate(x, y));
        }

        // Outer arc
        for (int i = arcSegments; i >= 0; i--)
        {
            var angle = startAngle + (endAngle - startAngle) * i / arcSegments;
            var x = center.X + coneLength * Math.Cos(angle);
            var y = center.Y + coneLength * Math.Sin(angle);
            coordinates.Add(new Coordinate(x, y));
        }

        coordinates.Add(coordinates[0]);

        var ring = new LinearRing(coordinates.ToArray());
        return new Polygon(ring);
    }

    #endregion

    #region Style Creation

    /// <summary>
    /// Creates the style for the location marker (Google Maps style blue dot).
    /// </summary>
    private static IStyle CreateLocationMarkerStyle(string hexColor = "#4285F4")
    {
        var color = MapsuiColorHelper.ParseHexColor(hexColor);
        return new SymbolStyle
        {
            SymbolScale = 0.6,
            Fill = new Brush(color),
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    /// <summary>
    /// Creates the style for the accuracy circle with optional pulse effect.
    /// </summary>
    private static IStyle CreateAccuracyCircleStyle(string hexColor = "#4285F4", double pulseScale = 1.0)
    {
        var color = MapsuiColorHelper.ParseHexColor(hexColor);

        var alpha = (int)(40 + (pulseScale - 1.0) * 100);
        alpha = Math.Clamp(alpha, 30, 60);

        return new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(alpha, color.R, color.G, color.B)),
            Outline = new Pen(Color.FromArgb(100, color.R, color.G, color.B), 1)
        };
    }

    /// <summary>
    /// Creates the style for the heading cone.
    /// </summary>
    private static IStyle CreateHeadingConeStyle()
    {
        return new VectorStyle
        {
            Fill = new Brush(Color.FromArgb(80, 66, 133, 244)),
            Outline = null
        };
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the service resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Clear cached features to release memory
        _accuracyFeature = null;
        _headingFeature = null;
        _markerFeature = null;
        _currentFeatureLayer = null;
        _lastMapPoint = null;
    }

    #endregion
}
