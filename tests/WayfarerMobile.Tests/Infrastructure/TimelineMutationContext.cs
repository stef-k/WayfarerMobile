using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Infrastructure;

/// <summary>Composes production Timeline services with isolated SQLite storage and mocked external boundaries.</summary>
internal sealed class TimelineMutationContext : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timeline-authority-" + Guid.NewGuid().ToString("N"));
    private DatabaseService _databaseService = new();
    private readonly Mock<IConnectivity> _connectivity = new();
    public Mock<IApiClient> Api { get; } = new();
    public MockToastService Toast { get; } = new();
    public SQLiteAsyncConnection Database { get; private set; } = null!;
    public TimelineRepository Repository { get; private set; } = null!;
    public TimelineSyncService Service { get; private set; } = null!;
    public TimelineDataService Data { get; private set; } = null!;
    public DatabaseService DatabaseService => _databaseService;

    public static async Task<TimelineMutationContext> CreateAsync(bool online, bool initializeQueue = true)
    {
        var context = new TimelineMutationContext();
        Directory.CreateDirectory(context._root);
        context.Api.SetupGet(x => x.IsConfigured).Returns(true);
        context.Api.Setup(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<TimelineLocationUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimelineUpdateResponse { Success = true });
        context.Api.Setup(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await context.OpenAsync(online);
        if (initializeQueue)
            await context.Service.GetPendingCountAsync();
        return context;
    }

    /// <summary>Reopens the on-disk database with a fresh production service, then enables explicit replay.</summary>
    public async Task RestartOnlineAsync()
    {
        Service.Dispose();
        await _databaseService.DisposeAsync();
        _databaseService = new DatabaseService();
        await OpenAsync(online: true);
        await Service.StartAsync();
    }

    private async Task OpenAsync(bool online)
    {
        FileSystem.DatabaseTestRoot.Value = _root;
        Database = await _databaseService.GetConnectionAsync();
        Repository = new TimelineRepository(_databaseService.GetConnectionAsync);
        _connectivity.SetupGet(x => x.NetworkAccess).Returns(online ? NetworkAccess.Internet : NetworkAccess.None);
        Service = new TimelineSyncService(Api.Object, Repository, _databaseService,
            _connectivity.Object, NullLogger<TimelineSyncService>.Instance);
        Data = new TimelineDataService(Repository, Api.Object, NullLogger<TimelineDataService>.Instance);
    }

    public async Task<(LocalTimelineEntry localOnly, LocalTimelineEntry linked)> SeedCollisionAsync()
    {
        // Imports allocate SQLite IDs independently of server IDs. Make the next allocation collide with server 42.
        await Database.ExecuteAsync("INSERT INTO sqlite_sequence (name, seq) VALUES ('LocalTimelineEntries', 41)");
        var timestamp = DateTime.Today.ToUniversalTime().AddHours(1);
        var localOnly = new LocalTimelineEntry
        {
            Id = 42, ServerId = null, Latitude = 37.98, Longitude = 23.72,
            Timestamp = timestamp, Notes = "Imported notes", ActivityType = "Walk"
        };
        var linked = new LocalTimelineEntry
        {
            Id = 43, ServerId = 42, Latitude = 38, Longitude = 24,
            Timestamp = timestamp.AddHours(1), Notes = "Linked notes", ActivityType = "Run"
        };
        await Repository.InsertLocalTimelineEntryAsync(localOnly);
        await Repository.InsertLocalTimelineEntryAsync(linked);
        return (localOnly, linked);
    }

    public TimelineViewModel CreateViewModel(bool online)
    {
        var settings = new MockSettingsService();
        var layer = new TimelineLayerService(NullLogger<TimelineLayerService>.Instance);
        var manager = new TimelineEntryManager(Service, Toast, NullLogger<TimelineEntryManager>.Instance);
        var activities = new Mock<IActivitySyncService>();
        activities.Setup(x => x.GetActivityTypesAsync()).ReturnsAsync([]);
        return new TimelineViewModel(Api.Object, _databaseService, Service, Toast, settings,
            Mock.Of<IMapBuilder>(), layer, Data, manager, activities.Object,
            callbacks => new CoordinateEditorViewModel(callbacks, Service, Toast, NullLogger<CoordinateEditorViewModel>.Instance),
            callbacks => new DateTimeEditorViewModel(callbacks, Service, Toast, NullLogger<DateTimeEditorViewModel>.Instance),
            NullLogger<TimelineViewModel>.Instance) { IsOnline = online };
    }

    public NotesEditorViewModel CreateNotesEditor() => new(Service, Mock.Of<ITripSyncService>(),
        Mock.Of<ITripEditingService>(), Mock.Of<ITripStateManager>(), Toast, new MockSettingsService());

    public void VerifyNoRemoteMutations()
    {
        Api.Verify(x => x.UpdateTimelineLocationAsync(It.IsAny<int>(),
            It.IsAny<TimelineLocationUpdateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Api.Verify(x => x.DeleteTimelineLocationAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public async ValueTask DisposeAsync()
    {
        Service.Dispose();
        await _databaseService.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
