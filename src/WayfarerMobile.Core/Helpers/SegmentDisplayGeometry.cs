using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

/// <summary>Resolves the approved transient endpoint connection for map presentation only.</summary>
public static class SegmentDisplayGeometry
{
    public const string StraightConnectionLabel = "Straight endpoint connection — route geometry unavailable";

    public static SegmentAnchorResolution? ResolveStraightConnection(
        TripSegment segment, IReadOnlyCollection<TripPlace> places)
    {
        // Empty waypoint data does not certify completeness. Never repair rejected geometry
        // or feed these derived display vertices into persistence, counts or navigation.
        if (!string.IsNullOrWhiteSpace(segment.Geometry) || segment.HasCustomRoute || segment.HasWaypoints ||
            segment.OriginId is null || segment.OriginId == Guid.Empty ||
            segment.DestinationId is null || segment.DestinationId == Guid.Empty)
            return null;

        var resolution = SegmentAnchorResolver.Resolve(segment, places);
        return resolution.IsValid ? resolution : null;
    }
}
