using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// HTTP client for communicating with the Wayfarer backend API.
/// </summary>
public class ApiClient : IApiClient, IVisitApiClient
{
    private readonly ISettingsService _settings;
    private readonly ILogger<ApiClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Use relaxed encoding to prevent HTML characters (<, >) from being escaped to \u003C, \u003E
        // This is needed for notes HTML content to be stored correctly on the server
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new UtcDateTimeConverter() }
    };

    /// <summary>
    /// HTTP status codes that are considered transient and should be retried.
    /// </summary>
    private static readonly HashSet<HttpStatusCode> TransientStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    /// <summary>
    /// Creates a new instance of ApiClient.
    /// </summary>
    /// <param name="settings">Settings service for configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    public ApiClient(ISettingsService settings, ILogger<ApiClient> logger, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _settings = settings;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        // Configure Polly retry pipeline for transient failures
        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
                    .HandleResult(response => TransientStatusCodes.Contains(response.StatusCode)),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry {Attempt} after {Delay}ms due to {Reason}",
                        args.AttemptNumber,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? $"HTTP {args.Outcome.Result?.StatusCode}");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    /// <summary>
    /// Gets the HttpClient instance for API calls.
    /// </summary>
    private HttpClient HttpClientInstance => _httpClientFactory.CreateClient("WayfarerApi");

    /// <inheritdoc/>
    public bool IsConfigured => _settings.IsConfigured;

    /// <inheritdoc/>
    public async Task<ApiResult> LogLocationAsync(
        LocationLogRequest location,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await SendLocationAsync("/api/location/log-location", location, idempotencyKey, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ApiResult> CheckInAsync(
        LocationLogRequest location,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await SendLocationAsync("/api/location/check-in", location, idempotencyKey, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Test connection by fetching settings
        var settings = await GetSettingsAsync(cancellationToken);
        return settings != null;
    }

    /// <inheritdoc/>
    public async Task<ServerSettings?> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot get settings - API is not configured");
            return null;
        }

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, "/api/settings"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var settings = await response.Content.ReadFromJsonAsync<ServerSettings>(JsonOptions, cancellationToken);
                _logger.LogInformation("Fetched server settings: TimeThreshold={Time}min, DistanceThreshold={Distance}m",
                    settings?.LocationTimeThresholdMinutes, settings?.LocationDistanceThresholdMeters);
                return settings;
            }

            _logger.LogWarning("Failed to get settings: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching server settings");
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out fetching server settings");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching server settings");
            return null;
        }
    }

    /// <summary>
    /// Gets the list of trips from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of trip summaries.</returns>
    public async Task<List<TripSummary>> GetTripsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot get trips - API is not configured");
            return new List<TripSummary>();
        }

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, "/api/trips"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var trips = await response.Content.ReadFromJsonAsync<List<TripSummary>>(JsonOptions, cancellationToken);
                return trips ?? new List<TripSummary>();
            }

            _logger.LogWarning("Failed to get trips: {StatusCode}", response.StatusCode);
            return new List<TripSummary>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error getting trips");
            return new List<TripSummary>();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out getting trips");
            return new List<TripSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting trips");
            return new List<TripSummary>();
        }
    }

    /// <summary>
    /// Gets trip details by ID.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trip details or null if not found.</returns>
    public async Task<TripDetails?> GetTripDetailsAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot get trip details - API is not configured");
            return null;
        }

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, $"/api/trips/{tripId}"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TripDetails>(JsonOptions, cancellationToken);
            }

            _logger.LogWarning("Failed to get trip details: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error getting trip details for {TripId}", tripId);
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out getting trip details for {TripId}", tripId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting trip details for {TripId}", tripId);
            return null;
        }
    }

    /// <summary>
    /// Gets trip boundary for tile download calculation.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trip boundary response or null if not found.</returns>
    public async Task<TripBoundaryResponse?> GetTripBoundaryAsync(Guid tripId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot get trip boundary - API is not configured");
            return null;
        }

        try
        {
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, $"/api/trips/{tripId}/boundary"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TripBoundaryResponse>(JsonOptions, cancellationToken);
            }

            _logger.LogWarning("Failed to get trip boundary: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error getting trip boundary for {TripId}", tripId);
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out getting trip boundary for {TripId}", tripId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting trip boundary for {TripId}", tripId);
            return null;
        }
    }

    #region Place CRUD

    /// <summary>
    /// Creates a new place in a trip.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="request">The place creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Place response or null on failure.</returns>
    public async Task<PlaceResponse?> CreatePlaceAsync(
        Guid tripId,
        PlaceCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Post, $"/api/trips/{tripId}/places");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var place = await response.Content.ReadFromJsonAsync<TripPlace>(JsonOptions, cancellationToken);
                return new PlaceResponse { Success = true, Id = place?.Id ?? Guid.Empty, Place = place };
            }

            _logger.LogWarning("Failed to create place: {StatusCode}", response.StatusCode);
            return new PlaceResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error creating place in trip {TripId}", tripId);
            return new PlaceResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out creating place in trip {TripId}", tripId);
            return new PlaceResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating place in trip {TripId}", tripId);
            return new PlaceResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Updates an existing place.
    /// </summary>
    /// <param name="placeId">The place ID.</param>
    /// <param name="request">The place update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Place response or null on failure.</returns>
    public async Task<PlaceResponse?> UpdatePlaceAsync(
        Guid placeId,
        PlaceUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/trips/places/{placeId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var place = await response.Content.ReadFromJsonAsync<TripPlace>(JsonOptions, cancellationToken);
                return new PlaceResponse { Success = true, Id = placeId, Place = place };
            }

            _logger.LogWarning("Failed to update place {PlaceId}: {StatusCode}", placeId, response.StatusCode);
            return new PlaceResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating place {PlaceId}", placeId);
            return new PlaceResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating place {PlaceId}", placeId);
            return new PlaceResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating place {PlaceId}", placeId);
            return new PlaceResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Deletes a place.
    /// </summary>
    /// <param name="placeId">The place ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> DeletePlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        try
        {
            var request = CreateRequest(HttpMethod.Delete, $"/api/trips/places/{placeId}");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted place {PlaceId}", placeId);
                return true;
            }

            _logger.LogWarning("Failed to delete place {PlaceId}: {StatusCode}", placeId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error deleting place {PlaceId}", placeId);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out deleting place {PlaceId}", placeId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting place {PlaceId}", placeId);
            return false;
        }
    }

    #endregion

    #region Visit Operations

    /// <summary>
    /// Gets recent visits from the server (for background polling when SSE is unavailable).
    /// </summary>
    /// <param name="sinceSeconds">Number of seconds to look back for visits.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent visit events, or empty list on failure.</returns>
    public async Task<List<SseVisitStartedEvent>> GetRecentVisitsAsync(
        int sinceSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Cannot get recent visits - API is not configured");
            return new List<SseVisitStartedEvent>();
        }

        try
        {
            var endpoint = $"/api/mobile/visits/recent?since={sinceSeconds}";
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, endpoint),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RecentVisitsResponse>(JsonOptions, cancellationToken);
                _logger.LogDebug("Fetched {Count} recent visits", result?.Visits?.Count ?? 0);
                return result?.Visits ?? new List<SseVisitStartedEvent>();
            }

            _logger.LogDebug("Failed to get recent visits: {StatusCode}", response.StatusCode);
            return new List<SseVisitStartedEvent>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Network error fetching recent visits");
            return new List<SseVisitStartedEvent>();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogDebug(ex, "Request timed out fetching recent visits");
            return new List<SseVisitStartedEvent>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unexpected error fetching recent visits");
            return new List<SseVisitStartedEvent>();
        }
    }

    /// <summary>
    /// Response model for recent visits API.
    /// </summary>
    private class RecentVisitsResponse
    {
        public bool Success { get; set; }
        public List<SseVisitStartedEvent>? Visits { get; set; }
    }

    #endregion

    #region Timeline

    /// <summary>
    /// Updates a timeline location entry.
    /// </summary>
    /// <param name="locationId">The location ID.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response containing update result.</returns>
    public async Task<TimelineUpdateResponse?> UpdateTimelineLocationAsync(
        int locationId,
        TimelineLocationUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        try
        {
            // Server API uses /api/location/{id} for timeline updates
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/location/{locationId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new TimelineUpdateResponse { Success = true, LocationId = locationId };
            }

            _logger.LogWarning("Failed to update timeline location {LocationId}: {StatusCode}", locationId, response.StatusCode);
            return new TimelineUpdateResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating timeline location {LocationId}", locationId);
            return new TimelineUpdateResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating timeline location {LocationId}", locationId);
            return new TimelineUpdateResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating timeline location {LocationId}", locationId);
            return new TimelineUpdateResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Deletes a timeline location.
    /// </summary>
    /// <param name="locationId">The location ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> DeleteTimelineLocationAsync(int locationId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        try
        {
            // Server API uses /api/location/{id} for timeline deletes
            var request = CreateRequest(HttpMethod.Delete, $"/api/location/{locationId}");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted timeline location {LocationId}", locationId);
                return true;
            }

            _logger.LogWarning("Failed to delete timeline location {LocationId}: {StatusCode}", locationId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error deleting timeline location {LocationId}", locationId);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out deleting timeline location {LocationId}", locationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting timeline location {LocationId}", locationId);
            return false;
        }
    }

    #endregion

    #region Region CRUD

    /// <summary>
    /// Creates a new region in a trip.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="request">The region creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Region response or null on failure.</returns>
    public async Task<RegionResponse?> CreateRegionAsync(
        Guid tripId,
        RegionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Post, $"/api/trips/{tripId}/regions");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var region = await response.Content.ReadFromJsonAsync<TripRegion>(JsonOptions, cancellationToken);
                return new RegionResponse { Success = true, Id = region?.Id ?? Guid.Empty, Region = region };
            }

            _logger.LogWarning("Failed to create region: {StatusCode}", response.StatusCode);
            return new RegionResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error creating region in trip {TripId}", tripId);
            return new RegionResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out creating region in trip {TripId}", tripId);
            return new RegionResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating region in trip {TripId}", tripId);
            return new RegionResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Updates an existing region.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <param name="request">The region update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Region response or null on failure.</returns>
    public async Task<RegionResponse?> UpdateRegionAsync(
        Guid regionId,
        RegionUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/trips/regions/{regionId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);
            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var region = await response.Content.ReadFromJsonAsync<TripRegion>(JsonOptions, cancellationToken);
                return new RegionResponse { Success = true, Id = regionId, Region = region };
            }

            _logger.LogWarning("Failed to update region {RegionId}: {StatusCode}", regionId, response.StatusCode);
            return new RegionResponse { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating region {RegionId}", regionId);
            return new RegionResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating region {RegionId}", regionId);
            return new RegionResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating region {RegionId}", regionId);
            return new RegionResponse { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Deletes a region.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    public async Task<bool> DeleteRegionAsync(Guid regionId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        try
        {
            var request = CreateRequest(HttpMethod.Delete, $"/api/trips/regions/{regionId}");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Deleted region {RegionId}", regionId);
                return true;
            }

            _logger.LogWarning("Failed to delete region {RegionId}: {StatusCode}", regionId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error deleting region {RegionId}", regionId);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out deleting region {RegionId}", regionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting region {RegionId}", regionId);
            return false;
        }
    }

    #endregion

    #region Public Trips

    /// <summary>
    /// Gets paginated list of public trips with optional search.
    /// </summary>
    /// <param name="searchQuery">Optional search query.</param>
    /// <param name="sort">Sort option (updated, newest, name, places).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated public trips response.</returns>
    public async Task<PublicTripsResponse?> GetPublicTripsAsync(
        string? searchQuery = null,
        string sort = "updated",
        int page = 1,
        int pageSize = 24,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build query string
            var queryParams = new List<string>
            {
                $"sort={Uri.EscapeDataString(sort)}",
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                queryParams.Add($"q={Uri.EscapeDataString(searchQuery)}");
            }

            var endpoint = $"/api/trips/public?{string.Join("&", queryParams)}";
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, endpoint),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PublicTripsResponse>(JsonOptions, cancellationToken);
                _logger.LogInformation("Fetched {Count} public trips (page {Page}/{TotalPages})",
                    result?.Trips.Count ?? 0, result?.Page, result?.TotalPages);
                return result;
            }

            _logger.LogWarning("Failed to get public trips: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching public trips");
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out fetching public trips");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching public trips");
            return null;
        }
    }

    /// <summary>
    /// Clones a public trip to the current user's account.
    /// </summary>
    /// <param name="tripId">The trip ID to clone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Clone response with new trip ID.</returns>
    public async Task<CloneTripResponse?> CloneTripAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new CloneTripResponse { Success = false, Error = "API not configured" };
        }

        try
        {
            var request = CreateRequest(HttpMethod.Post, $"/api/trips/{tripId}/clone");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CloneTripResponse>(JsonOptions, cancellationToken);
                if (result != null)
                {
                    result.Success = true;
                    _logger.LogInformation("Cloned trip {TripId} to new trip {NewTripId}",
                        tripId, result.NewTripId);
                }
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to clone trip {TripId}: {StatusCode} - {Error}",
                tripId, response.StatusCode, errorBody);

            return new CloneTripResponse
            {
                Success = false,
                Error = $"HTTP {(int)response.StatusCode}: {errorBody}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error cloning trip {TripId}", tripId);
            return new CloneTripResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out cloning trip {TripId}", tripId);
            return new CloneTripResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error cloning trip {TripId}", tripId);
            return new CloneTripResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc/>
    public async Task<TripUpdateResponse?> UpdateTripAsync(
        Guid tripId,
        TripUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new TripUpdateResponse { Success = false, Error = "API not configured" };
        }

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/trips/{tripId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TripUpdateResponse>(JsonOptions, cancellationToken);
                if (result != null)
                {
                    result.Success = true;
                    _logger.LogInformation("Updated trip {TripId}", tripId);
                }
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to update trip {TripId}: {StatusCode} - {Error}",
                tripId, response.StatusCode, errorBody);

            return new TripUpdateResponse
            {
                Success = false,
                Error = $"HTTP {(int)response.StatusCode}: {errorBody}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating trip {TripId}", tripId);
            return new TripUpdateResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating trip {TripId}", tripId);
            return new TripUpdateResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating trip {TripId}", tripId);
            return new TripUpdateResponse { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Segment Operations

    /// <inheritdoc/>
    public async Task<SegmentUpdateResponse?> UpdateSegmentNotesAsync(
        Guid segmentId,
        SegmentNotesUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new SegmentUpdateResponse { Success = false, Error = "API not configured" };
        }

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/trips/segments/{segmentId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SegmentUpdateResponse>(JsonOptions, cancellationToken);
                if (result != null)
                {
                    result.Success = true;
                    _logger.LogInformation("Updated segment notes for {SegmentId}", segmentId);
                }
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to update segment {SegmentId}: {StatusCode} - {Error}",
                segmentId, response.StatusCode, errorBody);

            return new SegmentUpdateResponse
            {
                Success = false,
                Error = $"HTTP {(int)response.StatusCode}: {errorBody}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating segment {SegmentId}", segmentId);
            return new SegmentUpdateResponse { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating segment {SegmentId}", segmentId);
            return new SegmentUpdateResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating segment {SegmentId}", segmentId);
            return new SegmentUpdateResponse { Success = false, Error = ex.Message };
        }
    }

    #endregion

    #region Area Operations

    /// <inheritdoc/>
    public async Task<AreaUpdateResponse?> UpdateAreaNotesAsync(
        Guid areaId,
        AreaNotesUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AreaUpdateResponse { Success = false };
        }

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Put, $"/api/trips/areas/{areaId}");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AreaUpdateResponse>(JsonOptions, cancellationToken);
                if (result != null)
                {
                    result.Success = true;
                    _logger.LogInformation("Updated area notes for {AreaId}", areaId);
                }
                return result;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to update area {AreaId}: {StatusCode} - {Error}",
                areaId, response.StatusCode, errorBody);

            return new AreaUpdateResponse { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error updating area {AreaId}", areaId);
            return new AreaUpdateResponse { Success = false };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out updating area {AreaId}", areaId);
            return new AreaUpdateResponse { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating area {AreaId}", areaId);
            return new AreaUpdateResponse { Success = false };
        }
    }

    #endregion

    /// <summary>
    /// Gets the underlying HttpClient for direct tile downloads.
    /// </summary>
    public HttpClient HttpClient => HttpClientInstance;

    /// <inheritdoc/>
    public async Task<TimelineResponse?> GetTimelineLocationsAsync(
        string dateType,
        int year,
        int? month = null,
        int? day = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot get timeline locations - API is not configured");
            return null;
        }

        try
        {
            // Build query string
            var queryParams = new List<string>
            {
                $"dateType={Uri.EscapeDataString(dateType)}",
                $"year={year}"
            };

            if (month.HasValue)
                queryParams.Add($"month={month.Value}");
            if (day.HasValue)
                queryParams.Add($"day={day.Value}");

            var endpoint = $"/api/location/chronological?{string.Join("&", queryParams)}";
            var response = await ExecuteWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, endpoint),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<TimelineResponse>(JsonOptions, cancellationToken);
                _logger.LogInformation("Fetched {Count} timeline locations for {DateType} {Year}/{Month}/{Day}",
                    result?.Data?.Count ?? 0, dateType, year, month, day);
                return result;
            }

            _logger.LogWarning("Failed to get timeline locations: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching timeline locations");
            return null;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out fetching timeline locations");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching timeline locations");
            return null;
        }
    }

    /// <summary>
    /// Sends a location to the specified endpoint.
    /// </summary>
    private async Task<ApiResult> SendLocationAsync(
        string endpoint,
        LocationLogRequest location,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Cannot send location - API is not configured");
            return ApiResult.Fail("API not configured");
        }

        try
        {
            var payload = new LocationPayload
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Timestamp = location.Timestamp,
                Accuracy = location.Accuracy,
                Altitude = location.Altitude,
                Speed = location.Speed,
                LocationType = location.Provider,
                ActivityTypeId = location.ActivityTypeId,
                Notes = location.Notes
            };

            var request = CreateRequest(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            }
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            _logger.LogDebug("Sending location to {Endpoint}: ({Lat}, {Lon})",
                endpoint, location.Latitude, location.Longitude);

            var response = await HttpClientInstance.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = ParseSuccessResponse(responseBody);
                _logger.LogDebug("Location sent successfully: {Message}", result.Message);
                return result;
            }

            _logger.LogWarning("Failed to send location: {StatusCode} - {Body}",
                response.StatusCode, responseBody);

            return ApiResult.Fail(
                $"Server returned {(int)response.StatusCode}: {responseBody}",
                (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error sending location");
            return ApiResult.Fail($"Network error: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Request timed out sending location");
            return ApiResult.Fail("Request timed out", isTransient: true);
        }
        catch (TaskCanceledException ex)
        {
            // TaskCanceledException can occur from:
            // 1. User/app cancellation (CancellationToken triggered)
            // 2. Connection pool timeout (HttpClient internal timeout)
            // Both are transient and should trigger retry
            _logger.LogWarning(ex, "Request canceled sending location");
            return ApiResult.Fail("Request canceled", isTransient: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending location");
            return ApiResult.Fail($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates an HTTP request with proper authorization headers.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var baseUrl = _settings.ServerUrl?.TrimEnd('/') ?? "";
        var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");

        if (!string.IsNullOrEmpty(_settings.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// Executes an HTTP request with retry policy for transient failures.
    /// </summary>
    /// <param name="requestFactory">Factory to create the request (needed for retries as requests can't be reused).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTP response.</returns>
    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        return await _retryPipeline.ExecuteAsync(
            async token =>
            {
                var request = requestFactory();
                return await HttpClientInstance.SendAsync(request, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Parses a successful response to determine if location was logged or skipped.
    /// </summary>
    private static ApiResult ParseSuccessResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var message = root.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : "Success";

            // Extract locationId if present (returned by server when location is stored)
            int? locationId = root.TryGetProperty("locationId", out var idProp)
                && idProp.ValueKind == JsonValueKind.Number
                    ? idProp.GetInt32()
                    : null;

            // Check if location was skipped due to thresholds
            if (message?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true)
            {
                return ApiResult.SkippedResult(message);
            }

            var result = ApiResult.Ok(message);
            result.LocationId = locationId;
            return result;
        }
        catch (JsonException)
        {
            // Non-JSON response or malformed JSON - treat as success with default message
            return ApiResult.Ok("Location logged");
        }
    }

    /// <summary>
    /// Payload structure matching the server's GpsLoggerLocationDto.
    /// </summary>
    private class LocationPayload
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }
        public double? Accuracy { get; set; }
        public double? Altitude { get; set; }
        public double? Speed { get; set; }
        public string? LocationType { get; set; }
        public string? Notes { get; set; }
        public int? ActivityTypeId { get; set; }
    }
}
