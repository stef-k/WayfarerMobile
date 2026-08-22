using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

public static class SegmentPresentationProjector
{
    public const string UnavailableMessage = "Route details unavailable";

    public static IReadOnlyList<string> CreateTrail(
        TripSegment segment,
        IReadOnlyCollection<TripPlace> places,
        IReadOnlyList<SegmentCoordinate>? geometry)
    {
        if (!segment.HasWaypoints) return Array.Empty<string>();
        var resolution = SegmentAnchorResolver.Resolve(segment, places, geometry);
        return resolution.IsValid ? resolution.TextTrail : [UnavailableMessage];
    }
}
