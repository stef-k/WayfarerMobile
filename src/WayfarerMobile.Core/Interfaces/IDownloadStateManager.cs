using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Manages download pause/resume state, progress tracking, and stop requests.
/// Provides persistence for download checkpoints and handles interruption coordination.
/// </summary>
/// <remarks>
/// This service focuses on state management for downloads:
/// - Stop request tracking (pause/cancel coordination)
/// - State persistence (checkpoints, resume data)
/// - Progress queries (paused downloads, completion status)
///
/// Does NOT handle:
/// - Actual tile downloading (use ITileDownloadService)
/// - Cache limit enforcement (use CacheLimitEnforcer)
/// - Download orchestration (use TripDownloadService)
/// </remarks>
public interface IDownloadStateManager
{
    #region Stop Request Management

    /// <summary>
    /// Requests a download to stop with the specified reason.
    /// Thread-safe; can be called from any thread.
    /// </summary>
    /// <param name="tripId">The trip ID to stop.</param>
    /// <param name="reason">The stop reason (see DownloadStopReason constants).</param>
    void RequestStop(int tripId, string reason);

    /// <summary>
    /// Checks if a stop has been requested for a download.
    /// </summary>
    /// <param name="tripId">The trip ID to check.</param>
    /// <returns>True if a stop has been requested.</returns>
    bool IsStopRequested(int tripId);

    /// <summary>
    /// Gets the stop reason for a download.
    /// </summary>
    /// <param name="tripId">The trip ID to check.</param>
    /// <param name="reason">The stop reason if found.</param>
    /// <returns>True if a stop reason exists.</returns>
    bool TryGetStopReason(int tripId, out string reason);

    /// <summary>
    /// Clears any pending stop request for a download.
    /// Called when starting/resuming a download.
    /// </summary>
    /// <param name="tripId">The trip ID to clear.</param>
    void ClearStopRequest(int tripId);

    #endregion

    #region State Persistence

    /// <summary>
    /// Saves the current download state for later resumption.
    /// </summary>
    /// <param name="state">The download state to save.</param>
    /// <returns>A task representing the async operation.</returns>
    Task SaveStateAsync(DownloadState state);

    /// <summary>
    /// Loads the saved download state for a trip.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>The saved state, or null if none exists.</returns>
    Task<DownloadState?> GetStateAsync(int tripId);

    /// <summary>
    /// Loads the saved download state by server trip ID.
    /// </summary>
    /// <param name="tripServerId">The server trip ID.</param>
    /// <returns>The saved state, or null if none exists.</returns>
    Task<DownloadState?> GetStateByServerIdAsync(Guid tripServerId);

    /// <summary>
    /// Deletes the saved download state for a trip.
    /// Called when download completes or is cancelled.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>A task representing the async operation.</returns>
    Task DeleteStateAsync(int tripId);

    #endregion

    #region State Queries

    /// <summary>
    /// Gets all paused downloads that can be resumed.
    /// </summary>
    /// <returns>List of paused download states.</returns>
    Task<List<DownloadState>> GetPausedDownloadsAsync();

    /// <summary>
    /// Checks if a download is currently paused (and resumable).
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>True if the download is paused and can be resumed.</returns>
    Task<bool> IsPausedAsync(int tripId);

    /// <summary>
    /// Checks if a download has any saved state (paused, in progress, or limit reached).
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>True if any state exists for the download.</returns>
    Task<bool> HasStateAsync(int tripId);

    #endregion
}

/// <summary>
/// Represents the persistent state of a download operation.
/// </summary>
public record DownloadState
{
    /// <summary>
    /// Gets or sets the local trip ID.
    /// </summary>
    public int TripId { get; init; }

    /// <summary>
    /// Gets or sets the server-side trip ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets or sets the trip name for display.
    /// </summary>
    public string TripName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the remaining tiles to download.
    /// </summary>
    public List<TileCoordinate> RemainingTiles { get; init; } = [];

    /// <summary>
    /// Gets or sets the number of tiles completed.
    /// </summary>
    public int CompletedTileCount { get; init; }

    /// <summary>
    /// Gets or sets the total number of tiles.
    /// </summary>
    public int TotalTileCount { get; init; }

    /// <summary>
    /// Gets or sets the bytes downloaded so far.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Gets or sets the download status.
    /// </summary>
    public DownloadStatus Status { get; init; } = DownloadStatus.Paused;

    /// <summary>
    /// Gets or sets the reason for interruption.
    /// </summary>
    public string InterruptionReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets when the state was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets whether the download can be resumed.
    /// </summary>
    public bool CanResume => Status is DownloadStatus.Paused or DownloadStatus.InProgress or DownloadStatus.LimitReached;

    /// <summary>
    /// Gets the completion percentage (0-100).
    /// </summary>
    public int ProgressPercent => TotalTileCount > 0
        ? (int)(CompletedTileCount * 100.0 / TotalTileCount)
        : 0;
}

/// <summary>
/// Download status values.
/// </summary>
public enum DownloadStatus
{
    /// <summary>Download is paused and can be resumed.</summary>
    Paused,

    /// <summary>Download is actively in progress.</summary>
    InProgress,

    /// <summary>Download was cancelled by user.</summary>
    Cancelled,

    /// <summary>Download hit cache limit.</summary>
    LimitReached
}

/// <summary>
/// Stop reason constants for download interruption.
/// </summary>
public static class DownloadStopReason
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
