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

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class QueueRecoverySafetyTests
{
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
        queue.Verify(x => x.ClaimOldestPendingLocationAsync(It.IsAny<int>()), Times.Never);
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
        release.SetResult(ApiResult.Success(123));
        await drain;
        await preparation;
        service.IsDeliverySuspended.Should().BeTrue();
        service.Dispose();
    }

    private static QueueDrainService CreateDrain(MockSettingsService settings, out Mock<ILocationQueueRepository> queue,
        out Mock<IApiClient> api, Func<Task<ApiResult>>? submit)
    {
        queue = new Mock<ILocationQueueRepository>();
        queue.Setup(x => x.ResetStuckLocationsAsync()).ReturnsAsync(0);
        queue.Setup(x => x.ClaimOldestPendingLocationAsync(It.IsAny<int>())).ReturnsAsync(new QueuedLocation
            { Id = 7, Timestamp = DateTime.UtcNow, Latitude = 1, Longitude = 2, IsUserInvoked = true, IdempotencyKey = Guid.NewGuid().ToString("D") });
        api = new Mock<IApiClient>(); api.SetupGet(x => x.IsConfigured).Returns(true);
        if (submit != null) api.Setup(x => x.CheckInAsync(It.IsAny<LocationData>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(submit);
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
