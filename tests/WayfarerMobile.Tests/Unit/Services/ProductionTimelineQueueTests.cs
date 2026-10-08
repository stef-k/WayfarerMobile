using System.Text.Json;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>Exercises provenance checks in the production persisted mutation queue and its upgrade.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineQueueTests
{
    [Theory]
    [InlineData("Update", false)]
    [InlineData("Update", true)]
    [InlineData("Delete", false)]
    [InlineData("Delete", true)]
    public async Task LegacyUpgrade_HoldsUnprovableOldestWithoutStarvingSafeWork(string operation, bool sourceEvidence)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true, initializeQueue: false);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        await CreateLegacyQueueAsync(context.Database);
        var ambiguousSnapshot = operation == "Delete" && sourceEvidence ? JsonSerializer.Serialize(localOnly) : null;
        var safeSnapshot = operation == "Delete" ? JsonSerializer.Serialize(linked) : null;
        if (operation == "Delete")
            await context.Repository.DeleteLocalTimelineEntryAsync(linked.Id);

        await InsertLegacyAsync(context.Database, 1, operation, 42,
            operation == "Update" && sourceEvidence ? localOnly.Id : null, ambiguousSnapshot, "Ambiguous payload");
        await InsertLegacyAsync(context.Database, 2, operation, 42,
            operation == "Update" ? linked.Id : null, safeSnapshot, "Safe payload");
        await InsertLegacyAsync(context.Database, 3, "Update", 99, null, null, "Unrelated queued payload");

        // Production initialization adds provenance columns without replacing the old table or payloads.
        (await context.Service.GetPendingCountAsync()).Should().Be(3);
        var upgraded = await context.Database.Table<PendingTimelineMutation>().ToListAsync();
        upgraded.Should().HaveCount(3);
        upgraded.Should().OnlyContain(m => !m.ServerIdentityConfirmed && m.AuthorityError == null);
        upgraded.Single(m => m.Id == 1).Notes.Should().Be("Ambiguous payload");
        await context.Service.StartAsync();
        await context.Service.TriggerDrainAsync();
        context.VerifyNoRemoteMutations();
        await context.Service.TriggerDrainAsync();

        var retained = await context.Database.Table<PendingTimelineMutation>().OrderBy(m => m.Id).ToListAsync();
        retained.Select(m => m.Id).Should().Equal(1, 3);
        retained[0].AuthorityError.Should().Contain("held");
        retained[0].SyncAttempts.Should().Be(0);
        retained[0].IsRejected.Should().BeFalse();
        retained[0].Notes.Should().Be("Ambiguous payload");
        retained[0].DeletedEntryJson.Should().Be(ambiguousSnapshot);
        retained[1].Notes.Should().Be("Unrelated queued payload");
        retained[1].AuthorityError.Should().BeNull();
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");
        await context.Service.ClearRejectedMutationsAsync();
        (await context.Database.Table<PendingTimelineMutation>().CountAsync()).Should().Be(2);
        if (operation == "Delete")
            context.Api.Verify(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        else
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(42,
                It.Is<TimelineLocationUpdateRequest>(r => r.Notes == "Safe payload"),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MergeAndDeleteReplacement_RetainUnknownAndDifferentSourceRows()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        var unknown = new PendingTimelineMutation { LocationId = 42, Notes = "Unknown source", IncludeNotes = true };
        var different = new PendingTimelineMutation { LocationId = 42, LocalEntryId = 42, Notes = "Different source" };
        var safe = new PendingTimelineMutation
        {
            LocationId = 42, LocalEntryId = linked.Id, Notes = "Safe pending notes", IncludeNotes = true,
            OriginalNotes = "Original linked notes"
        };
        await context.Database.InsertAllAsync(new[] { unknown, different, safe });
        var identity = TimelineDataService.ToTimelineLocation(linked).Identity;
        await context.Service.UpdateLocationAsync(identity, notes: "Newest notes", includeNotes: true);
        var updates = await context.Database.Table<PendingTimelineMutation>().OrderBy(m => m.Id).ToListAsync();
        updates.Should().HaveCount(3);
        updates.Single(m => m.Id == unknown.Id).Notes.Should().Be("Unknown source");
        updates.Single(m => m.Id == different.Id).Notes.Should().Be("Different source");
        updates.Single(m => m.Id == safe.Id).Notes.Should().Be("Newest notes");
        updates.Single(m => m.Id == safe.Id).OriginalNotes.Should().Be("Original linked notes");

        await context.Service.DeleteLocationAsync(identity);
        var remaining = await context.Database.Table<PendingTimelineMutation>().ToListAsync();
        remaining.Should().HaveCount(3);
        remaining.Should().Contain(m => m.Id == unknown.Id && m.Notes == "Unknown source");
        remaining.Should().Contain(m => m.Id == different.Id && m.Notes == "Different source");
        var deletion = remaining.Single(m => m.OperationType == "Delete");
        deletion.ServerIdentityConfirmed.Should().BeTrue();
        deletion.LocalEntryId.Should().Be(linked.Id);
        JsonSerializer.Deserialize<LocalTimelineEntry>(deletion.DeletedEntryJson!)!.ServerId.Should().Be(42);
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");
        context.VerifyNoRemoteMutations();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerOriginWithoutLocalCopy_RetainsAuthorityAcrossRestart(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var identity = TimelineEntryIdentity.FromServer(42);
        if (delete)
            await context.Service.DeleteLocationAsync(identity);
        else
            await context.Service.UpdateLocationAsync(identity, notes: "Server-origin notes", includeNotes: true);

        var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
        pending.ServerIdentityConfirmed.Should().BeTrue();
        pending.LocalEntryId.Should().BeNull();
        pending.DeletedEntryJson.Should().BeNull();
        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();
        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        if (delete)
            context.Api.Verify(x => x.DeleteTimelineLocationAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        else
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(42, It.IsAny<TimelineLocationUpdateRequest>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedFlag_CannotOverrideContradictorySourceEvidence(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, linked) = await context.SeedCollisionAsync();
        var mutation = new PendingTimelineMutation
        {
            LocationId = 42, LocalEntryId = 42, ServerIdentityConfirmed = true,
            OperationType = delete ? "Delete" : "Update",
            DeletedEntryJson = delete ? JsonSerializer.Serialize(linked) : null
        };
        await context.Database.InsertAsync(mutation);
        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();
        context.VerifyNoRemoteMutations();
        var held = await context.Database.GetAsync<PendingTimelineMutation>(mutation.Id);
        held.AuthorityError.Should().NotBeNullOrEmpty();
        held.SyncAttempts.Should().Be(0);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.Notes.Should().Be("Linked notes");
    }

    /// <summary>Creates the pre-285 table shape, intentionally lacking the two new provenance columns.</summary>
    private static Task<int> CreateLegacyQueueAsync(SQLiteAsyncConnection database) => database.ExecuteAsync("""
        CREATE TABLE PendingTimelineMutations (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            OperationType TEXT,
            LocationId INTEGER,
            LocalEntryId INTEGER,
            Latitude REAL,
            Longitude REAL,
            LocalTimestamp BIGINT,
            Notes TEXT,
            IncludeNotes INTEGER,
            ActivityTypeId INTEGER,
            ClearActivity INTEGER,
            OriginalLatitude REAL,
            OriginalLongitude REAL,
            OriginalTimestamp BIGINT,
            OriginalNotes TEXT,
            OriginalActivityType TEXT,
            DeletedEntryJson TEXT,
            CreatedAt BIGINT,
            SyncAttempts INTEGER,
            LastSyncAttempt BIGINT,
            LastError TEXT,
            IsRejected INTEGER,
            RejectionReason TEXT)
        """);

    private static Task<int> InsertLegacyAsync(SQLiteAsyncConnection database, int id, string operation,
        int locationId, int? localEntryId, string? snapshot, string notes) => database.ExecuteAsync("""
        INSERT INTO PendingTimelineMutations
        (Id, OperationType, LocationId, LocalEntryId, DeletedEntryJson, Notes, IncludeNotes, CreatedAt, SyncAttempts, IsRejected)
        VALUES (?, ?, ?, ?, ?, ?, 1, ?, 0, 0)
        """, id, operation, locationId, localEntryId, snapshot, notes, DateTime.UtcNow.AddMinutes(id));
}
