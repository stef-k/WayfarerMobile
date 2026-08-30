using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Algorithms;

namespace WayfarerMobile.Core.Navigation;

/// <summary>
/// Builds the local navigation graph from transport trip data.
/// </summary>
public static class TripNavigationGraphBuilder
{
    public static TripNavigationGraph Build(
        TripDetails trip,
        Action<Guid, SegmentGeometryFailure>? geometryFailure = null)
    {
        var graph = new TripNavigationGraph { TripId = trip.Id };

        foreach (var region in trip.Regions)
        {
            foreach (var place in region.Places)
            {
                graph.AddNode(new NavigationNode
                {
                    Id = place.Id.ToString(),
                    Name = place.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    Type = NavigationNodeType.Place,
                    SortOrder = place.SortOrder,
                    Notes = place.Notes,
                    IconName = place.Icon
                });
            }
        }

        foreach (var segment in trip.Segments)
        {
            var parseResult = TripSegmentGeometryParser.Parse(segment.Geometry);
            if (!parseResult.IsSuccess)
            {
                if (parseResult.Failure != SegmentGeometryFailure.Empty)
                    geometryFailure?.Invoke(segment.Id, parseResult.Failure!.Value);
                continue;
            }

            if (segment.Waypoints.Count == 0)
            {
                var edge = CreateEdge(segment, segment.OriginId, segment.DestinationId,
                    segment.DistanceKm ?? 0, (int)(segment.DurationMinutes ?? 0));
                edge.RouteGeometry = ToRoutePoints(parseResult.Coordinates);
                graph.AddEdge(edge);
                continue;
            }

            IReadOnlyList<SegmentCoordinate> parsedGeometry = parseResult.Coordinates
                .Select(point => new SegmentCoordinate(point.Latitude, point.Longitude)).ToList();

            var resolution = SegmentAnchorResolver.Resolve(segment, trip.AllPlaces, parsedGeometry);
            if (!resolution.IsValid) continue;

            var slices = new List<List<RoutePoint>>();
            var distances = new List<double>();
            for (var index = 1; index < resolution.Anchors.Count; index++)
            {
                var start = resolution.Anchors[index - 1].RouteVertexIndex!.Value;
                var end = resolution.Anchors[index].RouteVertexIndex!.Value;
                var slice = resolution.Geometry.Skip(start).Take(end - start + 1)
                    .Select(point => new RoutePoint { Latitude = point.Latitude, Longitude = point.Longitude }).ToList();
                slices.Add(slice);
                distances.Add(CalculateDistanceKm(slice));
            }

            var durations = AllocateDuration((int)(segment.DurationMinutes ?? 0), distances);
            for (var index = 0; index < slices.Count; index++)
            {
                var edge = CreateEdge(segment, resolution.Anchors[index].PlaceId,
                    resolution.Anchors[index + 1].PlaceId, distances[index], durations[index]);
                edge.RouteGeometry = slices[index];
                graph.AddEdge(edge);
            }
        }

        return graph;
    }

    private static NavigationEdge CreateEdge(TripSegment segment, Guid? from, Guid? to, double distance, int duration) => new()
    {
        ParentSegmentId = segment.Id,
        FromNodeId = (from ?? Guid.Empty).ToString(),
        ToNodeId = (to ?? Guid.Empty).ToString(),
        TransportMode = segment.TransportMode ?? "walking",
        DistanceKm = distance,
        DurationMinutes = duration,
        UserNotes = segment.Notes,
        EdgeType = NavigationEdgeType.UserSegment
    };

    private static List<RoutePoint> ToRoutePoints(IEnumerable<(double Latitude, double Longitude)> points) =>
        points.Select(point => new RoutePoint { Latitude = point.Latitude, Longitude = point.Longitude }).ToList();

    private static double CalculateDistanceKm(IReadOnlyList<RoutePoint> points)
    {
        var meters = 0d;
        for (var index = 1; index < points.Count; index++)
            meters += GeoMath.CalculateDistance(points[index - 1].Latitude, points[index - 1].Longitude,
                points[index].Latitude, points[index].Longitude);
        return meters / 1000d;
    }

    /// <summary>
    /// Allocates whole parent minutes by distance. Floors each share, then assigns remaining
    /// minutes by descending fractional remainder and semantic order, preserving the exact total.
    /// </summary>
    private static int[] AllocateDuration(int totalMinutes, IReadOnlyList<double> distances)
    {
        var result = new int[distances.Count];
        if (totalMinutes <= 0 || distances.Count == 0) return result;
        var totalDistance = distances.Sum();
        if (totalDistance <= 0)
        {
            result[0] = totalMinutes;
            return result;
        }
        var shares = distances.Select(distance => totalMinutes * distance / totalDistance).ToArray();
        for (var index = 0; index < shares.Length; index++) result[index] = (int)Math.Floor(shares[index]);
        var remaining = totalMinutes - result.Sum();
        foreach (var index in Enumerable.Range(0, shares.Length)
                     .OrderByDescending(index => shares[index] - result[index]).ThenBy(index => index).Take(remaining))
            result[index]++;
        return result;
    }
}
