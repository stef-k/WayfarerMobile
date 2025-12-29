using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a downloaded trip stored locally for offline access.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes,
/// as the main project targets MAUI-specific frameworks.
/// </remarks>
[Table("DownloadedTrips")]
public class DownloadedTripEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public Guid ServerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double BoundingBoxNorth { get; set; }
    public double BoundingBoxSouth { get; set; }
    public double BoundingBoxEast { get; set; }
    public double BoundingBoxWest { get; set; }

    public DateTime DownloadedAt { get; set; }
    public long TotalSizeBytes { get; set; }

    [Indexed]
    public string Status { get; set; } = "pending";

    public int PlaceCount { get; set; }
    public int RegionCount { get; set; }
    public int SegmentCount { get; set; }
    public int AreaCount { get; set; }
    public int TileCount { get; set; }
    public int ProgressPercent { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
    public DateTime? ServerUpdatedAt { get; set; }
    public string? Notes { get; set; }
    public string? CoverImageUrl { get; set; }
}

/// <summary>
/// Download status constants.
/// </summary>
public static class TripDownloadStatus
{
    public const string Pending = "pending";
    public const string Downloading = "downloading";
    public const string Complete = "complete";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string MetadataOnly = "metadata_only";
}
