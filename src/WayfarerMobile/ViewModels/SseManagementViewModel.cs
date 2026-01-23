using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for managing SSE (Server-Sent Events) subscriptions for real-time group updates.
/// Handles location and membership event streaming with throttling and reconnection.
/// </summary>
public class SseManagementViewModel : IDisposable
{
    #region Fields

    private readonly ISseClientFactory _sseClientFactory;
    private readonly IGroupsService _groupsService;
    private readonly ILogger<SseManagementViewModel> _logger;
    private ISseManagementCallbacks? _callbacks;

    /// <summary>
    /// SSE client for consolidated group events (location + membership updates).
    /// Single client receives both location and membership events from the same stream.
    /// </summary>
    private ISseClient? _groupSseClient;

    /// <summary>
    /// Cancellation token source for SSE subscriptions.
    /// </summary>
    private CancellationTokenSource? _sseCts;

    /// <summary>
    /// Dictionary tracking last update time per user for throttling.
    /// Thread-safe for concurrent SSE event handling.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();

    /// <summary>
    /// Throttle interval in milliseconds for SSE updates.
    /// </summary>
    private const int ThrottleIntervalMs = 2000;

    /// <summary>
    /// Flag indicating if this instance has been disposed.
    /// </summary>
    private bool _isDisposed;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of SseManagementViewModel.
    /// </summary>
    /// <param name="sseClientFactory">Factory for creating SSE clients.</param>
    /// <param name="groupsService">Service for group operations.</param>
    /// <param name="logger">Logger instance.</param>
    public SseManagementViewModel(
        ISseClientFactory sseClientFactory,
        IGroupsService groupsService,
        ILogger<SseManagementViewModel> logger)
    {
        _sseClientFactory = sseClientFactory;
        _groupsService = groupsService;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the callbacks for accessing parent ViewModel state and operations.
    /// </summary>
    /// <param name="callbacks">The callback implementation.</param>
    public void SetCallbacks(ISseManagementCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Abandons SSE clients without waiting for cleanup.
    /// Just unsubscribes from events and forgets references - instant, non-blocking.
    /// Old connections will timeout naturally or be GC'd.
    /// </summary>
    public void AbandonSseClients()
    {
        _logger.LogDebug("Abandoning SSE clients (non-blocking)");

        // Unsubscribe from events immediately (prevents old events reaching handlers)
        if (_groupSseClient != null)
        {
            _groupSseClient.LocationReceived -= OnLocationReceived;
            _groupSseClient.LocationDeleted -= OnLocationDeleted;
            _groupSseClient.MembershipReceived -= OnMembershipReceived;
            _groupSseClient.InviteCreated -= OnInviteCreated;
            _groupSseClient.Connected -= OnSseConnected;
            _groupSseClient.Reconnecting -= OnSseReconnecting;
            _groupSseClient.PermanentError -= OnSsePermanentError;
        }

        // Trigger cancellation but don't wait for it (fire and forget)
        var oldCts = _sseCts;
        if (oldCts != null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { oldCts.Cancel(); oldCts.Dispose(); }
                catch { /* ignore cleanup errors */ }
            });
        }

        // Clear references - old client becomes orphaned, will be GC'd eventually
        _sseCts = null;
        _groupSseClient = null;

        // Clear throttle tracking
        _lastUpdateTimes.Clear();
    }

    /// <summary>
    /// Ensures SSE client exists. Only starts new client if null.
    /// SSE client has auto-reconnect, so we don't restart based on IsConnected.
    /// Called when navigating back to today's date.
    /// </summary>
    public async Task EnsureSseConnectedAsync()
    {
        // Check if SSE client already exists (it has auto-reconnect built in)
        // Don't check IsConnected - it may be temporarily false during reconnection
        if (_groupSseClient != null)
        {
            _logger.LogDebug("SSE client exists, skipping start (auto-reconnect handles connection)");
            return;
        }

        _logger.LogDebug("SSE client is null, starting new subscription");
        // Start SSE only if client doesn't exist
        await StartSseSubscriptionsAsync();
    }

