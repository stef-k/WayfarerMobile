using System.Net;
using System.Net.Http.Headers;
using WayfarerMobile.Core.Maps;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class OsmLiveTileCacheClientTests
{
    private static readonly TileCacheKey Key = new("osm-standard", 12, 2048, 1362);
    private static readonly Uri TileUri = new("https://tile.openstreetmap.org/12/2048/1362.png");
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreshEntry_DoesNotSendHttp()
    {
        var store = new RecordingTileStore(new CachedTile(Key, [1, 2, 3], Now.AddDays(1), null, null, null, null));
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be used for a fresh tile."));
        var client = CreateClient(store, handler);

        var bytes = await client.GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(1, 2, 3);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExpiredEntry_304_PreservesBytesAndRefreshesMetadata()
    {
        var original = new CachedTile(Key, [4, 5, 6], Now.AddMinutes(-1), "\"v1\"", Now.AddDays(-2), null, null);
        var store = new RecordingTileStore(original);
        var handler = new RecordingHandler(request =>
        {
            request.Headers.IfNoneMatch.Should().ContainSingle(x => x.Tag == "\"v1\"");
            request.Headers.IfModifiedSince.Should().Be(original.LastModified);
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromHours(2) };
            return response;
        });

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(original.Bytes);
        store.Current!.Bytes.Should().Equal(original.Bytes);
        store.Current.FreshUntil.Should().Be(Now.AddHours(2));
        store.AtomicWrites.Should().Be(1);
    }

    [Fact]
    public async Task ExpiredEntry_200_AtomicallyReplacesBytesAndMetadata()
    {
        var store = new RecordingTileStore(new CachedTile(Key, [1], Now.AddMinutes(-1), "\"old\"", null, null, null));
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) };
            response.Headers.ETag = new EntityTagHeaderValue("\"new\"");
            response.Content.Headers.LastModified = Now;
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromDays(10) };
            return response;
        });

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(9, 8, 7);
        store.Current!.ETag.Should().Be("\"new\"");
        store.Current.LastModified.Should().Be(Now);
        store.Current.FreshUntil.Should().Be(Now.AddDays(10));
        store.AtomicWrites.Should().Be(1);
    }

    [Fact]
    public async Task ResponseWithoutUsableCachingHeaders_UsesSevenDayFallback()
    {
        var store = new RecordingTileStore();
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([7])
        });

        await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        store.Current!.FreshUntil.Should().Be(Now.AddDays(7));
    }

    [Fact]
    public async Task NoCacheResponse_IsStoredImmediatelyStale_AndNextRequestRevalidates()
    {
        var store = new RecordingTileStore();
        var requestCount = 0;
        var handler = new RecordingHandler(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 5, 6]) };
                response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
                response.Headers.ETag = new EntityTagHeaderValue("\"no-cache-v1\"");
                return response;
            }

            request.Headers.IfNoneMatch.Should().ContainSingle(x => x.Tag == "\"no-cache-v1\"");
            return new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Headers = { CacheControl = new CacheControlHeaderValue { NoCache = true } }
            };
        });
        var client = CreateClient(store, handler);

        await client.GetTileAsync(Key, TileUri, CancellationToken.None);
        var second = await client.GetTileAsync(Key, TileUri, CancellationToken.None);

        second.Should().Equal(4, 5, 6);
        handler.Requests.Should().HaveCount(2);
        store.Current!.FreshUntil.Should().Be(Now);
        store.Current.ETag.Should().Be("\"no-cache-v1\"");
    }

    [Fact]
    public async Task NoCacheRevalidationTransportFailure_PreservesEntryWithoutServingIt()
    {
        var cached = new CachedTile(Key, [4, 5, 6], Now, "\"no-cache-v1\"", Now.AddDays(-1), "no-cache", null);
        var store = new RecordingTileStore(cached);
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));

        var action = () => CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        await action.Should().ThrowAsync<HttpRequestException>();
        store.GetStored(Key).Should().Be(cached);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task NoStoreResponse_ReturnsBytes_RemovesOnlyExactPreviousEntry_AndDoesNotPersistResponse()
    {
        var unrelatedKey = new TileCacheKey("osm-standard", 12, 2049, 1362);
        var exact = new CachedTile(Key, [1], Now.AddMinutes(-1), "\"old\"", null, null, null);
        var unrelated = new CachedTile(unrelatedKey, [8], Now.AddDays(1), null, null, null, null);
        var store = new RecordingTileStore(exact, unrelated);
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return response;
        });

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(9, 8, 7);
        store.GetStored(Key).Should().BeNull();
        store.GetStored(unrelatedKey).Should().Be(unrelated);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredOrdinaryEntry_TransportFailure_ReturnsUnchangedStaleBytes()
    {
        var cached = new CachedTile(Key, [2, 3, 4], Now.AddMinutes(-1), "\"v1\"", null, "max-age=60", null);
        var store = new RecordingTileStore(cached);
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(cached.Bytes);
        store.GetStored(Key).Should().Be(cached);
        store.AtomicWrites.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task ExpiredOrdinaryEntry_PermanentFailure_DoesNotServeOrRefreshStaleBytes(HttpStatusCode statusCode)
    {
        var cached = new CachedTile(Key, [2, 3, 4], Now.AddMinutes(-1), "\"v1\"", null, "max-age=60", null);
        var store = new RecordingTileStore(cached);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().BeNull();
        store.GetStored(Key).Should().Be(cached);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredOrdinaryEntry_TransientFailure_ReturnsUnchangedStaleBytes()
    {
        var cached = new CachedTile(Key, [2, 3, 4], Now.AddMinutes(-1), "\"v1\"", null, "max-age=60", null);
        var store = new RecordingTileStore(cached);
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().Equal(cached.Bytes);
        store.GetStored(Key).Should().Be(cached);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task PastExpires_IsStoredImmediatelyStale_WithoutFallbackFreshness()
    {
        var store = new RecordingTileStore();
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([7]) };
            response.Content.Headers.Expires = Now.AddMinutes(-1);
            return response;
        });

        await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        store.Current!.Expires.Should().Be(Now.AddMinutes(-1));
        store.Current.FreshUntil.Should().Be(Now);
    }

    [Fact]
    public async Task EmptyNoStoreResponse_RemovesOnlyExactPreviousEntry_AndDoesNotServeIt()
    {
        var unrelatedKey = new TileCacheKey("osm-standard", 12, 2049, 1362);
        var exact = new CachedTile(Key, [1], Now.AddMinutes(-1), "\"old\"", null, null, null);
        var unrelated = new CachedTile(unrelatedKey, [8], Now.AddDays(1), null, null, null, null);
        var store = new RecordingTileStore(exact, unrelated);
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return response;
        });

        var bytes = await CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        bytes.Should().BeNull();
        store.GetStored(Key).Should().BeNull();
        store.GetStored(unrelatedKey).Should().Be(unrelated);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task MustRevalidateTransportFailure_PreservesEntryWithoutServingIt()
    {
        var cached = new CachedTile(Key, [4, 5, 6], Now, "\"must-v1\"", null, "must-revalidate", null);
        var store = new RecordingTileStore(cached);
        var handler = new RecordingHandler(_ => throw new HttpRequestException("offline"));

        var action = () => CreateClient(store, handler).GetTileAsync(Key, TileUri, CancellationToken.None);

        await action.Should().ThrowAsync<HttpRequestException>();
        store.GetStored(Key).Should().Be(cached);
        store.AtomicWrites.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentSameTileRequests_CoalesceToOneHttpRequest()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        });
        var client = CreateClient(new RecordingTileStore(), handler);

        var first = client.GetTileAsync(Key, TileUri, CancellationToken.None);
        var second = client.GetTileAsync(Key, TileUri, CancellationToken.None);
        await handler.FirstRequest;
        gate.SetResult();
        await Task.WhenAll(first, second);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentDistinctTiles_AreNotGloballySerialized()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
        });
        var client = CreateClient(new RecordingTileStore(), handler);

        var first = client.GetTileAsync(Key, TileUri, CancellationToken.None);
        var secondKey = new TileCacheKey("osm-standard", 12, 2049, 1362);
        var second = client.GetTileAsync(secondKey, new Uri("https://tile.openstreetmap.org/12/2049/1362.png"), CancellationToken.None);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        await Task.WhenAll(first, second);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CancelledRequest_DoesNotEnterHttpHandler()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(new RecordingTileStore(), handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => client.GetTileAsync(Key, TileUri, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().BeEmpty();
    }

    private static OsmLiveTileCacheClient CreateClient(RecordingTileStore store, RecordingHandler handler) =>
        new(new HttpClient(handler), store, new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingTileStore(params CachedTile[] entries) : ILiveTileStore
    {
        private readonly Dictionary<TileCacheKey, CachedTile> _entries = entries.ToDictionary(x => x.Key);
        public CachedTile? Current { get; private set; } = entries.LastOrDefault();
        public int AtomicWrites { get; private set; }
        public CachedTile? GetStored(TileCacheKey key) => _entries.GetValueOrDefault(key);
        public Task<CachedTile?> GetAsync(TileCacheKey key, CancellationToken cancellationToken) => Task.FromResult(GetStored(key));
        public Task WriteAtomicallyAsync(CachedTile tile, CancellationToken cancellationToken)
        {
            Current = tile;
            _entries[tile.Key] = tile;
            AtomicWrites++;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(TileCacheKey key, CancellationToken cancellationToken)
        {
            _entries.Remove(key);
            if (Current?.Key == key) Current = null;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;
        private readonly TaskCompletionSource _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<HttpRequestMessage> Requests { get; } = [];
        public Task FirstRequest => _firstRequest.Task;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            : this((request, _) => Task.FromResult(response(request))) { }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            _firstRequest.TrySetResult();
            return _response(request, cancellationToken);
        }
    }
}
