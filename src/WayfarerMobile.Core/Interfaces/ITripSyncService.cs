namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for trip sync service.
/// Provides optimistic UI pattern for trip CRUD operations with offline support.
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
    /// <param name="tripId">The trip ID.</param>
    /// <param name="regionId">The optional region ID.</param>
    /// <param name="name">The place name.</param>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="iconName">Optional icon name.</param>
    /// <param name="markerColor">Optional marker color.</param>
    /// <param name="displayOrder">Optional display order.</param>
    /// <param name="clientTempId">Optional client-generated temp ID. If provided, used for reconciliation with in-memory objects.</param>
    /// <returns>The entity ID (server ID if online, temp ID if queued).</returns>
    Task<Guid> CreatePlaceAsync(
        Guid tripId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes = null,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null,
        Guid? clientTempId = null);

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
    /// <param name="tripId">The trip ID.</param>
    /// <param name="name">The region name.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="coverImageUrl">Optional cover image URL.</param>
    /// <param name="centerLatitude">Optional center latitude.</param>
    /// <param name="centerLongitude">Optional center longitude.</param>
    /// <param name="displayOrder">Optional display order.</param>
    /// <param name="clientTempId">Optional client-generated temp ID. If provided, used for reconciliation with in-memory objects.</param>
    /// <returns>The entity ID (server ID if online, temp ID if queued).</returns>
    Task<Guid> CreateRegionAsync(
        Guid tripId,
        string name,
        string? notes = null,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null,
        Guid? clientTempId = null);

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

    #region Trip Operations

    /// <summary>
    /// Update a trip's metadata (name, notes) with optimistic UI pattern.
    /// </summary>
    Task UpdateTripAsync(
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false);

    #endregion

    #region Segment Operations

    /// <summary>
    /// Update a segment's notes with optimistic UI pattern.
    /// </summary>
    Task UpdateSegmentNotesAsync(
        Guid segmentId,
        Guid tripId,
        string? notes);

    #endregion

    #region Area Operations

    /// <summary>
    /// Update an area's (polygon) notes with optimistic UI pattern.
    /// </summary>
    Task UpdateAreaNotesAsync(
        Guid tripId,
        Guid areaId,
        string? notes);

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
    /// Get count of failed mutations (exhausted retries or rejected).
    /// </summary>
    Task<int> GetFailedCountAsync();

    /// <summary>
    /// Clear rejected mutations.
    /// </summary>
    Task ClearRejectedMutationsAsync();

    /// <summary>
    /// Reset retry attempts for all failed mutations.
    /// </summary>
    Task ResetFailedMutationsAsync();

    /// <summary>
    /// Cancel all pending mutations (discard changes).
    /// </summary>
    Task CancelPendingMutationsAsync();

    /// <summary>
    /// Clears all pending mutations for a specific trip.
    /// Call this when a trip is deleted.
    /// </summary>
    Task ClearPendingMutationsForTripAsync(Guid tripId);

    /// <summary>
    /// Gets count of failed mutations for a specific trip.
    /// </summary>
    Task<int> GetFailedCountForTripAsync(Guid tripId);

    /// <summary>
    /// Resets retry attempts for a specific trip's failed mutations.
    /// </summary>
    Task ResetFailedMutationsForTripAsync(Guid tripId);

    /// <summary>
    /// Cancels pending mutations for a specific trip (discards unsynced changes).
    /// Also restores original values in offline tables.
    /// </summary>
    Task CancelPendingMutationsForTripAsync(Guid tripId);
}
