using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutingApiClientTests
{
    [Fact]
    public async Task CredentialBearingConfiguredServer_IsRejectedBeforeHttpClientCreation()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.IsConfigured).Returns(true);
        settings.SetupGet(value => value.ServerUrl)
            .Returns("https://user:password@wayfarer.example");
        settings.SetupGet(value => value.ApiToken).Returns("secret-token");
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var client = new HostedRoutingApiClient(factory.Object, settings.Object);

        var action = () => client.DiscoverAsync(default);

        await action.Should().ThrowAsync<HttpRequestException>();
        factory.VerifyNoOtherCalls();
    }

    [Fact]
    public void TripJson_CapturesCurrentProfileGuidOnlyInTransientObjectState()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(HostedSegmentProfileIdentity.Configure);
        var segment = JsonSerializer.Deserialize<WayfarerMobile.Core.Models.TripSegment>(
            $$"""{"id":"22222222-2222-2222-2222-222222222222","transportProfileId":"{{profileId}}"}""",
            new JsonSerializerOptions { TypeInfoResolver = resolver });

        HostedSegmentProfileIdentity.Get(segment).Should().Be(profileId);
    }

    [Fact]
    public async Task ControlledFlow_UsesOnlyAuthenticatedWayfarerContractAndBothIdentities()
    {
        const string identity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var requests = new List<(Uri Uri, string? Authorization, string Body)>();
        var handler = new RecordingHandler(async request =>
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/api/mobile/routing/profiles" => $$"""{"outcome":"available","discoveryCatalogIdentity":"{{identity}}","profiles":[],"provider":"geoapify","providerModes":[{"key":"walk","label":"Walk"},{"key":"bus","label":"Bus"}],"futureField":true}""",
                var path when path.StartsWith("/api/mobile/routing/capability/") => $$"""{"outcome":"available","transportProfileId":"{{profileId}}","provider":"geoapify","providerConfigurationId":"22222222-2222-2222-2222-222222222222","mappingIdentity":"mapping","storageMode":"persistent","attribution":[{"text":"Powered by test","url":"https://example.test"}],"discoveryCatalogIdentity":"{{identity}}","selectedProfileAuthorityIdentity":"{{identity}}","providerMode":"walk"}""",
                "/api/mobile/routing/route" => $$"""{"succeeded":true,"outcome":"available","geometry":[{"longitude":23,"latitude":37},{"longitude":23.01,"latitude":37.01}],"distanceMetres":1500,"durationSeconds":900,"instructions":[{"text":"Continue","type":"continue","fromIndex":0,"toIndex":1,"distanceMetres":1500,"durationSeconds":900}],"generatedAt":"2026-08-30T00:00:00+02:00","provider":"geoapify","providerConfigurationId":"22222222-2222-2222-2222-222222222222","mappingIdentity":"mapping","transportProfileId":"{{profileId}}","matchPoints":[{"longitude":23,"latitude":37},{"longitude":23.01,"latitude":37.01}],"attribution":[{"text":"Powered by test","url":"https://example.test"}],"storageMode":"persistent","selectedProfileAuthorityIdentity":"{{identity}}","providerMode":"walk"}""",
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
            return Json(HttpStatusCode.OK, json);
        });
        var client = Create(handler);

        var catalog = await client.DiscoverAsync(default);
        var capability = await client.GetCapabilityAsync(
            profileId, "walk", catalog.DiscoveryCatalogIdentity!, default);
        var route = await client.GetRouteAsync(new(profileId, new(23, 37), new(23.01, 37.01), [],
            capability.SelectedProfileAuthorityIdentity!, "walk"), default);

        route.Succeeded.Should().BeTrue();
        route.Provider.Should().Be("geoapify");
        route.StorageMode.Should().Be("persistent");
        route.ProviderMode.Should().Be("walk");
        route.GeneratedAt.Should().Be(new DateTimeOffset(2026, 8, 29, 22, 0, 0, TimeSpan.Zero));
        catalog.Modes.Should().Equal(new HostedProviderMode("walk", "Walk"), new HostedProviderMode("bus", "Bus"));
        capability.ProviderMode.Should().Be("walk");
        requests.Should().OnlyContain(item => item.Uri.Host == "wayfarer.test" && item.Authorization == "Bearer token");
        requests[1].Uri.Query.Should().Contain($"discoveryCatalogIdentity={identity}").And.Contain("providerMode=walk");
        requests[2].Body.Should().Contain($"\"selectedProfileAuthorityIdentity\":\"{identity}\"")
            .And.Contain("\"providerMode\":\"walk\"")
            .And.NotContain("discoveryCatalogIdentity").And.NotContain("apiKey");
    }

    [Fact]
    public async Task OldBackend404_IsBoundedRoutingUnavailable()
    {
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.NotFound, "{}"))));

        var catalog = await client.DiscoverAsync(default);

        catalog.Outcome.Should().Be("unavailable");
        catalog.Modes.Should().BeEmpty();
    }

    [Fact]
    public async Task Discovery_IgnoresLegacyProfilesAndUnknownAdditiveResponseMember()
    {
        const string identity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var json = JsonSerializer.Serialize(new
        {
            outcome = "available",
            discoveryCatalogIdentity = identity,
            profiles = new[] { new { transportProfileId = profileId, displayName = "Walking", modeKey = "walk", category = "active" } },
            futureMetadata = new { version = 2 }
        });
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, json))));

        var catalog = await client.DiscoverAsync(default);

        catalog.Outcome.Should().Be("available");
        catalog.Modes.Should().BeEmpty();
    }

    [Fact]
    public async Task Discovery_IgnoresWrongKindForUnusedLegacyProfilesMember()
    {
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"outcome":"available","discoveryCatalogIdentity":"v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","profiles":"wrong"}"""))));

        var catalog = await client.DiscoverAsync(default);

        catalog.Outcome.Should().Be("available");
        catalog.Modes.Should().BeEmpty();
    }

    [Fact]
    public async Task Capability400InvalidRequest_IsReturnedAsTerminalOutcome()
    {
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"outcome":"invalid-request","transportProfileId":"11111111-1111-1111-1111-111111111111"}"""))));

        var capability = await client.GetCapabilityAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "walk", "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", default);

        capability.Outcome.Should().Be("invalid-request");
    }

    [Fact]
    public async Task Route400InvalidRequest_IsReturnedAsTerminalOutcome()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"succeeded":false,"outcome":"invalid-request"}"""))));

        var route = await client.GetRouteAsync(new(profileId, new(23, 37), new(23.01, 37.01), [],
            "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "walk"), default);

        route.Outcome.Should().Be("invalid-request");
        route.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-08-30T12:00:00", false)]
    [InlineData("2026-08-30T12:00:00+02:00", true)]
    public async Task RouteGeneratedAt_RequiresExplicitOffsetAndNormalizesUtc(string generatedAt, bool valid)
    {
        const string identity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var json = JsonSerializer.Serialize(new
        {
            succeeded = true, outcome = "available",
            geometry = new[] { new { longitude = 23d, latitude = 37d }, new { longitude = 23.01, latitude = 37.01 } },
            distanceMetres = 1500d, durationSeconds = 900d,
            instructions = Array.Empty<object>(), generatedAt, provider = "geoapify",
            providerConfigurationId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            mappingIdentity = "mapping", transportProfileId = profileId,
            matchPoints = new[] { new { longitude = 23d, latitude = 37d }, new { longitude = 23.01, latitude = 37.01 } },
            attribution = new[] { new { text = "Powered by test", url = "https://example.test" } },
            storageMode = "persistent", selectedProfileAuthorityIdentity = identity
        });
        var client = Create(new RecordingHandler(_ => Task.FromResult(Json(HttpStatusCode.OK, json))));

        var route = await client.GetRouteAsync(
            new(profileId, new(23, 37), new(23.01, 37.01), [], identity, "walk"), default);

        if (valid)
            route.GeneratedAt.Should().Be(new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero));
        else
            route.GeneratedAt.Should().BeNull();
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
