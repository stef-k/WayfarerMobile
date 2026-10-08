using System.Net;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure;

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
    public async Task LegacyFalseServerLink_CannotUpdateOrDeleteAnotherServerRecord(bool online, bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        // Older linkage heuristics could assign an imported row the ID of a different owned location.
        localOnly.ServerId = 42;
        localOnly.LastEnrichedAt = DateTime.UtcNow;
        await context.Repository.UpdateLocalTimelineEntryAsync(localOnly);
        var syncEvents = 0;
        context.Service.SyncCompleted += (_, _) => syncEvents++;
        context.Service.SyncQueued += (_, _) => syncEvents++;
        context.Service.SyncRejected += (_, _) => syncEvents++;
        var selected = TimelineDataService.ToTimelineLocation(localOnly);

        if (delete)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.DeleteLocationAsync(selected.Identity));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.UpdateLocationAsync(selected.Identity,
                1, 2, DateTime.UtcNow.AddDays(-1), "Changed notes", true, 1, activityTypeName: "Cycle"));

        var retained = await context.Repository.GetLocalTimelineEntryAsync(linked.Id);
        retained.Should().NotBeNull();
        retained!.Notes.Should().Be("Linked notes");
        retained.Latitude.Should().Be(38);
        retained.Longitude.Should().Be(24);
        retained.ActivityType.Should().Be("Run");
        retained.Timestamp.Should().Be(linked.Timestamp);
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");
        syncEvents.Should().Be(0);
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<TimelineLocationUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Api.Verify(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task LinkedCollision_UsesServerIdentityAndRetainsOfflineReplay(bool online, bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        var identity = TimelineDataService.ToTimelineLocation(linked).Identity;
        if (delete)
            await context.Service.DeleteLocationAsync(identity);
        else
            await context.Service.UpdateLocationAsync(identity, 1, 2, notes: "Edited linked notes",
                includeNotes: true, activityTypeId: 7, activityTypeName: "Cycle");

        var changed = await context.Repository.GetLocalTimelineEntryAsync(linked.Id);
        if (delete)
            changed.Should().BeNull();
        else
        {
            changed!.Notes.Should().Be("Edited linked notes");
            changed.Latitude.Should().Be(1);
            changed.Longitude.Should().Be(2);
            changed.ActivityType.Should().Be("Cycle");
        }
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");

        if (!online)
        {
            context.VerifyNoRemoteMutations();
            var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
            pending.LocationId.Should().Be(42);
            pending.LocalEntryId.Should().Be(linked.Id);
            pending.ServerIdentityConfirmed.Should().BeTrue();
            await context.RestartOnlineAsync();
            await context.Service.TriggerDrainAsync();
        }

        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        if (delete)
            context.Api.Verify(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        else
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(42,
                It.Is<TimelineLocationUpdateRequest>(r => r.Notes == "Edited linked notes" && r.Latitude == 1),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownOrStaleIdentity_CannotBorrowAnotherRowsServerLinkage()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        await context.SeedCollisionAsync();
        var identities = new[] { new TimelineLocation { Id = 42 }.Identity, TimelineEntryIdentity.FromLocal(42, 42) };
        foreach (var identity in identities)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.UpdateLocationAsync(identity,
                notes: "Wrong record", includeNotes: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.DeleteLocationAsync(identity));
        }
        context.VerifyNoRemoteMutations();
        (await context.Repository.GetLocalTimelineEntryAsync(43))!.Notes.Should().Be("Linked notes");
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedServerRejection_RollsBackOnlyTheAuthorizedEntry(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        var failure = new HttpRequestException("Rejected", null, HttpStatusCode.BadRequest);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        context.Api.Setup(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var identity = TimelineDataService.ToTimelineLocation(linked).Identity;
        if (delete)
            await context.Service.DeleteLocationAsync(identity);
        else
            await context.Service.UpdateLocationAsync(identity, notes: "Rejected notes", includeNotes: true);

        (await context.Repository.GetLocalTimelineEntryByServerIdAsync(42))!.Notes.Should().Be("Linked notes");
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LinkedTemporaryOnlineFailure_PersistsProvenanceForRetry()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        var (_, linked) = await context.SeedCollisionAsync();
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync((TimelineUpdateResponse?)null);
        await context.Service.UpdateLocationAsync(TimelineDataService.ToTimelineLocation(linked).Identity,
            notes: "Retried notes", includeNotes: true);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.Notes.Should().Be("Retried notes");
        var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
        pending.ServerIdentityConfirmed.Should().BeTrue();
        pending.OriginalNotes.Should().Be("Linked notes");

        context.Api.Setup(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new TimelineUpdateResponse { Success = true });
        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();
        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
