namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Event args for sync failure.
/// </summary>
public class SyncFailureEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that failed to sync.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this was a client error (4xx).
    /// </summary>
    public bool IsClientError { get; set; }

    /// <summary>
    /// Gets or sets the entity type (Place, Region, Segment, Area, Trip).
    /// </summary>
    public string? EntityType { get; set; }
}

/// <summary>
/// Event args for sync queued.
/// </summary>
public class SyncQueuedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that was queued.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Event args for sync success.
/// </summary>
public class SyncSuccessEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that synced successfully.
    /// </summary>
    public Guid EntityId { get; set; }
}

/// <summary>
/// Event args for entity created with server-assigned ID.
/// </summary>
public class EntityCreatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the temporary client ID used before sync.
    /// </summary>
    public Guid TempClientId { get; set; }

    /// <summary>
    /// Gets or sets the server-assigned ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the entity type (Place, Region).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
}
