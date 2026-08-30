namespace WayfarerMobile.Core.Migrations;

public interface ILegacyOsrmPreferenceState
{
    Task RemovePreferencesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
    Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken);
}

/// <summary>Removes only the obsolete public-OSRM preference residue.</summary>
public static class OsrmRoutingDecommissionMigration
{
    public const int SchemaVersion = 9;

    public static IReadOnlyCollection<string> ObsoletePreferenceKeys { get; } =
    [
        "cached_osrm_route"
    ];

    public static async Task ApplyAsync(ILegacyOsrmPreferenceState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        await state.RemovePreferencesAsync(ObsoletePreferenceKeys, cancellationToken).ConfigureAwait(false);
        await state.RecordSchemaVersionAsync(SchemaVersion, cancellationToken).ConfigureAwait(false);
    }
}
