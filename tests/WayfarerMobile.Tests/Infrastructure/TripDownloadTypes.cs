namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Test copies of TripDownloadService event args and result types.
/// </summary>
/// <remarks>
/// These are copies from WayfarerMobile project for testing purposes,
/// as the main project targets MAUI-specific frameworks.
/// </remarks>

/// <summary>
/// Event args for download progress updates.
/// </summary>
public record DownloadProgressEventArgs
{
    public int TripId { get; init; }
    public int ProgressPercent { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Event args for cache limit events.
/// </summary>
public record CacheLimitEventArgs
{
    public int TripId { get; init; }
    public string TripName { get; init; } = string.Empty;
    public double CurrentUsageMB { get; init; }
    public int MaxSizeMB { get; init; }
    public double UsagePercent { get; init; }
    public CacheLimitLevel Level { get; init; }
}

/// <summary>
/// Cache limit warning levels.
/// </summary>
public enum CacheLimitLevel
{
    Warning,
    Critical,
    LimitReached
}

/// <summary>
/// Result of checking trip cache limit.
/// </summary>
public record CacheLimitCheckResult
{
    public long CurrentSizeBytes { get; init; }
    public double CurrentSizeMB { get; init; }
    public int MaxSizeMB { get; init; }
    public double UsagePercent { get; init; }
    public bool IsLimitReached { get; init; }
    public bool IsWarningLevel { get; init; }
}

/// <summary>
/// Result of checking cache quota for a new download.
/// </summary>
public record CacheQuotaCheckResult
{
    public int TileCount { get; init; }
    public long EstimatedSizeBytes { get; init; }
    public double EstimatedSizeMB { get; init; }
    public long AvailableBytes { get; init; }
    public double AvailableMB { get; init; }
    public double CurrentUsageMB { get; init; }
    public int MaxSizeMB { get; init; }
    public bool HasSufficientQuota { get; init; }
    public double WouldExceedBy { get; init; }
}

/// <summary>
/// Event args for terminal download events.
/// </summary>
public record DownloadTerminalEventArgs
{
    public int TripId { get; init; }
    public Guid TripServerId { get; init; }
    public string TripName { get; init; } = string.Empty;
    public int TilesDownloaded { get; init; }
    public long TotalBytes { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Event args for download paused events.
/// </summary>
public record DownloadPausedEventArgs
{
    public int TripId { get; init; }
    public Guid TripServerId { get; init; }
    public string TripName { get; init; } = string.Empty;
    public DownloadPauseReasonType Reason { get; init; }
    public int TilesCompleted { get; init; }
    public int TotalTiles { get; init; }
    public bool CanResume { get; init; } = true;
}

/// <summary>
/// Reason for pausing a download.
/// </summary>
public enum DownloadPauseReasonType
{
    UserRequest,
    UserCancel,
    NetworkLost,
    StorageLow,
    CacheLimitReached
}
