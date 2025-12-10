namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Types of route waypoints.
/// </summary>
public enum WaypointType
{
    /// <summary>Starting point.</summary>
    Start,

    /// <summary>Intermediate waypoint (place).</summary>
    Waypoint,

    /// <summary>Route geometry point.</summary>
    RoutePoint,

    /// <summary>Final destination.</summary>
    Destination
}
