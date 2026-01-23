using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Collections;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the My Trips tab - manages user's trips, downloads, and sync status.
/// </summary>
public partial class MyTripsViewModel : BaseViewModel, ITripDownloadCallbacks
{
    private readonly IApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly TripDownloadService _downloadService;
    private readonly ITripEditingService _tripEditingService;
    private readonly IToastService _toastService;
    private readonly ITripNavigationService _tripNavigationService;
    private readonly ITripSyncService _tripSyncService;
    private readonly ITripStateManager _tripStateManager;
    private readonly IDownloadStateService _downloadStateService;
    private readonly ILogger<MyTripsViewModel> _logger;
    private bool _hasRecoveredStuckDownloads;

    #region Observable Properties

    /// <summary>
    /// Gets the collection of user's trips grouped by status.
    /// </summary>
    public ObservableCollection<TripGrouping> Trips { get; } = new();

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

    /// <summary>
    /// Gets or sets the error message (surfaced to coordinator).
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Guard against rapid taps on Load to Map button.
    /// </summary>
    private bool _isLoadingToMap;

    /// <summary>
    /// Backing field for IsPageReady.
    /// </summary>
    private bool _isPageReady;

    /// <summary>
    /// Gets whether the page is ready for navigation.
    /// This prevents the ObjectDisposedException crash when user taps Load
    /// before OnAppearingAsync finishes (issue #185).
    /// Exposed for UI binding to disable Load button during initialization.
    /// </summary>
    public bool IsPageReady
    {
        get => _isPageReady;
        private set
        {
            if (_isPageReady != value)
            {
                _isPageReady = value;
                OnPropertyChanged();
            }
        }
    }

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

    #region Child ViewModels

    /// <summary>
    /// Gets the download ViewModel for download operations.
    /// </summary>
    public TripDownloadViewModel Download { get; }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MyTripsViewModel.
    /// </summary>
    public MyTripsViewModel(
        IApiClient apiClient,
        ISettingsService settingsService,
        TripDownloadService downloadService,
        ITripEditingService tripEditingService,
        IToastService toastService,
        ITripNavigationService tripNavigationService,
        ITripSyncService tripSyncService,
        ITripStateManager tripStateManager,
        IDownloadStateService downloadStateService,
        TripDownloadViewModel downloadViewModel,
        ILogger<MyTripsViewModel> logger)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _downloadService = downloadService;
        _tripEditingService = tripEditingService;
        _toastService = toastService;
        _tripNavigationService = tripNavigationService;
        _tripSyncService = tripSyncService;
        _tripStateManager = tripStateManager;
        _downloadStateService = downloadStateService;
        _logger = logger;
        Title = "My Trips";

