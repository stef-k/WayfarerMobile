using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Handles place CRUD operations with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public class PlaceOperationsHandler : IPlaceOperationsHandler
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly IPlaceRepository _placeRepository;
    private readonly ITripRepository _tripRepository;
    private readonly IConnectivity _connectivity;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    /// <summary>
    /// Creates a new instance of PlaceOperationsHandler.
    /// </summary>
    public PlaceOperationsHandler(
        IApiClient apiClient,
        DatabaseService databaseService,
        IPlaceRepository placeRepository,
        ITripRepository tripRepository,
        IConnectivity connectivity)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _placeRepository = placeRepository;
        _tripRepository = tripRepository;
        _connectivity = connectivity;
    }

    /// <summary>
    /// Ensures the database connection is initialized.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _database = await _databaseService.GetConnectionAsync();
            await _database.CreateTableAsync<PendingTripMutation>();

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
    private bool IsConnected =>
        _connectivity.NetworkAccess == NetworkAccess.Internet;

    /// <inheritdoc/>
    public async Task<PlaceOperationResult> CreatePlaceAsync(
        Guid tripId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes = null,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null,
        Guid? clientTempId = null)
    {
        await EnsureInitializedAsync();

        // Use caller's temp ID if provided, otherwise generate one
        var tempClientId = clientTempId ?? Guid.NewGuid();

        // D6: If RegionId has a pending CREATE, queue this Place CREATE to maintain dependency ordering
        // This prevents 400 errors when online but parent Region hasn't synced yet
        if (regionId.HasValue)
        {
            var pendingRegionCreate = await _database!.Table<PendingTripMutation>()
                .Where(m => m.EntityId == regionId.Value
                         && m.EntityType == "Region"
                         && m.OperationType == "Create")
                .FirstOrDefaultAsync();

            if (pendingRegionCreate != null)
            {
                // Ensure offline entry exists with upsert pattern
                await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
                await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
                return PlaceOperationResult.Queued(tempClientId, "Queued - parent Region pending sync");
            }
        }

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
            // Ensure offline entry exists with upsert pattern (temp ID as placeholder ServerId)
            await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            return PlaceOperationResult.Queued(tempClientId, "Created offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.CreatePlaceAsync(tripId, request);

            if (response?.Success == true && response.Id != Guid.Empty)
            {
                // Success: create offline entry with server ID (upsert in case temp entry exists)
                await EnsureOfflinePlaceEntryAsync(tripId, response.Id, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
                return PlaceOperationResult.Completed(response.Id, tempClientId);
            }

            // Null/failed response: queue for retry and ensure offline entry exists
            await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            return PlaceOperationResult.Queued(tempClientId, "Sync failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            return PlaceOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // Network error: queue for retry and ensure offline entry exists
            await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            return PlaceOperationResult.Queued(tempClientId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout: queue for retry and ensure offline entry exists
            await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            return PlaceOperationResult.Queued(tempClientId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            // Unexpected error: queue for retry and ensure offline entry exists
            await EnsureOfflinePlaceEntryAsync(tripId, tempClientId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder);
            await EnqueuePlaceMutationAsync("Create", tempClientId, tripId, regionId, name, latitude, longitude, notes, iconName, markerColor, displayOrder, true, tempClientId);
            return PlaceOperationResult.Queued(tempClientId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    /// <inheritdoc/>
    public async Task<PlaceOperationResult> UpdatePlaceAsync(
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
        var offlinePlace = await _placeRepository.GetOfflinePlaceByServerIdAsync(placeId);
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
            await _placeRepository.UpdateOfflinePlaceAsync(offlinePlace);
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
            return PlaceOperationResult.Queued(placeId, "Saved offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.UpdatePlaceAsync(placeId, request);

            if (response != null)
            {
                return PlaceOperationResult.Completed(placeId);
            }

            // Sync failed - queue for retry with original values for restoration
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            return PlaceOperationResult.Queued(placeId, "Sync failed - will retry");
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
                await _placeRepository.UpdateOfflinePlaceAsync(offlinePlace);
            }
            return PlaceOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            return PlaceOperationResult.Queued(placeId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            return PlaceOperationResult.Queued(placeId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry with original values
            await EnqueuePlaceMutationWithOriginalAsync("Update", placeId, tripId, null,
                name, latitude, longitude, notes, iconName, markerColor, displayOrder, includeNotes, null,
                originalName, originalLatitude, originalLongitude, originalNotes, originalIconName, originalMarkerColor, originalDisplayOrder);
            return PlaceOperationResult.Queued(placeId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    /// <inheritdoc/>
    public async Task<PlaceOperationResult> DeletePlaceAsync(Guid placeId, Guid tripId)
    {
        await EnsureInitializedAsync();

        // D4: If entity was never synced (pending CREATE exists), just cancel the CREATE
        var pendingCreate = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == placeId && m.EntityType == "Place" && m.OperationType == "Create")
            .FirstOrDefaultAsync();

        if (pendingCreate != null)
        {
            // Entity was never synced - remove from queue and offline table
            await _database.DeleteAsync(pendingCreate);
            await _placeRepository.DeleteOfflinePlaceByServerIdAsync(placeId);

            // Also delete any pending mutations for this place
            var relatedMutations = await _database.Table<PendingTripMutation>()
                .Where(m => m.EntityId == placeId && m.EntityType == "Place")
                .ToListAsync();
            foreach (var m in relatedMutations)
                await _database.DeleteAsync(m);

            return PlaceOperationResult.Completed(placeId, message: "Cancelled - entity was never synced");
        }

        // 1. Read full place data for restoration
        var offlinePlace = await _placeRepository.GetOfflinePlaceByServerIdAsync(placeId);

        // 2. Delete from offline table (optimistic)
        await _placeRepository.DeleteOfflinePlaceByServerIdAsync(placeId);

        if (!IsConnected)
        {
            // 3. Store original data in queue for restoration if user cancels
            await EnqueueDeleteMutationWithOriginalAsync(placeId, tripId, offlinePlace);
            return PlaceOperationResult.Queued(placeId, "Deleted offline - will sync when online");
        }

        try
        {
            var success = await _apiClient.DeletePlaceAsync(placeId);

            if (success)
            {
                return PlaceOperationResult.Completed(placeId);
            }

            await EnqueueDeleteMutationWithOriginalAsync(placeId, tripId, offlinePlace);
            return PlaceOperationResult.Queued(placeId, "Delete failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore the place in offline table
            if (offlinePlace != null)
            {
                offlinePlace.Id = 0; // Reset for insert
                await _placeRepository.InsertOfflinePlaceAsync(offlinePlace);
            }
            return PlaceOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteMutationWithOriginalAsync(placeId, tripId, offlinePlace);
            return PlaceOperationResult.Queued(placeId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteMutationWithOriginalAsync(placeId, tripId, offlinePlace);
            return PlaceOperationResult.Queued(placeId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationWithOriginalAsync(placeId, tripId, offlinePlace);
            return PlaceOperationResult.Queued(placeId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    #region Private Helper Methods

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

    private async Task EnqueueDeleteMutationWithOriginalAsync(Guid entityId, Guid tripId, OfflinePlaceEntity? originalPlace)
    {
        // Remove any pending creates/updates for this entity
        await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == entityId && m.EntityType == "Place")
            .DeleteAsync();

        var mutation = new PendingTripMutation
        {
            EntityType = "Place",
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

    /// <summary>
    /// Ensures an offline place entry exists with upsert pattern.
    /// Used when queuing CREATE mutations - the offline entry must exist for subsequent updates/deletes.
    /// </summary>
    private async Task EnsureOfflinePlaceEntryAsync(
        Guid tripId,
        Guid serverId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes,
        string? iconName,
        string? markerColor,
        int? displayOrder)
    {
        var localTrip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripId);
        if (localTrip == null)
            return;

        // Upsert pattern: check if entry already exists
        var existing = await _placeRepository.GetOfflinePlaceByServerIdAsync(serverId);
        if (existing != null)
            return; // Already exists, no action needed

        var offlinePlace = new OfflinePlaceEntity
        {
            TripId = localTrip.Id,
            ServerId = serverId,
            RegionId = regionId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = notes,
            IconName = iconName,
            MarkerColor = markerColor,
            SortOrder = displayOrder ?? 0
        };
        await _placeRepository.InsertOfflinePlaceAsync(offlinePlace);
    }

    #endregion
}
