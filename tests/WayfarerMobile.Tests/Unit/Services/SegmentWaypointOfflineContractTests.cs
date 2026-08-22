using SQLite;
using WayfarerMobile.Core.Migrations;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Tests.Unit.Services;

public class SegmentWaypointOfflineContractTests
{
    [Fact]
    public async Task ProductionRepositoryCodecAndReconstruction_RoundTripSegmentWaypoints()
    {
        SQLitePCL.Batteries_V2.Init();
        var path = Path.Combine(Path.GetTempPath(), $"wayfarer-segment-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);
        var waypoints = new List<TripSegmentWaypoint>
        {
            new() { PlaceId = Guid.Parse("22222222-2222-2222-2222-222222222222"), Position = 0, RouteVertexIndex = 2 },
            new() { PlaceId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Position = 1, RouteVertexIndex = null }
        };

        try
        {
            await connection.CreateTableAsync<OfflineSegmentEntity>();
            var repository = new SegmentRepository(() => Task.FromResult(connection));
            var segmentId = Guid.NewGuid();
            var geometry = "{\"type\":\"LineString\",\"coordinates\":[[1,2],[3,4]]}";
            await repository.SaveOfflineSegmentsAsync(42,
            [
                new OfflineSegmentEntity
                {
                    ServerId = Guid.NewGuid(), OriginId = Guid.NewGuid(), DestinationId = Guid.NewGuid(),
                    Geometry = "legacy geometry", WaypointsJson = "[]"
                }
            ]);
            await repository.SaveOfflineSegmentsAsync(42,
            [
                new OfflineSegmentEntity
                {
                    ServerId = segmentId, OriginId = Guid.NewGuid(), DestinationId = Guid.NewGuid(),
                    Geometry = geometry, WaypointsJson = SegmentWaypointJson.Serialize(waypoints), HasCustomRoute = true
                }
            ]);

            var stored = (await repository.GetOfflineSegmentsAsync(42)).Should().ContainSingle().Subject;
            var mapper = typeof(SegmentWaypointJson).Assembly
                .GetType("WayfarerMobile.Core.Helpers.OfflineSegmentWaypointMapper")?.GetMethod("Reconstruct");
            mapper.Should().NotBeNull("offline reconstruction must use a production mapper");
            var restored = (WayfarerMobile.Core.Models.TripSegment)mapper!.Invoke(null,
                [stored.ServerId, stored.OriginId, stored.DestinationId, stored.Geometry, stored.WaypointsJson, stored.HasCustomRoute])!;
            restored.Id.Should().Be(segmentId);
            restored.Geometry.Should().Be(geometry);
            restored.HasCustomRoute.Should().BeTrue();
            restored.Waypoints.Should().BeEquivalentTo(waypoints, options => options.WithStrictOrdering());
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VersionSevenMigration_PreservesRowAndAddsEmptyWaypointContract()
    {
        SQLitePCL.Batteries_V2.Init();
        var path = Path.Combine(Path.GetTempPath(), $"wayfarer-migration-{Guid.NewGuid():N}.db3");
        var connection = new SQLiteAsyncConnection(path);
        try
        {
            await connection.ExecuteAsync("CREATE TABLE OfflineSegments (Id INTEGER PRIMARY KEY, ServerId TEXT, Notes TEXT)");
            await connection.ExecuteAsync("INSERT INTO OfflineSegments (Id, ServerId, Notes) VALUES (1, ?, ?)", Guid.NewGuid(), "keep me");
            var state = new SqliteMigrationState(connection);
            await SegmentWaypointMigration.ApplyAsync(state, CancellationToken.None);

            var row = (await connection.QueryAsync<LegacyRow>(
                "SELECT Id, Notes, WaypointsJson, HasCustomRoute FROM OfflineSegments")).Should().ContainSingle().Subject;
            row.Notes.Should().Be("keep me");
            SegmentWaypointJson.Deserialize(row.WaypointsJson).Should().BeEmpty();
            row.HasCustomRoute.Should().BeFalse();
            state.Version.Should().Be(8);
        }
        finally
        {
            await connection.CloseAsync();
            File.Delete(path);
        }
    }

    private sealed class SqliteMigrationState(SQLiteAsyncConnection connection) : ISegmentWaypointMigrationState
    {
        public int Version { get; private set; }
        public async Task EnsureColumnAsync(string table, string column, string definition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
        public Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken)
        {
            Version = version;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacyRow
    {
        public int Id { get; set; }
        public string? Notes { get; set; }
        public string? WaypointsJson { get; set; }
        public bool HasCustomRoute { get; set; }
    }
}
