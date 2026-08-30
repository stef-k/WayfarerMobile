namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed class HostedRoutingTriggerIntegrationTests
{
    [Fact]
    public void MemberDirectPathUsesSharedCoordinatorInsteadOfTripNavigationService()
    {
        var source = ReadSource("MemberDetailsViewModel.cs");

        source.Should().Contain("CalculateHostedRouteToCoordinatesAsync")
            .And.NotContain("_tripNavigationService.CalculateRouteToCoordinatesAsync");
    }

    [Fact]
    public void NextPlaceDirectPathUsesSharedHostedOwner()
    {
        var source = ReadSource("NavigationCoordinatorViewModel.cs");
        var method = source[source.IndexOf("StartNavigationToNextAsync", StringComparison.Ordinal)..];
        method = method[..method.IndexOf("/// <summary>", StringComparison.Ordinal)];

        method.Should().Contain("TryHostedAsync");
        method.Should().Contain("route?.IsDirectRoute == true");
    }

    private static string ReadSource(string fileName)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "src", "WayfarerMobile", "ViewModels", fileName));
    }
}
