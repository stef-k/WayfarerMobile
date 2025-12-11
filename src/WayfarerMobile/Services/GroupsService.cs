using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for interacting with the groups API.
/// </summary>
public class GroupsService : IGroupsService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<GroupsService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new instance of GroupsService.
    /// </summary>
    /// <param name="settings">Settings service for configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    public GroupsService(ISettingsService settings, ILogger<GroupsService> logger, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _settings = settings;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Gets the HttpClient instance for API calls.
    /// </summary>
    private HttpClient HttpClientInstance => _httpClientFactory.CreateClient("WayfarerApi");

    /// <inheritdoc/>
    public async Task<List<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch groups - API not configured");
            return new List<GroupSummary>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Get, "/api/mobile/groups?scope=all");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch groups: {StatusCode}", response.StatusCode);
                return new List<GroupSummary>();
            }

            var groups = await response.Content.ReadFromJsonAsync<List<GroupSummary>>(JsonOptions, cancellationToken);
            _logger.LogDebug("Fetched {Count} groups", groups?.Count ?? 0);

            return groups ?? new List<GroupSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching groups");
            return new List<GroupSummary>();
        }
    }

    /// <inheritdoc/>
    public async Task<List<GroupMember>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch group members - API not configured");
            return new List<GroupMember>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Get, $"/api/mobile/groups/{groupId}/members");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch group members: {StatusCode}", response.StatusCode);
                return new List<GroupMember>();
            }

            var members = await response.Content.ReadFromJsonAsync<List<GroupMember>>(JsonOptions, cancellationToken);
            _logger.LogDebug("Fetched {Count} members for group {GroupId}", members?.Count ?? 0, groupId);

            return members ?? new List<GroupMember>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching group members for {GroupId}", groupId);
            return new List<GroupMember>();
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, MemberLocation>> GetLatestLocationsAsync(
        Guid groupId,
        List<string>? userIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch locations - API not configured");
            return new Dictionary<string, MemberLocation>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Post, $"/api/mobile/groups/{groupId}/locations/latest");
            request.Content = JsonContent.Create(new { includeUserIds = userIds }, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch latest locations: {StatusCode}", response.StatusCode);
                return new Dictionary<string, MemberLocation>();
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var locations = ParseLatestLocationsResponse(responseBody);

            _logger.LogDebug("Fetched {Count} latest locations for group {GroupId}", locations.Count, groupId);

            return locations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching latest locations for {GroupId}", groupId);
            return new Dictionary<string, MemberLocation>();
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

    /// <inheritdoc/>
    public async Task<GroupLocationsQueryResponse?> QueryLocationsAsync(
        Guid groupId,
        GroupLocationsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot query locations - API not configured");
            return null;
        }

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Post, $"/api/mobile/groups/{groupId}/locations/query");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query locations: {StatusCode}", response.StatusCode);
                return null;
            }

            var queryResponse = await response.Content.ReadFromJsonAsync<GroupLocationsQueryResponse>(JsonOptions, cancellationToken);
            _logger.LogDebug(
                "Queried {Count} locations (total {Total}) for group {GroupId}",
                queryResponse?.ReturnedItems ?? 0,
                queryResponse?.TotalItems ?? 0,
                groupId);

            return queryResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying locations for {GroupId}", groupId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdatePeerVisibilityAsync(
        Guid groupId,
        bool disabled,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot update peer visibility - API not configured");
            return false;
        }

        try
        {
            var request = CreateRequest(new HttpMethod("PATCH"), $"/api/mobile/groups/{groupId}/peer-visibility");
            request.Content = JsonContent.Create(new PeerVisibilityUpdateRequest { Disabled = disabled }, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to update peer visibility: {StatusCode}", response.StatusCode);
                return false;
            }

            _logger.LogDebug("Updated peer visibility for group {GroupId}: disabled={Disabled}", groupId, disabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating peer visibility for {GroupId}", groupId);
            return false;
        }
    }

    /// <summary>
    /// Parses the latest locations response into a dictionary.
    /// </summary>
    private static Dictionary<string, MemberLocation> ParseLatestLocationsResponse(string responseBody)
    {
        var result = new Dictionary<string, MemberLocation>();

        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            // The response is an array of location objects with userId
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var userId = element.TryGetProperty("userId", out var userIdProp)
                        ? userIdProp.GetString()
                        : null;

                    if (string.IsNullOrEmpty(userId))
                        continue;

                    var location = new MemberLocation();

                    if (element.TryGetProperty("coordinates", out var coords))
                    {
                        if (coords.TryGetProperty("y", out var lat))
                            location.Latitude = lat.GetDouble();
                        if (coords.TryGetProperty("x", out var lon))
                            location.Longitude = lon.GetDouble();
                    }

                    if (element.TryGetProperty("localTimestamp", out var timestamp))
                        location.Timestamp = timestamp.GetDateTime();

                    if (element.TryGetProperty("isLive", out var isLive))
                        location.IsLive = isLive.GetBoolean();

                    if (element.TryGetProperty("fullAddress", out var address))
                        location.Address = address.GetString();

                    result[userId] = location;
                }
            }
        }
        catch (JsonException ex)
        {
            // Log parse error but don't propagate - return partial results
            System.Diagnostics.Debug.WriteLine($"[GroupsService] Failed to parse locations response: {ex.Message}");
        }

        return result;
    }
}
