using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

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
        DatabaseService databaseService)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
    }

    /// <summary>
    /// Ensures the database connection is initialized.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database = await _databaseService.GetConnectionAsync();
        await _database.CreateTableAsync<PendingTripMutation>();
        _initialized = true;
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
            Icon = iconName,
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
        catch (Exception ex)
        {
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Sync failed: {ex.Message} - will retry" });
            return tempClientId;
        }
    }

    /// <summary>
    /// Updates a place with optimistic UI pattern.
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

        var request = new PlaceUpdateRequest
        {
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = includeNotes ? notes : null,
            Icon = iconName,
            MarkerColor = markerColor
        };

        if (!IsConnected)
        {
            await EnqueuePlaceMutationAsync("Update", placeId, tripId, null, name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null);
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

            await EnqueuePlaceMutationAsync("Update", placeId, tripId, null, name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = placeId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (Exception ex)
        {
            await EnqueuePlaceMutationAsync("Update", placeId, tripId, null, name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Sync failed: {ex.Message} - will retry" });
        }
    }

    /// <summary>
    /// Deletes a place with optimistic UI pattern.
    /// </summary>
    public async Task DeletePlaceAsync(Guid placeId, Guid tripId)
    {
        await EnsureInitializedAsync();

        if (!IsConnected)
        {
            await EnqueueDeleteMutationAsync("Place", placeId, tripId);
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

            await EnqueueDeleteMutationAsync("Place", placeId, tripId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = placeId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationAsync("Place", placeId, tripId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = placeId, Message = $"Delete failed: {ex.Message} - will retry" });
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
                .Where(m => m.EntityId == entityId && m.EntityType == "Place" && !m.IsServerRejected && m.OperationType != "Delete")
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
        catch (Exception ex)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = tempClientId, Message = $"Sync failed: {ex.Message} - will retry" });
            return tempClientId;
        }
    }

    /// <summary>
    /// Updates a region with optimistic UI pattern.
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
            await EnqueueRegionMutationAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null);
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

            await EnqueueRegionMutationAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = regionId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (Exception ex)
        {
            await EnqueueRegionMutationAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Sync failed: {ex.Message} - will retry" });
        }
    }

    /// <summary>
    /// Deletes a region with optimistic UI pattern.
    /// </summary>
    public async Task DeleteRegionAsync(Guid regionId, Guid tripId)
    {
        await EnsureInitializedAsync();

        if (!IsConnected)
        {
            await EnqueueDeleteMutationAsync("Region", regionId, tripId);
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

            await EnqueueDeleteMutationAsync("Region", regionId, tripId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = regionId, ErrorMessage = $"Server rejected: {ex.Message}", IsClientError = true });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationAsync("Region", regionId, tripId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = regionId, Message = $"Delete failed: {ex.Message} - will retry" });
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
                .Where(m => m.EntityId == entityId && m.EntityType == "Region" && !m.IsServerRejected && m.OperationType != "Delete")
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

    #endregion

    #region Process Pending Mutations

    /// <summary>
    /// Process pending mutations (call when connectivity is restored).
    /// </summary>
    public async Task ProcessPendingMutationsAsync()
    {
        await EnsureInitializedAsync();

        if (!IsConnected) return;

        var pending = await _database!.Table<PendingTripMutation>()
            .Where(m => m.CanSync)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        foreach (var mutation in pending)
        {
            try
            {
                mutation.SyncAttempts++;
                mutation.LastSyncAttempt = DateTime.UtcNow;

                var success = await ProcessMutationAsync(mutation);

                if (success)
                {
                    await _database.DeleteAsync(mutation);
                    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = mutation.EntityId });
                }
                else
                {
                    mutation.LastError = "No response from server";
                    await _database.UpdateAsync(mutation);
                }
            }
            catch (HttpRequestException ex) when (IsClientError(ex))
            {
                mutation.IsServerRejected = true;
                mutation.LastError = ex.Message;
                await _database.UpdateAsync(mutation);
                SyncRejected?.Invoke(this, new SyncFailureEventArgs { EntityId = mutation.EntityId, ErrorMessage = ex.Message, IsClientError = true });
            }
            catch (Exception ex)
            {
                mutation.LastError = ex.Message;
                await _database.UpdateAsync(mutation);
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
            Icon = mutation.IconName,
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
            Icon = mutation.IconName,
            MarkerColor = mutation.MarkerColor
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

    #endregion

    #region Utility Methods

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTripMutation>()
            .Where(m => m.CanSync)
            .CountAsync();
    }

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        await _database!.Table<PendingTripMutation>()
            .Where(m => m.IsServerRejected)
            .DeleteAsync();
    }

    private static bool IsClientError(HttpRequestException ex)
    {
        return ex.StatusCode.HasValue &&
               (int)ex.StatusCode.Value >= 400 &&
               (int)ex.StatusCode.Value < 500;
    }

    #endregion
}

