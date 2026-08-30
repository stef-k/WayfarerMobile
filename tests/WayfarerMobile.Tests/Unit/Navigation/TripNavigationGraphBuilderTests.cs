using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Core.Navigation;

namespace WayfarerMobile.Tests.Unit.Navigation;

public class TripNavigationGraphBuilderTests
{
    [Fact]
    public void Build_ApiGeoJsonSegment_RetainsEdgeWithExactRouteGeometry()
    {
        var (trip, fromId, toId) = CreateTrip(
            "{\"type\":\"LineString\",\"coordinates\":[[23.7275,37.9838],[23.7281,37.9844]]}");

        var graph = TripNavigationGraphBuilder.Build(trip);

        var edge = graph.GetEdgeBetween(fromId.ToString(), toId.ToString());
        edge.Should().NotBeNull();
        edge!.RouteGeometry.Should().BeEquivalentTo(
            [
                new RoutePoint { Latitude = 37.9838, Longitude = 23.7275 },
                new RoutePoint { Latitude = 37.9844, Longitude = 23.7281 }
            ],
            options => options.WithStrictOrdering());

        var corruptPoints = PolylineDecoder.Decode(trip.Segments.Single().Geometry!);
        edge.RouteGeometry.Should().NotBeEquivalentTo(corruptPoints);
    }

    private static (TripDetails Trip, Guid FromId, Guid ToId) CreateTrip(string geometry)
    {
        var fromId = Guid.NewGuid();
        var toId = Guid.NewGuid();
        return (
            new TripDetails
            {
                Id = Guid.NewGuid(),
                Regions =
                [
                    new TripRegion
                    {
                        Places =
                        [
                            new TripPlace { Id = fromId, Name = "From", Latitude = 37.9838, Longitude = 23.7275 },
                            new TripPlace { Id = toId, Name = "To", Latitude = 37.9844, Longitude = 23.7281 }
                        ]
                    }
                ],
                Segments =
                [
                    new TripSegment
                    {
                        Id = Guid.NewGuid(),
                        OriginId = fromId,
                        DestinationId = toId,
                        Geometry = geometry
                    }
                ]
            },
            fromId,
            toId);
    }
}
