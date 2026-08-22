using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure.Mocks;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class QueueRecoverySafetyTests
{
    [Fact]
    public async Task RecoveryExport_BlocksResumeUntilSuspensionProtectedSnapshotCompletes()
    {
        var exporterEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExporter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deliveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelivery = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new MockSettingsService();
        var queued = new QueuedLocation
        {
            Id = 8,
            Timestamp = new DateTime(2026, 8, 22, 10, 11, 12, DateTimeKind.Utc),
            Latitude = 37.98,
            Longitude = 23.72,
            IsUserInvoked = true,
            SyncStatus = SyncStatus.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("D")
        };
        var queue = new Mock<ILocationQueueRepository>();
        queue.Setup(x => x.ResetStuckLocationsAsync()).ReturnsAsync(0);
        queue.Setup(x => x.ClaimNextPendingLocationWithPriorityAsync()).ReturnsAsync(() =>
        {
            queued.SyncStatus = SyncStatus.Syncing;
            return queued;
        });
        queue.Setup(x => x.ResetLocationToPendingAsync(queued.Id)).Callback(() => queued.SyncStatus = SyncStatus.Pending)
            .Returns(Task.CompletedTask);
        queue.Setup(x => x.GetAllQueuedLocationsForExportAsync()).ReturnsAsync(() =>
            queued.SyncStatus == SyncStatus.Pending ? [queued] : []);
        var api = new Mock<IApiClient>();
        api.SetupGet(x => x.IsConfigured).Returns(true);
        api.Setup(x => x.CheckInAsync(It.IsAny<LocationLogRequest>(), queued.IdempotencyKey, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                deliveryEntered.TrySetResult();
                return releaseDelivery.Task;
            });
        var connectivity = new Mock<IConnectivity>();
        connectivity.SetupGet(x => x.NetworkAccess).Returns(NetworkAccess.Internet);
        var recoveryOperations = new QueueRecoveryOperationCoordinator();
        using var drainService = new QueueDrainService(
            api.Object, queue.Object, settings, connectivity.Object, NullLogger<QueueDrainService>.Instance,
            recoveryOperations);
        await drainService.StartAsync();

        var activities = new Mock<IActivitySyncService>();
        activities.Setup(x => x.GetAllActivityTypesAsync()).ReturnsAsync([]);
        var canonicalExporter = new QueueExportService(queue.Object, activities.Object);
        string? sharedContent = null;
        var exportService = new Mock<IQueueExportService>();
        exportService.Setup(x => x.ShareExportAsync("csv")).Returns(async () =>
        {
            exporterEntered.TrySetResult();
            await releaseExporter.Task;
            sharedContent = await canonicalExporter.ExportToCsvAsync();
        });
        var coordinator = new RecoveryExportCoordinator(drainService, exportService.Object, queue.Object, recoveryOperations);

        var export = coordinator.ExportAndShareAsync("csv");
        await exporterEntered.Task;
        var resume = drainService.ResumeAndReconcileAsync();

        resume.IsCompleted.Should().BeFalse();
        settings.QueueDeliverySuspended.Should().BeTrue();
        queue.Verify(x => x.ClaimNextPendingLocationWithPriorityAsync(), Times.Never);

        releaseExporter.SetResult();
        (await export).Should().BeTrue();
        sharedContent.Should().Contain(queued.IdempotencyKey);

        await deliveryEntered.Task;
        settings.QueueDeliverySuspended.Should().BeFalse();
        queued.SyncStatus.Should().Be(SyncStatus.Syncing);

        releaseDelivery.SetResult(ApiResult.Fail("temporary", 503, true));
        await resume;
        settings.QueueDeliverySuspended.Should().BeFalse();
        queued.SyncStatus.Should().Be(SyncStatus.Pending);
        queue.Verify(x => x.ClaimNextPendingLocationWithPriorityAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DirectRecoveryExport_WaitsForClaimAndExportsItsPostDeliveryState()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new MockSettingsService();
        var queued = new QueuedLocation
        {
            Id = 7,
            Timestamp = new DateTime(2026, 8, 22, 10, 11, 12, DateTimeKind.Utc),
            Latitude = 37.98,
            Longitude = 23.72,
            IsUserInvoked = true,
            SyncStatus = SyncStatus.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("D")
        };
        var queue = new Mock<ILocationQueueRepository>();
        queue.Setup(x => x.ResetStuckLocationsAsync()).ReturnsAsync(0);
        queue.SetupSequence(x => x.ClaimNextPendingLocationWithPriorityAsync())
            .ReturnsAsync(() =>
            {
                queued.SyncStatus = SyncStatus.Syncing;
                return queued;
            })
            .ReturnsAsync((QueuedLocation?)null);
        queue.Setup(x => x.ResetLocationToPendingAsync(queued.Id)).Callback(() => queued.SyncStatus = SyncStatus.Pending).Returns(Task.CompletedTask);
        queue.Setup(x => x.GetAllQueuedLocationsForExportAsync()).ReturnsAsync(() =>
            queued.SyncStatus == SyncStatus.Pending && !queued.IsRejected ? [queued] : []);
        var api = new Mock<IApiClient>();
        api.SetupGet(x => x.IsConfigured).Returns(true);
        api.Setup(x => x.CheckInAsync(It.IsAny<LocationLogRequest>(), queued.IdempotencyKey, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.TrySetResult();
                return release.Task;
            });
        var connectivity = new Mock<IConnectivity>();
        connectivity.SetupGet(x => x.NetworkAccess).Returns(NetworkAccess.Internet);
        using var drainService = new QueueDrainService(
            api.Object, queue.Object, settings, connectivity.Object, NullLogger<QueueDrainService>.Instance);
        var activities = new Mock<IActivitySyncService>();
        activities.Setup(x => x.GetAllActivityTypesAsync()).ReturnsAsync([]);
        var canonicalExporter = new QueueExportService(queue.Object, activities.Object);
        string? sharedContent = null;
        var exportService = new Mock<IQueueExportService>();
        exportService.Setup(x => x.ShareExportAsync("csv")).Returns(async () =>
            sharedContent = await canonicalExporter.ExportToCsvAsync());
        var coordinatorType = typeof(QueueDrainService).Assembly.GetType("WayfarerMobile.Services.RecoveryExportCoordinator");
        coordinatorType.Should().NotBeNull("direct recovery export needs a production-owned quiescence coordinator");
        var coordinator = Activator.CreateInstance(coordinatorType!, drainService, exportService.Object);
        var exportAndShare = coordinatorType!.GetMethod("ExportAndShareAsync");
        exportAndShare.Should().NotBeNull();

        await drainService.StartAsync();
        var activeDrain = drainService.TriggerDrainAsync();
        await entered.Task;

        var directExport = (Task)exportAndShare!.Invoke(coordinator, ["csv", CancellationToken.None])!;

        directExport.IsCompleted.Should().BeFalse();
        sharedContent.Should().BeNull();
        settings.QueueDeliverySuspended.Should().BeTrue();

        release.SetResult(ApiResult.Fail("temporary", 503, true));
        await activeDrain;
        await directExport;

        settings.QueueDeliverySuspended.Should().BeTrue();
        queued.SyncStatus.Should().Be(SyncStatus.Pending);
        sharedContent.Should().Contain(queued.IdempotencyKey);
        exportService.Verify(x => x.ShareExportAsync("csv"), Times.Once);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("geojson")]
    public async Task RecoveryExport_ImportsIntoTimelineWithoutQueueOrServerOwnership(string format)
    {
        var timestamp = new DateTime(2026, 8, 22, 10, 11, 12, DateTimeKind.Utc);
        var queued = new QueuedLocation { Id = 41, Timestamp = timestamp, Latitude = 37.98, Longitude = 23.72,
            CheckInNotes = "recovery note", DeviceModel = "capture phone", SyncStatus = SyncStatus.Pending,
            IdempotencyKey = Guid.NewGuid().ToString("D") };
        var queue = new Mock<ILocationQueueRepository>(MockBehavior.Strict);
        queue.Setup(x => x.GetAllQueuedLocationsForExportAsync()).ReturnsAsync([queued]);
        var activities = new Mock<IActivitySyncService>(MockBehavior.Strict);
        activities.Setup(x => x.GetAllActivityTypesAsync()).ReturnsAsync([]);
        var exporter = new QueueExportService(queue.Object, activities.Object);
        var timeline = new TimelineRepositoryFake();
        var importer = new TimelineImportService(timeline, NullLogger<TimelineImportService>.Instance);

        var content = format == "csv" ? await exporter.ExportToCsvAsync() : await exporter.ExportToGeoJsonAsync();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = format == "csv" ? await importer.ImportFromCsvAsync(stream) : await importer.ImportFromGeoJsonAsync(stream);

        result.Imported.Should().Be(1);
        timeline.Entries.Should().ContainSingle();
        timeline.Entries[0].Should().Match<LocalTimelineEntry>(x => x.Timestamp == timestamp && x.Latitude == 37.98 &&
            x.Longitude == 23.72 && x.Notes == "recovery note" && x.DeviceModel == "capture phone" &&
            x.ServerId == null && x.QueuedLocationId == null);
        queue.VerifyAll();
    }

    [Fact]
    public async Task PersistedSuspension_PreventsARecreatedServiceFromClaiming()
    {
        var settings = new MockSettingsService();
        var first = CreateDrain(settings, out var firstQueue, out _, null);
        await first.SuspendAndWaitForQuiescenceAsync();
        var recreated = CreateDrain(settings, out var queue, out _, null);
        await recreated.StartAsync();
        await recreated.TriggerDrainAsync();
        settings.QueueDeliverySuspended.Should().BeTrue();
        queue.Verify(x => x.ClaimNextPendingLocationWithPriorityAsync(), Times.Never);
        first.Dispose(); recreated.Dispose();
    }

    [Fact]
    public async Task Preparation_WaitsForActiveDeliveryBeforeExportReady()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ApiResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateDrain(new MockSettingsService(), out _, out _, () => { entered.TrySetResult(); return release.Task; });
        await service.StartAsync();
        var drain = service.TriggerDrainAsync();
        await entered.Task;
        var preparation = service.SuspendAndWaitForQuiescenceAsync();
        preparation.IsCompleted.Should().BeFalse();
        release.SetResult(new ApiResult { Success = true, LocationId = 123 });
        await drain;
        await preparation;
        service.IsDeliverySuspended.Should().BeTrue();
        service.Dispose();
    }

    [Fact]
    public async Task Resume_UsesOrdinaryDrainAndPreservesFailedRowForRetry()
    {
        var settings = new MockSettingsService { QueueDeliverySuspended = true };
        var service = CreateDrain(settings, out var queue, out _, () => Task.FromResult(ApiResult.Fail("temporary", 503, true)));
        await service.StartAsync();
        await service.ResumeAndReconcileAsync();
        service.Stop();
        settings.QueueDeliverySuspended.Should().BeFalse();
        queue.Verify(x => x.ResetLocationToPendingAsync(7), Times.AtLeastOnce);
        queue.Verify(x => x.ClearAllQueueAsync(), Times.Never);
        service.Dispose();
    }

    [Fact]
    public async Task OrdinaryDrain_ConfirmsExistingServerIdentityAndMarksSynced()
    {
        var service = CreateDrain(new MockSettingsService(), out var queue, out _,
            () => Task.FromResult(new ApiResult { Success = true, LocationId = 321 }));
        await service.StartAsync();
        await service.TriggerDrainAsync();
        queue.Verify(x => x.MarkServerConfirmedAsync(7, 321), Times.Once);
        queue.Verify(x => x.MarkLocationSyncedAsync(7), Times.Once);
        service.Dispose();
    }

    private static QueueDrainService CreateDrain(MockSettingsService settings, out Mock<ILocationQueueRepository> queue,
        out Mock<IApiClient> api, Func<Task<ApiResult>>? submit)
    {
        queue = new Mock<ILocationQueueRepository>();
        queue.Setup(x => x.ResetStuckLocationsAsync()).ReturnsAsync(0);
        queue.Setup(x => x.ClaimNextPendingLocationWithPriorityAsync()).ReturnsAsync(new QueuedLocation
            { Id = 7, Timestamp = DateTime.UtcNow, Latitude = 1, Longitude = 2, IsUserInvoked = true, IdempotencyKey = Guid.NewGuid().ToString("D") });
        api = new Mock<IApiClient>(); api.SetupGet(x => x.IsConfigured).Returns(true);
        if (submit != null) api.Setup(x => x.CheckInAsync(It.IsAny<LocationLogRequest>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(submit);
        var connectivity = new Mock<IConnectivity>(); connectivity.SetupGet(x => x.NetworkAccess).Returns(NetworkAccess.Internet);
        return new QueueDrainService(api.Object, queue.Object, settings, connectivity.Object, NullLogger<QueueDrainService>.Instance);
    }

    private sealed class TimelineRepositoryFake : ITimelineRepository
    {
        public List<LocalTimelineEntry> Entries { get; } = [];
        public Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry) { entry.Id = 1; Entries.Add(entry); return Task.FromResult(1); }
        public Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(DateTime timestamp, int toleranceSeconds = 2) => Task.FromResult<LocalTimelineEntry?>(null);
        public Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry) => Task.CompletedTask;
        public Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync() => Task.FromResult(Entries);
        public Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(DateTime fromDate, DateTime toDate) => Task.FromResult(new List<LocalTimelineEntry>());
        public Task DeleteLocalTimelineEntryAsync(int id) => throw new NotSupportedException(); public Task<int> DeleteLocalTimelineEntryByTimestampAsync(DateTime timestamp,double latitude,double longitude,int toleranceSeconds=2)=>throw new NotSupportedException(); public Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id)=>throw new NotSupportedException(); public Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId)=>throw new NotSupportedException(); public Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync()=>throw new NotSupportedException(); public Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date)=>throw new NotSupportedException(); public Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> items)=>throw new NotSupportedException(); public Task<int> ClearAllLocalTimelineEntriesAsync()=>throw new NotSupportedException(); public Task<bool> UpdateLocalTimelineServerIdAsync(DateTime timestamp,double latitude,double longitude,int serverId,int toleranceSeconds=2)=>throw new NotSupportedException(); public Task<int> GetLocalTimelineEntryCountAsync()=>throw new NotSupportedException(); public Task<List<LocalTimelineEntry>> GetEntriesMissingServerIdAsync(DateTime? sinceTimestamp=null)=>throw new NotSupportedException(); public Task<bool> UpdateServerIdByQueuedLocationIdAsync(int queuedLocationId,int serverId)=>throw new NotSupportedException(); public Task<int> DeleteByQueuedLocationIdAsync(int queuedLocationId)=>throw new NotSupportedException(); public Task<LocalTimelineEntry?> GetByQueuedLocationIdAsync(int queuedLocationId)=>throw new NotSupportedException();
    }
}
