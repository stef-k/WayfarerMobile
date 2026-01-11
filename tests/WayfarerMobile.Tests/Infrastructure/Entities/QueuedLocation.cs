using SQLite;
using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a location data point queued for server synchronization.
/// Stored in SQLite for offline support.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes,
/// as the main project targets MAUI-specific frameworks.
/// </remarks>
[Table("QueuedLocations")]
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
    /// Gets or sets user notes for this location (HTML format).
    /// </summary>
    public string? Notes { get; set; }

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
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets whether the server has confirmed receipt of this location.
    /// </summary>
    public bool ServerConfirmed { get; set; }

    /// <summary>
    /// Gets or sets the server-assigned ID for this location.
    /// Stored alongside ServerConfirmed to enable crash recovery reconciliation.
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
    /// Examples: "Client: Time 2.3min &lt; 5min threshold", "Server: HTTP 400 Bad Request"
    /// </summary>
    public string? RejectionReason { get; set; }

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
