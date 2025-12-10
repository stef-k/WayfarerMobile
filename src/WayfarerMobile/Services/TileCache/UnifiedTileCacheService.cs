using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Smart tile source coordinator with intelligent priority system.
/// Priority: Live cache → Trip tiles → Direct download with caching.
/// </summary>
public class UnifiedTileCacheService
{
    #region Fields

    private readonly LiveTileCacheService _liveTileService;
    private readonly TripDownloadService _tripDownloadService;
    private readonly DatabaseService _databaseService;

    // Cache the active trip to avoid repeated database queries
    private DownloadedTripEntity? _cachedActiveTrip;
    private LocationData? _lastLocationCheck;
    private DateTime _lastTripCheckTime = DateTime.MinValue;
    private readonly TimeSpan _tripCheckCooldown = TimeSpan.FromSeconds(30);

    // Tile statistics for logging
    private readonly object _statsLock = new();
    private int _liveCacheHits;
    private int _tripCacheHits;
    private int _onlineDownloads;
    private int _cacheMisses;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of UnifiedTileCacheService.
    /// </summary>
    public UnifiedTileCacheService(
        LiveTileCacheService liveTileService,
        TripDownloadService tripDownloadService,
        DatabaseService databaseService)
    {
        _liveTileService = liveTileService;
        _tripDownloadService = tripDownloadService;
        _databaseService = databaseService;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets a tile using intelligent priority system with complete fallback chain.
    /// Priority: Live cache → Trip tiles → Direct download with caching.
    /// </summary>
    /// <param name="z">Zoom level.</param>
    /// <param name="x">Tile X coordinate.</param>
    /// <param name="y">Tile Y coordinate.</param>
    /// <param name="currentLocation">Current user location for context.</param>
    /// <returns>Tile file if available, null if unavailable.</returns>
    public async Task<FileInfo?> GetTileAsync(int z, int x, int y, LocationData? currentLocation)
    {
        try
        {
            // Priority 1: Live cache (most current, optimized for current location)
            var liveTile = await _liveTileService.GetCachedTileAsync(z, x, y);
            if (liveTile != null && liveTile.Exists)
            {
                RecordHit("live");
                return liveTile;
            }

            // Priority 2: Trip tiles (offline fallback)
            if (currentLocation != null)
            {
                var activeTrip = await GetActiveTripAsync(currentLocation);
                if (activeTrip != null)
                {
                    var tripTilePath = _tripDownloadService.GetCachedTilePath(activeTrip.Id, z, x, y);
                    if (tripTilePath != null)
                    {
                        RecordHit("trip");
                        return new FileInfo(tripTilePath);
                    }
                }
            }

            // Priority 3: Direct download with caching (online fallback)
            if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
            {
                var downloadedTile = await _liveTileService.GetOrDownloadTileAsync(z, x, y);
                if (downloadedTile != null && downloadedTile.Exists)
                {
                    RecordHit("download");
                    return downloadedTile;
                }
            }

            // No tile available from any source
            RecordHit("miss");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] Error getting tile {z}/{x}/{y}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Detects if the current location is within a downloaded trip area.
    /// Uses caching to avoid repeated database queries.
    /// </summary>
    /// <param name="currentLocation">Current user location.</param>
    /// <returns>Active trip if user is within a trip boundary, null otherwise.</returns>
    public async Task<DownloadedTripEntity?> GetActiveTripAsync(LocationData currentLocation)
    {
        try
        {
            // Use cached result if location hasn't changed much and check was recent
            if (_lastLocationCheck != null && _cachedActiveTrip != null)
            {
                var distance = GeoMath.CalculateDistance(
                    _lastLocationCheck.Latitude, _lastLocationCheck.Longitude,
                    currentLocation.Latitude, currentLocation.Longitude);

                if (distance < 1000 && DateTime.UtcNow - _lastTripCheckTime < _tripCheckCooldown)
                {
                    return _cachedActiveTrip;
                }
            }

            // Check all downloaded trips for boundary overlap
            var downloadedTrips = await _tripDownloadService.GetDownloadedTripsAsync();

            foreach (var trip in downloadedTrips)
            {
                if (trip.Status == TripDownloadStatus.Complete && IsLocationInTripBoundary(currentLocation, trip))
                {
                    _cachedActiveTrip = trip;
                    _lastLocationCheck = currentLocation;
                    _lastTripCheckTime = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] User in trip area: {trip.Name}");
                    return trip;
                }
            }

            // No active trip found
            if (_cachedActiveTrip != null)
            {
                System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] User left trip area: {_cachedActiveTrip.Name}");
            }

            _cachedActiveTrip = null;
            _lastLocationCheck = currentLocation;
            _lastTripCheckTime = DateTime.UtcNow;

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] Error detecting active trip: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Intelligent prefetch that adapts based on trip context.
    /// </summary>
    /// <param name="currentLocation">Current user location.</param>
    public async Task PrefetchAroundLocationAsync(LocationData currentLocation)
    {
        try
        {
            await _liveTileService.PrefetchAroundLocationAsync(currentLocation.Latitude, currentLocation.Longitude);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] Error during prefetch: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets unified cache statistics.
    /// </summary>
    public async Task<TileCacheStatistics> GetStatisticsAsync()
    {
        try
        {
            var liveTileCount = await _liveTileService.GetTotalCachedFilesAsync();
            var liveTileSize = await _liveTileService.GetTotalCacheSizeBytesAsync();
            var tripCacheSize = await _tripDownloadService.GetTotalCacheSizeAsync();

            return new TileCacheStatistics
            {
                LiveTileCount = liveTileCount,
                LiveCacheSizeBytes = liveTileSize,
                TripCacheSizeBytes = tripCacheSize,
                TotalSizeBytes = liveTileSize + tripCacheSize,
                ActiveTripName = _cachedActiveTrip?.Name
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] Error getting statistics: {ex.Message}");
            return new TileCacheStatistics();
        }
    }

    /// <summary>
    /// Clears all caches (both live and trip).
    /// </summary>
    public async Task ClearAllCachesAsync()
    {
        try
        {
            await _liveTileService.ClearAllAsync();
            // Note: Trip tiles are cleared via TripDownloadService.DeleteTripAsync()

            _cachedActiveTrip = null;
            _lastLocationCheck = null;
            _lastTripCheckTime = DateTime.MinValue;

            System.Diagnostics.Debug.WriteLine("[UnifiedTileCacheService] All caches cleared");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UnifiedTileCacheService] Error clearing caches: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets cache hit statistics summary.
    /// </summary>
    public string GetHitStatsSummary()
    {
        lock (_statsLock)
        {
            var total = _liveCacheHits + _tripCacheHits + _onlineDownloads + _cacheMisses;
            if (total == 0) return "No tile requests";

            var hitRate = (double)(_liveCacheHits + _tripCacheHits) / total * 100;
            return $"Tiles: {total} (Live:{_liveCacheHits} Trip:{_tripCacheHits} DL:{_onlineDownloads} Miss:{_cacheMisses}) Hit:{hitRate:F0}%";
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Checks if a location falls within a trip's boundary.
    /// </summary>
    private static bool IsLocationInTripBoundary(LocationData location, DownloadedTripEntity trip)
    {
        return location.Latitude >= trip.BoundingBoxSouth &&
               location.Latitude <= trip.BoundingBoxNorth &&
               location.Longitude >= trip.BoundingBoxWest &&
               location.Longitude <= trip.BoundingBoxEast;
    }

    /// <summary>
    /// Records a cache hit for statistics.
    /// </summary>
    private void RecordHit(string type)
    {
        lock (_statsLock)
        {
            switch (type)
            {
                case "live": _liveCacheHits++; break;
                case "trip": _tripCacheHits++; break;
                case "download": _onlineDownloads++; break;
                case "miss": _cacheMisses++; break;
            }
        }
    }

    #endregion
}

/// <summary>
/// Tile cache statistics.
/// </summary>
public class TileCacheStatistics
{
    /// <summary>
    /// Gets or sets the number of live cached tiles.
    /// </summary>
    public int LiveTileCount { get; set; }

    /// <summary>
    /// Gets or sets the live cache size in bytes.
    /// </summary>
    public long LiveCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the trip cache size in bytes.
    /// </summary>
    public long TripCacheSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the total cache size in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Gets or sets the active trip name.
    /// </summary>
    public string? ActiveTripName { get; set; }

    /// <summary>
    /// Gets the total size formatted as a string.
    /// </summary>
    public string FormattedTotalSize => FormatSize(TotalSizeBytes);

    /// <summary>
    /// Gets the live cache size formatted as a string.
    /// </summary>
    public string FormattedLiveSize => FormatSize(LiveCacheSizeBytes);

    /// <summary>
    /// Gets the trip cache size formatted as a string.
    /// </summary>
    public string FormattedTripSize => FormatSize(TripCacheSizeBytes);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
