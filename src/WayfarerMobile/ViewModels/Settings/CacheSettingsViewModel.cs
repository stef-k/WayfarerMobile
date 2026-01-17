using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for offline map cache settings including tile server configuration.
/// </summary>
public partial class CacheSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether offline map cache is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _mapOfflineCacheEnabled;

    /// <summary>
    /// Gets or sets the prefetch radius (1-10).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrefetchRadiusGridSize))]
    private int _liveCachePrefetchRadius;

    /// <summary>
    /// Gets or sets the maximum live cache size in MB.
    /// </summary>
    [ObservableProperty]
    private int _maxLiveCacheSizeMB;

    /// <summary>
    /// Gets or sets the maximum trip cache size in MB.
    /// </summary>
    [ObservableProperty]
    private int _maxTripCacheSizeMB;

    /// <summary>
    /// Gets or sets the prefetch distance threshold in meters.
    /// </summary>
    [ObservableProperty]
    private int _prefetchDistanceThresholdMeters;

    /// <summary>
    /// Gets or sets the maximum concurrent tile downloads (1-4).
    /// </summary>
    [ObservableProperty]
    private int _maxConcurrentTileDownloads;

    /// <summary>
    /// Gets or sets the minimum delay between tile requests in ms (50-5000).
    /// </summary>
    [ObservableProperty]
    private int _minTileRequestDelayMs;

    /// <summary>
    /// Gets or sets the custom tile server URL.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultTileServer))]
    private string _tileServerUrl = string.Empty;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the grid size description for the current prefetch radius.
    /// </summary>
    public string PrefetchRadiusGridSize => $"{2 * LiveCachePrefetchRadius + 1}×{2 * LiveCachePrefetchRadius + 1} tiles";

    /// <summary>
    /// Gets whether the current tile server URL is the default OSM server.
    /// </summary>
    public bool IsDefaultTileServer => TileServerUrl == SettingsService.DefaultTileServerUrl;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CacheSettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    public CacheSettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads cache settings from the service.
    /// </summary>
    public void LoadSettings()
    {
        MapOfflineCacheEnabled = _settingsService.MapOfflineCacheEnabled;
        LiveCachePrefetchRadius = _settingsService.LiveCachePrefetchRadius;
        MaxLiveCacheSizeMB = _settingsService.MaxLiveCacheSizeMB;
        MaxTripCacheSizeMB = _settingsService.MaxTripCacheSizeMB;
        MaxConcurrentTileDownloads = _settingsService.MaxConcurrentTileDownloads;
        MinTileRequestDelayMs = _settingsService.MinTileRequestDelayMs;
        PrefetchDistanceThresholdMeters = _settingsService.PrefetchDistanceThresholdMeters;
        TileServerUrl = _settingsService.TileServerUrl;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Resets the tile server URL to the default OSM server.
    /// </summary>
    [RelayCommand]
    private void ResetTileServerUrl()
    {
        TileServerUrl = SettingsService.DefaultTileServerUrl;
    }

    #endregion

    #region Property Changed Handlers

    /// <summary>
    /// Saves offline cache setting.
    /// </summary>
    partial void OnMapOfflineCacheEnabledChanged(bool value)
    {
        _settingsService.MapOfflineCacheEnabled = value;
    }

    /// <summary>
    /// Saves prefetch radius setting.
    /// </summary>
    partial void OnLiveCachePrefetchRadiusChanged(int value)
    {
        _settingsService.LiveCachePrefetchRadius = value;
    }

    /// <summary>
    /// Saves max live cache size setting.
    /// </summary>
    partial void OnMaxLiveCacheSizeMBChanged(int value)
    {
        _settingsService.MaxLiveCacheSizeMB = value;
    }

    /// <summary>
    /// Saves max trip cache size setting.
    /// </summary>
    partial void OnMaxTripCacheSizeMBChanged(int value)
    {
        _settingsService.MaxTripCacheSizeMB = value;
    }

    /// <summary>
    /// Saves max concurrent tile downloads setting.
    /// </summary>
    partial void OnMaxConcurrentTileDownloadsChanged(int value)
    {
        _settingsService.MaxConcurrentTileDownloads = value;
    }

    /// <summary>
    /// Saves min tile request delay setting.
    /// </summary>
    partial void OnMinTileRequestDelayMsChanged(int value)
    {
        _settingsService.MinTileRequestDelayMs = value;
    }

    /// <summary>
    /// Saves prefetch distance threshold setting.
    /// </summary>
    partial void OnPrefetchDistanceThresholdMetersChanged(int value)
    {
        _settingsService.PrefetchDistanceThresholdMeters = value;
    }

    /// <summary>
    /// Saves tile server URL setting.
    /// </summary>
    partial void OnTileServerUrlChanged(string value)
    {
        _settingsService.TileServerUrl = value;
    }

    #endregion
}
