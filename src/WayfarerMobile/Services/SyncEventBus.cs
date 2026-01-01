using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Thread-safe implementation of <see cref="ISyncEventBus"/>.
/// Centralizes sync-related events from multiple services.
/// Registered as singleton in DI.
/// </summary>
public class SyncEventBus : ISyncEventBus
{
    private readonly ILogger<SyncEventBus> _logger;

    /// <inheritdoc/>
    public event EventHandler<SyncSuccessEventArgs>? SyncSucceeded;

    /// <inheritdoc/>
    public event EventHandler<SyncFailureEventArgs>? SyncFailed;

    /// <inheritdoc/>
    public event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <inheritdoc/>
    public event EventHandler<EntityCreatedEventArgs>? EntityCreated;

    /// <inheritdoc/>
    public event EventHandler<TripsUpdatedEventArgs>? TripsUpdated;

    /// <inheritdoc/>
    public event EventHandler<TripDataChangedEventArgs>? TripDataChanged;

    /// <inheritdoc/>
    public event EventHandler<SyncConnectivityEventArgs>? ConnectivityChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncEventBus"/> class.
    /// </summary>
    public SyncEventBus(ILogger<SyncEventBus> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void PublishSyncSuccess(SyncSuccessEventArgs args)
    {
        _logger.LogDebug("Sync succeeded: Entity {EntityId}", args.EntityId);
        RaiseEvent(SyncSucceeded, args);
    }

    /// <inheritdoc/>
    public void PublishSyncFailure(SyncFailureEventArgs args)
    {
        _logger.LogWarning(
            "Sync failed: Entity {EntityId} - {Error} (ClientError: {IsClientError})",
            args.EntityId,
            args.ErrorMessage,
            args.IsClientError);
        RaiseEvent(SyncFailed, args);
    }

    /// <inheritdoc/>
    public void PublishSyncQueued(SyncQueuedEventArgs args)
    {
        _logger.LogDebug("Sync queued: Entity {EntityId} - {Message}", args.EntityId, args.Message);
        RaiseEvent(SyncQueued, args);
    }

    /// <inheritdoc/>
    public void PublishEntityCreated(EntityCreatedEventArgs args)
    {
        _logger.LogInformation(
            "Entity created: {EntityType} TempId={TempId} ServerId={ServerId}",
            args.EntityType,
            args.TempClientId,
            args.ServerId);
        RaiseEvent(EntityCreated, args);
    }

    /// <inheritdoc/>
    public void PublishTripsUpdated(TripsUpdatedEventArgs args)
    {
        _logger.LogDebug(
            "Trips updated: {UpdateType} - {Count} trips (Source: {Source})",
            args.UpdateType,
            args.AffectedTripIds.Count,
            args.Source ?? "unknown");
        RaiseEvent(TripsUpdated, args);
    }

    /// <inheritdoc/>
    public void PublishTripDataChanged(TripDataChangedEventArgs args)
    {
        _logger.LogDebug(
            "Trip data changed: {TripId} - {ChangeType} ({Context})",
            args.TripId,
            args.ChangeType,
            args.Context ?? "no context");
        RaiseEvent(TripDataChanged, args);
    }

    /// <inheritdoc/>
    public void PublishConnectivityChanged(SyncConnectivityEventArgs args)
    {
        _logger.LogInformation(
            "Sync connectivity: {Status} - {Reason} (Pending: {Pending})",
            args.IsConnected ? "Connected" : "Disconnected",
            args.Reason ?? "no reason",
            args.PendingOperations?.ToString() ?? "unknown");
        RaiseEvent(ConnectivityChanged, args);
    }

    /// <summary>
    /// Raises an event on the main thread for UI safety.
    /// </summary>
    private void RaiseEvent<T>(EventHandler<T>? handler, T args)
    {
        if (handler == null)
            return;

        if (MainThread.IsMainThread)
        {
            handler.Invoke(this, args);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    handler.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in sync event handler for {EventType}", typeof(T).Name);
                }
            });
        }
    }
}
