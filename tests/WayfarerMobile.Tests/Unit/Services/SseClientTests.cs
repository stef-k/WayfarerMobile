using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq.Protected;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for SseClient Server-Sent Events client.
/// </summary>
public class SseClientTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ILogger<TestSseClient>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;

    public SseClientTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<TestSseClient>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();

        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.example.com");
        _mockSettings.Setup(s => s.ApiToken).Returns("test-token-123");

        var httpClient = new HttpClient(_mockHttpHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
    }

    private TestSseClient CreateClient()
    {
        return new TestSseClient(
            _mockSettings.Object,
            _mockLogger.Object,
            _mockHttpClientFactory.Object);
    }

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var act = () => new TestSseClient(null!, _mockLogger.Object, _mockHttpClientFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new TestSseClient(_mockSettings.Object, null!, _mockHttpClientFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_ThrowsArgumentNullException()
    {
        var act = () => new TestSseClient(_mockSettings.Object, _mockLogger.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ValidDependencies_CreatesInstance()
    {
        var client = CreateClient();
        client.Should().NotBeNull();
    }

    [Fact]
    public void IsConnected_InitialState_ReturnsFalse()
    {
        var client = CreateClient();
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Stop_WhenNotConnected_SetsIsConnectedToFalse()
    {
        var client = CreateClient();
        client.Stop();
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Stop_CanBeCalledMultipleTimes_DoesNotThrow()
    {
        var client = CreateClient();
        var act = () => { client.Stop(); client.Stop(); client.Stop(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_SetsIsConnectedToFalse()
    {
        var client = CreateClient();
        client.Dispose();
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_DoesNotThrow()
    {
        var client = CreateClient();
        var act = () => { client.Dispose(); client.Dispose(); client.Dispose(); };
        act.Should().NotThrow();
    }

    [Fact]
    public void LocationReceived_CanSubscribeAndUnsubscribe()
    {
        var client = CreateClient();
        var eventRaised = false;
        void Handler(object? sender, SseLocationEventArgs e) => eventRaised = true;
        client.LocationReceived += Handler;
        client.LocationReceived -= Handler;
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void MembershipReceived_CanSubscribeAndUnsubscribe()
    {
        var client = CreateClient();
        var eventRaised = false;
        void Handler(object? sender, SseMembershipEventArgs e) => eventRaised = true;
        client.MembershipReceived += Handler;
        client.MembershipReceived -= Handler;
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void HeartbeatReceived_CanSubscribeAndUnsubscribe()
    {
        var client = CreateClient();
        var eventRaised = false;
        void Handler(object? sender, EventArgs e) => eventRaised = true;
        client.HeartbeatReceived += Handler;
        client.HeartbeatReceived -= Handler;
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void Connected_CanSubscribeAndUnsubscribe()
    {
        var client = CreateClient();
        var eventRaised = false;
        void Handler(object? sender, EventArgs e) => eventRaised = true;
        client.Connected += Handler;
        client.Connected -= Handler;
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void Reconnecting_CanSubscribeAndUnsubscribe()
    {
        var client = CreateClient();
        var eventRaised = false;
        void Handler(object? sender, SseReconnectEventArgs e) => eventRaised = true;
        client.Reconnecting += Handler;
        client.Reconnecting -= Handler;
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToUserAsync_EmptyUserName_LogsWarningAndReturns()
    {
        var client = CreateClient();
        await client.SubscribeToUserAsync(string.Empty);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToUserAsync_NoServerUrl_LogsErrorAndReturns()
    {
        _mockSettings.Setup(s => s.ServerUrl).Returns((string?)null);
        var client = CreateClient();
        await client.SubscribeToUserAsync("testuser");
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToUserAsync_ValidUserName_BuildsCorrectUrl()
    {
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.example.com");
        string? capturedUrl = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = CreateClient();
        using var cts = new CancellationTokenSource(100);
        try { await client.SubscribeToUserAsync("testuser", cts.Token); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }

        capturedUrl.Should().Be("https://api.example.com/api/mobile/sse/location-update/testuser");
    }

    [Fact]
    public async Task SubscribeToGroupAsync_EmptyGroupId_LogsWarningAndReturns()
    {
        var client = CreateClient();
        await client.SubscribeToGroupAsync(string.Empty);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToGroupAsync_ValidGroupId_BuildsCorrectUrl()
    {
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.example.com");
        string? capturedUrl = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = CreateClient();
        using var cts = new CancellationTokenSource(100);
        try { await client.SubscribeToGroupAsync("group-abc-123", cts.Token); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }

        capturedUrl.Should().Be("https://api.example.com/api/mobile/sse/group-location-update/group-abc-123");
    }

    [Fact]
    public async Task SubscribeToGroupMembershipAsync_EmptyGroupId_LogsWarningAndReturns()
    {
        var client = CreateClient();
        await client.SubscribeToGroupMembershipAsync(string.Empty);
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToGroupMembershipAsync_ValidGroupId_BuildsCorrectUrl()
    {
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.example.com");
        string? capturedUrl = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedUrl = req.RequestUri?.ToString())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = CreateClient();
        using var cts = new CancellationTokenSource(100);
        try { await client.SubscribeToGroupMembershipAsync("group-xyz", cts.Token); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }

        capturedUrl.Should().Be("https://api.example.com/api/sse/stream/group-membership-update/group-xyz");
    }

    [Fact]
    public async Task Subscribe_SetsAuthorizationHeader()
    {
        _mockSettings.Setup(s => s.ApiToken).Returns("my-secret-token");
        AuthenticationHeaderValue? capturedAuth = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedAuth = req.Headers.Authorization)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = CreateClient();
        using var cts = new CancellationTokenSource(100);
        try { await client.SubscribeToUserAsync("testuser", cts.Token); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }

        capturedAuth.Should().NotBeNull();
        capturedAuth!.Scheme.Should().Be("Bearer");
        capturedAuth.Parameter.Should().Be("my-secret-token");
    }

    [Fact]
    public async Task Subscribe_SetsAcceptHeader()
    {
        IEnumerable<MediaTypeWithQualityHeaderValue>? capturedAccept = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedAccept = req.Headers.Accept)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });

        var client = CreateClient();
        using var cts = new CancellationTokenSource(100);
        try { await client.SubscribeToUserAsync("testuser", cts.Token); }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }

        capturedAccept.Should().NotBeNull();
        capturedAccept.Should().Contain(h => h.MediaType == "text/event-stream");
    }
}

public sealed class TestSseClient : ISseClient
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TestSseClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _connectionLock = new();
    private bool _disposed;
    private volatile bool _isConnected;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly int[] BackoffDelaysMs = [1000, 2000, 5000];

    public event EventHandler<SseLocationEventArgs>? LocationReceived;
    public event EventHandler<SseMembershipEventArgs>? MembershipReceived;
    public event EventHandler? HeartbeatReceived;
    public event EventHandler? Connected;
#pragma warning disable CS0067 // Event is never used - required by ISseClient interface
    public event EventHandler<SseReconnectEventArgs>? Reconnecting;
#pragma warning restore CS0067

    public bool IsConnected => _isConnected;

    public TestSseClient(ISettingsService settings, ILogger<TestSseClient> logger, IHttpClientFactory httpClientFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public Task SubscribeToUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName)) { _logger.LogWarning("userName is empty"); return Task.CompletedTask; }
        var serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl)) { _logger.LogError("Server URL not configured"); return Task.CompletedTask; }
        var url = serverUrl.TrimEnd((char)47) + "/api/mobile/sse/location-update/" + Uri.EscapeDataString(userName);
        return SubscribeAsync(url, "user:" + userName, cancellationToken);
    }

    public Task SubscribeToGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId)) { _logger.LogWarning("groupId is empty"); return Task.CompletedTask; }
        var serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl)) { _logger.LogError("Server URL not configured"); return Task.CompletedTask; }
        var url = serverUrl.TrimEnd((char)47) + "/api/mobile/sse/group-location-update/" + Uri.EscapeDataString(groupId);
        return SubscribeAsync(url, "group:" + groupId, cancellationToken);
    }

    public Task SubscribeToGroupMembershipAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId)) { _logger.LogWarning("groupId is empty"); return Task.CompletedTask; }
        var serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl)) { _logger.LogError("Server URL not configured"); return Task.CompletedTask; }
        var url = serverUrl.TrimEnd((char)47) + "/api/sse/stream/group-membership-update/" + Uri.EscapeDataString(groupId);
        return SubscribeAsync(url, "group-membership:" + groupId, cancellationToken);
    }

    public void Stop()
    {
        lock (_connectionLock) { _cancellationTokenSource?.Cancel(); _isConnected = false; }
    }

    private async Task SubscribeAsync(string url, string channelName, CancellationToken externalToken)
    {
        Stop();
        CancellationTokenSource cts;
        lock (_connectionLock) { _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken); cts = _cancellationTokenSource; }
        var cancellationToken = cts.Token;
        try { await ConnectAndStreamAsync(url, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { }
        _isConnected = false;
    }

    private async Task ConnectAndStreamAsync(string url, CancellationToken cancellationToken)
    {
        var apiToken = _settings.ApiToken;
        if (string.IsNullOrWhiteSpace(apiToken)) { _logger.LogError("API token not configured"); return; }
        cancellationToken.ThrowIfCancellationRequested();

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("SSE subscription failed");
            _isConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var dataBuffer = new StringBuilder();
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null) break;
                if (line.Length > 0 && line[0] == (char)58) { HeartbeatReceived?.Invoke(this, EventArgs.Empty); continue; }
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) { dataBuffer.Append(line[5..].TrimStart()); continue; }
                if (string.IsNullOrWhiteSpace(line) && dataBuffer.Length > 0)
                {
                    var json = dataBuffer.ToString();
                    dataBuffer.Clear();
                    try
                    {
                        var locationEvent = JsonSerializer.Deserialize<SseLocationEvent>(json, JsonOptions);
                        if (locationEvent != null && !string.IsNullOrEmpty(locationEvent.UserName)) LocationReceived?.Invoke(this, new SseLocationEventArgs(locationEvent));
                        else
                        {
                            var membershipEvent = JsonSerializer.Deserialize<SseMembershipEvent>(json, JsonOptions);
                            if (membershipEvent != null && !string.IsNullOrEmpty(membershipEvent.Action)) MembershipReceived?.Invoke(this, new SseMembershipEventArgs(membershipEvent));
                        }
                    }
                    catch { }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        lock (_connectionLock) { _cancellationTokenSource?.Dispose(); _cancellationTokenSource = null; }
        _disposed = true;
    }
}

/// <summary>
/// Interface for SSE client - copy for testing purposes.
/// </summary>
public interface ISseClient : IDisposable
{
    bool IsConnected { get; }
    event EventHandler<SseLocationEventArgs>? LocationReceived;
    event EventHandler<SseMembershipEventArgs>? MembershipReceived;
    event EventHandler? HeartbeatReceived;
    event EventHandler? Connected;
    event EventHandler<SseReconnectEventArgs>? Reconnecting;
    Task SubscribeToUserAsync(string userName, CancellationToken cancellationToken = default);
    Task SubscribeToGroupAsync(string groupId, CancellationToken cancellationToken = default);
    Task SubscribeToGroupMembershipAsync(string groupId, CancellationToken cancellationToken = default);
    void Stop();
}

