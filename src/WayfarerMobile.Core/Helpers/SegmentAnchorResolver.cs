using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Helpers;

public readonly record struct SegmentCoordinate(double Latitude, double Longitude);

public enum SegmentAnchorFailure
{
    MissingPlace,
    DuplicateIdentity,
    InvalidPosition,
    InvalidLocation,
    InvalidRouteIndex,
    CoordinateIndexMismatch,
    MalformedGeometry
}

public sealed record ResolvedSegmentAnchor(
    int SemanticPosition,
    string Label,
    string Role,
    Guid PlaceId,
    string PlaceName,
    double Latitude,
    double Longitude,
    int? RouteVertexIndex);

public sealed record SegmentAnchorResolution(
    IReadOnlyList<ResolvedSegmentAnchor> Anchors,
    IReadOnlyList<SegmentCoordinate> Geometry,
    SegmentAnchorFailure? Failure)
{
    public bool IsValid => Failure is null;
    public IReadOnlyList<string> TextTrail => IsValid
        ? Anchors.Select(anchor => $"{anchor.Label} — {anchor.Role} — {anchor.PlaceName}").ToList()
        : Array.Empty<string>();
}

/// <summary>Resolves the single trusted semantic anchor sequence for a Segment.</summary>
public static class SegmentAnchorResolver
{
    private const double CoordinateTolerance = 0.0000001;

    public static SegmentAnchorResolution Resolve(
        TripSegment segment,
        IReadOnlyCollection<TripPlace> places,
        IReadOnlyList<SegmentCoordinate>? effectiveGeometry = null)
    {
        if (!segment.OriginId.HasValue || !segment.DestinationId.HasValue)
            return Fail(SegmentAnchorFailure.MissingPlace);

        var waypoints = segment.Waypoints ?? new List<TripSegmentWaypoint>();
        if (waypoints.Select(w => w.Position).Distinct().Count() != waypoints.Count ||
            waypoints.Where((waypoint, index) => waypoint.Position != index).Any())
            return Fail(SegmentAnchorFailure.InvalidPosition);

        var placeLookup = places.GroupBy(place => place.Id).ToDictionary(group => group.Key, group => group.First());
        var identities = new[] { segment.OriginId.Value }
            .Concat(waypoints.Select(waypoint => waypoint.PlaceId))
            .Append(segment.DestinationId.Value)
            .ToList();

        if (waypoints.Select(w => w.PlaceId).Distinct().Count() != waypoints.Count)
            return Fail(SegmentAnchorFailure.DuplicateIdentity);
        if (waypoints.Any(waypoint => waypoint.PlaceId == segment.OriginId || waypoint.PlaceId == segment.DestinationId))
            return Fail(SegmentAnchorFailure.DuplicateIdentity);

        var anchorPlaces = new List<TripPlace>(identities.Count);
        foreach (var identity in identities)
        {
            if (!placeLookup.TryGetValue(identity, out var place))
                return Fail(SegmentAnchorFailure.MissingPlace);
            if (!IsValidLocation(place.Latitude, place.Longitude))
                return Fail(SegmentAnchorFailure.InvalidLocation);
            anchorPlaces.Add(place);
        }

        if (segment.HasCustomRoute && effectiveGeometry is null)
            return Fail(SegmentAnchorFailure.MalformedGeometry);
        var geometry = effectiveGeometry?.ToList();
        if (geometry is null)
            geometry = anchorPlaces.Select(place => new SegmentCoordinate(place.Latitude, place.Longitude)).ToList();
        if (geometry.Count < 2 || geometry.Any(point => !IsValidLocation(point.Latitude, point.Longitude)))
            return Fail(SegmentAnchorFailure.MalformedGeometry);

        var indices = new int[anchorPlaces.Count];
        indices[0] = 0;
        indices[^1] = geometry.Count - 1;
        for (var index = 0; index < waypoints.Count; index++)
        {
            var routeIndex = waypoints[index].RouteVertexIndex;
            if (routeIndex is null)
            {
                if (segment.HasCustomRoute || geometry.Count != anchorPlaces.Count)
                    return Fail(SegmentAnchorFailure.InvalidRouteIndex);
                routeIndex = index + 1;
            }
            indices[index + 1] = routeIndex.Value;
        }

        for (var index = 0; index < indices.Length; index++)
        {
            var routeIndex = indices[index];
            if (routeIndex < 0 || routeIndex >= geometry.Count ||
                (index > 0 && routeIndex <= indices[index - 1]))
                return Fail(SegmentAnchorFailure.InvalidRouteIndex);
            var point = geometry[routeIndex];
            var place = anchorPlaces[index];
            if (Math.Abs(point.Latitude - place.Latitude) > CoordinateTolerance ||
                Math.Abs(point.Longitude - place.Longitude) > CoordinateTolerance)
                return Fail(SegmentAnchorFailure.CoordinateIndexMismatch);
        }

        var anchors = anchorPlaces.Select((place, index) => new ResolvedSegmentAnchor(
            index,
            GetLabel(index),
            index == 0 ? "Start" : index == anchorPlaces.Count - 1 ? "End" : $"Via {index}",
            place.Id,
            place.Name ?? string.Empty,
            place.Latitude,
            place.Longitude,
            indices[index])).ToList();
        return new SegmentAnchorResolution(anchors, geometry, null);
    }

    public static string GetLabel(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0) throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));
        var value = zeroBasedIndex + 1;
        var characters = new Stack<char>();
        while (value > 0)
        {
            value--;
            characters.Push((char)('A' + value % 26));
            value /= 26;
        }
        return new string(characters.ToArray());
    }

    private static bool IsValidLocation(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static SegmentAnchorResolution Fail(SegmentAnchorFailure failure) =>
        new(Array.Empty<ResolvedSegmentAnchor>(), Array.Empty<SegmentCoordinate>(), failure);
}
