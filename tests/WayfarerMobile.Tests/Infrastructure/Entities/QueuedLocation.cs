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
    /// Gets or sets whether the server explicitly rejected this location.
    /// When true, this location should not be retried (unlike technical failures).
    /// </summary>
    public bool IsServerRejected { get; set; }

    /// <summary>
    /// Gets or sets whether this location was filtered by client-side threshold check.
    /// Used by queue drain service when location doesn't meet time AND distance thresholds.
    /// </summary>
    public bool IsFiltered { get; set; }

    /// <summary>
    /// Gets or sets the reason for filtering.
    /// </summary>
    public string? FilterReason { get; set; }

    /// <summary>
    /// Gets or sets when this record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
