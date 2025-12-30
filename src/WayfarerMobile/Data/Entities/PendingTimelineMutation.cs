using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a pending timeline location mutation queued for server synchronization.
/// Used for offline support - changes are saved locally first, then synced when online.
/// </summary>
/// <remarks>
/// <para>
/// This entity also stores original values for rollback support. If the server
/// rejects the mutation (4xx error), the local entry can be reverted to its
/// original state even after app restart.
/// </para>
/// </remarks>
[Table("PendingTimelineMutations")]
public class PendingTimelineMutation
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the operation type (e.g., "Update", "Delete").
    /// </summary>
    public string OperationType { get; set; } = "Update";

    /// <summary>
    /// Gets or sets the server location ID being mutated.
    /// </summary>
    [Indexed]
    public int LocationId { get; set; }

    /// <summary>
    /// Gets or sets the local timeline entry ID (for rollback).
    /// </summary>
    public int? LocalEntryId { get; set; }

    #region New Values (what we're changing to)

    /// <summary>
    /// Gets or sets the new latitude (null if not changed).
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the new longitude (null if not changed).
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the new local timestamp (null if not changed).
    /// </summary>
    public DateTime? LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the new notes HTML (null if not changed).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether notes field is included in update (allows setting notes to null).
    /// </summary>
    public bool IncludeNotes { get; set; }

    #endregion

    #region Original Values (for rollback on server rejection)

    /// <summary>
    /// Gets or sets the original latitude before mutation.
    /// Used to rollback if server rejects the change.
    /// </summary>
    public double? OriginalLatitude { get; set; }

    /// <summary>
    /// Gets or sets the original longitude before mutation.
    /// Used to rollback if server rejects the change.
    /// </summary>
    public double? OriginalLongitude { get; set; }

    /// <summary>
    /// Gets or sets the original timestamp before mutation.
    /// Used to rollback if server rejects the change.
    /// </summary>
    public DateTime? OriginalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the original notes before mutation.
    /// Used to rollback if server rejects the change.
    /// </summary>
    public string? OriginalNotes { get; set; }

    /// <summary>
    /// Gets or sets the full deleted entry as JSON.
    /// Used to restore the entry if a delete is rejected by server.
    /// </summary>
    public string? DeletedEntryJson { get; set; }

    #endregion

    #region Sync State

    /// <summary>
    /// Gets or sets when this mutation was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of sync attempts.
    /// </summary>
    public int SyncAttempts { get; set; }

    /// <summary>
    /// Gets or sets the last sync attempt timestamp.
    /// </summary>
    public DateTime? LastSyncAttempt { get; set; }

    /// <summary>
    /// Gets or sets the last error message.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets whether this mutation was rejected (by server 4xx error).
    /// When true, this mutation should not be retried.
    /// </summary>
    [Indexed]
    public bool IsRejected { get; set; }

    /// <summary>
    /// Gets or sets the reason for rejection.
    /// Example: "Server: HTTP 400 Bad Request"
    /// </summary>
    public string? RejectionReason { get; set; }

    #endregion

    /// <summary>
    /// Maximum sync attempts before giving up.
    /// </summary>
    public const int MaxSyncAttempts = 5;

    /// <summary>
    /// Gets whether this mutation can be synced.
    /// </summary>
    [Ignore]
    public bool CanSync => !IsRejected && SyncAttempts < MaxSyncAttempts;

    /// <summary>
    /// Gets whether this mutation has rollback data available.
    /// </summary>
    [Ignore]
    public bool HasRollbackData =>
        OperationType == "Delete"
            ? !string.IsNullOrEmpty(DeletedEntryJson)
            : OriginalLatitude.HasValue || OriginalLongitude.HasValue ||
              OriginalTimestamp.HasValue || OriginalNotes != null;
}
