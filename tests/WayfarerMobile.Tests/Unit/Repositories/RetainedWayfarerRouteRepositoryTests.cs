using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Repositories;

[Collection("SQLite")]
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

        for (var index = 1; index < RetainedWayfarerRouteRepository.MaximumRoutes; index++)
        {
            await owner.Repository.SaveAsync(Candidate($"fill-{index}", context: UniqueContext(index)),
                PartitionB, receipt.AddMinutes(3), () => true);
        }
        var selectionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSelection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinatedSelection = Task.Run(() => owner.Repository.SelectAsync(Context(), PartitionA,
            receipt.AddHours(1), () =>
            {
                selectionEntered.SetResult();
                releaseSelection.Task.GetAwaiter().GetResult();
                return true;
            }));
        await selectionEntered.Task;
        var capSave = owner.Repository.SaveAsync(Candidate("cap", context: UniqueContext(200)),
            PartitionB, receipt.AddHours(1).AddMinutes(1), () => true);
        releaseSelection.SetResult();

        var coordinated = await coordinatedSelection;
        (await capSave).Should().Be(RetainedRouteSaveResult.Saved);
        coordinated!.Route.Waypoints.Should().HaveCount(3);
        (await owner.Connection.Table<RetainedWayfarerRouteEntity>().CountAsync())
            .Should().Be(RetainedWayfarerRouteRepository.MaximumRoutes);

        await owner.Connection.ExecuteAsync(@"
            CREATE TRIGGER fail_retained_eviction BEFORE DELETE ON RetainedWayfarerRoutes
            BEGIN SELECT RAISE(ABORT, 'injected eviction failure'); END");
        var failed = await owner.Repository.SaveAsync(Candidate("must-rollback", context: UniqueContext(201)),
            PartitionB, receipt.AddHours(2), () => true);

        failed.Should().Be(RetainedRouteSaveResult.Failed);
        (await owner.Connection.Table<RetainedWayfarerRouteEntity>().CountAsync())
            .Should().Be(RetainedWayfarerRouteRepository.MaximumRoutes);
        var preserved = await owner.Repository.SelectAsync(Context(), PartitionA,
            receipt.AddHours(2), () => true);
        preserved!.Route.Waypoints.Should().ContainSingle(point => point.Longitude == 23.008);
    }

    [Fact]
    public async Task MatchRejectsEveryAuthorityEndpointAndAnchorMismatch()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        var baseline = Context();
        await owner.Repository.SaveAsync(Candidate("baseline"), PartitionA, receipt, () => true);
        var mismatches = new[]
        {
            baseline with { NormalizedServer = "https://other.test" },
            baseline with { SavedTransportProfileId = Guid.Parse("33333333-3333-3333-3333-333333333333") },
            baseline with { Origin = new(23.1, 37) },
            baseline with { Destination = new(23.02, 37.01) },
            baseline with { Anchors = [new(23.002, 37.002)] },
            baseline with { Anchors = [new(23.003, 37.003), new(23.002, 37.002)] }
        };
        foreach (var mismatch in mismatches)
            (await owner.Repository.SelectAsync(mismatch, PartitionA, receipt.AddMinutes(1), () => true))
                .Should().BeNull();

        var baselineMetadata = Candidate("unused").Metadata;
        var authorities = new[]
        {
            baselineMetadata with { Provider = "other-provider" },
            baselineMetadata with { ProviderConfigurationId = Guid.Parse("44444444-4444-4444-4444-444444444444") },
            baselineMetadata with { MappingIdentity = "mapping-v2" }
        };
        foreach (var authority in authorities)
        {
            await owner.Repository.SaveAsync(Candidate("authority-change", context: UniqueContext(50),
                metadata: authority), PartitionA, receipt.AddMinutes(2), () => true);
            (await owner.Repository.SelectAsync(baseline, PartitionA, receipt.AddMinutes(3), () => true))
                .Should().BeNull();
            await owner.Repository.SaveAsync(Candidate("restore"), PartitionA, receipt.AddMinutes(4), () => true);
        }
    }

    [Fact]
    public async Task GlobalCapCountsPartitions_AndStableTieEvictsOldestPrimaryKey()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 13, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        for (var index = 0; index <= RetainedWayfarerRouteRepository.MaximumRoutes; index++)
        {
            var partition = index % 2 == 0 ? PartitionA : PartitionB;
            (await owner.Repository.SaveAsync(Candidate($"route-{index}", context: UniqueContext(index)),
                partition, receipt, () => true)).Should().Be(RetainedRouteSaveResult.Saved);
        }

        var rows = await owner.Connection.Table<RetainedWayfarerRouteEntity>().OrderBy(row => row.Id).ToListAsync();
        rows.Should().HaveCount(RetainedWayfarerRouteRepository.MaximumRoutes);
        rows.Min(row => row.Id).Should().Be(2);
        (await owner.Repository.SelectAsync(UniqueContext(0), PartitionA, receipt.AddYears(10), () => true))
            .Should().BeNull();
        (await owner.Repository.SelectAsync(UniqueContext(1), PartitionB, receipt.AddYears(10), () => true))
            .Should().NotBeNull("retained routes do not expire by age");
    }

    [Fact]
    public async Task FutureClockRules_ClearAndPrivacyRemainBoundedAndIsolated()
    {
        var receipt = new DateTimeOffset(2026, 8, 31, 14, 0, 0, TimeSpan.Zero);
        await using var owner = await CreateOwnerAsync();
        await owner.Connection.ExecuteAsync("CREATE TABLE Sentinel (Value TEXT NOT NULL)");
        await owner.Connection.ExecuteAsync("INSERT INTO Sentinel (Value) VALUES ('preserve-me')");
        await owner.Repository.SaveAsync(Candidate("prior-private-instruction"), PartitionA, receipt, () => true);
        var unknownAuthority = Candidate("unknown-authority") with
        {
            Metadata = Candidate("unused").Metadata with { StorageMode = "future-mode" }
        };
        (await owner.Repository.SaveAsync(unknownAuthority, PartitionA, receipt, () => true))
            .Should().Be(RetainedRouteSaveResult.Rejected);
        var malformed = Candidate("malformed");
        malformed.Route.Waypoints[0].Latitude = double.NaN;
        (await owner.Repository.SaveAsync(malformed, PartitionA, receipt, () => true))
            .Should().Be(RetainedRouteSaveResult.Rejected);
        var tooFuture = Candidate("future-rejected", middleLongitude: 23.009,
            generatedAt: receipt.AddMinutes(5).AddMilliseconds(1));
        (await owner.Repository.SaveAsync(tooFuture, PartitionA, receipt, () => true))
            .Should().Be(RetainedRouteSaveResult.Rejected);
        var prior = await owner.Repository.SelectAsync(Context(), PartitionA, receipt, () => true);
        prior!.Route.Waypoints.Should().ContainSingle(point => point.Longitude == 23.005);

        var nearFuture = Candidate("near-future", middleLongitude: 23.006,
            generatedAt: receipt.AddMinutes(5));
        (await owner.Repository.SaveAsync(nearFuture, PartitionA, receipt, () => true))
            .Should().Be(RetainedRouteSaveResult.Saved);
        var selected = await owner.Repository.SelectAsync(Context(), PartitionA, receipt.AddMinutes(1), () => true);
        selected!.Route.HostedProvenance!.Age.Should().Be(TimeSpan.Zero);
        selected.Route.Attribution.Should().ContainSingle(item => item.Url == "https://example.test/attribution");

        var row = (await owner.Connection.Table<RetainedWayfarerRouteEntity>().ToListAsync()).Single();
        var stored = string.Join('|', row.GetType().GetProperties().Select(property => property.GetValue(row)));
        stored.Should().NotContain("must-not-be-stored").And.NotContain("place:private")
            .And.NotContain("secret-token").And.NotContain("token-hash").And.NotContain("raw-response")
            .And.NotContain("member-name").And.NotContain("private-note");
        var columns = await owner.Connection.QueryAsync<TableColumn>("PRAGMA table_info('RetainedWayfarerRoutes')");
        columns.Select(column => column.Name).Should().NotContain(name =>
            name.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Response", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Member", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Note", StringComparison.OrdinalIgnoreCase));

        await owner.Repository.ClearAsync();
        await owner.Repository.ClearAsync();
        (await owner.Connection.Table<RetainedWayfarerRouteEntity>().CountAsync()).Should().Be(0);
        (await owner.Connection.ExecuteScalarAsync<string>("SELECT Value FROM Sentinel"))
            .Should().Be("preserve-me");
    }

    private async Task<RepositoryOwner> CreateOwnerAsync()
    {
        var connection = new SQLiteAsyncConnection(databasePath);
        await RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None);
        return new(connection, new RetainedWayfarerRouteRepository(connection));
    }

    private static HostedRouteCandidate Candidate(string instruction, double middleLongitude = 23.005,
        HostedRouteRequestContext? context = null, HostedRouteCapabilityMetadata? metadata = null,
        DateTimeOffset? generatedAt = null)
    {
        context ??= Context();
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
            metadata ?? new("geoapify", ConfigurationId, "mapping-v1", "persistent"),
            generatedAt ?? new DateTimeOffset(2026, 8, 31, 7, 55, 0, TimeSpan.Zero));
    }

    private static HostedRouteRequestContext Context() => new(ProfileId, "walk", "active",
        new(23, 37), new(23.01, 37.01), [new(23.002, 37.002), new(23.002, 37.002)],
        "must-not-be-stored", 7, 3, "https://wayfarer.test", "place:private", "hosted");

    private static HostedRouteRequestContext UniqueContext(int index) => Context() with
    {
        Destination = new(23.1 + (index * 0.0001), 37.1),
        Anchors = []
    };

    private sealed class RepositoryOwner(
        SQLiteAsyncConnection connection,
        RetainedWayfarerRouteRepository repository) : IAsyncDisposable
    {
        public RetainedWayfarerRouteRepository Repository { get; } = repository;
        public SQLiteAsyncConnection Connection { get; } = connection;

        public async ValueTask DisposeAsync() => await Connection.CloseAsync();
    }

    private sealed class TableColumn
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
