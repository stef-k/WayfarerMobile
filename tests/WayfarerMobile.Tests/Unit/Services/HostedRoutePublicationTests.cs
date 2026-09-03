using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutePublicationTests
{
    [Theory]
    [InlineData(2, "hosted")]
    [InlineData(1, "direct")]
    public void CandidateCannotOverwriteNewerOrDirectRoute(long liveGeneration, string liveChoice)
    {
        var direct = DirectRoute();
        var candidate = Candidate();
        var live = Live(candidate) with { Generation = liveGeneration, NavigationChoice = liveChoice };

        HostedRoutePublication.TryPublish(candidate, live, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
    }

    [Fact]
    public void DelayedCandidateCannotPublishAfterLiveLocationChanges()
    {
        var direct = DirectRoute();
        var candidate = Candidate();
        var live = Live(candidate) with { Origin = new(23.0002, 37.0002) };

        HostedRoutePublication.TryPublish(candidate, live, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
        direct.HostedProvenance.Should().BeNull();
    }

    [Fact]
    public void CandidateCannotPublishAfterAuthenticationSessionRevisionChanges()
    {
        var direct = DirectRoute();
        var candidate = Candidate();
        var live = Live(candidate) with
        {
            AuthenticationSessionRevision = candidate.Context.AuthenticationSessionRevision + 1
        };

        HostedRoutePublication.TryPublish(candidate, live, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
    }

    [Fact]
    public void CandidatePublishesOnlyWhenAllLiveAuthorityStillMatches()
    {
        var direct = DirectRoute();
        var candidate = Candidate();

        HostedRoutePublication.TryPublish(candidate, Live(candidate), direct).Should().BeTrue();
        direct.IsDirectRoute.Should().BeFalse();
        direct.Attribution.Should().ContainSingle();
        direct.HostedProvenance.Should().Be(new HostedRouteProvenance(
            candidate.SelectedProfileId,
            candidate.SelectedProfileAuthorityIdentity,
            candidate.Metadata.Provider,
            candidate.Metadata.ProviderConfigurationId,
            candidate.Metadata.MappingIdentity,
            candidate.Metadata.StorageMode,
            candidate.GeneratedAt,
            candidate.SelectedProviderMode));
    }

    private static HostedRouteCandidate Candidate()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var context = HostedRouteRequestContext.ForTest(profileId);
        return new HostedRouteCandidate(RoutedRoute(), context,
            profileId,
            "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            new("geoapify", Guid.Parse("22222222-2222-2222-2222-222222222222"), "mapping", "persistent"),
            DateTimeOffset.UtcNow, "walk");
    }

    private static HostedRouteLiveAuthority Live(HostedRouteCandidate candidate) => new(
        candidate.Context.Generation,
        candidate.Context.AuthenticationSessionRevision,
        candidate.Context.NormalizedServer,
        candidate.Context.Origin,
        candidate.Context.Destination,
        candidate.Context.Anchors,
        candidate.Context.TargetAssociation,
        candidate.Context.SegmentId,
        candidate.Context.SavedTransportProfileId,
        candidate.Context.ModeKey,
        candidate.Context.Category,
        candidate.SelectedProfileId,
        candidate.SelectedProfileAuthorityIdentity,
        candidate.Context.NavigationChoice,
        candidate.SelectedProviderMode);

    [Fact]
    public void CandidateCannotPublishForDifferentProviderMode()
    {
        var direct = DirectRoute();
        var candidate = Candidate();

        HostedRoutePublication.TryPublish(candidate,
            Live(candidate) with { SelectedProviderMode = "drive" }, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
    }

    private static NavigationRoute DirectRoute() => new()
    {
        IsDirectRoute = true,
        Waypoints = [new() { Longitude = 23, Latitude = 37 }, new() { Longitude = 23.01, Latitude = 37.01 }]
    };

    private static NavigationRoute RoutedRoute() => new()
    {
        IsDirectRoute = false,
        Waypoints = [new() { Longitude = 23, Latitude = 37 }, new() { Longitude = 23.02, Latitude = 37.02 }],
        Attribution = [new("Powered by test", "https://example.test")]
    };
}
