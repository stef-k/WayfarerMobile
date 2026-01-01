namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Event args for download progress updates.
/// </summary>
public record DownloadProgressEventArgs
{
    /// <summary>
    /// Gets the trip ID being downloaded.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; init; }

    /// <summary>
    /// Gets the status message.
    /// </summary>
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Event args for cache limit events (warning, critical, limit reached).
/// </summary>
public record CacheLimitEventArgs
{
    /// <summary>
    /// Gets the trip ID being downloaded.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the trip name for display.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current cache usage in MB.
    /// </summary>
    public double CurrentUsageMB { get; init; }

    /// <summary>
    /// Gets the maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// Gets the usage percentage (0-100+).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Gets the level of the warning.
    /// </summary>
    public CacheLimitLevel Level { get; init; }
}

/// <summary>
/// Cache limit warning levels.
/// </summary>
public enum CacheLimitLevel
{
    /// <summary>Warning level (80%).</summary>
    Warning,

    /// <summary>Critical level (90%).</summary>
    Critical,

    /// <summary>Limit reached (100%).</summary>
    LimitReached
}

/// <summary>
/// Result of checking trip cache limit.
/// </summary>
public record CacheLimitCheckResult
{
    /// <summary>
    /// Gets the current cache size in bytes.
    /// </summary>
    public long CurrentSizeBytes { get; init; }

    /// <summary>
    /// Gets the current cache size in MB.
    /// </summary>
    public double CurrentSizeMB { get; init; }

    /// <summary>
    /// Gets the maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// Gets the usage percentage (0-100+).
    /// </summary>
    public double UsagePercent { get; init; }

    /// <summary>
    /// Gets whether the limit has been reached.
    /// </summary>
    public bool IsLimitReached { get; init; }

    /// <summary>
    /// Gets whether we're at warning level (80-99%).
    /// </summary>
    public bool IsWarningLevel { get; init; }
}

/// <summary>
/// Result of checking cache quota for a new download.
/// </summary>
public record CacheQuotaCheckResult
{
    /// <summary>
    /// Number of tiles to download.
    /// </summary>
    public int TileCount { get; init; }

    /// <summary>
    /// Estimated download size in bytes.
    /// </summary>
    public long EstimatedSizeBytes { get; init; }

    /// <summary>
    /// Estimated download size in MB.
    /// </summary>
    public double EstimatedSizeMB { get; init; }

    /// <summary>
    /// Available cache quota in bytes.
    /// </summary>
    public long AvailableBytes { get; init; }

    /// <summary>
    /// Available cache quota in MB.
    /// </summary>
    public double AvailableMB { get; init; }

    /// <summary>
    /// Current cache usage in MB.
    /// </summary>
    public double CurrentUsageMB { get; init; }

    /// <summary>
    /// Maximum cache size in MB.
    /// </summary>
    public int MaxSizeMB { get; init; }

    /// <summary>
    /// True if there's enough quota for the download.
    /// </summary>
    public bool HasSufficientQuota { get; init; }

    /// <summary>
    /// Amount by which the download would exceed the limit (in MB).
    /// Zero if within quota.
    /// </summary>
    public double WouldExceedBy { get; init; }
}

/// <summary>
/// Event args for terminal download events (completed or failed).
/// </summary>
public record DownloadTerminalEventArgs
{
    /// <summary>
    /// Gets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the server trip ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of tiles downloaded.
    /// </summary>
    public int TilesDownloaded { get; init; }

    /// <summary>
    /// Gets the total bytes downloaded.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Gets the error message (for failed downloads).
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Event args for download paused events.
/// </summary>
public record DownloadPausedEventArgs
{
    /// <summary>
    /// Gets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets the server trip ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the reason for pausing.
    /// </summary>
    public DownloadPauseReasonType Reason { get; init; }

    /// <summary>
    /// Gets the number of tiles completed so far.
    /// </summary>
    public int TilesCompleted { get; init; }

    /// <summary>
    /// Gets the total number of tiles.
    /// </summary>
    public int TotalTiles { get; init; }

    /// <summary>
    /// Gets whether the download can be resumed.
    /// </summary>
    public bool CanResume { get; init; } = true;
}

/// <summary>
/// Reason for pausing a download.
/// </summary>
public enum DownloadPauseReasonType
{
    /// <summary>User requested pause.</summary>
    UserRequest,

    /// <summary>User cancelled the download.</summary>
    UserCancel,

    /// <summary>Network connection lost.</summary>
    NetworkLost,

    /// <summary>Device storage is low.</summary>
    StorageLow,

    /// <summary>Trip cache limit reached.</summary>
    CacheLimitReached
}
