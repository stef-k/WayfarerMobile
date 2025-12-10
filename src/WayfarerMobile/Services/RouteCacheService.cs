using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Algorithms;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for caching the last fetched OSRM route.
/// Stores a single route in preferences that survives app restart.
/// </summary>
/// <remarks>
/// Cache validity rules:
/// - Same destination place ID
/// - Current location within 50m of cached origin
/// - Route not older than configured max age (default 5 minutes)
/// </remarks>
public class RouteCacheService
{
    private readonly ILogger<RouteCacheService> _logger;
    private const string CacheKey = "cached_osrm_route";

    /// <summary>
    /// Maximum distance from cached origin to consider cache valid (meters).
    /// </summary>
    private const double MaxOriginDistanceMeters = 50;

    /// <summary>
    /// Maximum age of cached route before it's considered stale.
    /// </summary>
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromMinutes(5);

    private CachedRoute? _memoryCache;

    /// <summary>
    /// Creates a new instance of RouteCacheService.
    /// </summary>
    public RouteCacheService(ILogger<RouteCacheService> logger)
    {
        _logger = logger;
        LoadFromPreferences();
    }

    /// <summary>
    /// Attempts to get a valid cached route for the given parameters.
    /// </summary>
    /// <param name="currentLat">Current latitude.</param>
    /// <param name="currentLon">Current longitude.</param>
    /// <param name="destinationPlaceId">Target place ID.</param>
    /// <returns>The cached route if valid, null otherwise.</returns>
    public CachedRoute? GetValidRoute(double currentLat, double currentLon, string destinationPlaceId)
    {
        if (_memoryCache == null)
        {
            return null;
        }

        // Check destination matches
        if (_memoryCache.DestinationPlaceId != destinationPlaceId)
        {
            _logger.LogDebug("Cache miss: different destination");
            return null;
        }

        // Check origin proximity
        var distanceFromOrigin = GeoMath.CalculateDistance(
            currentLat, currentLon,
            _memoryCache.OriginLatitude, _memoryCache.OriginLongitude);

        if (distanceFromOrigin > MaxOriginDistanceMeters)
        {
            _logger.LogDebug("Cache miss: origin too far ({Distance:F0}m)", distanceFromOrigin);
            return null;
        }

        // Check age
        var age = DateTime.UtcNow - _memoryCache.FetchedAtUtc;
        if (age > MaxCacheAge)
        {
            _logger.LogDebug("Cache miss: too old ({Age:F1} minutes)", age.TotalMinutes);
            return null;
        }

        _logger.LogDebug("Cache hit: route to {Destination}", destinationPlaceId);
        return _memoryCache;
    }

    /// <summary>
    /// Saves a route to the cache.
    /// </summary>
    /// <param name="route">The route to cache.</param>
    public void SaveRoute(CachedRoute route)
    {
        _memoryCache = route;
        SaveToPreferences();
        _logger.LogDebug("Cached route to {Destination}", route.DestinationPlaceId);
    }

    /// <summary>
    /// Clears the cached route.
    /// </summary>
    public void Clear()
    {
        _memoryCache = null;
        Preferences.Remove(CacheKey);
        _logger.LogDebug("Route cache cleared");
    }

    /// <summary>
    /// Loads the cached route from preferences.
    /// </summary>
    private void LoadFromPreferences()
    {
        try
        {
            var json = Preferences.Get(CacheKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                _memoryCache = JsonSerializer.Deserialize<CachedRoute>(json);
                _logger.LogDebug("Loaded cached route from preferences");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cached route from preferences");
            _memoryCache = null;
        }
    }

    /// <summary>
    /// Saves the cached route to preferences.
    /// </summary>
    private void SaveToPreferences()
    {
        try
        {
            if (_memoryCache != null)
            {
                var json = JsonSerializer.Serialize(_memoryCache);
                Preferences.Set(CacheKey, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save cached route to preferences");
        }
    }
}

/// <summary>
/// Represents a cached route from OSRM or other routing service.
/// </summary>
public class CachedRoute
{
    /// <summary>
    /// Gets or sets the destination place ID.
    /// </summary>
    public string DestinationPlaceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination place name.
    /// </summary>
    public string DestinationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the origin latitude when route was fetched.
    /// </summary>
    public double OriginLatitude { get; set; }

    /// <summary>
    /// Gets or sets the origin longitude when route was fetched.
    /// </summary>
    public double OriginLongitude { get; set; }

    /// <summary>
    /// Gets or sets the encoded polyline geometry.
    /// </summary>
    public string Geometry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total distance in meters.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Gets or sets the estimated duration in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the routing service source (e.g., "osrm").
    /// </summary>
    public string Source { get; set; } = "osrm";

    /// <summary>
    /// Gets or sets when the route was fetched (UTC).
    /// </summary>
    public DateTime FetchedAtUtc { get; set; }
}
