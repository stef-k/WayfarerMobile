using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed partial class NavigationCoordinatorTripChooserTests
{
    [Fact]
    public async Task PinExternalMaps_WithoutLocation_RemainsExternalAndMakesZeroHostedCalls()
    {
        var scenario = CreateScenario("External Maps");
        scenario.Callbacks.SetupGet(callback => callback.CurrentLocation).Returns((LocationData?)null);
        var handoffs = 0;
        var started = await scenario.Coordinator.StartNavigationToCoordinatesAsync(37.01, 23.01,
            "Pin", () => { handoffs++; return Task.CompletedTask; }, () => new(23.01, 37.01));

        started.Should().BeFalse();
        handoffs.Should().Be(1);
        scenario.Navigation.ActiveRoute.Should().BeNull();
        scenario.Api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RouteRetry_ChangedCatalogRequiresExplicitReselection()
    {
        const string updated = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQ";
        var scenario = CreateScenario(null);
        ConfigureHosted(scenario);
        scenario.Api.SetupSequence(api => api.DiscoverAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostedRoutingCatalog(Identity, "available", "geoapify", [new("walk", "Walk")]))
            .ReturnsAsync(new HostedRoutingCatalog(updated, "available", "geoapify", [new("walk", "On foot")]));
        scenario.Api.SetupSequence(api => api.GetCapabilityAsync(Guid.Empty, "walk", Identity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(Guid.Empty, Identity, Identity, [new("Test", "https://example.test")]))
            .ReturnsAsync(new HostedRoutingCapability("catalog-changed", Guid.Empty, null, null, null, null, null));
        scenario.Api.Setup(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException());
        scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(async model =>
            {
                await model.LoadCommand.ExecuteAsync(null);
                await model.ChooseModeCommand.ExecuteAsync(model.Modes.Single());
                await model.RetryCommand.ExecuteAsync(null);
                model.Modes.Should().Equal(new HostedProviderMode("walk", "On foot"));
                model.CanRetry.Should().BeFalse();
                scenario.Api.Verify(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()), Times.Once);
                await model.CancelCommand.ExecuteAsync(null);
            });

        (await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString())).Should().BeFalse();
        scenario.Api.Verify(api => api.GetCapabilityAsync(Guid.Empty, "walk", updated, It.IsAny<CancellationToken>()), Times.Never);
        scenario.Navigation.ActiveRoute.Should().BeNull();
    }

    private static async Task ChooseDirectionsAsync(DirectionsViewModel model, string? choice)
    {
        if (choice == "Direct") await model.DirectCommand.ExecuteAsync(null);
        else if (choice == "External Maps") await model.ExternalMapsCommand.ExecuteAsync(null);
        else if (choice == null) await model.CancelCommand.ExecuteAsync(null);
        else
        {
            await model.LoadCommand.ExecuteAsync(null);
            await model.ChooseModeCommand.ExecuteAsync(model.Modes.Single());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialChoice_MakesZeroHostedCalls(bool direct)
    {
        var scenario = CreateScenario(direct ? "Direct" : null);
        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());
        started.Should().Be(direct);
        scenario.Api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoadOptions_RequiresExplicitModeWithMatchingCatalogAuthority()
    {
        var scenario = CreateScenario(null);
        ConfigureHosted(scenario);
        scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(async model =>
            {
                scenario.Api.VerifyNoOtherCalls();
                await model.LoadCommand.ExecuteAsync(null);
                model.Modes.Should().Equal(new HostedProviderMode("walk", "Walk"));
                VerifyNoProviderRequest(scenario.Api);
                await model.ChooseModeCommand.ExecuteAsync(model.Modes.Single());
            });

        (await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString())).Should().BeTrue();
        scenario.Api.Verify(api => api.GetCapabilityAsync(Guid.Empty, "walk", Identity,
            It.IsAny<CancellationToken>()), Times.Once);
        scenario.Api.Verify(api => api.GetRouteAsync(It.Is<HostedRouteRequest>(request =>
            request.ProviderMode == "walk" && request.SelectedProfileAuthorityIdentity == Identity),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOperation_ExplicitRetryRepeatsOnlyThatOperation(bool calculation)
    {
        var scenario = CreateScenario(null);
        ConfigureHosted(scenario);
        if (calculation)
            scenario.Api.SetupSequence(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException())
                .ReturnsAsync(HostedRouteResponse.ValidForTest(Guid.Empty, Identity));
        else
            scenario.Api.SetupSequence(api => api.DiscoverAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException())
                .ReturnsAsync(new HostedRoutingCatalog(Identity, "available", "geoapify", [new("walk", "Walk")]));
        scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(async model =>
            {
                await model.LoadCommand.ExecuteAsync(null);
                if (calculation) await model.ChooseModeCommand.ExecuteAsync(model.Modes.Single());
                model.Status.Should().Be(calculation
                    ? "The route could not be calculated. Try again or choose Direct."
                    : "Route options are unavailable. Try again or choose Direct.");
                model.CanRetry.Should().BeTrue();
                model.IsComplete.Should().BeFalse();
                await model.RetryCommand.ExecuteAsync(null);
                if (!calculation)
                {
                    VerifyNoProviderRequest(scenario.Api);
                    await model.DirectCommand.ExecuteAsync(null);
                }
            });

        (await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString())).Should().BeTrue();
        scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Exactly(calculation ? 1 : 2));
        scenario.Api.Verify(api => api.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(calculation ? 2 : 0));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Loading_ExitRejectsLateCompletionAndDuplicateLoad(bool direct, bool failed)
    {
        var scenario = CreateScenario(null);
        var response = new TaskCompletionSource<HostedRoutingCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken requestToken = default;
        scenario.Api.Setup(api => api.DiscoverAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token => { requestToken = token; return response.Task; });
        DirectionsViewModel? surface = null;
        Task? loading = null;
        scenario.Callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(async model =>
            {
                surface = model;
                loading = model.LoadCommand.ExecuteAsync(null);
                model.Status.Should().Be("Loading route options…");
                await model.LoadCommand.ExecuteAsync(null);
                await (direct ? model.DirectCommand : model.CancelCommand).ExecuteAsync(null);
            });

        var started = await scenario.Coordinator.StartNavigationToPlaceAsync(scenario.Destination.Id.ToString());
        started.Should().Be(direct);
        requestToken.IsCancellationRequested.Should().BeTrue();
        var status = surface!.Status;
        if (failed) response.SetException(new HttpRequestException());
        else response.SetResult(new(Identity, "available", "geoapify", [new("walk", "Walk")]));
        await loading!;
        surface.Modes.Should().BeEmpty();
        surface.Status.Should().Be(status);
        scenario.Api.Verify(api => api.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoProviderRequest(scenario.Api);
        scenario.Navigation.ActiveRoute?.IsDirectRoute.Should().BeTrue();
    }
}
