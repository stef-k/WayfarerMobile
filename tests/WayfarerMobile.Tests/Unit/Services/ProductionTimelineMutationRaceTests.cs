using System.Net;
using System.Reflection;
using System.Text.Json;
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MutationCreation_DelayedCallbackAndEnrichmentPreserveSourceAndRollback(bool online, bool reject)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
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
        var callbackDispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownershipAtLocalWrite = false;
        var response = new TaskCompletionSource<TimelineUpdateResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).Returns(() =>
            {
                requestStarted.TrySetResult();
                return response.Task;
            });
        if (!online)
        {
            // SQLite's existing trace boundary pauses the optimistic write while the real callback starts.
            // Its queries run on the transaction's connection, without releasing ownership to cleanup.
            context.Database.Tracer = sql =>
            {
                if (!sql.Contains("update \"LocalTimelineEntries\"", StringComparison.OrdinalIgnoreCase)) return;
                ownershipAtLocalWrite = context.Database.GetConnection().Table<PendingTimelineMutation>()
                    .Where(m => m.LocalEntryId == original.Id && m.ServerIdentityConfirmed).Count() == 1;
                requestStarted.TrySetResult();
                if (!callbackDispatched.Task.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The delayed capture callback was not dispatched.");
            };
            context.Database.Trace = true;
        }
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
            callbackDispatched.TrySetResult();
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
            if (!online)
            {
                ownershipAtLocalWrite.Should().BeTrue("durable intent must precede the optimistic write in its transaction");
                context.VerifyNoRemoteMutations();
            }
        }
        finally
        {
            callbackDispatched.TrySetResult();
            if (online && reject)
                response.TrySetException(new HttpRequestException("Rejected", null, HttpStatusCode.BadRequest));
            else if (online)
                response.TrySetException(new HttpRequestException("Connection lost"));
            await update.WaitAsync(TimeSpan.FromSeconds(5));
            context.Database.Trace = false;
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
                It.IsAny<CancellationToken>()), Times.Exactly(online ? 2 : 1));
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
            try { await mutation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptimisticWriteFails_RollsBackMergedIntentOrDeleteReplacement(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, original) = await context.SeedCollisionAsync();
        var identity = TimelineDataService.ToTimelineLocation(original).Identity;
        await context.Service.UpdateLocationAsync(identity, notes: "Already queued", includeNotes: true);
        var source = await context.Repository.GetLocalTimelineEntryAsync(original.Id);
        var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
        var operation = delete ? "DELETE" : "UPDATE";
        await context.Database.ExecuteAsync($"""
            CREATE TRIGGER reject_optimistic_write BEFORE {operation} ON LocalTimelineEntries
            BEGIN SELECT RAISE(ABORT, 'Local write unavailable'); END
            """);

        if (delete)
            await Assert.ThrowsAsync<SQLiteException>(() => context.Service.DeleteLocationAsync(identity));
        else
            await Assert.ThrowsAsync<SQLiteException>(() => context.Service.UpdateLocationAsync(identity,
                latitude: 39, notes: "Must roll back", includeNotes: true));

        (await context.Repository.GetLocalTimelineEntryAsync(original.Id)).Should().BeEquivalentTo(source);
        (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Should().BeEquivalentTo([pending]);
        context.VerifyNoRemoteMutations();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ActiveDelivery_ExcludesCompetingDrainsAndRemainsRecoverableAfterInterruption(bool immediate, bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: immediate);
        var (_, original) = await context.SeedCollisionAsync();
        var held = new PendingTimelineMutation { LocationId = 99, AuthorityError = "Historical work held", Notes = "Retain" };
        await context.Database.InsertAsync(held);
        var identity = TimelineDataService.ToTimelineLocation(original).Identity;
        if (!immediate)
        {
            await context.Service.UpdateLocationAsync(identity, notes: "In flight", includeNotes: true);
            await context.RestartOnlineAsync();
        }
        await context.Service.StartAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).Returns(async () =>
            {
                requestStarted.TrySetResult();
                return new TimelineUpdateResponse { Success = await response.Task };
            });
        context.Api.Setup(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>())).Returns(() =>
        {
            requestStarted.TrySetResult();
            return response.Task;
        });
        Task delivery;
        if (!immediate)
            delivery = context.Service.TriggerDrainAsync();
        else if (delete)
            delivery = context.Service.DeleteLocationAsync(identity);
        else
            delivery = context.Service.UpdateLocationAsync(identity, notes: "In flight", includeNotes: true);
        Task? competingDrains = null;
        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Make the competing drains eligible without sleeping through the five-second rate limit.
            // This exercises processing exclusion rather than receiving a rate-limit-only pass.
            typeof(TimelineSyncService).GetField("_lastSyncTime", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(context.Service, DateTime.MinValue);
            competingDrains = Task.WhenAll(context.Service.TriggerDrainAsync(), context.Service.TriggerDrainAsync());
            await competingDrains.WaitAsync(TimeSpan.FromSeconds(5));
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
                It.IsAny<CancellationToken>()), delete ? Times.Never() : Times.Once());
            context.Api.Verify(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>()),
                delete ? Times.Once() : Times.Never());
            var committed = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single(m => m.Id != held.Id);
            committed.LocalEntryId.Should().Be(original.Id);
            committed.ServerIdentityConfirmed.Should().BeTrue();
            if (delete)
            {
                (await context.Repository.GetLocalTimelineEntryAsync(original.Id)).Should().BeNull();
                JsonSerializer.Deserialize<LocalTimelineEntry>(committed.DeletedEntryJson!)!.Should().BeEquivalentTo(original);
            }
        }
        finally
        {
            context.Service.Stop();
            response.TrySetCanceled();
            await delivery.WaitAsync(TimeSpan.FromSeconds(5));
            if (competingDrains != null)
                await competingDrains.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var retry = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single(m => m.Id != held.Id);
        retry.CanSync.Should().BeTrue();
        retry.SyncAttempts.Should().Be(1);
        if (!delete) retry.OriginalNotes.Should().Be(original.Notes);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineUpdateResponse { Success = true });
        context.Api.Setup(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();
        (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Should().BeEquivalentTo([held]);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), delete ? Times.Never() : Times.Exactly(2));
        context.Api.Verify(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>()),
            delete ? Times.Exactly(2) : Times.Never());
    }

    [Fact]
    public async Task OnlineEditDuringDelivery_PreservesLaterEditAndOtherQueuedWork()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, original) = await context.SeedCollisionAsync();
        var identity = TimelineDataService.ToTimelineLocation(original).Identity;
        await context.Service.UpdateLocationAsync(identity, latitude: 39);
        await context.Service.UpdateLocationAsync(TimelineEntryIdentity.FromServer(99), notes: "Other work", includeNotes: true);
        var other = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single(m => m.LocationId == 99);
        await context.RestartOnlineAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<TimelineUpdateResponse?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).Returns((int _, TimelineLocationUpdateRequest request, CancellationToken _) =>
            {
                if (request.Notes != "Sending now")
                    return Task.FromResult<TimelineUpdateResponse?>(new TimelineUpdateResponse { Success = true });
                requestStarted.TrySetResult();
                return response.Task;
            });
        var first = context.Service.UpdateLocationAsync(identity, notes: "Sending now", includeNotes: true);
        Task? later = null;
        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            later = context.Service.UpdateLocationAsync(identity, notes: "Later edit", includeNotes: true);
            later.IsCompleted.Should().BeFalse("merging must wait for acknowledgement of the active intent");
            var inFlight = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single(m => m.LocationId == 42);
            inFlight.Notes.Should().Be("Sending now");
            inFlight.Latitude.Should().Be(39);
            inFlight.OriginalNotes.Should().Be(original.Notes);
            inFlight.OriginalLatitude.Should().Be(original.Latitude);
        }
        finally
        {
            response.TrySetResult(new TimelineUpdateResponse { Success = true });
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (later != null) await later.WaitAsync(TimeSpan.FromSeconds(5));
        }
        (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!.Notes.Should().Be("Later edit");
        (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Should().BeEquivalentTo([other]);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.Is<TimelineLocationUpdateRequest>(r =>
            r.Notes == "Sending now" && r.Latitude == 39), It.IsAny<CancellationToken>()), Times.Once);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.Is<TimelineLocationUpdateRequest>(r =>
            r.Notes == "Later edit"), It.IsAny<CancellationToken>()), Times.Once);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(99, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
