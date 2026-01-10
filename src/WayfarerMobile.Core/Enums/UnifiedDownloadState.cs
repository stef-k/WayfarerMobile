namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Unified download state that represents all possible states of a trip download.
/// This is the single source of truth for download state across the application.
/// </summary>
/// <remarks>
/// Values are grouped by category:
/// - 0-9: Initial/active states
/// - 10-19: Paused states (resumable)
/// - 20-29: Terminal error states
/// - 30-39: Completed without tiles
/// - 40+: Fully completed
/// </remarks>
public enum UnifiedDownloadState
{
    /// <summary>Trip exists only on server, no local data.</summary>
    ServerOnly = 0,

    /// <summary>Currently downloading trip metadata (places, segments, areas).</summary>
    DownloadingMetadata = 1,

    /// <summary>Currently downloading map tiles.</summary>
    DownloadingTiles = 2,

    /// <summary>Download paused by user.</summary>
    PausedByUser = 10,

    /// <summary>Download paused due to network loss.</summary>
    PausedNetworkLost = 11,

    /// <summary>Download paused due to low storage.</summary>
    PausedStorageLow = 12,

    /// <summary>Download paused because cache limit was reached.</summary>
    PausedCacheLimit = 13,

    /// <summary>Download failed with error.</summary>
    Failed = 20,

    /// <summary>Download cancelled by user (cleanup pending or complete).</summary>
    Cancelled = 21,

    /// <summary>Trip metadata downloaded, no tiles (usable with online maps).</summary>
    MetadataOnly = 30,

    /// <summary>Trip fully downloaded with offline maps.</summary>
    Complete = 40
}

/// <summary>
/// Extension methods for UnifiedDownloadState.
/// Provides helper methods for state queries and UI display.
/// </summary>
public static class UnifiedDownloadStateExtensions
{
    /// <summary>
    /// Gets whether the state represents an active download in progress.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if actively downloading.</returns>
    public static bool IsDownloading(this UnifiedDownloadState state) =>
        state is UnifiedDownloadState.DownloadingMetadata or UnifiedDownloadState.DownloadingTiles;

    /// <summary>
    /// Gets whether the state represents a paused download.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if paused.</returns>
    public static bool IsPaused(this UnifiedDownloadState state) =>
        state is >= UnifiedDownloadState.PausedByUser and <= UnifiedDownloadState.PausedCacheLimit;

    /// <summary>
    /// Gets whether the download can be resumed from this state.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if resumable.</returns>
    public static bool CanResume(this UnifiedDownloadState state) =>
        state.IsPaused() || state == UnifiedDownloadState.Failed;

    /// <summary>
    /// Gets whether the download can be paused from this state.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if can be paused.</returns>
    public static bool CanPause(this UnifiedDownloadState state) =>
        state.IsDownloading();

    /// <summary>
    /// Gets whether the trip can be loaded to map from this state.
    /// Requires metadata to be available.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if can load to map.</returns>
    public static bool CanLoadToMap(this UnifiedDownloadState state) =>
        state is UnifiedDownloadState.MetadataOnly
            or UnifiedDownloadState.Complete
            or UnifiedDownloadState.PausedByUser
            or UnifiedDownloadState.PausedNetworkLost
            or UnifiedDownloadState.PausedStorageLow
            or UnifiedDownloadState.PausedCacheLimit
            or UnifiedDownloadState.Failed;

    /// <summary>
    /// Gets whether tiles can be downloaded for this trip.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if can download tiles.</returns>
    public static bool CanDownloadTiles(this UnifiedDownloadState state) =>
        state is UnifiedDownloadState.ServerOnly or UnifiedDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether the trip has local data that can be deleted.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if has local data.</returns>
    public static bool HasLocalData(this UnifiedDownloadState state) =>
        state != UnifiedDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether tiles can be deleted without removing metadata.
    /// Only valid for complete downloads with tiles.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if can delete tiles only.</returns>
    public static bool CanDeleteTilesOnly(this UnifiedDownloadState state) =>
        state == UnifiedDownloadState.Complete;

    /// <summary>
    /// Gets the display group name for UI grouping.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>Group name for display.</returns>
    public static string GetGroupName(this UnifiedDownloadState state) => state switch
    {
        UnifiedDownloadState.Complete => "Downloaded",
        UnifiedDownloadState.MetadataOnly => "Metadata Only",
        >= UnifiedDownloadState.DownloadingMetadata and <= UnifiedDownloadState.PausedCacheLimit => "In Progress",
        UnifiedDownloadState.Failed => "Failed",
        _ => "Available on Server"
    };

    /// <summary>
    /// Gets the status text for UI display.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>Status text for display.</returns>
    public static string GetStatusText(this UnifiedDownloadState state) => state switch
    {
        UnifiedDownloadState.ServerOnly => "Online",
        UnifiedDownloadState.DownloadingMetadata => "Downloading...",
        UnifiedDownloadState.DownloadingTiles => "Downloading tiles...",
        UnifiedDownloadState.PausedByUser => "Paused",
        UnifiedDownloadState.PausedNetworkLost => "Paused (no network)",
        UnifiedDownloadState.PausedStorageLow => "Paused (low storage)",
        UnifiedDownloadState.PausedCacheLimit => "Paused (cache full)",
        UnifiedDownloadState.Failed => "Failed",
        UnifiedDownloadState.Cancelled => "Cancelled",
        UnifiedDownloadState.MetadataOnly => "Metadata",
        UnifiedDownloadState.Complete => "Offline",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets whether metadata is expected to be complete for this state.
    /// Used to determine if Load to Map should be available.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns>True if metadata should be complete.</returns>
    public static bool HasMetadata(this UnifiedDownloadState state) =>
        state is UnifiedDownloadState.DownloadingTiles
            or UnifiedDownloadState.PausedByUser
            or UnifiedDownloadState.PausedNetworkLost
            or UnifiedDownloadState.PausedStorageLow
            or UnifiedDownloadState.PausedCacheLimit
            or UnifiedDownloadState.MetadataOnly
            or UnifiedDownloadState.Complete;
}
