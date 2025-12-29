using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a trip place stored locally for offline access.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes.
/// </remarks>
[Table("OfflinePlaces")]
public class OfflinePlaceEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TripId { get; set; }

    public Guid ServerId { get; set; }
    public Guid? RegionId { get; set; }
    public string? RegionName { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? IconName { get; set; }
    public string? Website { get; set; }
    public string? Phone { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
