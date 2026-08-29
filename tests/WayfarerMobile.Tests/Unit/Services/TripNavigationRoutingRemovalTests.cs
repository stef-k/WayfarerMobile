using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using CoreTripPlace = WayfarerMobile.Core.Models.TripPlace;
using CoreTripSegment = WayfarerMobile.Core.Models.TripSegment;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class TripNavigationRoutingRemovalTests
{
    [Fact]
    public async Task AdHocDirectRoute_UpdatesProgressAnnouncesAndArrivesWithoutTripGraph()
    {
        var audio = new Mock<INavigationAudioService>();
        var navigation = CreateNavigation(audio: audio.Object);
        var publishedStates = new List<TripNavigationState>();
        var announcements = new List<string>();
        var rerouted = false;
        navigation.StateChanged += (_, state) => publishedStates.Add(state);
        navigation.InstructionAnnounced += (_, instruction) => announcements.Add(instruction);
        navigation.Rerouted += (_, _) => rerouted = true;

        var route = await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0.001, 0, "Map target");

        var progressing = navigation.UpdateLocation(0.0004, 0);
        var arrived = navigation.UpdateLocation(0.001, 0);

        route.IsDirectRoute.Should().BeTrue();
        progressing.Status.Should().Be(NavigationStatus.OnRoute);
        progressing.DistanceToDestinationMeters.Should().BePositive();
        progressing.DistanceToNextWaypointMeters.Should().BePositive();
        progressing.EstimatedTimeRemaining.Should().BePositive();
        progressing.ProgressPercent.Should().BeGreaterThan(0);
        arrived.Status.Should().Be(NavigationStatus.Arrived);
        publishedStates.Select(state => state.Status).Should().ContainInOrder(
            NavigationStatus.OnRoute,
            NavigationStatus.Arrived);
        announcements.Should().ContainSingle();
        audio.Verify(service => service.AnnounceStepInstructionAsync(
            It.IsAny<string>(), It.Is<double>(distance => distance > 0)), Times.Once);
        rerouted.Should().BeFalse();
    }

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

    [Fact]
    public void TripPlaceNavigation_InvalidSavedGeometry_FallsBackToExplicitDirectRoute()
    {
        var (trip, origin, destination) = CreateTrip("{not json");
        var state = new Mock<ITripStateManager>();
        state.SetupGet(service => service.LoadedTrip).Returns(trip);
        var navigation = CreateNavigation(state.Object);

        navigation.LoadTrip(trip).Should().BeTrue();
        var route = navigation.CalculateRouteToPlace(
            origin.Latitude, origin.Longitude, destination.Id.ToString());

        route.Should().NotBeNull();
        route!.IsDirectRoute.Should().BeTrue();
        route.Waypoints.Should().HaveCount(2);
    }

    [Fact]
    public void TripPlaceNavigation_WithoutSavedPath_ReturnsExplicitDirectRoute()
    {
        var (trip, origin, destination) = CreateTrip(geometry: null, includeSegment: false);
        var state = new Mock<ITripStateManager>();
        state.SetupGet(service => service.LoadedTrip).Returns(trip);
        var navigation = CreateNavigation(state.Object);

        navigation.LoadTrip(trip).Should().BeTrue();
        var route = navigation.CalculateRouteToPlace(
            origin.Latitude, origin.Longitude, destination.Id.ToString());

        route.Should().NotBeNull();
        route!.IsDirectRoute.Should().BeTrue();
    }

    private static TripNavigationService CreateNavigation(
        ITripStateManager? state = null,
        INavigationAudioService? audio = null) =>
        new(
            NullLogger<TripNavigationService>.Instance,
            audio ?? Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            state ?? Mock.Of<ITripStateManager>());

    private static (TripDetails Trip, CoreTripPlace Origin, CoreTripPlace Destination) CreateTrip(
        string? geometry,
        bool includeSegment = true)
    {
        var origin = new CoreTripPlace
        {
            Id = Guid.NewGuid(), Name = "Origin", Latitude = 37.98, Longitude = 23.72, SortOrder = 0
        };
        var destination = new CoreTripPlace
        {
            Id = Guid.NewGuid(), Name = "Destination", Latitude = 38.00, Longitude = 23.74, SortOrder = 1
        };
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Fallback route",
            Regions = [new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = [origin, destination] }]
        };
        if (includeSegment)
        {
            trip.Segments =
            [
                new CoreTripSegment
                {
                    Id = Guid.NewGuid(),
                    OriginId = origin.Id,
                    DestinationId = destination.Id,
                    Geometry = geometry
                }
            ];
        }

        return (trip, origin, destination);
    }
}
