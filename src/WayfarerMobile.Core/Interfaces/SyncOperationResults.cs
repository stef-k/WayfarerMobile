namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Result type for sync operations (completed, queued, or rejected).
/// </summary>
public enum SyncResultType
{
    /// <summary>
    /// Operation synced to server successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Operation queued for later sync (offline or temporary failure).
    /// </summary>
    Queued,

    /// <summary>
    /// Operation rejected by server (4xx client error).
    /// </summary>
    Rejected
}

/// <summary>
/// Result of a place operation (create, update, delete).
/// </summary>
public record PlaceOperationResult
{
    /// <summary>
    /// Whether the operation succeeded (even if queued).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The entity ID (server ID or temp client ID for create, place ID for update/delete).
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// The temporary client ID used before server sync (for create operations).
    /// Used to reconcile in-memory objects with server-assigned IDs.
    /// </summary>
    public Guid? TempClientId { get; init; }

    /// <summary>
    /// The type of sync result.
    /// </summary>
    public SyncResultType ResultType { get; init; }

    /// <summary>
    /// Optional message describing the result.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Creates a successful completed result with temp ID tracking.
    /// </summary>
    public static PlaceOperationResult Completed(Guid entityId, Guid? tempClientId = null, string? message = null)
        => new() { Success = true, EntityId = entityId, TempClientId = tempClientId, ResultType = SyncResultType.Completed, Message = message };

    /// <summary>
    /// Creates a successful queued result.
    /// </summary>
    public static PlaceOperationResult Queued(Guid entityId, string? message = null)
        => new() { Success = true, EntityId = entityId, TempClientId = entityId, ResultType = SyncResultType.Queued, Message = message };

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static PlaceOperationResult Rejected(string message)
        => new() { Success = false, EntityId = null, TempClientId = null, ResultType = SyncResultType.Rejected, Message = message };
}

/// <summary>
/// Result of a region operation (create, update, delete).
/// </summary>
public record RegionOperationResult
{
    /// <summary>
    /// Whether the operation succeeded (even if queued).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The entity ID (server ID or temp client ID for create, region ID for update/delete).
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// The temporary client ID used before server sync (for create operations).
    /// Used to reconcile in-memory objects with server-assigned IDs.
    /// </summary>
    public Guid? TempClientId { get; init; }

    /// <summary>
    /// The type of sync result.
    /// </summary>
    public SyncResultType ResultType { get; init; }

    /// <summary>
    /// Optional message describing the result.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Creates a successful completed result with temp ID tracking.
    /// </summary>
    public static RegionOperationResult Completed(Guid entityId, Guid? tempClientId = null, string? message = null)
        => new() { Success = true, EntityId = entityId, TempClientId = tempClientId, ResultType = SyncResultType.Completed, Message = message };

    /// <summary>
    /// Creates a successful queued result.
    /// </summary>
    public static RegionOperationResult Queued(Guid entityId, string? message = null)
        => new() { Success = true, EntityId = entityId, TempClientId = entityId, ResultType = SyncResultType.Queued, Message = message };

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static RegionOperationResult Rejected(string message)
        => new() { Success = false, EntityId = null, TempClientId = null, ResultType = SyncResultType.Rejected, Message = message };
}

/// <summary>
/// Result of a trip entity operation (trip, segment, or area update).
/// </summary>
public record EntityOperationResult
{
    /// <summary>
    /// Whether the operation succeeded (even if queued).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The entity ID (trip ID, segment ID, or area ID).
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// The type of sync result.
    /// </summary>
    public SyncResultType ResultType { get; init; }

    /// <summary>
    /// Optional message describing the result.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Creates a successful completed result.
    /// </summary>
    public static EntityOperationResult Completed(Guid entityId, string? message = null)
        => new() { Success = true, EntityId = entityId, ResultType = SyncResultType.Completed, Message = message };

    /// <summary>
    /// Creates a successful queued result.
    /// </summary>
    public static EntityOperationResult Queued(Guid entityId, string? message = null)
        => new() { Success = true, EntityId = entityId, ResultType = SyncResultType.Queued, Message = message };

    /// <summary>
    /// Creates a rejected result.
    /// </summary>
    public static EntityOperationResult Rejected(string message)
        => new() { Success = false, EntityId = null, ResultType = SyncResultType.Rejected, Message = message };
}
