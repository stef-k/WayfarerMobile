using System.Text.Json;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class SegmentPresentationProjectorTests
{
    [Fact]
    public void Trail_WithoutWaypointsOrGeometry_PreservesFullEndpointNames()
    {
        var (segment, places, _) = SegmentAnchorResolverTests.CreateAbc();
        segment.Waypoints.Clear();
        places[0].Name = "Station, Region – Full suffix";

        var details = SegmentPresentationProjector.Project(segment, places);
        details.WaypointCountText.Should().Be("Waypoint count unavailable");
        details.RoutePointCountText.Should().Be("Route points unavailable");
        details.Trail.Should().Equal(
            "A — Start — Station, Region – Full suffix", "B — End — Charlie");
    }

    [Fact]
    public void OrderedVias_CountSavedPlacesSeparatelyFromRepeatedGeometryVertices()
    {
        var (segment, places, _) = SegmentAnchorResolverTests.CreateAbc();
        var secondVia = new TripPlace { Id = Guid.NewGuid(), Name = "Delta", Latitude = 37.915, Longitude = 23.715 };
        places.Add(secondVia);
        segment.Waypoints.Add(new() { PlaceId = secondVia.Id, Position = 1, RouteVertexIndex = 3 });
        segment.Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.71,37.91],[23.715,37.915],[23.72,37.92]]}""";

        var details = SegmentPresentationProjector.Project(segment, places);

        details.Trail.Should().Equal("A — Start — Alpha", "B — Via 1 — Bravo", "C — Via 2 — Delta", "D — End — Charlie");
        details.WaypointCountText.Should().Be("Waypoints: 2");
        details.RoutePointCountText.Should().Be("Route points: 5");
    }

    [Fact]
    public void ClosedLoopAndDistinctEqualNames_PreserveEveryOccurrence()
    {
        var (segment, places, _) = SegmentAnchorResolverTests.CreateAbc();
        segment.DestinationId = segment.OriginId;
        places[1].Name = places[0].Name;
        segment.Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.70,37.90]]}""";

        SegmentPresentationProjector.Project(segment, places).Trail.Should().Equal(
            "A — Start — Alpha", "B — Via 1 — Alpha", "C — End — Alpha");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"type\":\"MultiLineString\",\"coordinates\":[]}")]
    public void InvalidGeometryAndMissingEndpoint_RetainIndependentText(string? geometry)
    {
        var (segment, places, _) = SegmentAnchorResolverTests.CreateAbc();
        segment.Geometry = geometry;
        segment.OriginId = null;
        segment.OriginName = "Must not substitute a name without identity";

        var details = SegmentPresentationProjector.Project(segment, places);

        details.Trail.Should().Equal("A — Start — Place unavailable", "Route details unavailable", "B — End — Charlie");
        details.WaypointCountText.Should().Be("Waypoint count unavailable");
        details.RoutePointCountText.Should().Be("Route points unavailable");
    }

    [Fact]
    public void UnresolvableWaypoint_DoesNotReportPartialCountOrHideGeometryCount()
    {
        var (segment, places, _) = SegmentAnchorResolverTests.CreateAbc();
        segment.Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.72,37.92]]}""";
        segment.Waypoints.Add(new() { PlaceId = Guid.NewGuid(), Position = 1, RouteVertexIndex = 2 });

        var details = SegmentPresentationProjector.Project(segment, places);

        details.Trail.Should().Equal("A — Start — Alpha", "Route details unavailable", "B — End — Charlie");
        details.WaypointCountText.Should().Be("Waypoint count unavailable");
        details.RoutePointCountText.Should().Be("Route points: 3");
    }

    [Fact]
    public void DtoAndOfflineEmptyCollections_DoNotRecoverLostAvailability()
    {
        var dto = JsonSerializer.Deserialize<TripSegment>("""{"waypoints":[],"routeJson":"????"}""")!;
        var offline = new TripSegment { Geometry = dto.Geometry, Waypoints = SegmentWaypointJson.Deserialize(SegmentWaypointJson.Serialize(dto.Waypoints)) };

        foreach (var segment in new[] { dto, offline })
        {
            var details = SegmentPresentationProjector.Project(segment, []);
            details.Trail.Should().Equal("A — Start — Place unavailable", "B — End — Place unavailable");
            details.WaypointCountText.Should().Be("Waypoint count unavailable");
            details.RoutePointCountText.Should().Be("Route points: 2");
        }
    }
}
