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
/// Use <see cref="TimeZoneId"/> for display conversion when available.
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
    [Column("Timezone")] // Keep DB column name for backward compatibility
    public string? TimeZoneId { get; set; }

    #endregion

    #region Capture Metadata

    /// <summary>
    /// Gets or sets the origin of this location record.
    /// Values: "mobile-log", "mobile-checkin", "api-log", "api-checkin", "queue-import".
    /// Preserved during import/export for roundtrip support.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets whether this location was user-invoked (manual check-in).
    /// Null if unknown (e.g., imported from external source).
    /// </summary>
    public bool? IsUserInvoked { get; set; }

    /// <summary>
    /// Gets or sets the app version that captured this location.
    /// Example: "1.2.3"
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Gets or sets the app build number that captured this location.
    /// Example: "45"
    /// </summary>
    public string? AppBuild { get; set; }

    /// <summary>
    /// Gets or sets the device model that captured this location.
    /// Example: "Pixel 7 Pro", "iPhone 14"
    /// </summary>
    public string? DeviceModel { get; set; }

    /// <summary>
    /// Gets or sets the OS version that captured this location.
    /// Example: "Android 14", "iOS 17.2"
    /// </summary>
    public string? OsVersion { get; set; }

    /// <summary>
    /// Gets or sets the battery level (0-100) when location was captured.
    /// Null if unavailable or unknown.
    /// </summary>
    public int? BatteryLevel { get; set; }

    /// <summary>
    /// Gets or sets whether device was charging when location was captured.
    /// Null if unavailable or unknown.
    /// </summary>
    public bool? IsCharging { get; set; }

    #endregion

    #region Record Metadata

    /// <summary>
    /// Gets or sets when this record was created locally (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this record was last enriched from server (UTC).
    /// Null indicates never enriched (local-only or pending enrichment).
    /// </summary>
    public DateTime? LastEnrichedAt { get; set; }

    /// <summary>
    /// Gets or sets the ID of the QueuedLocation that created this entry.
    /// Used for stable mapping between queue and timeline (update/remove on sync).
    /// Null for entries created from direct online submissions (log-location path).
    /// </summary>
    [Indexed]
    public int? QueuedLocationId { get; set; }

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
