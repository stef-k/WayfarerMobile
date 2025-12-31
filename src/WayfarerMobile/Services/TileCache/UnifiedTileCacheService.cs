using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Smart tile source coordinator with intelligent priority system.
/// Priority: Live cache → Trip tiles → Direct download with caching.
/// Subscribes to LocationBridge to trigger prefetch on location changes.
/// Includes background retry timer to complete incomplete cache even when app is backgrounded.
/// </summary>
public class UnifiedTileCacheService : IDisposable
{
    #region Fields

    private readonly LiveTileCacheService _liveTileService;
    private readonly TripDownloadService _tripDownloadService;
    private readonly DatabaseService _databaseService;
    private readonly ILocationBridge _locationBridge;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<UnifiedTileCacheService> _logger;

    // Cache the active trip to avoid repeated database queries
    private DownloadedTripEntity? _cachedActiveTrip;
    private LocationData? _lastLocationCheck;
    private DateTime _lastTripCheckTime = DateTime.MinValue;
    private readonly TimeSpan _tripCheckCooldown = TimeSpan.FromSeconds(30);

    // Prefetch debouncing and retry
    private LocationData? _lastPrefetchLocation;
    private DateTime _lastPrefetchTime = DateTime.MinValue;
    private bool _isPrefetching;
    private int _lastPrefetchDownloaded;
    private int _lastPrefetchTotal;
    private readonly object _prefetchLock = new();
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    // Background retry timer - ensures incomplete cache gets completed even when backgrounded
    private Timer? _retryTimer;
    private static readonly TimeSpan RetryTimerInterval = TimeSpan.FromSeconds(60);
    private bool _disposed;

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
    /// <param name="liveTileService">Live tile cache service.</param>
    /// <param name="tripDownloadService">Trip download service.</param>
    /// <param name="databaseService">Database service.</param>
    /// <param name="locationBridge">Location bridge.</param>
    /// <param name="settingsService">Settings service.</param>
    /// <param name="logger">Logger instance.</param>
    public UnifiedTileCacheService(
        LiveTileCacheService liveTileService,
        TripDownloadService tripDownloadService,
        DatabaseService databaseService,
        ILocationBridge locationBridge,
        ISettingsService settingsService,
        ILogger<UnifiedTileCacheService> logger)
    {
        _liveTileService = liveTileService;
        _tripDownloadService = tripDownloadService;
        _databaseService = databaseService;
        _locationBridge = locationBridge;
        _settingsService = settingsService;
        _logger = logger;

        // Subscribe to location updates to trigger prefetch
        _locationBridge.LocationReceived += OnLocationReceived;

        // Subscribe to prefetch events to track completion for retry logic
        _liveTileService.PrefetchProgress += OnPrefetchProgress;
        _liveTileService.PrefetchCompleted += OnPrefetchCompleted;

        // Start background retry timer - runs even when app is backgrounded
        // This ensures incomplete cache gets completed without requiring location events
        _retryTimer = new Timer(OnRetryTimerTick, null, RetryTimerInterval, RetryTimerInterval);

        _logger.LogDebug("Initialized with background retry timer");
    }

    /// <summary>
    /// Tracks prefetch progress for retry logic.
    /// </summary>
    private void OnPrefetchProgress(object? sender, (int Downloaded, int Total) progress)
    {
        lock (_prefetchLock)
        {
            _lastPrefetchDownloaded = progress.Downloaded;
            _lastPrefetchTotal = progress.Total;
        }
    }

    /// <summary>
    /// Handles prefetch completion - updates final counts.
    /// </summary>
    private void OnPrefetchCompleted(object? sender, int downloadedCount)
    {
        lock (_prefetchLock)
        {
            // If no tiles needed downloading (100% cached), mark as complete
            if (_lastPrefetchTotal == 0)
            {
                _lastPrefetchDownloaded = 0;
                _lastPrefetchTotal = 0; // 0/0 means 100% cached
            }
            else
            {
                // Update final downloaded count
                _lastPrefetchDownloaded = downloadedCount;
            }
        }
    }

