using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>Settings and status for the bounded interactive map cache.</summary>
public partial class CacheSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly LiveTileCacheService _liveCache;

    [ObservableProperty]
    private int _maxLiveCacheSizeMB;

    [ObservableProperty]
    private string _liveCacheUsage = "Calculating…";

    public CacheSettingsViewModel(ISettingsService settings, LiveTileCacheService liveCache)
    {
        _settings = settings;
        _liveCache = liveCache;
    }

    public void LoadSettings()
    {
        MaxLiveCacheSizeMB = _settings.MaxLiveCacheSizeMB;
        _ = RefreshUsageAsync();
    }

    partial void OnMaxLiveCacheSizeMBChanged(int value) => _settings.MaxLiveCacheSizeMB = value;

    [RelayCommand]
    private async Task ClearLiveCacheAsync()
    {
        await _liveCache.ClearAllAsync();
        await RefreshUsageAsync();
    }

    [RelayCommand]
    private async Task RefreshUsageAsync()
    {
        var bytes = await _liveCache.GetTotalCacheSizeBytesAsync();
        var tiles = await _liveCache.GetTotalCachedFilesAsync();
        LiveCacheUsage = $"{tiles:N0} tiles · {bytes / 1024d / 1024d:N1} MB";
    }
}
