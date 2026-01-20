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
    /// <param name="isUserInvoked">True for manual check-ins (skip filtering, prioritize sync).</param>
    /// <param name="activityTypeId">Optional activity type ID (for user-invoked check-ins).</param>
    /// <param name="notes">Optional notes (for user-invoked check-ins).</param>
    /// <returns>The ID of the queued location.</returns>
    Task<int> QueueLocationAsync(
        LocationData location,
        bool isUserInvoked = false,
        int? activityTypeId = null,
        string? notes = null);

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

    /// <summary>
    /// Gets pending, non-rejected locations for export, ordered by Timestamp ASC, Id ASC.
    /// Excludes Synced, Syncing, and Rejected locations to prevent duplicates on re-import.
    /// </summary>
    /// <returns>Pending, non-rejected queued locations for export.</returns>
    Task<List<QueuedLocation>> GetAllQueuedLocationsForExportAsync();

    #endregion

    #region Sync Status Operations

    /// <summary>
    /// Marks a location as successfully synced.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task MarkLocationSyncedAsync(int id);

    /// <summary>
    /// Marks a location as confirmed by server (API call succeeded).
    /// Called immediately after API success, BEFORE marking as Synced.
    /// If app crashes between ServerConfirmed and Synced, crash recovery will
    /// complete the Synced transition instead of resetting to Pending.
    /// </summary>
    /// <param name="id">The location ID.</param>
    /// <param name="serverId">The server-assigned ID (for local timeline reconciliation).</param>
    Task MarkServerConfirmedAsync(int id, int? serverId = null);

    /// <summary>
    /// Marks multiple locations as successfully synced in a single batch operation.
    /// </summary>
    /// <param name="ids">The location IDs to mark as synced.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> MarkLocationsSyncedAsync(IEnumerable<int> ids);

    /// <summary>
    /// Records a sync failure and resets SyncStatus to Pending for retry.
    /// If ServerConfirmed is true, marks as Synced instead to avoid duplicate sends.
    /// Increments SyncAttempts for diagnostics when not ServerConfirmed.
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
    /// Increments the retry count and resets SyncStatus to Pending for retry.
    /// Used for transient failures (network errors, server errors) where retry is appropriate.
    /// If ServerConfirmed is true, marks as Synced instead to avoid duplicate sends.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task IncrementRetryCountAsync(int id);

    /// <summary>
    /// Resets a location back to pending status for retry after transient failures.
    /// If ServerConfirmed is true, marks as Synced instead to avoid duplicate sends.
    /// </summary>
    /// <param name="id">The location ID.</param>
    Task ResetLocationToPendingAsync(int id);

    /// <summary>
    /// Resets locations stuck in "Syncing" status back to "Pending".
    /// Called at startup to handle crash recovery.
    /// </summary>
    /// <returns>Number of locations reset.</returns>
    Task<int> ResetStuckLocationsAsync();

    /// <summary>
    /// Resets locations that have been stuck in "Syncing" status for too long.
    /// Called periodically during runtime to handle edge cases where sync operations
    /// fail to complete (e.g., network timeout without proper cleanup).
    /// </summary>
    /// <param name="stuckThresholdMinutes">Locations stuck longer than this are reset. Default 30 minutes.</param>
    /// <returns>Number of locations reset.</returns>
    Task<int> ResetTimedOutSyncingLocationsAsync(int stuckThresholdMinutes = 30);

    /// <summary>
    /// Atomically claims pending locations by marking them as Syncing and returns them.
    /// Only returns locations that were successfully claimed (prevents race conditions).
    /// </summary>
    /// <param name="limit">Maximum locations to claim.</param>
    /// <returns>List of claimed locations (already marked as Syncing).</returns>
    Task<List<QueuedLocation>> ClaimPendingLocationsAsync(int limit);

    /// <summary>
    /// Atomically claims the oldest pending location by marking it as Syncing.
    /// Used by QueueDrainService for one-at-a-time processing.
    /// Returns null if no pending locations or if another service claimed it first.
    /// </summary>
    /// <returns>The claimed location (already marked as Syncing), or null if none available.</returns>
    Task<QueuedLocation?> ClaimOldestPendingLocationAsync(int candidateLimit = 5);

    /// <summary>
    /// Claims the next pending location, prioritizing user-invoked items.
    /// User-invoked locations sync before background locations.
    /// </summary>
    /// <returns>The claimed location (already marked as Syncing), or null if none available.</returns>
    Task<QueuedLocation?> ClaimNextPendingLocationWithPriorityAsync();

    /// <summary>
    /// Resets multiple locations from Syncing back to Pending in a single batch operation.
    /// Used for failure recovery when batch sync fails or is interrupted.
    /// If ServerConfirmed is true, marks as Synced instead to avoid duplicate sends.
    /// </summary>
    /// <param name="ids">The IDs of locations to reset.</param>
    /// <returns>Number of rows updated.</returns>
    Task<int> ResetLocationsBatchToPendingAsync(IEnumerable<int> ids);

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
    /// Clears synced and rejected entries from the queue.
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    Task<int> ClearSyncedAndRejectedQueueAsync();

    /// <summary>
    /// Clears all locations from the queue (pending, synced, and failed).
    /// </summary>
    /// <returns>The number of locations deleted.</returns>
    Task<int> ClearAllQueueAsync();

    /// <summary>
    /// Enforces queue limit by removing oldest safe entries, then oldest pending if needed.
    /// Never removes Syncing entries (in-flight protection).
    /// </summary>
    /// <param name="maxQueuedLocations">The maximum number of locations to keep.</param>
    Task CleanupOldLocationsAsync(int maxQueuedLocations);

    #endregion

    #region Diagnostic Queries

    /// <summary>
    /// Gets total count of all queued locations.
    /// </summary>
    Task<int> GetTotalCountAsync();

    /// <summary>
    /// Gets the count of pending locations that can be synced (excludes rejected).
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Gets the count of entries currently syncing.
    /// </summary>
    Task<int> GetSyncingCountAsync();

    /// <summary>
    /// Gets the count of pending locations that are retrying (SyncAttempts > 0).
    /// </summary>
    Task<int> GetRetryingCountAsync();

    /// <summary>
    /// Gets the count of rejected locations (for diagnostics).
    /// </summary>
    Task<int> GetRejectedLocationCountAsync();

    /// <summary>
    /// Gets the count of synced locations (for diagnostics).
    /// </summary>
    Task<int> GetSyncedLocationCountAsync();

    /// <summary>
    /// Gets the oldest pending location (for diagnostics).
    /// </summary>
    Task<QueuedLocation?> GetOldestPendingLocationAsync();

    /// <summary>
    /// Gets the newest pending location (for coverage calculation).
    /// </summary>
    Task<QueuedLocation?> GetNewestPendingLocationAsync();

    /// <summary>
    /// Gets the last synced location (for diagnostics).
    /// </summary>
    Task<QueuedLocation?> GetLastSyncedLocationAsync();

    /// <summary>
    /// Gets confirmed entries with ServerId for crash recovery reconciliation.
    /// Returns entries where ServerConfirmed=true AND ServerId IS NOT NULL.
    /// Used by LocalTimelineStorageService to backfill missing ServerIds.
    /// </summary>
    /// <param name="sinceTimestamp">Optional: only return entries after this timestamp.</param>
    /// <returns>List of confirmed entries with ServerId.</returns>
    Task<List<QueuedLocation>> GetConfirmedEntriesWithServerIdAsync(DateTime? sinceTimestamp = null);

    /// <summary>
    /// Gets all non-rejected queue entries for local timeline backfill.
    /// Returns entries where IsRejected=false (includes Pending, Syncing, Synced).
    /// Used by LocalTimelineStorageService to backfill missed queue entries.
    /// </summary>
    /// <param name="sinceTimestamp">Optional: only return entries after this timestamp.</param>
    /// <returns>List of non-rejected queue entries ordered by timestamp.</returns>
    Task<List<QueuedLocation>> GetNonRejectedEntriesForBackfillAsync(DateTime? sinceTimestamp = null);

    #endregion
}
