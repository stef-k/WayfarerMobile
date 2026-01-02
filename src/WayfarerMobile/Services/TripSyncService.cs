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
        IMutationQueueService mutationQueue)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _mutationQueue = mutationQueue;
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
        await EnsureInitializedAsync();

        // Generate temporary client ID
        var tempClientId = Guid.NewGuid();

        var request = new PlaceCreateRequest
        {
            RegionId = regionId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = notes,
            IconName = iconName,
            MarkerColor = markerColor,
            DisplayOrder = displayOrder
        };

        if (!IsConnected)
        {
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Created offline - will sync when online" });
            return tempClientId;
        }

        try
        {
            var response = await _apiClient.CreatePlaceAsync(tripId, request);

            if (response?.Success == true && response.Id != Guid.Empty)
            {
                EntityCreated?.Invoke(this, new EntityCreatedEventArgs { TempClientId = tempClientId, ServerId = response.Id, EntityType = "Place" });
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = response.Id });
                return response.Id;
            }

            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Sync failed - will retry" });
            return tempClientId;
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = tempClientId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
            return Guid.Empty;
        }
        catch (HttpRequestException ex)
        {
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Network error: {ex.Message} - will retry" });
            return tempClientId;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Request timed out - will retry" });
            return tempClientId;
        }
        catch (Exception ex)
        {
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Unexpected error: {ex.Message} - will retry" });
            return tempClientId;
        }
    }

    /// <summary>
    /// Updates a place with optimistic UI pattern.
    /// 1. Reads current value from offline table (original)
    /// 2. Updates offline table with new values (optimistic)
    /// 3. Stores original in queue for restoration if needed
    /// 4. Tries to sync to server
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
        await EnsureInitializedAsync();

        // 1. Read current value from offline table (for restoration if needed)
        var offlinePlace = await _databaseService.GetOfflinePlaceByServerIdAsync(placeId);
        string? originalName = offlinePlace?.Name;
        double? originalLatitude = offlinePlace?.Latitude;
        double? originalLongitude = offlinePlace?.Longitude;
        string? originalNotes = offlinePlace?.Notes;
        string? originalIconName = offlinePlace?.IconName;
        string? originalMarkerColor = offlinePlace?.MarkerColor;
        int? originalDisplayOrder = offlinePlace?.SortOrder;

        // 2. Update offline table with new values (optimistic update)
        if (offlinePlace != null)
        {
            if (name != null) offlinePlace.Name = name;
            if (latitude.HasValue) offlinePlace.Latitude = latitude.Value;
            if (longitude.HasValue) offlinePlace.Longitude = longitude.Value;
            if (includeNotes) offlinePlace.Notes = notes;
            if (iconName != null) offlinePlace.IconName = iconName;
            if (markerColor != null) offlinePlace.MarkerColor = markerColor;
            if (displayOrder.HasValue) offlinePlace.SortOrder = displayOrder.Value;
            await _databaseService.UpdateOfflinePlaceAsync(offlinePlace);
        }

        var request = new PlaceUpdateRequest
        {
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = includeNotes ? notes : null,
            IconName = iconName,
            MarkerColor = markerColor,
            DisplayOrder = displayOrder
        };

        if (!IsConnected)
        {
            // 3. Store original values in queue for restoration
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Saved offline - will sync when online" });
            return;
        }

        try
        {
            var response = await _apiClient.UpdatePlaceAsync(placeId, request);

            if (response != null)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = placeId });
                return;
            }

            // Sync failed - queue for retry with original values for restoration
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original values in offline table
            if (offlinePlace != null)
            {
                offlinePlace.Name = originalName ?? offlinePlace.Name;
                if (originalLatitude.HasValue) offlinePlace.Latitude = originalLatitude.Value;
                if (originalLongitude.HasValue) offlinePlace.Longitude = originalLongitude.Value;
                if (includeNotes) offlinePlace.Notes = originalNotes;
                offlinePlace.IconName = originalIconName;
                offlinePlace.MarkerColor = originalMarkerColor;
                if (originalDisplayOrder.HasValue) offlinePlace.SortOrder = originalDisplayOrder.Value;
                await _databaseService.UpdateOfflinePlaceAsync(offlinePlace);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = placeId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    /// <summary>
    /// Deletes a place with optimistic UI pattern.
    /// 1. Reads full place data for restoration
    /// 2. Deletes from offline table (optimistic)
    /// 3. Stores original data in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    public async Task DeletePlaceAsync(Guid placeId, Guid tripId)
    {
        await EnsureInitializedAsync();

        // 1. Read full place data for restoration
        var offlinePlace = await _databaseService.GetOfflinePlaceByServerIdAsync(placeId);

        // 2. Delete from offline table (optimistic)
        await _databaseService.DeleteOfflinePlaceByServerIdAsync(placeId);

        if (!IsConnected)
        {
            // 3. Store original data in queue for restoration if user cancels
            await EnqueueDeleteMutationWithOriginalAsync("Place", placeId, tripId, offlinePlace);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeletePlaceAsync(placeId);

            if (success)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = placeId });
                return;
            }

            await EnqueueDeleteMutationWithOriginalAsync("Place", placeId, tripId, offlinePlace);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore the place in offline table
            if (offlinePlace != null)
            {
                offlinePlace.Id = 0; // Reset for insert
                await _databaseService.InsertOfflinePlaceAsync(offlinePlace);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = placeId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteMutationWithOriginalAsync("Place", placeId, tripId, offlinePlace);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteMutationWithOriginalAsync("Place", placeId, tripId, offlinePlace);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationWithOriginalAsync("Place", placeId, tripId, offlinePlace);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    private async Task EnqueuePlaceMutationAsync(
        string operationType,
        Guid entityId,
        Guid tripId,
        Guid? regionId,
        string? name,
        double? latitude,
        double? longitude,
        string? notes,
        string? iconName,
        string? markerColor,
        int? displayOrder,
        bool includeNotes,
        Guid? tempClientId)
    {
        // For updates, check if there's an existing mutation to merge
        if (operationType == "Update")
        {
            var existing = await _database!.Table<PendingTripMutation>()
                .Where(m => m.EntityId == entityId && m.EntityType == "Place" && !m.IsRejected && m.OperationType != "Delete")
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                if (name != null) existing.Name = name;
                if (latitude.HasValue) existing.Latitude = latitude;
                if (longitude.HasValue) existing.Longitude = longitude;
                if (includeNotes) { existing.Notes = notes; existing.IncludeNotes = true; }
                if (iconName != null) existing.IconName = iconName;
                if (markerColor != null) existing.MarkerColor = markerColor;
                if (displayOrder.HasValue) existing.DisplayOrder = displayOrder;
                existing.CreatedAt = DateTime.UtcNow;
                await _database.UpdateAsync(existing);
                return;
            }
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Place",
            OperationType = operationType,
            EntityId = entityId,
            TripId = tripId,
            RegionId = regionId,
            TempClientId = tempClientId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = notes,
            IncludeNotes = includeNotes,
            IconName = iconName,
            MarkerColor = markerColor,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    private async Task EnqueuePlaceMutationWithOriginalAsync(
        string operationType,
        Guid entityId,
        Guid tripId,
        Guid? regionId,
        string? name,
        double? latitude,
        double? longitude,
        string? notes,
        string? iconName,
        string? markerColor,
        int? displayOrder,
        bool includeNotes,
        Guid? tempClientId,
        string? originalName,
        double? originalLatitude,
        double? originalLongitude,
        string? originalNotes,
        string? originalIconName,
        string? originalMarkerColor,
        int? originalDisplayOrder)
    {
        // For updates, check if there's an existing mutation to merge
        if (operationType == "Update")
        {
            var existing = await _database!.Table<PendingTripMutation>()
                .Where(m => m.EntityId == entityId && m.EntityType == "Place" && !m.IsRejected && m.OperationType != "Delete")
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                // Update new values
                if (name != null) existing.Name = name;
                if (latitude.HasValue) existing.Latitude = latitude;
                if (longitude.HasValue) existing.Longitude = longitude;
                if (includeNotes) { existing.Notes = notes; existing.IncludeNotes = true; }
                if (iconName != null) existing.IconName = iconName;
                if (markerColor != null) existing.MarkerColor = markerColor;
                if (displayOrder.HasValue) existing.DisplayOrder = displayOrder;
                // Keep original values from first mutation (don't overwrite)
                existing.CreatedAt = DateTime.UtcNow;
                await _database.UpdateAsync(existing);
                return;
            }
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Place",
            OperationType = operationType,
            EntityId = entityId,
            TripId = tripId,
            RegionId = regionId,
            TempClientId = tempClientId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = notes,
            IncludeNotes = includeNotes,
            IconName = iconName,
            MarkerColor = markerColor,
            DisplayOrder = displayOrder,
            // Store original values for restoration
            OriginalName = originalName,
            OriginalLatitude = originalLatitude,
            OriginalLongitude = originalLongitude,
            OriginalNotes = originalNotes,
            OriginalIconName = originalIconName,
            OriginalMarkerColor = originalMarkerColor,
            OriginalDisplayOrder = originalDisplayOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    #endregion

    #region Region Operations

    /// <summary>
    /// Creates a new region with optimistic UI pattern.
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
        await EnsureInitializedAsync();

        var tempClientId = Guid.NewGuid();

        var request = new RegionCreateRequest
        {
            Name = name,
            Notes = notes,
            CoverImageUrl = coverImageUrl,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            DisplayOrder = displayOrder
        };

        if (!IsConnected)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Created offline - will sync when online" });
            return tempClientId;
        }

        try
        {
            var response = await _apiClient.CreateRegionAsync(tripId, request);

            if (response?.Success == true && response.Id != Guid.Empty)
            {
                EntityCreated?.Invoke(this, new EntityCreatedEventArgs { TempClientId = tempClientId, ServerId = response.Id, EntityType = "Region" });
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = response.Id });
                return response.Id;
            }

            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Sync failed - will retry" });
            return tempClientId;
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = tempClientId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
            return Guid.Empty;
        }
        catch (HttpRequestException ex)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Network error: {ex.Message} - will retry" });
            return tempClientId;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = "Request timed out - will retry" });
            return tempClientId;
        }
        catch (Exception ex)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Unexpected error: {ex.Message} - will retry" });
            return tempClientId;
        }
    }

    /// <summary>
    /// Updates a region with optimistic UI pattern.
    /// 1. Reads current value from offline table (original)
    /// 2. Updates offline table with new values (optimistic)
    /// 3. Stores original in queue for restoration if needed
    /// 4. Tries to sync to server
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
        await EnsureInitializedAsync();

        // 1. Read current value from offline table (for restoration if needed)
        var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(regionId);
        string? originalName = offlineArea?.Name;
        string? originalNotes = offlineArea?.Notes;
        int? originalDisplayOrder = offlineArea?.SortOrder;
        double? originalCenterLatitude = offlineArea?.CenterLatitude;
        double? originalCenterLongitude = offlineArea?.CenterLongitude;

        // 2. Update offline table with new values (optimistic update)
        if (offlineArea != null)
        {
            if (name != null) offlineArea.Name = name;
            if (includeNotes) offlineArea.Notes = notes;
            if (displayOrder.HasValue) offlineArea.SortOrder = displayOrder.Value;
            if (centerLatitude.HasValue) offlineArea.CenterLatitude = centerLatitude;
            if (centerLongitude.HasValue) offlineArea.CenterLongitude = centerLongitude;
            await _databaseService.UpdateOfflineAreaAsync(offlineArea);
        }

        var request = new RegionUpdateRequest
        {
            Name = name,
            Notes = includeNotes ? notes : null,
            CoverImageUrl = coverImageUrl,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            DisplayOrder = displayOrder
        };

        if (!IsConnected)
        {
            // 3. Store original values in queue for restoration
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Saved offline - will sync when online" });
            return;
        }

        try
        {
            var response = await _apiClient.UpdateRegionAsync(regionId, request);

            if (response != null)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = regionId });
                return;
            }

            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original values in offline table
            if (offlineArea != null)
            {
                offlineArea.Name = originalName ?? offlineArea.Name;
                if (includeNotes) offlineArea.Notes = originalNotes;
                if (originalDisplayOrder.HasValue) offlineArea.SortOrder = originalDisplayOrder.Value;
                if (originalCenterLatitude.HasValue) offlineArea.CenterLatitude = originalCenterLatitude;
                if (originalCenterLongitude.HasValue) offlineArea.CenterLongitude = originalCenterLongitude;
                await _databaseService.UpdateOfflineAreaAsync(offlineArea);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = regionId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    /// <summary>
    /// Deletes a region with optimistic UI pattern.
    /// 1. Reads full region data for restoration
    /// 2. Deletes from offline table (optimistic)
    /// 3. Stores original data in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    public async Task DeleteRegionAsync(Guid regionId, Guid tripId)
    {
        await EnsureInitializedAsync();

        // 1. Read full region data for restoration
        var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(regionId);

        // 2. Delete from offline table (optimistic)
        await _databaseService.DeleteOfflineAreaByServerIdAsync(regionId);

        if (!IsConnected)
        {
            // 3. Store original data in queue for restoration if user cancels
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeleteRegionAsync(regionId);

            if (success)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = regionId });
                return;
            }

            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore the region in offline table
            if (offlineArea != null)
            {
                offlineArea.Id = 0; // Reset for insert
                await _databaseService.InsertOfflineAreaAsync(offlineArea);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = regionId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    private async Task EnqueueRegionMutationAsync(
        string operationType,
        Guid entityId,
        Guid tripId,
        string? name,
        string? notes,
        string? coverImageUrl,
        double? centerLatitude,
        double? centerLongitude,
        int? displayOrder,
        bool includeNotes,
        Guid? tempClientId)
    {
        if (operationType == "Update")
        {
            var existing = await _database!.Table<PendingTripMutation>()
                .Where(m => m.EntityId == entityId && m.EntityType == "Region" && !m.IsRejected && m.OperationType != "Delete")
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                if (name != null) existing.Name = name;
                if (includeNotes) { existing.Notes = notes; existing.IncludeNotes = true; }
                if (coverImageUrl != null) existing.CoverImageUrl = coverImageUrl;
                if (centerLatitude.HasValue) existing.CenterLatitude = centerLatitude;
                if (centerLongitude.HasValue) existing.CenterLongitude = centerLongitude;
                if (displayOrder.HasValue) existing.DisplayOrder = displayOrder;
                existing.CreatedAt = DateTime.UtcNow;
                await _database.UpdateAsync(existing);
                return;
            }
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Region",
            OperationType = operationType,
            EntityId = entityId,
            TripId = tripId,
            TempClientId = tempClientId,
            Name = name,
            Notes = notes,
            IncludeNotes = includeNotes,
            CoverImageUrl = coverImageUrl,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    private async Task EnqueueRegionMutationWithOriginalAsync(
        string operationType,
        Guid entityId,
        Guid tripId,
        string? name,
        string? notes,
        string? coverImageUrl,
        double? centerLatitude,
        double? centerLongitude,
        int? displayOrder,
        bool includeNotes,
        Guid? tempClientId,
        string? originalName,
        string? originalNotes,
        string? originalCoverImageUrl,
        double? originalCenterLatitude,
        double? originalCenterLongitude,
        int? originalDisplayOrder)
    {
        // For updates, check if there's an existing mutation to merge
        if (operationType == "Update")
        {
            var existing = await _database!.Table<PendingTripMutation>()
                .Where(m => m.EntityId == entityId && m.EntityType == "Region" && !m.IsRejected && m.OperationType != "Delete")
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                // Update new values
                if (name != null) existing.Name = name;
                if (includeNotes) { existing.Notes = notes; existing.IncludeNotes = true; }
                if (coverImageUrl != null) existing.CoverImageUrl = coverImageUrl;
                if (centerLatitude.HasValue) existing.CenterLatitude = centerLatitude;
                if (centerLongitude.HasValue) existing.CenterLongitude = centerLongitude;
                if (displayOrder.HasValue) existing.DisplayOrder = displayOrder;
                // Keep original values from first mutation (don't overwrite)
                existing.CreatedAt = DateTime.UtcNow;
                await _database.UpdateAsync(existing);
                return;
            }
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Region",
            OperationType = operationType,
            EntityId = entityId,
            TripId = tripId,
            TempClientId = tempClientId,
            Name = name,
            Notes = notes,
            IncludeNotes = includeNotes,
            CoverImageUrl = coverImageUrl,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            DisplayOrder = displayOrder,
            // Store original values for restoration
            OriginalName = originalName,
            OriginalNotes = originalNotes,
            OriginalCoverImageUrl = originalCoverImageUrl,
            OriginalCenterLatitude = originalCenterLatitude,
            OriginalCenterLongitude = originalCenterLongitude,
            OriginalDisplayOrder = originalDisplayOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    private async Task EnqueueDeleteRegionMutationWithOriginalAsync(Guid entityId, Guid tripId, OfflineAreaEntity? originalArea)
    {
        // Remove any pending creates/updates for this entity
        await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == "Region")
            .DeleteAsync();

        var mutation = new PendingTripMutation
        {
            EntityType = "Region",
            OperationType = "Delete",
            EntityId = entityId,
            TripId = tripId,
            // Store original values for restoration if user cancels
            OriginalName = originalArea?.Name,
            OriginalNotes = originalArea?.Notes,
            OriginalCenterLatitude = originalArea?.CenterLatitude,
            OriginalCenterLongitude = originalArea?.CenterLongitude,
            OriginalDisplayOrder = originalArea?.SortOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    #endregion

    #region Trip Operations

    /// <summary>
    /// Updates a trip's metadata with optimistic UI pattern.
    /// 1. Reads current values from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    public async Task UpdateTripAsync(
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false)
    {
        await EnsureInitializedAsync();

        // 1. Read original from offline table for potential restoration
        var originalTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
        string? originalName = originalTrip?.Name;
        string? originalNotes = originalTrip?.Notes;

        // 2. Apply optimistic update to offline table
        if (originalTrip != null)
        {
            if (name != null) originalTrip.Name = name;
            if (includeNotes) originalTrip.Notes = notes;
            await _databaseService.SaveDownloadedTripAsync(originalTrip);
        }

        var request = new TripUpdateRequest
        {
            Name = name,
            Notes = includeNotes ? notes : null
        };

        if (!IsConnected)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tripId, Message = "Updated offline - will sync when online" });
            return;
        }

        try
        {
            var response = await _apiClient.UpdateTripAsync(tripId, request);

            if (response?.Success == true)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = tripId });
                return;
            }

            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tripId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Revert optimistic update on server rejection
            if (originalTrip != null)
            {
                originalTrip.Name = originalName ?? originalTrip.Name;
                if (includeNotes) originalTrip.Notes = originalNotes;
                await _databaseService.SaveDownloadedTripAsync(originalTrip);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = tripId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tripId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tripId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tripId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    private async Task EnqueueTripMutationAsync(
        Guid tripId,
        string? name,
        string? notes,
        bool includeNotes,
        string? originalName,
        string? originalNotes)
    {
        var existing = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == tripId && m.EntityType == "Trip" && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Update new values but preserve original values from first mutation
            if (name != null) existing.Name = name;
            if (includeNotes) { existing.Notes = notes; existing.IncludeNotes = true; }
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
            return;
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Trip",
            OperationType = "Update",
            EntityId = tripId,
            TripId = tripId,
            Name = name,
            Notes = notes,
            IncludeNotes = includeNotes,
            OriginalName = originalName,
            OriginalNotes = originalNotes,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    #endregion

    #region Segment Operations

    /// <summary>
    /// Updates a segment's notes with optimistic UI pattern.
    /// 1. Reads current notes from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    public async Task UpdateSegmentNotesAsync(
        Guid segmentId,
        Guid tripId,
        string? notes)
    {
        await EnsureInitializedAsync();

        // 1. Read original from offline table
        var offlineSegment = await _databaseService.GetOfflineSegmentByServerIdAsync(segmentId);
        string? originalNotes = offlineSegment?.Notes;

        // 2. Update offline table (optimistic)
        if (offlineSegment != null)
        {
            offlineSegment.Notes = notes;
            await _databaseService.UpdateOfflineSegmentAsync(offlineSegment);
        }

        var request = new SegmentNotesUpdateRequest { Notes = notes };

        if (!IsConnected)
        {
            // 3. Store original in queue for restoration
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = segmentId, Message = "Updated offline - will sync when online" });
            return;
        }

        try
        {
            var response = await _apiClient.UpdateSegmentNotesAsync(segmentId, request);

            if (response?.Success == true)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = segmentId });
                return;
            }

            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = segmentId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original
            if (offlineSegment != null)
            {
                offlineSegment.Notes = originalNotes;
                await _databaseService.UpdateOfflineSegmentAsync(offlineSegment);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = segmentId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = segmentId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = segmentId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = segmentId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    private async Task EnqueueSegmentMutationAsync(
        Guid segmentId,
        Guid tripId,
        string? notes)
    {
        var existing = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == segmentId && m.EntityType == "Segment" && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Notes = notes;
            existing.IncludeNotes = true;
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
            return;
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Segment",
            OperationType = "Update",
            EntityId = segmentId,
            TripId = tripId,
            Notes = notes,
            IncludeNotes = true,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    private async Task EnqueueSegmentMutationWithOriginalAsync(
        Guid segmentId,
        Guid tripId,
        string? notes,
        string? originalNotes)
    {
        var existing = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == segmentId && m.EntityType == "Segment" && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Notes = notes;
            existing.IncludeNotes = true;
            // Keep original notes from first mutation (don't overwrite)
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
            return;
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Segment",
            OperationType = "Update",
            EntityId = segmentId,
            TripId = tripId,
            Notes = notes,
            IncludeNotes = true,
            OriginalNotes = originalNotes,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    #endregion

    #region Area Operations

    /// <summary>
    /// Updates an area's (polygon) notes with optimistic UI pattern.
    /// 1. Reads current notes from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    public async Task UpdateAreaNotesAsync(
        Guid tripId,
        Guid areaId,
        string? notes)
    {
        await EnsureInitializedAsync();

        // 1. Read original from offline table
        var offlinePolygon = await _databaseService.GetOfflinePolygonByServerIdAsync(areaId);
        string? originalNotes = offlinePolygon?.Notes;

        // 2. Update offline table (optimistic)
        if (offlinePolygon != null)
        {
            offlinePolygon.Notes = notes;
            await _databaseService.UpdateOfflinePolygonAsync(offlinePolygon);
        }

        var request = new AreaNotesUpdateRequest { Notes = notes };

        if (!IsConnected)
        {
            // 3. Store original in queue for restoration
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = areaId, Message = "Updated offline - will sync when online" });
            return;
        }

        try
        {
            var response = await _apiClient.UpdateAreaNotesAsync(areaId, request);

            if (response?.Success == true)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = areaId });
                return;
            }

            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = areaId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original
            if (offlinePolygon != null)
            {
                offlinePolygon.Notes = originalNotes;
                await _databaseService.UpdateOfflinePolygonAsync(offlinePolygon);
            }
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = areaId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = areaId, Message = $"Network error: {ex.Message} - will retry" });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = areaId, Message = "Request timed out - will retry" });
        }
        catch (Exception ex)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = areaId, Message = $"Unexpected error: {ex.Message} - will retry" });
        }
    }

    private async Task EnqueueAreaMutationAsync(
        Guid areaId,
        Guid tripId,
        string? notes)
    {
        var existing = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == areaId && m.EntityType == "Area" && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Notes = notes;
            existing.IncludeNotes = true;
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
            return;
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Area",
            OperationType = "Update",
            EntityId = areaId,
            TripId = tripId,
            Notes = notes,
            IncludeNotes = true,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    private async Task EnqueueAreaMutationWithOriginalAsync(
        Guid areaId,
        Guid tripId,
        string? notes,
        string? originalNotes)
    {
        var existing = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == areaId && m.EntityType == "Area" && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Notes = notes;
            existing.IncludeNotes = true;
            // Keep original notes from first mutation (don't overwrite)
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
            return;
        }

        var mutation = new PendingTripMutation
        {
            EntityType = "Area",
            OperationType = "Update",
            EntityId = areaId,
            TripId = tripId,
            Notes = notes,
            IncludeNotes = true,
            OriginalNotes = originalNotes,
            CreatedAt = DateTime.UtcNow
        };
        await _database!.InsertAsync(mutation);
    }

    #endregion

    #region Delete Helper

    private async Task EnqueueDeleteMutationAsync(string entityType, Guid entityId, Guid tripId)
    {
        // Remove any pending creates/updates for this entity
        await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == entityType)
            .DeleteAsync();

        var mutation = new PendingTripMutation
        {
            EntityType = entityType,
            OperationType = "Delete",
            EntityId = entityId,
            TripId = tripId,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    private async Task EnqueueDeleteMutationWithOriginalAsync(string entityType, Guid entityId, Guid tripId, OfflinePlaceEntity? originalPlace)
    {
        // Remove any pending creates/updates for this entity
        await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == entityType)
            .DeleteAsync();

        var mutation = new PendingTripMutation
        {
            EntityType = entityType,
            OperationType = "Delete",
            EntityId = entityId,
            TripId = tripId,
            RegionId = originalPlace?.RegionId,
            // Store original values for restoration if user cancels
            OriginalName = originalPlace?.Name,
            OriginalLatitude = originalPlace?.Latitude,
            OriginalLongitude = originalPlace?.Longitude,
            OriginalNotes = originalPlace?.Notes,
            OriginalIconName = originalPlace?.IconName,
            OriginalMarkerColor = originalPlace?.MarkerColor,
            OriginalDisplayOrder = originalPlace?.SortOrder,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
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
