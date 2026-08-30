using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class HostedRoutingServiceTests
{
    [Fact]
    public async Task EligibleRequest_UsesAuthenticatedWayfarerRouteAndReturnsTransientRoute()
    {
        var api = new Mock<IHostedRoutingApiClient>();
        var profileId = Guid.NewGuid();
        api.Setup(client => client.GetCapabilityAsync(profileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRoutingCapability.Available(
                profileId, "provider", Guid.NewGuid(), "mapping", "persistent", []));
        api.Setup(client => client.GetRouteAsync(It.IsAny<HostedRouteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HostedRouteResponse.ValidForTest(profileId));
        var service = new HostedRoutingService(api.Object, NullLogger<HostedRoutingService>.Instance);

        var result = await service.RequestRouteAsync(HostedRouteRequestContext.ForTest(profileId));

        result.Outcome.Should().Be(HostedRoutingOutcome.Success);
        result.Route.Should().NotBeNull();
        result.Route!.IsDirectRoute.Should().BeFalse();
        api.Verify(client => client.GetRouteAsync(
            It.Is<HostedRouteRequest>(request => request.TransportProfileId == profileId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
