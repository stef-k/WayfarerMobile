using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a location entry stored locally for timeline display and export.
/// Supports offline-first architecture with server enrichment when online.
/// </summary>
/// <remarks>
/// <para>
/// This entity stores locations captured by the device (via AND filter matching server logic)
/// and caches server-enriched data (addresses, activities) for offline access.
/// </para>
/// <para>
/// <strong>Timestamp handling:</strong> All timestamps are stored in UTC.
/// Use <see cref="Timezone"/> for display conversion when available.
/// </para>
/// </remarks>
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
    /// Null indicates a local-only entry not yet synced to server.
    /// </summary>
    [Indexed]
    public int? ServerId { get; set; }

    #region Core Location Data

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

    #endregion

    #region Server-Enriched Data

    /// <summary>
    /// Gets or sets the short address (e.g., "123 Main St").
    /// Populated from server during reconciliation.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the full address.
    /// Populated from server during reconciliation.
    /// </summary>
    public string? FullAddress { get; set; }

    /// <summary>
    /// Gets or sets the place/city name.
    /// Populated from server during reconciliation.
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// Gets or sets the region/state/province.
    /// Populated from server during reconciliation.
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the country name.
    /// Populated from server during reconciliation.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the postal code.
    /// Populated from server during reconciliation.
    /// </summary>
    public string? PostCode { get; set; }

    /// <summary>
    /// Gets or sets the activity type name (e.g., "Walking", "Driving").
    /// Populated from server during reconciliation.
    /// </summary>
    public string? ActivityType { get; set; }

    /// <summary>
    /// Gets or sets user notes for this location (HTML format).
    /// May be edited locally or synced from server.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the timezone identifier (e.g., "Europe/Athens").
    /// Used for converting UTC timestamp to local display time.
    /// </summary>
    public string? Timezone { get; set; }

    #endregion

    #region Metadata

    /// <summary>
    /// Gets or sets when this record was created locally (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this record was last enriched from server (UTC).
    /// Null indicates never enriched (local-only or pending enrichment).
    /// </summary>
    public DateTime? LastEnrichedAt { get; set; }

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether this entry has been synced to the server.
    /// </summary>
    [Ignore]
    public bool IsSynced => ServerId.HasValue;

    /// <summary>
    /// Gets whether this entry has been enriched with server data.
    /// </summary>
    [Ignore]
    public bool IsEnriched => LastEnrichedAt.HasValue;

    /// <summary>
    /// Gets a display-friendly location string.
    /// </summary>
    [Ignore]
    public string DisplayLocation
    {
        get
        {
            if (!string.IsNullOrEmpty(Place) && !string.IsNullOrEmpty(Country))
                return $"{Place}, {Country}";
            if (!string.IsNullOrEmpty(Place))
                return Place;
            if (!string.IsNullOrEmpty(Country))
                return Country;
            if (!string.IsNullOrEmpty(Address))
                return Address;
            return $"{Latitude:F4}, {Longitude:F4}";
        }
    }

    #endregion
}