    /// <summary>
    /// Background timer callback - retries incomplete cache even without location events.
    /// This ensures the cache eventually completes when app is backgrounded.
    /// Uses the most recent location from LocationBridge for accurate prefetch.
    /// Designed to be non-intrusive and yield to mission-critical background services.
    /// </summary>
    private void OnRetryTimerTick(object? state)
    {
        try
        {
            LocationData? locationToRetry = null;

            lock (_prefetchLock)
            {
                // Don't interfere if prefetch is already running
                if (_isPrefetching)
                    return;

                // Only retry if cache is incomplete (has been attempted before)
                if (_lastPrefetchTotal == 0 || _lastPrefetchDownloaded >= _lastPrefetchTotal)
                    return;

                // Respect retry cooldown
                var timeSinceLastPrefetch = DateTime.UtcNow - _lastPrefetchTime;
                if (timeSinceLastPrefetch < RetryInterval)
                    return;

                // Check network connectivity - don't compete for network when offline
                if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                    return;

                // Skip on low battery to preserve power for critical services (location tracking)
                // Only retry when charging or battery > 20%
                try
                {
                    var batteryLevel = Battery.Default.ChargeLevel;
                    var batteryState = Battery.Default.State;
                    if (batteryLevel < 0.2 && batteryState == BatteryState.Discharging)
                    {
                        _logger.LogDebug("Retry skipped - low battery");
                        return;
                    }
                }
                catch
                {
                    // Battery API may not be available, continue anyway
                }

                // Get the most recent location from the bridge (more accurate than cached _lastPrefetchLocation)
                locationToRetry = _locationBridge.LastLocation ?? _lastPrefetchLocation;

                if (locationToRetry == null)
                    return;

                _logger.LogDebug("Background retry - cache incomplete ({Downloaded}/{Total})", _lastPrefetchDownloaded, _lastPrefetchTotal);
            }

            // Trigger prefetch outside the lock on low-priority background thread
            _ = Task.Run(() => PrefetchAroundLocationAsync(locationToRetry));
        }
        catch (Exception ex)
        {
            // Never let timer callback crash - tile caching is non-critical
            _logger.LogDebug(ex, "Retry timer error (ignored)");
        }
    }

    #endregion

    #region Location Handling

    /// <summary>
    /// Handles location updates to trigger prefetch when user moves significantly.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        // Check if we should prefetch (debounce by distance)
        if (!ShouldPrefetch(location))
            return;

