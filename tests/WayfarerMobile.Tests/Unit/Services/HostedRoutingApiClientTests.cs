using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutingApiClientTests
{
    [Fact]
    public async Task ControlledFlow_UsesOnlyAuthenticatedWayfarerContractAndBothIdentities()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var requests = new List<(Uri Uri, string? Authorization, string Body)>();
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/mobile/routing/profiles" => $$"""{"outcome":"available","discoveryCatalogIdentity":"v1.catalog-a","profiles":[{"transportProfileId":"{{profileId}}","displayName":"Walking","modeKey":"walk","category":"active"}]}""",
                var path when path.StartsWith("/api/mobile/routing/capability/") => $$"""{"outcome":"available","transportProfileId":"{{profileId}}","provider":"geoapify","providerConfigurationId":"22222222-2222-2222-2222-222222222222","mappingIdentity":"mapping","storageMode":"persistent","attribution":[{"text":"Powered by test","url":"https://example.test"}],"discoveryCatalogIdentity":"v1.catalog-a","selectedProfileAuthorityIdentity":"v1.selected-a"}""",
                "/api/mobile/routing/route" => $$"""{"succeeded":true,"outcome":"available","geometry":[{"longitude":23,"latitude":37},{"longitude":23.01,"latitude":37.01}],"distanceMetres":1500,"durationSeconds":900,"instructions":[{"text":"Continue","type":"continue","fromIndex":0,"toIndex":1,"distanceMetres":1500,"durationSeconds":900}],"generatedAt":"2026-08-30T18:00:00Z","provider":"geoapify","providerConfigurationId":"22222222-2222-2222-2222-222222222222","mappingIdentity":"mapping","transportProfileId":"{{profileId}}","matchPoints":[{"longitude":23,"latitude":37},{"longitude":23.01,"latitude":37.01}],"attribution":[{"text":"Powered by test","url":"https://example.test"}],"storageMode":"persistent","selectedProfileAuthorityIdentity":"v1.selected-a"}""",
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
            return Json(HttpStatusCode.OK, json);
        });
        var client = Create(handler);

        var catalog = await client.DiscoverAsync(default);
        var capability = await client.GetCapabilityAsync(profileId, catalog.DiscoveryCatalogIdentity!, default);
        var route = await client.GetRouteAsync(new(profileId, new(23, 37), new(23.01, 37.01), [],
            capability.SelectedProfileAuthorityIdentity!), default);

        route.Succeeded.Should().BeTrue();
        requests.Should().OnlyContain(item => item.Uri.Host == "wayfarer.test" && item.Authorization == "Bearer token");
        requests[1].Uri.Query.Should().Contain("discoveryCatalogIdentity=v1.catalog-a");
        requests[2].Body.Should().Contain("\"selectedProfileAuthorityIdentity\":\"v1.selected-a\"")
            .And.NotContain("discoveryCatalogIdentity").And.NotContain("provider").And.NotContain("apiKey");
    }

    [Fact]
    public async Task OldBackend404_IsBoundedRoutingUnavailable()
    {
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.NotFound, "{}"))));

        var catalog = await client.DiscoverAsync(default);

        catalog.Outcome.Should().Be("unavailable");
        catalog.Profiles.Should().BeEmpty();
    }

    private static HostedRoutingApiClient Create(HttpMessageHandler handler)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.IsConfigured).Returns(true);
        settings.SetupGet(value => value.ServerUrl).Returns("https://wayfarer.test");
        settings.SetupGet(value => value.ApiToken).Returns("token");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient("WayfarerApi")).Returns(new HttpClient(handler));
        return new(factory.Object, settings.Object);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request);
    }
}
