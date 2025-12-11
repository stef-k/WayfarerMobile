using SQLite;
using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents a location data point queued for server synchronization.
/// Stored in SQLite for offline support.
/// </summary>
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
    /// Gets or sets whether the server explicitly rejected this location.
    /// When true, this location should not be retried (unlike technical failures).
    /// Lesson learned: Use dedicated field instead of storing metadata in notes/errors.
    /// </summary>
    public bool IsServerRejected { get; set; }

    /// <summary>
    /// Gets or sets when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets whether this location can be synced (not rejected, under max attempts).
    /// </summary>
    [Ignore]
    public bool CanSync => SyncStatus == SyncStatus.Pending && !IsServerRejected && SyncAttempts < MaxSyncAttempts;

    /// <summary>
    /// Maximum sync attempts before giving up.
    /// </summary>
    public const int MaxSyncAttempts = 5;
}
