using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
    public async Task ReplacingRoute_StartsFreshAnnouncementSession()
    {
        var navigation = CreateNavigation();
        var announcements = new List<string>();
        navigation.InstructionAnnounced += (_, instruction) => announcements.Add(instruction);

        await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0.001, 0, "Route A");
        navigation.UpdateLocation(0.0002, 0);
        await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0, 0.001, "Route B");

        navigation.UpdateLocation(0, 0.0002);

        announcements.Should().HaveCount(2);
    }

    [Fact]
    public async Task StopNavigation_ClearsRouteAndPreventsFurtherUpdates()
    {
        var navigation = CreateNavigation();
        var publishedStates = new List<TripNavigationState>();
        var announcements = new List<string>();
        navigation.StateChanged += (_, state) => publishedStates.Add(state);
        navigation.InstructionAnnounced += (_, instruction) => announcements.Add(instruction);
        await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0.001, 0, "Destination");
        navigation.UpdateLocation(0.0002, 0);
        var stateCountAtStop = publishedStates.Count;
        var announcementCountAtStop = announcements.Count;

        navigation.StopNavigation();
        var afterStop = navigation.UpdateLocation(0.0003, 0);

        navigation.ActiveRoute.Should().BeNull();
        afterStop.Status.Should().Be(NavigationStatus.NoRoute);
        publishedStates.Should().HaveCount(stateCountAtStop);
        announcements.Should().HaveCount(announcementCountAtStop);
    }

    [Fact]
    public async Task HostedProvenance_ClearsThroughNormalReplacementAndStop()
    {
        var navigation = CreateNavigation();
        var first = await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0.001, 0, "Hosted");
        first.HostedProvenance = Provenance();

        var replacement = await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0, 0.001, "Direct");

        navigation.ActiveRoute.Should().BeSameAs(replacement);
        replacement.HostedProvenance.Should().BeNull();
        navigation.StopNavigation();
        navigation.ActiveRoute.Should().BeNull();
    }

    [Fact]
    public async Task Arrival_PublishesCompletionThenClearsRoute()
    {
        var navigation = CreateNavigation();
        var publishedStates = new List<TripNavigationState>();
        navigation.StateChanged += (_, state) => publishedStates.Add(state);
        await navigation.CalculateRouteToCoordinatesAsync(0, 0, 0.001, 0, "Destination");

        var arrived = navigation.UpdateLocation(0.001, 0);
        var stateCountAtArrival = publishedStates.Count;
        var afterArrival = navigation.UpdateLocation(0.0009, 0);

        arrived.Status.Should().Be(NavigationStatus.Arrived);
        publishedStates.Should().ContainSingle(state => state.Status == NavigationStatus.Arrived);
        navigation.ActiveRoute.Should().BeNull();
        afterArrival.Status.Should().Be(NavigationStatus.NoRoute);
        publishedStates.Should().HaveCount(stateCountAtArrival);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TripPlaceNavigation_UnavailableSavedGeometry_FallsBackToExplicitDirectRoute(string? geometry)
    {
        var (trip, origin, destination) = CreateTrip(geometry);
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

    [Fact]
    public void NextPlace_CoLocatedPlacesResolveExactSelectedPlaceSegmentAndAnchors()
    {
        var origin = Place("Origin", 0, 0, 0);
        var selected = Place("Selected", 0.01, 0.01, 1);
        var colocated = Place("Co-located", 0.01, 0.01, 2);
        var selectedAnchor = Place("Selected anchor", 0.005, 0.006, 10);
        var otherAnchor = Place("Other anchor", 0.007, 0.008, 11);
        var selectedProfile = Guid.NewGuid();
        var selectedSegment = Segment(origin.Id, selected.Id, selectedAnchor.Id, selectedProfile);
        var otherSegment = Segment(origin.Id, colocated.Id, otherAnchor.Id, Guid.NewGuid());
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Co-located targets",
            Regions = [new TripRegion
            {
                Id = Guid.NewGuid(), Name = "Region",
                Places = [origin, selected, colocated, selectedAnchor, otherAnchor]
            }],
            Segments = [selectedSegment, otherSegment]
        };
        var state = new Mock<ITripStateManager>();
        state.SetupGet(service => service.LoadedTrip).Returns(trip);
        var navigation = CreateNavigation(state.Object);
        navigation.LoadTrip(trip).Should().BeTrue();

        var route = navigation.CalculateRouteToNextPlace(origin.Latitude, origin.Longitude);
        var destinationId = Guid.Parse(route!.Waypoints[^1].PlaceId!);
        var authority = HostedTripTargetAuthority.Resolve(
            trip, destinationId, origin.Latitude, origin.Longitude);

        destinationId.Should().Be(selected.Id);
        authority.Should().NotBeNull();
        authority!.DestinationPlaceId.Should().Be(selected.Id);
        authority.SegmentId.Should().Be(selectedSegment.Id);
        authority.SavedTransportProfileId.Should().Be(selectedProfile);
        authority.Anchors.Should().Equal(new HostedRouteCoordinate(
            selectedAnchor.Longitude, selectedAnchor.Latitude));
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

    private static CoreTripPlace Place(string name, double latitude, double longitude, int sortOrder) => new()
    {
        Id = Guid.NewGuid(), Name = name, Latitude = latitude, Longitude = longitude, SortOrder = sortOrder
    };

    private static CoreTripSegment Segment(Guid originId, Guid destinationId, Guid anchorId, Guid profileId)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(HostedSegmentProfileIdentity.Configure);
        var segment = JsonSerializer.Deserialize<CoreTripSegment>(
            $$"""{"id":"{{Guid.NewGuid()}}","fromPlaceId":"{{originId}}","toPlaceId":"{{destinationId}}","mode":"walking","transportProfileId":"{{profileId}}","waypoints":[{"placeId":"{{anchorId}}","position":0}]}""",
            new JsonSerializerOptions { TypeInfoResolver = resolver });
        return segment!;
    }

    private static HostedRouteProvenance Provenance() => new(
        Guid.NewGuid(),
        "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        "geoapify",
        Guid.NewGuid(),
        "mapping",
        "persistent",
        DateTimeOffset.UtcNow);
}
