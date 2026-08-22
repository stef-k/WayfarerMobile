using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class SegmentAnchorResolverTests
{
    [Fact]
    public void Resolve_ValidAThroughC_ReturnsSharedLabelsRolesAndValidatedIndices()
    {
        var (segment, places, geometry) = CreateAbc();

        var result = SegmentAnchorResolver.Resolve(segment, places, geometry);

        result.IsValid.Should().BeTrue();
        result.Anchors.Select(a => (a.Label, a.Role, a.PlaceName, a.RouteVertexIndex)).Should().Equal(
            ("A", "Start", "Alpha", (int?)0),
            ("B", "Via 1", "Bravo", (int?)1),
            ("C", "End", "Charlie", (int?)2));
        result.TextTrail.Should().Equal(
            "A — Start — Alpha", "B — Via 1 — Bravo", "C — End — Charlie");
    }

    [Fact]
    public void Label_TransitionsFromZToAa()
    {
        SegmentAnchorResolver.GetLabel(25).Should().Be("Z");
        SegmentAnchorResolver.GetLabel(26).Should().Be("AA");
    }

    [Fact]
    public void Resolve_ReusedEndpointIdentity_PreservesTextAndCombinesBadgeLabels()
    {
        var (segment, places, geometry) = CreateAbc();
        segment.DestinationId = segment.OriginId;
        geometry[2] = geometry[0];

        var result = SegmentAnchorResolver.Resolve(segment, places, geometry);
        var badges = SegmentDecorationProjector.CreateBadges(result);

        result.Anchors.Should().HaveCount(3);
        badges.Should().ContainSingle(b => b.Label == "A/C");
    }

    [Fact]
    public void Resolve_MismatchedWaypointIndex_FailsNeutrally()
    {
        var (segment, places, geometry) = CreateAbc();
        segment.Waypoints[0].RouteVertexIndex = 2;

        var result = SegmentAnchorResolver.Resolve(segment, places, geometry);

        result.IsValid.Should().BeFalse();
        result.Failure.Should().Be(SegmentAnchorFailure.CoordinateIndexMismatch);
        SegmentDecorationProjector.CreateBadges(result).Should().BeEmpty();
    }

    internal static (TripSegment Segment, List<TripPlace> Places, List<SegmentCoordinate> Geometry) CreateAbc()
    {
        var a = new TripPlace { Id = Guid.NewGuid(), Name = "Alpha", Latitude = 37.90, Longitude = 23.70 };
        var b = new TripPlace { Id = Guid.NewGuid(), Name = "Bravo", Latitude = 37.91, Longitude = 23.71 };
        var c = new TripPlace { Id = Guid.NewGuid(), Name = "Charlie", Latitude = 37.92, Longitude = 23.72 };
        var segment = new TripSegment
        {
            Id = Guid.NewGuid(), OriginId = a.Id, DestinationId = c.Id, HasCustomRoute = true,
            Waypoints = [new TripSegmentWaypoint { PlaceId = b.Id, Position = 0, RouteVertexIndex = 1 }]
        };
        return (segment, [a, b, c],
            [new(a.Latitude, a.Longitude), new(b.Latitude, b.Longitude), new(c.Latitude, c.Longitude)]);
    }
}
