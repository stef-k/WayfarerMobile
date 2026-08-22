using System.Text.Json;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Models;

public class TripSegmentWaypointContractTests
{
    [Fact]
    public void BackendShapedSegment_DeserializesOrderedWaypointsAndCustomRouteFlag()
    {
        var fromId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var viaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var toId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var json = $$"""
            {
              "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "fromPlaceId": "{{fromId}}",
              "toPlaceId": "{{toId}}",
              "routeJson": "{\"type\":\"LineString\",\"coordinates\":[[23.70,37.90],[23.71,37.91],[23.72,37.92]]}",
              "hasCustomRoute": true,
              "waypoints": [
                { "placeId": "{{viaId}}", "position": 0, "routeVertexIndex": 1, "futureField": "ignored" }
              ],
              "futureSegmentField": 42
            }
            """;

        var segment = JsonSerializer.Deserialize<TripSegment>(json)!;

        segment.OriginId.Should().Be(fromId);
        segment.DestinationId.Should().Be(toId);
        segment.HasCustomRoute.Should().BeTrue();
        segment.Waypoints.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TripSegmentWaypoint { PlaceId = viaId, Position = 0, RouteVertexIndex = 1 });
    }

    [Fact]
    public void LegacySegmentWithoutWaypoints_UsesEmptyCollection()
    {
        var segment = JsonSerializer.Deserialize<TripSegment>("""{"fromPlaceId":null,"toPlaceId":null,"routeJson":null}""")!;

        segment.Waypoints.Should().NotBeNull().And.BeEmpty();
    }
}
