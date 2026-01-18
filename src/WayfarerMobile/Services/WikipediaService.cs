using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Implementation of Wikipedia search service using MediaWiki Geosearch API.
/// </summary>
public class WikipediaService : IWikipediaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WikipediaService> _logger;

    private const string WikipediaApiBase = "https://en.wikipedia.org/w/api.php";
    private const int InitialSearchRadiusMeters = 100;
    private const int FallbackSearchRadiusMeters = 1000;

    /// <summary>
    /// Creates a new instance of WikipediaService.
    /// </summary>
    public WikipediaService(ILogger<WikipediaService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0 (Location tracking app)");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc/>
    public async Task<WikipediaSearchResult?> SearchNearbyAsync(double latitude, double longitude)
    {
        try
        {
            // First try with smaller radius (100m)
            var result = await SearchWithRadiusAsync(latitude, longitude, InitialSearchRadiusMeters);

            // If no result, try with larger radius (1km)
            if (result == null)
            {
                _logger.LogDebug("No Wikipedia article found within {Radius}m, trying {FallbackRadius}m",
                    InitialSearchRadiusMeters, FallbackSearchRadiusMeters);
                result = await SearchWithRadiusAsync(latitude, longitude, FallbackSearchRadiusMeters);
            }

            if (result != null)
            {
                _logger.LogInformation("Found Wikipedia article '{Title}' at {Distance:F0}m from coordinates",
                    result.Title, result.DistanceMeters);
            }
            else
            {
                _logger.LogDebug("No Wikipedia article found near {Lat}, {Lon}", latitude, longitude);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Wikipedia for coordinates {Lat}, {Lon}", latitude, longitude);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> OpenNearbyArticleAsync(double latitude, double longitude)
    {
        var result = await SearchNearbyAsync(latitude, longitude);

        if (result == null)
        {
            return false;
        }

        try
        {
            await Launcher.OpenAsync(new Uri(result.Url));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Wikipedia URL: {Url}", result.Url);
            return false;
        }
    }

    private async Task<WikipediaSearchResult?> SearchWithRadiusAsync(double latitude, double longitude, int radiusMeters)
    {
        var apiUrl = $"{WikipediaApiBase}?action=query&list=geosearch" +
                     $"&gscoord={latitude}|{longitude}" +
                     $"&gsradius={radiusMeters}" +
                     $"&gslimit=1" +
                     $"&format=json";

        try
        {
            var response = await _httpClient.GetStringAsync(apiUrl);
            var apiResponse = JsonSerializer.Deserialize<WikipediaApiResponse>(response);

            var firstResult = apiResponse?.Query?.Geosearch?.FirstOrDefault();
            if (firstResult == null)
            {
                return null;
            }

            return new WikipediaSearchResult
            {
                Title = firstResult.Title,
                PageId = firstResult.PageId,
                DistanceMeters = firstResult.Dist
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("HTTP error searching Wikipedia API: {Message}", ex.Message);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error parsing Wikipedia API response");
            return null;
        }
    }

    #region API Response Models

    private class WikipediaApiResponse
    {
        [JsonPropertyName("query")]
        public WikipediaQuery? Query { get; set; }
    }

    private class WikipediaQuery
    {
        [JsonPropertyName("geosearch")]
        public List<WikipediaGeosearchResult>? Geosearch { get; set; }
    }

    private class WikipediaGeosearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("pageid")]
        public int PageId { get; set; }

        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lon")]
        public double Lon { get; set; }

        [JsonPropertyName("dist")]
        public double Dist { get; set; }
    }

    #endregion
}