/// <summary>
/// Interface for trip sync service.
/// </summary>
public interface ITripSyncService
{
    /// <summary>
    /// Event raised when sync is rejected by server.
    /// </summary>
    event EventHandler<SyncFailureEventArgs>? SyncRejected;

    /// <summary>
    /// Event raised when sync is queued for offline retry.
    /// </summary>
    event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <summary>
    /// Event raised when sync completes successfully.
    /// </summary>
    event EventHandler<SyncSuccessEventArgs>? SyncCompleted;

    /// <summary>
    /// Event raised when a create operation completes with server ID.
    /// </summary>
    event EventHandler<EntityCreatedEventArgs>? EntityCreated;

    #region Place Operations

    /// <summary>
    /// Create a new place with optimistic UI pattern.
    /// </summary>
    Task<Guid> CreatePlaceAsync(
        Guid tripId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes = null,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null);

    /// <summary>
    /// Update a place with optimistic UI pattern.
    /// </summary>
    Task UpdatePlaceAsync(
        Guid placeId,
        Guid tripId,
        string? name = null,
        double? latitude = null,
        double? longitude = null,
        string? notes = null,
        bool includeNotes = false,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null);

    /// <summary>
    /// Delete a place with optimistic UI pattern.
    /// </summary>
    Task DeletePlaceAsync(Guid placeId, Guid tripId);

    #endregion

    #region Region Operations

    /// <summary>
    /// Create a new region with optimistic UI pattern.
    /// </summary>
    Task<Guid> CreateRegionAsync(
        Guid tripId,
        string name,
        string? notes = null,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null);

    /// <summary>
    /// Update a region with optimistic UI pattern.
    /// </summary>
    Task UpdateRegionAsync(
        Guid regionId,
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null);

    /// <summary>
    /// Delete a region with optimistic UI pattern.
    /// </summary>
    Task DeleteRegionAsync(Guid regionId, Guid tripId);

    #endregion

    /// <summary>
    /// Process pending mutations when online.
    /// </summary>
    Task ProcessPendingMutationsAsync();

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Clear rejected mutations.
    /// </summary>
    Task ClearRejectedMutationsAsync();
}

/// <summary>
/// Event args for sync failure.
/// </summary>
public class SyncFailureEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that failed to sync.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this was a client error (4xx).
    /// </summary>
    public bool IsClientError { get; set; }
}

/// <summary>
/// Event args for sync queued.
/// </summary>
public class SyncQueuedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that was queued.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Event args for sync success.
/// </summary>
public class SyncSuccessEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the entity ID that synced successfully.
    /// </summary>
    public Guid EntityId { get; set; }
}

/// <summary>
/// Event args for entity created with server-assigned ID.
/// </summary>
public class EntityCreatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets or sets the temporary client ID used before sync.
    /// </summary>
    public Guid TempClientId { get; set; }

    /// <summary>
    /// Gets or sets the server-assigned ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the entity type (Place, Region).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
}
