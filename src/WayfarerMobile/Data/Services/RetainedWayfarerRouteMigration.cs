using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Services;

public static class RetainedWayfarerRouteMigration
{
    public const int SchemaVersion = 11;

    public static async Task ApplyApplicationUpgradeAsync(SQLiteAsyncConnection connection,
        int installedVersion, Func<int, Task> recordSchemaVersion,
        CancellationToken cancellationToken)
    {
        if (installedVersion >= SchemaVersion) return;
        await ApplyAsync(connection, cancellationToken);
        if (installedVersion >= 10)
            await EnsureProviderModeColumnAsync(connection, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await recordSchemaVersion(SchemaVersion);
    }

    public static async Task ApplyAsync(SQLiteAsyncConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await connection.CreateTableAsync<RetainedWayfarerRouteEntity>();
        cancellationToken.ThrowIfCancellationRequested();
        await connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS IX_RetainedWayfarerRoutes_Lookup
            ON RetainedWayfarerRoutes (
                AccountPartition, NormalizedServer, IsCurrentAuthority, TransportProfileId,
                OriginLongitude, OriginLatitude, DestinationLongitude, DestinationLatitude)");
        await connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS IX_RetainedWayfarerRoutes_Eviction
            ON RetainedWayfarerRoutes (LastUsedAtUnixMilliseconds, StoredAtUnixMilliseconds, Id)");
    }

    private static async Task EnsureProviderModeColumnAsync(SQLiteAsyncConnection connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var columns = await connection.GetTableInfoAsync("RetainedWayfarerRoutes");
        if (columns.All(column => column.Name != nameof(RetainedWayfarerRouteEntity.ProviderMode)))
            await connection.ExecuteAsync("ALTER TABLE RetainedWayfarerRoutes ADD COLUMN ProviderMode TEXT NULL");
    }
}
