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
    /// Gets or sets the turn-by-turn navigation steps.
    /// </summary>
    public List<NavigationStep> Steps { get; set; } = new();

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

    /// <summary>
    /// Gets or sets whether this is a direct/straight-line route (no OSRM data).
    /// </summary>
    public bool IsDirectRoute { get; set; }

    /// <summary>
    /// Gets or sets the initial bearing for direct routes (degrees from north).
    /// </summary>
    public double InitialBearing { get; set; }
}

/// <summary>
/// A single turn-by-turn instruction in the navigation route.
/// </summary>
public class NavigationStep
{
    /// <summary>
    /// Gets or sets the human-readable instruction text.
    /// </summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the distance for this step in meters.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Gets or sets the duration for this step in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maneuver type (turn, depart, arrive, etc.).
    /// </summary>
    public string ManeuverType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude where this step begins.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude where this step begins.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the street name for this step.
    /// </summary>
    public string? StreetName { get; set; }
}