    /// <summary>
    /// Starts SSE subscription for the selected group.
    /// Uses consolidated endpoint that delivers both location and membership events.
    /// </summary>
    public async Task StartSseSubscriptionsAsync()
    {
        if (_callbacks == null)
            return;

        var groupId = _callbacks.SelectedGroupId;
        if (groupId == null || !_callbacks.IsToday)
            return;

        // Abandon any existing subscription (instant, non-blocking)
        AbandonSseClients();

        _sseCts = new CancellationTokenSource();
        var groupIdString = groupId.Value.ToString();

        // Create single SSE client for consolidated group stream
        _groupSseClient = _sseClientFactory.Create();
        _groupSseClient.LocationReceived += OnLocationReceived;
        _groupSseClient.LocationDeleted += OnLocationDeleted;
        _groupSseClient.MembershipReceived += OnMembershipReceived;
        _groupSseClient.InviteCreated += OnInviteCreated;
        _groupSseClient.Connected += OnSseConnected;
        _groupSseClient.Reconnecting += OnSseReconnecting;
        _groupSseClient.PermanentError += OnSsePermanentError;

        _logger.LogDebug("Starting SSE subscription for group {GroupId}", groupIdString);

        // Start subscription in background (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _groupSseClient.SubscribeToGroupAsync(groupIdString, _sseCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Group SSE subscription cancelled");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogNetworkWarningIfOnline("Network error in SSE subscription: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SSE subscription");
            }
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops SSE subscription.
    /// Non-blocking: cancels immediately, cleanup runs in background.
    /// </summary>
    public void StopSseSubscriptions()
    {
        _logger.LogDebug("Stopping SSE subscription");

        // Cancel ongoing operations immediately (non-blocking)
        _sseCts?.Cancel();

        // Capture references for background cleanup
        var oldCts = _sseCts;
        var oldGroupClient = _groupSseClient;

        // Clear references immediately so new subscriptions can start
        _sseCts = null;
        _groupSseClient = null;

        // Unsubscribe from events on main thread to prevent race conditions
        if (oldGroupClient != null)
        {
            oldGroupClient.LocationReceived -= OnLocationReceived;
            oldGroupClient.LocationDeleted -= OnLocationDeleted;
            oldGroupClient.MembershipReceived -= OnMembershipReceived;
            oldGroupClient.InviteCreated -= OnInviteCreated;
            oldGroupClient.Connected -= OnSseConnected;
            oldGroupClient.Reconnecting -= OnSseReconnecting;
            oldGroupClient.PermanentError -= OnSsePermanentError;
        }

        // Dispose in background to avoid blocking main thread
        // HttpClient cleanup on Android can take 2+ seconds per connection
        _ = Task.Run(() =>
        {
            try
            {
                oldGroupClient?.Stop();
                oldGroupClient?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE client cleanup error: {Message}", ex.Message);
            }

            try
            {
                oldCts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE CTS cleanup error: {Message}", ex.Message);
            }
        });

        // Clear throttle tracking
        _lastUpdateTimes.Clear();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles location received events from SSE with throttling.
    /// </summary>
    private async void OnLocationReceived(object? sender, SseLocationEventArgs e)
    {
        // Guard against events firing after disposal
        if (_isDisposed || _callbacks == null || _callbacks.IsDisposed)
            return;

        try
        {
            // Skip live updates when viewing historical data
            if (!_callbacks.IsToday)
            {
                _logger.LogDebug("SSE update skipped - viewing historical date");
                return;
            }

            var userId = e.Location.UserId;
            var now = DateTime.UtcNow;

            // Throttle updates - only process if enough time has passed
            if (_lastUpdateTimes.TryGetValue(userId, out var lastUpdate))
            {
                var elapsed = (now - lastUpdate).TotalMilliseconds;
                if (elapsed < ThrottleIntervalMs)
                {
                    _logger.LogDebug("SSE update throttled for {UserId} ({Elapsed}ms since last)", userId, elapsed);
                    return;
                }
            }

            _lastUpdateTimes[userId] = now;
            _logger.LogDebug("SSE location received for {UserName}", e.Location.UserName);

            // Refresh the specific member's location
            await RefreshMemberLocationAsync(userId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error handling SSE location event: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling SSE location event");
        }
    }

    /// <summary>
    /// Handles location deleted events from SSE.
    /// When a location is deleted, refresh the member's data to get their new latest location.
    /// </summary>
    private async void OnLocationDeleted(object? sender, SseLocationDeletedEventArgs e)
    {
        // Guard against events firing after disposal
        if (_isDisposed || _callbacks == null || _callbacks.IsDisposed)
            return;

        try
        {
            // Skip updates when viewing historical data
            if (!_callbacks.IsToday)
            {
                _logger.LogDebug("SSE location deleted skipped - viewing historical date");
                return;
            }

            var userId = e.LocationDeleted.UserId;
            _logger.LogDebug("SSE location deleted: {LocationId} for user {UserId}",
                e.LocationDeleted.LocationId, userId);

            // Refresh the specific member's location to get their new latest
            await RefreshMemberLocationAsync(userId);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error handling SSE location deleted event: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling SSE location deleted event");
        }
    }

    /// <summary>
    /// Handles membership events from SSE (peer visibility changes, member removal).
    /// </summary>
    private async void OnMembershipReceived(object? sender, SseMembershipEventArgs e)
    {
        // Guard against events firing after disposal
        if (_isDisposed || _callbacks == null || _callbacks.IsDisposed)
            return;

        try
        {
            // Skip membership updates when viewing historical data
            if (!_callbacks.IsToday)
            {
                _logger.LogDebug("SSE membership update skipped - viewing historical date");
                return;
            }

            var membership = e.Membership;
            _logger.LogDebug("SSE membership event: {Action} for {UserId}", membership.Action, membership.UserId);

            switch (membership.Action)
            {
                case "visibility-changed":
                    await HandlePeerVisibilityChangedAsync(membership.UserId, membership.Disabled ?? false);
                    break;

                case "member-removed":
                case "member-left":
                    await HandleMemberRemovedAsync(membership.UserId);
                    break;

                case "member-joined":
                    // Reload members to show the new member
                    await _callbacks.LoadMembersAsync();
                    break;

                case "invite-declined":
                case "invite-revoked":
                    // These are informational - no UI action needed
                    _logger.LogDebug("Invite event: {Action}", membership.Action);
                    break;

                default:
                    _logger.LogDebug("Unhandled membership action: {Action}", membership.Action);
                    break;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error handling SSE membership event: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling SSE membership event");
        }
    }

    /// <summary>
    /// Handles invite created events from SSE.
    /// Currently logs the event; future implementation could refresh pending invitations UI.
    /// </summary>
    private void OnInviteCreated(object? sender, SseInviteCreatedEventArgs e)
    {
        // Guard against events firing after disposal
        if (_isDisposed || _callbacks == null || _callbacks.IsDisposed)
            return;

        _logger.LogDebug("SSE invite created: {InvitationId}", e.InviteCreated.InvitationId);
        // Future: Could refresh pending invitations list if UI is added
    }

    /// <summary>
    /// Handles SSE connected event.
    /// </summary>
    private void OnSseConnected(object? sender, EventArgs e)
    {
        _logger.LogDebug("SSE connected");
    }

    /// <summary>
    /// Handles SSE reconnecting event.
    /// </summary>
    private void OnSseReconnecting(object? sender, SseReconnectEventArgs e)
    {
        _logger.LogDebug("SSE reconnecting (attempt {Attempt}, delay {DelayMs}ms)", e.Attempt, e.DelayMs);
    }

    /// <summary>
    /// Handles SSE permanent error event (401/403/404).
    /// Stops retrying since these errors won't resolve by retrying.
    /// </summary>
    private void OnSsePermanentError(object? sender, SsePermanentErrorEventArgs e)
    {
        _logger.LogWarning(
            "SSE permanent error (stopping): {StatusCode} - {Message}",
            e.StatusCode, e.Message);

        // Don't try to reconnect - the error is permanent
        // User will need to re-authenticate or check server configuration
        AbandonSseClients();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Handles peer visibility change events.
    /// </summary>
    private async Task HandlePeerVisibilityChangedAsync(string? userId, bool isDisabled)
    {
        if (string.IsNullOrEmpty(userId) || _callbacks == null)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var member = _callbacks.Members.FirstOrDefault(m => m.UserId == userId);
            if (member != null)
            {
                member.OrgPeerVisibilityAccessDisabled = isDisabled;

                // Update current user state if this is us
                if (member.IsSelf)
                {
                    _callbacks.MyPeerVisibilityDisabled = isDisabled;
                }

                // If another member disabled visibility, remove their location
                if (!member.IsSelf && isDisabled)
                {
                    member.LastLocation = null;
                }

                // Update map markers
                if (_callbacks.IsMapView)
                {
                    _callbacks.UpdateMapMarkers();
                }

                _logger.LogDebug("Updated peer visibility for {UserId}: disabled={Disabled}", userId, isDisabled);
            }
        });
    }

    /// <summary>
    /// Handles member removal events.
    /// </summary>
    private async Task HandleMemberRemovedAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId) || _callbacks == null)
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var member = _callbacks.Members.FirstOrDefault(m => m.UserId == userId);
            if (member != null)
            {
                _callbacks.Members.Remove(member);

                // Update map markers
                if (_callbacks.IsMapView)
                {
                    _callbacks.UpdateMapMarkers();
                }

                _logger.LogDebug("Removed member {UserId} from group", userId);
            }
        });
    }

    /// <summary>
    /// Refreshes a specific member's location.
    /// </summary>
    private async Task RefreshMemberLocationAsync(string userId)
    {
        if (_callbacks == null)
            return;

        var groupId = _callbacks.SelectedGroupId;
        if (groupId == null)
            return;

        try
        {
            var locations = await _groupsService.GetLatestLocationsAsync(
                groupId.Value,
                new List<string> { userId });

            if (locations.TryGetValue(userId, out var location))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var member = _callbacks.Members.FirstOrDefault(m => m.UserId == userId);
                    if (member != null)
                    {
                        member.LastLocation = location;

                        // Update map markers
                        if (_callbacks.IsMapView)
                        {
                            _callbacks.UpdateMapMarkers();
                        }
                    }
                });
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error refreshing location for {UserId}: {Message}", userId, ex.Message);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Request timed out refreshing location for {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error refreshing location for {UserId}", userId);
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the SSE management resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopSseSubscriptions();
        GC.SuppressFinalize(this);
    }

    #endregion
}
