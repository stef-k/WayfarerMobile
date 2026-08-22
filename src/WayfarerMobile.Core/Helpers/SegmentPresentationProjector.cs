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

    public static TripSegment? PrepareTripReplacement(TripDetails trip, TripSegment? selectedSegment)
    {
        foreach (var segment in trip.Segments)
        {
            var origin = trip.AllPlaces.FirstOrDefault(place => place.Id == segment.OriginId);
            var destination = trip.AllPlaces.FirstOrDefault(place => place.Id == segment.DestinationId);
            segment.OriginName ??= origin?.Name;
            segment.DestinationName ??= destination?.Name;
            segment.AnchorTrail = CreateTrail(segment, trip.AllPlaces, ParseGeometry(segment));
        }

        return selectedSegment == null
            ? null
            : trip.Segments.FirstOrDefault(segment => segment.Id == selectedSegment.Id);
    }

    private static IReadOnlyList<SegmentCoordinate>? ParseGeometry(TripSegment segment)
    {
        var parsed = TripSegmentGeometryParser.Parse(segment.Geometry);
        return parsed.IsSuccess
            ? parsed.Coordinates.Select(point => new SegmentCoordinate(point.Latitude, point.Longitude)).ToList()
            : parsed.Failure == SegmentGeometryFailure.Empty ? null : Array.Empty<SegmentCoordinate>();
    }
}
