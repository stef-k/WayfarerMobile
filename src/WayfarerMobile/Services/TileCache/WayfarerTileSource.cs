using BruTile;
using BruTile.Predefined;
using Mapsui.Tiling.Provider;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services.TileCache;

/// <summary>Mapsui source for human-visible OSM Standard viewport requests.</summary>
public sealed class WayfarerTileSource : ILocalTileSource
{
    private readonly LiveTileCacheService _liveCache;
    private readonly ILogger<WayfarerTileSource> _logger;

    public WayfarerTileSource(LiveTileCacheService liveCache, ILogger<WayfarerTileSource> logger, string name = "OpenStreetMap")
    {
        _liveCache = liveCache;
        _logger = logger;
        Name = name;
        Schema = new GlobalSphericalMercator();
    }

    public ITileSchema Schema { get; }
    public string Name { get; }
    public Attribution Attribution => new("© OpenStreetMap contributors", "https://www.openstreetmap.org/copyright");

    public async Task<byte[]?> GetTileAsync(TileInfo tileInfo)
    {
        try
        {
            return await _liveCache.GetTileAsync(tileInfo.Index.Level, tileInfo.Index.Col, tileInfo.Index.Row);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting interactive tile z={Zoom} x={X} y={Y}", tileInfo.Index.Level, tileInfo.Index.Col, tileInfo.Index.Row);
            return null;
        }
    }
}
