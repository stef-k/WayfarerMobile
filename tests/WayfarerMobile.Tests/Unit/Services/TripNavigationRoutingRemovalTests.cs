using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class TripNavigationRoutingRemovalTests
{
    [Fact]
    public async Task MapTargetNavigation_DoesNotContactPublicProvider_AndUsesDirectGuidance()
    {
        var transport = new RecordingRouteTransport();
        var navigation = new TripNavigationService(
            NullLogger<TripNavigationService>.Instance,
            new OsrmRoutingService(new HttpClient(transport), NullLogger<OsrmRoutingService>.Instance),
            new RouteCacheService(NullLogger<RouteCacheService>.Instance),
            Mock.Of<INavigationAudioService>(),
            new NavigationRouteBuilder(NullLogger<NavigationRouteBuilder>.Instance),
            Mock.Of<ITripStateManager>());

        var route = await navigation.CalculateRouteToCoordinatesAsync(
            37.9838, 23.7275,
            37.9715, 23.7267,
            "Map target");

        transport.RequestCount.Should().Be(0);
        route.IsDirectRoute.Should().BeTrue();
        route.Waypoints.Should().HaveCount(2);
    }

    private sealed class RecordingRouteTransport : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            const string body = """
                {"code":"Ok","routes":[{"geometry":"_p~iF~ps|U_ulLnnqC_mqNvxq`@","distance":1200,"duration":900,"legs":[]}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
