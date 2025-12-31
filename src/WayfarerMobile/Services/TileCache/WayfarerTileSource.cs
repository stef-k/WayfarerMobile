using BruTile;
using BruTile.Predefined;
using Mapsui.Tiling.Provider;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Custom Mapsui tile source that integrates with Wayfarer's offline caching system.
/// Provides tiles from cache when available, falls back to online download.
/// Implements ILocalTileSource for async tile fetching (Mapsui 5.0+).
/// </summary>
public class WayfarerTileSource : ILocalTileSource
{
    #region Fields

    private readonly UnifiedTileCacheService _unifiedTileService;
    private readonly ILocationBridge _locationBridge;
    private readonly ILogger<WayfarerTileSource> _logger;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of WayfarerTileSource.
    /// </summary>
    /// <param name="unifiedTileService">The unified tile cache service.</param>
    /// <param name="locationBridge">The location bridge for current location context.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="name">Display name for the tile source.</param>
    public WayfarerTileSource(
        UnifiedTileCacheService unifiedTileService,
        ILocationBridge locationBridge,
        ILogger<WayfarerTileSource> logger,
        string name = "WayfarerCached")
    {
        _unifiedTileService = unifiedTileService ?? throw new ArgumentNullException(nameof(unifiedTileService));
        _locationBridge = locationBridge ?? throw new ArgumentNullException(nameof(locationBridge));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Name = name;

        // Use GlobalSphericalMercator schema (same as OSM)
        Schema = new GlobalSphericalMercator();
    }

    #endregion

    #region ITileSource Implementation

    /// <summary>
    /// Gets the tile schema defining tile coordinates.
    /// </summary>
    public ITileSchema Schema { get; }

    /// <summary>
    /// Gets the name of this tile source.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the attribution for the tiles.
    /// </summary>
    public Attribution Attribution => new("© OpenStreetMap contributors");

    /// <summary>
    /// Gets a tile using the offline-first caching strategy.
    /// Priority: Live cache → Trip cache → Online download.
    /// </summary>
    /// <param name="tileInfo">Tile information including zoom, x, y coordinates.</param>
    /// <returns>Tile data as byte array, or null if unavailable.</returns>
    public async Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        try
        {
            int zoom = tileInfo.Index.Level;
            int x = tileInfo.Index.Col;
            int y = tileInfo.Index.Row;

            // Get current location for context-aware caching
            LocationData? currentLocation = _locationBridge.LastLocation;

            // Get tile from unified cache service
            var tileFile = await _unifiedTileService.GetTileAsync(zoom, x, y, currentLocation);

            if (tileFile != null && tileFile.Exists)
            {
                return await File.ReadAllBytesAsync(tileFile.FullName);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting tile z={Zoom} x={X} y={Y}", tileInfo.Index.Level, tileInfo.Index.Col, tileInfo.Index.Row);
            return null;
        }
    }

    #endregion
}
