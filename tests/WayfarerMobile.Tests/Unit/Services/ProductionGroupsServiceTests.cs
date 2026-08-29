using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq.Protected;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Regression tests for the production <see cref="GroupsService"/> peer-visibility request.
/// </summary>
public class ProductionGroupsServiceTests
{
    [Fact]
    public async Task UpdatePeerVisibilityAsync_SendsAuthenticatedPostWithDisabledState()
    {
        var groupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        using var cancellationSource = new CancellationTokenSource();
        var cancellationToken = cancellationSource.Token;
        HttpMethod? capturedMethod = null;
        Uri? capturedUri = null;
        string? capturedAuthorization = null;
        string? capturedContent = null;
        CancellationToken capturedCancellationToken = default;
        var contactCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (request, token) =>
            {
                contactCount++;
                capturedMethod = request.Method;
                capturedUri = request.RequestUri;
                capturedAuthorization = request.Headers.Authorization?.ToString();
                capturedContent = await request.Content!.ReadAsStringAsync(token);
                capturedCancellationToken = token;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        using var httpClient = new HttpClient(handler.Object);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient("WayfarerApi")).Returns(httpClient);
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.IsConfigured).Returns(true);
        settings.Setup(value => value.ServerUrl).Returns("https://api.example.com");
        settings.Setup(value => value.ApiToken).Returns("test-token-123");
        var service = new GroupsService(
            settings.Object,
            Mock.Of<ILogger<GroupsService>>(),
            httpClientFactory.Object);

        var result = await service.UpdatePeerVisibilityAsync(groupId, disabled: true, cancellationToken);

        result.Should().BeTrue();
        contactCount.Should().Be(1);
        capturedMethod.Should().Be(HttpMethod.Post);
        capturedUri.Should().Be(new Uri($"https://api.example.com/api/mobile/groups/{groupId}/peer-visibility"));
        capturedAuthorization.Should().Be("Bearer test-token-123");
        capturedCancellationToken.Should().Be(cancellationToken);
        using var content = JsonDocument.Parse(capturedContent!);
        content.RootElement.GetProperty("disabled").GetBoolean().Should().BeTrue();
    }
}
