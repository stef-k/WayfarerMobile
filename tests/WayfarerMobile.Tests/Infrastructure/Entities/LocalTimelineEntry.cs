using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a location entry stored locally for timeline display and export.
/// Test copy of the entity from WayfarerMobile.
/// </summary>
[Table("LocalTimelineEntries")]
public class LocalTimelineEntry
{
    /// <summary>
    /// Gets or sets the local unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the server location ID.
    /// </summary>
    [Indexed]
    public int? ServerId { get; set; }

    /// <summary>
    /// Gets or sets the latitude in degrees.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude in degrees.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this location was recorded (UTC).
    /// </summary>
    [Indexed]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the horizontal accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the altitude in meters above sea level.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the speed in meters per second.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the bearing/heading in degrees (0-360).
    /// </summary>
    public double? Bearing { get; set; }

    /// <summary>
    /// Gets or sets the location provider (GPS, Network, etc.).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the short address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the full address.
    /// </summary>
    public string? FullAddress { get; set; }

    /// <summary>
    /// Gets or sets the place/city name.
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// Gets or sets the region/state/province.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the country name.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the postal code.
    /// </summary>
    public string? PostCode { get; set; }

    /// <summary>
    /// Gets or sets the activity type name.
    /// </summary>
    public string? ActivityType { get; set; }

    /// <summary>
    /// Gets or sets user notes for this location.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the timezone identifier.
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Gets or sets when this record was created locally (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this record was last enriched from server (UTC).
    /// </summary>
    public DateTime? LastEnrichedAt { get; set; }

    /// <summary>
    /// Gets or sets the ID of the QueuedLocation that created this entry.
    /// Used for stable mapping between queue and timeline (update/remove on sync).
    /// </summary>
    [Indexed]
    public int? QueuedLocationId { get; set; }
}
