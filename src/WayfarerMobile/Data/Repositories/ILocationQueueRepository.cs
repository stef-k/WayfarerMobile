using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for location queue operations.
/// Manages GPS location capture, sync queue, and purge operations.
/// </summary>
public interface ILocationQueueRepository
{
    #region Queue Operations

    /// <summary>
    /// Queues a location for server synchronization.
    /// </summary>
    /// <param name="location">The location data to queue.</param>
    Task QueueLocationAsync(LocationData location);

    /// <summary>
    /// Gets all pending locations for synchronization.
    /// Excludes rejected locations (they should not be retried).
    /// </summary>
    /// <param name="limit">Maximum number of locations to retrieve.</param>
    /// <returns>List of pending locations.</returns>
    Task<List<QueuedLocation>> GetPendingLocationsAsync(int limit = 100);

    /// <summary>
    /// Gets the oldest pending location for queue drain processing.
    /// </summary>
    /// <returns>The oldest pending location or null if queue is empty.</returns>
    Task<QueuedLocation?> GetOldestPendingForDrainAsync();

    /// <summary>
    /// Gets all locations for a specific date.
    /// </summary>
    /// <param name="date">The date to retrieve locations for.</param>
    /// <returns>List of locations for that date.</returns>
    Task<List<QueuedLocation>> GetLocationsForDateAsync(DateTime date);

    /// <summary>
    /// Gets all queued locations for export, ordered by timestamp descending.
    /// </summary>
    /// <returns>All queued locations regardless of sync status.</returns>
    Task<List<QueuedLocation>> GetAllQueuedLocationsAsync();

    #endregion

    #region Sync Status Operations

    /// <summary>
    /// Marks a location as successfully synced.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task MarkLocationSyncedAsync(int id);

    /// <summary>
    /// Marks multiple locations as successfully synced in a single batch operation.
    /// </summary>
    /// <param name="ids">The location IDs to mark as synced.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> MarkLocationsSyncedAsync(IEnumerable<int> ids);

    /// <summary>
    /// Records a sync failure for diagnostics. Location stays Pending for retry.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="error">The error message.</param>
    Task MarkLocationFailedAsync(int id, string error);

    /// <summary>
    /// Marks a location as rejected (by client threshold check or server).
    /// Rejected locations should not be retried.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="reason">The rejection reason.</param>
    Task MarkLocationRejectedAsync(int id, string reason);

    /// <summary>
    /// Marks a location as currently syncing.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task MarkLocationSyncingAsync(int id);

    /// <summary>
    /// Increments the retry count for a location without marking it as failed.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task IncrementRetryCountAsync(int id);

    /// <summary>
    /// Resets a location back to pending status for retry after transient failures.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task ResetLocationToPendingAsync(int id);

    /// <summary>
    /// Resets locations stuck in "Syncing" status back to "Pending".
    /// </summary>
    /// <returns>Number of locations reset.</returns>
    Task<int> ResetStuckLocationsAsync();

    #endregion

    #region Cleanup Operations

    /// <summary>
    /// Removes synced locations older than the specified days.
    /// </summary>
    /// <param name="daysOld">Number of days old.</param>
    /// <returns>The number of locations deleted.</returns>
    Task<int> PurgeSyncedLocationsAsync(int daysOld = 7);

    /// <summary>
    /// Clears all pending locations from the queue.
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    Task<int> ClearPendingQueueAsync();

    /// <summary>
    /// Clears all synced locations from the queue.
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    Task<int> ClearSyncedQueueAsync();

    /// <summary>
    /// Clears all locations from the queue (pending, synced, and failed).
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    Task<int> ClearAllQueueAsync();

    #endregion

    #region Diagnostic Queries

    /// <summary>
    /// Gets the count of pending locations that can be synced.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Gets the count of pending locations (for diagnostics).
    /// </summary>
    Task<int> GetPendingLocationCountAsync();

    /// <summary>
    /// Gets the count of rejected locations (for diagnostics).
    /// </summary>
    Task<int> GetRejectedLocationCountAsync();

    /// <summary>
    /// Gets the count of synced locations (for diagnostics).
    /// </summary>
    Task<int> GetSyncedLocationCountAsync();

    /// <summary>
    /// Gets the count of failed locations (for diagnostics).
    /// </summary>
    Task<int> GetFailedLocationCountAsync();

    /// <summary>
    /// Gets the oldest pending location (for diagnostics).
    /// </summary>
    Task<QueuedLocation?> GetOldestPendingLocationAsync();

    /// <summary>
    /// Gets the last synced location (for diagnostics).
    /// </summary>
    Task<QueuedLocation?> GetLastSyncedLocationAsync();

    #endregion
}
