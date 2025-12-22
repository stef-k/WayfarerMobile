using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
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
    private readonly ILogger<TripsViewModel> _logger;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadCts;
    private int _publicTripsPage = 1;
    private const int PageSize = 20;
    private bool _hasMorePublicTrips = true;

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
        ILogger<TripsViewModel> logger)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _downloadService = downloadService;
        _toastService = toastService;
        _logger = logger;
        Title = "Trips";

        // Subscribe to download progress
        _downloadService.ProgressChanged += OnDownloadProgressChanged;
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

            // Build grouped list
            var items = new List<TripListItem>();

            // Add server trips with download status
            foreach (var trip in serverTrips.OrderByDescending(t => t.UpdatedAt))
            {
                var downloaded = downloadedTrips.FirstOrDefault(d => d.ServerId == trip.Id);
                items.Add(new TripListItem(trip, downloaded));
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trips");
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

            // Navigate to main page with trip
            await Shell.Current.GoToAsync("//main", new Dictionary<string, object>
            {
                ["LoadTrip"] = tripDetails
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trip to map");
            await _toastService.ShowErrorAsync($"Failed to load trip: {ex.Message}");
        }
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
    /// </summary>
    [RelayCommand]
    private async Task FullDownloadAsync(TripListItem? item)
    {
        if (item == null || IsDownloading)
            return;

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
            await _downloadService.DeleteTripAsync(item.ServerId);

            // Update item directly instead of full reload (avoids shimmer)
            item.DownloadState = TripDownloadState.ServerOnly;
            item.DownloadedEntity = null;
            OnPropertyChanged(nameof(MyTrips));

            await _toastService.ShowSuccessAsync("Offline data deleted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete offline data");
            await _toastService.ShowErrorAsync($"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancel ongoing download.
    /// </summary>
    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
        IsDownloading = false;
        DownloadingTripName = null;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone trip");
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
        try
        {
            IsDownloading = true;
            DownloadingTripName = item.Name;
            DownloadProgress = 0;
            DownloadStatusMessage = "Starting download...";
            _downloadCts = new CancellationTokenSource();

            var summary = new TripSummary
            {
                Id = item.ServerId,
                Name = item.Name,
                BoundingBox = item.BoundingBox
            };

            var result = await _downloadService.DownloadTripAsync(summary, _downloadCts.Token);

            if (result != null)
            {
                // Update item directly instead of full reload (avoids shimmer)
                item.DownloadState = includeTiles ? TripDownloadState.Complete : TripDownloadState.MetadataOnly;
                item.DownloadedEntity = result;
                OnPropertyChanged(nameof(MyTrips));

                await _toastService.ShowSuccessAsync($"'{item.Name}' downloaded");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed");
            await _toastService.ShowErrorAsync($"Download failed: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
            DownloadingTripName = null;
            _downloadCts?.Dispose();
            _downloadCts = null;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public trips");
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
            DownloadProgress = e.ProgressPercent / 100.0;
            DownloadStatusMessage = e.Message;
        });
    }

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        // Load my trips if empty
        if (MyTrips.Count == 0)
        {
            await LoadTripsAsync();
        }

        await base.OnAppearingAsync();
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _downloadService.ProgressChanged -= OnDownloadProgressChanged;
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
    /// Format: "X Regions / Y Places" with download status suffix.
    /// </summary>
    public string StatsText => DownloadState switch
    {
        TripDownloadState.Complete => DownloadedEntity != null
            ? $"{_serverStatsText} • {DownloadedEntity.TileCount} tiles"
            : _serverStatsText ?? "Downloaded",
        TripDownloadState.MetadataOnly => DownloadedEntity != null
            ? $"{_serverStatsText} • No tiles"
            : _serverStatsText ?? "Metadata only",
        TripDownloadState.Downloading => "Downloading...",
        _ => _serverStatsText ?? "Empty trip"
    };

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
    /// Only available for downloaded trips (metadata or complete).
    /// </summary>
    public bool CanLoadToMap => DownloadState == TripDownloadState.MetadataOnly ||
                                 DownloadState == TripDownloadState.Complete;

    /// <summary>
    /// Gets whether Quick Download is available.
    /// </summary>
    public bool CanQuickDownload => DownloadState == TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether Full Download is available.
    /// </summary>
    public bool CanFullDownload => DownloadState == TripDownloadState.ServerOnly || DownloadState == TripDownloadState.MetadataOnly;

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

        // Cache server stats (Regions / Places format)
        _serverStatsText = trip.StatsText;
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
