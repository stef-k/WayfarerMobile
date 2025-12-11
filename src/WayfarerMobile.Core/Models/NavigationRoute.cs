namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a calculated navigation route.
/// </summary>
public class NavigationRoute
{
    /// <summary>
    /// Gets or sets the route waypoints.
    /// </summary>
    public List<NavigationWaypoint> Waypoints { get; set; } = new();

    /// <summary>
    /// Gets or sets the destination name.
    /// </summary>
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total distance in meters.
    /// </summary>
    public double TotalDistanceMeters { get; set; }

    /// <summary>
    /// Gets or sets the estimated duration.
    /// </summary>
    public TimeSpan EstimatedDuration { get; set; }
}
