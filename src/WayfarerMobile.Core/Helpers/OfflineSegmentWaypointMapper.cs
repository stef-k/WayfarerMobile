using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

public static class OfflineSegmentWaypointMapper
{
    public static TripSegment Reconstruct(
        Guid segmentId,
        Guid originId,
        Guid destinationId,
        string? geometry,
        string? waypointsJson,
        bool hasCustomRoute) => new()
    {
        Id = segmentId,
        OriginId = originId,
        DestinationId = destinationId,
        Geometry = geometry,
        Waypoints = SegmentWaypointJson.Deserialize(waypointsJson),
        HasCustomRoute = hasCustomRoute
    };
}
