using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed partial class NavigationCoordinatorHostedRoutingTests
{
    [Theory]
    [InlineData("reuse")]
    [InlineData("refresh")]
    [InlineData("failed-refresh")]
    public async Task PinDirections_RetainedChoiceAndRefreshShareOneSurface(string choice)
    {
        var (_, retained, settings) = await CreateRetainedScenarioAsync();
        var api = SuccessfulApi();
        if (choice == "failed-refresh")
            api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException());
        var (coordinator, navigation, _, callbacks) = CreateCoordinator(api.Object,
            Mock.Of<IDialogService>(), retained, settings);
        callbacks.SetupGet(callback => callback.CurrentLocation).Returns(new LocationData { Latitude = 37, Longitude = 23 });
        callbacks.Setup(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()))
            .Returns<DirectionsViewModel>(async model =>
            {
                model.HasRetainedRoute.Should().BeTrue();
                api.VerifyNoOtherCalls();
                if (choice != "reuse")
                {
                    await model.LoadCommand.ExecuteAsync(null);
                    await model.ChooseModeCommand.ExecuteAsync(model.Modes.Single());
                }
                if (choice != "refresh") await model.UseRetainedCommand.ExecuteAsync(null);
            });

        var started = await coordinator.StartNavigationToCoordinatesAsync(37.01, 23.01, "Target",
            () => Task.CompletedTask, () => new(23.01, 37.01));

        started.Should().BeTrue();
        navigation.ActiveRoute!.HostedProvenance!.IsRetained.Should().Be(choice != "refresh");
        callbacks.Verify(callback => callback.ShowDirectionsAsync(It.IsAny<DirectionsViewModel>()), Times.Once);
        api.Verify(client => client.DiscoverAsync(It.IsAny<CancellationToken>()), Times.Exactly(choice == "reuse" ? 0 : 1));
    }
}
