using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a cached map tile for a downloaded trip.
/// </summary>
[Table("TripTiles")]
public class TripTileEntity
{
    /// <summary>
    /// Gets or sets the unique tile identifier (z/x/y format).
    /// </summary>
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trip ID this tile belongs to.
    /// </summary>
    [Indexed]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// Gets or sets the X tile coordinate.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Gets or sets the Y tile coordinate.
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// Gets or sets the file path to the cached tile.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets when the tile was downloaded.
    /// </summary>
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
