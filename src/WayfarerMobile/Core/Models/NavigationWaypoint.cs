using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a waypoint in a navigation route.
/// </summary>
public class NavigationWaypoint
{
    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the waypoint name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the waypoint type.
    /// </summary>
    public WaypointType Type { get; set; }

    /// <summary>
    /// Gets or sets the associated place ID.
    /// </summary>
    public string? PlaceId { get; set; }
}
