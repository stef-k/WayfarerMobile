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
        await using var context = await TimelineMutationContext.CreateAsync(online: false, initializeQueue: false);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        localOnly.Id.Should().Be(42);
        linked.Id.Should().Be(43);
        await CreateLegacyQueueAsync(context.Database);
        var ambiguousSnapshot = operation == "Delete" && sourceEvidence ? JsonSerializer.Serialize(localOnly) : null;
        // Historical ID collisions could bind another row or save its apparently consistent snapshot.
        var falselyBoundSnapshot = operation == "Delete" ? JsonSerializer.Serialize(linked) : null;
        if (operation == "Delete")
            await context.Repository.DeleteLocalTimelineEntryAsync(linked.Id);

        await InsertLegacyAsync(context.Database, 1, operation, 42,
            operation == "Update" && sourceEvidence ? localOnly.Id : null, ambiguousSnapshot, "Ambiguous payload");
        await InsertLegacyAsync(context.Database, 2, operation, 42,
            operation == "Update" ? linked.Id : null, falselyBoundSnapshot, "Falsely bound payload");
        await InsertLegacyAsync(context.Database, 3, "Update", 99, null, null, "Unrelated queued payload");

        // Production initialization adds provenance columns without replacing the old table or payloads.
        (await context.Service.GetPendingCountAsync()).Should().Be(3);
        var upgraded = await context.Database.Table<PendingTimelineMutation>().ToListAsync();
        upgraded.Should().HaveCount(3);
        upgraded.Should().OnlyContain(m => !m.ServerIdentityConfirmed && m.AuthorityError == null);
        upgraded.Single(m => m.Id == 1).Notes.Should().Be("Ambiguous payload");
        upgraded.Single(m => m.Id == 2).OriginalNotes.Should().Be("Legacy rollback notes");

        // Only newly authorized work is safe; all historical bindings remain unconfirmed.
        var confirmedIdentity = TimelineEntryIdentity.FromServer(99);
        if (operation == "Delete")
            await context.Service.DeleteLocationAsync(confirmedIdentity);
        else
            await context.Service.UpdateLocationAsync(confirmedIdentity, notes: "Confirmed payload", includeNotes: true);

        await context.RestartOnlineAsync();
        foreach (var _ in upgraded)
            await context.Service.TriggerDrainAsync();
        context.VerifyNoRemoteMutations();
        await context.Service.TriggerDrainAsync();

        var retained = await context.Database.Table<PendingTimelineMutation>().OrderBy(m => m.Id).ToListAsync();
        retained.Should().BeEquivalentTo(upgraded, options => options
            .Excluding(m => m.AuthorityError).Excluding(m => m.LastError).Excluding(m => m.CanSync));
        retained.Should().OnlyContain(m => m.AuthorityError != null && m.AuthorityError.Contains("legacy")
            && m.LastError == m.AuthorityError && !m.CanSync);
        (await context.Repository.GetLocalTimelineEntryAsync(localOnly.Id))!.Notes.Should().Be("Imported notes");

        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();
        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        await context.Service.ClearRejectedMutationsAsync();
        (await context.Database.Table<PendingTimelineMutation>().OrderBy(m => m.Id).ToListAsync())
            .Should().BeEquivalentTo(retained);
        if (operation == "Delete")
            context.Api.Verify(x => x.DeleteTimelineLocationAsync(99, It.IsAny<CancellationToken>()), Times.Once);
        else
            context.Api.Verify(x => x.UpdateTimelineLocationAsync(99,
                It.Is<TimelineLocationUpdateRequest>(r => r.Notes == "Confirmed payload"),
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedMutation_DoesNotMergeOrReplaceFalselyBoundLegacyUpdate(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, linked) = await context.SeedCollisionAsync();
        var legacy = new PendingTimelineMutation
        {
            LocationId = 42, LocalEntryId = linked.Id, ServerIdentityConfirmed = false,
            Notes = "Legacy payload", IncludeNotes = true, Latitude = 12,
            OriginalNotes = "Legacy rollback notes", OriginalLatitude = 38,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        await context.Database.InsertAsync(legacy);
        var identity = TimelineDataService.ToTimelineLocation(linked).Identity;
        if (delete)
            await context.Service.DeleteLocationAsync(identity);
        else
            await context.Service.UpdateLocationAsync(identity, notes: "Confirmed notes", includeNotes: true);

        var remaining = await context.Database.Table<PendingTimelineMutation>().ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Single(m => m.Id == legacy.Id).Should().BeEquivalentTo(legacy);
        var confirmed = remaining.Single(m => m.Id != legacy.Id);
        confirmed.ServerIdentityConfirmed.Should().BeTrue();
        confirmed.LocalEntryId.Should().Be(linked.Id);
        confirmed.OperationType.Should().Be(delete ? "Delete" : "Update");
        context.VerifyNoRemoteMutations();
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
            LocationId = 42, LocalEntryId = linked.Id, ServerIdentityConfirmed = true,
            Notes = "Safe pending notes", IncludeNotes = true,
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedFlag_CannotAuthorizeAnUnconfirmedRowOrDeletionSnapshot(bool delete)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (imported, linked) = await context.SeedCollisionAsync();
        imported.ServerId = 42;
        await context.Repository.UpdateLocalTimelineEntryAsync(imported);
        var mutation = new PendingTimelineMutation
        {
            LocationId = 42, LocalEntryId = imported.Id, ServerIdentityConfirmed = true,
            OperationType = delete ? "Delete" : "Update", Notes = "Unsafe queued edit",
            OriginalNotes = "Unsafe rollback", DeletedEntryJson = delete ? JsonSerializer.Serialize(imported) : null
        };
        await context.Database.InsertAsync(mutation);
        if (delete)
            await context.Repository.DeleteLocalTimelineEntryAsync(imported.Id);

        await context.RestartOnlineAsync();
        await context.Service.TriggerDrainAsync();

        context.VerifyNoRemoteMutations();
        var held = await context.Database.GetAsync<PendingTimelineMutation>(mutation.Id);
        held.AuthorityError.Should().NotBeNullOrEmpty();
        held.SyncAttempts.Should().Be(0);
        held.Notes.Should().Be(mutation.Notes);
        held.DeletedEntryJson.Should().Be(mutation.DeletedEntryJson);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id)).Should().BeEquivalentTo(linked);
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

    /// <summary>Seeds historical payload and rollback data older than subsequently authorized work.</summary>
    private static Task<int> InsertLegacyAsync(SQLiteAsyncConnection database, int id, string operation,
        int locationId, int? localEntryId, string? snapshot, string notes) => database.ExecuteAsync("""
        INSERT INTO PendingTimelineMutations
        (Id, OperationType, LocationId, LocalEntryId, DeletedEntryJson, Notes, IncludeNotes, OriginalNotes, CreatedAt, SyncAttempts, IsRejected)
        VALUES (?, ?, ?, ?, ?, ?, 1, ?, ?, 0, 0)
        """, id, operation, locationId, localEntryId, snapshot, notes, "Legacy rollback notes", DateTime.UtcNow.AddMinutes(id - 10));
}
