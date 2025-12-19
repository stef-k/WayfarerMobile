using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing dropped pin marker on the map.
/// Registered as Singleton - stateless rendering service.
/// </summary>
public class DroppedPinLayerService : IDroppedPinLayerService
{
    private readonly ILogger<DroppedPinLayerService> _logger;

    /// <summary>
    /// Creates a new instance of DroppedPinLayerService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public DroppedPinLayerService(ILogger<DroppedPinLayerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string DroppedPinLayerName => "DroppedPin";

    /// <inheritdoc />
    public void ShowDroppedPin(WritableLayer layer, double latitude, double longitude)
    {
        // Clear any existing pin
        layer.Clear();

        // Convert to Web Mercator
        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var point = new Point(x, y);

        // Create red pin marker
        var pinFeature = new GeometryFeature(point)
        {
            Styles = new[] { CreateDroppedPinStyle() }
        };

        layer.Add(pinFeature);
        layer.DataHasChanged();

        _logger.LogDebug("Dropped pin at {Lat:F6}, {Lon:F6}", latitude, longitude);
    }

    /// <inheritdoc />
    public void ClearDroppedPin(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
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
}
