using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLite;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure;
using WayfarerMobile.Tests.Infrastructure.Mocks;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Proves durable Timeline ownership at creation and during competing delivery and enrichment.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineMutationRaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlineRequest_DelayedCallbackAndEnrichmentPreserveSourceAndRollback(bool reject)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        var settings = new MockSettingsService();
        var queue = new LocationQueueRepository(() => Task.FromResult(context.Database), settings);
        var logger = new Mock<ILogger<LocalTimelineStorageService>>();
        using var storage = new LocalTimelineStorageService(context.Repository, queue, settings,
            new LocalTimelineFilter(settings), logger.Object);
        await storage.InitializeAsync();
        var location = new LocationData
        {
            Latitude = 38, Longitude = 24, Timestamp = DateTime.Today.ToUniversalTime().AddHours(1)
        };
        var queuedId = await queue.QueueLocationAsync(location);
        await queue.MarkServerConfirmedAsync(queuedId, 42);
        var server = new TimelineLocation
        {
            Id = 42, Timestamp = location.Timestamp,
            Coordinates = new TimelineCoordinates { X = 24, Y = 38 },
            Notes = "Original notes", ActivityType = "Walk"
        };
        context.Api.Setup(x => x.GetTimelineLocationsAsync("day", It.IsAny<int>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineResponse { Data = [server] });
        var date = location.Timestamp.ToLocalTime().Date;
        (await context.Data.EnrichFromServerAsync(date)).Should().BeTrue();
        var original = (await context.Repository.GetLocalTimelineEntryByServerIdAsync(42))!;
        original.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        await context.Repository.UpdateLocalTimelineEntryAsync(original);
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<TimelineUpdateResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).Returns(() =>
            {
                requestStarted.TrySetResult();
                return response.Task;
            });
        var update = context.Service.UpdateLocationAsync(TimelineDataService.ToTimelineLocation(original).Identity,
            latitude: 39, longitude: 25, notes: null, includeNotes: true, clearActivity: true);
        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(_ => callbackCompleted.TrySetResult()));
            LocationServiceCallbacks.NotifyLocationQueued(location, queuedId);
            await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!;
            duplicate.Id.Should().NotBe(original.Id);
            (await context.Data.EnrichFromServerAsync(date)).Should().BeTrue();

            var retained = await context.Repository.GetLocalTimelineEntryAsync(original.Id);
            retained.Should().NotBeNull("an in-flight request must already own its exact local source");
            retained!.Latitude.Should().Be(39);
            retained.Longitude.Should().Be(25);
            retained.Notes.Should().BeNull();
            retained.ActivityType.Should().BeNull();
            (await context.Repository.GetLocalTimelineEntryAsync(duplicate.Id)).Should().BeNull();
            var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
            pending.LocalEntryId.Should().Be(original.Id);
            pending.ServerIdentityConfirmed.Should().BeTrue();
            pending.OriginalNotes.Should().Be(original.Notes);
            pending.OriginalActivityType.Should().Be(original.ActivityType);
            pending.OriginalLatitude.Should().Be(original.Latitude);
        }
        finally
        {
            if (reject)
                response.TrySetException(new HttpRequestException("Rejected", null, HttpStatusCode.BadRequest));
            else
                response.TrySetException(new HttpRequestException("Connection lost"));
            await update.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var source = (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!;
        if (reject)
        {
            source.Should().BeEquivalentTo(original, options => options.Excluding(e => e.LastEnrichedAt));
            (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        }
        else
        {
            source.Notes.Should().BeNull();
            var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
            pending.CanSync.Should().BeTrue();
            pending.OriginalNotes.Should().Be(original.Notes);
            storage.Dispose();
            context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineUpdateResponse { Success = true });
            await context.RestartOnlineAsync();
            await context.Service.TriggerDrainAsync();
            (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.Is<TimelineLocationUpdateRequest>(r =>
                r.Latitude == 39 && r.Longitude == 25 && r.Notes == null && r.ClearActivity == true),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupCommitsBeforeCreation_RejectsStaleSourceWithoutOrphanedIntent(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, original) = await context.SeedCollisionAsync();
        original.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        await context.Repository.UpdateLocalTimelineEntryAsync(original);
        var sourceRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<ITimelineRepository>();
        repository.Setup(x => x.GetLocalTimelineEntryAsync(original.Id)).Returns(async () =>
        {
            var source = await context.Repository.GetLocalTimelineEntryAsync(original.Id);
            sourceRead.TrySetResult();
            await resume.Task;
            return source;
        });
        repository.Setup(x => x.UpdateLocalTimelineEntryAsync(It.IsAny<LocalTimelineEntry>()))
            .Returns((LocalTimelineEntry entry) => context.Repository.UpdateLocalTimelineEntryAsync(entry));
        repository.Setup(x => x.DeleteLocalTimelineEntryAsync(original.Id))
            .Returns(() => context.Repository.DeleteLocalTimelineEntryAsync(original.Id));
        using var service = new TimelineSyncService(context.Api.Object, repository.Object, context.DatabaseService,
            Mock.Of<IConnectivity>(), NullLogger<TimelineSyncService>.Instance);
        var identity = TimelineDataService.ToTimelineLocation(original).Identity;
        var mutation = delete ? service.DeleteLocationAsync(identity)
            : service.UpdateLocationAsync(identity, notes: "Must not be orphaned", includeNotes: true);
        try
        {
            await sourceRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = new LocalTimelineEntry
            {
                ServerId = 42, ServerLinkageConfirmed = true, Timestamp = original.Timestamp, Notes = "Newer cache"
            };
            await context.Repository.InsertLocalTimelineEntryAsync(duplicate);
            await context.Repository.PrepareConfirmedTimelineEntriesForEnrichmentAsync(original.Timestamp.ToLocalTime().Date);
            (await context.Repository.GetLocalTimelineEntryAsync(original.Id)).Should().BeNull();
            (await context.Repository.GetLocalTimelineEntryAsync(duplicate.Id)).Should().BeEquivalentTo(duplicate);
        }
        finally
        {
            resume.TrySetResult();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => mutation);
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        context.VerifyNoRemoteMutations();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MutationPersistenceFails_LeavesSourceAndExistingWorkUnchanged(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, original) = await context.SeedCollisionAsync();
        await context.Database.ExecuteAsync("""
            CREATE TRIGGER reject_mutation_insert BEFORE INSERT ON PendingTimelineMutations
            BEGIN SELECT RAISE(ABORT, 'Mutation storage unavailable'); END
            """);
        var identity = TimelineDataService.ToTimelineLocation(original).Identity;
        if (delete)
            await Assert.ThrowsAsync<SQLiteException>(() => context.Service.DeleteLocationAsync(identity));
        else
            await Assert.ThrowsAsync<SQLiteException>(() => context.Service.UpdateLocationAsync(identity,
                notes: "Partial update", includeNotes: true));

        (await context.Repository.GetLocalTimelineEntryAsync(original.Id)).Should().BeEquivalentTo(original);
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        context.VerifyNoRemoteMutations();
    }
}
