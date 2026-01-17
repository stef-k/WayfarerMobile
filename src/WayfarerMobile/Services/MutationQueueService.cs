using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing the offline mutation queue.
/// Handles queuing, retrieval, and lifecycle of pending sync mutations.
/// Extracted from TripSyncService for better separation of concerns.
/// </summary>
public class MutationQueueService : IMutationQueueService
{
    private readonly DatabaseService _databaseService;
    private readonly ITripRepository _tripRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly ISegmentRepository _segmentRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly ILogger<MutationQueueService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="MutationQueueService"/> class.
    /// </summary>
    public MutationQueueService(
        DatabaseService databaseService,
        ITripRepository tripRepository,
        IPlaceRepository placeRepository,
        ISegmentRepository segmentRepository,
        IAreaRepository areaRepository,
        ILogger<MutationQueueService> logger)
    {
        _databaseService = databaseService;
        _tripRepository = tripRepository;
        _placeRepository = placeRepository;
        _segmentRepository = segmentRepository;
        _areaRepository = areaRepository;
        _logger = logger;
    }

    #region Initialization

    /// <summary>
    /// Gets the database connection, shared with TripSyncService for transactional consistency.
    /// </summary>
    internal SQLiteAsyncConnection? Database => _database;

    /// <summary>
    /// Sets the database connection from TripSyncService for shared access.
    /// </summary>
    internal void SetDatabase(SQLiteAsyncConnection database)
    {
        _database = database;
        _initialized = true;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "tripsync.db");
            _database = new SQLiteAsyncConnection(dbPath);
            await _database.CreateTableAsync<PendingTripMutation>();
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    #endregion

    #region Queue Status

