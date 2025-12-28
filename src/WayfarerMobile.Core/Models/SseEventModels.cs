namespace WayfarerMobile.Core.Models;

/// <summary>
/// SSE event for location updates.
/// Received from channels: location-update-{userName} or group-{groupId} (consolidated)
/// </summary>
public class SseLocationEvent
{
    /// <summary>Location ID from the server.</summary>
    public int LocationId { get; set; }

    /// <summary>UTC timestamp of the location.</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>User ID who logged this location.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Username of the person.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Whether the user is currently live/active.</summary>
    public bool IsLive { get; set; }

    /// <summary>Type of location event (e.g., "check-in").</summary>
    public string? Type { get; set; }
}

/// <summary>
/// SSE event for location deletion.
/// Received from consolidated channel: group-{groupId}
/// </summary>
public class SseLocationDeletedEvent
{
    /// <summary>ID of the deleted location.</summary>
    public int LocationId { get; set; }

    /// <summary>User ID who owned the deleted location.</summary>
    public string UserId { get; set; } = string.Empty;
}

/// <summary>
/// Event arguments for location deleted SSE events.
/// </summary>
public class SseLocationDeletedEventArgs : EventArgs
{
    /// <summary>The location deleted event data.</summary>
    public SseLocationDeletedEvent LocationDeleted { get; }

    /// <summary>Creates a new instance.</summary>
    public SseLocationDeletedEventArgs(SseLocationDeletedEvent locationDeleted) => LocationDeleted = locationDeleted;
}

/// <summary>
/// SSE event for invitation creation.
/// Received from consolidated channel: group-{groupId}
/// </summary>
public class SseInviteCreatedEvent
{
    /// <summary>ID of the created invitation.</summary>
    public Guid InvitationId { get; set; }
}

/// <summary>
/// Event arguments for invite created SSE events.
/// </summary>
public class SseInviteCreatedEventArgs : EventArgs
{
    /// <summary>The invite created event data.</summary>
    public SseInviteCreatedEvent InviteCreated { get; }

    /// <summary>Creates a new instance.</summary>
    public SseInviteCreatedEventArgs(SseInviteCreatedEvent inviteCreated) => InviteCreated = inviteCreated;
}

/// <summary>
/// SSE event for membership changes.
/// Received from consolidated channel: group-{groupId}
/// </summary>
public class SseMembershipEvent
{
    /// <summary>
    /// Event type: "visibility-changed", "member-left", "member-removed",
    /// "member-joined", "invite-declined", "invite-revoked".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>User ID affected by the action.</summary>
    public string? UserId { get; set; }

    /// <summary>For visibility-changed: whether visibility is disabled.</summary>
    public bool? Disabled { get; set; }
}

/// <summary>
/// Event arguments for location SSE events.
/// </summary>
public class SseLocationEventArgs : EventArgs
{
    /// <summary>The location event data.</summary>
    public SseLocationEvent Location { get; }

    /// <summary>Creates a new instance.</summary>
    public SseLocationEventArgs(SseLocationEvent location) => Location = location;
}

/// <summary>
/// Event arguments for membership SSE events.
/// </summary>
public class SseMembershipEventArgs : EventArgs
{
    /// <summary>The membership event data.</summary>
    public SseMembershipEvent Membership { get; }

    /// <summary>Creates a new instance.</summary>
    public SseMembershipEventArgs(SseMembershipEvent membership) => Membership = membership;
}

/// <summary>
/// Event arguments for SSE reconnection attempts.
/// </summary>
public class SseReconnectEventArgs : EventArgs
{
    /// <summary>Reconnection attempt number (1-indexed).</summary>
    public int Attempt { get; }

    /// <summary>Delay in milliseconds before reconnecting.</summary>
    public int DelayMs { get; }

    /// <summary>Creates a new instance.</summary>
    public SseReconnectEventArgs(int attempt, int delayMs)
    {
        Attempt = attempt;
        DelayMs = delayMs;
    }
}

/// <summary>
/// SSE event for visit notifications.
/// Received from channel: user-visits-{userId}
/// </summary>
public class SseVisitStartedEvent
{
    /// <summary>Unique identifier of the visit event.</summary>
    public Guid VisitId { get; set; }

    /// <summary>Trip ID containing the visited place.</summary>
    public Guid TripId { get; set; }

    /// <summary>Trip name (snapshot at visit time).</summary>
    public string TripName { get; set; } = string.Empty;

    /// <summary>Place ID that was visited (null if place deleted).</summary>
    public Guid? PlaceId { get; set; }

    /// <summary>Place name (snapshot at visit time).</summary>
    public string PlaceName { get; set; } = string.Empty;

    /// <summary>Region name containing the place.</summary>
    public string RegionName { get; set; } = string.Empty;

    /// <summary>UTC timestamp when visit was confirmed.</summary>
    public DateTime ArrivedAtUtc { get; set; }

    /// <summary>Latitude of the visited place.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude of the visited place.</summary>
    public double? Longitude { get; set; }

    /// <summary>Icon name for the place marker.</summary>
    public string? IconName { get; set; }

    /// <summary>Marker color for the place.</summary>
    public string? MarkerColor { get; set; }
}

/// <summary>
/// Event arguments for visit started SSE events.
/// </summary>
public class SseVisitStartedEventArgs : EventArgs
{
    /// <summary>The visit started event data.</summary>
    public SseVisitStartedEvent Visit { get; }

    /// <summary>Creates a new instance.</summary>
    public SseVisitStartedEventArgs(SseVisitStartedEvent visit) => Visit = visit;
}
