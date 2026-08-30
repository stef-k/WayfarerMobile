using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Repositories;

public sealed class RetainedWayfarerRouteRepositoryTests : IAsyncLifetime
{
    private static readonly Guid ProfileId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigurationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PartitionA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PartitionB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string AuthorityIdentity = "v1.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"wayfarer-route-{Guid.NewGuid():N}.db3");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PersistentRoute_RoundTripsAcrossRecreation_AndRemainsPartitionIsolated()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
        await using (var owner = await CreateOwnerAsync())
        {
            var result = await owner.Repository.SaveAsync(Candidate("first"), PartitionA, receipt, () => true);
            result.Should().Be(RetainedRouteSaveResult.Saved);
        }

        await using var recreated = await CreateOwnerAsync();
        var retained = await recreated.Repository.SelectAsync(Context(), PartitionA, receipt.AddDays(30), () => true);
        var otherAccount = await recreated.Repository.SelectAsync(Context(), PartitionB, receipt.AddDays(30), () => true);

        retained.Should().NotBeNull();
        retained!.Route.Waypoints.Should().ContainSingle(point => point.Longitude == 23.005);
        retained.Route.HostedProvenance!.IsRetained.Should().BeTrue();
        retained.Route.HostedProvenance.Age.Should().Be(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(5));
        otherAccount.Should().BeNull();
    }

    [Fact]
    public async Task ValidReplacementCommits_AndRejectedRefreshPreservesIt()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        (await owner.Repository.SaveAsync(Candidate("prior"), PartitionA, receipt, () => true))
            .Should().Be(RetainedRouteSaveResult.Saved);
        (await owner.Repository.SaveAsync(Candidate("replacement", middleLongitude: 23.007),
            PartitionA, receipt.AddMinutes(1), () => true)).Should().Be(RetainedRouteSaveResult.Saved);

        var unauthorized = Candidate("unauthorized", middleLongitude: 23.009) with
        {
            Metadata = Candidate("unused").Metadata with { StorageMode = "transient" }
        };
        (await owner.Repository.SaveAsync(unauthorized, PartitionA, receipt.AddMinutes(2), () => true))
            .Should().Be(RetainedRouteSaveResult.Rejected);

        var retained = await owner.Repository.SelectAsync(Context(), PartitionA, receipt.AddMinutes(3), () => true);
        retained!.Route.Waypoints.Should().ContainSingle(point => point.Longitude == 23.007);
    }

    [Fact]
    public async Task MatchingRetainedRoute_IsSelectedWithoutAnyHostedApiContact()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        await owner.Repository.SaveAsync(Candidate("offline"), PartitionA, receipt, () => true);
        var service = new RetainedWayfarerRoutingService(owner.Repository,
            NullLogger<RetainedWayfarerRoutingService>.Instance);

        var route = await service.TrySelectOfflineAsync(Context(), PartitionA, receipt.AddHours(1), () => true);

        route.Should().NotBeNull();
        route!.HostedProvenance!.IsRetained.Should().BeTrue();
    }

    [Fact]
    public async Task OlderDelayedSave_CannotOverwriteNewerCurrentSave()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 11, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        await owner.Repository.SaveAsync(Candidate("prior", middleLongitude: 23.003),
            PartitionA, receipt, () => true);
        var releaseOlder = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var olderIsCurrent = true;
        var older = Task.Run(async () =>
        {
            await releaseOlder.Task;
            return await owner.Repository.SaveAsync(Candidate("older-a", middleLongitude: 23.004),
                PartitionA, receipt.AddMinutes(1), () => olderIsCurrent);
        });

        var newer = await owner.Repository.SaveAsync(Candidate("newer-b", middleLongitude: 23.008),
            PartitionA, receipt.AddMinutes(2), () => true);
        olderIsCurrent = false;
        releaseOlder.SetResult();

        newer.Should().Be(RetainedRouteSaveResult.Saved);
        (await older).Should().Be(RetainedRouteSaveResult.Superseded);
        var retained = await owner.Repository.SelectAsync(Context(), PartitionA,
            receipt.AddMinutes(3), () => true);
        retained!.Route.Waypoints.Should().ContainSingle(point => point.Longitude == 23.008);
    }

    private async Task<RepositoryOwner> CreateOwnerAsync()
    {
        var connection = new SQLiteAsyncConnection(databasePath);
        await RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None);
        return new(connection, new RetainedWayfarerRouteRepository(connection));
    }

    private static HostedRouteCandidate Candidate(string instruction, double middleLongitude = 23.005)
    {
        var context = Context();
        var route = new NavigationRoute
        {
            Waypoints =
            [
                new() { Longitude = 23, Latitude = 37 },
                new() { Longitude = middleLongitude, Latitude = 37.005 },
                new() { Longitude = 23.01, Latitude = 37.01 }
            ],
            Steps =
            [
                new() { Instruction = instruction, ManeuverType = "continue", Longitude = 23,
                    Latitude = 37, DistanceMeters = 1500, DurationSeconds = 900 }
            ],
            DestinationName = "must-not-be-stored",
            TotalDistanceMeters = 1500,
            EstimatedDuration = TimeSpan.FromSeconds(900),
            Attribution = [new("Powered by Wayfarer test", "https://example.test/attribution")]
        };
        return new(route, context, ProfileId, AuthorityIdentity,
            new("geoapify", ConfigurationId, "mapping-v1", "persistent"),
            new DateTimeOffset(2026, 8, 31, 7, 55, 0, TimeSpan.Zero));
    }

    private static HostedRouteRequestContext Context() => new(ProfileId, "walk", "active",
        new(23, 37), new(23.01, 37.01), [new(23.002, 37.002), new(23.002, 37.002)],
        "must-not-be-stored", 7, 3, "https://wayfarer.test", "place:private", "hosted");

    private sealed class RepositoryOwner(
        SQLiteAsyncConnection connection,
        RetainedWayfarerRouteRepository repository) : IAsyncDisposable
    {
        public RetainedWayfarerRouteRepository Repository { get; } = repository;

        public async ValueTask DisposeAsync() => await connection.CloseAsync();
    }
}
