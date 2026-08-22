namespace WayfarerMobile.Core.Migrations;

public interface ISegmentWaypointMigrationState
{
    Task EnsureColumnAsync(string table, string column, string definition, CancellationToken cancellationToken);
    Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken);
}

public static class SegmentWaypointMigration
{
    public const int SchemaVersion = 8;

    public static async Task ApplyAsync(ISegmentWaypointMigrationState state, CancellationToken cancellationToken)
    {
        await state.EnsureColumnAsync("OfflineSegments", "WaypointsJson", "TEXT", cancellationToken).ConfigureAwait(false);
        await state.EnsureColumnAsync("OfflineSegments", "HasCustomRoute", "INTEGER NOT NULL DEFAULT 0", cancellationToken).ConfigureAwait(false);
        await state.RecordSchemaVersionAsync(SchemaVersion, cancellationToken).ConfigureAwait(false);
    }
}
