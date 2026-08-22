using SQLite;
using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Data.Entities;

/// <summary>Provider-independent Trip content stored for offline use.</summary>
[Table("DownloadedTrips")]
public sealed class DownloadedTripEntity
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    [Indexed] public Guid ServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double BoundingBoxNorth { get; set; }
    public double BoundingBoxSouth { get; set; }
    public double BoundingBoxEast { get; set; }
    public double BoundingBoxWest { get; set; }
    public DateTime DownloadedAt { get; set; }
    [Indexed] public int UnifiedStateValue { get; set; } = (int)UnifiedDownloadState.ServerOnly;
    [Ignore] public UnifiedDownloadState UnifiedState
    {
        get => (UnifiedDownloadState)UnifiedStateValue;
        set => UnifiedStateValue = (int)value;
    }
    public DateTime StateChangedAt { get; set; } = DateTime.UtcNow;
    public int PlaceCount { get; set; }
    public int RegionCount { get; set; }
    public int SegmentCount { get; set; }
    public int AreaCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; }
    public DateTime? ServerUpdatedAt { get; set; }
    public string? Notes { get; set; }
    public string? CoverImageUrl { get; set; }
    [Ignore] public bool IsMetadataComplete => PlaceCount > 0 || RegionCount > 0 || SegmentCount > 0 || AreaCount > 0;
    [Ignore] public bool CanLoad => UnifiedState.CanLoadToMap();
}
