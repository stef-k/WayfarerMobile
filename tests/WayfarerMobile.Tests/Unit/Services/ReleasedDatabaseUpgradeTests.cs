using SQLite;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Tests.Unit.Services;

[Collection("SQLite")]
public sealed class ReleasedDatabaseUpgradeTests
{
    [Fact]
    public async Task Schema6_ReopensAt12_PreservingContentAndDeliveryState()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wayfarer-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        FileSystem.DatabaseTestRoot.Value = root;
        try
        {
            var trip = new DownloadedTripEntity
            {
                Id = 7, ServerId = Guid.NewGuid(), Name = "Saved Trip", Notes = "<p>Keep notes</p>",
                UnifiedStateValue = 40, PlaceCount = 2, SegmentCount = 1, Version = 3,
                BoundingBoxNorth = 2, BoundingBoxSouth = 1, BoundingBoxEast = 4, BoundingBoxWest = 3,
                DownloadedAt = new DateTime(2026, 1, 1), CoverImageUrl = "https://example.test/cover"
            };
            var place = new OfflinePlaceEntity { Id = 8, TripId = 7, ServerId = Guid.NewGuid(), Name = "Stop", Notes = "Keep" };
            var segment = new OfflineSegmentEntity
            {
                Id = 9, TripId = 7, ServerId = Guid.NewGuid(), OriginId = place.ServerId,
                DestinationId = Guid.NewGuid(), Geometry = "{\"type\":\"LineString\",\"coordinates\":[[3,1],[4,2]]}"
            };
            var queue = new QueuedLocation
            {
                Id = 10, Latitude = 1, Longitude = 3, SyncAttempts = 2,
                IdempotencyKey = "11111111-1111-1111-1111-111111111111", CheckInNotes = "Queued notes"
            };
            var timeline = new LocalTimelineEntry { Id = 11, Latitude = 1, Longitude = 3, Notes = "Local notes", ServerId = 42 };
            var mutation = new PendingTimelineMutation { Id = 12, LocationId = 42, LocalEntryId = 11, Notes = "Pending edit" };
            var live = new LiveTileEntity { Id = "1/0/0", FilePath = Path.Combine(root, "tiles", "live", "tile.png") };
            var setting = new AppSetting { Key = "preservation_sentinel", Value = "keep" };
            var seed = new SQLiteAsyncConnection(DatabaseService.DatabasePath);
            try
            {
                await CreateReleasedTablesAsync(seed);
                foreach (var row in new object[] { trip, place, segment, queue, timeline, mutation, live, setting })
                    await seed.InsertAsync(row);
                await RestoreReleasedColumnsAsync(seed);
            }
            finally { await seed.CloseAsync(); }

            Directory.CreateDirectory(Path.GetDirectoryName(live.FilePath)!);
            File.WriteAllText(live.FilePath, "retained live tile");
            var raster = Path.Combine(root, "tiles", "trip_7");
            Directory.CreateDirectory(raster);
            File.WriteAllText(Path.Combine(raster, "tile.png"), "obsolete raster");
            trip.UnifiedStateValue = 30; // Downloaded raster+content becomes content-only.

            for (var reopen = 0; reopen < 2; reopen++)
            {
                await using var service = new DatabaseService();
                var db = await service.GetConnectionAsync();
                (await service.GetSettingAsync<int>("db_schema_version")).Should().Be(12);
                (await db.FindAsync<DownloadedTripEntity>(trip.Id)).Should().BeEquivalentTo(trip);
                (await db.FindAsync<OfflinePlaceEntity>(place.Id)).Should().BeEquivalentTo(place);
                (await db.FindAsync<OfflineSegmentEntity>(segment.Id)).Should().BeEquivalentTo(segment);
                (await db.FindAsync<QueuedLocation>(queue.Id)).Should().BeEquivalentTo(queue);
                (await db.FindAsync<LocalTimelineEntry>(timeline.Id)).Should().BeEquivalentTo(timeline);
                (await db.FindAsync<PendingTimelineMutation>(mutation.Id)).Should().BeEquivalentTo(mutation);
                (await service.GetSettingAsync<string>(setting.Key)).Should().Be(setting.Value);
                (await db.FindAsync<LiveTileEntity>(live.Id))!.FilePath.Should().Be(live.FilePath);
                (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RetainedWayfarerRoutes")).Should().Be(0);
                (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('TripTiles', 'TripDownloadStates')"))
                    .Should().Be(0);
                File.ReadAllText(live.FilePath).Should().Be("retained live tile");
                Directory.Exists(raster).Should().BeFalse();
            }
        }
        finally
        {
            FileSystem.DatabaseTestRoot.Value = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateReleasedTablesAsync(SQLiteAsyncConnection db)
    {
        await db.CreateTableAsync<DownloadedTripEntity>();
        await db.CreateTableAsync<OfflinePlaceEntity>();
        await db.CreateTableAsync<OfflineSegmentEntity>();
        await db.CreateTableAsync<QueuedLocation>();
        await db.CreateTableAsync<LocalTimelineEntry>();
        await db.CreateTableAsync<PendingTimelineMutation>();
        await db.CreateTableAsync<LiveTileEntity>();
        await db.CreateTableAsync<AppSetting>();
        await db.InsertAsync(new AppSetting { Key = "db_schema_version", Value = "6" });
    }

    private static async Task RestoreReleasedColumnsAsync(SQLiteAsyncConnection db)
    {
        // Reproduce the changed columns of tag 1.2.0 (13fb16b3), without copying
        // the migration SQL. Unchanged content entities use their existing mappings.
        foreach (var definition in new[] { "TotalSizeBytes BIGINT", "Status TEXT", "PauseReason TEXT",
            "TilesCompleted INTEGER", "TilesTotal INTEGER", "TileCount INTEGER", "ProgressPercent INTEGER", "LastError TEXT" })
            await db.ExecuteAsync($"ALTER TABLE DownloadedTrips ADD COLUMN {definition}");
        await db.ExecuteAsync("ALTER TABLE OfflineSegments DROP COLUMN WaypointsJson");
        await db.ExecuteAsync("ALTER TABLE OfflineSegments DROP COLUMN HasCustomRoute");
        await db.ExecuteAsync("DROP INDEX IF EXISTS LiveTiles_ProviderId");
        foreach (var column in new[] { "ProviderId", "FreshUntilUtc", "CacheControl", "ExpiresUtc", "ETag", "LastModifiedUtc" })
            await db.ExecuteAsync($"ALTER TABLE LiveTiles DROP COLUMN {column}");
        // Raster tables are removed as a whole, so their obsolete payload is immaterial.
        await db.ExecuteAsync("CREATE TABLE TripTiles (Id INTEGER PRIMARY KEY)");
        await db.ExecuteAsync("INSERT INTO TripTiles VALUES (1)");
        await db.ExecuteAsync("CREATE TABLE TripDownloadStates (Id INTEGER PRIMARY KEY)");
        await db.ExecuteAsync("INSERT INTO TripDownloadStates VALUES (1)");
    }
}
