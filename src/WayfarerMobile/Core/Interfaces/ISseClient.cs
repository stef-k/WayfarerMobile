using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for Server-Sent Events (SSE) client.
/// Supports subscribing to real-time location and membership updates.
/// </summary>
public interface ISseClient : IDisposable
{
    #region Properties

    /// <summary>
    /// Whether the client is currently connected and streaming.
    /// </summary>
    bool IsConnected { get; }

    #endregion

    #region Events

    /// <summary>
    /// Fired when a location update is received from the SSE stream.
    /// </summary>
    event EventHandler<SseLocationEventArgs>? LocationReceived;

    /// <summary>
    /// Fired when a location is deleted.
    /// </summary>
    event EventHandler<SseLocationDeletedEventArgs>? LocationDeleted;

    /// <summary>
    /// Fired when a membership update is received from the SSE stream.
    /// </summary>
    event EventHandler<SseMembershipEventArgs>? MembershipReceived;

    /// <summary>
    /// Fired when an invitation is created.
    /// </summary>
    event EventHandler<SseInviteCreatedEventArgs>? InviteCreated;

    /// <summary>
    /// Fired when a heartbeat comment is received (connection alive).
    /// </summary>
    event EventHandler? HeartbeatReceived;

    /// <summary>
    /// Fired when the connection is established.
    /// </summary>
    event EventHandler? Connected;

    /// <summary>
    /// Fired when the connection is lost and attempting to reconnect.
    /// </summary>
    event EventHandler<SseReconnectEventArgs>? Reconnecting;

    #endregion

    #region Methods

    /// <summary>
    /// Subscribe to per-user SSE channel for location updates.
    /// </summary>
    /// <param name="userName">Username to subscribe to.</param>
    /// <param name="cancellationToken">Token to cancel the subscription.</param>
    /// <returns>Task that completes when subscription ends.</returns>
    Task SubscribeToUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to consolidated group SSE channel for location and membership updates.
    /// All event types (location, visibility-changed, member-left, etc.) come through this single stream.
    /// </summary>
    /// <param name="groupId">Group ID to subscribe to.</param>
    /// <param name="cancellationToken">Token to cancel the subscription.</param>
    /// <returns>Task that completes when subscription ends.</returns>
    Task SubscribeToGroupAsync(string groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the current SSE subscription.
    /// </summary>
    void Stop();

    #endregion
}
