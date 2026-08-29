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
    [Theory]
    [InlineData(true, HttpStatusCode.OK, true)]
    [InlineData(false, HttpStatusCode.Forbidden, false)]
    public async Task UpdatePeerVisibilityAsync_SendsOneAuthenticatedPostAndReturnsStatusResult(
        bool disabled,
        HttpStatusCode statusCode,
        bool expectedResult)
    {
        var groupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        HttpMethod? capturedMethod = null;
        Uri? capturedUri = null;
        string? capturedAuthorization = null;
        string? capturedContent = null;
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
            })
            .ReturnsAsync(new HttpResponseMessage(statusCode));

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

        var result = await service.UpdatePeerVisibilityAsync(groupId, disabled);

        result.Should().Be(expectedResult);
        contactCount.Should().Be(1);
        capturedMethod.Should().Be(HttpMethod.Post);
        capturedUri.Should().Be(new Uri($"https://api.example.com/api/mobile/groups/{groupId}/peer-visibility"));
        capturedAuthorization.Should().Be("Bearer test-token-123");
        using var content = JsonDocument.Parse(capturedContent!);
        content.RootElement.GetProperty("disabled").GetBoolean().Should().Be(disabled);
    }

    [Fact]
    public async Task UpdatePeerVisibilityAsync_ForwardsCancellationToSendAsync()
    {
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
            {
                using var registration = token.Register(() => handlerCancellationObserved.TrySetResult());
                sendStarted.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        using var httpClient = new HttpClient(handler.Object);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(factory => factory.CreateClient("WayfarerApi")).Returns(httpClient);
        var settings = new Mock<ISettingsService>();
        settings.Setup(value => value.IsConfigured).Returns(true);
        settings.Setup(value => value.ServerUrl).Returns("https://api.example.com");
        var service = new GroupsService(
            settings.Object,
            Mock.Of<ILogger<GroupsService>>(),
            httpClientFactory.Object);
        using var cancellationSource = new CancellationTokenSource();

        var resultTask = service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: true, cancellationSource.Token);
        await sendStarted.Task;
        cancellationSource.Cancel();

        await handlerCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        (await resultTask).Should().BeFalse();
    }
}
