using Mapsui.Layers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Controls;
using WayfarerMobile.Tests.Infrastructure;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

/// <summary>Exercises actual Timeline selection and editor entrypoints using production markers, ViewModels, and storage.</summary>
[Collection("SQLite")]
public sealed class ProductionTimelineSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollidingMarkers_SelectTheirOwnDetailsAndLocalOnlyActionsStayReadOnly(bool online)
    {
        await using var context = await TimelineMutationContext.CreateAsync(online);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        using var viewModel = context.CreateViewModel(online);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        using var layer = new WritableLayer();
        var renderer = new TimelineLayerService(NullLogger<TimelineLayerService>.Instance);
        renderer.UpdateTimelineMarkers(layer, viewModel.TimelineGroups.SelectMany(g => g).Select(i => i.Location));
        var identities = layer.GetFeatures().Select(f => (TimelineEntryIdentity)f["TimelineIdentity"]!).ToArray();
        identities.Should().HaveCount(2);
        var localIdentity = identities.Single(i => i.LocalEntryId == 42);
        var linkedIdentity = identities.Single(i => i.LocalEntryId == linked.Id);
        linkedIdentity.ServerId.Should().Be(42);
        localIdentity.ServerId.Should().BeNull();

        viewModel.ShowLocationDetails(linkedIdentity);
        viewModel.SelectedLocation!.Notes.Should().Be("Linked notes");
        viewModel.SelectedLocation.CanEdit.Should().BeTrue();
        viewModel.ShowLocationDetails(localIdentity);
        var selected = viewModel.SelectedLocation!;
        selected.Notes.Should().Be("Imported notes");
        selected.IsReadOnly.Should().BeTrue();
        selected.ReadOnlyExplanation.Should().Be(TimelineEntryIdentity.ReadOnlyExplanation);
        viewModel.PrepareNotesEditorNavigation().Should().BeNull();

        viewModel.CoordinateEditor.EnterCoordinatePickingModeCommand.Execute(null);
        viewModel.CoordinateEditor.PendingLatitude = 1;
        viewModel.CoordinateEditor.PendingLongitude = 2;
        await viewModel.CoordinateEditor.SaveCoordinatesCommand.ExecuteAsync(null);
        viewModel.DateTimeEditor.OpenEditDateTimePickerCommand.Execute(null);
        await viewModel.DateTimeEditor.SaveEditDateTimeCommand.ExecuteAsync(null);
        viewModel.OpenActivityPickerCommand.Execute(null);
        await viewModel.SaveActivityCommand.ExecuteAsync(null);
        await viewModel.ClearActivityCommand.ExecuteAsync(null);
        await viewModel.SaveNotesAsync("Blocked notes");
        await viewModel.SaveEntryChangesAsync(new TimelineEntryUpdateEventArgs { Identity = localIdentity, Notes = "Blocked full edit" });
        await viewModel.DeleteLocationAsync(localIdentity);

        viewModel.CoordinateEditor.IsCoordinatePickingMode.Should().BeFalse();
        viewModel.DateTimeEditor.IsEditDateTimePickerOpen.Should().BeFalse();
        viewModel.IsActivityPickerOpen.Should().BeFalse();
        viewModel.SelectedLocation!.Identity.Should().Be(localIdentity);
        selected.Notes.Should().Be("Imported notes");
        selected.Latitude.Should().Be(localOnly.Latitude);
        viewModel.TotalCount.Should().Be(2);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.Notes.Should().Be("Linked notes");
        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        context.VerifyNoRemoteMutations();
        context.Toast.SuccessMessages.Should().BeEmpty();
        var exporter = new TimelineExportService(context.Repository, NullLogger<TimelineExportService>.Instance);
        (await exporter.ExportToCsvAsync()).Should().Contain("Imported notes");
        (await exporter.ExportToGeoJsonAsync()).Should().Contain("Imported notes");
    }

    [Fact]
    public async Task LinkedEditorCallbacksAndNotesNavigation_RetainIdentityThroughReload()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (_, linked) = await context.SeedCollisionAsync();
        using var viewModel = context.CreateViewModel(online: false);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        var identity = TimelineDataService.ToTimelineLocation(linked).Identity;
        viewModel.ShowLocationDetails(identity);
        viewModel.CoordinateEditor.EnterCoordinatePickingModeCommand.Execute(null);
        viewModel.CoordinateEditor.PendingLatitude = 1;
        viewModel.CoordinateEditor.PendingLongitude = 2;
        await viewModel.CoordinateEditor.SaveCoordinatesCommand.ExecuteAsync(null);
        viewModel.SelectedLocation!.Identity.Should().Be(identity);
        viewModel.SelectedLocation.Latitude.Should().Be(1);
        viewModel.DateTimeEditor.OpenEditDateTimePickerCommand.Execute(null);
        viewModel.DateTimeEditor.EditDateTime = DateTime.Today.AddHours(4);
        await viewModel.DateTimeEditor.SaveEditDateTimeCommand.ExecuteAsync(null);
        viewModel.SelectedLocation!.Identity.Should().Be(identity);
        viewModel.ActivityTypes.Add(new ActivityType { Id = 7, Name = "Cycle" });
        viewModel.OpenActivityPickerCommand.Execute(null);
        viewModel.SelectedActivityForEdit = viewModel.ActivityTypes.Single();
        await viewModel.SaveActivityCommand.ExecuteAsync(null);

        var navigation = viewModel.PrepareNotesEditorNavigation()!;
        navigation["timelineIdentity"].Should().Be(identity);
        using var notes = context.CreateNotesEditor();
        notes.ApplyQueryAttributes(navigation);
        notes.SetCurrentContent("<p>Linked editor notes</p>");
        await notes.SaveCommand.ExecuteAsync(null);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        viewModel.ShowLocationDetails(identity);
        viewModel.SelectedLocation!.Notes.Should().Be("<p>Linked editor notes</p>");
        await viewModel.SaveEntryChangesAsync(new TimelineEntryUpdateEventArgs
        {
            Identity = identity, Latitude = 3, Longitude = 4,
            LocalTimestamp = linked.Timestamp, Notes = "Full linked edit", ClearActivity = true
        });
        viewModel.SelectedLocation!.Identity.Should().Be(identity);
        viewModel.SelectedLocation.Notes.Should().Be("Full linked edit");
        var pending = (await context.Database.Table<PendingTimelineMutation>().ToListAsync()).Single();
        pending.LocationId.Should().Be(42);
        pending.LocalEntryId.Should().Be(linked.Id);
        pending.ClearActivity.Should().BeTrue();
        context.VerifyNoRemoteMutations();
    }

    [Fact]
    public async Task ReloadAfterGenuineLinkage_ClosesStaleLocalOnlySelectionAndEnablesTheSameRow()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: false);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        var queuedCapture = new QueuedLocation
        {
            Latitude = localOnly.Latitude, Longitude = localOnly.Longitude,
            Timestamp = localOnly.Timestamp, SyncStatus = SyncStatus.Pending
        };
        await context.Database.InsertAsync(queuedCapture);
        localOnly.QueuedLocationId = queuedCapture.Id;
        await context.Repository.UpdateLocalTimelineEntryAsync(localOnly);
        using var viewModel = context.CreateViewModel(online: false);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        var stale = TimelineDataService.ToTimelineLocation(localOnly).Identity;
        viewModel.ShowLocationDetails(stale);
        viewModel.SelectedLocation!.CanEdit.Should().BeFalse();
        viewModel.SetPendingLocationToReopen(stale);
        var queue = new LocationQueueRepository(() => Task.FromResult(context.Database),
            new WayfarerMobile.Tests.Infrastructure.Mocks.MockSettingsService());
        await queue.MarkServerConfirmedAsync(queuedCapture.Id, 99);
        await context.Repository.UpdateServerIdByQueuedLocationIdAsync(queuedCapture.Id, 99);
        await viewModel.OnAppearingAsync();
        viewModel.SelectedLocation.Should().BeNull();
        viewModel.IsLocationSheetOpen.Should().BeFalse();
        viewModel.ShowLocationDetails(stale);
        viewModel.SelectedLocation.Should().BeNull();

        var refreshed = viewModel.TimelineGroups.SelectMany(g => g).Single(i => i.Identity.LocalEntryId == 42);
        viewModel.ShowLocationDetails(refreshed.Identity);
        viewModel.SelectedLocation!.Identity.ServerId.Should().Be(99);
        viewModel.SelectedLocation.CanEdit.Should().BeTrue();
        (await context.Database.Table<QueuedLocation>().CountAsync()).Should().Be(1);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.ServerId.Should().Be(42);
        await viewModel.OnDisappearingAsync();
        context.VerifyNoRemoteMutations();
    }

    [Fact]
    public async Task ActualServerResponse_GrantsAuthorityWithoutRetargetingTheLocalOnlySelection()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        using var viewModel = context.CreateViewModel(online: true);
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        var selectedIdentity = TimelineDataService.ToTimelineLocation(localOnly).Identity;
        viewModel.ShowLocationDetails(selectedIdentity);
        var serverLocation = new TimelineLocation
        {
            Id = 77, Timestamp = localOnly.Timestamp, LocalTimestamp = localOnly.Timestamp.ToLocalTime(),
            Coordinates = new TimelineCoordinates { X = 20, Y = 35 }, Notes = "Server response"
        };
        serverLocation.Identity.CanMutate.Should().BeFalse();
        var collidingServerLocation = new TimelineLocation
        {
            Id = 42, Timestamp = linked.Timestamp, LocalTimestamp = linked.Timestamp.ToLocalTime(),
            Coordinates = new TimelineCoordinates { X = linked.Longitude, Y = linked.Latitude }, Notes = "Linked server response"
        };
        context.Api.Setup(x => x.GetTimelineLocationsAsync("day", It.IsAny<int>(), It.IsAny<int?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimelineResponse { Data = [serverLocation, collidingServerLocation], TotalItems = 2 });
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        // Await the existing per-date enrichment owner so the test does not leave database work running.
        await context.Data.EnrichFromServerAsync(viewModel.SelectedDate);

        viewModel.SelectedLocation!.Identity.Should().Be(selectedIdentity);
        viewModel.SelectedLocation.Notes.Should().Be("Imported notes");
        viewModel.TimelineGroups.SelectMany(g => g).Should().Contain(i => i.Identity == selectedIdentity);
        serverLocation.Identity.Should().Be(TimelineEntryIdentity.FromServer(77));
        viewModel.ShowLocationDetails(serverLocation.Identity);
        viewModel.SelectedLocation!.CanEdit.Should().BeTrue();
        viewModel.SelectedLocation.Notes.Should().Be("Server response");
        collidingServerLocation.Identity.Should().Be(TimelineEntryIdentity.FromLocal(linked.Id, 42, true));
        context.VerifyNoRemoteMutations();
        viewModel.CoordinateEditor.EnterCoordinatePickingModeCommand.Execute(null);
        viewModel.CoordinateEditor.PendingLatitude = 1;
        viewModel.CoordinateEditor.PendingLongitude = 2;

        // Notes navigation starts with API provenance; enrichment has since created its local copy.
        var navigation = viewModel.PrepareNotesEditorNavigation()!;
        using var notes = context.CreateNotesEditor();
        notes.ApplyQueryAttributes(navigation);
        notes.SetCurrentContent("Edited server-origin notes");
        await notes.SaveCommand.ExecuteAsync(null);
        viewModel.IsOnline = false;
        await viewModel.LoadDataCommand.ExecuteAsync(null);
        viewModel.ShowLocationDetails((TimelineEntryIdentity)navigation["timelineIdentity"]);
        viewModel.SelectedLocation!.Identity.ServerId.Should().Be(77);
        viewModel.SelectedLocation.Identity.LocalEntryId.Should().NotBeNull();
        viewModel.SelectedLocation.Notes.Should().Be("Edited server-origin notes");
        await viewModel.CoordinateEditor.SaveCoordinatesCommand.ExecuteAsync(null);
        viewModel.SelectedLocation!.Identity.ServerId.Should().Be(77);
        viewModel.SelectedLocation.Latitude.Should().Be(1);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.Notes.Should().Be("Linked notes");
        context.Api.Verify(x => x.UpdateTimelineLocationAsync(77, It.IsAny<TimelineLocationUpdateRequest>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NotesEditor_LocalOnlyOrRawNumericNavigation_CannotSaveOrReuseOldAuthority()
    {
        await using var context = await TimelineMutationContext.CreateAsync(online: true);
        var (localOnly, linked) = await context.SeedCollisionAsync();
        using var notes = context.CreateNotesEditor();
        notes.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["timelineIdentity"] = TimelineDataService.ToTimelineLocation(localOnly).Identity,
            ["notes"] = "Imported notes"
        });
        notes.SetCurrentContent("Blocked notes");
        await notes.SaveCommand.ExecuteAsync(null);
        notes.HasChanges.Should().BeTrue();
        // Reusing a ViewModel with an old numeric link must discard any earlier proven identity.
        notes.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["timelineIdentity"] = TimelineDataService.ToTimelineLocation(linked).Identity
        });
        notes.ApplyQueryAttributes(new Dictionary<string, object> { ["locationId"] = 42 });
        await notes.SaveCommand.ExecuteAsync(null);

        context.VerifyNoRemoteMutations();
        (await context.Service.GetPendingCountAsync()).Should().Be(0);
        (await context.Repository.GetLocalTimelineEntryAsync(linked.Id))!.Notes.Should().Be("Linked notes");
        context.Toast.SuccessMessages.Should().BeEmpty();
        context.Toast.WarningMessages.Should().OnlyContain(m => m == TimelineEntryIdentity.ReadOnlyExplanation);
    }
}
