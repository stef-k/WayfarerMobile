using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed class NavigationCoordinatorHostedRoutingTests
{
    private static readonly Guid WalkingProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HikingProfile = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string IdentityA = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string IdentityB = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ";

    [Fact]
    public async Task OpenChooser_CatalogChanges_SubmitsDisplayedIdentityThenRefreshesBeforeReselection()
    {
        var catalogA = Catalog(IdentityA,
            new(WalkingProfile, "Walking", "walk", "active"),
            new(HikingProfile, "Hiking", "walk", "outdoors"));
        var catalogB = Catalog(IdentityB,
            new(WalkingProfile, "On foot", "walk", "active"),
            new(HikingProfile, "Trail", "walk", "outdoors"));
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.SetupSequence(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogA)
            .ReturnsAsync(catalogB);
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", WalkingProfile,
                null, null, null, null, null, null, null));
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(
                WalkingProfile, IdentityB, IdentityB, Attribution()));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityB));
        var presentations = new List<IReadOnlyList<string>>();
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync("Wayfarer routing profile",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .Callback<string, IReadOnlyList<string>, string>((_, choices, _) => presentations.Add(choices))
            .ReturnsAsync(() => presentations.Count == 1
                ? $"Walking — walk ({WalkingProfile:D})"
                : null);
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeTrue();
        route.HostedProvenance.Should().BeNull();
        presentations.Should().HaveCount(2);
        presentations[0].Should().ContainSingle(choice => choice.StartsWith("Walking —", StringComparison.Ordinal));
        presentations[1].Should().ContainSingle(choice => choice.StartsWith("On foot —", StringComparison.Ordinal));
        api.Verify(client => client.GetCapabilityAsync(WalkingProfile, IdentityA,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), IdentityB,
            It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenChooser_RepeatedCatalogChange_RefreshesOnlyOnceAndRetainsDirect()
    {
        var catalogA = Catalog(IdentityA,
            new(WalkingProfile, "Walking", "walk", "active"),
            new(HikingProfile, "Hiking", "walk", "outdoors"));
        var catalogB = Catalog(IdentityB,
            new HostedRoutingProfile(WalkingProfile, "On foot", "walk", "active"));
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.SetupSequence(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogA)
            .ReturnsAsync(catalogB);
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", WalkingProfile,
                null, null, null, null, null, null, null));
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", WalkingProfile,
                null, null, null, null, null, null, null));
        var presentations = new List<IReadOnlyList<string>>();
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync("Wayfarer routing profile",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .Callback<string, IReadOnlyList<string>, string>((_, choices, _) => presentations.Add(choices))
            .ReturnsAsync(() => presentations.Count == 1
                ? $"Walking — walk ({WalkingProfile:D})"
                : $"On foot — walk ({WalkingProfile:D})");
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeTrue();
        presentations.Should().HaveCount(2);
        api.Verify(client => client.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        api.Verify(client => client.GetCapabilityAsync(WalkingProfile, IdentityA,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetCapabilityAsync(WalkingProfile, IdentityB,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DelayedHostedResponse_CurrentLocationChanges_DoesNotPublishToActiveDirectRoute()
    {
        var routeResponse = new TaskCompletionSource<HostedRouteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var routeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = SuccessfulApi();
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                routeStarted.SetResult();
                return routeResponse.Task;
            });
        var dialogs = Mock.Of<IDialogService>();
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs);
        var location = new LocationData { Latitude = 37, Longitude = 23 };
        callbacks.SetupGet(value => value.CurrentLocation).Returns(() => location);

        var pending = coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");
        await routeStarted.Task;
        location = new LocationData { Latitude = 37.1, Longitude = 23.1 };
        routeResponse.SetResult(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));
        var route = await pending;

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeTrue();
        route.Waypoints.Should().HaveCount(2);
        route.Attribution.Should().BeEmpty();
        route.HostedProvenance.Should().BeNull();
    }

    [Fact]
    public async Task CurrentHostedResponse_PublishesToActiveRouteAndDirectReplacementClearsProvenance()
    {
        var api = SuccessfulApi();
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, Mock.Of<IDialogService>());
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var hosted = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        hosted.Should().BeSameAs(navigation.ActiveRoute);
        hosted.IsDirectRoute.Should().BeFalse();
        hosted.Attribution.Should().ContainSingle(item => item.Text == "Powered by Wayfarer test");
        hosted.HostedProvenance.Should().NotBeNull();
        hosted.HostedProvenance!.TransportProfileId.Should().Be(WalkingProfile);

        var direct = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.02, 23.02, "Direct", "direct");

        direct.Should().BeSameAs(navigation.ActiveRoute);
        direct.Should().NotBeSameAs(hosted);
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
        direct.HostedProvenance.Should().BeNull();
    }

    private static (NavigationCoordinatorViewModel Coordinator, TripNavigationService Navigation,
        MockSettingsService Settings, Mock<INavigationCallbacks> Callbacks) CreateCoordinator(
        IHostedRoutingApiClient api, IDialogService dialogs)
    {
        var state = new MockTripStateManager();
        var navigation = new TripNavigationService(
            NullLogger<TripNavigationService>.Instance,
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            state);
        var settings = new MockSettingsService();
        var coordinator = new NavigationCoordinatorViewModel(
            navigation,
            new NavigationHudViewModel(),
            Mock.Of<IVisitNotificationService>(),
            new HostedRoutingService(api, NullLogger<HostedRoutingService>.Instance),
            settings,
            dialogs,
            state,
            NullLogger<NavigationCoordinatorViewModel>.Instance);
        var callbacks = new Mock<INavigationCallbacks>();
        coordinator.SetCallbacks(callbacks.Object);
        return (coordinator, navigation, settings, callbacks);
    }

    private static Mock<IHostedRoutingApiClient> SuccessfulApi()
    {
        var api = new Mock<IHostedRoutingApiClient>();
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(IdentityA,
                new HostedRoutingProfile(WalkingProfile, "Walking", "walk", "active")));
        api.Setup(client => client.GetCapabilityAsync(WalkingProfile, IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(WalkingProfile, IdentityA, IdentityA, Attribution()));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA));
        return api;
    }

    private static HostedRoutingCatalog Catalog(string identity, params HostedRoutingProfile[] profiles) =>
        new(identity, "available", profiles);

    private static IReadOnlyList<HostedRouteAttribution> Attribution() =>
        [new("Powered by Wayfarer test", "https://example.test")];
}
