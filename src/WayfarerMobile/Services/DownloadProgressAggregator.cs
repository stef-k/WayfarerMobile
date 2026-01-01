using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Thread-safe implementation of <see cref="IDownloadProgressAggregator"/>.
/// Centralizes download progress events from multiple services.
/// Registered as singleton in DI.
/// </summary>
public class DownloadProgressAggregator : IDownloadProgressAggregator
{
    private readonly ILogger<DownloadProgressAggregator> _logger;
    private readonly ConcurrentDictionary<int, DownloadProgressEventArgs> _activeDownloads = new();

    /// <inheritdoc/>
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <inheritdoc/>
    public event EventHandler<DownloadTerminalEventArgs>? DownloadCompleted;

    /// <inheritdoc/>
    public event EventHandler<DownloadTerminalEventArgs>? DownloadFailed;

    /// <inheritdoc/>
    public event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <inheritdoc/>
    public event EventHandler<CacheLimitEventArgs>? CacheLimitWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadProgressAggregator"/> class.
    /// </summary>
    public DownloadProgressAggregator(ILogger<DownloadProgressAggregator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public DownloadProgressEventArgs? GetCurrentProgress(int tripId)
    {
        return _activeDownloads.TryGetValue(tripId, out var progress) ? progress : null;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, DownloadProgressEventArgs> GetActiveDownloads()
    {
        return _activeDownloads;
    }

    /// <inheritdoc/>
    public void PublishProgress(DownloadProgressEventArgs progress)
    {
        _activeDownloads[progress.TripId] = progress;

        _logger.LogDebug(
            "Download progress: Trip {TripId} at {Percent}% - {Status}",
            progress.TripId,
            progress.ProgressPercent,
            progress.StatusMessage);

        RaiseEvent(ProgressChanged, progress);
    }

    /// <inheritdoc/>
    public void PublishCompleted(DownloadTerminalEventArgs args)
    {
        _activeDownloads.TryRemove(args.TripId, out _);

        _logger.LogInformation(
            "Download completed: Trip {TripId} ({TripName}) - {Tiles} tiles, {Bytes} bytes",
            args.TripId,
            args.TripName,
            args.TilesDownloaded,
            args.TotalBytes);

        RaiseEvent(DownloadCompleted, args);
    }

    /// <inheritdoc/>
    public void PublishFailed(DownloadTerminalEventArgs args)
    {
        _activeDownloads.TryRemove(args.TripId, out _);

        _logger.LogWarning(
            "Download failed: Trip {TripId} ({TripName}) - {Error}",
            args.TripId,
            args.TripName,
            args.ErrorMessage);

        RaiseEvent(DownloadFailed, args);
    }

    /// <inheritdoc/>
    public void PublishPaused(DownloadPausedEventArgs args)
    {
        _logger.LogInformation(
            "Download paused: Trip {TripId} ({TripName}) - {Reason}, {Completed}/{Total} tiles",
            args.TripId,
            args.TripName,
            args.Reason,
            args.TilesCompleted,
            args.TotalTiles);

        RaiseEvent(DownloadPaused, args);
    }

    /// <inheritdoc/>
    public void PublishCacheLimitWarning(CacheLimitEventArgs args)
    {
        _logger.LogWarning(
            "Cache limit {Level}: Trip {TripId} ({TripName}) - {Usage:F1}MB / {Max}MB ({Percent:F0}%)",
            args.Level,
            args.TripId,
            args.TripName,
            args.CurrentUsageMB,
            args.MaxSizeMB,
            args.UsagePercent);

        RaiseEvent(CacheLimitWarning, args);
    }

    /// <inheritdoc/>
    public void ClearDownload(int tripId)
    {
        _activeDownloads.TryRemove(tripId, out _);
        _logger.LogDebug("Cleared download tracking for Trip {TripId}", tripId);
    }

    /// <summary>
    /// Raises an event on the main thread for UI safety.
    /// </summary>
    private void RaiseEvent<T>(EventHandler<T>? handler, T args)
    {
        if (handler == null)
            return;

        if (MainThread.IsMainThread)
        {
            handler.Invoke(this, args);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    handler.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in event handler for {EventType}", typeof(T).Name);
                }
            });
        }
    }
}
