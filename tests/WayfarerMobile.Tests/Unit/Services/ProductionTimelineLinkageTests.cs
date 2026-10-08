using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Protects imported history from capture linkage and cleanup using production services and SQLite.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineLinkageTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupReconciliation_LinksOnlyTheCaptureAndKeepsMatchingImportsReadOnly(bool online)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
        var location = CreateLocation();
        var imported = await ImportAsync(context, location.Timestamp, includeNearby: true);
        var queue = CreateQueue(context);
        var queuedId = await queue.QueueLocationAsync(location);
        using var storage = CreateStorage(context, queue);
        await storage.AddPendingLocationAsync(location, queuedId);
        var capture = (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!;
        await queue.MarkServerConfirmedAsync(queuedId, 42);

        await storage.InitializeAsync();

        (await context.Repository.GetLocalTimelineEntryAsync(capture.Id))!.ServerId.Should().Be(42);
        foreach (var original in imported)
        {
            var entry = (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!;
            entry.Should().BeEquivalentTo(original);
            var display = new TimelineLocationDisplay(TimelineDataService.ToTimelineLocation(entry));
            display.IsReadOnly.Should().BeTrue();
            display.ReadOnlyExplanation.Should().Be(TimelineEntryIdentity.ReadOnlyExplanation);
            if (online)
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.UpdateLocationAsync(
                    display.Identity, notes: "Unrelated update", includeNotes: true));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.DeleteLocationAsync(display.Identity));
        }
        context.VerifyNoRemoteMutations();
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        (await context.Database.Table<QueuedLocation>().CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public async Task CallbacksWithoutAnOriginatingRow_DoNotLinkOrDeleteMatchingImports(int queuedId)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var imported = await ImportAsync(context, location.Timestamp, includeNearby: true);
        var logger = new Mock<ILogger<LocalTimelineStorageService>>();
        using var storage = CreateStorage(context, CreateQueue(context), logger: logger.Object);
        await storage.InitializeAsync();

        await NotifyAndWaitAsync(logger, () => LocationSyncCallbacks.NotifyLocationSynced(
            queuedId, 42, location.Timestamp, location.Latitude, location.Longitude));
        await NotifyAndWaitAsync(logger, () => LocationSyncCallbacks.NotifyLocationSkipped(
            queuedId, location.Timestamp, location.Latitude, location.Longitude, "Skipped"));

        (await context.Repository.GetAllLocalTimelineEntriesAsync()).Should().BeEquivalentTo(imported);
        context.VerifyNoRemoteMutations();
    }

    [Fact]
    public async Task ConfirmedUpload_LinksItsCaptureAndRestartRecoversTheSameRow()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var imported = await ImportAsync(context, location.Timestamp, includeNearby: true);
        var queue = CreateQueue(context);
        var logger = new Mock<ILogger<LocalTimelineStorageService>>();
        using var storage = CreateStorage(context, queue, logger: logger.Object);
        await storage.InitializeAsync();
        storage.ResetFilter();
        var queuedId = await queue.QueueLocationAsync(location, isUserInvoked: true);
        await storage.AddPendingLocationAsync(location, queuedId);
        var capture = (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!;
        context.Api.Setup(x => x.CheckInAsync(It.IsAny<LocationLogRequest>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new ApiResult { Success = true, LocationId = 42 });
        var connectivity = new Mock<IConnectivity>();
        connectivity.SetupGet(x => x.NetworkAccess).Returns(NetworkAccess.Internet);
        using (var drain = new QueueDrainService(context.Api.Object, queue, new MockSettingsService(),
            connectivity.Object, NullLogger<QueueDrainService>.Instance))
        {
            await drain.StartAsync();
            await NotifyAndWaitAsync(logger, () => drain.TriggerDrainAsync());
        }
        var linked = (await context.Repository.GetLocalTimelineEntryAsync(capture.Id))!;
        linked.ServerId.Should().Be(42);
        (await context.Database.GetAsync<QueuedLocation>(queuedId)).ServerConfirmed.Should().BeTrue();
        (await context.Database.GetAsync<QueuedLocation>(queuedId)).SyncStatus.Should().Be(SyncStatus.Synced);

        // Simulate a crash after queue confirmation but before the Timeline callback persisted linkage.
        storage.Dispose();
        linked.ServerId = null;
        await context.Repository.UpdateLocalTimelineEntryAsync(linked);
        await context.RestartOnlineAsync();
        using var recoveredStorage = CreateStorage(context, queue);
        await recoveredStorage.InitializeAsync();
        var recovered = (await context.Repository.GetLocalTimelineEntryAsync(capture.Id))!;
        recovered.QueuedLocationId.Should().Be(queuedId);
        recovered.ServerId.Should().Be(42);
        foreach (var original in imported)
            (await context.Repository.GetLocalTimelineEntryAsync(original.Id)).Should().BeEquivalentTo(original);
        await context.Service.UpdateLocationAsync(TimelineDataService.ToTimelineLocation(recovered).Identity,
            notes: "Confirmed capture edit", includeNotes: true);
        context.Api.Verify(x => x.CheckInAsync(It.IsAny<LocationLogRequest>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipCallback_RemovesOnlyItsPendingCaptureAndPreservesServerOriginAndImports()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var imported = await ImportAsync(context, location.Timestamp, includeNearby: true);
        var queue = CreateQueue(context);
        var logger = new Mock<ILogger<LocalTimelineStorageService>>();
        using var storage = CreateStorage(context, queue, logger: logger.Object);
        await storage.InitializeAsync();
        storage.ResetFilter();
        var queuedId = await queue.QueueLocationAsync(location);
        await storage.AddPendingLocationAsync(location, queuedId);
        await storage.AddAcceptedLocationAsync(location, 77);
        await queue.MarkLocationRejectedAsync(queuedId, "Skipped");

        await NotifyAndWaitAsync(logger, () => LocationSyncCallbacks.NotifyLocationSkipped(
            queuedId, location.Timestamp, location.Latitude, location.Longitude, "Skipped"));

        (await context.Repository.GetByQueuedLocationIdAsync(queuedId)).Should().BeNull();
        var serverOrigin = (await context.Repository.GetLocalTimelineEntryByServerIdAsync(77))!;
        TimelineDataService.ToTimelineLocation(serverOrigin).Identity.CanMutate.Should().BeTrue();
        (await context.Repository.GetAllLocalTimelineEntriesAsync()).Should().BeEquivalentTo(imported.Append(serverOrigin));
        context.VerifyNoRemoteMutations();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backfill_CreatesItsOwnQueueBoundRowAndRetainsLinkageAfterRestart(bool confirmed)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var imported = (await ImportAsync(context, location.Timestamp, includeNearby: false)).Single();
        var settings = new MockSettingsService { LocationTimeThresholdMinutes = 0, LocationDistanceThresholdMeters = 0 };
        var queue = CreateQueue(context);
        var queuedId = await queue.QueueLocationAsync(location);
        if (confirmed)
            await queue.MarkServerConfirmedAsync(queuedId, 42);
        else
            // An unconfirmed persisted server ID must not grant authority to a backfilled row.
            await context.Database.ExecuteAsync("UPDATE QueuedLocations SET ServerId = 42 WHERE Id = ?", queuedId);
        using var storage = CreateStorage(context, queue, settings);
        await storage.InitializeAsync();

        var backfilled = (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!;
        backfilled.Should().NotBeNull();
        backfilled.Id.Should().NotBe(imported.Id);
        backfilled.ServerId.Should().Be(confirmed ? 42 : null);
        (await context.Repository.GetLocalTimelineEntryAsync(imported.Id)).Should().BeEquivalentTo(imported);
        storage.Dispose();
        await queue.MarkServerConfirmedAsync(queuedId, 42);
        await context.RestartOnlineAsync();
        using var recoveredStorage = CreateStorage(context, queue, settings);
        await recoveredStorage.InitializeAsync();
        (await context.Repository.GetLocalTimelineEntryAsync(backfilled.Id))!.ServerId.Should().Be(42);
        (await context.Repository.GetLocalTimelineEntryAsync(imported.Id)).Should().BeEquivalentTo(imported);
        (await context.Repository.GetLocalTimelineEntryCountAsync()).Should().Be(2);
        context.VerifyNoRemoteMutations();
    }

    [Fact]
    public async Task QueueLinkage_RequiresConfirmationAndNeverOverwritesOrDeletesAnExistingServerLink()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var queue = CreateQueue(context);
        var queuedId = await queue.QueueLocationAsync(location);
        using var storage = CreateStorage(context, queue);
        await storage.AddPendingLocationAsync(location, queuedId);

        (await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedId, 42)).Should().BeFalse();
        await queue.MarkServerConfirmedAsync(queuedId, 42);
        (await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedId, 99)).Should().BeFalse();
        (await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedId, 42)).Should().BeTrue();
        (await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedId, 99)).Should().BeFalse();
        (await context.Repository.DeleteByQueuedLocationIdAsync(queuedId)).Should().Be(0);
        (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!.ServerId.Should().Be(42);
        context.VerifyNoRemoteMutations();
    }

    [Fact]
    public async Task AmbiguousQueueBinding_DoesNotGrantAuthorityOrDeleteEitherRow()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var location = CreateLocation();
        var queue = CreateQueue(context);
        var queuedId = await queue.QueueLocationAsync(location);
        using var storage = CreateStorage(context, queue);
        await storage.AddPendingLocationAsync(location, queuedId);
        storage.ResetFilter();
        await storage.AddPendingLocationAsync(location, queuedId);
        await queue.MarkServerConfirmedAsync(queuedId, 42);

        (await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedId, 42)).Should().BeFalse();
        (await context.Repository.DeleteByQueuedLocationIdAsync(queuedId)).Should().Be(0);
        var entries = await context.Repository.GetAllLocalTimelineEntriesAsync();
        entries.Should().HaveCount(2).And.OnlyContain(e => e.ServerId == null);
        (await context.Database.Table<QueuedLocation>().CountAsync()).Should().Be(1);
        context.VerifyNoRemoteMutations();
    }

    private static LocationData CreateLocation() => new()
    {
        Latitude = 37.98, Longitude = 23.72,
        Timestamp = DateTime.UtcNow.AddHours(-1), Provider = "gps"
    };

    private static LocationQueueRepository CreateQueue(TimelineMutationContext context) =>
        new(() => Task.FromResult(context.Database), new MockSettingsService());

    private static LocalTimelineStorageService CreateStorage(TimelineMutationContext context,
        ILocationQueueRepository queue, MockSettingsService? settings = null,
        ILogger<LocalTimelineStorageService>? logger = null)
    {
        settings ??= new MockSettingsService();
        return new(context.Repository, queue, settings, new LocalTimelineFilter(settings),
            logger ?? NullLogger<LocalTimelineStorageService>.Instance);
    }

    /// <summary>Imports nearby matching rows without changing the production importer's duplicate rules.</summary>
    private static async Task<List<LocalTimelineEntry>> ImportAsync(TimelineMutationContext context,
        DateTime timestamp, bool includeNearby)
    {
        // A distinct place is the importer's first timestamp candidate, allowing both matching rows to coexist.
        var csv = "TimestampUtc,Latitude,Longitude,Notes\n"
            + (includeNearby ? $"{timestamp:O},1,2,Other imported place\n" : "")
            + $"{timestamp:O},37.98,23.72,Imported matching point\n"
            + (includeNearby ? $"{timestamp.AddSeconds(1):O},37.98,23.72,Imported nearby point\n" : "");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var importer = new TimelineImportService(context.Repository, NullLogger<TimelineImportService>.Instance);
        var result = await importer.ImportFromCsvAsync(stream);
        result.Errors.Should().BeEmpty();
        result.Imported.Should().Be(includeNearby ? 3 : 1);
        return await context.Repository.GetAllLocalTimelineEntriesAsync();
    }

    /// <summary>Awaits the existing callback's completion log so assertions do not race async event handlers.</summary>
    private static Task NotifyAndWaitAsync(Mock<ILogger<LocalTimelineStorageService>> logger, Action notify) =>
        NotifyAndWaitAsync(logger, () => { notify(); return Task.CompletedTask; });

    private static async Task NotifyAndWaitAsync(Mock<ILogger<LocalTimelineStorageService>> logger, Func<Task> notify)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ => completed.TrySetResult()));
        await notify();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
