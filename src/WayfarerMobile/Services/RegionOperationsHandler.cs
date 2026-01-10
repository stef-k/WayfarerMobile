using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Handles region CRUD operations with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public class RegionOperationsHandler : IRegionOperationsHandler
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly IAreaRepository _areaRepository;
    private readonly ITripRepository _tripRepository;
    private readonly IConnectivity _connectivity;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    /// <summary>
    /// Creates a new instance of RegionOperationsHandler.
    /// </summary>
    public RegionOperationsHandler(
        IApiClient apiClient,
        DatabaseService databaseService,
        IAreaRepository areaRepository,
        ITripRepository tripRepository,
        IConnectivity connectivity)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _areaRepository = areaRepository;
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
    public async Task<RegionOperationResult> CreateRegionAsync(
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
            // Create offline entry immediately with temp ID so subsequent updates work
            var localTrip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripId);
            if (localTrip != null)
            {
                var offlineArea = new OfflineAreaEntity
                {
                    TripId = localTrip.Id,
                    ServerId = tempClientId,
                    Name = name,
                    Notes = notes,
                    CoverImageUrl = coverImageUrl,
                    CenterLatitude = centerLatitude,
                    CenterLongitude = centerLongitude,
                    SortOrder = displayOrder ?? 0
                };
                await _areaRepository.InsertOfflineAreaAsync(offlineArea);
            }

            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            return RegionOperationResult.Queued(tempClientId, "Created offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.CreateRegionAsync(tripId, request);

            if (response?.Success == true && response.Id != Guid.Empty)
            {
                // Create offline entry so subsequent updates can find it
                var localTrip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripId);
                if (localTrip != null)
                {
                    var offlineArea = new OfflineAreaEntity
                    {
                        TripId = localTrip.Id,
                        ServerId = response.Id,
                        Name = name,
                        Notes = notes,
                        CoverImageUrl = coverImageUrl,
                        CenterLatitude = centerLatitude,
                        CenterLongitude = centerLongitude,
                        SortOrder = displayOrder ?? 0
                    };
                    await _areaRepository.InsertOfflineAreaAsync(offlineArea);
                }

                return RegionOperationResult.Completed(response.Id);
            }

            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            return RegionOperationResult.Queued(tempClientId, "Sync failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            return RegionOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            return RegionOperationResult.Queued(tempClientId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            return RegionOperationResult.Queued(tempClientId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueRegionMutationAsync("Create", tempClientId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, true, tempClientId);
            return RegionOperationResult.Queued(tempClientId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    /// <inheritdoc/>
    public async Task<RegionOperationResult> UpdateRegionAsync(
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
        var offlineArea = await _areaRepository.GetOfflineAreaByServerIdAsync(regionId);
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
            await _areaRepository.UpdateOfflineAreaAsync(offlineArea);
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
            return RegionOperationResult.Queued(regionId, "Saved offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.UpdateRegionAsync(regionId, request);

            if (response != null)
            {
                return RegionOperationResult.Completed(regionId);
            }

            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            return RegionOperationResult.Queued(regionId, "Sync failed - will retry");
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
                await _areaRepository.UpdateOfflineAreaAsync(offlineArea);
            }
            return RegionOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            return RegionOperationResult.Queued(regionId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            return RegionOperationResult.Queued(regionId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry with original values
            await EnqueueRegionMutationWithOriginalAsync("Update", regionId, tripId, name, notes, coverImageUrl, centerLatitude, centerLongitude, displayOrder, includeNotes, null,
                originalName, originalNotes, null, originalCenterLatitude, originalCenterLongitude, originalDisplayOrder);
            return RegionOperationResult.Queued(regionId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    /// <inheritdoc/>
    public async Task<RegionOperationResult> DeleteRegionAsync(Guid regionId, Guid tripId)
    {
        await EnsureInitializedAsync();

        // 1. Read full region data for restoration
        var offlineArea = await _areaRepository.GetOfflineAreaByServerIdAsync(regionId);

        // 2. Delete from offline table (optimistic)
        await _areaRepository.DeleteOfflineAreaByServerIdAsync(regionId);

        if (!IsConnected)
        {
            // 3. Store original data in queue for restoration if user cancels
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            return RegionOperationResult.Queued(regionId, "Deleted offline - will sync when online");
        }

        try
        {
            var success = await _apiClient.DeleteRegionAsync(regionId);

            if (success)
            {
                return RegionOperationResult.Completed(regionId);
            }

            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            return RegionOperationResult.Queued(regionId, "Delete failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore the region in offline table
            if (offlineArea != null)
            {
                offlineArea.Id = 0; // Reset for insert
                await _areaRepository.InsertOfflineAreaAsync(offlineArea);
            }
            return RegionOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            return RegionOperationResult.Queued(regionId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            return RegionOperationResult.Queued(regionId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueDeleteRegionMutationWithOriginalAsync(regionId, tripId, offlineArea);
            return RegionOperationResult.Queued(regionId, $"Unexpected error: {ex.Message} - will retry");
        }
    }

    #region Private Helper Methods

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
