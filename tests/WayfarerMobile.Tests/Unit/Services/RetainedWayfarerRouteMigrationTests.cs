using SQLite;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Tests.Unit.Services;

[Collection("SQLite")]
public sealed class RetainedWayfarerRouteMigrationTests
{
    [Fact]
    public async Task Version9File_MigratesIdempotentlyWithoutChangingExistingOwnership()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wayfarer-v9-{Guid.NewGuid():N}.db3");
        try
        {
            var connection = new SQLiteAsyncConnection(path);
            await connection.ExecuteAsync(
                "CREATE TABLE AppSettings (Id INTEGER PRIMARY KEY AUTOINCREMENT, Key TEXT, Value TEXT)");
            await connection.ExecuteAsync(
                "INSERT INTO AppSettings (Key, Value) VALUES ('db_schema_version', '9')");
            await connection.ExecuteAsync(
                "CREATE TABLE Sentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            await connection.ExecuteAsync("INSERT INTO Sentinel (Id, Value) VALUES (1, 'preserve-me')");

            await RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None);
            await RetainedWayfarerRouteMigration.ApplyAsync(connection, CancellationToken.None);

            (await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'RetainedWayfarerRoutes'"))
                .Should().Be(1);
            var indexes = await connection.QueryAsync<SchemaObject>(
                "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'RetainedWayfarerRoutes'");
            indexes.Select(item => item.Name).Should().Contain(
                "IX_RetainedWayfarerRoutes_Lookup", "IX_RetainedWayfarerRoutes_Eviction");
            (await connection.ExecuteScalarAsync<string>("SELECT Value FROM Sentinel WHERE Id = 1"))
                .Should().Be("preserve-me");
            (await connection.ExecuteScalarAsync<string>(
                "SELECT Value FROM AppSettings WHERE Key = 'db_schema_version'"))
                .Should().Be("9", "DatabaseService retains schema-version ownership");
            await connection.CloseAsync();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class SchemaObject
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}
