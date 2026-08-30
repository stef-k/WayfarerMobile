using WayfarerMobile.Core.Migrations;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class OsrmRoutingDecommissionMigrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    public async Task Apply_RemovesOnlyExactLegacyPreference_AndRerunIsSafe(string? legacyValue)
    {
        var state = new RecordingState();
        state.Preferences["authentication_token"] = "retained";
        state.Preferences["max_live_cache_size_mb"] = "500";
        if (legacyValue is not null)
        {
            state.Preferences["cached_osrm_route"] = legacyValue;
        }

        await OsrmRoutingDecommissionMigration.ApplyAsync(state, CancellationToken.None);
        await OsrmRoutingDecommissionMigration.ApplyAsync(state, CancellationToken.None);

        state.Preferences.Should().NotContainKey("cached_osrm_route");
        state.Preferences.Should().Contain("authentication_token", "retained");
        state.Preferences.Should().Contain("max_live_cache_size_mb", "500");
        state.SchemaVersion.Should().Be(9);
        state.CompletionWrites.Should().Be(2);
        state.RemovedKeys.Should().OnlyContain(key => key == "cached_osrm_route");
    }

    private sealed class RecordingState : ILegacyOsrmPreferenceState
    {
        public Dictionary<string, string> Preferences { get; } = [];
        public List<string> RemovedKeys { get; } = [];
        public int SchemaVersion { get; private set; } = 8;
        public int CompletionWrites { get; private set; }

        public Task RemovePreferencesAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            foreach (var key in keys)
            {
                RemovedKeys.Add(key);
                Preferences.Remove(key);
            }

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
