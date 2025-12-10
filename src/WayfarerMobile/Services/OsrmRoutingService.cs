using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for fetching routes from OSRM (Open Source Routing Machine) public API.
/// Uses the demo server at router.project-osrm.org for route calculations.
/// </summary>
/// <remarks>
/// Rate limit: 1 request per second (demo server policy).
/// No API key required.
/// Profiles: foot, car, bike.
/// </remarks>
public class OsrmRoutingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OsrmRoutingService> _logger;

    private const string BaseUrl = "https://router.project-osrm.org";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1.1); // Slightly over 1s to be safe

    /// <summary>
    /// Creates a new instance of OsrmRoutingService.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="logger">The logger.</param>
    public OsrmRoutingService(HttpClient httpClient, ILogger<OsrmRoutingService> logger)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = RequestTimeout;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0");
        _logger = logger;
    }

    /// <summary>
    /// Fetches a route between two points.
    /// </summary>
    /// <param name="fromLat">Origin latitude.</param>
    /// <param name="fromLon">Origin longitude.</param>
    /// <param name="toLat">Destination latitude.</param>
    /// <param name="toLon">Destination longitude.</param>
    /// <param name="profile">Routing profile (foot, car, bike). Default is foot.</param>
    /// <returns>The route result or null if failed.</returns>
    public async Task<OsrmRouteResult?> GetRouteAsync(
        double fromLat, double fromLon,
        double toLat, double toLon,
        string profile = "foot")
    {
        try
        {
            // Enforce rate limit
            await EnforceRateLimitAsync();

            // Build URL: /route/v1/{profile}/{lon},{lat};{lon},{lat}
            // Note: OSRM uses lon,lat order (not lat,lon)
            var url = $"{BaseUrl}/route/v1/{profile}/{fromLon},{fromLat};{toLon},{toLat}" +
                      "?overview=full&geometries=polyline&steps=false";

            _logger.LogDebug("Fetching OSRM route: {Url}", url);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM request failed with status {StatusCode}", response.StatusCode);
                return null;
            }

            var osrmResponse = await response.Content.ReadFromJsonAsync<OsrmResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (osrmResponse?.Code != "Ok" || osrmResponse.Routes == null || osrmResponse.Routes.Count == 0)
            {
                _logger.LogWarning("OSRM returned no routes. Code: {Code}", osrmResponse?.Code);
                return null;
            }

            var route = osrmResponse.Routes[0];

            _logger.LogInformation(
                "OSRM route fetched: {Distance:F1}km, {Duration:F0}min",
                route.Distance / 1000,
                route.Duration / 60);

            return new OsrmRouteResult
            {
                Geometry = route.Geometry,
                DistanceMeters = route.Distance,
                DurationSeconds = route.Duration,
                Source = "osrm"
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("OSRM request timed out");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "OSRM request failed (network error)");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse OSRM response");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching OSRM route");
            return null;
        }
    }

    /// <summary>
    /// Enforces the rate limit by waiting if necessary.
    /// </summary>
    private static async Task EnforceRateLimitAsync()
    {
        var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
        if (timeSinceLastRequest < MinRequestInterval)
        {
            var delay = MinRequestInterval - timeSinceLastRequest;
            await Task.Delay(delay);
        }
        _lastRequestTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Result from OSRM route request.
/// </summary>
public class OsrmRouteResult
{
    /// <summary>
    /// Gets or sets the encoded polyline geometry.
    /// </summary>
    public string Geometry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total distance in meters.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Gets or sets the total duration in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Gets or sets the source identifier.
    /// </summary>
    public string Source { get; set; } = "osrm";
}

#region OSRM API Response Models

/// <summary>
/// OSRM API response.
/// </summary>
internal class OsrmResponse
{
    /// <summary>
    /// Gets or sets the response code ("Ok" on success).
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// Gets or sets the list of routes.
    /// </summary>
    [JsonPropertyName("routes")]
    public List<OsrmRoute>? Routes { get; set; }
}

/// <summary>
/// OSRM route in response.
/// </summary>
internal class OsrmRoute
{
    /// <summary>
    /// Gets or sets the encoded polyline geometry.
    /// </summary>
    [JsonPropertyName("geometry")]
    public string Geometry { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the distance in meters.
    /// </summary>
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    /// <summary>
    /// Gets or sets the duration in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }
}

#endregion
