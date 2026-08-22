using WayfarerMobile.Core.Migrations;

namespace WayfarerMobile.Tests.Unit.Services;

public class SegmentWaypointMigrationTests
{
    [Fact]
    public async Task ApplyAsync_EnsuresOnlyWaypointColumnsAndRecordsVersionEight()
    {
        var state = new RecordingState();

        await SegmentWaypointMigration.ApplyAsync(state, CancellationToken.None);
        await SegmentWaypointMigration.ApplyAsync(state, CancellationToken.None);

        state.Columns.Should().OnlyContain(item => item.Table == "OfflineSegments");
        state.Columns.Select(item => item.Column).Distinct().Should().Equal("WaypointsJson", "HasCustomRoute");
        state.Version.Should().Be(8);
    }

    private sealed class RecordingState : ISegmentWaypointMigrationState
    {
        public HashSet<(string Table, string Column, string Definition)> Columns { get; } = new();
        public int Version { get; private set; }
        public Task EnsureColumnAsync(string table, string column, string definition, CancellationToken cancellationToken)
        {
            Columns.Add((table, column, definition));
            return Task.CompletedTask;
        }
        public Task RecordSchemaVersionAsync(int version, CancellationToken cancellationToken)
        {
            Version = version;
            return Task.CompletedTask;
        }
    }
}
