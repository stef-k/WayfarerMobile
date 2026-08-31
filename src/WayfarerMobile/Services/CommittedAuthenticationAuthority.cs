using System.Text.Json;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

public interface IProtectedAuthenticationStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    bool Remove(string key);
}

public sealed record AuthenticationAuthoritySnapshot(string? ServerUrl, string? ApiToken, Guid RoutingPartition);

public sealed class CommittedAuthenticationAuthority
{
    internal const string EnvelopeKey = "authentication_authority_v1";
    internal const string LegacyServerKey = "server_url";
    internal const string LegacyTokenKey = "api_token";
    private readonly IProtectedAuthenticationStore store;
    private readonly ILogger<CommittedAuthenticationAuthority> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private AuthenticationAuthoritySnapshot? snapshot;
    private long revision;

    public CommittedAuthenticationAuthority(IProtectedAuthenticationStore store,
        ILogger<CommittedAuthenticationAuthority> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public long Revision => Interlocked.Read(ref revision);

    public AuthenticationAuthoritySnapshot Current
    {
        get
        {
            if (snapshot == null) Task.Run(PreloadAsync).GetAwaiter().GetResult();
            return Volatile.Read(ref snapshot)!;
        }
    }

    public async Task PreloadAsync()
    {
        if (snapshot != null) return;
        await gate.WaitAsync();
        try
        {
            if (snapshot != null) return;
            var loaded = await LoadAsync();
            Volatile.Write(ref snapshot, loaded);
            Interlocked.Increment(ref revision);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CommitAsync(string serverUrl, string apiToken, CancellationToken cancellationToken = default)
    {
        var normalizedServer = HostedRouteServerIdentity.Normalize(serverUrl);
        if (normalizedServer.Length == 0) throw new ArgumentException("A valid HTTP or HTTPS server is required.", nameof(serverUrl));
        if (string.IsNullOrWhiteSpace(apiToken)) throw new ArgumentException("An API token is required.", nameof(apiToken));
        await ReplaceAsync(new(normalizedServer, apiToken, Guid.NewGuid()), cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var replacement = new AuthenticationAuthoritySnapshot(null, null, Guid.NewGuid());
            Volatile.Write(ref snapshot, replacement);
            Interlocked.Increment(ref revision);

            Exception? primaryFailure = null;
            try
            {
                await store.SetAsync(EnvelopeKey, JsonSerializer.Serialize(replacement));
            }
            catch (Exception exception)
            {
                primaryFailure = exception;
                TryRemove(EnvelopeKey, ref primaryFailure);
            }
            TryRemove(LegacyServerKey, ref primaryFailure);
            TryRemove(LegacyTokenKey, ref primaryFailure);
            if (primaryFailure != null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ReplaceAsync(AuthenticationAuthoritySnapshot replacement,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await store.SetAsync(EnvelopeKey, JsonSerializer.Serialize(replacement));
            store.Remove(LegacyServerKey);
            store.Remove(LegacyTokenKey);
            Volatile.Write(ref snapshot, replacement);
            Interlocked.Increment(ref revision);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AuthenticationAuthoritySnapshot> LoadAsync()
    {
        try
        {
            var envelope = await store.GetAsync(EnvelopeKey);
            if (TryParse(envelope, out var loaded)) return loaded!;
            var legacyServer = string.IsNullOrEmpty(envelope)
                ? await store.GetAsync(LegacyServerKey) : null;
            var legacyToken = string.IsNullOrEmpty(envelope)
                ? await store.GetAsync(LegacyTokenKey) : null;
            var normalizedServer = HostedRouteServerIdentity.Normalize(legacyServer);
            var migrated = normalizedServer.Length > 0 && !string.IsNullOrWhiteSpace(legacyToken)
                ? new AuthenticationAuthoritySnapshot(normalizedServer, legacyToken, Guid.NewGuid())
                : new AuthenticationAuthoritySnapshot(null, null, Guid.NewGuid());
            await store.SetAsync(EnvelopeKey, JsonSerializer.Serialize(migrated));
            store.Remove(LegacyServerKey);
            store.Remove(LegacyTokenKey);
            return migrated;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Protected authentication storage is unavailable");
            return new(null, null, Guid.NewGuid());
        }
    }

    private static bool TryParse(string? value, out AuthenticationAuthoritySnapshot? parsed)
    {
        parsed = null;
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            var candidate = JsonSerializer.Deserialize<AuthenticationAuthoritySnapshot>(value);
            if (candidate == null || candidate.RoutingPartition == Guid.Empty) return false;
            var hasServer = !string.IsNullOrEmpty(candidate.ServerUrl);
            var hasToken = !string.IsNullOrEmpty(candidate.ApiToken);
            if (hasServer != hasToken) return false;
            if (hasServer && HostedRouteServerIdentity.Normalize(candidate.ServerUrl) != candidate.ServerUrl)
                return false;
            parsed = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void TryRemove(string key, ref Exception? primaryFailure)
    {
        try
        {
            store.Remove(key);
        }
        catch (Exception exception)
        {
            if (primaryFailure == null) primaryFailure = exception;
            else logger.LogWarning("Secondary protected authentication cleanup failed: {FailureType}",
                exception.GetType().Name);
        }
    }
}
