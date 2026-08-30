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
    /// Gets or sets whether this is a direct/straight-line route rather than routed geometry.
    /// </summary>
    public bool IsDirectRoute { get; set; }

    /// <summary>
    /// Gets or sets the initial bearing for direct routes (degrees from north).
    /// </summary>
    public double InitialBearing { get; set; }

    /// <summary>Gets transient linked attribution for the active hosted route.</summary>
    public List<HostedRouteAttribution> Attribution { get; set; } = new();

    /// <summary>Gets or sets safe transient provenance for the active hosted route.</summary>
    public HostedRouteProvenance? HostedProvenance { get; set; }
}

/// <summary>Contains one safe linked attribution displayed only with an active hosted route.</summary>
public sealed record HostedRouteAttribution(string Text, string Url);

/// <summary>Safe memory-only provenance retained with an active hosted route.</summary>
public sealed record HostedRouteProvenance(
    Guid TransportProfileId,
    string SelectedProfileAuthorityIdentity,
    string Provider,
    Guid ProviderConfigurationId,
    string MappingIdentity,
    string StorageMode,
    DateTimeOffset GeneratedAt);

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
