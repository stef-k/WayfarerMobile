using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

[Collection("SQLite")]
public sealed class NavigationCoordinatorHostedRoutingTests : IAsyncLifetime
{
    private static readonly Guid WalkingProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HikingProfile = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string IdentityA = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string IdentityB = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ";
    private readonly List<SQLiteAsyncConnection> retainedConnections = [];
    private readonly List<string> retainedDatabasePaths = [];
    private HostedRoutingService? lastHostedRouting;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in retainedConnections) await connection.CloseAsync();
        foreach (var path in retainedDatabasePaths)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenChooser_CatalogChangesThenDismissalReturnsNoRoute()
    {
        var catalogA = Catalog(IdentityA, new HostedProviderMode("walk", "Walk"),
            new HostedProviderMode("bicycle", "Bicycle"));
        var catalogB = Catalog(IdentityB, new HostedProviderMode("walk", "On foot"));
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.SetupSequence(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogA)
            .ReturnsAsync(catalogB);
        api.Setup(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", Guid.Empty,
                null, null, null, null, null, null, null));
        api.Setup(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(
                Guid.Empty, IdentityB, IdentityB, Attribution()));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(Guid.Empty, IdentityB));
        var presentations = new List<IReadOnlyList<string>>();
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .Callback<string, IReadOnlyList<string>, string>((_, choices, _) => presentations.Add(choices))
            .ReturnsAsync(() => presentations.Count == 1
                ? "Walk"
                : null);
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeNull();
        navigation.ActiveRoute.Should().BeNull();
        presentations.Should().HaveCount(2);
        presentations[0].Should().Equal("Walk", "Bicycle");
        presentations[1].Should().Equal("On foot");
        api.Verify(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(), IdentityB,
            It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenChooser_RepeatedCatalogChange_RefreshesOnlyOnceAndRetainsDirect()
    {
        var catalogA = Catalog(IdentityA, new HostedProviderMode("walk", "Walk"),
            new HostedProviderMode("bicycle", "Bicycle"));
        var catalogB = Catalog(IdentityB, new HostedProviderMode("walk", "On foot"));
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.SetupSequence(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogA)
            .ReturnsAsync(catalogB);
        api.Setup(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", Guid.Empty,
                null, null, null, null, null, null, null));
        api.Setup(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", Guid.Empty,
                null, null, null, null, null, null, null));
        var presentations = new List<IReadOnlyList<string>>();
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .Callback<string, IReadOnlyList<string>, string>((_, choices, _) => presentations.Add(choices))
            .ReturnsAsync(() => presentations.Count == 1
                ? "Walk"
                : "On foot");
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeTrue();
        presentations.Should().HaveCount(2);
        api.Verify(client => client.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        api.Verify(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityB,
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
        var dialogs = SelectedModeDialogs();
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
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, SelectedModeDialogs());
        callbacks.SetupGet(value => value.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var hosted = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.01, 23.01, "Target", "foot");

        hosted.Should().BeSameAs(navigation.ActiveRoute);
        hosted.IsDirectRoute.Should().BeFalse();
        hosted.Attribution.Should().ContainSingle(item => item.Text == "Powered by Wayfarer test");
        hosted.HostedProvenance.Should().NotBeNull();
        hosted.HostedProvenance!.TransportProfileId.Should().Be(Guid.Empty);
        hosted.HostedProvenance.ProviderMode.Should().Be("walk");

        var direct = await coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.02, 23.02, "Direct", "direct");

        direct.Should().BeSameAs(navigation.ActiveRoute);
        direct.Should().NotBeSameAs(hosted);
        direct.IsDirectRoute.Should().BeTrue();
        direct.Attribution.Should().BeEmpty();
        direct.HostedProvenance.Should().BeNull();
    }

    [Theory]
    [InlineData("Use retained route", true)]
    [InlineData(null, false)]
    public async Task MatchingRetainedAdHocRoute_OffersRetainedOrDirectWithoutHostedContact(
        string? choice, bool expectRetained)
    {
        var (_, retainedService, settings) = await CreateRetainedScenarioAsync();
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync("Wayfarer retained route",
                It.Is<IReadOnlyList<string>>(options => options.SequenceEqual(
                    new[] { "Use retained route", "Refresh with Wayfarer" })), "Direct"))
            .ReturnsAsync(choice);
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(
            api.Object, dialogs.Object, retainedService, settings);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var selected = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Current target", "foot");

        selected.Should().BeSameAs(navigation.ActiveRoute);
        selected.IsDirectRoute.Should().Be(!expectRetained);
        if (expectRetained) selected.HostedProvenance!.IsRetained.Should().BeTrue();
        else selected.HostedProvenance.Should().BeNull();
        api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MatchingRetainedAdHocRoute_ExplicitRefreshContactsHostedAndOnlyValidSuccessReplaces(
        bool freshSucceeds)
    {
        var (repository, retainedService, settings) = await CreateRetainedScenarioAsync();
        var api = SuccessfulApi();
        if (!freshSucceeds)
        {
            api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(HostedRouteResponse.ValidForTest(WalkingProfile, IdentityA) with
                { Succeeded = false, Outcome = "invalid-response" });
        }
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync("Wayfarer retained route",
                It.Is<IReadOnlyList<string>>(options => options.SequenceEqual(
                    new[] { "Use retained route", "Refresh with Wayfarer" })), "Direct"))
            .ReturnsAsync("Refresh with Wayfarer");
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.Is<IReadOnlyList<string>>(options => options.SequenceEqual(new[] { "Walk" })), "Direct"))
            .ReturnsAsync("Walk");
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(
            api.Object, dialogs.Object, retainedService, settings);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var selected = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Current target", "foot");

        selected.Should().BeSameAs(navigation.ActiveRoute);
        selected.HostedProvenance!.IsRetained.Should().Be(!freshSucceeds);
        api.Verify(client => client.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA,
            It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var retained = await repository.SelectAsync(new HostedRouteRequestContext(null, "walk", "walk",
                new(23, 37), new(23.01, 37.01), [], "Current target", 1,
                settings.AuthenticationSessionRevision, "https://test.example.com",
                "ad-hoc-coordinates", "hosted"),
            settings.RoutingAccountPartition, DateTimeOffset.UtcNow, () => true);
        retained!.Route.Steps.Should().ContainSingle(step => step.Instruction == "Retained");
    }

    [Fact]
    public async Task ValidSavedSegmentGeometry_RemainsAheadOfRetainedAndFreshRouting()
    {
        var origin = new TripPlace
        {
            Id = Guid.NewGuid(), Name = "Origin", Latitude = 37.98, Longitude = 23.72, SortOrder = 0
        };
        var destination = new TripPlace
        {
            Id = Guid.NewGuid(), Name = "Destination", Latitude = 38, Longitude = 23.74, SortOrder = 1
        };
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(), Name = "Saved route",
            Regions = [new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = [origin, destination] }],
            Segments =
            [
                new TripSegment
                {
                    Id = Guid.NewGuid(), OriginId = origin.Id, DestinationId = destination.Id,
                    TransportMode = "walking",
                    Geometry = """{"type":"LineString","coordinates":[[23.72,37.98],[23.73,37.99],[23.74,38.00]]}"""
                }
            ]
        };
        var state = new MockTripStateManager();
        state.SetLoadedTrip(trip);
        var navigation = new TripNavigationService(
            NullLogger<TripNavigationService>.Instance,
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            state);
        navigation.LoadTrip(trip).Should().BeTrue();
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        var coordinator = new NavigationCoordinatorViewModel(
            navigation, new NavigationHudViewModel(), Mock.Of<IVisitNotificationService>(),
            new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance),
            CreateRetainedRoutingService(), new MockSettingsService(), Mock.Of<IDialogService>(),
            state, NullLogger<NavigationCoordinatorViewModel>.Instance);
        var callbacks = new Mock<INavigationCallbacks>();
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = origin.Latitude, Longitude = origin.Longitude });
        coordinator.SetCallbacks(callbacks.Object);

        await coordinator.StartNavigationToPlaceAsync(destination.Id.ToString());

        navigation.ActiveRoute!.IsDirectRoute.Should().BeFalse();
        navigation.ActiveRoute.Waypoints.Should().HaveCount(4);
        navigation.ActiveRoute.Waypoints.Should().ContainSingle(waypoint =>
            waypoint.Type == WaypointType.RoutePoint &&
            waypoint.Latitude == 37.99 &&
            waypoint.Longitude == 23.73);
        navigation.ActiveRoute.HostedProvenance.Should().BeNull();
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FreshValidatedRoute_RemainsPublishedWhenLocalPersistenceFails()
    {
        var retained = new RetainedWayfarerRoutingService(
            new RetainedWayfarerRouteRepository(() =>
                Task.FromException<SQLiteAsyncConnection>(new InvalidOperationException("injected storage failure"))),
            NullLogger<RetainedWayfarerRoutingService>.Instance);
        var api = SuccessfulApi();
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(
            api.Object, SelectedModeDialogs(), retained);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeFalse();
        route.HostedProvenance.Should().NotBeNull();
    }

    private async Task<(RetainedWayfarerRouteRepository Repository,
        RetainedWayfarerRoutingService Service, MockSettingsService Settings)> CreateRetainedScenarioAsync()
    {
        var connection = new SQLiteAsyncConnection(":memory:");
        retainedConnections.Add(connection);
        await RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None);
        var repository = new RetainedWayfarerRouteRepository(connection);
        var settings = new MockSettingsService();
        var context = new HostedRouteRequestContext(null, "walk", "walk",
            new(23, 37), new(23.01, 37.01), [], "Current target", 1,
            settings.AuthenticationSessionRevision, "https://test.example.com",
            "ad-hoc-coordinates", "hosted");
        var route = new NavigationRoute
        {
            Waypoints =
            [
                new() { Longitude = 23, Latitude = 37 },
                new() { Longitude = 23.005, Latitude = 37.005 },
                new() { Longitude = 23.01, Latitude = 37.01 }
            ],
            Steps =
            [
                new() { Instruction = "Retained", ManeuverType = "continue",
                    GeometryFromIndex = 0, GeometryToIndex = 2, DistanceMeters = 1500,
                    DurationSeconds = 900, Longitude = 23, Latitude = 37 }
            ],
            DestinationName = "must-not-be-stored", TotalDistanceMeters = 1500,
            EstimatedDuration = TimeSpan.FromSeconds(900),
            Attribution = [new("Powered by Wayfarer test", "https://example.test")]
        };
        var candidate = new HostedRouteCandidate(route, context, WalkingProfile, IdentityA,
            new("geoapify", Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "mapping", "persistent"), DateTimeOffset.UtcNow.AddMinutes(-1), "walk");
        (await repository.SaveAsync(candidate, settings.RoutingAccountPartition,
            DateTimeOffset.UtcNow, () => true)).Should().Be(RetainedRouteSaveResult.Saved);
        return (repository, new(repository,
            NullLogger<RetainedWayfarerRoutingService>.Instance), settings);
    }

    private (NavigationCoordinatorViewModel Coordinator, TripNavigationService Navigation,
        MockSettingsService Settings, Mock<INavigationCallbacks> Callbacks) CreateCoordinator(
        IHostedRoutingApiClient api, IDialogService dialogs,
        RetainedWayfarerRoutingService? retainedRouting = null,
        MockSettingsService? suppliedSettings = null)
    {
        var state = new MockTripStateManager();
        var navigation = new TripNavigationService(
            NullLogger<TripNavigationService>.Instance,
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            state);
        var settings = suppliedSettings ?? new MockSettingsService();
        var hostedRouting = new HostedRoutingService(api, NullLogger<HostedRoutingService>.Instance);
        lastHostedRouting = hostedRouting;
        var coordinator = new NavigationCoordinatorViewModel(
            navigation,
            new NavigationHudViewModel(),
            Mock.Of<IVisitNotificationService>(),
            hostedRouting,
            retainedRouting ?? CreateRetainedRoutingService(),
            settings,
            dialogs,
            state,
            NullLogger<NavigationCoordinatorViewModel>.Instance);
        var callbacks = new Mock<INavigationCallbacks>();
        coordinator.SetCallbacks(callbacks.Object);
        return (coordinator, navigation, settings, callbacks);
    }

    private RetainedWayfarerRoutingService CreateRetainedRoutingService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wayfarer-navigation-route-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);
        retainedConnections.Add(connection);
        retainedDatabasePaths.Add(path);
        RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        return new(new RetainedWayfarerRouteRepository(connection),
            NullLogger<RetainedWayfarerRoutingService>.Instance);
    }

    private static Mock<IHostedRoutingApiClient> SuccessfulApi()
    {
        var api = new Mock<IHostedRoutingApiClient>();
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(IdentityA, new HostedProviderMode("walk", "Walk")));
        api.Setup(client => client.GetCapabilityAsync(Guid.Empty, "walk", IdentityA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(Guid.Empty, IdentityA, IdentityA, Attribution()));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(Guid.Empty, IdentityA));
        return api;
    }

    private static HostedRoutingCatalog Catalog(string identity, params HostedProviderMode[] modes) =>
        new(identity, "available", [], "geoapify", modes);

    private static IReadOnlyList<HostedRouteAttribution> Attribution() =>
        [new("Powered by Wayfarer test", "https://example.test")];

    private static IDialogService SelectedModeDialogs()
    {
        var dialogs = new Mock<IDialogService>();
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .ReturnsAsync("Walk");
        return dialogs.Object;
    }

    [Fact]
    public async Task FreshRoute_DismissedChooserReturnsNoRouteAndPreservesCurrentNavigation()
    {
        var modes = new[]
        {
            new HostedProviderMode("walk", "Walk"), new("bicycle", "Bicycle"),
            new("motorcycle", "Motorcycle"), new("drive", "Drive"), new("bus", "Bus")
        };
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(IdentityA, modes));
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.Is<IReadOnlyList<string>>(choices => choices.SequenceEqual(
                    new[] { "Walk", "Bicycle", "Motorcycle", "Drive", "Bus" })), "Direct"))
            .ReturnsAsync((string?)null);
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var priorRoute = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.005, 23.005, "Existing", "direct");
        coordinator.IsNavigating = true;
        lastHostedRouting!.SelectRetained(1, WalkingProfile, "walk", IdentityA);
        var priorSelection = lastHostedRouting!.CurrentSelection;

        var route = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Target", "car");

        route.Should().BeNull();
        navigation.ActiveRoute.Should().BeSameAs(priorRoute);
        coordinator.IsNavigating.Should().BeTrue();
        priorSelection!.ProviderMode.Should().Be("walk");
        lastHostedRouting.CurrentSelection.Should().Be(priorSelection);
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FreshRoute_ExplicitDirectReturnsDirectWithoutProviderContact()
    {
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(IdentityA, new HostedProviderMode("walk", "Walk")));
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.Is<IReadOnlyList<string>>(choices => choices.SequenceEqual(new[] { "Walk" })), "Direct"))
            .ReturnsAsync("Direct");
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object, dialogs.Object);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Target", "car");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route!.IsDirectRoute.Should().BeTrue();
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExplicitDirect_MakesNoHostedOrChooserContact()
    {
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        var (coordinator, _, _, _) = CreateCoordinator(api.Object, dialogs.Object);

        var route = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Target", "direct");

        route!.IsDirectRoute.Should().BeTrue();
        api.VerifyNoOtherCalls();
        dialogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AuthenticationChangesWhileChoosing_PreventCapabilityContact()
    {
        var settings = new MockSettingsService();
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(IdentityA, new HostedProviderMode("walk", "Walk")));
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .Callback(() => settings.ApiToken = "replacement-token")
            .ReturnsAsync("Walk");
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(
            api.Object, dialogs.Object, suppliedSettings: settings);
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = 37, Longitude = 23 });

        var route = await coordinator.CalculateRouteToCoordinatesAsync(
            37, 23, 37.01, 23.01, "Target", "foot");

        route.Should().BeSameAs(navigation.ActiveRoute);
        route.IsDirectRoute.Should().BeTrue();
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