    /// <inheritdoc/>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .CountAsync();
    }

    /// <inheritdoc/>
    public async Task<int> GetFailedCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => m.IsRejected || m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .CountAsync();
    }

    /// <inheritdoc/>
    public async Task<int> GetFailedCountForTripAsync(Guid tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId && (m.IsRejected || m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts))
            .CountAsync();
    }

    /// <inheritdoc/>
    public async Task<List<PendingTripMutation>> GetFailedMutationsForTripAsync(Guid tripId)
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId && (m.IsRejected || m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts))
            .ToListAsync();
    }

    #endregion

    #region Queue Management

    /// <inheritdoc/>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        var count = await _database!.Table<PendingTripMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();
        _logger.LogDebug("Cleared {Count} rejected mutations", count);
    }

    /// <inheritdoc/>
    public async Task ResetFailedMutationsAsync()
    {
        await EnsureInitializedAsync();
        var failed = await _database!.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        foreach (var mutation in failed)
        {
            mutation.SyncAttempts = 0;
            await _database.UpdateAsync(mutation);
        }

        _logger.LogDebug("Reset {Count} failed mutations for retry", failed.Count);
    }

    /// <inheritdoc/>
    public async Task ResetFailedMutationsForTripAsync(Guid tripId)
    {
        await EnsureInitializedAsync();
        var failed = await _database!.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId && !m.IsRejected && m.SyncAttempts >= PendingTripMutation.MaxSyncAttempts)
            .ToListAsync();

        foreach (var mutation in failed)
        {
            mutation.SyncAttempts = 0;
            await _database.UpdateAsync(mutation);
        }

        _logger.LogDebug("Reset {Count} failed mutations for trip {TripId}", failed.Count, tripId);
    }

    /// <inheritdoc/>
    public async Task CancelPendingMutationsAsync()
    {
        await EnsureInitializedAsync();

        // Get all pending mutations and capture their IDs
        var mutations = await _database!.Table<PendingTripMutation>().ToListAsync();
        var mutationIds = mutations.Select(m => m.Id).ToList();

        if (mutationIds.Count == 0)
            return;

        // Restore original values for each mutation before deleting
        foreach (var mutation in mutations)
        {
            await RestoreOriginalValuesAsync(mutation);
        }

        // Delete only the mutations we queried (prevents race condition with newly added mutations)
        foreach (var id in mutationIds)
        {
            await _database.DeleteAsync<PendingTripMutation>(id);
        }

        _logger.LogInformation("Cancelled {Count} pending mutations", mutationIds.Count);
    }

    /// <inheritdoc/>
    public async Task CancelPendingMutationsForTripAsync(Guid tripId)
    {
        await EnsureInitializedAsync();

        // Get all pending mutations for this trip and capture their IDs
        var mutations = await _database!.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId)
            .ToListAsync();
        var mutationIds = mutations.Select(m => m.Id).ToList();

        if (mutationIds.Count == 0)
            return;

        // Restore original values for each mutation
        foreach (var mutation in mutations)
        {
            await RestoreOriginalValuesAsync(mutation);
        }

        // Delete only the mutations we queried (prevents race condition with newly added mutations)
        foreach (var id in mutationIds)
        {
            await _database.DeleteAsync<PendingTripMutation>(id);
        }

        _logger.LogDebug("Cancelled {Count} pending mutations for trip {TripId}", mutationIds.Count, tripId);
    }

    /// <inheritdoc/>
    public async Task ClearPendingMutationsForTripAsync(Guid tripId)
    {
        await EnsureInitializedAsync();
        var count = await _database!.Table<PendingTripMutation>()
            .Where(m => m.TripId == tripId)
            .DeleteAsync();
        _logger.LogDebug("Cleared {Count} pending mutations for trip {TripId}", count, tripId);
    }

    #endregion

    #region Queue Retrieval

    /// <inheritdoc/>
    public async Task<List<PendingTripMutation>> GetPendingMutationsAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTripMutation.MaxSyncAttempts)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<List<PendingTripMutation>> GetAllMutationsAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    #endregion

    #region Mutation Lifecycle

    /// <inheritdoc/>
    public async Task MarkMutationRejectedAsync(int mutationId, string reason)
    {
        await EnsureInitializedAsync();
        var mutation = await _database!.Table<PendingTripMutation>()
            .Where(m => m.Id == mutationId)
            .FirstOrDefaultAsync();

        if (mutation != null)
        {
            mutation.IsRejected = true;
            mutation.RejectionReason = reason;
            await _database.UpdateAsync(mutation);
            _logger.LogWarning("Mutation {Id} rejected: {Reason}", mutationId, reason);
        }
    }

    /// <inheritdoc/>
    public async Task IncrementSyncAttemptAsync(int mutationId, string? errorMessage = null)
    {
        await EnsureInitializedAsync();
        var mutation = await _database!.Table<PendingTripMutation>()
            .Where(m => m.Id == mutationId)
            .FirstOrDefaultAsync();

        if (mutation != null)
        {
            mutation.SyncAttempts++;
            mutation.LastSyncAttempt = DateTime.UtcNow;
            mutation.LastError = errorMessage;
            await _database.UpdateAsync(mutation);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteMutationAsync(int mutationId)
    {
        await EnsureInitializedAsync();
        await _database!.DeleteAsync<PendingTripMutation>(mutationId);
    }

    #endregion

    #region Restoration

    /// <inheritdoc/>
    public async Task RestoreOriginalValuesAsync(PendingTripMutation mutation)
    {
        switch (mutation.EntityType)
        {
            case "Place":
                await RestorePlaceAsync(mutation);
                break;

            case "Region":
                await RestoreRegionAsync(mutation);
                break;

            case "Segment":
                await RestoreSegmentAsync(mutation);
                break;

            case "Area":
                await RestoreAreaAsync(mutation);
                break;

            case "Trip":
                await RestoreTripAsync(mutation);
                break;
        }
    }

    private async Task RestorePlaceAsync(PendingTripMutation mutation)
    {
        if (mutation.OperationType == "Create")
        {
            // Delete offline-created place (it was never synced to server)
            await _placeRepository.DeleteOfflinePlaceByServerIdAsync(mutation.EntityId);
        }
        else if (mutation.OperationType == "Delete" && mutation.OriginalName != null)
        {
            // Restore deleted place
            var downloadedTrip = await _tripRepository.GetDownloadedTripByServerIdAsync(mutation.TripId);
            var place = new OfflinePlaceEntity
            {
                ServerId = mutation.EntityId,
                TripId = downloadedTrip?.Id ?? 0,
                RegionId = mutation.RegionId,
                Name = mutation.OriginalName,
                Latitude = mutation.OriginalLatitude ?? 0,
                Longitude = mutation.OriginalLongitude ?? 0,
                Notes = mutation.OriginalNotes,
                IconName = mutation.OriginalIconName,
                MarkerColor = mutation.OriginalMarkerColor,
                SortOrder = mutation.OriginalDisplayOrder ?? 0
            };
            await _placeRepository.InsertOfflinePlaceAsync(place);
        }
        else if (mutation.OperationType == "Update")
        {
            var existingPlace = await _placeRepository.GetOfflinePlaceByServerIdAsync(mutation.EntityId);
            if (existingPlace != null)
            {
                if (mutation.OriginalName != null) existingPlace.Name = mutation.OriginalName;
                if (mutation.OriginalLatitude.HasValue) existingPlace.Latitude = mutation.OriginalLatitude.Value;
                if (mutation.OriginalLongitude.HasValue) existingPlace.Longitude = mutation.OriginalLongitude.Value;
                if (mutation.IncludeNotes) existingPlace.Notes = mutation.OriginalNotes;
                if (mutation.OriginalIconName != null) existingPlace.IconName = mutation.OriginalIconName;
                if (mutation.OriginalMarkerColor != null) existingPlace.MarkerColor = mutation.OriginalMarkerColor;
                if (mutation.OriginalDisplayOrder.HasValue) existingPlace.SortOrder = mutation.OriginalDisplayOrder.Value;
                if (mutation.OriginalRegionId.HasValue) existingPlace.RegionId = mutation.OriginalRegionId.Value;
                await _placeRepository.UpdateOfflinePlaceAsync(existingPlace);
            }
        }
    }

    private async Task RestoreRegionAsync(PendingTripMutation mutation)
    {
        if (mutation.OperationType == "Create")
        {
            // Delete offline-created region
            await _areaRepository.DeleteOfflineAreaByServerIdAsync(mutation.EntityId);
        }
        else if (mutation.OperationType == "Delete" && mutation.OriginalName != null)
        {
            // Restore deleted region
            var downloadedTrip = await _tripRepository.GetDownloadedTripByServerIdAsync(mutation.TripId);
            var area = new OfflineAreaEntity
            {
                ServerId = mutation.EntityId,
                TripId = downloadedTrip?.Id ?? 0,
                Name = mutation.OriginalName,
                Notes = mutation.OriginalNotes,
                CenterLatitude = mutation.OriginalCenterLatitude,
                CenterLongitude = mutation.OriginalCenterLongitude,
                SortOrder = mutation.OriginalDisplayOrder ?? 0
            };
            await _areaRepository.InsertOfflineAreaAsync(area);
        }
        else if (mutation.OperationType == "Update")
        {
            var existingArea = await _areaRepository.GetOfflineAreaByServerIdAsync(mutation.EntityId);
            if (existingArea != null)
            {
                if (mutation.OriginalName != null) existingArea.Name = mutation.OriginalName;
                if (mutation.IncludeNotes) existingArea.Notes = mutation.OriginalNotes;
                if (mutation.OriginalCenterLatitude.HasValue) existingArea.CenterLatitude = mutation.OriginalCenterLatitude;
                if (mutation.OriginalCenterLongitude.HasValue) existingArea.CenterLongitude = mutation.OriginalCenterLongitude;
                if (mutation.OriginalDisplayOrder.HasValue) existingArea.SortOrder = mutation.OriginalDisplayOrder.Value;
                await _areaRepository.UpdateOfflineAreaAsync(existingArea);
            }
        }
    }

    private async Task RestoreSegmentAsync(PendingTripMutation mutation)
    {
        if (mutation.OperationType == "Update")
        {
            var segment = await _segmentRepository.GetOfflineSegmentByServerIdAsync(mutation.EntityId);
            if (segment != null)
            {
                segment.Notes = mutation.OriginalNotes;
                await _segmentRepository.UpdateOfflineSegmentAsync(segment);
            }
        }
    }

    private async Task RestoreAreaAsync(PendingTripMutation mutation)
    {
        if (mutation.OperationType == "Update")
        {
            var polygon = await _areaRepository.GetOfflinePolygonByServerIdAsync(mutation.EntityId);
            if (polygon != null)
            {
                polygon.Notes = mutation.OriginalNotes;
                await _areaRepository.UpdateOfflinePolygonAsync(polygon);
            }
        }
    }

    private async Task RestoreTripAsync(PendingTripMutation mutation)
    {
        if (mutation.OperationType == "Update")
        {
            var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(mutation.EntityId);
            if (trip != null)
            {
                if (mutation.OriginalName != null) trip.Name = mutation.OriginalName;
                if (mutation.IncludeNotes) trip.Notes = mutation.OriginalNotes;
                await _tripRepository.SaveDownloadedTripAsync(trip);
            }
        }
    }

    #endregion
}
