using System.Text.Json;
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
/// 2. Save to local database (both PendingTimelineMutation and LocalTimelineEntry)
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - revert changes in LocalTimelineEntry, notify caller
/// 5. On 5xx/network error: Queue for retry when online (LocalTimelineEntry keeps optimistic values)
///
/// Rollback data is persisted in PendingTimelineMutation to survive app restarts.
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
    /// Also updates LocalTimelineEntry for offline viewing consistency.
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

        // Get original values for rollback before applying changes
        var originalValues = await GetOriginalValuesAsync(locationId);

        // Apply optimistic update to LocalTimelineEntry
        await ApplyLocalEntryUpdateAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes);

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
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Saved offline - will sync when online" });
            return;
        }

        // Try server sync
        try
        {
            var response = await _apiClient.UpdateTimelineLocationAsync(locationId, request);

            if (response != null && response.Success)
            {
                // Success - no need to store rollback data
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            // Null or failed response - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, revert local changes using original values
            await RevertLocalEntryFromValuesAsync(locationId, originalValues);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected changes: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Deletes a timeline location with optimistic UI pattern.
    /// Also deletes from LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task DeleteLocationAsync(int locationId)
    {
        await EnsureInitializedAsync();

        // Get the full entry before deleting (for rollback)
        var deletedEntryJson = await GetDeletedEntryJsonAsync(locationId);

        // Delete from local storage
        await ApplyLocalEntryDeleteAsync(locationId);

        if (!IsConnected)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeleteTimelineLocationAsync(locationId);

            if (success)
            {
                // Success - no rollback needed
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, restore local entry from JSON
            await RestoreDeletedEntryAsync(deletedEntryJson);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Process pending mutations (call when connectivity is restored).
    /// Uses persisted rollback data from mutations to revert on server rejection.
    /// </summary>
    public async Task ProcessPendingMutationsAsync()
    {
        await EnsureInitializedAsync();

        if (!IsConnected) return;

        // Inline CanSync expression - SQLite-net can't translate computed properties
        var pending = await _database!.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
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
                    // Success - remove from queue (rollback data is discarded with it)
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
                // Server rejected - mark as rejected and revert local changes using persisted rollback data
                mutation.IsRejected = true;
                mutation.RejectionReason = $"Server: {ex.Message}";
                mutation.LastError = ex.Message;
                await _database.UpdateAsync(mutation);

                // Revert local entry using rollback data from the mutation
                await RevertLocalEntryFromMutationAsync(mutation);

                SyncRejected?.Invoke(this, new SyncFailureEventArgs
                {
                    EntityId = Guid.Empty,
                    ErrorMessage = ex.Message,
                    IsClientError = true
                });
            }
            catch (HttpRequestException ex)
            {
                // Network error - will retry
                mutation.LastError = $"Network error: {ex.Message}";
                await _database.UpdateAsync(mutation);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Timeout - will retry
                mutation.LastError = "Request timed out";
                await _database.UpdateAsync(mutation);
            }
            catch (Exception ex)
            {
                // Unexpected error - will retry
                mutation.LastError = $"Unexpected: {ex.Message}";
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
        // Inline CanSync expression - SQLite-net can't translate computed properties
        return await _database!.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
            .CountAsync();
    }

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();
    }

    private static bool IsClientError(HttpRequestException ex)
    {
        // Check if it's a 4xx status code
        return ex.StatusCode.HasValue &&
               (int)ex.StatusCode.Value >= 400 &&
               (int)ex.StatusCode.Value < 500;
    }

    #region LocalTimelineEntry Integration (Persisted Rollback)

    /// <summary>
    /// Gets original values from LocalTimelineEntry for rollback support.
    /// </summary>
    private async Task<(int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes)> GetOriginalValuesAsync(int locationId)
    {
        var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null)
            return (null, null, null, null, null);

        return (localEntry.Id, localEntry.Latitude, localEntry.Longitude, localEntry.Timestamp, localEntry.Notes);
    }

    /// <summary>
    /// Applies an update to the local timeline entry (optimistic update).
    /// </summary>
    private async Task ApplyLocalEntryUpdateAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes)
    {
        var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        // Apply optimistic update
        if (latitude.HasValue) localEntry.Latitude = latitude.Value;
        if (longitude.HasValue) localEntry.Longitude = longitude.Value;
        if (localTimestamp.HasValue) localEntry.Timestamp = localTimestamp.Value;
        if (includeNotes) localEntry.Notes = notes;

        await _databaseService.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Enqueues a mutation with rollback data persisted in the mutation entity.
    /// </summary>
    private async Task EnqueueMutationWithRollbackAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes) originalValues)
    {
        // Check if there's already a pending mutation for this location
        var existing = await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Merge with existing mutation (latest values win, keep original rollback data)
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
                LocalEntryId = originalValues.localEntryId,
                Latitude = latitude,
                Longitude = longitude,
                LocalTimestamp = localTimestamp,
                Notes = notes,
                IncludeNotes = includeNotes,
                // Persist original values for rollback
                OriginalLatitude = originalValues.lat,
                OriginalLongitude = originalValues.lng,
                OriginalTimestamp = originalValues.timestamp,
                OriginalNotes = originalValues.notes,
                CreatedAt = DateTime.UtcNow
            };
            await _database.InsertAsync(mutation);
        }
    }

    /// <summary>
    /// Reverts local entry using provided original values.
    /// </summary>
    private async Task RevertLocalEntryFromValuesAsync(
        int locationId,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes) originalValues)
    {
        if (!originalValues.localEntryId.HasValue) return;

        var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        if (originalValues.lat.HasValue) localEntry.Latitude = originalValues.lat.Value;
        if (originalValues.lng.HasValue) localEntry.Longitude = originalValues.lng.Value;
        if (originalValues.timestamp.HasValue) localEntry.Timestamp = originalValues.timestamp.Value;
        localEntry.Notes = originalValues.notes;

        await _databaseService.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Gets the full entry serialized as JSON for delete rollback.
    /// </summary>
    private async Task<string?> GetDeletedEntryJsonAsync(int locationId)
    {
        var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return null;

        return JsonSerializer.Serialize(localEntry);
    }

    /// <summary>
    /// Deletes the local timeline entry (optimistic delete).
    /// </summary>
    private async Task ApplyLocalEntryDeleteAsync(int locationId)
    {
        var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        await _databaseService.DeleteLocalTimelineEntryAsync(localEntry.Id);
    }

    /// <summary>
    /// Enqueues a delete mutation with rollback data (full entry as JSON).
    /// </summary>
    private async Task EnqueueDeleteMutationWithRollbackAsync(int locationId, string? deletedEntryJson)
    {
        // Remove any pending updates for this location
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId)
            .DeleteAsync();

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            DeletedEntryJson = deletedEntryJson,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    /// <summary>
    /// Restores a deleted entry from JSON.
    /// </summary>
    private async Task RestoreDeletedEntryAsync(string? deletedEntryJson)
    {
        if (string.IsNullOrEmpty(deletedEntryJson)) return;

        try
        {
            var entry = JsonSerializer.Deserialize<LocalTimelineEntry>(deletedEntryJson);
            if (entry == null) return;

            entry.Id = 0; // Reset ID for new insert
            await _databaseService.InsertLocalTimelineEntryAsync(entry);
        }
        catch (JsonException)
        {
            // JSON deserialization failed - entry cannot be restored
        }
    }

    /// <summary>
    /// Reverts local entry using rollback data persisted in the mutation.
    /// </summary>
    private async Task RevertLocalEntryFromMutationAsync(PendingTimelineMutation mutation)
    {
        if (mutation.OperationType == "Delete")
        {
            // Restore deleted entry from JSON
            await RestoreDeletedEntryAsync(mutation.DeletedEntryJson);
        }
        else
        {
            // Revert updated fields
            if (!mutation.HasRollbackData) return;

            var localEntry = await _databaseService.GetLocalTimelineEntryByServerIdAsync(mutation.LocationId);
            if (localEntry == null) return;

            if (mutation.OriginalLatitude.HasValue) localEntry.Latitude = mutation.OriginalLatitude.Value;
            if (mutation.OriginalLongitude.HasValue) localEntry.Longitude = mutation.OriginalLongitude.Value;
            if (mutation.OriginalTimestamp.HasValue) localEntry.Timestamp = mutation.OriginalTimestamp.Value;
            localEntry.Notes = mutation.OriginalNotes;

            await _databaseService.UpdateLocalTimelineEntryAsync(localEntry);
        }
    }

    #endregion
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
