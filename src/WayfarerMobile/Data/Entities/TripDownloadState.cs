using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents the persistent state of a paused or interrupted trip download.
/// Used for pause/resume functionality.
/// </summary>
[Table("TripDownloadStates")]
public class TripDownloadStateEntity
{
    /// <summary>
    /// Gets or sets the unique identifier (same as TripId for 1:1 relationship).
    /// </summary>
    [PrimaryKey]
    public int TripId { get; set; }

    /// <summary>
    /// Gets or sets the server-side trip ID for reference.
    /// </summary>
    [Indexed]
    public Guid TripServerId { get; set; }

    /// <summary>
    /// Gets or sets the trip name for display.
    /// </summary>
    public string TripName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the remaining tile coordinates as JSON array.
    /// Format: [{"Zoom":15,"X":123,"Y":456}, ...]
    /// </summary>
    public string RemainingTilesJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the number of tiles already downloaded.
    /// </summary>
    public int CompletedTileCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of tiles to download.
    /// </summary>
    public int TotalTileCount { get; set; }

    /// <summary>
    /// Gets or sets the total bytes downloaded so far.
    /// </summary>
    public long DownloadedBytes { get; set; }

    /// <summary>
    /// Gets or sets the download state status.
    /// </summary>
    [Indexed]
    public string Status { get; set; } = DownloadStateStatus.Paused;

    /// <summary>
    /// Gets or sets the reason for interruption/pause.
    /// </summary>
    public string InterruptionReason { get; set; } = DownloadPauseReason.UserPause;

    /// <summary>
    /// Gets or sets when the download was paused/interrupted.
    /// </summary>
    public DateTime PausedAt { get; set; }

    /// <summary>
    /// Gets or sets the last time state was saved (for periodic saves).
    /// </summary>
    public DateTime LastSaveTime { get; set; }

    /// <summary>
    /// Gets or sets when this state was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Download state status constants.
/// </summary>
public static class DownloadStateStatus
{
    /// <summary>Download is paused and can be resumed.</summary>
    public const string Paused = "paused";

    /// <summary>Download is actively in progress.</summary>
    public const string InProgress = "in_progress";

    /// <summary>Download was cancelled by user.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Download hit cache limit.</summary>
    public const string LimitReached = "limit_reached";
}

/// <summary>
/// Interruption reason string constants for download state persistence.
/// </summary>
public static class DownloadPauseReason
{
    /// <summary>User manually paused the download.</summary>
    public const string UserPause = "user_pause";

    /// <summary>User cancelled the download.</summary>
    public const string UserCancel = "user_cancel";

    /// <summary>Network became unavailable.</summary>
    public const string NetworkLost = "network_lost";

    /// <summary>Device storage is low.</summary>
    public const string StorageLow = "storage_low";

    /// <summary>Trip cache size limit was reached.</summary>
    public const string CacheLimitReached = "cache_limit_reached";

    /// <summary>Periodic checkpoint save.</summary>
    public const string PeriodicSave = "periodic_save";

    /// <summary>App was backgrounded or terminated.</summary>
    public const string AppInterrupted = "app_interrupted";
}
