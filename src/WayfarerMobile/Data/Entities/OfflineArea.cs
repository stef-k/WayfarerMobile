using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a trip area/region stored locally for offline access.
/// Areas group places within a trip (e.g., cities, neighborhoods).
/// </summary>
[Table("OfflineAreas")]
public class OfflineAreaEntity
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the trip ID this area belongs to.
    /// </summary>
    [Indexed]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the server-side area/region ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the area name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the area description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the center latitude.
    /// </summary>
    public double? CenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the center longitude.
    /// </summary>
    public double? CenterLongitude { get; set; }

    /// <summary>
    /// Gets or sets the bounding box north latitude.
    /// </summary>
    public double? BoundsNorth { get; set; }

    /// <summary>
    /// Gets or sets the bounding box south latitude.
    /// </summary>
    public double? BoundsSouth { get; set; }

    /// <summary>
    /// Gets or sets the bounding box east longitude.
    /// </summary>
    public double? BoundsEast { get; set; }

    /// <summary>
    /// Gets or sets the bounding box west longitude.
    /// </summary>
    public double? BoundsWest { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the number of places in this area.
    /// </summary>
    public int PlaceCount { get; set; }
}
