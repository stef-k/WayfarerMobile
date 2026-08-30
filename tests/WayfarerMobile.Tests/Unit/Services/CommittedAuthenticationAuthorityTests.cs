using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class CommittedAuthenticationAuthorityTests
{
    [Fact]
    public async Task Commit_RoundTripsStablePartitionAcrossOwnerRecreation()
    {
        var storage = new MemoryProtectedStore();
        var first = Create(storage);
        await first.CommitAsync("HTTPS://WAYFARER.TEST/", "secret-token");
        var committed = first.Current;

        var recreated = Create(storage);
        await recreated.PreloadAsync();

        recreated.Current.Should().Be(committed);
        recreated.Current.RoutingPartition.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RecommitAndClear_RotatePartitionWithoutComparingToken()
    {
        var storage = new MemoryProtectedStore();
        var authority = Create(storage);
        await authority.CommitAsync("https://wayfarer.test", "same-token");
        var first = authority.Current.RoutingPartition;

        await authority.CommitAsync("https://wayfarer.test", "same-token");
        var second = authority.Current.RoutingPartition;
        await authority.ClearAsync();

        second.Should().NotBe(first);
        authority.Current.RoutingPartition.Should().NotBe(second);
        authority.Current.ServerUrl.Should().BeNull();
        authority.Current.ApiToken.Should().BeNull();
        storage.Values.Keys.Should().ContainSingle(key => key == CommittedAuthenticationAuthority.EnvelopeKey);
    }

    private static CommittedAuthenticationAuthority Create(IProtectedAuthenticationStore storage) =>
        new(storage, NullLogger<CommittedAuthenticationAuthority>.Instance);

    private sealed class MemoryProtectedStore : IProtectedAuthenticationStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string key) =>
            Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);
        public Task SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
        public bool Remove(string key) => Values.Remove(key);
    }
}
