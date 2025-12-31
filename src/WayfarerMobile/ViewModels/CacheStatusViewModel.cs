using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the cache status bottom sheet.
/// Displays detailed cache coverage information and overlay controls.
/// </summary>
public partial class CacheStatusViewModel : BaseViewModel
{
    #region Fields

    private readonly ICacheStatusService _cacheStatusService;
    private readonly IToastService _toastService;
    private readonly IDialogService _dialogService;
    private readonly UnifiedTileCacheService _unifiedTileService;

    private double _latitude;
    private double _longitude;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CacheStatusViewModel.
    /// </summary>
    public CacheStatusViewModel(
        ICacheStatusService cacheStatusService,
        IToastService toastService,
        IDialogService dialogService,
        UnifiedTileCacheService unifiedTileService)
    {
        _cacheStatusService = cacheStatusService;
        _toastService = toastService;
        _dialogService = dialogService;
        _unifiedTileService = unifiedTileService;

        Title = "Cache Status";
    }

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the overall coverage status.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    private CacheCoverageStatus _status = CacheCoverageStatus.Unknown;

    /// <summary>
    /// Gets or sets the coverage percentage (0.0 to 1.0).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoveragePercentText))]
    [NotifyPropertyChangedFor(nameof(CoverageProgress))]
    private double _coveragePercent;

    /// <summary>
    /// Gets or sets the total tiles checked.
    /// </summary>
    [ObservableProperty]
    private int _totalTiles;

    /// <summary>
    /// Gets or sets the cached tiles count.
    /// </summary>
    [ObservableProperty]
    private int _cachedTiles;

    /// <summary>
    /// Gets or sets the live cache tiles count.
    /// </summary>
    [ObservableProperty]
    private int _liveCachedTiles;

    /// <summary>
    /// Gets or sets the trip cache tiles count.
    /// </summary>
    [ObservableProperty]
    private int _tripCachedTiles;

    /// <summary>
    /// Gets or sets the formatted local size string.
    /// </summary>
    [ObservableProperty]
    private string _localSizeText = "0 B";

    /// <summary>
    /// Gets or sets the formatted total cache size string.
    /// </summary>
    [ObservableProperty]
    private string _totalSizeText = "0 B";

    /// <summary>
    /// Gets or sets the formatted live cache size string.
    /// </summary>
    [ObservableProperty]
    private string _liveSizeText = "0 B";

    /// <summary>
    /// Gets or sets the formatted trip cache size string.
    /// </summary>
    [ObservableProperty]
    private string _tripSizeText = "0 B";

    /// <summary>
    /// Gets or sets the active trip name.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveTrip))]
    private string? _activeTripName;

    /// <summary>
    /// Gets or sets the number of downloaded trips.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadedTripsText))]
    private int _downloadedTripCount;

    /// <summary>
    /// Gets or sets whether network is available.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkStatusText))]
    private bool _hasNetwork;

    /// <summary>
    /// Gets or sets whether the cache overlay is visible on the map.
    /// </summary>
    [ObservableProperty]
    private bool _isOverlayEnabled;

