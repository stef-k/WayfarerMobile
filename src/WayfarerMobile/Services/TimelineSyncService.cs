using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for synchronizing timeline location changes with the server.
/// Implements optimistic UI updates with offline queue for resilience.
///
/// Sync Strategy:
/// 1. Apply optimistic UI update immediately (caller responsibility)
/// 2. Save to local database
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - revert changes, notify caller
/// 5. On 5xx/network error: Queue for retry when online
/// </summary>
public class TimelineSyncService : ITimelineSyncService
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
    /// Creates a new instance of TimelineSyncService.
    /// </summary>
    public TimelineSyncService(
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
        await _database.CreateTableAsync<PendingTimelineMutation>();
        _initialized = true;
    }

    /// <summary>
    /// Gets whether the device is currently connected to the internet.
    /// </summary>
    private static bool IsConnected =>
        Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    /// <summary>
    /// Updates a timeline location with optimistic UI pattern.
    /// Call this after applying optimistic UI update.
    /// </summary>
    public async Task UpdateLocationAsync(
        int locationId,
        double? latitude = null,
        double? longitude = null,
        DateTime? localTimestamp = null,
        string? notes = null,
        bool includeNotes = false)
    {
        await EnsureInitializedAsync();

        // Build request
        var request = new TimelineLocationUpdateRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            LocalTimestamp = localTimestamp,
            Notes = includeNotes ? notes : null
        };

        // Check connectivity first
        if (!IsConnected)
        {
            await EnqueueMutationAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Saved offline - will sync when online" });
            return;
        }

        // Try server sync
        try
        {
            var response = await _apiClient.UpdateTimelineLocationAsync(locationId, request);

            if (response != null && response.Success)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            // Null or failed response - queue for retry
            await EnqueueMutationAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, don't queue for retry
            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected changes: {ex.Message}",
                IsClientError = true
            });
        }
        catch (Exception ex)
        {
            // Network error or 5xx - queue for retry
            await EnqueueMutationAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Sync failed: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Deletes a timeline location with optimistic UI pattern.
    /// </summary>
    public async Task DeleteLocationAsync(int locationId)
    {
        await EnsureInitializedAsync();

        if (!IsConnected)
        {
            await EnqueueDeleteMutationAsync(locationId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeleteTimelineLocationAsync(locationId);

            if (success)
            {
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            await EnqueueDeleteMutationAsync(locationId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected: {ex.Message}",
                IsClientError = true
            });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationAsync(locationId);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Delete failed: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Enqueue a delete mutation.
    /// </summary>
    private async Task EnqueueDeleteMutationAsync(int locationId)
    {
        // Remove any pending updates for this location
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId)
            .DeleteAsync();

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    /// <summary>
    /// Enqueue a mutation for later sync.
    /// </summary>
    private async Task EnqueueMutationAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes)
    {
        // Check if there's already a pending mutation for this location
        var existing = await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsServerRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Merge with existing mutation (latest values win)
            if (latitude.HasValue) existing.Latitude = latitude;
            if (longitude.HasValue) existing.Longitude = longitude;
            if (localTimestamp.HasValue) existing.LocalTimestamp = localTimestamp;
            if (includeNotes)
            {
                existing.Notes = notes;
                existing.IncludeNotes = true;
            }
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
        }
        else
        {
            var mutation = new PendingTimelineMutation
            {
                OperationType = "Update",
                LocationId = locationId,
                Latitude = latitude,
                Longitude = longitude,
                LocalTimestamp = localTimestamp,
                Notes = notes,
                IncludeNotes = includeNotes,
                CreatedAt = DateTime.UtcNow
            };
            await _database.InsertAsync(mutation);
        }
    }

    /// <summary>
    /// Process pending mutations (call when connectivity is restored).
    /// </summary>
    public async Task ProcessPendingMutationsAsync()
    {
        await EnsureInitializedAsync();

        if (!IsConnected) return;

        var pending = await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.CanSync)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        foreach (var mutation in pending)
        {
            try
            {
                mutation.SyncAttempts++;
                mutation.LastSyncAttempt = DateTime.UtcNow;

                bool success;
                if (mutation.OperationType == "Delete")
                {
                    success = await _apiClient.DeleteTimelineLocationAsync(mutation.LocationId);
                }
                else
                {
                    var request = new TimelineLocationUpdateRequest
                    {
                        Latitude = mutation.Latitude,
                        Longitude = mutation.Longitude,
                        LocalTimestamp = mutation.LocalTimestamp,
                        Notes = mutation.IncludeNotes ? mutation.Notes : null
                    };

                    var response = await _apiClient.UpdateTimelineLocationAsync(mutation.LocationId, request);
                    success = response?.Success == true;
                }

                if (success)
                {
                    // Success - remove from queue
                    await _database.DeleteAsync(mutation);
                    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                }
                else
                {
                    mutation.LastError = "No response from server";
                    await _database.UpdateAsync(mutation);
                }
            }
            catch (HttpRequestException ex) when (IsClientError(ex))
            {
                // Server rejected - mark as rejected, don't retry
                mutation.IsServerRejected = true;
                mutation.LastError = ex.Message;
                await _database.UpdateAsync(mutation);

                SyncRejected?.Invoke(this, new SyncFailureEventArgs
                {
                    EntityId = Guid.Empty,
                    ErrorMessage = ex.Message,
                    IsClientError = true
                });
            }
            catch (Exception ex)
            {
                mutation.LastError = ex.Message;
                await _database.UpdateAsync(mutation);
            }
        }
    }

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();
        return await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.CanSync)
            .CountAsync();
    }

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.IsServerRejected)
            .DeleteAsync();
    }

    private static bool IsClientError(HttpRequestException ex)
    {
        // Check if it's a 4xx status code
        return ex.StatusCode.HasValue &&
               (int)ex.StatusCode.Value >= 400 &&
               (int)ex.StatusCode.Value < 500;
    }
}

/// <summary>
/// Interface for timeline sync service.
/// </summary>
public interface ITimelineSyncService
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
    /// Update a timeline location with optimistic UI pattern.
    /// </summary>
    Task UpdateLocationAsync(
        int locationId,
        double? latitude = null,
        double? longitude = null,
        DateTime? localTimestamp = null,
        string? notes = null,
        bool includeNotes = false);

    /// <summary>
    /// Delete a timeline location with optimistic UI pattern.
    /// </summary>
    Task DeleteLocationAsync(int locationId);

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
