using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a polygon zone (TripArea) stored locally for offline access.
/// These are geographic boundaries drawn on the map (neighborhoods, parks, zones).
/// </summary>
[Table("OfflinePolygons")]
public class OfflinePolygonEntity
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the trip ID this polygon belongs to.
    /// </summary>
    [Indexed]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the region ID this polygon belongs to.
    /// </summary>
    public Guid RegionId { get; set; }

    /// <summary>
    /// Gets or sets the server-side polygon ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the polygon name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the polygon notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the fill color hex (e.g., "#3388FF").
    /// </summary>
    public string? FillColor { get; set; }

    /// <summary>
    /// Gets or sets the stroke/border color hex.
    /// </summary>
    public string? StrokeColor { get; set; }

    /// <summary>
    /// Gets or sets the GeoJSON geometry string.
    /// </summary>
    public string? GeometryGeoJson { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }
}
