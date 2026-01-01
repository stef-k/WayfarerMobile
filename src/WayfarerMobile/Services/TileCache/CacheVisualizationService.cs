using Mapsui;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Interfaces;
using Map = Mapsui.Map;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Unified facade for cache visualization that combines status monitoring and overlay rendering.
/// Delegates to CacheStatusService for status and CacheOverlayService for map overlay.
/// </summary>
public class CacheVisualizationService : ICacheVisualizationService
{
    private readonly CacheStatusService _statusService;
    private readonly CacheOverlayService _overlayService;

    /// <summary>
    /// Creates a new instance of CacheVisualizationService.
    /// </summary>
    /// <param name="statusService">The cache status service.</param>
    /// <param name="overlayService">The cache overlay service.</param>
    public CacheVisualizationService(
        CacheStatusService statusService,
        CacheOverlayService overlayService)
    {
        _statusService = statusService;
        _overlayService = overlayService;

        // Forward status changed events
        _statusService.StatusChanged += (sender, status) => StatusChanged?.Invoke(this, status);
    }

    #region Status (delegated to CacheStatusService)

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc />
    public string CurrentStatus => _statusService.CurrentStatus;

    /// <inheritdoc />
    public DetailedCacheInfo? LastDetailedInfo => _statusService.LastDetailedInfo;

    /// <inheritdoc />
    public Task ForceRefreshAsync() => _statusService.ForceRefreshAsync();

    /// <inheritdoc />
    public Task<DetailedCacheInfo> GetDetailedCacheInfoAsync() => _statusService.GetDetailedCacheInfoAsync();

    /// <inheritdoc />
    public Task<DetailedCacheInfo> GetDetailedCacheInfoAsync(double latitude, double longitude)
        => _statusService.GetDetailedCacheInfoAsync(latitude, longitude);

    /// <inheritdoc />
    public string FormatStatusMessage(DetailedCacheInfo info) => _statusService.FormatStatusMessage(info);

    #endregion

    #region Overlay (delegated to CacheOverlayService)

    /// <inheritdoc />
    public bool IsOverlayVisible => _overlayService.IsVisible;

    /// <inheritdoc />
    public Task<bool> ToggleOverlayAsync(Map map, double latitude, double longitude)
        => _overlayService.ToggleOverlayAsync(map, latitude, longitude);

    /// <inheritdoc />
    public Task ShowOverlayAsync(Map map, double latitude, double longitude)
        => _overlayService.ShowOverlayAsync(map, latitude, longitude);

    /// <inheritdoc />
    public void HideOverlay(Map map) => _overlayService.HideOverlay(map);

    /// <inheritdoc />
    public Task UpdateOverlayAsync(Map map, double latitude, double longitude)
        => _overlayService.UpdateOverlayAsync(map, latitude, longitude);

    #endregion
}
