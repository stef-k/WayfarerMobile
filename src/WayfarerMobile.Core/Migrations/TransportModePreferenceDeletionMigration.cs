namespace WayfarerMobile.Core.Migrations;

/// <summary>Provides the exact preference operations needed by schema 12.</summary>
public interface ITransportModePreferenceDeletionState
{
    Task RemovePreferenceAsync(string key, CancellationToken cancellationToken);
    Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken);
}

/// <summary>Deletes the obsolete navigation-mode preference without reading or reusing it.</summary>
public static class TransportModePreferenceDeletionMigration
{
    public const int SchemaVersion = 12;
    public const string ObsoletePreferenceKey = "last_transport_mode";

    public static async Task ApplyAsync(ITransportModePreferenceDeletionState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        await state.RemovePreferenceAsync(ObsoletePreferenceKey, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await state.RecordSchemaVersionAsync(SchemaVersion, cancellationToken).ConfigureAwait(false);
    }
}