        // Set up the download ViewModel with callbacks to this ViewModel
        Download = downloadViewModel;
        Download.SetCallbacks(this);
    }

    #endregion

    #region Commands - Load Trips

    /// <summary>
    /// Loads user's trips from the server and local database.
    /// </summary>
    [RelayCommand]
    public async Task LoadTripsAsync()
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
            var loadedTripId = _tripStateManager.CurrentLoadedTripId;
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

            Trips.Clear();
            foreach (var group in groups)
            {
                Trips.Add(group);
            }

            _logger.LogDebug("Loaded {Count} trips", items.Count);

            // Refresh sync queue status
            await RefreshSyncStatusAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error loading trips: {Message}", ex.Message);
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
            _logger.LogNetworkWarningIfOnline("Network error refreshing sync status: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error refreshing sync status");
        }
    }

    #endregion

    #region Commands - Sync

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
            _logger.LogNetworkWarningIfOnline("Network error retrying sync: {Message}", ex.Message);
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
            _logger.LogNetworkWarningIfOnline("Network error canceling sync: {Message}", ex.Message);
            await _toastService.ShowErrorAsync("Network error. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error canceling sync");
            await _toastService.ShowErrorAsync($"Failed to cancel: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Trip Actions

    /// <summary>
    /// Loads a trip to the map (navigates to MainPage).
    /// Only available for downloaded trips - loads from local storage.
    /// </summary>
    [RelayCommand]
    private async Task LoadTripToMapAsync(TripListItem? item)
    {
        if (item == null)
            return;

        // Guard against navigation before page is ready (issue #185)
        // This prevents ObjectDisposedException when user taps Load before OnAppearingAsync completes
        if (!IsPageReady)
        {
            _logger.LogDebug("LoadTripToMapAsync: Page not ready, ignoring tap");
            return;
        }

        // Guard against rapid taps - prevent multiple concurrent loads
        if (_isLoadingToMap)
        {
            _logger.LogDebug("LoadTripToMapAsync: Ignoring rapid tap, already loading");
            return;
        }

        try
        {
            _isLoadingToMap = true;

            // Load trip details from local storage (only downloaded trips can be loaded)
            var tripDetails = await _downloadService.GetOfflineTripDetailsAsync(item.ServerId);
            if (tripDetails == null)
            {
                await _toastService.ShowErrorAsync("Trip not found in local storage. Please download it first.");
                return;
            }

            // Mark this trip as loaded and clear others BEFORE navigating
            foreach (var group in Trips)
            {
                foreach (var tripItem in group)
                {
                    tripItem.IsCurrentlyLoaded = tripItem.ServerId == item.ServerId;
                }
            }

            // Navigate to main page with trip
            // Issue #191: Include a unique token so MainPage can distinguish
            // fresh navigation from Shell re-applying cached parameters
            await Shell.Current.GoToAsync("//main", new Dictionary<string, object>
            {
                ["LoadTrip"] = tripDetails,
                ["LoadTripToken"] = Guid.NewGuid()
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
        finally
        {
            _isLoadingToMap = false;
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
            await _tripEditingService.UpdateTripNameAsync(item.ServerId, newName);

            // Sync with server
            await _tripSyncService.UpdateTripAsync(item.ServerId, name: newName);

            // Reload trips to reflect change
            await LoadTripsAsync();

            await _toastService.ShowSuccessAsync("Trip name updated");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error updating trip name: {Message}", ex.Message);
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

    #region Public Methods

    /// <summary>
    /// Clears the error message.
    /// </summary>
    public void ClearError()
    {
        ErrorMessage = null;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Moves a trip item to the correct group based on its current GroupName.
    /// Used after download completes to move from "Available on Server" to "Downloaded".
    /// </summary>
    private void MoveItemToCorrectGroup(TripListItem item)
    {
        var targetGroupName = item.GroupName;

        // Find current group containing the item
        TripGrouping? currentGroup = null;
        foreach (var group in Trips)
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
            Trips.Remove(currentGroup);
        }

        // Find or create target group
        var targetGroup = Trips.FirstOrDefault(g => g.Name == targetGroupName);
        if (targetGroup == null)
        {
            // Create new group and insert in correct position
            targetGroup = new TripGrouping(targetGroupName, new[] { item });

            // Insert in order: Downloaded (0), Metadata Only (1), Available on Server (2)
            var insertIndex = targetGroupName switch
            {
                "Downloaded" => 0,
                "Metadata Only" => Trips.Any(g => g.Name == "Downloaded") ? 1 : 0,
                _ => Trips.Count
            };

            Trips.Insert(Math.Min(insertIndex, Trips.Count), targetGroup);
        }
        else
        {
            // Add to existing group at the top (most recently modified)
            targetGroup.Insert(0, item);
        }

        _logger.LogDebug("Moved trip {Name} from '{From}' to '{To}'", item.Name, currentGroup.Name, targetGroupName);
    }

    /// <summary>
    /// Updates the IsCurrentlyLoaded state for all trip items.
    /// Called when returning to this page to reflect current MainViewModel state.
    /// </summary>
    private void RefreshLoadedTripState()
    {
        var loadedTripId = _tripStateManager.CurrentLoadedTripId;
        _logger.LogDebug("RefreshLoadedTripState: CurrentLoadedTripId = {TripId}", loadedTripId);

        foreach (var group in Trips)
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

    /// <summary>
    /// Checks for paused downloads from previous sessions and updates item states.
    /// </summary>
    private async Task CheckForPausedDownloadsAsync()
    {
        try
        {
            // Update the Download ViewModel's count
            await Download.RefreshPausedDownloadsCountAsync();

            // Also update item states for paused downloads
            var pausedDownloads = await _downloadService.GetPausedDownloadsAsync();
            if (pausedDownloads.Count > 0)
            {
                _logger.LogInformation("Found {Count} paused download(s) from previous session", pausedDownloads.Count);

                // Update the download state for items that are paused
                foreach (var pausedState in pausedDownloads)
                {
                    // Find the item in Trips by server ID (more reliable than local ID)
                    foreach (var group in Trips)
                    {
                        var item = group.FirstOrDefault(i => i.ServerId == pausedState.TripServerId);
                        if (item != null)
                        {
                            // Set proper paused state based on pause reason
                            item.UnifiedState = pausedState.Status == DownloadStateStatus.LimitReached
                                ? Core.Enums.UnifiedDownloadState.PausedCacheLimit
                                : Core.Enums.UnifiedDownloadState.PausedByUser;
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

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        // Mark page as not ready until initialization completes (issue #185)
        IsPageReady = false;
        _logger.LogDebug("OnAppearingAsync: Trips.Count = {Count}", Trips.Count);

        // Recover stuck downloads once per session (downloads interrupted by app closure)
        if (!_hasRecoveredStuckDownloads)
        {
            _hasRecoveredStuckDownloads = true;
            var recovered = await _downloadStateService.RecoverStuckDownloadsAsync();
            if (recovered > 0)
            {
                _logger.LogInformation("Recovered {Count} stuck download(s) from previous session", recovered);
            }
        }

        // Load trips if empty
        if (Trips.Count == 0)
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

        // Mark page as ready only on successful initialization (issue #185)
        // If any step above fails/throws, page remains not ready to prevent navigation
        IsPageReady = true;
        _logger.LogDebug("OnAppearingAsync: Page ready");
    }

    /// <inheritdoc/>
    public override Task OnDisappearingAsync()
    {
        // Reset page ready state when page disappears (issue #185)
        // This handles app suspend/resume - IsPageReady must be re-established
        // by OnAppearingAsync when page becomes visible again
        IsPageReady = false;
        _logger.LogDebug("OnDisappearingAsync: Page ready reset");
        return base.OnDisappearingAsync();
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        Download.Dispose();
        base.Cleanup();
    }

    #endregion

    #region ITripDownloadCallbacks Implementation

    /// <inheritdoc/>
    Task ITripDownloadCallbacks.RefreshTripsAsync() => LoadTripsAsync();

    /// <inheritdoc/>
    void ITripDownloadCallbacks.MoveItemToCorrectGroup(TripListItem item) => MoveItemToCorrectGroup(item);

    /// <inheritdoc/>
    TripListItem? ITripDownloadCallbacks.FindItemByServerId(Guid serverId)
    {
        foreach (var group in Trips)
        {
            var item = group.FirstOrDefault(i => i.ServerId == serverId);
            if (item != null)
                return item;
        }
        return null;
    }

    /// <inheritdoc/>
    void ITripDownloadCallbacks.UpdateItemProgress(Guid serverId, double progress, bool isDownloading)
    {
        foreach (var group in Trips)
        {
            foreach (var item in group)
            {
                if (item.ServerId == serverId)
                {
                    item.IsDownloading = isDownloading && progress < 1.0;
                    item.DownloadProgress = progress;
                    return;
                }
            }
        }
    }

    /// <inheritdoc/>
    IReadOnlyList<TripGrouping> ITripDownloadCallbacks.TripGroups => Trips.ToList().AsReadOnly();

    /// <inheritdoc/>
    async Task ITripDownloadCallbacks.CheckForPausedDownloadsAsync()
    {
        await CheckForPausedDownloadsAsync();
    }

    /// <inheritdoc/>
    void ITripDownloadCallbacks.UpdateTripState(Guid serverId, Core.Enums.UnifiedDownloadState newState, bool isMetadataComplete, bool hasTiles)
    {
        foreach (var group in Trips)
        {
            var item = group.FirstOrDefault(t => t.ServerId == serverId);
            if (item != null)
            {
                item.UpdateState(newState, isMetadataComplete, hasTiles);
                MoveItemToCorrectGroup(item);
                return;
            }
        }
    }

    #endregion
}
