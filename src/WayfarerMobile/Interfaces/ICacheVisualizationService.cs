using Mapsui;
using WayfarerMobile.Core.Interfaces;
using Map = Mapsui.Map;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Unified service for cache status monitoring and visualization.
/// Combines cache health status (indicator) with overlay rendering (circles on map).
/// </summary>
public interface ICacheVisualizationService
{
    #region Status (from CacheStatusService)

    /// <summary>
    /// Event raised when cache status changes ("green", "yellow", or "red").
    /// </summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Gets the current cache status ("green", "yellow", or "red").
    /// </summary>
    string CurrentStatus { get; }

    /// <summary>
    /// Gets the last detailed cache info (may be null if not yet checked).
    /// </summary>
    DetailedCacheInfo? LastDetailedInfo { get; }

    /// <summary>
    /// Forces an immediate cache status refresh.
    /// Call this after tile downloads complete.
    /// </summary>
    Task ForceRefreshAsync();

    /// <summary>
    /// Gets detailed cache information for current location.
    /// Call this when user taps the cache indicator.
    /// Always does a fresh scan and updates the quick status indicator.
    /// </summary>
    /// <returns>Detailed cache information.</returns>
    Task<DetailedCacheInfo> GetDetailedCacheInfoAsync();

    /// <summary>
    /// Gets detailed cache information for a specific location.
    /// </summary>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    /// <returns>Detailed cache information.</returns>
    Task<DetailedCacheInfo> GetDetailedCacheInfoAsync(double latitude, double longitude);

    /// <summary>
    /// Formats cache status for display in alert.
    /// </summary>
    /// <param name="info">The detailed cache info to format.</param>
    /// <returns>Formatted status message.</returns>
    string FormatStatusMessage(DetailedCacheInfo info);

    #endregion

    #region Overlay (from CacheOverlayService)

    /// <summary>
    /// Gets whether the cache overlay is currently visible on the map.
    /// </summary>
    bool IsOverlayVisible { get; }

    /// <summary>
    /// Toggles the cache overlay visibility on the map.
    /// </summary>
    /// <param name="map">The Mapsui map instance.</param>
    /// <param name="latitude">Current latitude.</param>
    /// <param name="longitude">Current longitude.</param>
    /// <returns>True if overlay is now visible, false if hidden.</returns>
    Task<bool> ToggleOverlayAsync(Map map, double latitude, double longitude);

    /// <summary>
    /// Shows the cache overlay on the map at the specified location.
    /// </summary>
    /// <param name="map">The Mapsui map instance.</param>
    /// <param name="latitude">Location latitude.</param>
    /// <param name="longitude">Location longitude.</param>
    Task ShowOverlayAsync(Map map, double latitude, double longitude);

    /// <summary>
    /// Hides the cache overlay from the map.
    /// </summary>
    /// <param name="map">The Mapsui map instance.</param>
    void HideOverlay(Map map);

    /// <summary>
    /// Updates the cache overlay for a new location (if visible).
    /// </summary>
    /// <param name="map">The Mapsui map instance.</param>
    /// <param name="latitude">New location latitude.</param>
    /// <param name="longitude">New location longitude.</param>
    Task UpdateOverlayAsync(Map map, double latitude, double longitude);

    #endregion
}
