using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a trip segment stored locally for offline access.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes.
/// </remarks>
[Table("OfflineSegments")]
public class OfflineSegmentEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TripId { get; set; }

    public Guid ServerId { get; set; }
    public Guid? OriginId { get; set; }
    public Guid? DestinationId { get; set; }
    public string? TransportMode { get; set; }
    public double? DistanceKm { get; set; }
    public int? DurationMinutes { get; set; }
    public string? EncodedPolyline { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
