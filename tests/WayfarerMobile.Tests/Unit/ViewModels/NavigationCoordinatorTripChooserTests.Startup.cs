using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed partial class NavigationCoordinatorTripChooserTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task BothCallers_StartGuidanceAndAdvanceOnLocation(bool pin, bool direct)
    {
        var scenario = CreateScenario(direct ? "Direct" : "Wayfarer route");
        if (direct)
            scenario.Api.Setup(api => api.DiscoverAsync(It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Direct must never discover"));
        else ConfigureHosted(scenario);

        if (pin)
        {
            var context = CreateContext(scenario, direct ? NavigationMethod.Direct : NavigationMethod.Wayfarer);
            await context.NavigateToContextLocationCommand.ExecuteAsync(null);
            context.HasDroppedPin.Should().BeFalse();
        }
        else
        {
            var bridge = new NavigationCallbackBridge(scenario.Coordinator)
                { CurrentLocation = new() { Latitude = 37, Longitude = 23 } };
            using var sheet = CreateTripSheet(scenario.State, scenario.Editor);
            sheet.SetCallbacks(bridge);
            sheet.SelectedTripPlace = scenario.Destination;
            sheet.IsTripSheetOpen = true;
            await sheet.Editor.NavigateToTripPlaceCommand.ExecuteAsync(null);
            sheet.IsTripSheetOpen.Should().BeFalse();
        }

        var route = scenario.Navigation.ActiveRoute;
        route.Should().NotBeNull();
        route!.IsDirectRoute.Should().Be(direct);
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Hud.IsNavigating.Should().BeTrue();
        scenario.Hud.InstructionText.Should().NotBeNullOrEmpty();
        scenario.Callbacks.Verify(callback => callback.ShowNavigationRoute(route), Times.Once);
        scenario.VisitNotifications.Verify(service => service.UpdateNavigationState(true,
            pin ? null : scenario.Destination.Id), Times.Once);
        var initialDistance = scenario.Hud.DistanceText;

        scenario.Coordinator.UpdateLocation(37.005, 23.005);

        scenario.Hud.DistanceText.Should().NotBe(initialDistance);
        scenario.Hud.ProgressPercent.Should().BeGreaterThan(0);
        scenario.Callbacks.Verify(callback => callback.UpdateNavigationRouteProgress(route, 37.005, 23.005), Times.Once);
        if (direct)
        {
            scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Never);
            VerifyNoProviderRequest(scenario.Api);
        }
        else
            scenario.Api.Verify(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothCallers_DirectUsesLocationAfterSelection(bool pin)
    {
        var scenario = CreateScenario("Direct");
        var moved = new LocationData { Latitude = 37.002, Longitude = 23.002 };
        if (pin)
        {
            var context = CreateContext(scenario, NavigationMethod.Direct, () =>
                scenario.Callbacks.SetupGet(callback => callback.CurrentLocation).Returns(moved));
            await context.NavigateToContextLocationCommand.ExecuteAsync(null);
            context.HasDroppedPin.Should().BeFalse();
        }
        else
        {
            scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
                .Returns<DirectionsViewModel>(async model =>
                {
                    scenario.Callbacks.SetupGet(callback => callback.CurrentLocation).Returns(moved);
                    await model.DirectCommand.ExecuteAsync(null);
                });
            (await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString())).Should().BeTrue();
        }
        scenario.Navigation.ActiveRoute!.Waypoints[0].Latitude.Should().Be(moved.Latitude);
        scenario.Navigation.ActiveRoute.Waypoints[0].Longitude.Should().Be(moved.Longitude);
        scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoProviderRequest(scenario.Api);
    }

    [Fact]
    public async Task EssentialFailureAfterWakeAcquisition_CleansStateAndAllowsReleaseRetry()
    {
        var scenario = CreateScenario("Direct");
        scenario.WakeLock.SetupSequence(service => service.ReleaseWakeLock(WakeLockOwner.Navigation))
            .Throws(new InvalidOperationException("release unavailable"))
            .Pass();
        scenario.Coordinator.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(scenario.Coordinator.IsNavigating) && scenario.Coordinator.IsNavigating)
                throw new InvalidOperationException("display binding failed");
        };

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());

        started.Should().BeFalse();
        scenario.Navigation.ActiveRoute.Should().BeNull();
        scenario.Coordinator.IsNavigating.Should().BeFalse();
        scenario.Hud.IsNavigating.Should().BeFalse();
        scenario.WakeLock.Verify(service => service.ReleaseWakeLock(WakeLockOwner.Navigation), Times.Once);
        scenario.Coordinator.StopNavigation();
        scenario.WakeLock.Verify(service => service.ReleaseWakeLock(WakeLockOwner.Navigation), Times.Exactly(2));
        scenario.WakeLock.Verify(service => service.ReleaseWakeLock(WakeLockOwner.Persistent), Times.Never);
    }

    [Fact]
    public async Task DroppedPin_AccountChangesDuringPersistence_DoesNotActivateSavedResult()
    {
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var scenario = CreateScenario("Wayfarer route", beforeRetainedConnection: async () =>
        {
            if (++calls != 2) return; // First connection is retained lookup; second is save.
            saving.SetResult();
            await resume.Task;
        });
        ConfigureHosted(scenario);
        var prior = await scenario.Coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.02, 23.02, "Existing", direct: true);
        await scenario.Coordinator.StartNavigationWithRouteAsync(prior!);
        var context = CreateContext(scenario, NavigationMethod.Wayfarer);

        var pending = context.NavigateToContextLocationCommand.ExecuteAsync(null);
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
        scenario.Settings.ApiToken = "changed-test-session";
        resume.SetResult();
        await pending;

        context.HasDroppedPin.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().BeSameAs(prior);
        scenario.Hud.DestinationName.Should().Be("Existing");
        scenario.Callbacks.Verify(callback => callback.ShowNavigationRoute(It.IsAny<NavigationRoute>()), Times.Once);
    }

    [Fact]
    public async Task DroppedPin_MapFailure_PreservesPinAndCleansEssentialState()
    {
        var scenario = CreateScenario("Direct");
        var context = CreateContext(scenario, NavigationMethod.Direct);
        scenario.Callbacks.Setup(callback => callback.ShowNavigationRoute(It.IsAny<NavigationRoute>()))
            .Throws(new InvalidOperationException("map unavailable"));

        await context.NavigateToContextLocationCommand.ExecuteAsync(null);

        context.HasDroppedPin.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().BeNull();
        scenario.Coordinator.IsNavigating.Should().BeFalse();
        scenario.Hud.IsNavigating.Should().BeFalse();
        scenario.VisitNotifications.Verify(service => service.UpdateNavigationState(false, null), Times.Once);
        scenario.Dialogs.Verify(dialog => dialog.ShowInfoAsync("Navigation", It.Is<string>(text => text.Contains("try Directions again"))), Times.Once);
    }

    [Theory]
    [InlineData("dismiss")]
    [InlineData("target")]
    [InlineData("account")]
    public async Task DroppedPin_RejectedSelection_PreservesExistingNavigation(string change)
    {
        var scenario = CreateScenario("Direct");
        var prior = await scenario.Coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.02, 23.02, "Existing", direct: true);
        await scenario.Coordinator.StartNavigationWithRouteAsync(prior!);
        ContextMenuViewModel? context = null;
        context = CreateContext(scenario, change == "dismiss" ? null : NavigationMethod.Direct, () =>
        {
            if (change == "target") context!.ShowContextMenu(37.03, 23.03);
            if (change == "account") scenario.Settings.ApiToken = "changed-test-session";
        });

        await context.NavigateToContextLocationCommand.ExecuteAsync(null);

        context.HasDroppedPin.Should().BeTrue();
        scenario.Navigation.ActiveRoute.Should().BeSameAs(prior);
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Hud.IsNavigating.Should().BeTrue();
        scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Never);
        if (change == "dismiss")
            scenario.Dialogs.Verify(dialog => dialog.ShowInfoAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DroppedPin_UnavailablePicker_ReportsFailureAndPreservesGuidanceWithoutAPage()
    {
        var scenario = CreateScenario("Direct");
        var prior = await scenario.Coordinator.CalculateRouteToCoordinatesAsync(37, 23, 37.02, 23.02, "Existing", direct: true);
        await scenario.Coordinator.StartNavigationWithRouteAsync(prior!);
        // Exercise unavailable Directions presentation through the real context menu.
        var context = CreateContext(scenario, null, () =>
            throw new InvalidOperationException("Navigation selection is unavailable. Reopen the map and try again."));
        Application.Current = null;
        scenario.Dialogs.Setup(dialog => dialog.ShowInfoAsync("Navigation", It.IsAny<string>()))
            .Returns<string, string>((title, message) => new DialogService().ShowInfoAsync(title, message));
        scenario.WakeLock.Invocations.Clear();
        scenario.VisitNotifications.Invocations.Clear();
        scenario.Audio.Invocations.Clear();

        await context.NavigateToContextLocationCommand.ExecuteAsync(null);

        scenario.Dialogs.Verify(dialog => dialog.ShowInfoAsync("Navigation",
            "Navigation could not start. Reopen the map and try Directions again."), Times.Once);
        // The real DialogService safely returns without a page; no displayed dialog is claimed.
        context.HasDroppedPin.Should().BeTrue();
        context.DroppedPinLatitude.Should().Be(scenario.Destination.Latitude);
        context.DroppedPinLongitude.Should().Be(scenario.Destination.Longitude);
        scenario.Navigation.ActiveRoute.Should().BeSameAs(prior);
        scenario.Coordinator.IsNavigating.Should().BeTrue();
        scenario.Hud.IsNavigating.Should().BeTrue();
        scenario.Hud.DestinationName.Should().Be("Existing");
        scenario.Callbacks.Verify(callback => callback.ShowNavigationRoute(It.IsAny<NavigationRoute>()), Times.Once);
        scenario.Callbacks.Verify(callback => callback.ClearNavigationRoute(), Times.Never);
        scenario.WakeLock.Invocations.Should().BeEmpty();
        scenario.VisitNotifications.Invocations.Should().BeEmpty();
        scenario.Audio.Invocations.Should().BeEmpty();
        scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoProviderRequest(scenario.Api);
    }

    private static ContextMenuViewModel CreateContext(Scenario scenario, NavigationMethod? choice, Action? whileChoosing = null)
    {
        var callbacks = new Mock<IContextMenuCallbacks>(MockBehavior.Strict);
        callbacks.Setup(callback => callback.ShowDroppedPin(It.IsAny<double>(), It.IsAny<double>()));
        callbacks.Setup(callback => callback.ClearDroppedPinFromMap());
        scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(model =>
            {
                whileChoosing?.Invoke();
                return ChooseDirectionsAsync(model, choice switch
                {
                    NavigationMethod.Direct => "Direct",
                    NavigationMethod.Wayfarer => "Wayfarer route",
                    NavigationMethod.ExternalMaps => "External Maps",
                    _ => null
                });
            });
        callbacks.Setup(callback => callback.StartNavigationToCoordinatesAsync(It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<Func<HostedRouteCoordinate?>>()))
            .Returns<double, double, string, Func<Task>, Func<HostedRouteCoordinate?>>(
                scenario.Coordinator.StartNavigationToCoordinatesAsync);
        var context = new ContextMenuViewModel(NullLogger<ContextMenuViewModel>.Instance);
        context.SetCallbacks(callbacks.Object);
        context.ShowContextMenu(scenario.Destination.Latitude, scenario.Destination.Longitude);
        return context;
    }

    private static void ConfigureHosted(Scenario scenario)
    {
        scenario.Dialogs.Setup(dialog => dialog.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)", It.IsAny<IReadOnlyList<string>>(), "Cancel"))
            .ReturnsAsync("Walk");
        scenario.Api.Setup(api => api.GetCapabilityAsync(Guid.Empty, "walk", Identity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(Guid.Empty, Identity, Identity,
                [new("Test attribution", "https://example.test")]));
        scenario.Api.Setup(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(Guid.Empty, Identity));
    }
}
