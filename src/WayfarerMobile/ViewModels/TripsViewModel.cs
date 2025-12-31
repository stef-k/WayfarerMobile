using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Collections;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for managing trips - browsing, downloading, and loading to map.
/// </summary>
public partial class TripsViewModel : BaseViewModel
{
    private readonly IApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly TripDownloadService _downloadService;
    private readonly IToastService _toastService;
    private readonly TripNavigationService _tripNavigationService;
    private readonly ITripSyncService _tripSyncService;
    private readonly ILogger<TripsViewModel> _logger;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadCts;
    private int _publicTripsPage = 1;
    private const int PageSize = 20;
    private bool _hasMorePublicTrips = true;
    private bool _isProcessingPauseResume; // Guard against rapid pause/resume clicks

    #region Observable Properties - Tab Management

    /// <summary>
    /// Gets or sets the selected tab index (0 = My Trips, 1 = Public Trips).
    /// </summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    #endregion

    #region Observable Properties - My Trips

    /// <summary>
    /// Gets the collection of user's trips grouped by status.
    /// </summary>
    public ObservableCollection<TripGrouping> MyTrips { get; } = new();

    /// <summary>
    /// Gets or sets whether trips are being loaded (used by RefreshView).
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingTrips;

    /// <summary>
    /// Gets or sets whether this is the initial load (for shimmer display).
    /// Only true before first successful load; false after and during refreshes.
    /// </summary>
    [ObservableProperty]
    private bool _isInitialLoad = true;

    #endregion

    #region Observable Properties - Sync Queue Status

    /// <summary>
    /// Gets or sets the count of pending sync operations.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingSync))]
    [NotifyPropertyChangedFor(nameof(SyncStatusText))]
    private int _pendingSyncCount;

    /// <summary>
    /// Gets or sets the count of failed sync operations.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailedSync))]
    [NotifyPropertyChangedFor(nameof(SyncStatusText))]
    private int _failedSyncCount;

    /// <summary>
    /// Gets whether there are pending sync operations.
    /// </summary>
    public bool HasPendingSync => PendingSyncCount > 0;

    /// <summary>
    /// Gets whether there are failed sync operations.
    /// </summary>
    public bool HasFailedSync => FailedSyncCount > 0;

    /// <summary>
    /// Gets the sync status text for display.
    /// </summary>
    public string SyncStatusText
    {
        get
        {
            if (FailedSyncCount > 0)
                return $"{FailedSyncCount} sync failed";
            if (PendingSyncCount > 0)
                return $"{PendingSyncCount} pending sync";
            return string.Empty;
        }
    }

    #endregion

    #region Observable Properties - Public Trips

    /// <summary>
    /// Gets the collection of public trips.
    /// </summary>
    public ObservableCollection<PublicTripSummary> PublicTrips { get; } = new();

    /// <summary>
    /// Gets or sets the public trips search query.
    /// </summary>
    [ObservableProperty]
    private string _publicSearchQuery = string.Empty;

    /// <summary>
    /// Gets or sets the selected sort option.
    /// </summary>
    [ObservableProperty]
    private string _selectedPublicSort = PublicTripsSortOptions.Updated;

    /// <summary>
    /// Gets or sets whether public trips are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingPublicTrips;

    /// <summary>
    /// Gets or sets whether more public trips are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMorePublicTrips;

    /// <summary>
    /// Gets or sets whether public trips list is empty.
    /// </summary>
    [ObservableProperty]
    private bool _isPublicTripsEmpty;

    /// <summary>
    /// Gets or sets whether a trip is being cloned.
    /// </summary>
    [ObservableProperty]
    private bool _isCloningTrip;

    /// <summary>
    /// Gets the available sort options.
    /// </summary>
    public string[] PublicSortOptions { get; } =
    [
        PublicTripsSortOptions.Updated,
        PublicTripsSortOptions.Newest,
        PublicTripsSortOptions.Name,
        PublicTripsSortOptions.Places
    ];

    #endregion

    #region Observable Properties - Download

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

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether API is configured.
    /// </summary>
    public bool IsConfigured => _settingsService.IsConfigured;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripsViewModel.
    /// </summary>
    public TripsViewModel(
        IApiClient apiClient,
        ISettingsService settingsService,
        TripDownloadService downloadService,
        IToastService toastService,
        TripNavigationService tripNavigationService,
        ITripSyncService tripSyncService,
        ILogger<TripsViewModel> logger)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _downloadService = downloadService;
        _toastService = toastService;
        _tripNavigationService = tripNavigationService;
        _tripSyncService = tripSyncService;
        _logger = logger;
        Title = "Trips";

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

