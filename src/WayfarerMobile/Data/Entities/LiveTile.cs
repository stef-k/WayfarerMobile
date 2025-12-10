using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a cached map tile from live map browsing.
/// These tiles are cached during normal map usage and managed by LRU eviction.
/// </summary>
[Table("LiveTiles")]
public class LiveTileEntity
{
    /// <summary>
    /// Gets or sets the unique tile identifier (z/x/y format).
    /// </summary>
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    [Indexed]
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
    /// Gets or sets the tile source identifier (e.g., "osm", "satellite").
    /// </summary>
    [Indexed]
    public string TileSource { get; set; } = "osm";

    /// <summary>
    /// Gets or sets the file path to the cached tile.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets when the tile was first cached.
    /// </summary>
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when the tile was last accessed (for LRU eviction).
    /// </summary>
    [Indexed]
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the access count (for usage tracking).
    /// </summary>
    public int AccessCount { get; set; } = 1;
}
