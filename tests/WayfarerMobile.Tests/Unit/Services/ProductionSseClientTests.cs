using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Exercises the linked production transport with controlled HTTP bodies and retry delays.</summary>
public class ProductionSseClientTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RemoteDisconnect_ReconnectsThroughSharedGroupAndVisitPath(bool ioFailure, bool visits)
    {
        using var handler = new ResponseHandler(attempt => attempt == 1
            ? Ok(ioFailure ? new ControlledStream(remoteFailure: true) : new MemoryStream())
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(handler);
        int connected = 0;
        var retries = new List<(int, int)>();
        client.Connected += (_, _) => { Assert.True(client.IsConnected); connected++; };
        client.Reconnecting += (_, e) =>
        {
            Assert.False(client.IsConnected);
            retries.Add((e.Attempt, e.DelayMs));
        };

        await Subscribe(client, visits).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, connected);
        Assert.False(client.IsConnected);
        Assert.Equal([(1, 1000)], retries);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal(
            visits ? "/api/mobile/sse/visits" : "/api/mobile/sse/group/team", request));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LocalCancellation_UnblocksReadWithoutReconnect(bool external, bool ioOnClose)
    {
        using var stream = new ControlledStream(ioOnClose: ioOnClose);
        using var handler = new ResponseHandler(_ => Ok(stream));
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        int retries = 0;
        client.Reconnecting += (_, _) => retries++;
        var subscription = client.SubscribeToGroupAsync("team", cancellation.Token);
        await stream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        if (external) cancellation.Cancel();
        else client.Stop();
        await subscription.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stream.Closed);
        Assert.False(client.IsConnected);
        Assert.Single(handler.Requests);
        Assert.Equal(0, retries);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task PermanentStatus_RemainsTerminal(int status)
    {
        using var handler = new ResponseHandler(_ => new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("denied") });
        using var client = CreateClient(handler);
        var errors = new List<SsePermanentErrorEventArgs>();
        int retries = 0;
        client.PermanentError += (_, e) => errors.Add(e);
        client.Reconnecting += (_, _) => retries++;

        await client.SubscribeToGroupAsync("team");

        Assert.Equal(status, Assert.Single(errors).StatusCode);
        Assert.Equal("denied", errors[0].Message);
        Assert.Single(handler.Requests);
        Assert.Equal(0, retries);
        Assert.False(client.IsConnected);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task AdmissionStatus_PreservesCappedBackoff(int status)
    {
        using var handler = new ResponseHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        var retries = new List<(int, int)>();
        var delays = new List<int>();
        int permanentErrors = 0;
        client.PermanentError += (_, _) => permanentErrors++;
        client.Reconnecting += (_, e) => retries.Add((e.Attempt, e.DelayMs));
        client.ReconnectDelayAsync = (delay, token) =>
        {
            delays.Add(delay);
            if (delays.Count == 4) cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await client.SubscribeToVisitsAsync(cancellation.Token);

        Assert.Equal([1000, 2000, 5000, 5000], delays);
        Assert.Equal([(1, 1000), (2, 2000), (3, 5000), (4, 5000)], retries);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(0, permanentErrors);
    }

    [Fact]
    public async Task EventsAndHeartbeat_PreservePayloadsAndRetryState()
    {
        const string frames = """
            : heartbeat
            data: {"type":"location","userName":"walker","latitude":37.9,"longitude":23.7}

            data: {"type":"member-joined","userId":"member"}

            data: {"type":"visit_started","visitId":"11111111-1111-1111-1111-111111111111","tripName":"Trip","placeName":"Place","latitude":37.9}


            """;
        using var handler = new ResponseHandler(_ => Ok(new MemoryStream(Encoding.UTF8.GetBytes(frames))));
        using var client = CreateClient(handler);
        var events = new List<string>();
        client.HeartbeatReceived += (_, _) => events.Add("heartbeat");
        client.LocationReceived += (_, e) =>
        {
            Assert.Equal("walker", e.Location.UserName);
            Assert.Equal(37.9, e.Location.Latitude);
            events.Add("location");
        };
        client.MembershipReceived += (_, e) =>
        {
            Assert.Equal("member", e.Membership.UserId);
            Assert.Equal("member-joined", e.Membership.Action);
            events.Add("membership");
        };
        client.VisitStarted += (_, e) =>
        {
            Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), e.Visit.VisitId);
            Assert.Equal("Place", e.Visit.PlaceName);
            Assert.Equal("Trip", e.Visit.TripName);
            events.Add("visit");
        };
        client.Reconnecting += (_, e) =>
        {
            Assert.Equal(1, e.Attempt);
            Assert.Equal(1000, e.DelayMs);
            events.Add("retry");
            client.Stop();
        };

        await client.SubscribeToVisitsAsync();

        Assert.Equal(["heartbeat", "location", "membership", "visit", "retry"], events);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MissingToken_RemainsTerminalWithoutRequests()
    {
        using var handler = new ResponseHandler(_ => Ok(new MemoryStream()));
        using var client = CreateClient(handler, token: "");
        int retries = 0;
        client.Reconnecting += (_, _) => retries++;
        await client.SubscribeToVisitsAsync();
        Assert.Empty(handler.Requests);
        Assert.Equal(0, retries);
    }

    /// <summary>Uses the real named-client boundary while bypassing only elapsed retry time.</summary>
    private static SseClient CreateClient(ResponseHandler handler, string token = "test-token")
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.ServerUrl).Returns("https://example.test/");
        settings.SetupGet(s => s.ApiToken).Returns(token);
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        factory.Setup(f => f.CreateClient("SSE")).Returns(new HttpClient(handler));
        return new SseClient(settings.Object, NullLogger<SseClient>.Instance, factory.Object)
        {
            ReconnectDelayAsync = (_, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        };
    }

    private static Task Subscribe(SseClient client, bool visits) => visits
        ? client.SubscribeToVisitsAsync() : client.SubscribeToGroupAsync("team");

    private static HttpResponseMessage Ok(Stream stream) => new(HttpStatusCode.OK)
    { Content = new StreamContent(stream) };

    /// <summary>Captures real outgoing requests and supplies deterministic response bodies.</summary>
    private sealed class ResponseHandler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("text/event-stream", Assert.Single(request.Headers.Accept).MediaType);
            Requests.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(response(Requests.Count));
        }
    }

    /// <summary>A read ignores cancellation until force-close, like the transport Stop must unblock.</summary>
    private sealed class ControlledStream(bool remoteFailure = false, bool ioOnClose = false) : Stream
    {
        // Inline continuations deliberately expose close-before-cancel races without sleeps.
        private readonly TaskCompletionSource<int> _read = new();
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Closed { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            return remoteFailure ? ValueTask.FromException<int>(new IOException("remote body failed"))
                : new ValueTask<int>(_read.Task);
        }
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            _read.TrySetException(ioOnClose ? new IOException("closed") : new ObjectDisposedException("stream"));
            base.Dispose(disposing);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
