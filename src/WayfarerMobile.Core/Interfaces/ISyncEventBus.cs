namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Centralized event bus for sync-related events.
/// Decouples sync services from ViewModels, allowing multiple subscribers.
/// When TripSyncService is split, extracted services publish here.
/// </summary>
public interface ISyncEventBus
{
    #region Sync Status Events

    /// <summary>
    /// Event raised when a sync operation succeeds.
    /// </summary>
    event EventHandler<SyncSuccessEventArgs>? SyncSucceeded;

    /// <summary>
    /// Event raised when a sync operation fails.
    /// </summary>
    event EventHandler<SyncFailureEventArgs>? SyncFailed;

    /// <summary>
    /// Event raised when an operation is queued for later sync (offline).
    /// </summary>
    event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <summary>
    /// Event raised when a new entity is created with a server-assigned ID.
    /// </summary>
    event EventHandler<EntityCreatedEventArgs>? EntityCreated;

    #endregion

    #region Trip-Specific Events

    /// <summary>
    /// Event raised when trips are updated (added, modified, or deleted).
    /// </summary>
    event EventHandler<TripsUpdatedEventArgs>? TripsUpdated;

    /// <summary>
    /// Event raised when a specific trip's data changes.
    /// </summary>
    event EventHandler<TripDataChangedEventArgs>? TripDataChanged;

    #endregion

    #region Connectivity Events

    /// <summary>
    /// Event raised when sync connectivity status changes.
    /// </summary>
    event EventHandler<SyncConnectivityEventArgs>? ConnectivityChanged;

    #endregion

    #region Publish Methods

    /// <summary>
    /// Publishes a sync success event.
    /// </summary>
    void PublishSyncSuccess(SyncSuccessEventArgs args);

    /// <summary>
    /// Publishes a sync failure event.
    /// </summary>
    void PublishSyncFailure(SyncFailureEventArgs args);

    /// <summary>
    /// Publishes a sync queued event.
    /// </summary>
    void PublishSyncQueued(SyncQueuedEventArgs args);

    /// <summary>
    /// Publishes an entity created event.
    /// </summary>
    void PublishEntityCreated(EntityCreatedEventArgs args);

    /// <summary>
    /// Publishes a trips updated event.
    /// </summary>
    void PublishTripsUpdated(TripsUpdatedEventArgs args);

    /// <summary>
    /// Publishes a trip data changed event.
    /// </summary>
    void PublishTripDataChanged(TripDataChangedEventArgs args);

    /// <summary>
    /// Publishes a connectivity changed event.
    /// </summary>
    void PublishConnectivityChanged(SyncConnectivityEventArgs args);

    #endregion
}

/// <summary>
/// Event args for trips list updates.
/// </summary>
public class TripsUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the type of update.
    /// </summary>
    public TripsUpdateType UpdateType { get; init; }

    /// <summary>
    /// Gets the affected trip IDs.
    /// </summary>
    public IReadOnlyList<Guid> AffectedTripIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Gets optional source identifier for debugging.
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// Type of trips update.
/// </summary>
public enum TripsUpdateType
{
    /// <summary>Full refresh needed.</summary>
    FullRefresh,

    /// <summary>Trips were added.</summary>
    Added,

    /// <summary>Trips were modified.</summary>
    Modified,

    /// <summary>Trips were deleted.</summary>
    Deleted
}

/// <summary>
/// Event args for single trip data changes.
/// </summary>
public class TripDataChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the trip ID that changed.
    /// </summary>
    public Guid TripId { get; init; }

    /// <summary>
    /// Gets the type of data that changed.
    /// </summary>
    public TripDataChangeType ChangeType { get; init; }

    /// <summary>
    /// Gets additional context about the change.
    /// </summary>
    public string? Context { get; init; }
}

/// <summary>
/// Type of trip data change.
/// </summary>
public enum TripDataChangeType
{
    /// <summary>Trip metadata changed (name, description, etc.).</summary>
    Metadata,

    /// <summary>Trip places changed.</summary>
    Places,

    /// <summary>Trip segments changed.</summary>
    Segments,

    /// <summary>Trip was downloaded for offline use.</summary>
    Downloaded,

    /// <summary>Trip download was deleted.</summary>
    DownloadDeleted,

    /// <summary>Trip notes changed.</summary>
    Notes
}

/// <summary>
/// Event args for sync connectivity changes.
/// </summary>
public class SyncConnectivityEventArgs : EventArgs
{
    /// <summary>
    /// Gets whether sync services are currently connected/online.
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>
    /// Gets the reason for the connectivity state.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the number of pending sync operations, if known.
    /// </summary>
    public int? PendingOperations { get; init; }
}
