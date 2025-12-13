using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services.TileCache;

/// <summary>
/// Service for checking cache status. Subscribes to LocationBridge and updates
/// cache status when location changes. Does NOT run on startup.
/// Uses user-configured prefetch radius from settings.
/// </summary>
public class CacheStatusService
{
    private readonly ILocationBridge _locationBridge;
    private readonly ISettingsService _settingsService;
    private readonly LiveTileCacheService _liveTileCacheService;
    private readonly string _liveCacheDirectory;
    private readonly string _tripCacheDirectory;

    private string _currentStatus = "yellow";
    private DateTime _lastCheckTime = DateTime.MinValue;
    private LocationData? _lastCheckLocation;
    private DetailedCacheInfo? _lastDetailedInfo;
    private readonly object _statusLock = new();

    // Zoom levels ordered by importance (matches LiveTileCacheService prefetch order)
    // Quick check uses crucial 3: current view (15), one up (14), one down (16)
    private static readonly int[] QuickCheckZoomLevels = { 15, 14, 16 };

    // Full check uses all 8 levels for parity with prefetch
    private static readonly int[] FullCheckZoomLevels = { 15, 14, 16, 13, 12, 11, 10, 17 };

    // Debounce: only check every 30 seconds or 100m movement
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(30);
    private const double MinDistanceMeters = 100;

    /// <summary>
    /// Event raised when cache status changes.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Gets the current cache status ("green", "yellow", or "red").
    /// </summary>
    public string CurrentStatus => _currentStatus;

    /// <summary>
    /// Gets the last detailed cache info (may be null if not yet checked).
    /// </summary>
    public DetailedCacheInfo? LastDetailedInfo => _lastDetailedInfo;

    /// <summary>
    /// Creates a new instance of CacheStatusService.
    /// </summary>
    public CacheStatusService(
        ILocationBridge locationBridge,
        ISettingsService settingsService,
        LiveTileCacheService liveTileCacheService)
    {
        _locationBridge = locationBridge;
        _settingsService = settingsService;
        _liveTileCacheService = liveTileCacheService;
        _liveCacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "live");
        _tripCacheDirectory = Path.Combine(FileSystem.CacheDirectory, "tiles", "trips");

        // Subscribe to location updates - this is when we check cache status
        _locationBridge.LocationReceived += OnLocationReceived;

        // Subscribe to prefetch events - refresh status as tiles are downloaded
        _liveTileCacheService.PrefetchProgress += OnPrefetchProgress;
        _liveTileCacheService.PrefetchCompleted += OnPrefetchCompleted;

