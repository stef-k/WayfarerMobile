using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;
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

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        started.Should().BeFalse();
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

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        started.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().NotBeNull();
        scenario.Navigation.ActiveRoute!.IsDirectRoute.Should().BeTrue();
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Callbacks.Verify(value => value.ShowNavigationRoute(scenario.Navigation.ActiveRoute), Times.Once);
        VerifyNoProviderRequest(scenario.Api);
    }

    [Fact]
    public async Task WakeLockFailure_CommitsNavigationAndClosesTripSheetExactlyOnce()
    {
        var wakeLock = new Mock<IWakeLockService>(MockBehavior.Strict);
        wakeLock.Setup(service => service.AcquireWakeLock(true))
            .Throws(new InvalidOperationException("wake lock unavailable"));
        var scenario = CreateScenario("Direct", wakeLock: wakeLock);
        var editorCallbacks = new Mock<ITripItemEditorCallbacks>(MockBehavior.Strict);
        editorCallbacks.SetupGet(value => value.SelectedTripPlace).Returns(scenario.Destination);
        editorCallbacks.Setup(value => value.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString()))
            .Returns(() => scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString()));
        editorCallbacks.Setup(value => value.CloseTripSheet());
        var editor = CreateEditor(editorCallbacks.Object);

        await editor.NavigateToTripPlaceCommand.ExecuteAsync(null);

        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().NotBeNull();
        scenario.Hud.IsNavigating.Should().BeTrue();
        scenario.Callbacks.Verify(value => value.ShowNavigationRoute(scenario.Navigation.ActiveRoute), Times.Once);
        scenario.VisitNotifications.Verify(value => value.UpdateNavigationState(
            true, scenario.Destination.Id), Times.Once);
        editorCallbacks.Verify(value => value.CloseTripSheet(), Times.Once);
        scenario.Coordinator.StopNavigation();
        wakeLock.Verify(value => value.ReleaseWakeLock(), Times.Never);
    }

    [Fact]
    public async Task InitialAnnouncementFailure_CommitsNavigationAndAllowsLaterAnnouncements()
    {
        var audio = new Mock<INavigationAudioService>(MockBehavior.Strict);
        audio.SetupProperty(service => service.IsEnabled, true);
        audio.Setup(service => service.AnnounceNavigationStartAsync(It.IsAny<string>(), It.IsAny<double>()))
            .ThrowsAsync(new InvalidOperationException("speech unavailable"));
        audio.Setup(service => service.AnnounceOffRouteAsync()).Returns(Task.CompletedTask);
        var scenario = CreateScenario("Direct", audio: audio);

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());
        scenario.Hud.UpdateState(new TripNavigationState { Status = NavigationStatus.OffRoute });

        started.Should().BeTrue();
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().NotBeNull();
        scenario.Hud.IsNavigating.Should().BeTrue();
        audio.Verify(service => service.AnnounceOffRouteAsync(), Times.Once);
    }

    [Fact]
    public async Task SuccessfulAncillaryStartup_PreservesNavigationBehavior()
    {
        var scenario = CreateScenario("Direct");

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        started.Should().BeTrue();
        scenario.WakeLock.Verify(service => service.AcquireWakeLock(true), Times.Once);
        scenario.Audio.Verify(service => service.AnnounceNavigationStartAsync(
            scenario.Destination.Name, It.IsAny<double>()), Times.Once);
    }

    private Scenario CreateScenario(string? chooserResult, Mock<IWakeLockService>? wakeLock = null,
        Mock<INavigationAudioService>? audio = null)
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
            .ReturnsAsync(new HostedRoutingCatalog(Identity, "available", "geoapify",
                [new HostedProviderMode("walk", "Walk")]));
        var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
        dialogs.Setup(service => service.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)",
                It.IsAny<IReadOnlyList<string>>(), "Direct"))
            .ReturnsAsync(chooserResult);
        audio ??= new Mock<INavigationAudioService>();
        wakeLock ??= new Mock<IWakeLockService>();
        var hud = new NavigationHudViewModel(navigation, audio.Object, wakeLock.Object,
            NullLogger<NavigationHudViewModel>.Instance);
        var visitNotifications = new Mock<IVisitNotificationService>();
        var coordinator = new NavigationCoordinatorViewModel(
            navigation, hud, visitNotifications.Object,
            new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance),
            CreateRetainedRoutingService(), new MockSettingsService(), dialogs.Object, state,
            NullLogger<NavigationCoordinatorViewModel>.Instance);
        var callbacks = new Mock<INavigationCallbacks>();
        callbacks.SetupGet(value => value.CurrentLocation)
            .Returns(new LocationData { Latitude = origin.Latitude, Longitude = origin.Longitude });
        coordinator.SetCallbacks(callbacks.Object);
        return new(coordinator, navigation, hud, callbacks, api, destination, wakeLock, audio,
            visitNotifications);
    }

    private static TripItemEditorViewModel CreateEditor(ITripItemEditorCallbacks callbacks)
    {
        var editor = new TripItemEditorViewModel(
            Mock.Of<ITripSyncService>(), null!, Mock.Of<IWikipediaService>(),
            new MockToastService(), NullLogger<TripItemEditorViewModel>.Instance);
        editor.SetCallbacks(callbacks);
        return editor;
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
        TripNavigationService Navigation, NavigationHudViewModel Hud,
        Mock<INavigationCallbacks> Callbacks, Mock<IHostedRoutingApiClient> Api,
        TripPlace Destination, Mock<IWakeLockService> WakeLock, Mock<INavigationAudioService> Audio,
        Mock<IVisitNotificationService> VisitNotifications);
}
