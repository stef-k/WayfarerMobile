using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Services;

public static class RetainedWayfarerRouteMigration
{
    public const int SchemaVersion = 10;

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
}
