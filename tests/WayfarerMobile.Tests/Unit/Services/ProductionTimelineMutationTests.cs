using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Exercises production Timeline mutations with colliding local and server IDs.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineMutationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task LocalOnlyCollision_CannotUpdateOrDeleteAnotherServerRecord(bool online, bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
        var localOnly = new LocalTimelineEntry
        {
            Id = 42, ServerId = null, Latitude = 37.98, Longitude = 23.72,
            Timestamp = DateTime.UtcNow, Notes = "Imported notes", ActivityType = "Walk"
        };
        var linked = new LocalTimelineEntry
        {
            Id = 43, ServerId = 42, Latitude = 38, Longitude = 24,
            Timestamp = DateTime.UtcNow, Notes = "Linked notes", ActivityType = "Run"
        };
        await context.Repository.InsertLocalTimelineEntryAsync(localOnly);
        await context.Repository.InsertLocalTimelineEntryAsync(linked);
        var selected = TimelineDataService.ToTimelineLocation(localOnly);

        if (delete)
            await context.Service.DeleteLocationAsync(selected.Id);
        else
            await context.Service.UpdateLocationAsync(selected.Id, 1, 2, DateTime.UtcNow.AddDays(-1),
                "Changed notes", true, 1, activityTypeName: "Cycle");

        var retained = await context.Repository.GetLocalTimelineEntryAsync(linked.Id);
        retained.Should().NotBeNull();
        retained!.Notes.Should().Be("Linked notes");
        retained.Latitude.Should().Be(38);
        retained.Longitude.Should().Be(24);
        retained.ActivityType.Should().Be("Run");
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<TimelineLocationUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Api.Verify(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>Uses the existing production database seam with an isolated, disposable database.</summary>
internal sealed class TimelineMutationContext : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timeline-authority-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _databaseService = new();
    public Mock<IApiClient> Api { get; } = new();
    public SQLiteAsyncConnection Database { get; private set; } = null!;
    public TimelineRepository Repository { get; private set; } = null!;
    public TimelineSyncService Service { get; private set; } = null!;

    public static async Task<TimelineMutationContext> CreateAsync(bool online)
    {
        var context = new TimelineMutationContext();
        Directory.CreateDirectory(context._root);
        FileSystem.DatabaseTestRoot.Value = context._root;
        context.Database = await context._databaseService.GetConnectionAsync();
        context.Repository = new TimelineRepository(context._databaseService.GetConnectionAsync);
        context.Api.SetupGet(x => x.IsConfigured).Returns(true);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<TimelineLocationUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimelineUpdateResponse { Success = true });
        context.Api.Setup(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var connectivity = new Mock<IConnectivity>();
        connectivity.SetupGet(x => x.NetworkAccess).Returns(online ? NetworkAccess.Internet : NetworkAccess.None);
        context.Service = new TimelineSyncService(context.Api.Object, context.Repository, context._databaseService,
            connectivity.Object, NullLogger<TimelineSyncService>.Instance);
        await context.Service.GetPendingCountAsync();
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        Service.Dispose();
        await _databaseService.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
