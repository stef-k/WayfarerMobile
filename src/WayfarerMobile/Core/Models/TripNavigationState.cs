using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Core.Models;

/// <summary>
/// Current trip navigation state.
/// </summary>
public class TripNavigationState
{
    /// <summary>
    /// Gets or sets the navigation status.
    /// </summary>
    public NavigationStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the current instruction.
    /// </summary>
    public string? CurrentInstruction { get; set; }

    /// <summary>
    /// Gets or sets the distance to destination in meters.
    /// </summary>
    public double DistanceToDestinationMeters { get; set; }

    /// <summary>
    /// Gets or sets the distance to next waypoint in meters.
    /// </summary>
    public double DistanceToNextWaypointMeters { get; set; }

    /// <summary>
    /// Gets or sets the bearing to destination.
    /// </summary>
    public double BearingToDestination { get; set; }

    /// <summary>
    /// Gets or sets the next waypoint name.
    /// </summary>
    public string? NextWaypointName { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the route progress percentage.
    /// </summary>
    public double ProgressPercent { get; set; }
}
