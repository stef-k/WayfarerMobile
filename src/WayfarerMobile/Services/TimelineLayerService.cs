using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing timeline-related map layers.
/// Handles timeline location markers for the timeline page.
/// Registered as Singleton - stateless rendering service.
/// </summary>
public class TimelineLayerService : ITimelineLayerService
{
    private readonly ILogger<TimelineLayerService> _logger;

    /// <summary>
    /// Creates a new instance of TimelineLayerService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public TimelineLayerService(ILogger<TimelineLayerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string TimelineLayerName => "TimelineLocations";

    /// <inheritdoc />
    public List<MPoint> UpdateTimelineMarkers(WritableLayer layer, IEnumerable<TimelineLocation> locations)
    {
        layer.Clear();

        var points = new List<MPoint>();
        var locationList = locations.ToList();

        _logger.LogDebug("UpdateTimelineMarkers called with {LocationCount} locations", locationList.Count);

        foreach (var location in locationList)
        {
            var coords = location.Coordinates;
            if (coords == null || (coords.X == 0 && coords.Y == 0))
                continue;

            var (x, y) = SphericalMercator.FromLonLat(coords.X, coords.Y);
            var point = new MPoint(x, y);
            points.Add(point);

            // Create marker style (blue dot)
            var style = CreateTimelineMarkerStyle();

            var feature = new GeometryFeature(new Point(point.X, point.Y))
            {
                Styles = new[] { style }
            };

            // Add properties for tap identification
            feature["LocationId"] = location.Id;
            feature["Timestamp"] = location.LocalTimestamp.ToString("g");

            layer.Add(feature);
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {MarkerCount} timeline markers", points.Count);

        return points;
    }

    /// <inheritdoc />
    public void ClearTimelineMarkers(WritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    /// <summary>
    /// Creates the style for a timeline marker.
    /// </summary>
    private static IStyle CreateTimelineMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(220, 66, 133, 244)), // Google Blue
            Outline = new Pen(Color.White, 2),
            SymbolType = SymbolType.Ellipse
        };
    }
}
