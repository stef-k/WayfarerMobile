using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of ITripSyncService for testing.
/// Captures all sync operations and provides configurable behavior.
/// </summary>
public class MockTripSyncService : ITripSyncService
{
    private readonly List<SyncOperation> _operations = new();
    private int _pendingCount;
    private int _failedCount;

    /// <inheritdoc/>
    public event EventHandler<SyncFailureEventArgs>? SyncRejected;

    /// <inheritdoc/>
    public event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <inheritdoc/>
    public event EventHandler<SyncSuccessEventArgs>? SyncCompleted;

    /// <inheritdoc/>
    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;

    /// <summary>
    /// Gets all recorded operations.
    /// </summary>
    public IReadOnlyList<SyncOperation> Operations => _operations;

    /// <summary>
    /// Sets the pending count to return.
    /// </summary>
    public void SetPendingCount(int count) => _pendingCount = count;

    /// <summary>
    /// Sets the failed count to return.
    /// </summary>
    public void SetFailedCount(int count) => _failedCount = count;

    /// <summary>
    /// Raises the SyncCompleted event.
    /// </summary>
    public void RaiseSyncCompleted(Guid entityId) =>
        SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = entityId });

    /// <summary>
    /// Raises the SyncRejected event.
    /// </summary>
    public void RaiseSyncRejected(Guid entityId, string error, bool isClientError = false, string? entityType = null) =>
        SyncRejected?.Invoke(this, new SyncFailureEventArgs
        {
            EntityId = entityId,
            ErrorMessage = error,
            IsClientError = isClientError,
            EntityType = entityType
        });

    /// <summary>
    /// Raises the EntityCreated event.
    /// </summary>
    public void RaiseEntityCreated(Guid tempId, Guid serverId, string entityType) =>
        EntityCreated?.Invoke(this, new EntityCreatedEventArgs
        {
            TempClientId = tempId,
            ServerId = serverId,
            EntityType = entityType
        });

    #region Place Operations

    /// <inheritdoc/>
    public Task<Guid> CreatePlaceAsync(Guid tripId, Guid? regionId, string name,
        double latitude, double longitude, string? notes = null,
        string? iconName = null, string? markerColor = null, int? displayOrder = null,
        Guid? clientTempId = null)
    {
        // Use client's temp ID if provided, otherwise generate a new one
        var id = clientTempId ?? Guid.NewGuid();
        _operations.Add(new SyncOperation("CreatePlace", tripId, id));
        return Task.FromResult(id);
    }

    /// <inheritdoc/>
    public Task UpdatePlaceAsync(Guid placeId, Guid tripId, string? name = null,
        double? latitude = null, double? longitude = null, string? notes = null,
        bool includeNotes = false, string? iconName = null, string? markerColor = null,
        int? displayOrder = null, Guid? regionId = null)
    {
        _operations.Add(new SyncOperation("UpdatePlace", tripId, placeId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeletePlaceAsync(Guid placeId, Guid tripId)
    {
        _operations.Add(new SyncOperation("DeletePlace", tripId, placeId));
        return Task.CompletedTask;
    }

    #endregion

    #region Region Operations

    /// <inheritdoc/>
    public Task<Guid> CreateRegionAsync(Guid tripId, string name, string? notes = null,
        string? coverImageUrl = null, double? centerLatitude = null,
        double? centerLongitude = null, int? displayOrder = null,
        Guid? clientTempId = null)
    {
        // Use client's temp ID if provided, otherwise generate a new one
        var id = clientTempId ?? Guid.NewGuid();
        _operations.Add(new SyncOperation("CreateRegion", tripId, id));
        return Task.FromResult(id);
    }

    /// <inheritdoc/>
    public Task UpdateRegionAsync(Guid regionId, Guid tripId, string? name = null,
        string? notes = null, bool includeNotes = false, string? coverImageUrl = null,
        double? centerLatitude = null, double? centerLongitude = null,
        int? displayOrder = null)
    {
        _operations.Add(new SyncOperation("UpdateRegion", tripId, regionId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteRegionAsync(Guid regionId, Guid tripId)
    {
        _operations.Add(new SyncOperation("DeleteRegion", tripId, regionId));
        return Task.CompletedTask;
    }

    #endregion

    #region Trip Operations

    /// <inheritdoc/>
    public Task UpdateTripAsync(Guid tripId, string? name = null, string? notes = null,
        bool includeNotes = false)
    {
        _operations.Add(new SyncOperation("UpdateTrip", tripId, tripId));
        return Task.CompletedTask;
    }

    #endregion

    #region Segment/Area Operations

    /// <inheritdoc/>
    public Task UpdateSegmentNotesAsync(Guid segmentId, Guid tripId, string? notes)
    {
        _operations.Add(new SyncOperation("UpdateSegmentNotes", tripId, segmentId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAreaNotesAsync(Guid tripId, Guid areaId, string? notes)
    {
        _operations.Add(new SyncOperation("UpdateAreaNotes", tripId, areaId));
        return Task.CompletedTask;
    }

    #endregion

    #region Queue Operations

    /// <inheritdoc/>
    public Task ProcessPendingMutationsAsync()
    {
        _operations.Add(new SyncOperation("ProcessPending", Guid.Empty, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetPendingCountAsync() => Task.FromResult(_pendingCount);

    /// <inheritdoc/>
    public Task<int> GetFailedCountAsync() => Task.FromResult(_failedCount);

    /// <inheritdoc/>
    public Task ClearRejectedMutationsAsync()
    {
        _operations.Add(new SyncOperation("ClearRejected", Guid.Empty, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResetFailedMutationsAsync()
    {
        _operations.Add(new SyncOperation("ResetFailed", Guid.Empty, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelPendingMutationsAsync()
    {
        _operations.Add(new SyncOperation("CancelPending", Guid.Empty, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearPendingMutationsForTripAsync(Guid tripId)
    {
        _operations.Add(new SyncOperation("ClearPendingForTrip", tripId, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetFailedCountForTripAsync(Guid tripId) => Task.FromResult(0);

    /// <inheritdoc/>
    public Task ResetFailedMutationsForTripAsync(Guid tripId)
    {
        _operations.Add(new SyncOperation("ResetFailedForTrip", tripId, Guid.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelPendingMutationsForTripAsync(Guid tripId)
    {
        _operations.Add(new SyncOperation("CancelPendingForTrip", tripId, Guid.Empty));
        return Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// Clears all recorded operations.
    /// </summary>
    public void Reset()
    {
        _operations.Clear();
        _pendingCount = 0;
        _failedCount = 0;
    }
}

/// <summary>
/// Record of a sync operation.
/// </summary>
public record SyncOperation(string Type, Guid TripId, Guid EntityId);
