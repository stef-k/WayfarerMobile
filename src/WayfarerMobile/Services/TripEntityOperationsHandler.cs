using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Handles trip entity update operations (Trip, Segment, Area) with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public class TripEntityOperationsHandler : ITripEntityOperationsHandler
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly IConnectivity _connectivity;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    /// <summary>
    /// Creates a new instance of TripEntityOperationsHandler.
    /// </summary>
    public TripEntityOperationsHandler(
        IApiClient apiClient,
        DatabaseService databaseService,
        IConnectivity connectivity)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
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

    #region Trip Operations

    /// <inheritdoc/>
    public async Task<EntityOperationResult> UpdateTripAsync(
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
            return EntityOperationResult.Queued(tripId, "Updated offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.UpdateTripAsync(tripId, request);

            if (response?.Success == true)
            {
                return EntityOperationResult.Completed(tripId);
            }

            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            return EntityOperationResult.Queued(tripId, "Sync failed - will retry");
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
            return EntityOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            return EntityOperationResult.Queued(tripId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            return EntityOperationResult.Queued(tripId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueTripMutationAsync(tripId, name, notes, includeNotes, originalName, originalNotes);
            return EntityOperationResult.Queued(tripId, $"Unexpected error: {ex.Message} - will retry");
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

    /// <inheritdoc/>
    public async Task<EntityOperationResult> UpdateSegmentNotesAsync(
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
            return EntityOperationResult.Queued(segmentId, "Updated offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.UpdateSegmentNotesAsync(segmentId, request);

            if (response?.Success == true)
            {
                return EntityOperationResult.Completed(segmentId);
            }

            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(segmentId, "Sync failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original
            if (offlineSegment != null)
            {
                offlineSegment.Notes = originalNotes;
                await _databaseService.UpdateOfflineSegmentAsync(offlineSegment);
            }
            return EntityOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(segmentId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(segmentId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueSegmentMutationWithOriginalAsync(segmentId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(segmentId, $"Unexpected error: {ex.Message} - will retry");
        }
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

    /// <inheritdoc/>
    public async Task<EntityOperationResult> UpdateAreaNotesAsync(
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
            return EntityOperationResult.Queued(areaId, "Updated offline - will sync when online");
        }

        try
        {
            var response = await _apiClient.UpdateAreaNotesAsync(areaId, request);

            if (response?.Success == true)
            {
                return EntityOperationResult.Completed(areaId);
            }

            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(areaId, "Sync failed - will retry");
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - restore original
            if (offlinePolygon != null)
            {
                offlinePolygon.Notes = originalNotes;
                await _databaseService.UpdateOfflinePolygonAsync(offlinePolygon);
            }
            return EntityOperationResult.Rejected($"Server rejected: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(areaId, $"Network error: {ex.Message} - will retry");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(areaId, "Request timed out - will retry");
        }
        catch (Exception ex)
        {
            await EnqueueAreaMutationWithOriginalAsync(areaId, tripId, notes, originalNotes);
            return EntityOperationResult.Queued(areaId, $"Unexpected error: {ex.Message} - will retry");
        }
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

    #region Private Helper Methods

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
