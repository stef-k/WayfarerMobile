using FluentAssertions;
using WayfarerMobile.Core.Migrations;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class TransportModePreferenceDeletionMigrationTests
{
    [Fact]
    public async Task ApplyAsync_DeletesOnlyExactPreference_AndIsIdempotent()
    {
        var state = new RecordingState();
        state.Preferences[TransportModePreferenceDeletionMigration.ObsoletePreferenceKey] = "car";
        state.Preferences["last_transport_mode_backup"] = "keep";

        await TransportModePreferenceDeletionMigration.ApplyAsync(state, CancellationToken.None);
        await TransportModePreferenceDeletionMigration.ApplyAsync(state, CancellationToken.None);

        state.Preferences.Should().ContainSingle("last_transport_mode_backup", "keep");
        state.RemovedKeys.Should().OnlyContain(key =>
            key == TransportModePreferenceDeletionMigration.ObsoletePreferenceKey);
        state.SchemaVersion.Should().Be(TransportModePreferenceDeletionMigration.SchemaVersion);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotRecordCompletion_WhenDeletionFails()
    {
        var state = new RecordingState { FailDeletion = true };

        var action = () => TransportModePreferenceDeletionMigration.ApplyAsync(
            state, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        state.SchemaVersion.Should().Be(11);
    }

    private sealed class RecordingState : ITransportModePreferenceDeletionState
    {
        public Dictionary<string, string> Preferences { get; } = [];
        public List<string> RemovedKeys { get; } = [];
        public int SchemaVersion { get; private set; } = 11;
        public bool FailDeletion { get; init; }

        public Task RemovePreferenceAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailDeletion) throw new InvalidOperationException("deletion failed");
            RemovedKeys.Add(key);
            Preferences.Remove(key);
            return Task.CompletedTask;
        }

        public Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SchemaVersion = version;
            return Task.CompletedTask;
        }
    }
}
