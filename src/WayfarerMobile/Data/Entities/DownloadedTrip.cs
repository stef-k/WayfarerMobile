using SQLite;

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
    /// Gets or sets the download status.
    /// </summary>
    [Indexed]
    public string Status { get; set; } = "pending";

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
}

/// <summary>
/// Download status constants.
/// </summary>
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
