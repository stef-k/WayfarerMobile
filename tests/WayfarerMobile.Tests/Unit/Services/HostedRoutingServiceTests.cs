using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutingServiceTests
{
    private static readonly Guid WalkingProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CyclingProfile = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(true, "unknown", "unknown", HostedProfileSelectionKind.Selected)]
    [InlineData(false, "walk", "hiking", HostedProfileSelectionKind.Selected)]
    [InlineData(false, "walk", "active", HostedProfileSelectionKind.RequiresChoice)]
    [InlineData(false, "boat", "water", HostedProfileSelectionKind.RequiresChoice)]
    public void SelectProfile_UsesGuidThenOnlyAnUnambiguousTextualHint(
        bool savedGuidMatches, string modeKey, string category, HostedProfileSelectionKind expected)
    {
        var catalog = Catalog(
            new HostedRoutingProfile(WalkingProfile, "Walking", "walk", "active"),
            new HostedRoutingProfile(CyclingProfile, "Cycling", "bike", "active"));

        var result = HostedProfileSelector.Select(
            savedGuidMatches ? WalkingProfile : null, modeKey, category, catalog);

        result.Kind.Should().Be(expected);
        if (savedGuidMatches || modeKey == "walk" && category == "hiking")
            result.Profile?.TransportProfileId.Should().Be(WalkingProfile);
    }

    [Fact]
    public void ConfirmChoice_RejectsCancelledAndStaleCatalogChoices()
    {
        var original = Catalog(new HostedRoutingProfile(WalkingProfile, "Walking", "walk", "active"));
        var renamed = new HostedRoutingCatalog("v1.catalog-b", "available",
            [new(WalkingProfile, "On foot", "walk", "active")]);

        HostedProfileSelector.Confirm(null, original).Should().BeNull();
        HostedProfileSelector.Confirm(new(WalkingProfile, "Walking", "walk", "active"), renamed)
            .Should().BeNull();
    }

    [Fact]
    public async Task RequestRouteAsync_UsesCatalogForCapabilityAndSelectedAuthorityForRoute()
    {
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        var catalog = Catalog(new HostedRoutingProfile(WalkingProfile, "Walking", "walk", "active"));
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>())).ReturnsAsync(catalog);
        api.Setup(client => client.GetCapabilityAsync(
                WalkingProfile, catalog.DiscoveryCatalogIdentity!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(
                WalkingProfile, catalog.DiscoveryCatalogIdentity!, "v1.selected-a", Attribution()));
        api.Setup(client => client.GetRouteAsync(
                It.Is<HostedRouteRequest>(request => request.TransportProfileId == WalkingProfile
                    && request.SelectedProfileAuthorityIdentity == "v1.selected-a"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, "v1.selected-a"));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(
            WalkingProfile, expectedCatalogIdentity: catalog.DiscoveryCatalogIdentity));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
        result.Route.Should().NotBeNull();
        result.Route!.IsDirectRoute.Should().BeFalse();
        result.Route.Attribution.Should().ContainSingle(item => item.Text == "Powered by Wayfarer test");
        api.VerifyAll();
    }

    [Fact]
    public async Task RequestRouteAsync_UnrelatedCatalogChangeAfterCapability_DoesNotInvalidateSelectedAuthority()
    {
        var api = SuccessfulApi(WalkingProfile, "v1.catalog-a", "v1.selected-a");
        var state = HostedRoutingState.ForTest(catalogIdentity: "v1.catalog-b", selectedAuthorityIdentity: "v1.selected-a");
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance, state);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(
            WalkingProfile, expectedCatalogIdentity: "v1.catalog-a"));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
    }

    [Fact]
    public async Task RequestRouteAsync_SelectedAuthorityChangeBeforePublication_DiscardsResponse()
    {
        var api = SuccessfulApi(WalkingProfile, "v1.catalog-a", "v1.selected-a");
        var state = HostedRoutingState.ForTest(selectedAuthorityIdentity: "v1.selected-b");
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance, state);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(
            WalkingProfile, expectedCatalogIdentity: "v1.catalog-a"));

        result.Outcome.Should().Be(HostedRoutingOutcome.Stale);
        result.Route.Should().BeNull();
    }

    private static HostedRoutingCatalog Catalog(params HostedRoutingProfile[] profiles) =>
        new("v1.catalog-a", "available", profiles);

    private static Mock<IHostedRoutingApiClient> SuccessfulApi(Guid profileId, string catalogIdentity,
        string authorityIdentity)
    {
        var api = new Mock<IHostedRoutingApiClient>();
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCatalog(catalogIdentity, "available",
                [new(profileId, "Walking", "walk", "active")]));
        api.Setup(client => client.GetCapabilityAsync(profileId, catalogIdentity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(profileId, catalogIdentity, authorityIdentity, Attribution()));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(profileId, authorityIdentity));
        return api;
    }

    private static IReadOnlyList<WayfarerMobile.Core.Models.HostedRouteAttribution> Attribution() =>
        [new("Powered by Wayfarer test", "https://example.test")];
}
