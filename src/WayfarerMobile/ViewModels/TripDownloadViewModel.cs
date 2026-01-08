using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for trip download operations.
/// Manages download state, progress, pause/resume, and cache warnings.
/// </summary>
/// <remarks>
/// This ViewModel is owned by TripsViewModel and uses ITripDownloadCallbacks
/// to communicate list updates back to the parent without direct coupling.
/// </remarks>
public partial class TripDownloadViewModel : ObservableObject, IDisposable
{
    #region Fields

    private readonly TripDownloadService _downloadService;
    private readonly ITripNavigationService _tripNavigationService;
    private readonly ITripStateManager _tripStateManager;
    private readonly IToastService _toastService;
    private readonly ILogger<TripDownloadViewModel> _logger;

    private ITripDownloadCallbacks? _callbacks;
    private CancellationTokenSource? _downloadCts;
    private bool _isProcessingPauseResume;
    private bool _disposed;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether a download is in progress.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseDownload))]
    [NotifyPropertyChangedFor(nameof(CanResumeDownload))]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress (0.0-1.0).
    /// </summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>
    /// Gets or sets the download status message.
    /// </summary>
    [ObservableProperty]
    private string? _downloadStatusMessage;

    /// <summary>
    /// Gets or sets the name of the trip being downloaded.
    /// </summary>
    [ObservableProperty]
    private string? _downloadingTripName;

    /// <summary>
    /// Gets or sets the local ID of the trip being downloaded.
    /// </summary>
    [ObservableProperty]
    private int? _downloadingTripId;

    /// <summary>
    /// Gets or sets the server ID of the trip being downloaded (for per-item progress tracking).
    /// </summary>
    private Guid? _downloadingTripServerId;

    /// <summary>
    /// Gets or sets whether the current download is paused.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseDownload))]
    [NotifyPropertyChangedFor(nameof(CanResumeDownload))]
    private bool _isDownloadPaused;

    /// <summary>
    /// Gets or sets the count of persisted paused downloads (from previous sessions).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResumeDownload))]
    [NotifyPropertyChangedFor(nameof(HasPausedDownloads))]
    private int _pausedDownloadsCount;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether there are any paused downloads that can be resumed.
    /// </summary>
    public bool HasPausedDownloads => PausedDownloadsCount > 0;

    /// <summary>
    /// Gets whether the download can be paused.
    /// </summary>
    public bool CanPauseDownload => IsDownloading && !IsDownloadPaused;

    /// <summary>
    /// Gets whether the download can be resumed.
    /// True when:
    /// - Current download is paused, OR
    /// - No active download AND there are persisted paused downloads from previous sessions
    /// </summary>
    public bool CanResumeDownload => IsDownloadPaused || (!IsDownloading && HasPausedDownloads);

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripDownloadViewModel.
    /// </summary>
    public TripDownloadViewModel(
        TripDownloadService downloadService,
        ITripNavigationService tripNavigationService,
        ITripStateManager tripStateManager,
        IToastService toastService,
        ILogger<TripDownloadViewModel> logger)
    {
        _downloadService = downloadService;
        _tripNavigationService = tripNavigationService;
        _tripStateManager = tripStateManager;
        _toastService = toastService;
        _logger = logger;

        // Subscribe to download progress and cache events
        _downloadService.ProgressChanged += OnDownloadProgressChanged;
        _downloadService.CacheWarning += OnCacheWarning;
        _downloadService.CacheCritical += OnCacheCritical;
        _downloadService.CacheLimitReached += OnCacheLimitReached;

        // Subscribe to terminal download events
        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _downloadService.DownloadFailed += OnDownloadFailed;
        _downloadService.DownloadPaused += OnDownloadPaused;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Sets the callbacks for communicating with the parent ViewModel.
    /// Must be called after construction to enable list updates.
    /// </summary>
    /// <param name="callbacks">The callback implementation (typically TripsViewModel).</param>
    public void SetCallbacks(ITripDownloadCallbacks callbacks)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
    }

    #endregion

    #region Commands

    /// <summary>
    /// Quick download - metadata only (no tiles).
    /// </summary>
    [RelayCommand]
    public async Task QuickDownloadAsync(TripListItem? item)
    {
        if (item == null || IsDownloading)
            return;

        await DownloadTripInternalAsync(item, includeTiles: false);
    }

    /// <summary>
    /// Full download - metadata with tiles.
    /// Checks cache quota before starting and shows warning if insufficient.
    /// </summary>
    [RelayCommand]
    public async Task FullDownloadAsync(TripListItem? item)
    {
        if (item == null || IsDownloading)
            return;

        // Check cache quota before downloading
        var quotaCheck = await _downloadService.CheckCacheQuotaForTripAsync(item.BoundingBox);

        if (!quotaCheck.HasSufficientQuota)
        {
            var message = $"This trip needs ~{quotaCheck.EstimatedSizeMB:F0} MB ({quotaCheck.TileCount:N0} tiles).\n\n" +
                         $"Available: {quotaCheck.AvailableMB:F0} MB\n" +
                         $"Current usage: {quotaCheck.CurrentUsageMB:F0} / {quotaCheck.MaxSizeMB} MB\n\n" +
                         $"Would exceed limit by {quotaCheck.WouldExceedBy:F0} MB.";

            var action = await Shell.Current.DisplayAlertAsync(
                "Insufficient Cache Space",
                message,
                "Download Anyway",
                "Cancel");

            if (!action)
                return;
        }
        else if (quotaCheck.EstimatedSizeMB > 100)
        {
            // Show info for large downloads even if within quota
            var proceed = await Shell.Current.DisplayAlertAsync(
                "Large Download",
                $"This trip will download ~{quotaCheck.EstimatedSizeMB:F0} MB ({quotaCheck.TileCount:N0} tiles).\n\n" +
                $"Available quota: {quotaCheck.AvailableMB:F0} MB",
                "Continue",
                "Cancel");

            if (!proceed)
                return;
        }

        await DownloadTripInternalAsync(item, includeTiles: true);
    }

    /// <summary>
    /// Delete downloaded trip data.
    /// </summary>
    [RelayCommand]
    public async Task DeleteDownloadAsync(TripListItem? item)
    {
        if (item == null)
            return;

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Delete Offline Data",
            $"Remove offline data for '{item.Name}'? The trip will remain on the server.",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            // Check if this trip is currently loaded using single source of truth
            var isCurrentlyLoaded = _tripStateManager.CurrentLoadedTripId == item.ServerId;

            await _downloadService.DeleteTripAsync(item.ServerId);

            // If the trip was loaded on the map, unload it and navigate to main page
            if (isCurrentlyLoaded)
            {
                _tripNavigationService.UnloadTrip();
                _logger.LogInformation("Unloaded deleted trip from map: {TripName}", item.Name);

                // Navigate to main page with UnloadTrip signal
                await Shell.Current.GoToAsync("//main", new Dictionary<string, object>
                {
                    ["UnloadTrip"] = true
                });
            }

            // Clear entity FIRST so StatsText shows correct value when DownloadState triggers notification
            item.DownloadedEntity = null;
            item.DownloadState = TripDownloadState.ServerOnly;
            item.IsCurrentlyLoaded = false;  // Clear loaded state since trip data is deleted

            // Move item to correct group based on new state
            _callbacks?.MoveItemToCorrectGroup(item);

            await _toastService.ShowSuccessAsync("Offline data deleted");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error deleting offline data");
            await _toastService.ShowErrorAsync("Failed to delete files. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting offline data");
            await _toastService.ShowErrorAsync($"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete only offline map tiles, keeping trip data.
    /// </summary>
    [RelayCommand]
    public async Task DeleteTilesOnlyAsync(TripListItem? item)
    {
        if (item == null)
            return;

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Remove Offline Maps",
            $"Remove offline maps for '{item.Name}'? Trip data will be kept.",
            "Remove Maps",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            var deletedCount = await _downloadService.DeleteTripTilesAsync(item.ServerId);

            // Update item state - now has metadata only (no tiles)
            item.DownloadState = TripDownloadState.MetadataOnly;
            if (item.DownloadedEntity != null)
            {
                item.DownloadedEntity.TileCount = 0;
                item.DownloadedEntity.TotalSizeBytes = 0;
            }

            await _toastService.ShowSuccessAsync($"Removed {deletedCount} map tiles");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error deleting offline maps");
            await _toastService.ShowErrorAsync("Failed to delete map files. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting offline maps");
            await _toastService.ShowErrorAsync($"Failed to delete maps: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancel ongoing download with confirmation.
    /// Deletes all downloaded tiles for this trip.
    /// </summary>
    [RelayCommand]
    public async Task CancelDownloadAsync()
    {
        if (!DownloadingTripId.HasValue)
            return;

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Cancel Download",
            $"Cancel download for '{DownloadingTripName}'? All downloaded tiles will be deleted.",
            "Cancel Download",
            "Continue");

        if (!confirm)
            return;

        _logger.LogInformation("Cancelling download for trip {TripId} with cleanup", DownloadingTripId.Value);
        await _downloadService.CancelDownloadAsync(DownloadingTripId.Value, cleanup: true);

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
        IsDownloading = false;
        IsDownloadPaused = false;
        DownloadingTripName = null;
        DownloadingTripId = null;
        _downloadingTripServerId = null;

        await _toastService.ShowAsync("Download cancelled");
    }

    /// <summary>
    /// Pause ongoing download.
    /// </summary>
    [RelayCommand]
    public async Task PauseDownloadAsync()
    {
        _logger.LogInformation("PauseDownloadAsync: Entry - IsProcessing={IsProcessing}, TripId={TripId}, IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}",
            _isProcessingPauseResume, DownloadingTripId, IsDownloading, IsDownloadPaused);

        if (_isProcessingPauseResume || !DownloadingTripId.HasValue)
        {
            _logger.LogWarning("PauseDownloadAsync: Early exit - IsProcessing={IsProcessing}, HasTripId={HasTripId}",
                _isProcessingPauseResume, DownloadingTripId.HasValue);
            return;
        }

        _isProcessingPauseResume = true;
        try
        {
            _logger.LogInformation("PauseDownloadAsync: Calling service.PauseDownloadAsync for trip {TripId}", DownloadingTripId.Value);

            var paused = await _downloadService.PauseDownloadAsync(DownloadingTripId.Value);
            _logger.LogInformation("PauseDownloadAsync: Service returned paused={Paused}", paused);

            if (paused)
            {
                IsDownloadPaused = true;
                DownloadStatusMessage = "Download paused";
                _logger.LogInformation("PauseDownloadAsync: State updated - IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}",
                    IsDownloading, IsDownloadPaused);

                // Clean up CancellationTokenSource on pause to avoid memory leak
                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
                _downloadCts = null;

                _logger.LogInformation("Download paused successfully for trip {TripId}", DownloadingTripId.Value);
                await _toastService.ShowAsync("Download paused - can resume later");
            }
            else
            {
                _logger.LogWarning("Failed to pause download for trip {TripId}", DownloadingTripId.Value);
                await _toastService.ShowErrorAsync("Could not pause download");
            }
        }
        finally
        {
            _isProcessingPauseResume = false;
        }
    }

    /// <summary>
    /// Resume paused download from current session or a previous session.
    /// </summary>
    [RelayCommand]
    public async Task ResumeDownloadAsync()
    {
        _logger.LogInformation("ResumeDownloadAsync: Entry - IsProcessing={IsProcessing}, TripId={TripId}, IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}, PausedCount={PausedCount}",
            _isProcessingPauseResume, DownloadingTripId, IsDownloading, IsDownloadPaused, PausedDownloadsCount);

        if (_isProcessingPauseResume)
        {
            _logger.LogWarning("ResumeDownloadAsync: Early exit - already processing");
            return;
        }

        _isProcessingPauseResume = true;
        try
        {
            // Handle current session pause
            if (DownloadingTripId.HasValue)
            {
                _logger.LogInformation("ResumeDownloadAsync: Resuming current session download {TripId}", DownloadingTripId.Value);
                await ResumeDownloadByTripIdAsync(DownloadingTripId.Value);
                return;
            }

            // Handle previous session paused downloads - find the first one to resume
            _logger.LogInformation("ResumeDownloadAsync: Looking for paused downloads from previous session");
            var pausedDownloads = await _downloadService.GetPausedDownloadsAsync();
            _logger.LogInformation("ResumeDownloadAsync: Found {Count} paused downloads", pausedDownloads.Count);

            if (pausedDownloads.Count > 0)
            {
                var firstPaused = pausedDownloads[0];
                _logger.LogInformation("ResumeDownloadAsync: Resuming trip {TripId} ({TripName}), ServerId={ServerId}",
                    firstPaused.TripId, firstPaused.TripName, firstPaused.TripServerId);

                // Set up download state for this trip
                DownloadingTripId = firstPaused.TripId;
                DownloadingTripName = firstPaused.TripName;
                _downloadingTripServerId = firstPaused.TripServerId;
                await ResumeDownloadByTripIdAsync(firstPaused.TripId);
                return;
            }

            // No paused downloads found
            _logger.LogWarning("ResumeDownloadAsync: No paused downloads found");
            await _toastService.ShowErrorAsync("No paused downloads found");
        }
        finally
        {
            _isProcessingPauseResume = false;
            _logger.LogInformation("ResumeDownloadAsync: Exit - IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}",
                IsDownloading, IsDownloadPaused);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Checks for paused downloads from previous sessions and updates PausedDownloadsCount.
    /// </summary>
    public async Task RefreshPausedDownloadsCountAsync()
    {
        try
        {
            var pausedDownloads = await _downloadService.GetPausedDownloadsAsync();
            PausedDownloadsCount = pausedDownloads.Count;

            if (pausedDownloads.Count > 0)
            {
                _logger.LogInformation("Found {Count} paused download(s) from previous session", pausedDownloads.Count);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error checking for paused downloads");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error checking for paused downloads");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Internal download implementation.
    /// </summary>
    private async Task DownloadTripInternalAsync(TripListItem item, bool includeTiles)
    {
        // Store original state to restore if download fails/cancels
        var originalState = item.DownloadState;
        var downloadCompleted = false;

        try
        {
            IsDownloading = true;
            IsDownloadPaused = false;
            DownloadingTripName = item.Name;
            DownloadingTripId = null; // Will be set when we have the result
            _downloadingTripServerId = item.ServerId; // Track ServerId for per-item progress
            DownloadProgress = 0;
            DownloadStatusMessage = "Starting download...";
            _downloadCts = new CancellationTokenSource();

            // Set per-item state to Downloading for UI feedback
            item.DownloadState = TripDownloadState.Downloading;
            // Only show per-item progress bar for tile downloads (full downloads)
            // Metadata-only downloads are quick and only need the global overlay
            item.IsDownloading = includeTiles;
            item.DownloadProgress = 0;

            var summary = new TripSummary
            {
                Id = item.ServerId,
                Name = item.Name,
                BoundingBox = item.BoundingBox
            };

            var result = await _downloadService.DownloadTripAsync(summary, includeTiles, _downloadCts.Token);

            if (result != null)
            {
                // Track the trip ID for pause/resume
                DownloadingTripId = result.Id;

                // Set entity FIRST so StatsText has access to counts when DownloadState triggers notification
                item.DownloadedEntity = result;

                // Check if download actually completed or was paused/interrupted
                // result.Status will be "downloading" if paused, "complete" if finished
                if (result.Status == TripDownloadStatus.Complete ||
                    result.Status == TripDownloadStatus.MetadataOnly)
                {
                    item.DownloadState = result.Status == TripDownloadStatus.Complete
                        ? TripDownloadState.Complete
                        : TripDownloadState.MetadataOnly;
                    downloadCompleted = true;

                    // Move item to the correct group based on new state
                    _callbacks?.MoveItemToCorrectGroup(item);

                    await _toastService.ShowSuccessAsync($"'{item.Name}' downloaded");
                }
                else
                {
                    // Download was paused/interrupted - keep downloading state
                    // The OnDownloadPaused event handler will update UI appropriately
                    item.DownloadState = TripDownloadState.Downloading;
                    _logger.LogInformation("Download paused/interrupted for trip {TripId}, status: {Status}",
                        result.Id, result.Status);
                }
            }
            else if (!_downloadCts.IsCancellationRequested)
            {
                await _toastService.ShowErrorAsync("Download failed");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during download");
            await _toastService.ShowErrorAsync("Network error. Please check your connection.");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error during download");
            await _toastService.ShowErrorAsync("Failed to save files. Please check storage.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during download");
            await _toastService.ShowErrorAsync($"Download failed: {ex.Message}");
        }
        finally
        {
            // Only clear download state if not paused - paused state is handled by OnDownloadPaused event
            // This prevents race condition where finally clears state before event handler sets it
            if (!IsDownloadPaused)
            {
                IsDownloading = false;
                DownloadingTripName = null;
                DownloadingTripId = null;
                _downloadingTripServerId = null;
            }

            // Always clean up CTS (paused downloads get new CTS on resume)
            _downloadCts?.Dispose();
            _downloadCts = null;

            // Clear per-item download state
            item.IsDownloading = false;
            item.DownloadProgress = 0;

            // Restore original state if download didn't complete
            if (!downloadCompleted && item.DownloadState == TripDownloadState.Downloading)
            {
                item.DownloadState = originalState;
            }
        }
    }

    /// <summary>
    /// Resumes a specific paused download by trip ID.
    /// </summary>
    private async Task ResumeDownloadByTripIdAsync(int tripId)
    {
        // Create new CancellationTokenSource for the resume operation
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();

        // Set download state IMMEDIATELY so CanPauseDownload is true during the download
        // ResumeDownloadAsync blocks until completion, so we must set this before await
        IsDownloading = true;
        IsDownloadPaused = false;
        DownloadStatusMessage = "Resuming download...";

        // Update the TripListItem's state so the UI shows downloading
        if (_downloadingTripServerId.HasValue)
        {
            var item = _callbacks?.FindItemByServerId(_downloadingTripServerId.Value);
            if (item != null)
            {
                item.DownloadState = TripDownloadState.Downloading;
                item.IsDownloading = true;
                item.DownloadProgress = 0;
            }
        }

        try
        {
            var resumed = await _downloadService.ResumeDownloadAsync(tripId, _downloadCts.Token);
            if (!resumed)
            {
                // Resume failed - provide feedback and reset state
                await _toastService.ShowErrorAsync("Could not resume download");
                IsDownloadPaused = true; // Back to paused
                IsDownloading = false;
                DownloadStatusMessage = "Resume failed";

                // Revert item state
                if (_downloadingTripServerId.HasValue)
                {
                    var item = _callbacks?.FindItemByServerId(_downloadingTripServerId.Value);
                    if (item != null)
                    {
                        item.IsDownloading = false;
                        // Keep DownloadState as Downloading - it was already in that state in the DB
                    }
                }
            }
            // Success case: OnDownloadCompleted/OnDownloadPaused events handle final state cleanup
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Resumed download cancelled");
            IsDownloadPaused = true;
            IsDownloading = false;
            DownloadStatusMessage = "Download paused";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error resuming download for trip {TripId}", tripId);
            await _toastService.ShowErrorAsync("Network error. Please check your connection.");
            IsDownloadPaused = true;
            IsDownloading = false;
            DownloadStatusMessage = "Resume failed";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error resuming download for trip {TripId}", tripId);
            await _toastService.ShowErrorAsync("Failed to write files. Please check storage.");
            IsDownloadPaused = true;
            IsDownloading = false;
            DownloadStatusMessage = "Resume failed";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resuming download for trip {TripId}", tripId);
            await _toastService.ShowErrorAsync($"Resume failed: {ex.Message}");
            // Reset to paused state on error
            IsDownloadPaused = true;
            IsDownloading = false;
            DownloadStatusMessage = "Resume failed";
        }
        finally
        {
            // Clean up CTS if we're no longer in an active download state
            if (!IsDownloading || IsDownloadPaused)
            {
                _downloadCts?.Dispose();
                _downloadCts = null;

                // Also clean up item download state if we're not actively downloading
                if (_downloadingTripServerId.HasValue)
                {
                    var item = _callbacks?.FindItemByServerId(_downloadingTripServerId.Value);
                    if (item != null)
                    {
                        item.IsDownloading = false;
                        item.DownloadProgress = 0;
                    }
                }
            }
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles download progress updates.
    /// </summary>
    private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Capture the trip ID from progress events (available early in download)
            // This enables pause/resume/cancel during download, not just after completion
            if (!DownloadingTripId.HasValue && e.TripId > 0)
            {
                DownloadingTripId = e.TripId;
            }

            // Update ViewModel-level progress
            DownloadProgress = e.ProgressPercent / 100.0;
            DownloadStatusMessage = e.StatusMessage;

            // Update per-item progress via callback
            if (_downloadingTripServerId.HasValue)
            {
                _callbacks?.UpdateItemProgress(_downloadingTripServerId.Value, e.ProgressPercent / 100.0, true);
            }
        });
    }

    /// <summary>
    /// Handles cache warning (80% full).
    /// </summary>
    private void OnCacheWarning(object? sender, CacheLimitEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await _toastService.ShowAsync(
                $"Cache 80% full ({e.CurrentUsageMB:F0}/{e.MaxSizeMB} MB)");
        });
    }

    /// <summary>
    /// Handles cache critical (90% full).
    /// </summary>
    private void OnCacheCritical(object? sender, CacheLimitEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var pause = await Shell.Current.DisplayAlertAsync(
                "Cache Almost Full",
                $"Trip cache is 90% full ({e.CurrentUsageMB:F0}/{e.MaxSizeMB} MB).\n\n" +
                "Consider pausing the download and freeing up space.",
                "Pause Download",
                "Continue");

            if (pause && DownloadingTripId.HasValue)
            {
                await PauseDownloadAsync();
            }
        });
    }

    /// <summary>
    /// Handles cache limit reached (100% full).
    /// </summary>
    private void OnCacheLimitReached(object? sender, CacheLimitEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsDownloadPaused = true;
            DownloadStatusMessage = "Cache limit reached - paused";

            var action = await Shell.Current.DisplayAlertAsync(
                "Cache Limit Reached",
                $"Trip cache is full ({e.CurrentUsageMB:F0}/{e.MaxSizeMB} MB).\n\n" +
                "The download has been paused. You can:\n" +
                "- Increase the limit in Settings\n" +
                "- Delete other offline trips to free space\n" +
                "- Keep the partial download",
                "Go to Settings",
                "Keep Partial");

            if (action)
            {
                await Shell.Current.GoToAsync("//settings");
            }
        });
    }

    /// <summary>
    /// Handles download completed event.
    /// </summary>
    private void OnDownloadCompleted(object? sender, DownloadTerminalEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _logger.LogInformation("Download completed for trip {TripName}: {Tiles} tiles, {Bytes} bytes",
                e.TripName, e.TilesDownloaded, e.TotalBytes);

            // Clear download state
            IsDownloading = false;
            IsDownloadPaused = false;
            DownloadingTripName = null;
            DownloadingTripId = null;
            _downloadingTripServerId = null;

            // Refresh the trip list to update download status
            if (_callbacks != null)
            {
                await _callbacks.RefreshTripsAsync();
                await _callbacks.CheckForPausedDownloadsAsync();
            }
        });
    }

    /// <summary>
    /// Handles download failed event.
    /// </summary>
    private void OnDownloadFailed(object? sender, DownloadTerminalEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _logger.LogWarning("Download failed for trip {TripName}: {Error}", e.TripName, e.ErrorMessage);

            // Clear download state
            IsDownloading = false;
            IsDownloadPaused = false;
            DownloadingTripName = null;
            DownloadingTripId = null;
            _downloadingTripServerId = null;

            await _toastService.ShowErrorAsync($"Download failed: {e.ErrorMessage}");

            // Refresh the trip list
            if (_callbacks != null)
            {
                await _callbacks.RefreshTripsAsync();
            }
        });
    }

    /// <summary>
    /// Handles download paused event.
    /// </summary>
    private void OnDownloadPaused(object? sender, DownloadPausedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _logger.LogInformation("OnDownloadPaused: Event received - Trip={TripName}, Reason={Reason}, {Completed}/{Total} tiles, CanResume={CanResume}",
                e.TripName, e.Reason, e.TilesCompleted, e.TotalTiles, e.CanResume);
            _logger.LogInformation("OnDownloadPaused: Before state update - IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}, TripId={TripId}",
                IsDownloading, IsDownloadPaused, DownloadingTripId);

            IsDownloadPaused = true;
            DownloadStatusMessage = e.Reason switch
            {
                DownloadPauseReasonType.UserRequest => "Download paused",
                DownloadPauseReasonType.NetworkLost => "Paused - network lost",
                DownloadPauseReasonType.StorageLow => "Paused - storage low",
                DownloadPauseReasonType.CacheLimitReached => "Paused - cache limit",
                DownloadPauseReasonType.UserCancel => "Download cancelled",
                _ => "Download paused"
            };

            // If cancelled (not resumable), clear download state
            if (!e.CanResume)
            {
                _logger.LogInformation("OnDownloadPaused: Not resumable - clearing state");
                IsDownloading = false;
                IsDownloadPaused = false;
                DownloadingTripName = null;
                DownloadingTripId = null;
                _downloadingTripServerId = null;
            }

            _logger.LogInformation("OnDownloadPaused: After state update - IsDownloading={IsDownloading}, IsDownloadPaused={IsPaused}, CanPause={CanPause}, CanResume={CanResume}",
                IsDownloading, IsDownloadPaused, CanPauseDownload, CanResumeDownload);

            // Refresh paused downloads count via callback
            if (_callbacks != null)
            {
                _logger.LogInformation("OnDownloadPaused: Calling CheckForPausedDownloadsAsync");
                await _callbacks.CheckForPausedDownloadsAsync();
                _logger.LogInformation("OnDownloadPaused: After CheckForPausedDownloadsAsync - PausedCount={Count}", PausedDownloadsCount);
            }
        });
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _downloadService.ProgressChanged -= OnDownloadProgressChanged;
        _downloadService.CacheWarning -= OnCacheWarning;
        _downloadService.CacheCritical -= OnCacheCritical;
        _downloadService.CacheLimitReached -= OnCacheLimitReached;
        _downloadService.DownloadCompleted -= OnDownloadCompleted;
        _downloadService.DownloadFailed -= OnDownloadFailed;
        _downloadService.DownloadPaused -= OnDownloadPaused;

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
