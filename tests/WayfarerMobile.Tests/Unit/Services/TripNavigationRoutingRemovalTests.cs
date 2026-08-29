using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using CoreTripPlace = WayfarerMobile.Core.Models.TripPlace;
using CoreTripSegment = WayfarerMobile.Core.Models.TripSegment;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class TripNavigationRoutingRemovalTests
{
    [Fact]
    public async Task MapTargetNavigation_DoesNotContactPublicProvider_AndUsesDirectGuidance()
    {
        var navigation = CreateNavigation();

        var route = await navigation.CalculateRouteToCoordinatesAsync(
            37.9838, 23.7275,
            37.9715, 23.7267,
            "Map target");

        route.IsDirectRoute.Should().BeTrue();
        route.Waypoints.Should().HaveCount(2);
    }

    [Fact]
    public void TripPlaceNavigation_UsesSavedSegmentGeometryInOrder()
    {
        var origin = new CoreTripPlace { Id = Guid.NewGuid(), Name = "Origin", Latitude = 37.98, Longitude = 23.72, SortOrder = 0 };
        var destination = new CoreTripPlace { Id = Guid.NewGuid(), Name = "Destination", Latitude = 38.00, Longitude = 23.74, SortOrder = 1 };
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Saved route",
            Regions = [new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = [origin, destination] }],
            Segments =
            [
                new CoreTripSegment
                {
                    Id = Guid.NewGuid(),
                    OriginId = origin.Id,
                    DestinationId = destination.Id,
                    TransportMode = "walking",
                    Geometry = """{"type":"LineString","coordinates":[[23.72,37.98],[23.73,37.99],[23.74,38.00]]}"""
                }
            ]
        };
        var state = new Mock<ITripStateManager>();
        state.SetupGet(service => service.LoadedTrip).Returns(trip);
        var navigation = CreateNavigation(state.Object);

        navigation.LoadTrip(trip).Should().BeTrue();
        var route = navigation.CalculateRouteToPlace(origin.Latitude, origin.Longitude, destination.Id.ToString());

        route.Should().NotBeNull();
        route!.IsDirectRoute.Should().BeFalse();
        route.Waypoints.Select(point => (point.Latitude, point.Longitude)).Should().ContainInOrder(
            (37.98, 23.72),
            (37.99, 23.73),
            (38.00, 23.74));
    }

    private static TripNavigationService CreateNavigation(ITripStateManager? state = null) =>
        new(
            NullLogger<TripNavigationService>.Instance,
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            state ?? Mock.Of<ITripStateManager>());
}