        // Fire and forget prefetch on background thread
        _ = Task.Run(() => PrefetchAroundLocationAsync(location));
    }

    /// <summary>
    /// Determines if we should trigger prefetch based on distance moved or incomplete cache.
    /// </summary>
    private bool ShouldPrefetch(LocationData location)
    {
        lock (_prefetchLock)
        {
            // Don't start another prefetch if one is running
            if (_isPrefetching)
                return false;

            // Always prefetch if never done before
            if (_lastPrefetchLocation == null)
                return true;

            // Check if user moved significantly
            var threshold = _settingsService.PrefetchDistanceThresholdMeters;
            var distance = GeoMath.CalculateDistance(
                _lastPrefetchLocation.Latitude, _lastPrefetchLocation.Longitude,
                location.Latitude, location.Longitude);

            if (distance >= threshold)
                return true;

            // If cache wasn't 100% complete, retry after interval (even without movement)
            if (_lastPrefetchTotal > 0 && _lastPrefetchDownloaded < _lastPrefetchTotal)
            {
                var timeSinceLastPrefetch = DateTime.UtcNow - _lastPrefetchTime;
                if (timeSinceLastPrefetch >= RetryInterval)
                {
                    _logger.LogDebug("Retrying prefetch - incomplete ({Downloaded}/{Total})", _lastPrefetchDownloaded, _lastPrefetchTotal);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Checks if cache has room for prefetch (below 90% of max).
    /// </summary>
    private async Task<bool> HasRoomForPrefetchAsync()
    {
        try
        {
            var currentSize = await _liveTileService.GetTotalCacheSizeBytesAsync();
            var maxSizeBytes = (long)_settingsService.MaxLiveCacheSizeMB * 1024 * 1024;
            var usagePercent = (double)currentSize / maxSizeBytes * 100;

            if (usagePercent >= 90)
            {
                _logger.LogDebug("Skipping prefetch - cache at {UsagePercent:F0}% capacity", usagePercent);
                return false;
            }

            return true;
        }
        catch
        {
            return true; // On error, allow prefetch
        }
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
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "I/O error getting tile {Z}/{X}/{Y}", z, x, y);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Network error getting tile {Z}/{X}/{Y}", z, x, y);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error getting tile {Z}/{X}/{Y}", z, x, y);
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
                    _logger.LogDebug("User in trip area: {TripName}", trip.Name);
                    return trip;
                }
            }

            // No active trip found
            if (_cachedActiveTrip != null)
            {
                _logger.LogDebug("User left trip area: {TripName}", _cachedActiveTrip.Name);
            }

            _cachedActiveTrip = null;
            _lastLocationCheck = currentLocation;
            _lastTripCheckTime = DateTime.UtcNow;

            return null;
        }
        catch (SQLiteException ex)
        {
            _logger.LogDebug(ex, "Database error detecting active trip");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error detecting active trip");
            return null;
        }
    }

    /// <summary>
    /// Intelligent prefetch that adapts based on trip context.
    /// Respects storage limits - skips prefetch if cache is near capacity.
    /// </summary>
    /// <param name="currentLocation">Current user location.</param>
    public async Task PrefetchAroundLocationAsync(LocationData currentLocation)
    {
        lock (_prefetchLock)
        {
            if (_isPrefetching)
                return;
            _isPrefetching = true;
        }

        try
        {
            // Check storage capacity before prefetching to avoid churn
            if (!await HasRoomForPrefetchAsync())
            {
                lock (_prefetchLock)
                {
                    _lastPrefetchLocation = currentLocation;
                    _lastPrefetchTime = DateTime.UtcNow;
                    // Mark as "complete" to prevent retry spam when at capacity
                    _lastPrefetchTotal = 0;
                    _lastPrefetchDownloaded = 0;
                }
                return;
            }

            _logger.LogDebug("Starting prefetch at {Latitude:F4}, {Longitude:F4}", currentLocation.Latitude, currentLocation.Longitude);

            await _liveTileService.PrefetchAroundLocationAsync(currentLocation.Latitude, currentLocation.Longitude);

            lock (_prefetchLock)
            {
                _lastPrefetchLocation = currentLocation;
                _lastPrefetchTime = DateTime.UtcNow;
            }

            _logger.LogDebug("Prefetch completed");
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "I/O error during prefetch");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Network error during prefetch");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error during prefetch");
        }
        finally
        {
            lock (_prefetchLock)
            {
                _isPrefetching = false;
            }
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
        catch (SQLiteException ex)
        {
            _logger.LogDebug(ex, "Database error getting statistics");
            return new TileCacheStatistics();
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "I/O error getting statistics");
            return new TileCacheStatistics();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error getting statistics");
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

            _logger.LogDebug("All caches cleared");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error clearing caches");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error clearing caches");
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

    #region IDisposable

    /// <summary>
    /// Disposes resources including the background retry timer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Stop and dispose the retry timer
            _retryTimer?.Dispose();
            _retryTimer = null;

            // Unsubscribe from events
            _locationBridge.LocationReceived -= OnLocationReceived;
            _liveTileService.PrefetchProgress -= OnPrefetchProgress;
            _liveTileService.PrefetchCompleted -= OnPrefetchCompleted;

            _logger.LogDebug("Disposed");
        }

        _disposed = true;
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
