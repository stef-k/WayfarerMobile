using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

[Collection("SQLite")]
public sealed class NavigationCoordinatorTripChooserTests : IAsyncLifetime
{
    private const string Identity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly List<SQLite.SQLiteAsyncConnection> connections = [];
    private readonly List<string> databasePaths = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections) await connection.CloseAsync();
        foreach (var path in databasePaths)
            if (File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public async Task TripPlaceDismissal_PreservesActiveRouteAndNavigationState()
    {
        var scenario = CreateScenario(null);
        var prior = await scenario.Navigation.CalculateRouteToCoordinatesAsync(
            37, 23, 37.001, 23.001, "Existing");
        scenario.Coordinator.IsNavigating = true;

        await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        scenario.Navigation.ActiveRoute.Should().BeSameAs(prior);
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Callbacks.Verify(value => value.ShowNavigationRoute(It.IsAny<NavigationRoute>()), Times.Never);
        VerifyNoProviderRequest(scenario.Api);
    }

    [Fact]
    public async Task NextPlaceDismissal_PreservesActiveRouteAndNavigationState()
    {
        var scenario = CreateScenario(null);
        var prior = await scenario.Navigation.CalculateRouteToCoordinatesAsync(
            37, 23, 37.001, 23.001, "Existing");
        scenario.Coordinator.IsNavigating = true;

        await scenario.Coordinator.StartNavigationToNextAsync();

        scenario.Navigation.ActiveRoute.Should().BeSameAs(prior);
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Callbacks.Verify(value => value.ShowNavigationRoute(It.IsAny<NavigationRoute>()), Times.Never);
        VerifyNoProviderRequest(scenario.Api);
    }

    [Fact]
    public async Task TripPlaceExplicitDirect_ActivatesDirectWithoutProviderRequest()
    {
        var scenario = CreateScenario("Direct");

        await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        scenario.Navigation.ActiveRoute.Should().NotBeNull();
        scenario.Navigation.ActiveRoute!.IsDirectRoute.Should().BeTrue();
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Callbacks.Verify(value => value.ShowNavigationRoute(scenario.Navigation.ActiveRoute), Times.Once);
        VerifyNoProviderRequest(scenario.Api);
    }

    private Scenario CreateScenario(string? chooserResult)
    {
        var origin = new TripPlace
        {
            Id = Guid.NewGuid(), Name = "Origin", Latitude = 37, Longitude = 23, SortOrder = 0
        };
        var destination = new TripPlace
        {
            Id = Guid.NewGuid(), Name = "Destination", Latitude = 38, Longitude = 24, SortOrder = 1
        };
        var trip = new TripDetails
        {
            Id = Guid.NewGuid(), Name = "Trip",
            Regions = [new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = [origin, destination] }]
        };
        var state = new MockTripStateManager();
        state.SetLoadedTrip(trip);
        var navigation = new TripNavigationService(
            NullLogger<TripNavigationService>.Instance,
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance), state);
        navigation.LoadTrip(trip).Should().BeTrue();
        var api = new Mock<IHostedRoutingApiClient>(MockBehavior.Strict);
        api.Setup(client => client.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCatalog(Identity, "available", [], "geoapify",
                [new HostedProviderMode("walk", "Walk")]));
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .ReturnsAsync(chooserResult);
        var coordinator = new NavigationCoordinatorViewModel(
            navigation, new NavigationHudViewModel(), Mock.Of<IVisitNotificationService>(),
            new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance),
            CreateRetainedRoutingService(), new MockSettingsService(), dialogs.Object, state,
            NullLogger<NavigationCoordinatorViewModel>.Instance);
        var callbacks = new Mock<INavigationCallbacks>();
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = origin.Latitude, Longitude = origin.Longitude });
        coordinator.SetCallbacks(callbacks.Object);
        return new(coordinator, navigation, callbacks, api, destination);
    }

    private RetainedWayfarerRoutingService CreateRetainedRoutingService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wayfarer-navigation-trip-{Guid.NewGuid():N}.db3");
        var connection = new SQLite.SQLiteAsyncConnection(path);
        connections.Add(connection);
        databasePaths.Add(path);
        RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        return new(new RetainedWayfarerRouteRepository(connection),
            NullLogger<RetainedWayfarerRoutingService>.Instance);
    }

    private static void VerifyNoProviderRequest(Mock<IHostedRoutingApiClient> api)
    {
        api.Verify(client => client.GetCapabilityAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed record Scenario(NavigationCoordinatorViewModel Coordinator,
        TripNavigationService Navigation, Mock<INavigationCallbacks> Callbacks,
        Mock<IHostedRoutingApiClient> Api, TripPlace Destination);
}
