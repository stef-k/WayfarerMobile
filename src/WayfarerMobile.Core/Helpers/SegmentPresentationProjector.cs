using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

/// <summary>Drawer text only; counts and trail do not establish route navigability.</summary>
public sealed record SegmentDetailsPresentation(
    IReadOnlyList<string> Trail, string WaypointCountText, string RoutePointCountText);

public static class SegmentPresentationProjector
{
    public const string UnavailableMessage = "Route details unavailable";

    public static SegmentDetailsPresentation Project(TripSegment segment, IReadOnlyCollection<TripPlace> places)
    {
        var parsed = TripSegmentGeometryParser.Parse(segment.Geometry);
        IReadOnlyList<SegmentCoordinate>? geometry = parsed.IsSuccess
            ? parsed.Coordinates.Select(point => new SegmentCoordinate(point.Latitude, point.Longitude)).ToList()
            : parsed.Failure == SegmentGeometryFailure.Empty ? null : Array.Empty<SegmentCoordinate>();
        var resolution = ResolveWaypoints(segment, places, geometry);
        return new(BuildTrail(segment, places, resolution),
            resolution?.IsValid == true ? $"Waypoints: {segment.Waypoints.Count}" : "Waypoint count unavailable",
            parsed.IsSuccess ? $"Route points: {parsed.Coordinates.Count}" : "Route points unavailable");
    }

    public static IReadOnlyList<string> CreateTrail(
        TripSegment segment,
        IReadOnlyCollection<TripPlace> places,
        IReadOnlyList<SegmentCoordinate>? geometry) =>
        BuildTrail(segment, places, ResolveWaypoints(segment, places, geometry));

    private static SegmentAnchorResolution? ResolveWaypoints(
        TripSegment segment, IReadOnlyCollection<TripPlace> places, IReadOnlyList<SegmentCoordinate>? geometry) =>
        segment.HasWaypoints && !segment.Waypoints.Any(waypoint => waypoint is null)
            ? SegmentAnchorResolver.Resolve(segment, places, geometry)
            : null;

    private static IReadOnlyList<string> BuildTrail(
        TripSegment segment, IReadOnlyCollection<TripPlace> places, SegmentAnchorResolution? resolution)
    {
        if (resolution?.IsValid == true)
            return resolution.Anchors.Select(anchor =>
                $"{anchor.Label} — {anchor.Role} — {DisplayName(anchor.PlaceName)}").ToList();

        // Endpoint text is independent of geometry and intermediate anchor validity.
        var start = EndpointName(segment.OriginId, segment.OriginName, places);
        var end = EndpointName(segment.DestinationId, segment.DestinationName, places);
        return segment.HasWaypoints
            ? [$"A — Start — {start}", UnavailableMessage, $"B — End — {end}"]
            : [$"A — Start — {start}", $"B — End — {end}"];
    }

    private static string EndpointName(Guid? identity, string? name, IReadOnlyCollection<TripPlace> places) =>
        identity is null || identity == Guid.Empty
            ? "Place unavailable"
            : DisplayName(places.FirstOrDefault(place => place.Id == identity)?.Name ?? name);

    private static string DisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Place unavailable" : name;

    public static TripSegment? PrepareTripReplacement(TripDetails trip, TripSegment? selectedSegment)
    {
        foreach (var segment in trip.Segments)
        {
            var origin = trip.AllPlaces.FirstOrDefault(place => place.Id == segment.OriginId);
            var destination = trip.AllPlaces.FirstOrDefault(place => place.Id == segment.DestinationId);
            segment.OriginName ??= origin?.Name;
            segment.DestinationName ??= destination?.Name;
            segment.AnchorTrail = Project(segment, trip.AllPlaces).Trail;
        }

        return selectedSegment == null
            ? null
            : trip.Segments.FirstOrDefault(segment => segment.Id == selectedSegment.Id);
    }
}
