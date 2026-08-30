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
        var live = candidate.Context with { Generation = liveGeneration, NavigationChoice = liveChoice };

        HostedRoutePublication.TryPublish(candidate, live, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
    }

    [Theory]
    [InlineData("different-session", "https://wayfarer.test", "place:test")]
    [InlineData("session", "https://other.test", "place:test")]
    [InlineData("session", "https://wayfarer.test", "member:other")]
    public void CandidateCannotPublishAfterLiveAuthorityDriftsWithoutAnotherHostedRequest(
        string session, string server, string target)
    {
        var direct = DirectRoute();
        var candidate = Candidate();
        var live = candidate.Context with
        {
            SessionAuthority = session,
            NormalizedServer = server,
            TargetAssociation = target
        };

        HostedRoutePublication.TryPublish(candidate, live, direct).Should().BeFalse();
        direct.IsDirectRoute.Should().BeTrue();
    }

    [Fact]
    public void CandidatePublishesOnlyWhenAllLiveAuthorityStillMatches()
    {
        var direct = DirectRoute();
        var candidate = Candidate();

        HostedRoutePublication.TryPublish(candidate, candidate.Context, direct).Should().BeTrue();
        direct.IsDirectRoute.Should().BeFalse();
        direct.Attribution.Should().ContainSingle();
    }

    private static HostedRouteCandidate Candidate()
    {
        var profileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var context = HostedRouteRequestContext.ForTest(profileId) with
        {
            SelectedTransportProfileId = profileId,
            SelectedProfileAuthorityIdentity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
        };
        return new HostedRouteCandidate(RoutedRoute(), context,
            profileId,
            "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            new("geoapify", Guid.Parse("22222222-2222-2222-2222-222222222222"), "mapping", "persistent"),
            DateTimeOffset.UtcNow);
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
