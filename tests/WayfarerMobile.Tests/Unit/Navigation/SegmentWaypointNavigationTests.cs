using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Navigation;

namespace WayfarerMobile.Tests.Unit.Navigation;

public class SegmentWaypointNavigationTests
{
    [Fact]
    public void Build_ValidAbcSegment_CreatesConsecutiveNamedGeometrySlices()
    {
        var (segment, places, geometry) = Helpers.SegmentAnchorResolverTests.CreateAbc();
        segment.Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.72,37.92]]}""";
        segment.DurationMinutes = 7;
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(),
            Regions = [new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = places }],
            Segments = [segment]
        };

        var graph = TripNavigationGraphBuilder.Build(trip);
        var edges = graph.GetAllEdges().Where(e => e.ParentSegmentId == segment.Id).ToList();

        edges.Should().HaveCount(2);
        edges.Select(e => (e.FromNodeId, e.ToNodeId)).Should().Equal(
            (places[0].Id.ToString(), places[1].Id.ToString()),
            (places[1].Id.ToString(), places[2].Id.ToString()));
        edges[0].RouteGeometry.Should().HaveCount(2);
        edges[1].RouteGeometry.Should().HaveCount(2);
        edges.Sum(e => e.DurationMinutes).Should().Be(7);
        graph.GetNode(places[1].Id.ToString())!.Name.Should().Be("Bravo");
    }

    [Fact]
    public void Build_InvalidWaypointIndex_DoesNotFabricateConnection()
    {
        var (segment, places, _) = Helpers.SegmentAnchorResolverTests.CreateAbc();
        segment.Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.72,37.92]]}""";
        segment.Waypoints[0].RouteVertexIndex = 99;
        var trip = new TripDetails
        {
            Regions = [new TripRegion { Places = places }], Segments = [segment]
        };

        TripNavigationGraphBuilder.Build(trip).GetAllEdges().Should().BeEmpty();
    }
}
