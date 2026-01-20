using SQLite;
using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a location data point queued for server synchronization.
/// Stored in SQLite for offline support.
/// </summary>
[Table("QueuedLocations")]
// Composite index for efficient claim queries: WHERE SyncStatus = Pending AND IsRejected = 0 ORDER BY Timestamp
// Note: SQLite-net-pcl doesn't support composite indexes via attributes, so this is created in DbInitializer
public class QueuedLocation
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the latitude in degrees.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude in degrees.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the altitude in meters above sea level.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the horizontal accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the speed in meters per second.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the bearing/heading in degrees (0-360).
    /// </summary>
    public double? Bearing { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this location was recorded.
    /// </summary>
    [Indexed]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the location provider (GPS, Network, etc.).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the sync status of this location.
    /// </summary>
    [Indexed]
    public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;

    /// <summary>
    /// Gets or sets the number of times sync has been attempted.
    /// </summary>
    public int SyncAttempts { get; set; }

    /// <summary>
    /// Gets or sets the last sync attempt timestamp.
    /// </summary>
    public DateTime? LastSyncAttempt { get; set; }

    /// <summary>
    /// Gets or sets the error message from the last failed sync attempt.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Gets or sets a unique key for idempotent sync operations.
    /// Once the server confirms receipt, this key is marked to prevent duplicate re-sends
    /// if the app crashes between API success and local DB update.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets whether the server has confirmed receipt of this location.
    /// Set to true after successful API response, BEFORE marking as Synced.
    /// Used to prevent duplicate sync attempts on crash recovery.
    /// </summary>
    public bool ServerConfirmed { get; set; }

    /// <summary>
    /// Gets or sets the server-assigned ID for this location.
    /// Stored alongside ServerConfirmed to enable crash recovery reconciliation
    /// with local timeline entries that may be missing their ServerId.
    /// </summary>
    public int? ServerId { get; set; }

    /// <summary>
    /// Gets or sets whether this location was rejected (by client threshold check or server).
    /// When true, this location should not be retried.
    /// </summary>
    [Indexed]
    public bool IsRejected { get; set; }

    /// <summary>
    /// Gets or sets the reason for rejection.
    /// Examples: "Client: Time 2.3min below 5min threshold", "Server: HTTP 400 Bad Request"
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Gets or sets whether this location was user-invoked (manual check-in).
    /// User-invoked locations skip all client-side filtering and are prioritized in sync queue.
    /// False for background/live locations (apply filtering).
    /// </summary>
    [Indexed]
    public bool IsUserInvoked { get; set; }

    /// <summary>
    /// Gets or sets the activity type ID for manual check-ins.
    /// Only populated for user-invoked locations (IsUserInvoked = true).
    /// </summary>
    public int? ActivityTypeId { get; set; }

    /// <summary>
    /// Gets or sets notes for this location.
    /// Only populated for user-invoked locations (IsUserInvoked = true).
    /// </summary>
    public string? CheckInNotes { get; set; }

    #region Metadata Fields (for diagnostics and export)

    /// <summary>
    /// Gets or sets the device's timezone ID when the location was captured.
    /// Used for accurate LocalTimestamp calculation during export.
    /// Example: "Europe/Athens", "America/New_York"
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Gets or sets the mobile app version when the location was captured.
    /// Example: "1.2.3"
    /// </summary>
    public string? AppVersion { get; set; }

    /// <summary>
    /// Gets or sets the mobile app build number when the location was captured.
    /// Example: "45"
    /// </summary>
    public string? AppBuild { get; set; }

    /// <summary>
    /// Gets or sets the device model when the location was captured.
    /// Example: "Pixel 7 Pro", "iPhone 14"
    /// </summary>
    public string? DeviceModel { get; set; }

    /// <summary>
    /// Gets or sets the OS version when the location was captured.
    /// Example: "Android 14", "iOS 17.2"
    /// </summary>
    public string? OsVersion { get; set; }

    /// <summary>
    /// Gets or sets the battery level (0-100) when the location was captured.
    /// Null if unavailable.
    /// </summary>
    public int? BatteryLevel { get; set; }

    /// <summary>
    /// Gets or sets whether the device was charging when the location was captured.
    /// Null if unavailable.
    /// </summary>
    public bool? IsCharging { get; set; }

    #endregion

    /// <summary>
    /// Gets or sets when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets whether this location can be synced (pending and not rejected).
    /// Valid locations retry until 300-day purge regardless of attempt count.
    /// </summary>
    [Ignore]
    public bool CanSync => SyncStatus == SyncStatus.Pending && !IsRejected;
}
