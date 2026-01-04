using Mapsui;
using Mapsui.Nts;
using Mapsui.Layers;
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
/// Service for managing group-related map layers.
/// Handles group member markers and historical location breadcrumbs.
/// Registered as Singleton - stateless, pure rendering functions.
/// </summary>
public class GroupLayerService : IGroupLayerService
{
    private readonly ILogger<GroupLayerService> _logger;

    /// <summary>
    /// Creates a new instance of GroupLayerService.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public GroupLayerService(ILogger<GroupLayerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string GroupMembersLayerName => "GroupMembers";

    /// <inheritdoc />
    public string HistoricalLocationsLayerName => "HistoricalLocations";

    /// <inheritdoc />
    public List<MPoint> UpdateGroupMemberMarkers(WritableLayer layer, IEnumerable<GroupMemberLocation> members, double liveMarkerPulseScale = 1.0)
    {
        layer.Clear();

        var points = new List<MPoint>();
        var memberList = members.ToList();

        _logger.LogDebug("UpdateGroupMemberMarkers called with {MemberCount} members, pulse scale {Scale:F2}", memberList.Count, liveMarkerPulseScale);

        foreach (var member in memberList)
        {
            if (member.Latitude == 0 && member.Longitude == 0)
            {
                _logger.LogDebug("Skipping member {DisplayName} - zero coordinates", member.DisplayName);
                continue;
            }

            var (x, y) = SphericalMercator.FromLonLat(member.Longitude, member.Latitude);
            var point = new MPoint(x, y);
            points.Add(point);

            // Parse member color
            var color = MapsuiColorHelper.ParseHexColor(member.ColorHex);

            // Create compound marker styles based on live status
            var styles = member.IsLive
                ? CreateLiveMarkerStyles(color, liveMarkerPulseScale)
                : CreateLatestMarkerStyles(color);

            // Add marker with properties for tap handling
            var markerPoint = new Point(point.X, point.Y);
            var feature = new GeometryFeature(markerPoint)
            {
                Styles = styles
            };

            // Add properties for tap identification
            feature["UserId"] = member.UserId;
            feature["DisplayName"] = member.DisplayName;
            feature["IsLive"] = member.IsLive;

            layer.Add(feature);
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {MarkerCount} group member markers", points.Count);

        return points;
    }

    /// <summary>
    /// Creates marker styles for a live (currently sharing) member.
    /// RED outer ring indicates "live/actively sharing".
    /// The outer ring pulses with the provided scale to indicate active sharing.
    /// </summary>
    /// <param name="memberColor">The member's assigned color.</param>
    /// <param name="pulseScale">Scale multiplier for pulse animation (1.0 to 1.35).</param>
    private static IStyle[] CreateLiveMarkerStyles(Color memberColor, double pulseScale = 1.0)
    {
        // Calculate opacity for pulse effect (fades slightly at max scale)
        var pulseOpacity = (byte)(255 - (pulseScale - 1.0) * 200); // 255 at 1.0, ~185 at 1.35

        return new IStyle[]
        {
            // Pulsing outer glow (semi-transparent, scales with animation)
            new SymbolStyle
            {
                SymbolScale = 0.8 * pulseScale,
                Fill = new Brush(Color.FromArgb(pulseOpacity, 244, 67, 54)), // Material Red with pulse opacity
                SymbolType = SymbolType.Ellipse
            },
            // Outer ring (RED for live status - static)
            new SymbolStyle
            {
                SymbolScale = 0.8,
                Fill = new Brush(Color.FromArgb(255, 244, 67, 54)), // Material Red
                Outline = new Pen(Color.White, 2),
                SymbolType = SymbolType.Ellipse
            },
            // Middle ring (white separator)
            new SymbolStyle
            {
                SymbolScale = 0.6,
                Fill = new Brush(Color.White),
                SymbolType = SymbolType.Ellipse
            },
            // Inner dot (member's color)
            new SymbolStyle
            {
                SymbolScale = 0.45,
                Fill = new Brush(memberColor),
                SymbolType = SymbolType.Ellipse
            }
        };
    }

    /// <summary>
    /// Creates marker styles for a member with latest (not live) location.
    /// GREEN outer ring indicates "latest known location".
    /// </summary>
    private static IStyle[] CreateLatestMarkerStyles(Color memberColor)
    {
        return new IStyle[]
        {
            // Outer ring (GREEN for latest/not-live)
            new SymbolStyle
            {
                SymbolScale = 0.8,
                Fill = new Brush(Color.FromArgb(255, 76, 175, 80)), // Material Green
                Outline = new Pen(Color.White, 2),
                SymbolType = SymbolType.Ellipse
            },
            // Middle ring (white separator)
            new SymbolStyle
            {
                SymbolScale = 0.6,
                Fill = new Brush(Color.White),
                SymbolType = SymbolType.Ellipse
            },
            // Inner dot (member's color)
            new SymbolStyle
            {
                SymbolScale = 0.45,
                Fill = new Brush(memberColor),
                SymbolType = SymbolType.Ellipse
            }
        };
    }

    /// <inheritdoc />
    public void UpdateHistoricalLocationMarkers(
        WritableLayer layer,
        IEnumerable<GroupLocationResult> locations,
        Dictionary<string, string> memberColors)
    {
        layer.Clear();

        var locationList = locations.ToList();
        _logger.LogDebug("UpdateHistoricalLocationMarkers called with {LocationCount} locations", locationList.Count);

        foreach (var location in locationList)
        {
            if (location.Latitude == 0 && location.Longitude == 0)
                continue;

            var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            var point = new MPoint(x, y);

            // Get member color or use default
            var colorHex = location.UserId != null && memberColors.TryGetValue(location.UserId, out var hex)
                ? hex
                : "#4285F4"; // Default blue
            var color = MapsuiColorHelper.ParseHexColor(colorHex);

            // Create smaller, semi-transparent marker for historical points
            var style = new SymbolStyle
            {
                SymbolScale = 0.35,
                Fill = new Brush(Color.FromArgb(180, color.R, color.G, color.B)),
                Outline = new Pen(Color.FromArgb(200, 255, 255, 255), 1),
                SymbolType = SymbolType.Ellipse
            };

            var feature = new GeometryFeature(new Point(point.X, point.Y))
            {
                Styles = new[] { style }
            };

            // Add properties for tap identification
            feature["UserId"] = location.UserId ?? "";
            feature["LocationId"] = location.Id;
            feature["Timestamp"] = location.LocalTimestamp.ToString("g");
            feature["TimestampUtc"] = location.Timestamp; // Use UTC timestamp directly from server
            feature["Latitude"] = location.Latitude;
            feature["Longitude"] = location.Longitude;
            feature["IsHistorical"] = true;

            layer.Add(feature);
        }

        layer.DataHasChanged();
        _logger.LogDebug("Added {LocationCount} historical markers", locationList.Count);
    }
}
