using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class SegmentPresentationProjectorTests
{
    [Fact]
    public void Trail_UsesNeutralFailureValidWaypointTrailAndLegacyEndpoints()
    {
        var method = typeof(SegmentAnchorResolver).Assembly
            .GetType("WayfarerMobile.Core.Helpers.SegmentPresentationProjector")?
            .GetMethod("CreateTrail");
        method.Should().NotBeNull("selected-Segment presentation must own the neutral invalid state");
        var (valid, places, geometry) = SegmentAnchorResolverTests.CreateAbc();
        var invalid = new TripSegment
        {
            OriginId = valid.OriginId, DestinationId = valid.DestinationId, HasCustomRoute = true,
            Waypoints = [new() { PlaceId = valid.Waypoints[0].PlaceId, Position = 0, RouteVertexIndex = 2 }]
        };
        var legacy = new TripSegment { OriginId = valid.OriginId, DestinationId = valid.DestinationId };

        Invoke(invalid).Should().Equal("Route details unavailable");
        Invoke(valid).Should().Equal("A — Start — Alpha", "B — Via 1 — Bravo", "C — End — Charlie");
        Invoke(valid).Should().NotContain("Route details unavailable");
        Invoke(legacy).Should().BeEmpty();

        IReadOnlyList<string> Invoke(TripSegment segment) =>
            (IReadOnlyList<string>)method!.Invoke(null, [segment, places, geometry])!;
    }
}