        System.Diagnostics.Debug.WriteLine("[CacheStatusService] Initialized and subscribed to LocationBridge and LiveTileCacheService");
    }

    /// <summary>
    /// Handles location updates from the bridge.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        // Debounce: check if we should update
        if (!ShouldCheckCache(location))
            return;

        // Run cache check on background thread (fire and forget)
        _ = Task.Run(() => CheckCacheStatusAsync(location.Latitude, location.Longitude));
    }

    /// <summary>
    /// Handles prefetch progress - refresh cache status periodically during download.
    /// </summary>
    private void OnPrefetchProgress(object? sender, (int Downloaded, int Total) progress)
    {
        System.Diagnostics.Debug.WriteLine($"[CacheStatusService] Prefetch progress: {progress.Downloaded}/{progress.Total}");
        _ = ForceRefreshAsync();
    }

    /// <summary>
    /// Handles prefetch completion - refresh cache status immediately.
    /// </summary>
    private void OnPrefetchCompleted(object? sender, int downloadedCount)
    {
        System.Diagnostics.Debug.WriteLine($"[CacheStatusService] Prefetch completed with {downloadedCount} tiles, refreshing status");
        _ = ForceRefreshAsync();
    }

    /// <summary>
    /// Forces an immediate cache status refresh. Call this after tile downloads complete.
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        LocationData? location;
        lock (_statusLock)
        {
            location = _lastCheckLocation ?? _locationBridge.LastLocation;
            if (location == null)
                return;

            // Reset debounce to allow immediate check
            _lastCheckTime = DateTime.MinValue;
        }

        await Task.Run(() => CheckCacheStatusAsync(location.Latitude, location.Longitude));
    }

    /// <summary>
    /// Determines if we should check cache based on time/distance.
    /// </summary>
    private bool ShouldCheckCache(LocationData location)
    {
        var now = DateTime.UtcNow;

        lock (_statusLock)
        {
            // Always check if never checked
            if (_lastCheckLocation == null)
                return true;

            // Check time interval
            if (now - _lastCheckTime < MinCheckInterval)
                return false;

            // Check distance moved
            var distance = CalculateDistance(
                _lastCheckLocation.Latitude, _lastCheckLocation.Longitude,
                location.Latitude, location.Longitude);

            return distance >= MinDistanceMeters;
        }
    }

    /// <summary>
    /// Checks cache status for a location (runs on background thread).
    /// </summary>
    private async Task CheckCacheStatusAsync(double latitude, double longitude)
    {
        try
        {
            int total = 0;
            int cached = 0;
            // Use user-configured prefetch radius from settings
            int halfGrid = _settingsService.LiveCachePrefetchRadius;

            // Cache trip directories once outside the loops for performance
            string[]? tripDirs = null;
            if (Directory.Exists(_tripCacheDirectory))
            {
                tripDirs = Directory.GetDirectories(_tripCacheDirectory);
            }

            foreach (var zoom in QuickCheckZoomLevels)
            {
                var (centerX, centerY) = LatLonToTile(latitude, longitude, zoom);

                for (int dx = -halfGrid; dx <= halfGrid; dx++)
                {
                    for (int dy = -halfGrid; dy <= halfGrid; dy++)
                    {
                        int x = centerX + dx;
                        int y = centerY + dy;

                        if (x < 0 || y < 0 || y >= (1 << zoom))
                            continue;

                        total++;

                        // Check live cache
                        var livePath = Path.Combine(_liveCacheDirectory, zoom.ToString(), x.ToString(), $"{y}.png");
                        if (File.Exists(livePath))
                        {
                            cached++;
                            continue;
                        }

                        // Check trip caches (using cached directory list)
                        if (tripDirs != null)
                        {
                            foreach (var tripDir in tripDirs)
                            {
                                var tripPath = Path.Combine(tripDir, zoom.ToString(), x.ToString(), $"{y}.png");
                                if (File.Exists(tripPath))
                                {
                                    cached++;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            double percentage = total > 0 ? (double)cached / total : 0;
            var newStatus = percentage >= 0.9 ? "green" : percentage >= 0.3 ? "yellow" : "red";

            // Update state with thread safety
            lock (_statusLock)
            {
                _lastCheckTime = DateTime.UtcNow;
                _lastCheckLocation = new LocationData { Latitude = latitude, Longitude = longitude };

                if (newStatus != _currentStatus)
                {
                    _currentStatus = newStatus;
                    MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke(this, newStatus));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheStatusService] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets detailed cache information for current location.
    /// Call this when user taps the cache indicator.
    /// Always does a fresh scan and updates the quick status indicator.
    /// </summary>
    public async Task<DetailedCacheInfo> GetDetailedCacheInfoAsync()
    {
        var location = _lastCheckLocation ?? _locationBridge.LastLocation;
        if (location == null)
        {
            return new DetailedCacheInfo { Status = "No location available" };
        }

        var info = await GetDetailedCacheInfoAsync(location.Latitude, location.Longitude);

        // Also update the quick status indicator based on fresh data (thread-safe)
        var newStatus = info.CoveragePercentage >= 0.9 ? "green" :
                        info.CoveragePercentage >= 0.3 ? "yellow" : "red";

        lock (_statusLock)
        {
            if (newStatus != _currentStatus)
            {
                _currentStatus = newStatus;
                MainThread.BeginInvokeOnMainThread(() => StatusChanged?.Invoke(this, newStatus));
            }
        }

        return info;
    }

    /// <summary>
    /// Gets detailed cache information for a specific location.
    /// </summary>
    public async Task<DetailedCacheInfo> GetDetailedCacheInfoAsync(double latitude, double longitude)
    {
        return await Task.Run(() =>
        {
            try
            {
                int total = 0;
                int cached = 0;
                int liveCached = 0;
                int tripCached = 0;
                long totalSize = 0;
                // Use user-configured prefetch radius from settings
                int halfGrid = _settingsService.LiveCachePrefetchRadius;

                // Cache trip directories once outside the loops for performance
                string[]? tripDirs = null;
                if (Directory.Exists(_tripCacheDirectory))
                {
                    tripDirs = Directory.GetDirectories(_tripCacheDirectory);
                }

                var zoomDetails = new List<ZoomLevelCoverage>();

                foreach (var zoom in FullCheckZoomLevels)
                {
                    var (centerX, centerY) = LatLonToTile(latitude, longitude, zoom);
                    int zoomTotal = 0;
                    int zoomCached = 0;
                    int zoomLive = 0;
                    int zoomTrip = 0;

                    for (int dx = -halfGrid; dx <= halfGrid; dx++)
                    {
                        for (int dy = -halfGrid; dy <= halfGrid; dy++)
                        {
                            int x = centerX + dx;
                            int y = centerY + dy;

                            if (x < 0 || y < 0 || y >= (1 << zoom))
                                continue;

                            zoomTotal++;
                            total++;

                            // Check live cache
                            var livePath = Path.Combine(_liveCacheDirectory, zoom.ToString(), x.ToString(), $"{y}.png");
                            if (File.Exists(livePath))
                            {
                                zoomCached++; cached++; zoomLive++; liveCached++;
                                try { totalSize += new FileInfo(livePath).Length; } catch { }
                                continue;
                            }

                            // Check trip caches (using cached directory list)
                            if (tripDirs != null)
                            {
                                foreach (var tripDir in tripDirs)
                                {
                                    var tripPath = Path.Combine(tripDir, zoom.ToString(), x.ToString(), $"{y}.png");
                                    if (File.Exists(tripPath))
                                    {
                                        zoomCached++; cached++; zoomTrip++; tripCached++;
                                        try { totalSize += new FileInfo(tripPath).Length; } catch { }
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    zoomDetails.Add(new ZoomLevelCoverage
                    {
                        ZoomLevel = zoom,
                        TotalTiles = zoomTotal,
                        CachedTiles = zoomCached,
                        LiveCachedTiles = zoomLive,
                        TripCachedTiles = zoomTrip,
                        CoveragePercentage = zoomTotal > 0 ? (double)zoomCached / zoomTotal : 0,
                        PrimaryCacheSource = zoomLive > zoomTrip ? "Live" : (zoomTrip > 0 ? "Trip" : "None")
                    });
                }

                double percentage = total > 0 ? (double)cached / total : 0;

                _lastDetailedInfo = new DetailedCacheInfo
                {
                    Status = percentage >= 0.9 ? "Excellent" : percentage >= 0.7 ? "Good" : percentage >= 0.4 ? "Partial" : percentage > 0 ? "Poor" : "None",
                    CachedTiles = cached,
                    TotalTiles = total,
                    LiveCachedTiles = liveCached,
                    TripCachedTiles = tripCached,
                    LocalSizeBytes = totalSize,
                    CoveragePercentage = percentage,
                    ZoomLevelDetails = zoomDetails,
                    LastUpdated = DateTime.UtcNow
                };

                return _lastDetailedInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CacheStatusService] Error: {ex.Message}");
                return new DetailedCacheInfo { Status = "Error" };
            }
        });
    }

    /// <summary>
    /// Formats cache status for display in alert.
    /// </summary>
    public string FormatStatusMessage(DetailedCacheInfo info)
    {
        return $"Status: {info.Status} ({info.CoveragePercentage:P0})\n\n" +
               $"Tiles: {info.CachedTiles} / {info.TotalTiles}\n" +
               $"  Live: {info.LiveCachedTiles}\n" +
               $"  Trip: {info.TripCachedTiles}\n\n" +
               $"Size: {info.LocalSizeBytes / 1024.0 / 1024.0:F1} MB";
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (Math.Max(0, Math.Min((int)n - 1, x)), Math.Max(0, Math.Min((int)n - 1, y)));
    }
}

/// <summary>
/// Detailed cache information for a location.
/// </summary>
public class DetailedCacheInfo
{
    public string Status { get; set; } = "";
    public int CachedTiles { get; set; }
    public int TotalTiles { get; set; }
    public int LiveCachedTiles { get; set; }
    public int TripCachedTiles { get; set; }
    public long LocalSizeBytes { get; set; }
    public double CoveragePercentage { get; set; }
    public DateTime? LastUpdated { get; set; }
    public List<ZoomLevelCoverage> ZoomLevelDetails { get; set; } = new();
}

/// <summary>
/// Cache coverage for a specific zoom level.
/// </summary>
public class ZoomLevelCoverage
{
    public int ZoomLevel { get; set; }
    public int TotalTiles { get; set; }
    public int CachedTiles { get; set; }
    public int LiveCachedTiles { get; set; }
    public int TripCachedTiles { get; set; }
    public double CoveragePercentage { get; set; }
    public string PrimaryCacheSource { get; set; } = "None";
}
