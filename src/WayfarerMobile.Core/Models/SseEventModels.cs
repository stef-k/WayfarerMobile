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
