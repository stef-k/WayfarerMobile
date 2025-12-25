using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a pending trip mutation (place update) queued for server synchronization.
/// Used for offline support - changes are saved locally first, then synced when online.
/// </summary>
[Table("PendingTripMutations")]
public class PendingTripMutation
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the entity type being mutated (e.g., "Place", "Region").
    /// </summary>
    public string EntityType { get; set; } = "Place";

    /// <summary>
    /// Gets or sets the operation type (e.g., "Update", "Delete").
    /// </summary>
    public string OperationType { get; set; } = "Update";

    /// <summary>
    /// Gets or sets the entity ID being mutated.
    /// </summary>
    [Indexed]
    public Guid EntityId { get; set; }

    /// <summary>
    /// Gets or sets the trip ID this mutation belongs to.
    /// </summary>
    [Indexed]
    public Guid TripId { get; set; }

    /// <summary>
    /// Gets or sets the region ID (for place mutations).
    /// </summary>
    public Guid? RegionId { get; set; }

    /// <summary>
    /// Gets or sets the temporary client ID for create operations.
    /// Used to map the local entity to the server-assigned ID.
    /// </summary>
    public Guid? TempClientId { get; set; }

    /// <summary>
    /// Gets or sets the new name (null if not changed).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the new latitude (null if not changed).
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the new longitude (null if not changed).
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the new notes HTML (null if not changed).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the new display order (null if not changed).
    /// </summary>
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets the new icon name (null if not changed).
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the new marker color (null if not changed).
    /// </summary>
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets whether to clear the icon.
    /// </summary>
    public bool? ClearIcon { get; set; }

    /// <summary>
    /// Gets or sets whether to clear the marker color.
    /// </summary>
    public bool? ClearMarkerColor { get; set; }

    /// <summary>
    /// Gets or sets whether notes field is included in update (allows setting notes to null).
    /// </summary>
    public bool IncludeNotes { get; set; }

    #region Region-specific fields

    /// <summary>
    /// Gets or sets the cover image URL (for regions).
    /// </summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the center latitude (for regions).
    /// </summary>
    public double? CenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the center longitude (for regions).
    /// </summary>
    public double? CenterLongitude { get; set; }

    #endregion

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
    /// Gets or sets whether the server rejected this mutation (4xx error).
    /// When true, this mutation should not be retried.
    /// </summary>
    public bool IsServerRejected { get; set; }

    #region Original values for restoration

    /// <summary>
    /// Gets or sets the original name (for restoration on cancel/rejection).
    /// </summary>
    public string? OriginalName { get; set; }

    /// <summary>
    /// Gets or sets the original latitude (for restoration on cancel/rejection).
    /// </summary>
    public double? OriginalLatitude { get; set; }

    /// <summary>
    /// Gets or sets the original longitude (for restoration on cancel/rejection).
    /// </summary>
    public double? OriginalLongitude { get; set; }

    /// <summary>
    /// Gets or sets the original notes HTML (for restoration on cancel/rejection).
    /// </summary>
    public string? OriginalNotes { get; set; }

    /// <summary>
    /// Gets or sets the original display order (for restoration on cancel/rejection).
    /// </summary>
    public int? OriginalDisplayOrder { get; set; }

    /// <summary>
    /// Gets or sets the original icon name (for restoration on cancel/rejection).
    /// </summary>
    public string? OriginalIconName { get; set; }

    /// <summary>
    /// Gets or sets the original marker color (for restoration on cancel/rejection).
    /// </summary>
    public string? OriginalMarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the original cover image URL (for regions, for restoration).
    /// </summary>
    public string? OriginalCoverImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the original center latitude (for regions, for restoration).
    /// </summary>
    public double? OriginalCenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the original center longitude (for regions, for restoration).
    /// </summary>
    public double? OriginalCenterLongitude { get; set; }

    #endregion

    /// <summary>
    /// Maximum sync attempts before giving up.
    /// </summary>
    public const int MaxSyncAttempts = 5;

    /// <summary>
    /// Gets whether this mutation can be synced.
    /// </summary>
    [Ignore]
    public bool CanSync => !IsServerRejected && SyncAttempts < MaxSyncAttempts;
}
