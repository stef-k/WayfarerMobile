using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Globalization;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>Calls only the authenticated Wayfarer Mobile routing contract with bounded strict parsing.</summary>
public sealed class HostedRoutingApiClient : IHostedRoutingApiClient
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ISettingsService settings;

    public HostedRoutingApiClient(IHttpClientFactory httpClientFactory, ISettingsService settings)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = settings;
    }

    public async Task<HostedRoutingCatalog> DiscoverAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/mobile/routing/profiles", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return new(null, "unavailable", []);
        if (!response.IsSuccessStatusCode) return new(null, "unavailable", []);
        return await ParseAsync<HostedRoutingCatalog>(response, cancellationToken)
            ?? new(null, "invalid-response", []);
    }

    public async Task<HostedRoutingCapability> GetCapabilityAsync(Guid profileId,
        string discoveryCatalogIdentity, CancellationToken cancellationToken)
    {
        var endpoint = $"/api/mobile/routing/capability/{profileId:D}?discoveryCatalogIdentity={Uri.EscapeDataString(discoveryCatalogIdentity)}";
        using var response = await SendAsync(HttpMethod.Get, endpoint, null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new("unavailable", profileId, null, null, null, null, null, null, null);
        var value = await ParseAsync<CapabilityDto>(response, cancellationToken);
        return value?.ToModel() ?? new("invalid-response", profileId, null, null, null, null, null, null, null);
    }

    public async Task<HostedRouteResponse> GetRouteAsync(HostedRouteRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/mobile/routing/route", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return Failure("unavailable");
        var value = await ParseAsync<RouteResponseDto>(response, cancellationToken);
        return value?.ToModel() ?? Failure("invalid-response");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string endpoint, object? body,
        CancellationToken cancellationToken)
    {
        var normalizedServer = HostedRouteServerIdentity.Normalize(settings.ServerUrl);
        if (!settings.IsConfigured || normalizedServer.Length == 0
            || normalizedServer != settings.ServerUrl) throw new HttpRequestException("Wayfarer is unavailable.");
        using var request = new HttpRequestMessage(method, $"{normalizedServer}{endpoint}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);
        return await httpClientFactory.CreateClient("WayfarerApi").SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<T?> ParseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) return default;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (bounded.Length + read > MaximumResponseBytes) return default;
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bounded.Position = 0;
        try { return await JsonSerializer.DeserializeAsync<T>(bounded, JsonOptions, cancellationToken); }
        catch (JsonException) { return default; }
    }

    private static HostedRouteResponse Failure(string outcome) =>
        new(false, outcome, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private sealed record CapabilityDto(string Outcome, Guid TransportProfileId, string? Provider,
        Guid? ProviderConfigurationId, string? MappingIdentity, string? StorageMode,
        IReadOnlyList<HostedRouteAttribution>? Attribution, string? DiscoveryCatalogIdentity,
        string? SelectedProfileAuthorityIdentity)
    {
        public HostedRoutingCapability ToModel() => new(Outcome, TransportProfileId, Provider,
            ProviderConfigurationId, MappingIdentity, StorageMode, Attribution, DiscoveryCatalogIdentity,
            SelectedProfileAuthorityIdentity);
    }

    private sealed record RouteResponseDto(bool Succeeded, string Outcome, IReadOnlyList<HostedRouteCoordinate>? Geometry,
        double? DistanceMetres, double? DurationSeconds, IReadOnlyList<HostedRouteInstruction>? Instructions,
        string? GeneratedAt, string? Provider, Guid? ProviderConfigurationId, string? MappingIdentity,
        Guid? TransportProfileId, IReadOnlyList<HostedRouteCoordinate>? MatchPoints,
        IReadOnlyList<HostedRouteAttribution>? Attribution, string? StorageMode,
        string? SelectedProfileAuthorityIdentity)
    {
        public HostedRouteResponse ToModel() => new(Succeeded, Outcome, Geometry, DistanceMetres, DurationSeconds,
            Instructions, ParseGeneratedAt(GeneratedAt), Provider, ProviderConfigurationId, MappingIdentity,
            TransportProfileId, MatchPoints, Attribution, StorageMode,
            SelectedProfileAuthorityIdentity);

        private static DateTimeOffset? ParseGeneratedAt(string? value)
        {
            if (value == null || !(value.EndsWith('Z') || value.Length >= 6
                    && (value[^6] is '+' or '-') && value[^3] == ':')) return null;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed) ? parsed.ToUniversalTime() : null;
        }
    }
}
