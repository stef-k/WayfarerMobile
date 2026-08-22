namespace WayfarerMobile.Core.Migrations;

public interface ILegacyRasterState
{
    Task RemoveTileSchemaAndNormalizeTripDataAsync(CancellationToken cancellationToken);
    Task RemovePreferencesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
    Task RemoveOwnedTripRasterFilesAsync(CancellationToken cancellationToken);
    Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken);
}

/// <summary>Coordinates the bounded, idempotent removal of legacy raster-download state.</summary>
public static class RasterDecommissionMigration
{
    public const int SchemaVersion = 7;

    public static IReadOnlyCollection<string> ObsoletePreferenceKeys { get; } =
    [
        "map_offline_cache_enabled",
        "live_cache_prefetch_radius",
        "prefetch_distance_threshold",
        "prefetch_distance_threshold_meters",
        "max_trip_cache_size_mb",
        "max_concurrent_tile_downloads",
        "min_tile_request_delay_ms",
        "tile_server_url"
    ];

    public static async Task ApplyAsync(ILegacyRasterState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        await state.RemoveTileSchemaAndNormalizeTripDataAsync(cancellationToken).ConfigureAwait(false);
        await state.RemovePreferencesAsync(ObsoletePreferenceKeys, cancellationToken).ConfigureAwait(false);
        await state.RemoveOwnedTripRasterFilesAsync(cancellationToken).ConfigureAwait(false);
        await state.RecordSchemaVersionAsync(SchemaVersion, cancellationToken).ConfigureAwait(false);
    }
}
