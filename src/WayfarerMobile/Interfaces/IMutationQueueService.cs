using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for managing the offline mutation queue.
/// Handles queuing, retrieval, and lifecycle of pending sync mutations.
/// </summary>
public interface IMutationQueueService
{
    #region Queue Status

    /// <summary>
    /// Gets the count of pending mutations that can still be synced.
    /// </summary>
    Task<int> GetPendingCountAsync();

    /// <summary>
    /// Gets the count of failed mutations (exhausted retries or rejected).
    /// </summary>
    Task<int> GetFailedCountAsync();

    /// <summary>
    /// Gets the count of failed mutations for a specific trip.
    /// </summary>
    Task<int> GetFailedCountForTripAsync(Guid tripId);

    /// <summary>
    /// Gets pending mutations for a specific trip that need attention (failed or rejected).
    /// </summary>
    Task<List<PendingTripMutation>> GetFailedMutationsForTripAsync(Guid tripId);

    #endregion

    #region Queue Management

    /// <summary>
    /// Clears rejected mutations (user acknowledged the rejections).
    /// </summary>
    Task ClearRejectedMutationsAsync();

    /// <summary>
    /// Resets retry attempts for all failed mutations, allowing them to be retried.
    /// </summary>
    Task ResetFailedMutationsAsync();

    /// <summary>
    /// Resets retry attempts for a specific trip's failed mutations.
    /// </summary>
    Task ResetFailedMutationsForTripAsync(Guid tripId);

    /// <summary>
    /// Cancels all pending mutations and restores original values.
    /// </summary>
    Task CancelPendingMutationsAsync();

    /// <summary>
    /// Cancels pending mutations for a specific trip and restores original values.
    /// </summary>
    Task CancelPendingMutationsForTripAsync(Guid tripId);

    /// <summary>
    /// Clears all pending mutations for a specific trip without restoration.
    /// Use when a trip is deleted and restoration is not needed.
    /// </summary>
    Task ClearPendingMutationsForTripAsync(Guid tripId);

    #endregion

    #region Queue Retrieval

    /// <summary>
    /// Gets all pending mutations that can be synced.
    /// </summary>
    Task<List<PendingTripMutation>> GetPendingMutationsAsync();

    /// <summary>
    /// Gets all mutations (for debugging/diagnostics).
    /// </summary>
    Task<List<PendingTripMutation>> GetAllMutationsAsync();

    #endregion

    #region Mutation Lifecycle

    /// <summary>
    /// Marks a mutation as rejected by the server.
    /// </summary>
    Task MarkMutationRejectedAsync(int mutationId, string reason);

    /// <summary>
    /// Increments the sync attempt count for a mutation.
    /// </summary>
    Task IncrementSyncAttemptAsync(int mutationId, string? errorMessage = null);

    /// <summary>
    /// Deletes a mutation after successful sync.
    /// </summary>
    Task DeleteMutationAsync(int mutationId);

    #endregion

    #region Restoration

    /// <summary>
    /// Restores original values from a mutation to the offline tables.
    /// Called when canceling mutations or when server rejects a sync.
    /// </summary>
    Task RestoreOriginalValuesAsync(PendingTripMutation mutation);

    #endregion
}
