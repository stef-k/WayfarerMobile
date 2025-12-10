namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for navigation audio announcements.
/// </summary>
public interface INavigationAudioService
{
    /// <summary>
    /// Gets or sets whether audio announcements are enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Announces the start of navigation.
    /// </summary>
    /// <param name="destinationName">The destination name.</param>
    /// <param name="totalDistanceMeters">Total distance in meters.</param>
    Task AnnounceNavigationStartAsync(string destinationName, double totalDistanceMeters);

    /// <summary>
    /// Announces approaching a waypoint.
    /// </summary>
    /// <param name="waypointName">The waypoint name.</param>
    /// <param name="distanceMeters">Distance to waypoint in meters.</param>
    /// <param name="transportMode">The transport mode (walk, drive, etc.).</param>
    Task AnnounceApproachingWaypointAsync(string waypointName, double distanceMeters, string? transportMode);

    /// <summary>
    /// Announces arrival at a waypoint.
    /// </summary>
    /// <param name="waypointName">The waypoint name.</param>
    Task AnnounceArrivalAsync(string waypointName);

    /// <summary>
    /// Announces that the user is off route.
    /// </summary>
    Task AnnounceOffRouteAsync();

    /// <summary>
    /// Announces that the route has been recalculated.
    /// </summary>
    Task AnnounceReroutingAsync();

    /// <summary>
    /// Announces route completion.
    /// </summary>
    /// <param name="destinationName">The destination name.</param>
    Task AnnounceRouteCompleteAsync(string destinationName);

    /// <summary>
    /// Stops any current announcement.
    /// </summary>
    Task StopAsync();
}
