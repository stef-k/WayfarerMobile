using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace WayfarerMobile.Core.Maps;

public readonly record struct TileCacheKey(string ProviderId, int Zoom, int X, int Y);

public sealed record CachedTile(
    TileCacheKey Key,
    byte[] Bytes,
    DateTimeOffset FreshUntil,
    string? ETag,
    DateTimeOffset? LastModified,
    string? CacheControl,
    DateTimeOffset? Expires);

public interface ILiveTileStore
{
    Task<CachedTile?> GetAsync(TileCacheKey key, CancellationToken cancellationToken);
    Task WriteAtomicallyAsync(CachedTile tile, CancellationToken cancellationToken);
    Task RemoveAsync(TileCacheKey key, CancellationToken cancellationToken);
}

/// <summary>Applies HTTP caching semantics to renderer-requested OSM Standard tiles.</summary>
public sealed class OsmLiveTileCacheClient
{
    public const string ProviderId = "osm-standard";
    public static readonly TimeSpan FallbackFreshness = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly ILiveTileStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<TileCacheKey, Task<byte[]?>> _inflight = new();

    public OsmLiveTileCacheClient(HttpClient httpClient, ILiveTileStore store, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<byte[]?> GetTileAsync(TileCacheKey key, Uri tileUri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cached = await _store.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null && cached.FreshUntil > _timeProvider.GetUtcNow())
            return cached.Bytes;

        cancellationToken.ThrowIfCancellationRequested();
        var request = _inflight.GetOrAdd(key, _ => FetchAsync(key, tileUri, cached));
        try
        {
            return await request.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (request.IsCompleted)
                _inflight.TryRemove(new KeyValuePair<TileCacheKey, Task<byte[]?>>(key, request));
        }
    }

    private async Task<byte[]?> FetchAsync(TileCacheKey key, Uri tileUri, CachedTile? cached)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, tileUri);
        if (!string.IsNullOrWhiteSpace(cached?.ETag))
            request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        if (cached?.LastModified is not null)
            request.Headers.IfModifiedSince = cached.LastModified;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cached is not null && !RequiresSuccessfulValidation(cached.CacheControl)) return cached.Bytes;
            throw;
        }

        using (response)
        {
            var now = _timeProvider.GetUtcNow();
            var freshUntil = ResolveFreshUntil(response, now,
                response.StatusCode == HttpStatusCode.NotModified ? cached?.CacheControl : null);

            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                var refreshed = cached with
                {
                    FreshUntil = freshUntil,
                    ETag = response.Headers.ETag?.Tag ?? cached.ETag,
                    LastModified = response.Content?.Headers.LastModified ?? cached.LastModified,
                    CacheControl = response.Headers.CacheControl?.ToString() ?? cached.CacheControl,
                    Expires = response.Content?.Headers.Expires ?? cached.Expires
                };
                await _store.WriteAtomicallyAsync(refreshed, CancellationToken.None).ConfigureAwait(false);
                return refreshed.Bytes;
            }

            if (!response.IsSuccessStatusCode)
                return cached is not null
                    && IsTransientFailure(response.StatusCode)
                    && !RequiresSuccessfulValidation(cached.CacheControl)
                    ? cached.Bytes
                    : null;

            var noStore = response.Headers.CacheControl?.NoStore == true;
            if (noStore)
                await _store.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length == 0)
                return cached is not null
                    && !RequiresSuccessfulValidation(cached.CacheControl)
                    && !noStore
                    ? cached.Bytes
                    : null;

            if (noStore)
                return bytes;

            var replacement = new CachedTile(
                key,
                bytes,
                freshUntil,
                response.Headers.ETag?.Tag,
                response.Content.Headers.LastModified,
                response.Headers.CacheControl?.ToString(),
                response.Content.Headers.Expires);
            await _store.WriteAtomicallyAsync(replacement, CancellationToken.None).ConfigureAwait(false);
            return bytes;
        }
    }

    private static DateTimeOffset ResolveFreshUntil(
        HttpResponseMessage response,
        DateTimeOffset now,
        string? inheritedCacheControl)
    {
        var cacheControl = response.Headers.CacheControl;
        if (cacheControl is null && !string.IsNullOrWhiteSpace(inheritedCacheControl))
            CacheControlHeaderValue.TryParse(inheritedCacheControl, out cacheControl);

        if (cacheControl?.NoCache == true || cacheControl?.NoStore == true || cacheControl?.MustRevalidate == true)
            return now;

        var maxAge = cacheControl?.SharedMaxAge ?? cacheControl?.MaxAge;
        if (maxAge is { } age && age >= TimeSpan.Zero)
            return now.Add(age);

        var expires = response.Content?.Headers.Expires;
        if (expires is { } expiry)
            return expiry > now ? expiry : now;

        return now.Add(FallbackFreshness);
    }

    private static bool RequiresSuccessfulValidation(string? cacheControl) =>
        CacheControlHeaderValue.TryParse(cacheControl, out var parsed)
        && (parsed.NoCache || parsed.NoStore || parsed.MustRevalidate);

    private static bool IsTransientFailure(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;
}
