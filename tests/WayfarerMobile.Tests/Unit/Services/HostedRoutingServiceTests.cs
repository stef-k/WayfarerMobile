using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutingServiceTests
{
    private static readonly Guid WalkingProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CyclingProfile = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string IdentityA = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string IdentityB = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ";

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
    public void ConfirmChoice_AcceptsSameGuidAndRefreshesRenamedMetadata()
    {
        var original = Catalog(new HostedRoutingProfile(WalkingProfile, "Walking", "walk", "active"));
        var renamed = new HostedRoutingCatalog(IdentityB, "available",
            [new(WalkingProfile, "On foot", "walk", "active")]);

        HostedProfileSelector.Confirm(null, original).Should().BeNull();
        HostedProfileSelector.Confirm(new(WalkingProfile, "Walking", "walk", "active"), renamed)
            .Should().Be(renamed.Profiles[0]);
    }

    [Theory]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData(" v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB", false)]
    [InlineData("v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAé", false)]
    public void OpaqueIdentity_RequiresCanonicalSha256Base64UrlFraming(string value, bool expected)
    {
        HostedOpaqueIdentity.IsValid(value).Should().Be(expected);
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
                WalkingProfile, catalog.DiscoveryCatalogIdentity!, IdentityA, Attribution()));
        api.Setup(client => client.GetRouteAsync(
                It.Is<HostedRouteRequest>(request => request.TransportProfileId == WalkingProfile
                    && request.SelectedProfileAuthorityIdentity == IdentityA),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(
            WalkingProfile, expectedCatalogIdentity: catalog.DiscoveryCatalogIdentity));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
        result.Candidate.Should().NotBeNull();
        result.Candidate!.Route.IsDirectRoute.Should().BeFalse();
        result.Candidate.Route.Attribution.Should().ContainSingle(item => item.Text == "Powered by Wayfarer test");
        api.VerifyAll();
    }

    [Fact]
    public async Task RequestRouteAsync_UnrelatedCatalogChangeAfterCapability_DoesNotInvalidateSelectedAuthority()
    {
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(
            WalkingProfile, expectedCatalogIdentity: IdentityA));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
    }

    [Fact]
    public void Publication_SelectedAuthorityChangeBeforePublication_DiscardsCandidate()
    {
        var context = HostedRouteRequestContext.ForTest(WalkingProfile);
        var candidate = new HostedRouteCandidate(new WayfarerMobile.Core.Models.NavigationRoute(),
            context, WalkingProfile, IdentityA,
            new("geoapify", CyclingProfile, "mapping", "persistent"), DateTimeOffset.UtcNow);
        var live = new HostedRouteLiveAuthority(context.Generation, context.AuthenticationSessionRevision,
            context.NormalizedServer, context.Origin, context.Destination, context.Anchors,
            context.TargetAssociation, context.SegmentId, context.SavedTransportProfileId,
            context.ModeKey, context.Category, WalkingProfile, IdentityB, context.NavigationChoice);

        HostedRoutePublication.Current(candidate, live).Should().BeFalse();
    }

    [Fact]
    public async Task RequestRouteAsync_ACompletesLast_OnlyBCanPublishOrClearLoading()
    {
        var aCompletion = new TaskCompletionSource<HostedRouteResponse>();
        var aStarted = new TaskCompletionSource();
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        api.SetupSequence(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => { aStarted.SetResult(); return aCompletion.Task; })
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);
        var a = service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile) with { Generation = 1 });
        await aStarted.Task;
        var b = service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile) with { Generation = 2 });

        (await b).Outcome.Should().Be(HostedRoutingOutcome.Success);
        service.IsLoading.Should().BeFalse();
        aCompletion.SetResult(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));
        (await a).Outcome.Should().Be(HostedRoutingOutcome.Stale);
        service.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task SelectDirect_WhileHostedRequestIsInFlight_DiscardsHostedResponse()
    {
        var completion = new TaskCompletionSource<HostedRouteResponse>();
        var started = new TaskCompletionSource();
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => { started.SetResult(); return completion.Task; });
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);
        var pending = service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));
        await started.Task;

        service.SelectDirect(2);
        completion.SetResult(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));

        (await pending).Outcome.Should().Be(HostedRoutingOutcome.Stale);
        service.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task CapabilityAndRouteMetadata_MustMatchAndUnknownStorageRemainsTransientlyUsable()
    {
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(WalkingProfile, IdentityA, IdentityA, Attribution(),
                mappingIdentity: "mapping-v2", storageMode: "future-transient"));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA) with
            { MappingIdentity = "mapping-v2", StorageMode = "future-transient" });
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
        result.Candidate!.Metadata.Should().Be(new HostedRouteCapabilityMetadata("geoapify",
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "mapping-v2", "future-transient"));
        result.Candidate.GeneratedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("invalid-request")]
    [InlineData("catalog-changed")]
    public async Task TerminalCapabilityOutcome_MakesNoRouteContact(string outcome)
    {
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability(outcome, WalkingProfile, null, null, null, null,
                null, outcome == "catalog-changed" ? null : IdentityA, null));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));

        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("invalid-request")]
    [InlineData("authority-changed")]
    public async Task TerminalRouteOutcome_IsNotRetried(string outcome)
    {
        var api = SuccessfulApi(WalkingProfile, IdentityA, IdentityA);
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA) with
            { Succeeded = false, Outcome = outcome });
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));

        result.Outcome.Should().Be(HostedRoutingOutcome.InvalidResponse);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("routing-disabled")]
    [InlineData("no-authority")]
    public async Task DiscoveryUnavailable_RemainsLocalAndMakesNoCapabilityOrRouteRequest(string outcome)
    {
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCatalog(null, outcome, []));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));

        result.Outcome.Should().Be(HostedRoutingOutcome.Unavailable);
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HostedFailureLeavesDirectRouteUnchanged()
    {
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCatalog(null, "routing-disabled", []));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);
        var direct = new WayfarerMobile.Core.Models.NavigationRoute
        {
            IsDirectRoute = true,
            Waypoints = [new() { Longitude = 23, Latitude = 37 }, new() { Longitude = 23.01, Latitude = 37.01 }]
        };

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(WalkingProfile));

        result.Candidate.Should().BeNull();
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
    }

    [Fact]
    public void Canonicalize_UsesLongitudeLatitudeAwayFromZeroAndPreservesDuplicates()
    {
        var result = HostedRouteIdentity.Canonicalize([
            new(1.234565, -0.000005), new(1.234565, -0.000005), new(-0.0, 90)]);

        result.Should().Equal(123457, -1, 123457, -1, 0, 9000000);
    }

    [Fact]
    public void Canonicalize_RejectsInvalidWgs84Coordinates()
    {
        var act = () => HostedRouteIdentity.Canonicalize([new(0, 90.00001)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static HostedRoutingCatalog Catalog(params HostedRoutingProfile[] profiles) =>
        new(IdentityA, "available", profiles);

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