    /// <summary>
    /// Gets or sets the zoom level coverage list.
    /// </summary>
    public ObservableCollection<ZoomCoverageStatus> ZoomCoverage { get; } = new();

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the status text display.
    /// </summary>
    public string StatusText => Status switch
    {
        CacheCoverageStatus.Excellent => "Excellent",
        CacheCoverageStatus.Good => "Good",
        CacheCoverageStatus.Partial => "Partial",
        CacheCoverageStatus.Poor => "Poor",
        CacheCoverageStatus.None => "No Cache",
        CacheCoverageStatus.Error => "Error",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public Color StatusColor => Status switch
    {
        CacheCoverageStatus.Excellent => Colors.LimeGreen,
        CacheCoverageStatus.Good => Colors.LimeGreen,
        CacheCoverageStatus.Partial => Colors.Orange,
        CacheCoverageStatus.Poor => Colors.Red,
        CacheCoverageStatus.None => Colors.Red,
        _ => Colors.Gray
    };

    /// <summary>
    /// Gets the formatted coverage percentage text.
    /// </summary>
    public string CoveragePercentText => $"{CoveragePercent:P0}";

    /// <summary>
    /// Gets the coverage progress for progress bar (0-100).
    /// </summary>
    public double CoverageProgress => CoveragePercent * 100;

    /// <summary>
    /// Gets whether there is an active trip.
    /// </summary>
    public bool HasActiveTrip => !string.IsNullOrEmpty(ActiveTripName);

    /// <summary>
    /// Gets the downloaded trips text.
    /// </summary>
    public string DownloadedTripsText => DownloadedTripCount == 1
        ? "1 trip downloaded"
        : $"{DownloadedTripCount} trips downloaded";

    /// <summary>
    /// Gets the network status text.
    /// </summary>
    public string NetworkStatusText => HasNetwork ? "Online" : "Offline";

    /// <summary>
    /// Gets the tiles breakdown text.
    /// </summary>
    public string TilesBreakdownText => $"Live: {LiveCachedTiles} | Trip: {TripCachedTiles}";

    #endregion

    #region Commands

    /// <summary>
    /// Command to load cache status data.
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            // Update overlay state
            IsOverlayEnabled = _cacheStatusService.IsOverlayVisible;

            // Get detailed status
            var result = await _cacheStatusService.GetDetailedStatusAsync(_latitude, _longitude);

            // Update properties
            Status = result.Status;
            CoveragePercent = result.CoveragePercent;
            TotalTiles = result.TotalTiles;
            CachedTiles = result.CachedTiles;
            LiveCachedTiles = result.LiveCachedTiles;
            TripCachedTiles = result.TripCachedTiles;
            LocalSizeText = FormatSize(result.LocalSizeBytes);
            TotalSizeText = FormatSize(result.TotalAppSizeBytes);
            LiveSizeText = FormatSize(result.LiveCacheSizeBytes);
            TripSizeText = FormatSize(result.TripCacheSizeBytes);
            ActiveTripName = result.ActiveTripName;
            DownloadedTripCount = result.DownloadedTripCount;
            HasNetwork = result.HasNetwork;

            // Update zoom coverage
            ZoomCoverage.Clear();
            foreach (var zoom in result.ZoomCoverage)
            {
                ZoomCoverage.Add(new ZoomCoverageStatus
                {
                    Zoom = zoom.Zoom,
                    TotalTiles = zoom.TotalTiles,
                    CachedTiles = zoom.CachedTiles,
                    LiveTiles = zoom.LiveTiles,
                    TripTiles = zoom.TripTiles,
                    CoveragePercent = zoom.CoveragePercent
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CacheStatusViewModel] Error loading data: {ex.Message}");
            Status = CacheCoverageStatus.Error;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Command to toggle the cache overlay on the map.
    /// </summary>
    [RelayCommand]
    private async Task ToggleOverlayAsync()
    {
        try
        {
            var isNowVisible = await _cacheStatusService.ToggleOverlayAsync(_latitude, _longitude);
            IsOverlayEnabled = isNowVisible;

            var message = isNowVisible
                ? "Cache overlay shown on map"
                : "Cache overlay hidden";
            await _toastService.ShowSuccessAsync(message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CacheStatusViewModel] Error toggling overlay: {ex.Message}");
            await _toastService.ShowErrorAsync("Failed to toggle overlay");
        }
    }

    /// <summary>
    /// Command to clear the live cache.
    /// </summary>
    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "Clear Cache",
                $"This will clear all {TotalSizeText} of cached tiles. You'll need an internet connection to reload them. Continue?",
                "Clear",
                "Cancel");

            if (!confirm) return;

            IsBusy = true;
            await _unifiedTileService.ClearAllCachesAsync();
            await _toastService.ShowSuccessAsync("Cache cleared");

            // Refresh data
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CacheStatusViewModel] Error clearing cache: {ex.Message}");
            await _toastService.ShowErrorAsync("Failed to clear cache");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the location to check cache status for.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    public void SetLocation(double latitude, double longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
    }

    /// <inheritdoc/>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadDataAsync();
    }

    #endregion

    #region Private Methods

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    #endregion
}
