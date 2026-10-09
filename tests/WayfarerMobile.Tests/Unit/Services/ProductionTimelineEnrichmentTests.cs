using System.Net;
using Microsoft.Extensions.Logging;
using Moq;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure;
using WayfarerMobile.Tests.Infrastructure.Mocks;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Protects persisted offline edits from production Timeline enrichment and duplicate cleanup.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineEnrichmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedCaptureDuplicate_PreservesOfflineUpdateThroughRestartAndReplay(bool reject)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
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
            Notes = "Server notes", ActivityType = "Walk", Address = "Server address"
        };
        context.Api.Setup(x => x.GetTimelineLocationsAsync("day", It.IsAny<int>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineResponse { Data = [server] });
        var date = location.Timestamp.ToLocalTime().Date;
        (await context.Data.EnrichFromServerAsync(date)).Should().BeTrue();
        var original = (await context.Repository.GetLocalTimelineEntryByServerIdAsync(42))!;
        original.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        await context.Repository.UpdateLocalTimelineEntryAsync(original);
        var unrelated = new LocalTimelineEntry
        {
            ServerId = 99, ServerLinkageConfirmed = true, Timestamp = location.Timestamp,
            Latitude = 1, Longitude = 2, Notes = "Unrelated location"
        };
        await context.Repository.InsertLocalTimelineEntryAsync(unrelated);
        var editedTimestamp = location.Timestamp.AddMinutes(10);
        await context.Service.UpdateLocationAsync(TimelineDataService.ToTimelineLocation(original).Identity,
            latitude: 39, longitude: 25, localTimestamp: editedTimestamp,
            notes: null, includeNotes: true, clearActivity: true);
        var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
        pending.LocalEntryId.Should().Be(original.Id);
        pending.ServerIdentityConfirmed.Should().BeTrue();
        pending.OriginalNotes.Should().Be(original.Notes);
        pending.OriginalActivityType.Should().Be(original.ActivityType);
        pending.OriginalLatitude.Should().Be(original.Latitude);

        // Deliver the real delayed fallback callback after the API cache row owns an offline edit.
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ => callbackCompleted.TrySetResult()));
        LocationServiceCallbacks.NotifyLocationQueued(location, queuedId);
        await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = (await context.Repository.GetByQueuedLocationIdAsync(queuedId))!;
        duplicate.Id.Should().NotBe(original.Id);
        duplicate.IsSynced.Should().BeTrue();
        duplicate.CreatedAt.Should().BeAfter(original.CreatedAt);

        (await context.Data.EnrichFromServerAsync(date)).Should().BeTrue();

        var retained = (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!;
        retained.Should().NotBeNull("the pending update needs its exact original source row");
        retained.Latitude.Should().Be(39);
        retained.Longitude.Should().Be(25);
        retained.Timestamp.Should().Be(editedTimestamp);
        retained.Notes.Should().BeNull("clearing Notes offline must survive stale server enrichment");
        retained.ActivityType.Should().BeNull();
        (await context.Repository.GetLocalTimelineEntryAsync(duplicate.Id)).Should().BeNull();
        (await context.Database.GetAsync<PendingTimelineMutation>(pending.Id)).Should().BeEquivalentTo(pending);
        context.VerifyNoRemoteMutations();
        storage.Dispose();
        if (reject)
            context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
                It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("Rejected", null, HttpStatusCode.BadRequest));

        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();

        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.Is<TimelineLocationUpdateRequest>(r =>
            r.Latitude == 39 && r.Longitude == 25 && r.LocalTimestamp == editedTimestamp
            && r.Notes == null && r.ClearActivity == true), It.IsAny<CancellationToken>()), Times.Once);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(), It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        context.Api.Verify(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        var replayed = (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!;
        if (reject)
        {
            replayed.Should().BeEquivalentTo(original, options => options.Excluding(e => e.LastEnrichedAt));
            var rejected = await context.Database.GetAsync<PendingTimelineMutation>(pending.Id);
            rejected.IsRejected.Should().BeTrue();
            rejected.AuthorityError.Should().BeNull();
            rejected.OriginalNotes.Should().Be(pending.OriginalNotes);
            await context.Service.ClearRejectedMutationsAsync();
        }
        else
            replayed.Should().BeEquivalentTo(retained);
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        (await context.Repository.GetLocalTimelineEntryAsync(unrelated.Id)).Should().BeEquivalentTo(unrelated);

        server.Address = "Refreshed address";
        server.ActivityType = "Train";
        server.Notes = "Notes after synchronization";
        (await context.Data.EnrichFromServerAsync(date)).Should().BeTrue();
        var enriched = (await context.Repository.GetLocalTimelineEntryAsync(original.Id))!;
        enriched.Address.Should().Be(server.Address);
        enriched.ActivityType.Should().Be(server.ActivityType);
        enriched.Notes.Should().Be(reject ? original.Notes : server.Notes);
        (await context.Repository.GetLocalTimelineEntryAsync(unrelated.Id)).Should().BeEquivalentTo(unrelated);
    }

    [Fact]
    public async Task UnreferencedDuplicates_KeepNewestBeforeMutationQueueInitialization()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false, initializeQueue: false);
        var timestamp = DateTime.Today.ToUniversalTime().AddHours(1);
        var older = new LocalTimelineEntry
        {
            ServerId = 42, ServerLinkageConfirmed = true, Timestamp = timestamp,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var newer = new LocalTimelineEntry
        {
            ServerId = 42, ServerLinkageConfirmed = true, Timestamp = timestamp, CreatedAt = DateTime.UtcNow
        };
        await context.Repository.InsertLocalTimelineEntryAsync(older);
        await context.Repository.InsertLocalTimelineEntryAsync(newer);
        context.Api.Setup(x => x.GetTimelineLocationsAsync("day", It.IsAny<int>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineResponse
        {
            Data = [new TimelineLocation { Id = 42, Timestamp = timestamp, Address = "Server address" }]
        });

        (await context.Data.EnrichFromServerAsync(timestamp.ToLocalTime().Date)).Should().BeTrue();

        (await context.Repository.GetLocalTimelineEntryAsync(older.Id)).Should().BeNull();
        (await context.Repository.GetLocalTimelineEntryAsync(newer.Id))!.Address.Should().Be("Server address");
        (await context.Repository.GetLocalTimelineEntryCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_RetainsIndependentHeldAndExhaustedSourcesAndRemovesOnlyRedundantConfirmedRows()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (suspicious, heldSource) = await context.SeedCollisionAsync();
        suspicious.ServerId = 42;
        await context.Repository.UpdateLocalTimelineEntryAsync(suspicious);
        heldSource.CreatedAt = DateTime.UtcNow.AddMinutes(-3);
        await context.Repository.UpdateLocalTimelineEntryAsync(heldSource);
        var exhaustedSource = new LocalTimelineEntry
        {
            ServerId = 42, ServerLinkageConfirmed = true, Timestamp = heldSource.Timestamp,
            Latitude = 38, Longitude = 24, Notes = "Other original notes", ActivityType = "Walk",
            CreatedAt = heldSource.CreatedAt.AddMinutes(1)
        };
        var redundant = new LocalTimelineEntry
        {
            ServerId = 42, ServerLinkageConfirmed = true, Timestamp = heldSource.Timestamp,
            Latitude = 38, Longitude = 24, CreatedAt = heldSource.CreatedAt.AddMinutes(2)
        };
        await context.Repository.InsertLocalTimelineEntryAsync(exhaustedSource);
        await context.Repository.InsertLocalTimelineEntryAsync(redundant);
        foreach (var source in new[] { heldSource, exhaustedSource })
            await context.Service.UpdateLocationAsync(TimelineDataService.ToTimelineLocation(source).Identity,
                notes: null, includeNotes: true, clearActivity: true);
        var held = (await context.Database.Table<PendingTimelineMutation>()
            .Where(m => m.LocalEntryId == heldSource.Id).ToListAsync()).Single();
        held.AuthorityError = "Held for inspection";
        held.LastError = held.AuthorityError;
        await context.Database.UpdateAsync(held);
        var exhausted = (await context.Database.Table<PendingTimelineMutation>()
            .Where(m => m.LocalEntryId == exhaustedSource.Id).ToListAsync()).Single();
        exhausted.SyncAttempts = PendingTimelineMutation.MaxSyncAttempts;
        exhausted.LastError = "Network unavailable";
        await context.Database.UpdateAsync(exhausted);
        context.Api.Setup(x => x.GetTimelineLocationsAsync("day", It.IsAny<int>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineResponse
        {
            Data = [new TimelineLocation
            {
                Id = 42, Timestamp = heldSource.Timestamp, Notes = "Stale server notes",
                ActivityType = "Stale server activity", Address = "Refreshed server address"
            }]
        });

        (await context.Data.EnrichFromServerAsync(heldSource.Timestamp.ToLocalTime().Date)).Should().BeTrue();

        foreach (var source in new[] { heldSource, exhaustedSource })
        {
            var retained = (await context.Repository.GetLocalTimelineEntryAsync(source.Id))!;
            retained.Should().NotBeNull();
            retained.ServerId.Should().Be(42);
            retained.Notes.Should().BeNull();
            retained.ActivityType.Should().BeNull();
        }
        (await context.Repository.GetLocalTimelineEntryAsync(exhaustedSource.Id))!.Address
            .Should().Be("Refreshed server address", "the newest referenced row is the primary cache row");
        (await context.Repository.GetLocalTimelineEntryAsync(redundant.Id)).Should().BeNull();
        (await context.Repository.GetLocalTimelineEntryAsync(suspicious.Id)).Should().BeEquivalentTo(suspicious);
        (await context.Database.Table<PendingTimelineMutation>().ToListAsync())
            .Should().BeEquivalentTo(new[] { held, exhausted });
        context.VerifyNoRemoteMutations();
    }
}
