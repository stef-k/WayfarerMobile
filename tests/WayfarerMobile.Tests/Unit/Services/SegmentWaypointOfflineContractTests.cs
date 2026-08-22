using System.Text.Json;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Tests.Unit.Services;

public class SegmentWaypointOfflineContractTests
{
    [Fact]
    public void OfflineWaypointJson_RoundTripsOrderIndicesAndNulls()
    {
        var waypoints = new List<TripSegmentWaypoint>
        {
            new() { PlaceId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Position = 0, RouteVertexIndex = 2 },
            new() { PlaceId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Position = 1, RouteVertexIndex = null }
        };

        var entity = new OfflineSegmentEntity { WaypointsJson = JsonSerializer.Serialize(waypoints) };
        var restored = JsonSerializer.Deserialize<List<TripSegmentWaypoint>>(entity.WaypointsJson!);

        restored.Should().BeEquivalentTo(waypoints, options => options.WithStrictOrdering());
        restored![1].RouteVertexIndex.Should().BeNull();
    }

    [Fact]
    public void LegacyOfflineRowWithoutWaypointJson_ReconstructsEmptyWaypoints()
    {
        var entity = new OfflineSegmentEntity { WaypointsJson = null };

        SegmentWaypointJson.Deserialize(entity.WaypointsJson).Should().BeEmpty();
    }
}
