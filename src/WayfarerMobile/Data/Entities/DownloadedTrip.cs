using SQLite;
using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a downloaded trip stored locally for offline access.
/// </summary>
[Table("DownloadedTrips")]
public class DownloadedTripEntity
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the server-side trip ID.
    /// </summary>
    [Indexed]
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bounding box north latitude.
    /// </summary>
    public double BoundingBoxNorth { get; set; }

    /// <summary>
    /// Gets or sets the bounding box south latitude.
    /// </summary>
    public double BoundingBoxSouth { get; set; }

    /// <summary>
    /// Gets or sets the bounding box east longitude.
    /// </summary>
    public double BoundingBoxEast { get; set; }

    /// <summary>
    /// Gets or sets the bounding box west longitude.
    /// </summary>
    public double BoundingBoxWest { get; set; }

    /// <summary>
    /// Gets or sets when the trip was downloaded.
    /// </summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>
    /// Gets or sets the total size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the legacy download status string.
    /// </summary>
    /// <remarks>
    /// Kept for backwards compatibility. Use <see cref="UnifiedState"/> for new code.
    /// </remarks>
    [Indexed]
    [Obsolete("Use UnifiedState instead. This property is kept for migration compatibility.")]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Gets or sets the unified download state as an integer for SQLite storage.
    /// </summary>
    /// <remarks>
    /// SQLite-net-pcl stores enums by name as strings. We use int for:
    /// 1. More efficient storage and indexing
    /// 2. Faster queries (int comparison vs string)
    /// 3. Safe enum value changes (renaming won't break data)
    /// </remarks>
    [Indexed]
    public int UnifiedStateValue { get; set; } = (int)UnifiedDownloadState.ServerOnly;

    /// <summary>
    /// Gets or sets the unified download state.
    /// This is the single source of truth for download state.
    /// </summary>
    [Ignore]
    public UnifiedDownloadState UnifiedState
    {
        get => (UnifiedDownloadState)UnifiedStateValue;
        set => UnifiedStateValue = (int)value;
    }

    /// <summary>
    /// Gets or sets additional context for paused states.
    /// </summary>
    /// <remarks>
    /// Examples: "User requested", "Network unavailable", "Storage at 95%".
    /// </remarks>
    public string? PauseReason { get; set; }

    /// <summary>
    /// Gets or sets when the state last changed.
    /// </summary>
    public DateTime StateChangedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of tiles completed during download.
    /// </summary>
    /// <remarks>
    /// Used to track progress for pause/resume functionality.
    /// </remarks>
    public int TilesCompleted { get; set; }

    /// <summary>
    /// Gets or sets the total number of tiles expected for this download.
    /// </summary>
    public int TilesTotal { get; set; }

    /// <summary>
    /// Gets or sets the number of places in this trip.
    /// </summary>
    public int PlaceCount { get; set; }

    /// <summary>
    /// Gets or sets the number of regions in this trip.
    /// </summary>
    public int RegionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of segments in this trip.
    /// </summary>
    public int SegmentCount { get; set; }

    /// <summary>
    /// Gets or sets the number of areas (geographic polygons) in this trip.
    /// </summary>
    public int AreaCount { get; set; }

    /// <summary>
    /// Gets or sets the number of tiles downloaded.
    /// </summary>
    public int TileCount { get; set; }

    /// <summary>
    /// Gets or sets the download progress (0-100).
    /// </summary>
    public int ProgressPercent { get; set; }

    /// <summary>
    /// Gets or sets the last error message if download failed.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets when this record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the server version for sync tracking.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the server's last update timestamp.
    /// </summary>
    public DateTime? ServerUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the trip notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the trip cover image URL.
    /// </summary>
    public string? CoverImageUrl { get; set; }

    #region Computed Properties (not persisted)

    /// <summary>
    /// Gets whether metadata (places, regions, etc.) is complete.
    /// </summary>
    [Ignore]
    public bool IsMetadataComplete => PlaceCount > 0 || RegionCount > 0 || AreaCount > 0;

    /// <summary>
    /// Gets whether this trip has any downloaded tiles.
    /// </summary>
    [Ignore]
    public bool HasTiles => TileCount > 0;

    /// <summary>
    /// Gets whether the trip can be loaded to map.
    /// Delegates to the unified state extension method.
    /// </summary>
    [Ignore]
    public bool CanLoad => UnifiedState.CanLoadToMap();

    /// <summary>
    /// Gets whether the download can be resumed.
    /// Delegates to the unified state extension method.
    /// </summary>
    [Ignore]
    public bool CanResume => UnifiedState.CanResume();

    /// <summary>
    /// Gets whether the download can be paused.
    /// Delegates to the unified state extension method.
    /// </summary>
    [Ignore]
    public bool CanPause => UnifiedState.CanPause();

    #endregion
}

/// <summary>
/// Download status constants.
/// </summary>
/// <remarks>
/// These constants are deprecated. Use <see cref="UnifiedDownloadState"/> instead.
/// Kept for migration compatibility.
/// </remarks>
[Obsolete("Use UnifiedDownloadState enum instead.")]
public static class TripDownloadStatus
{
    /// <summary>Pending download.</summary>
    public const string Pending = "pending";

    /// <summary>Currently downloading.</summary>
    public const string Downloading = "downloading";

    /// <summary>Download complete.</summary>
    public const string Complete = "complete";

    /// <summary>Download failed.</summary>
    public const string Failed = "failed";

    /// <summary>Download cancelled by user.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Metadata only (places without tiles).</summary>
    public const string MetadataOnly = "metadata_only";
}