    #region Property Change Handlers

    /// <summary>
    /// Called when the search query changes (with debounce).
    /// </summary>
    partial void OnPublicSearchQueryChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(value, _searchCts.Token);
    }

    /// <summary>
    /// Called when sort option changes.
    /// </summary>
    partial void OnSelectedPublicSortChanged(string value)
    {
        _ = RefreshPublicTripsAsync();
    }

    /// <summary>
    /// Called when selected tab changes.
    /// </summary>
    partial void OnSelectedTabIndexChanged(int value)
    {
        // Load public trips when switching to that tab for the first time
        if (value == 1 && PublicTrips.Count == 0 && !IsLoadingPublicTrips)
        {
            _ = RefreshPublicTripsAsync();
        }
    }

    #endregion

    #region Commands - My Trips

    /// <summary>
    /// Loads user's trips from the server and local database.
    /// </summary>
    [RelayCommand]
    private async Task LoadTripsAsync()
    {
        // Note: Don't guard with IsLoadingTrips here - RefreshView's TwoWay binding
        // sets it to true before invoking the command, which would cause early return
        try
        {
            IsLoadingTrips = true;
            ErrorMessage = null;

            var serverTrips = await _apiClient.GetTripsAsync();
            var downloadedTrips = await _downloadService.GetDownloadedTripsAsync();

            // Check which trip is currently loaded on the map
            var loadedTripId = MainViewModel.CurrentLoadedTripId;
            _logger.LogDebug("LoadTripsAsync: CurrentLoadedTripId = {TripId}", loadedTripId);

            // Build grouped list
            var items = new List<TripListItem>();

            // Add server trips with download status
            foreach (var trip in serverTrips.OrderByDescending(t => t.UpdatedAt))
            {
                var downloaded = downloadedTrips.FirstOrDefault(d => d.ServerId == trip.Id);
                var item = new TripListItem(trip, downloaded);

                // Mark as currently loaded if it matches
                if (loadedTripId.HasValue && trip.Id == loadedTripId.Value)
                {
                    _logger.LogDebug("LoadTripsAsync: Marking trip {TripName} ({TripId}) as currently loaded", trip.Name, trip.Id);
                    item.IsCurrentlyLoaded = true;
                }

                items.Add(item);
            }

            // Group by download status
            var groups = items
                .GroupBy(t => t.GroupName)
                .OrderBy(g => g.Key == "Downloaded" ? 0 : g.Key == "Metadata Only" ? 1 : 2)
                .Select(g => new TripGrouping(g.Key, g.ToList()))
                .ToList();

            MyTrips.Clear();
            foreach (var group in groups)
            {
                MyTrips.Add(group);
            }

            _logger.LogDebug("Loaded {Count} trips", items.Count);

            // Refresh sync queue status
            await RefreshSyncStatusAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading trips");
            ErrorMessage = "Failed to load trips. Please check your connection.";
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading trips");
            ErrorMessage = "Request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading trips");
            ErrorMessage = $"Failed to load trips: {ex.Message}";
        }
        finally
        {
            IsLoadingTrips = false;
            // Mark initial load complete - shimmer won't show on subsequent refreshes
            IsInitialLoad = false;
        }
    }

    /// <summary>
    /// Refreshes the sync queue status counts.
    /// </summary>
    private async Task RefreshSyncStatusAsync()
    {
        try
        {
            PendingSyncCount = await _tripSyncService.GetPendingCountAsync();
            FailedSyncCount = await _tripSyncService.GetFailedCountAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error refreshing sync status");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error refreshing sync status");
        }
    }

    /// <summary>
    /// Moves a trip item to the correct group based on its current GroupName.
    /// Used after download completes to move from "Available on Server" to "Downloaded".
    /// </summary>
    private void MoveItemToCorrectGroup(TripListItem item)
    {
        var targetGroupName = item.GroupName;

        // Find current group containing the item
        TripGrouping? currentGroup = null;
        foreach (var group in MyTrips)
        {
            if (group.Contains(item))
            {
                currentGroup = group;
                break;
            }
        }

        if (currentGroup == null)
        {
            _logger.LogWarning("Item {Name} not found in any group", item.Name);
            return;
        }

        // Already in correct group?
        if (currentGroup.Name == targetGroupName)
        {
            return;
        }

        // Remove from current group
        currentGroup.Remove(item);

        // Remove empty groups
        if (currentGroup.Count == 0)
        {
            MyTrips.Remove(currentGroup);
        }

        // Find or create target group
        var targetGroup = MyTrips.FirstOrDefault(g => g.Name == targetGroupName);
        if (targetGroup == null)
        {
            // Create new group and insert in correct position
            targetGroup = new TripGrouping(targetGroupName, new[] { item });

            // Insert in order: Downloaded (0), Metadata Only (1), Available on Server (2)
            var insertIndex = targetGroupName switch
            {
                "Downloaded" => 0,
                "Metadata Only" => MyTrips.Any(g => g.Name == "Downloaded") ? 1 : 0,
                _ => MyTrips.Count
            };

            MyTrips.Insert(Math.Min(insertIndex, MyTrips.Count), targetGroup);
        }
        else
        {
            // Add to existing group at the top (most recently modified)
            targetGroup.Insert(0, item);
        }

        _logger.LogDebug("Moved trip {Name} from '{From}' to '{To}'", item.Name, currentGroup.Name, targetGroupName);
    }

    /// <summary>
    /// Retries failed sync operations.
    /// </summary>
    [RelayCommand]
    private async Task RetrySyncAsync()
    {
        try
        {
            await _tripSyncService.ResetFailedMutationsAsync();
            await RefreshSyncStatusAsync();
            await _toastService.ShowSuccessAsync("Retrying sync...");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error retrying sync");
            await _toastService.ShowErrorAsync("Network error. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error retrying sync");
            await _toastService.ShowErrorAsync($"Failed to retry: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels all pending sync operations.
    /// </summary>
    [RelayCommand]
    private async Task CancelSyncAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var confirm = await page.DisplayAlertAsync(
            "Cancel Pending Changes",
            "This will discard all pending changes that haven't been synced to the server. This action cannot be undone.",
            "Cancel Changes",
            "Keep");

        if (!confirm)
            return;

        try
        {
            await _tripSyncService.CancelPendingMutationsAsync();
            await RefreshSyncStatusAsync();
            await _toastService.ShowSuccessAsync("Pending changes discarded");

            // Reload trips to reflect any reverted changes
            await LoadTripsAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error canceling sync");
            await _toastService.ShowErrorAsync("Network error. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error canceling sync");
            await _toastService.ShowErrorAsync($"Failed to cancel: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a trip to the map (navigates to MainPage).
    /// Only available for downloaded trips - loads from local storage.
    /// </summary>
    [RelayCommand]
    private async Task LoadTripToMapAsync(TripListItem? item)
    {
        if (item == null)
            return;

        try
        {
            // Load trip details from local storage (only downloaded trips can be loaded)
            var tripDetails = await _downloadService.GetOfflineTripDetailsAsync(item.ServerId);
            if (tripDetails == null)
            {
                await _toastService.ShowErrorAsync("Trip not found in local storage. Please download it first.");
                return;
            }

            // Mark this trip as loaded and clear others BEFORE navigating
            foreach (var group in MyTrips)
            {
                foreach (var tripItem in group)
                {
                    tripItem.IsCurrentlyLoaded = tripItem.ServerId == item.ServerId;
                }
            }

            // Navigate to main page with trip
            await Shell.Current.GoToAsync("//main", new Dictionary<string, object>
            {
                ["LoadTrip"] = tripDetails
            });
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error loading trip to map");
            await _toastService.ShowErrorAsync("Failed to read trip data. Please try downloading again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading trip to map");
            await _toastService.ShowErrorAsync($"Failed to load trip: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates back to the currently loaded trip without reloading.
    /// </summary>
    [RelayCommand]
    private async Task BackToTripAsync(TripListItem? item)
    {
        if (item == null || !item.IsCurrentlyLoaded)
            return;

        // Simply navigate to main page - trip is already loaded
        await Shell.Current.GoToAsync("//main");
    }

    /// <summary>
    /// Quick download - metadata only (no tiles).
    /// </summary>
    [RelayCommand]
    private async Task QuickDownloadAsync(TripListItem? item)
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
    private async Task FullDownloadAsync(TripListItem? item)
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
    private async Task DeleteDownloadAsync(TripListItem? item)
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
            var isCurrentlyLoaded = MainViewModel.CurrentLoadedTripId == item.ServerId;

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
            MoveItemToCorrectGroup(item);

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
    private async Task DeleteTilesOnlyAsync(TripListItem? item)
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
    private async Task CancelDownloadAsync()
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
    private async Task PauseDownloadAsync()
    {
        if (_isProcessingPauseResume || !DownloadingTripId.HasValue)
            return;

        _isProcessingPauseResume = true;
        try
        {
            _logger.LogInformation("Pausing download for trip {TripId}", DownloadingTripId.Value);

            var paused = await _downloadService.PauseDownloadAsync(DownloadingTripId.Value);
            if (paused)
            {
                IsDownloadPaused = true;
                DownloadStatusMessage = "Download paused";

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
    private async Task ResumeDownloadAsync()
    {
        if (_isProcessingPauseResume)
            return;

        _isProcessingPauseResume = true;
        try
        {
            // Handle current session pause
            if (DownloadingTripId.HasValue)
            {
                await ResumeDownloadByTripIdAsync(DownloadingTripId.Value);
                return;
            }

            // Handle previous session paused downloads - find the first one to resume
            var pausedDownloads = await _downloadService.GetPausedDownloadsAsync();
            if (pausedDownloads.Count > 0)
            {
                var firstPaused = pausedDownloads[0];
                // Set up download state for this trip
                DownloadingTripId = firstPaused.TripId;
                DownloadingTripName = firstPaused.TripName;
                _downloadingTripServerId = firstPaused.TripServerId;
                await ResumeDownloadByTripIdAsync(firstPaused.TripId);
                return;
            }

            // No paused downloads found
            await _toastService.ShowErrorAsync("No paused downloads found");
        }
        finally
        {
            _isProcessingPauseResume = false;
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

        // Update UI to show resuming state (keep IsDownloadPaused true until confirmed)
        IsDownloading = true;
        DownloadStatusMessage = "Resuming download...";

        try
        {
            var resumed = await _downloadService.ResumeDownloadAsync(tripId, _downloadCts.Token);
            if (resumed)
            {
                // Resume started successfully - download is now active
                IsDownloadPaused = false;
                // OnDownloadCompleted/OnDownloadPaused events handle final state cleanup
            }
            else
            {
                // Resume failed - provide feedback and reset state
                await _toastService.ShowErrorAsync("Could not resume download");
                IsDownloadPaused = true; // Still paused
                IsDownloading = false;
                DownloadStatusMessage = "Resume failed";
            }
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
            }
        }
    }

    /// <summary>
    /// Shows edit options for a trip (name, notes).
    /// </summary>
    [RelayCommand]
    private async Task EditTripAsync(TripListItem? item)
    {
        if (item == null)
            return;

        var action = await Shell.Current.DisplayActionSheetAsync(
            $"Edit: {item.Name}",
            "Cancel",
            null,
            "Edit Name",
            "Edit Notes");

        switch (action)
        {
            case "Edit Name":
                await EditTripNameAsync(item);
                break;
            case "Edit Notes":
                await EditTripNotesAsync(item);
                break;
        }
    }

    private async Task EditTripNameAsync(TripListItem item)
    {
        var newName = await Shell.Current.DisplayPromptAsync(
            "Edit Trip Name",
            "Enter new name:",
            initialValue: item.Name,
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name)
            return;

        try
        {
            // Optimistically update UI
            var oldName = item.Name;

            // Update local storage first
            await _downloadService.UpdateTripNameAsync(item.ServerId, newName);

            // Sync with server
            await _tripSyncService.UpdateTripAsync(item.ServerId, name: newName);

            // Reload trips to reflect change
            await LoadTripsAsync();

            await _toastService.ShowSuccessAsync("Trip name updated");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating trip name");
            await _toastService.ShowErrorAsync("Network error. Changes saved locally.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating trip name");
            await _toastService.ShowErrorAsync($"Failed to update: {ex.Message}");
        }
    }

    private async Task EditTripNotesAsync(TripListItem item)
    {
        try
        {
            // Load trip details to get current notes
            var tripDetails = await _downloadService.GetOfflineTripDetailsAsync(item.ServerId);
            if (tripDetails == null)
            {
                await _toastService.ShowErrorAsync("Trip not found. Please download it first.");
                return;
            }

            // Navigate to notes editor with trip info
            var navParams = new Dictionary<string, object>
            {
                { "tripId", item.ServerId.ToString() },
                { "notes", tripDetails.Notes ?? string.Empty },
                { "entityType", "Trip" }
            };

            await Shell.Current.GoToAsync("notesEditor", navParams);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error loading trip for notes editor");
            await _toastService.ShowErrorAsync("Failed to read trip data.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening notes editor");
            await _toastService.ShowErrorAsync($"Failed to open editor: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Public Trips

    /// <summary>
    /// Refreshes the public trips list.
    /// </summary>
    [RelayCommand]
    private async Task RefreshPublicTripsAsync()
    {
        if (IsLoadingPublicTrips)
            return;

        try
        {
            IsLoadingPublicTrips = true;
            _publicTripsPage = 1;
            _hasMorePublicTrips = true;
            PublicTrips.Clear();

            await LoadPublicTripsAsync();
        }
        finally
        {
            IsLoadingPublicTrips = false;
        }
    }

    /// <summary>
    /// Loads more public trips (infinite scroll).
    /// </summary>
    [RelayCommand]
    private async Task LoadMorePublicTripsAsync()
    {
        if (IsLoadingMorePublicTrips || !_hasMorePublicTrips || IsLoadingPublicTrips)
            return;

        try
        {
            IsLoadingMorePublicTrips = true;
            _publicTripsPage++;
            await LoadPublicTripsAsync();
        }
        finally
        {
            IsLoadingMorePublicTrips = false;
        }
    }

    /// <summary>
    /// Clone a public trip to user's account.
    /// </summary>
    [RelayCommand]
    private async Task ClonePublicTripAsync(PublicTripSummary? trip)
    {
        if (trip == null || IsCloningTrip)
            return;

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Copy Trip",
            $"Copy \"{trip.Name}\" to your trips? This will create a copy that you can edit.",
            "Copy",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            IsCloningTrip = true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _apiClient.CloneTripAsync(trip.Id, cts.Token);

            if (result?.Success == true && result.NewTripId.HasValue)
            {
                _logger.LogInformation("Successfully cloned trip {TripName}", trip.Name);
                await _toastService.ShowSuccessAsync($"'{trip.Name}' added to your trips");

                // Refresh my trips
                await LoadTripsAsync();

                // Switch to My Trips tab
                SelectedTabIndex = 0;
            }
            else
            {
                var errorMessage = result?.Error ?? "Unknown error";
                await _toastService.ShowErrorAsync($"Clone failed: {errorMessage}");
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error cloning trip");
            await _toastService.ShowErrorAsync("Network error. Please check your connection.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out cloning trip");
            await _toastService.ShowErrorAsync("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error cloning trip");
            await _toastService.ShowErrorAsync("Failed to copy trip. Please try again.");
        }
        finally
        {
            IsCloningTrip = false;
        }
    }

    #endregion

    #region Commands - Navigation

    /// <summary>
    /// Navigate to settings page.
    /// </summary>
    [RelayCommand]
    private async Task GoToSettingsAsync()
    {
        await Shell.Current.GoToAsync("//settings");
    }

    /// <summary>
    /// Dismiss error message.
    /// </summary>
    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
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
            item.IsDownloading = true;
            item.DownloadProgress = 0;

            var summary = new TripSummary
            {
                Id = item.ServerId,
                Name = item.Name,
                BoundingBox = item.BoundingBox
            };

            var result = await _downloadService.DownloadTripAsync(summary, _downloadCts.Token);

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
                    MoveItemToCorrectGroup(item);

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
    /// Loads public trips from API.
    /// </summary>
    private async Task LoadPublicTripsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var response = await _apiClient.GetPublicTripsAsync(
                string.IsNullOrWhiteSpace(PublicSearchQuery) ? null : PublicSearchQuery,
                SelectedPublicSort,
                _publicTripsPage,
                PageSize,
                cts.Token);

            if (response == null)
            {
                _logger.LogWarning("Failed to load public trips");
                return;
            }

            foreach (var trip in response.Trips)
            {
                PublicTrips.Add(trip);
            }

            _hasMorePublicTrips = response.HasMore;
            IsPublicTripsEmpty = PublicTrips.Count == 0;

            _logger.LogDebug("Loaded {Count} public trips (page {Page})", response.Trips.Count, _publicTripsPage);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading public trips");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading public trips");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading public trips");
        }
    }

    /// <summary>
    /// Debounces search to avoid too many API calls.
    /// </summary>
    private async Task DebounceSearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(400, ct);
            if (!ct.IsCancellationRequested)
            {
                await RefreshPublicTripsAsync();
            }
        }
        catch (TaskCanceledException)
        {
            // Expected when cancelled
        }
    }

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

            // Update per-item progress for the trip being downloaded
            UpdateItemDownloadProgress(e.TripId, e.ProgressPercent / 100.0);
        });
    }

    /// <summary>
    /// Updates the download progress on the specific trip item.
    /// </summary>
    private void UpdateItemDownloadProgress(int tripId, double progress)
    {
        // Use tracked ServerId for matching (available from start of download)
        var serverIdToMatch = _downloadingTripServerId;
        if (!serverIdToMatch.HasValue)
            return;

        foreach (var group in MyTrips)
        {
            foreach (var item in group)
            {
                if (item.ServerId == serverIdToMatch.Value)
                {
                    item.IsDownloading = progress < 1.0;
                    item.DownloadProgress = progress;
                    return;
                }
            }
        }
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
            await LoadTripsAsync();
            await CheckForPausedDownloadsAsync();
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
            await LoadTripsAsync();
        });
    }

    /// <summary>
    /// Handles download paused event.
    /// </summary>
    private void OnDownloadPaused(object? sender, DownloadPausedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _logger.LogInformation("Download paused for trip {TripName}: {Reason}, {Completed}/{Total} tiles",
                e.TripName, e.Reason, e.TilesCompleted, e.TotalTiles);

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
                IsDownloading = false;
                IsDownloadPaused = false;
                DownloadingTripName = null;
                DownloadingTripId = null;
                _downloadingTripServerId = null;
            }

            // Refresh paused downloads count
            await CheckForPausedDownloadsAsync();
        });
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        _logger.LogDebug("OnAppearingAsync: MyTrips.Count = {Count}", MyTrips.Count);

        // Load my trips if empty
        if (MyTrips.Count == 0)
        {
            _logger.LogDebug("OnAppearingAsync: Calling LoadTripsAsync");
            await LoadTripsAsync();
        }

        // Check for paused downloads from previous sessions
        await CheckForPausedDownloadsAsync();

        // Always refresh loaded state (even after LoadTripsAsync, in case there's timing issues)
        _logger.LogDebug("OnAppearingAsync: Calling RefreshLoadedTripState");
        RefreshLoadedTripState();

        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Checks for paused downloads from previous sessions.
    /// </summary>
    private async Task CheckForPausedDownloadsAsync()
    {
        try
        {
            var pausedDownloads = await _downloadService.GetPausedDownloadsAsync();
            PausedDownloadsCount = pausedDownloads.Count;

            if (pausedDownloads.Count > 0)
            {
                _logger.LogInformation("Found {Count} paused download(s) from previous session", pausedDownloads.Count);

                // Update the download state for items that are paused
                foreach (var pausedState in pausedDownloads)
                {
                    // Find the item in MyTrips by server ID (more reliable than local ID)
                    foreach (var group in MyTrips)
                    {
                        var item = group.FirstOrDefault(i => i.ServerId == pausedState.TripServerId);
                        if (item != null)
                        {
                            item.DownloadState = TripDownloadState.Downloading;
                            // Set proper paused state for UI (not actively downloading, shows paused progress)
                            item.IsDownloading = false;
                            item.DownloadProgress = pausedState.TotalTileCount > 0
                                ? pausedState.CompletedTileCount / (double)pausedState.TotalTileCount
                                : 0;
                            _logger.LogDebug("Marked trip {TripName} as paused: {Completed}/{Total} tiles",
                                item.Name, pausedState.CompletedTileCount, pausedState.TotalTileCount);
                            break; // Found the item, no need to check other groups
                        }
                    }
                }
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

    /// <summary>
    /// Updates the IsCurrentlyLoaded state for all trip items.
    /// Called when returning to this page to reflect current MainViewModel state.
    /// </summary>
    private void RefreshLoadedTripState()
    {
        var loadedTripId = MainViewModel.CurrentLoadedTripId;
        _logger.LogDebug("RefreshLoadedTripState: CurrentLoadedTripId = {TripId}", loadedTripId);

        foreach (var group in MyTrips)
        {
            foreach (var item in group)
            {
                var isLoaded = loadedTripId.HasValue && item.ServerId == loadedTripId.Value;
                if (item.IsCurrentlyLoaded != isLoaded)
                {
                    _logger.LogDebug("RefreshLoadedTripState: Setting {TripName} ({ServerId}) IsCurrentlyLoaded = {IsLoaded}",
                        item.Name, item.ServerId, isLoaded);
                    item.IsCurrentlyLoaded = isLoaded;
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _downloadService.ProgressChanged -= OnDownloadProgressChanged;
        _downloadService.CacheWarning -= OnCacheWarning;
        _downloadService.CacheCritical -= OnCacheCritical;
        _downloadService.CacheLimitReached -= OnCacheLimitReached;
        _downloadService.DownloadCompleted -= OnDownloadCompleted;
        _downloadService.DownloadFailed -= OnDownloadFailed;
        _downloadService.DownloadPaused -= OnDownloadPaused;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        base.Cleanup();
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Grouping of trips by status.
/// </summary>
public class TripGrouping : ObservableCollection<TripListItem>
{
    /// <summary>
    /// Gets the group name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a new trip grouping.
    /// </summary>
    public TripGrouping(string name, IEnumerable<TripListItem> items) : base(items)
    {
        Name = name;
    }
}

/// <summary>
/// Trip list item with download status.
/// </summary>
public partial class TripListItem : ObservableObject
{
    /// <summary>
    /// Gets the server ID.
    /// </summary>
    public Guid ServerId { get; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the stats text (dynamically calculated based on download state).
    /// Shows regions, places, segments, areas, and tiles.
    /// </summary>
    public string StatsText
    {
        get
        {
            if (DownloadState == TripDownloadState.Downloading)
                return "Downloading...";

            if (DownloadState == TripDownloadState.ServerOnly)
                return _serverStatsText ?? "Available online";

            // For downloaded trips (MetadataOnly or Complete), show detailed stats
            if (DownloadedEntity == null)
                return DownloadState == TripDownloadState.Complete ? "Downloaded" : "Metadata only";

            var parts = new List<string>();

            if (DownloadedEntity.RegionCount > 0)
                parts.Add($"{DownloadedEntity.RegionCount} region{(DownloadedEntity.RegionCount == 1 ? "" : "s")}");

            if (DownloadedEntity.PlaceCount > 0)
                parts.Add($"{DownloadedEntity.PlaceCount} place{(DownloadedEntity.PlaceCount == 1 ? "" : "s")}");

            if (DownloadedEntity.SegmentCount > 0)
                parts.Add($"{DownloadedEntity.SegmentCount} segment{(DownloadedEntity.SegmentCount == 1 ? "" : "s")}");

            if (DownloadedEntity.AreaCount > 0)
                parts.Add($"{DownloadedEntity.AreaCount} area{(DownloadedEntity.AreaCount == 1 ? "" : "s")}");

            if (DownloadState == TripDownloadState.Complete && DownloadedEntity.TileCount > 0)
                parts.Add($"{DownloadedEntity.TileCount} tiles");
            else if (DownloadState == TripDownloadState.MetadataOnly)
                parts.Add("No tiles");

            return parts.Count > 0 ? string.Join(" • ", parts) : "Empty trip";
        }
    }

    /// <summary>
    /// Server stats text (cached from initial load).
    /// </summary>
    private readonly string? _serverStatsText;

    /// <summary>
    /// Gets the bounding box (for download).
    /// </summary>
    public BoundingBox? BoundingBox { get; }

    /// <summary>
    /// Gets or sets the download status.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloaded))]
    [NotifyPropertyChangedFor(nameof(IsMetadataOnly))]
    [NotifyPropertyChangedFor(nameof(IsServerOnly))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanDeleteTilesOnly))]
    [NotifyPropertyChangedFor(nameof(GroupName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    [NotifyPropertyChangedFor(nameof(CanQuickDownload))]
    [NotifyPropertyChangedFor(nameof(CanFullDownload))]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private TripDownloadState _downloadState;

    /// <summary>
    /// Gets or sets the downloaded entity (for stats updates).
    /// </summary>
    public Data.Entities.DownloadedTripEntity? DownloadedEntity { get; set; }

    /// <summary>
    /// Gets whether the trip is downloaded.
    /// </summary>
    public bool IsDownloaded => DownloadState == TripDownloadState.Complete;

    /// <summary>
    /// Gets whether the trip has metadata only.
    /// </summary>
    public bool IsMetadataOnly => DownloadState == TripDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether the trip is on server only.
    /// </summary>
    public bool IsServerOnly => DownloadState == TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether the trip has any local data that can be deleted.
    /// Includes complete downloads, metadata only, and failed/stuck downloads.
    /// </summary>
    public bool CanDelete => DownloadState != TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets the group name for this trip.
    /// </summary>
    public string GroupName => DownloadState switch
    {
        TripDownloadState.Complete => "Downloaded",
        TripDownloadState.MetadataOnly => "Metadata Only",
        _ => "Available on Server"
    };

    /// <summary>
    /// Gets the status text.
    /// </summary>
    public string StatusText => DownloadState switch
    {
        TripDownloadState.Complete => "Offline",
        TripDownloadState.MetadataOnly => "Metadata",
        TripDownloadState.Downloading => "Downloading...",
        _ => "Online"
    };

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public Color StatusColor => DownloadState switch
    {
        TripDownloadState.Complete => Colors.Green,
        TripDownloadState.MetadataOnly => Colors.Orange,
        TripDownloadState.Downloading => Colors.Blue,
        _ => Colors.Gray
    };

    /// <summary>
    /// Gets whether Load to Map is available.
    /// Only available for downloaded trips (metadata or complete) that aren't already loaded.
    /// </summary>
    public bool CanLoadToMap => !IsCurrentlyLoaded &&
                                 (DownloadState == TripDownloadState.MetadataOnly ||
                                  DownloadState == TripDownloadState.Complete);

    /// <summary>
    /// Gets or sets whether this trip is currently loaded on the map.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private bool _isCurrentlyLoaded;

    /// <summary>
    /// Gets whether Quick Download is available.
    /// </summary>
    public bool CanQuickDownload => DownloadState == TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether Full Download is available.
    /// </summary>
    public bool CanFullDownload => DownloadState == TripDownloadState.ServerOnly || DownloadState == TripDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether Delete Tiles Only is available.
    /// Only available for trips with offline maps (Complete state with tiles).
    /// </summary>
    public bool CanDeleteTilesOnly => DownloadState == TripDownloadState.Complete &&
                                       (DownloadedEntity?.TileCount ?? 0) > 0;

    /// <summary>
    /// Gets whether editing is available.
    /// Only available for downloaded trips (have local data to edit).
    /// </summary>
    public bool CanEdit => DownloadState == TripDownloadState.MetadataOnly ||
                            DownloadState == TripDownloadState.Complete;

    /// <summary>
    /// Gets or sets whether downloading.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress (0.0-1.0).
    /// </summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>
    /// Creates a new trip list item.
    /// </summary>
    public TripListItem(TripSummary trip, Data.Entities.DownloadedTripEntity? downloaded)
    {
        ServerId = trip.Id;
        Name = trip.Name;
        BoundingBox = trip.BoundingBox;

        // Cache server stats for fallback
        _serverStatsText = trip.PlacesCount > 0 ? trip.StatsText : null;
        DownloadedEntity = downloaded;

        // Determine download state
        if (downloaded == null)
        {
            _downloadState = TripDownloadState.ServerOnly;
        }
        else if (downloaded.Status == Data.Entities.TripDownloadStatus.Complete)
        {
            _downloadState = TripDownloadState.Complete;
        }
        else if (downloaded.Status == Data.Entities.TripDownloadStatus.MetadataOnly)
        {
            _downloadState = TripDownloadState.MetadataOnly;
        }
        else
        {
            _downloadState = TripDownloadState.Downloading;
        }
    }
}

/// <summary>
/// Trip download state.
/// </summary>
public enum TripDownloadState
{
    /// <summary>Trip exists only on server.</summary>
    ServerOnly,

    /// <summary>Trip metadata is downloaded but no tiles.</summary>
    MetadataOnly,

    /// <summary>Trip is being downloaded.</summary>
    Downloading,

    /// <summary>Trip is fully downloaded with tiles.</summary>
    Complete
}

#endregion
