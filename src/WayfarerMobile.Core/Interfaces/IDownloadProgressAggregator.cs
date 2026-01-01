namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Centralized aggregator for download progress events.
/// Allows ViewModels to subscribe to a single source instead of multiple services.
/// When TripDownloadService is split into smaller services, they all publish here.
/// </summary>
public interface IDownloadProgressAggregator
{
    /// <summary>
    /// Event raised when download progress changes for any trip.
    /// </summary>
    event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when a download completes successfully.
    /// </summary>
    event EventHandler<DownloadTerminalEventArgs>? DownloadCompleted;

    /// <summary>
    /// Event raised when a download fails.
    /// </summary>
    event EventHandler<DownloadTerminalEventArgs>? DownloadFailed;

    /// <summary>
    /// Event raised when a download is paused.
    /// </summary>
    event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <summary>
    /// Event raised when cache limit is approaching or reached.
    /// </summary>
    event EventHandler<CacheLimitEventArgs>? CacheLimitWarning;

    /// <summary>
    /// Gets the current download state for a trip, if any.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>Current progress info or null if not downloading.</returns>
    DownloadProgressEventArgs? GetCurrentProgress(int tripId);

    /// <summary>
    /// Gets all active downloads.
    /// </summary>
    /// <returns>Dictionary of tripId to progress info.</returns>
    IReadOnlyDictionary<int, DownloadProgressEventArgs> GetActiveDownloads();

    /// <summary>
    /// Publishes a progress update. Called by download services.
    /// </summary>
    void PublishProgress(DownloadProgressEventArgs progress);

    /// <summary>
    /// Publishes a download completion. Called by download services.
    /// </summary>
    void PublishCompleted(DownloadTerminalEventArgs args);

    /// <summary>
    /// Publishes a download failure. Called by download services.
    /// </summary>
    void PublishFailed(DownloadTerminalEventArgs args);

    /// <summary>
    /// Publishes a download pause. Called by download services.
    /// </summary>
    void PublishPaused(DownloadPausedEventArgs args);

    /// <summary>
    /// Publishes a cache limit warning. Called by cache services.
    /// </summary>
    void PublishCacheLimitWarning(CacheLimitEventArgs args);

    /// <summary>
    /// Clears tracking for a completed/cancelled download.
    /// </summary>
    void ClearDownload(int tripId);
}
