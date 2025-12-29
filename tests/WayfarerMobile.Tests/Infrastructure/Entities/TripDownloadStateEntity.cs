using SQLite;

namespace WayfarerMobile.Data.Entities;

/// <summary>
/// Represents the persistent state of a paused or interrupted trip download.
/// </summary>
/// <remarks>
/// This is a copy of the entity from WayfarerMobile for testing purposes.
/// </remarks>
[Table("TripDownloadStates")]
public class TripDownloadStateEntity
{
    [PrimaryKey]
    public int TripId { get; set; }

    [Indexed]
    public Guid TripServerId { get; set; }

    public string TripName { get; set; } = string.Empty;
    public string RemainingTilesJson { get; set; } = "[]";
    public int CompletedTileCount { get; set; }
    public int TotalTileCount { get; set; }
    public long DownloadedBytes { get; set; }

    [Indexed]
    public string Status { get; set; } = DownloadStateStatus.Paused;

    public string InterruptionReason { get; set; } = DownloadPauseReason.UserPause;
    public DateTime PausedAt { get; set; }
    public DateTime LastSaveTime { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Download state status constants.
/// </summary>
public static class DownloadStateStatus
{
    public const string Paused = "paused";
    public const string InProgress = "in_progress";
    public const string Cancelled = "cancelled";
    public const string LimitReached = "limit_reached";
}

/// <summary>
/// Interruption reason string constants.
/// </summary>
public static class DownloadPauseReason
{
    public const string UserPause = "user_pause";
    public const string UserCancel = "user_cancel";
    public const string NetworkLost = "network_lost";
    public const string StorageLow = "storage_low";
    public const string CacheLimitReached = "cache_limit_reached";
    public const string PeriodicSave = "periodic_save";
    public const string AppInterrupted = "app_interrupted";
}
