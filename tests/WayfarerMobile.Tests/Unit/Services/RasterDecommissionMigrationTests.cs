using WayfarerMobile.Core.Migrations;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class RasterDecommissionMigrationTests
{
    [Fact]
    public async Task PopulatedLegacyState_RemovesOnlyRasterOwnership_AndRerunIsSafe()
    {
        var state = new RecordingLegacyRasterState
        {
            HasTripTiles = true,
            HasTripDownloadStates = true,
            HasLiveTiles = true,
            HasTripData = true,
            HasQueuedLocations = true,
            HasPendingMutations = true,
            HasLegacyTripFiles = true,
            HasLiveTileFiles = true
        };
        state.Preferences.UnionWith(RasterDecommissionMigration.ObsoletePreferenceKeys);

        await RasterDecommissionMigration.ApplyAsync(state, CancellationToken.None);
        await RasterDecommissionMigration.ApplyAsync(state, CancellationToken.None);

        state.HasTripTiles.Should().BeFalse();
        state.HasTripDownloadStates.Should().BeFalse();
        state.HasLegacyTripFiles.Should().BeFalse();
        state.Preferences.Should().NotIntersectWith(RasterDecommissionMigration.ObsoletePreferenceKeys);
        state.HasLiveTiles.Should().BeTrue();
        state.HasLiveTileFiles.Should().BeTrue();
        state.HasTripData.Should().BeTrue();
        state.HasQueuedLocations.Should().BeTrue();
        state.HasPendingMutations.Should().BeTrue();
        state.SchemaVersion.Should().Be(7);
        state.CompletionWrites.Should().Be(2);
    }

    private sealed class RecordingLegacyRasterState : ILegacyRasterState
    {
        public bool HasTripTiles { get; set; }
        public bool HasTripDownloadStates { get; set; }
        public bool HasLiveTiles { get; set; }
        public bool HasTripData { get; set; }
        public bool HasQueuedLocations { get; set; }
        public bool HasPendingMutations { get; set; }
        public bool HasLegacyTripFiles { get; set; }
        public bool HasLiveTileFiles { get; set; }
        public int SchemaVersion { get; private set; } = 6;
        public int CompletionWrites { get; private set; }
        public HashSet<string> Preferences { get; } = [];

        public Task RemoveTileSchemaAndNormalizeTripDataAsync(CancellationToken cancellationToken)
        {
            HasTripTiles = false;
            HasTripDownloadStates = false;
            return Task.CompletedTask;
        }

        public Task RemovePreferencesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            Preferences.ExceptWith(keys);
            return Task.CompletedTask;
        }

        public Task RemoveOwnedTripRasterFilesAsync(CancellationToken cancellationToken)
        {
            HasLegacyTripFiles = false;
            return Task.CompletedTask;
        }

        public Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken)
        {
            SchemaVersion = version;
            CompletionWrites++;
            return Task.CompletedTask;
        }
    }
}
