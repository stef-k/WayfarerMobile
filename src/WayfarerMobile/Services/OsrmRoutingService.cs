using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Helpers;

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
            // steps=true for turn-by-turn instructions
            var url = $"{BaseUrl}/route/v1/{profile}/{fromLon},{fromLat};{toLon},{toLat}" +
                      "?overview=full&geometries=polyline&steps=true";

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

            // Parse step instructions from legs
            var steps = new List<OsrmStepResult>();
            if (route.Legs != null)
            {
                foreach (var leg in route.Legs)
                {
                    if (leg.Steps == null) continue;
                    foreach (var step in leg.Steps)
                    {
                        steps.Add(new OsrmStepResult
                        {
                            Instruction = step.Maneuver?.Instruction ?? GenerateInstruction(step.Maneuver?.Type, step.Maneuver?.Modifier, step.Name),
                            DistanceMeters = step.Distance,
                            DurationSeconds = step.Duration,
                            ManeuverType = step.Maneuver?.Type ?? "unknown",
                            Modifier = step.Maneuver?.Modifier,
                            StreetName = step.Name,
                            Latitude = step.Maneuver?.Location?.Count > 1 ? step.Maneuver.Location[1] : 0,
                            Longitude = step.Maneuver?.Location?.Count > 0 ? step.Maneuver.Location[0] : 0
                        });
                    }
                }
            }

            _logger.LogInformation(
                "OSRM route fetched: {Distance:F1}km, {Duration:F0}min, {Steps} steps",
                route.Distance / 1000,
                route.Duration / 60,
                steps.Count);

            return new OsrmRouteResult
            {
                Geometry = route.Geometry,
                DistanceMeters = route.Distance,
                DurationSeconds = route.Duration,
                Steps = steps,
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
            _logger.LogNetworkWarningIfOnline("OSRM request failed (network error): {Message}", ex.Message);
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

    /// <summary>
    /// Generates a human-readable instruction from maneuver type and modifier.
    /// </summary>
    private static string GenerateInstruction(string? type, string? modifier, string? streetName)
    {
        var street = string.IsNullOrEmpty(streetName) ? "" : $" onto {streetName}";

        return type switch
        {
            "depart" => $"Head {modifier ?? "forward"}{street}",
            "arrive" => "You have arrived",
            "turn" => modifier switch
            {
                "left" => $"Turn left{street}",
                "right" => $"Turn right{street}",
                "slight left" => $"Bear left{street}",
                "slight right" => $"Bear right{street}",
                "sharp left" => $"Sharp left{street}",
                "sharp right" => $"Sharp right{street}",
                "uturn" => "Make a U-turn",
                _ => $"Turn{street}"
            },
            "continue" => $"Continue{street}",
            "merge" => $"Merge{street}",
            "fork" => modifier switch
            {
                "left" => $"Keep left{street}",
                "right" => $"Keep right{street}",
                _ => $"Continue{street}"
            },
            "roundabout" => $"Enter roundabout{street}",
            "rotary" => $"Enter rotary{street}",
            "exit roundabout" or "exit rotary" => $"Exit{street}",
            "end of road" => modifier switch
            {
                "left" => $"Turn left{street}",
                "right" => $"Turn right{street}",
                _ => $"Continue{street}"
            },
            _ => $"Continue{street}"
        };
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
    /// Gets or sets the turn-by-turn step instructions.
    /// </summary>
    public List<OsrmStepResult> Steps { get; set; } = new();

    /// <summary>
    /// Gets or sets the source identifier.
    /// </summary>
    public string Source { get; set; } = "osrm";
}

/// <summary>
/// A single step/instruction in the route.
/// </summary>
public class OsrmStepResult
{
    /// <summary>
    /// Human-readable instruction text.
    /// </summary>
    public string Instruction { get; set; } = string.Empty;

    /// <summary>
    /// Distance for this step in meters.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Duration for this step in seconds.
    /// </summary>
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Maneuver type (turn, depart, arrive, etc.).
    /// </summary>
    public string ManeuverType { get; set; } = string.Empty;

    /// <summary>
    /// Maneuver modifier (left, right, slight left, etc.).
    /// </summary>
    public string? Modifier { get; set; }

    /// <summary>
    /// Street name for this step.
    /// </summary>
    public string? StreetName { get; set; }

    /// <summary>
    /// Latitude where this maneuver occurs.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitude where this maneuver occurs.
    /// </summary>
    public double Longitude { get; set; }
}

#region OSRM API Response Models

/// <summary>
/// OSRM API response.
/// </summary>
internal class OsrmResponse
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("routes")]
    public List<OsrmRoute>? Routes { get; set; }
}

/// <summary>
/// OSRM route in response.
/// </summary>
internal class OsrmRoute
{
    [JsonPropertyName("geometry")]
    public string Geometry { get; set; } = string.Empty;

    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("legs")]
    public List<OsrmLeg>? Legs { get; set; }
}

/// <summary>
/// OSRM leg (segment between waypoints).
/// </summary>
internal class OsrmLeg
{
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("steps")]
    public List<OsrmStep>? Steps { get; set; }
}

/// <summary>
/// OSRM step (single instruction/maneuver).
/// </summary>
internal class OsrmStep
{
    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("maneuver")]
    public OsrmManeuver? Maneuver { get; set; }
}

/// <summary>
/// OSRM maneuver details.
/// </summary>
internal class OsrmManeuver
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("modifier")]
    public string? Modifier { get; set; }

    [JsonPropertyName("instruction")]
    public string? Instruction { get; set; }

    /// <summary>
    /// Location as [longitude, latitude].
    /// </summary>
    [JsonPropertyName("location")]
    public List<double>? Location { get; set; }

    [JsonPropertyName("bearing_before")]
    public double BearingBefore { get; set; }

    [JsonPropertyName("bearing_after")]
    public double BearingAfter { get; set; }
}

#endregion
