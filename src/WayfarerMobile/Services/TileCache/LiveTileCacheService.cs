using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Maps;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services.TileCache;

/// <summary>Bounded cache for tiles requested by the interactive OSM map renderer.</summary>
public sealed class LiveTileCacheService : ILiveTileStore
{
    private static readonly Uri TileBaseUri = new("https://tile.openstreetmap.org/");
    private readonly ILiveTileCacheRepository _repository;
    private readonly ISettingsService _settings;
    private readonly ILogger<LiveTileCacheService> _logger;
    private readonly OsmLiveTileCacheClient _client;
    private readonly string _cacheDirectory;

    public LiveTileCacheService(ILiveTileCacheRepository repository, ISettingsService settings,
        IHttpClientFactory httpClientFactory, ILogger<LiveTileCacheService> logger)
    {
        _repository = repository;
        _settings = settings;
        _logger = logger;
        _cacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "live", OsmLiveTileCacheClient.ProviderId);
        Directory.CreateDirectory(_cacheDirectory);
        _client = new OsmLiveTileCacheClient(httpClientFactory.CreateClient("Tiles"), this);
    }

    public Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken = default)
    {
        var key = new TileCacheKey(OsmLiveTileCacheClient.ProviderId, zoom, x, y);
        return _client.GetTileAsync(key, new Uri(TileBaseUri, $"{zoom}/{x}/{y}.png"), cancellationToken);
    }

    public Task<int> GetTotalCachedFilesAsync() => _repository.GetLiveTileCountAsync();
    public Task<long> GetTotalCacheSizeBytesAsync() => _repository.GetLiveCacheSizeAsync();

    public async Task ClearAllAsync()
    {
        if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, recursive: true);
        Directory.CreateDirectory(_cacheDirectory);
        await _repository.ClearLiveTilesAsync();
    }

    public async Task EvictLruTilesAsync()
    {
        var maximumBytes = (long)_settings.MaxLiveCacheSizeMB * 1024 * 1024;
        var currentBytes = await _repository.GetLiveCacheSizeAsync();
        if (currentBytes <= maximumBytes) return;

        foreach (var tile in await _repository.GetOldestLiveTilesAsync(100))
        {
            try
            {
                if (File.Exists(tile.FilePath)) File.Delete(tile.FilePath);
                await _repository.DeleteLiveTileAsync(tile.Id);
                currentBytes -= tile.FileSizeBytes;
                if (currentBytes <= maximumBytes * 0.8) break;
            }
            catch (Exception ex) when (ex is IOException or SQLiteException)
            {
                _logger.LogDebug(ex, "Could not evict live tile {TileId}", tile.Id);
            }
        }
    }

    async Task<CachedTile?> ILiveTileStore.GetAsync(TileCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await _repository.GetLiveTileAsync(GetId(key))
            ?? await AdoptCanonicalLegacyEntryAsync(key, cancellationToken);
        if (entity is null || !File.Exists(entity.FilePath)) return null;

        var bytes = await File.ReadAllBytesAsync(entity.FilePath, cancellationToken);
        await _repository.UpdateLiveTileAccessAsync(entity.Id);
        var fallbackExpiry = new DateTimeOffset(DateTime.SpecifyKind(entity.CachedAt, DateTimeKind.Utc)).AddDays(7);
        return new CachedTile(key, bytes,
            entity.FreshUntilUtc == default ? fallbackExpiry : ToDateTimeOffset(entity.FreshUntilUtc)!.Value,
            entity.ETag, ToDateTimeOffset(entity.LastModifiedUtc), entity.CacheControl, ToDateTimeOffset(entity.ExpiresUtc));
    }

    async Task ILiveTileStore.WriteAtomicallyAsync(CachedTile tile, CancellationToken cancellationToken)
    {
        var path = GetPath(tile.Key);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        var backupPath = path + $".{Guid.NewGuid():N}.bak";
        var hadPreviousFile = File.Exists(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, tile.Bytes, cancellationToken);
            if (hadPreviousFile) File.Copy(path, backupPath);
            File.Move(temporaryPath, path, overwrite: true);
            try
            {
                await _repository.SaveLiveTileAsync(new LiveTileEntity
                {
                    Id = GetId(tile.Key), ProviderId = tile.Key.ProviderId, Zoom = tile.Key.Zoom, X = tile.Key.X, Y = tile.Key.Y,
                    TileSource = "osm", FilePath = path, FileSizeBytes = tile.Bytes.Length, CachedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow, FreshUntilUtc = tile.FreshUntil.UtcDateTime, ETag = tile.ETag,
                    LastModifiedUtc = tile.LastModified?.UtcDateTime, CacheControl = tile.CacheControl, ExpiresUtc = tile.Expires?.UtcDateTime
                });
            }
            catch
            {
                if (hadPreviousFile) File.Move(backupPath, path, overwrite: true);
                else if (File.Exists(path)) File.Delete(path);
                throw;
            }
            _ = EvictLruTilesAsync();
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
    }

    async Task ILiveTileStore.RemoveAsync(TileCacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _repository.DeleteLiveTileAsync(GetId(key));
        var path = GetPath(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private async Task<LiveTileEntity?> AdoptCanonicalLegacyEntryAsync(TileCacheKey key, CancellationToken cancellationToken)
    {
        if (key.ProviderId != OsmLiveTileCacheClient.ProviderId) return null;
        var legacyId = $"{key.Zoom}/{key.X}/{key.Y}";
        var legacy = await _repository.GetLiveTileAsync(legacyId);
        if (legacy is null || !string.Equals(legacy.TileSource, "osm", StringComparison.OrdinalIgnoreCase) || !File.Exists(legacy.FilePath)) return null;

        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(legacy.FilePath, path, overwrite: true);
        legacy.Id = GetId(key);
        legacy.ProviderId = key.ProviderId;
        legacy.FilePath = path;
        await _repository.SaveLiveTileAsync(legacy);
        await _repository.DeleteLiveTileAsync(legacyId);
        return legacy;
    }

    private string GetPath(TileCacheKey key) => Path.Combine(_cacheDirectory, key.Zoom.ToString(), key.X.ToString(), $"{key.Y}.png");
    private static string GetId(TileCacheKey key) => $"{key.ProviderId}/{key.Zoom}/{key.X}/{key.Y}";
    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
