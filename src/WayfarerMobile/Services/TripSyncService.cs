using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for synchronizing trip data changes with the server.
/// Implements optimistic UI updates with offline queue for resilience.
///
/// Sync Strategy:
/// 1. Apply optimistic UI update immediately (caller responsibility)
/// 2. Save to local database
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - revert changes, notify caller
/// 5. On 5xx/network error: Queue for retry when online
/// </summary>
public class TripSyncService : ITripSyncService
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly IMutationQueueService _mutationQueue;
    private readonly IPlaceOperationsHandler _placeOps;
    private readonly IRegionOperationsHandler _regionOps;
    private readonly ITripEntityOperationsHandler _entityOps;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    /// <summary>
    /// Event raised when a sync operation fails with server rejection (4xx).
    /// Caller should revert optimistic UI updates.
    /// </summary>
    public event EventHandler<SyncFailureEventArgs>? SyncRejected;

    /// <summary>
    /// Event raised when a sync is queued for offline retry.
    /// </summary>
    public event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <summary>
    /// Event raised when a sync completes successfully.
    /// </summary>
    public event EventHandler<SyncSuccessEventArgs>? SyncCompleted;

    /// <summary>
    /// Event raised when a create operation completes and server ID is assigned.
    /// </summary>
    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;

    /// <summary>
    /// Creates a new instance of TripSyncService.
    /// </summary>
    public TripSyncService(
        IApiClient apiClient,
        DatabaseService databaseService,
        IMutationQueueService mutationQueue,
        IPlaceOperationsHandler placeOps,
        IRegionOperationsHandler regionOps,
        ITripEntityOperationsHandler entityOps)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _mutationQueue = mutationQueue;
        _placeOps = placeOps;
        _regionOps = regionOps;
        _entityOps = entityOps;
    }

    /// <summary>
    /// Ensures the database connection is initialized.
    /// Thread-safe initialization using double-check locking pattern.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_initialized) return;

            _database = await _databaseService.GetConnectionAsync();
            await _database.CreateTableAsync<PendingTripMutation>();

            // Share database connection with MutationQueueService for transactional consistency
            if (_mutationQueue is MutationQueueService mqs)
            {
                mqs.SetDatabase(_database);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets whether the device is currently connected to the internet.
    /// </summary>
    private static bool IsConnected =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    #region Place Operations

    /// <summary>
    /// Creates a new place with optimistic UI pattern.
    /// Delegates to PlaceOperationsHandler and raises events based on result.
    /// </summary>
    public async Task<Guid> CreatePlaceAsync(
        Guid tripId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes = null,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null)
    {
        var result = await _placeOps.CreatePlaceAsync(tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);

        RaisePlaceEvents(result, isCreate: true);

        return result.EntityId ?? Guid.Empty;
    }

    /// <summary>
    /// Updates a place with optimistic UI pattern.
    /// Delegates to PlaceOperationsHandler and raises events based on result.
    /// </summary>
    public async Task UpdatePlaceAsync(
        Guid placeId,
        Guid tripId,
        string? name = null,
        double? latitude = null,
        double? longitude = null,
        string? notes = null,
        bool includeNotes = false,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null)
    {
        var result = await _placeOps.UpdatePlaceAsync(placeId, tripId, name, latitude, longitude, notes, includeNotes, iconName, markerColor, displayOrder);

        RaisePlaceEvents(result, isCreate: false);
    }

    /// <summary>
    /// Deletes a place with optimistic UI pattern.
    /// Delegates to PlaceOperationsHandler and raises events based on result.
    /// </summary>
    public async Task DeletePlaceAsync(Guid placeId, Guid tripId)
    {
        var result = await _placeOps.DeletePlaceAsync(placeId, tripId);

        RaisePlaceEvents(result, isCreate: false);
    }

    /// <summary>
    /// Raises appropriate events based on place operation result.
    /// </summary>
    private void RaisePlaceEvents(PlaceOperationResult result, bool isCreate)
    {
        switch (result.ResultType)
        {
            case SyncResultType.Completed:
                if (isCreate && result.EntityId.HasValue)
                {
                    // For creates, the EntityId is the server ID
                    EntityCreated?.Invoke(this, new EntityCreatedEventArgs
                    {
                        TempClientId = Guid.Empty, // Handler doesn't track temp ID
                        ServerId = result.EntityId.Value,
                        EntityType = "Place"
                    });
                }
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = result.EntityId ?? Guid.Empty });
                break;

            case SyncResultType.Queued:
                SyncQueued?.Invoke(this, new SyncQueuedEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    Message = result.Message ?? "Queued for later sync"
                });
                break;

            case SyncResultType.Rejected:
                SyncRejected?.Invoke(this, new SyncFailureEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    ErrorMessage = result.Message ?? "Server rejected",
                    IsClientError = true
                });
                break;
        }
    }

    #endregion

    #region Region Operations

    /// <summary>
    /// Creates a new region with optimistic UI pattern.
    /// Delegates to RegionOperationsHandler and raises events based on result.
    /// </summary>
    public async Task<Guid> CreateRegionAsync(
        Guid tripId,
        string name,
        string? notes = null,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null)
    {
        var result = await _regionOps.CreateRegionAsync(tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder);

        RaiseRegionEvents(result, isCreate: true);

        return result.EntityId ?? Guid.Empty;
    }

    /// <summary>
    /// Updates a region with optimistic UI pattern.
    /// Delegates to RegionOperationsHandler and raises events based on result.
    /// </summary>
    public async Task UpdateRegionAsync(
        Guid regionId,
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null)
    {
        var result = await _regionOps.UpdateRegionAsync(regionId, tripId, name, notes, includeNotes, coverImageUrl, centerLatitude, centerLongitude, displayOrder);

        RaiseRegionEvents(result, isCreate: false);
    }

    /// <summary>
    /// Deletes a region with optimistic UI pattern.
    /// Delegates to RegionOperationsHandler and raises events based on result.
    /// </summary>
    public async Task DeleteRegionAsync(Guid regionId, Guid tripId)
    {
        var result = await _regionOps.DeleteRegionAsync(regionId, tripId);

        RaiseRegionEvents(result, isCreate: false);
    }

    /// <summary>
    /// Raises appropriate events based on region operation result.
    /// </summary>
    private void RaiseRegionEvents(RegionOperationResult result, bool isCreate)
    {
        switch (result.ResultType)
        {
            case SyncResultType.Completed:
                if (isCreate && result.EntityId.HasValue)
                {
                    // For creates, the EntityId is the server ID
                    EntityCreated?.Invoke(this, new EntityCreatedEventArgs
                    {
                        TempClientId = Guid.Empty, // Handler doesn't track temp ID
                        ServerId = result.EntityId.Value,
                        EntityType = "Region"
                    });
                }
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = result.EntityId ?? Guid.Empty });
                break;

            case SyncResultType.Queued:
                SyncQueued?.Invoke(this, new SyncQueuedEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    Message = result.Message ?? "Queued for later sync"
                });
                break;

            case SyncResultType.Rejected:
                SyncRejected?.Invoke(this, new SyncFailureEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    ErrorMessage = result.Message ?? "Server rejected",
                    IsClientError = true
                });
                break;
        }
    }

    private void RaiseEntityEvents(EntityOperationResult result)
    {
        switch (result.ResultType)
        {
            case SyncResultType.Completed:
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = result.EntityId ?? Guid.Empty });
                break;

            case SyncResultType.Queued:
                SyncQueued?.Invoke(this, new SyncQueuedEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    Message = result.Message ?? "Queued for later sync"
                });
                break;

            case SyncResultType.Rejected:
                SyncRejected?.Invoke(this, new SyncFailureEventArgs
                {
                    EntityId = result.EntityId ?? Guid.Empty,
                    ErrorMessage = result.Message ?? "Server rejected",
                    IsClientError = true
                });
                break;
        }
    }

    #endregion

    #region Trip Operations

    /// <summary>
    /// Updates a trip's metadata with optimistic UI pattern.
    /// Delegates to ITripEntityOperationsHandler and raises events based on result.
    /// </summary>
    public async Task UpdateTripAsync(
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false)
    {
        var result = await _entityOps.UpdateTripAsync(tripId, name, notes, includeNotes);
        RaiseEntityEvents(result);
    }

    #endregion

    #region Segment Operations

    /// <summary>
    /// Updates a segment's notes with optimistic UI pattern.
    /// Delegates to ITripEntityOperationsHandler and raises events based on result.
    /// </summary>
    public async Task UpdateSegmentNotesAsync(
        Guid segmentId,
        Guid tripId,
        string? notes)
    {
        var result = await _entityOps.UpdateSegmentNotesAsync(segmentId, tripId, notes);
        RaiseEntityEvents(result);
    }

    #endregion

    #region Area Operations

    /// <summary>
    /// Updates an area's (polygon) notes with optimistic UI pattern.
    /// Delegates to ITripEntityOperationsHandler and raises events based on result.
    /// </summary>
    public async Task UpdateAreaNotesAsync(
        Guid tripId,
        Guid areaId,
        string? notes)
    {
        var result = await _entityOps.UpdateAreaNotesAsync(tripId, areaId, notes);
        RaiseEntityEvents(result);
    }

    #endregion

    #region Process Pending Mutations

    /// <summary>
    /// Process pending mutations (call when connectivity is restored).
    /// </summary>
    public async Task ProcessPendingMutationsAsync()
    {
        await EnsureInitializedAsync();

        if (!IsConnected) return;

        var pending = await _mutationQueue.GetPendingMutationsAsync();

        foreach (var mutation in pending)
        {
            try
            {
                var success = await ProcessMutationAsync(mutation);

                if (success)
                {
                    await _mutationQueue.DeleteMutationAsync(mutation.Id);
                    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = mutation.EntityId });
                }
                else
                {
                    await _mutationQueue.IncrementSyncAttemptAsync(mutation.Id, "No response from server");
                }
            }
            catch (HttpRequestException ex) when (IsClientError(ex))
            {
                // 4xx error - server permanently rejected this mutation
                // Roll back optimistic changes since they can never be synced
                await _mutationQueue.RestoreOriginalValuesAsync(mutation);
                await _mutationQueue.MarkMutationRejectedAsync(mutation.Id, $"Server: {ex.Message}");
                SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = mutation.EntityId, ErrorMessage = ex.Message, IsClientError = true });
            }
            catch (HttpRequestException ex)
            {
                // Network error - will retry
                await _mutationQueue.IncrementSyncAttemptAsync(mutation.Id, $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Timeout - will retry
                await _mutationQueue.IncrementSyncAttemptAsync(mutation.Id, "Request timed out");
            }
            catch (Exception ex)
            {
                // Unexpected error - will retry
                await _mutationQueue.IncrementSyncAttemptAsync(mutation.Id, $"Unexpected error: {ex.Message}");
            }
        }
    }

    private async Task<bool> ProcessMutationAsync(PendingTripMutation mutation)
    {
        return (mutation.EntityType, mutation.OperationType) switch
        {
            ("Place", "Create") => await ProcessPlaceCreateAsync(mutation),
            ("Place", "Update") => await ProcessPlaceUpdateAsync(mutation),
            ("Place", "Delete") => await _apiClient.DeletePlaceAsync(mutation.EntityId),
            ("Region", "Create") => await ProcessRegionCreateAsync(mutation),
            ("Region", "Update") => await ProcessRegionUpdateAsync(mutation),
            ("Region", "Delete") => await _apiClient.DeleteRegionAsync(mutation.EntityId),
            ("Trip", "Update") => await ProcessTripUpdateAsync(mutation),
            ("Segment", "Update") => await ProcessSegmentUpdateAsync(mutation),
            ("Area", "Update") => await ProcessAreaUpdateAsync(mutation),
            _ => false
        };
    }

    private async Task<bool> ProcessPlaceCreateAsync(PendingTripMutation mutation)
    {
        var request = new PlaceCreateRequest
        {
            RegionId = mutation.RegionId,
            Name = mutation.Name ?? "Unnamed Place",
            Latitude = mutation.Latitude ?? 0,
            Longitude = mutation.Longitude ?? 0,
            Notes = mutation.IncludeNotes ? mutation.Notes : null,
            IconName = mutation.IconName,
            MarkerColor = mutation.MarkerColor,
            DisplayOrder = mutation.DisplayOrder
        };

        var response = await _apiClient.CreatePlaceAsync(mutation.TripId, request);

        if (response?.Success == true && response.Id != Guid.Empty)
        {
            EntityCreated?.Invoke(this, new EntityCreatedEventArgs
            {
                TempClientId = mutation.TempClientId ?? mutation.EntityId,
                ServerId = response.Id,
                EntityType = "Place"
            });
            return true;
        }

        return false;
    }

    private async Task<bool> ProcessPlaceUpdateAsync(PendingTripMutation mutation)
    {
        var request = new PlaceUpdateRequest
        {
            Name = mutation.Name,
            Latitude = mutation.Latitude,
            Longitude = mutation.Longitude,
            Notes = mutation.IncludeNotes ? mutation.Notes : null,
            IconName = mutation.IconName,
            MarkerColor = mutation.MarkerColor,
            DisplayOrder = mutation.DisplayOrder
        };

        var response = await _apiClient.UpdatePlaceAsync(mutation.EntityId, request);
        return response != null;
    }

    private async Task<bool> ProcessRegionCreateAsync(PendingTripMutation mutation)
    {
        var request = new RegionCreateRequest
        {
            Name = mutation.Name ?? "Unnamed Region",
            Notes = mutation.IncludeNotes ? mutation.Notes : null,
            CoverImageUrl = mutation.CoverImageUrl,
            CenterLatitude = mutation.CenterLatitude,
            CenterLongitude = mutation.CenterLongitude,
            DisplayOrder = mutation.DisplayOrder
        };

        var response = await _apiClient.CreateRegionAsync(mutation.TripId, request);

        if (response?.Success == true && response.Id != Guid.Empty)
        {
            EntityCreated?.Invoke(this, new EntityCreatedEventArgs
            {
                TempClientId = mutation.TempClientId ?? mutation.EntityId,
                ServerId = response.Id,
                EntityType = "Region"
            });
            return true;
        }

        return false;
    }

    private async Task<bool> ProcessRegionUpdateAsync(PendingTripMutation mutation)
    {
        var request = new RegionUpdateRequest
        {
            Name = mutation.Name,
            Notes = mutation.IncludeNotes ? mutation.Notes : null,
            CoverImageUrl = mutation.CoverImageUrl,
            CenterLatitude = mutation.CenterLatitude,
            CenterLongitude = mutation.CenterLongitude,
            DisplayOrder = mutation.DisplayOrder
        };

        var response = await _apiClient.UpdateRegionAsync(mutation.EntityId, request);
        return response != null;
    }

    private async Task<bool> ProcessTripUpdateAsync(PendingTripMutation mutation)
    {
        var request = new TripUpdateRequest
        {
            Name = mutation.Name,
            Notes = mutation.IncludeNotes ? mutation.Notes : null
        };

        var response = await _apiClient.UpdateTripAsync(mutation.EntityId, request);
        return response?.Success == true;
    }

    private async Task<bool> ProcessSegmentUpdateAsync(PendingTripMutation mutation)
    {
        var request = new SegmentNotesUpdateRequest
        {
            Notes = mutation.Notes
        };

        var response = await _apiClient.UpdateSegmentNotesAsync(mutation.EntityId, request);
        return response?.Success == true;
    }

    private async Task<bool> ProcessAreaUpdateAsync(PendingTripMutation mutation)
    {
        var request = new AreaNotesUpdateRequest
        {
            Notes = mutation.Notes
        };

        var response = await _apiClient.UpdateAreaNotesAsync(mutation.EntityId, request);
        return response?.Success == true;
    }

    #endregion

    #region Utility Methods (delegated to MutationQueueService)

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    public Task<int> GetPendingCountAsync()
        => _mutationQueue.GetPendingCountAsync();

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public Task ClearRejectedMutationsAsync()
        => _mutationQueue.ClearRejectedMutationsAsync();

    /// <summary>
    /// Get count of failed mutations (exhausted retries or rejected).
    /// </summary>
    public Task<int> GetFailedCountAsync()
        => _mutationQueue.GetFailedCountAsync();

    /// <summary>
    /// Reset retry attempts for all failed mutations.
    /// </summary>
    public async Task ResetFailedMutationsAsync()
    {
        await _mutationQueue.ResetFailedMutationsAsync();

        // Try to process immediately if online
        if (IsConnected)
        {
            await ProcessPendingMutationsAsync();
        }
    }

    /// <summary>
    /// Cancel all pending mutations (discard changes).
    /// Restores original values in offline tables before deleting mutations.
    /// </summary>
    public Task CancelPendingMutationsAsync()
        => _mutationQueue.CancelPendingMutationsAsync();

    /// <summary>
    /// Clears all pending mutations for a specific trip.
    /// Call this when a trip is deleted to clean up the queue.
    /// </summary>
    public Task ClearPendingMutationsForTripAsync(Guid tripId)
        => _mutationQueue.ClearPendingMutationsForTripAsync(tripId);

    /// <summary>
    /// Gets pending mutations for a specific trip that need attention (failed or rejected).
    /// </summary>
    public Task<List<PendingTripMutation>> GetFailedMutationsForTripAsync(Guid tripId)
        => _mutationQueue.GetFailedMutationsForTripAsync(tripId);

    /// <summary>
    /// Gets count of failed mutations for a specific trip.
    /// </summary>
    public Task<int> GetFailedCountForTripAsync(Guid tripId)
        => _mutationQueue.GetFailedCountForTripAsync(tripId);

    /// <summary>
    /// Resets retry attempts for a specific trip's failed mutations.
    /// </summary>
    public async Task ResetFailedMutationsForTripAsync(Guid tripId)
    {
        await _mutationQueue.ResetFailedMutationsForTripAsync(tripId);

        if (IsConnected)
        {
            await ProcessPendingMutationsAsync();
        }
    }

    /// <summary>
    /// Cancels pending mutations for a specific trip (discards unsynced changes).
    /// Also restores original values in offline tables.
    /// </summary>
    public Task CancelPendingMutationsForTripAsync(Guid tripId)
        => _mutationQueue.CancelPendingMutationsForTripAsync(tripId);

    /// <summary>
    /// Determines if the HTTP error is a permanent client error (should not be retried).
    /// Excludes 429 Too Many Requests which is temporary and should be retried.
    /// </summary>
    private static bool IsClientError(HttpRequestException ex)
    {
        if (!ex.StatusCode.HasValue)
            return false;

        var statusCode = (int)ex.StatusCode.Value;

        // 429 Too Many Requests is NOT a permanent client error - it should be retried
        if (statusCode == 429)
            return false;

        return statusCode >= 400 && statusCode < 500;
    }

    #endregion
}
