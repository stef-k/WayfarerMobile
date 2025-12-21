using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a trip place stored locally for offline access.
/// </summary>
[Table("OfflinePlaces")]
public class OfflinePlaceEntity
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the trip ID this place belongs to.
    /// </summary>
    [Indexed]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the server-side place ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    public Guid? RegionId { get; set; }

    /// <summary>
    /// Gets or sets the region name.
    /// </summary>
    public string? RegionName { get; set; }

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the place notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the place address.
    /// </summary>
    public string? Address { get; set; }
}
