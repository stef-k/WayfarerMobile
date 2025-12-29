using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a cached map tile for a downloaded trip.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes.
/// </remarks>
[Table("TripTiles")]
public class TripTileEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public int TripId { get; set; }

    public int Zoom { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime DownloadedAt { get; set; }
}
