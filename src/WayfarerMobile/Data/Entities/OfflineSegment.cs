using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a trip segment (route between places) stored locally for offline access.
/// </summary>
[Table("OfflineSegments")]
public class OfflineSegmentEntity
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the trip ID this segment belongs to.
    /// </summary>
    [Indexed]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the server-side segment ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the origin place ID.
    /// </summary>
    [Indexed]
    public Guid OriginId { get; set; }

    /// <summary>
    /// Gets or sets the destination place ID.
    /// </summary>
    [Indexed]
    public Guid DestinationId { get; set; }

    /// <summary>
    /// Gets or sets the transportation mode (walk, drive, transit, etc.).
    /// </summary>
    public string? TransportMode { get; set; }

    /// <summary>
    /// Gets or sets the distance in kilometers.
    /// </summary>
    public double? DistanceKm { get; set; }

    /// <summary>
    /// Gets or sets the duration in minutes.
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the route geometry (encoded polyline) from user-defined segment.
    /// </summary>
    public string? Geometry { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }
}
