using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

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
            var edge = new NavigationEdge
            {
                FromNodeId = (segment.OriginId ?? Guid.Empty).ToString(),
                ToNodeId = (segment.DestinationId ?? Guid.Empty).ToString(),
                TransportMode = segment.TransportMode ?? "walking",
                DistanceKm = segment.DistanceKm ?? 0,
                DurationMinutes = (int)(segment.DurationMinutes ?? 0),
                EdgeType = NavigationEdgeType.UserSegment
            };

            var parseResult = TripSegmentGeometryParser.Parse(segment.Geometry);
            if (parseResult.IsSuccess)
            {
                edge.RouteGeometry = parseResult.Coordinates
                    .Select(point => new RoutePoint
                    {
                        Latitude = point.Latitude,
                        Longitude = point.Longitude
                    })
                    .ToList();
            }
            else if (parseResult.Failure != SegmentGeometryFailure.Empty)
            {
                geometryFailure?.Invoke(segment.Id, parseResult.Failure!.Value);
            }

            graph.AddEdge(edge);
        }

        return graph;
    }
}
