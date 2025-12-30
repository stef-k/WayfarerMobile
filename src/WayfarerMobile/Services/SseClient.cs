using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Exception thrown when SSE encounters a permanent error that should not be retried.
/// Examples: 401 Unauthorized, 403 Forbidden, 404 Not Found.
/// </summary>
public class SsePermanentErrorException : Exception
{
    /// <summary>HTTP status code that caused the error.</summary>
    public int StatusCode { get; }

    /// <summary>Creates a new instance.</summary>
    public SsePermanentErrorException(int statusCode, string message)
        : base($"SSE permanent error ({statusCode}): {message}")
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Client for subscribing to Server-Sent Events (SSE) location updates.
/// Supports per-user and group-level channels with automatic reconnection.
/// </summary>
public class SseClient : ISseClient
{
    #region Fields

    private readonly ISettingsService _settings;
    private readonly ILogger<SseClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly object _connectionLock = new();
    private bool _disposed;
    private volatile bool _isConnected;

    /// <summary>
    /// Active response stream for force-close on cancellation.
    /// CancellationToken.Cancel() does NOT interrupt active stream reads in .NET.
    /// We must close the stream directly to unblock ReadLineAsync immediately.
    /// </summary>
    private Stream? _activeResponseStream;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Exponential backoff delays in milliseconds: 1s, 2s, 5s (capped).
    /// </summary>
    private static readonly int[] BackoffDelaysMs = [1000, 2000, 5000];

    /// <summary>
    /// HTTP status codes that should not trigger reconnection attempts.
    /// These represent permanent errors that won't be resolved by retrying.
    /// </summary>
    private static readonly HashSet<int> NonRetryableStatusCodes = [401, 403, 404];

    #endregion

    #region Events

    /// <inheritdoc />
    public event EventHandler<SseLocationEventArgs>? LocationReceived;

    /// <inheritdoc />
    public event EventHandler<SseLocationDeletedEventArgs>? LocationDeleted;

    /// <inheritdoc />
    public event EventHandler<SseMembershipEventArgs>? MembershipReceived;

    /// <inheritdoc />
    public event EventHandler<SseInviteCreatedEventArgs>? InviteCreated;

    /// <inheritdoc />
    public event EventHandler<SseVisitStartedEventArgs>? VisitStarted;

    /// <inheritdoc />
    public event EventHandler? HeartbeatReceived;

    /// <inheritdoc />
    public event EventHandler? Connected;

    /// <inheritdoc />
    public event EventHandler<SseReconnectEventArgs>? Reconnecting;

    /// <inheritdoc />
    public event EventHandler<SsePermanentErrorEventArgs>? PermanentError;

    #endregion

    #region Properties

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of <see cref="SseClient"/>.
    /// </summary>
    /// <param name="settings">Settings service for server URL and API token.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating clients.</param>
    public SseClient(
        ISettingsService settings,
        ILogger<SseClient> logger,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task SubscribeToUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            _logger.LogWarning("Cannot subscribe to SSE: userName is empty");
            return;
        }

        string? serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            _logger.LogError("Server URL not configured for SSE subscription");
            return;
        }

        string url = $"{serverUrl.TrimEnd('/')}/api/mobile/sse/location-update/{Uri.EscapeDataString(userName)}";
        await SubscribeAsync(url, $"user:{userName}", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SubscribeToGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            _logger.LogWarning("Cannot subscribe to SSE: groupId is empty");
            return;
        }

        string? serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            _logger.LogError("Server URL not configured for SSE subscription");
            return;
        }

        // Consolidated endpoint: location + membership events in single stream
        string url = $"{serverUrl.TrimEnd('/')}/api/mobile/sse/group/{Uri.EscapeDataString(groupId)}";
        await SubscribeAsync(url, $"group:{groupId}", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SubscribeToVisitsAsync(CancellationToken cancellationToken = default)
    {
        string? serverUrl = _settings.ServerUrl;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            _logger.LogError("Server URL not configured for SSE visit subscription");
            return;
        }

        // Visit notifications endpoint: user-visits-{userId} channel
        string url = $"{serverUrl.TrimEnd('/')}/api/mobile/sse/visits";
        await SubscribeAsync(url, "visits", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Stop()
    {
        Stream? streamToClose;
        CancellationTokenSource? ctsToCancel;

        lock (_connectionLock)
        {
            streamToClose = _activeResponseStream;
            ctsToCancel = _cancellationTokenSource;
            _activeResponseStream = null;
            _isConnected = false;
        }

        // Force-close stream FIRST - this is what unblocks ReadLineAsync immediately
        // CancellationToken.Cancel() does NOT interrupt active stream reads in .NET
        if (streamToClose != null)
        {
            try
            {
                streamToClose.Close();
                _logger.LogDebug("SSE stream force-closed");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE stream close error (expected): {Message}", ex.Message);
            }
        }

        // Then cancel token (triggers registered callbacks in background tasks)
        if (ctsToCancel != null && !ctsToCancel.IsCancellationRequested)
        {
            try
            {
                ctsToCancel.Cancel();
                _logger.LogInformation("SSE subscription stopped");
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE cancellation error: {Message}", ex.Message);
            }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Subscribe to SSE endpoint with automatic reconnection.
    /// </summary>
    private async Task SubscribeAsync(string url, string channelName, CancellationToken externalToken)
    {
        // Cancel any existing subscription
        Stop();

        // Create new cancellation token linked to external token
        CancellationTokenSource cts;
        lock (_connectionLock)
        {
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            cts = _cancellationTokenSource;
        }

        var cancellationToken = cts.Token;
        int reconnectAttempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (reconnectAttempt > 0)
                {
                    int delayMs = BackoffDelaysMs[Math.Min(reconnectAttempt - 1, BackoffDelaysMs.Length - 1)];
                    _logger.LogInformation("Reconnecting to {Channel} in {Delay}ms (attempt {Attempt})",
                        channelName, delayMs, reconnectAttempt);

                    Reconnecting?.Invoke(this, new SseReconnectEventArgs(reconnectAttempt, delayMs));

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation("Connecting to SSE channel: {Channel}", channelName);
                await ConnectAndStreamAsync(url, cancellationToken).ConfigureAwait(false);

                // If we reach here, stream ended normally (not an error)
                break;
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation (user-initiated stop)
                _logger.LogInformation("SSE subscription cancelled: {Channel}", channelName);
                break;
            }
            catch (Exception ex) when (IsCancellationException(ex))
            {
                // HttpClient sometimes wraps cancellation in other exception types
                _logger.LogInformation("SSE subscription stopped: {Message}", ex.Message);
                break;
            }
            catch (SsePermanentErrorException ex)
            {
                // Permanent error (401, 403, 404) - do not retry
                _logger.LogWarning(
                    "SSE subscription terminated due to permanent error on {Channel}: {StatusCode}",
                    channelName, ex.StatusCode);
                _isConnected = false;
                break; // Exit loop - no retry
            }
            catch (Exception ex)
            {
                // Real connection error - log and retry with backoff
                _logger.LogError(ex, "SSE connection error on {Channel}", channelName);
                reconnectAttempt++;
                _isConnected = false;

                // Continue loop to reconnect with backoff
            }
        }

        _isConnected = false;
    }

    /// <summary>
    /// Connect to SSE endpoint and stream events.
    /// </summary>
    private async Task ConnectAndStreamAsync(string url, CancellationToken cancellationToken)
    {
        string? apiToken = _settings.ApiToken;
        _logger.LogInformation(
            "SSE connecting to {Url}, token available: {HasToken}, token length: {TokenLength}",
            url,
            !string.IsNullOrWhiteSpace(apiToken),
            apiToken?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogError("API token not configured for SSE subscription");
            return;
        }

        // Check cancellation before starting
        cancellationToken.ThrowIfCancellationRequested();

        // Use dedicated SSE client with isolated connection pool
        // This prevents SSE connections from blocking API calls when cancelling
        var httpClient = _httpClientFactory.CreateClient("SSE");

        // Create request with Bearer auth and Accept header
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage? response;
        try
        {
            // Send request and get streaming response
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE connection cancelled during request");
            throw;
        }
        catch (Exception ex) when (IsCancellationException(ex))
        {
            _logger.LogInformation("SSE connection stopped: {Message}", ex.Message);
            throw new OperationCanceledException("SSE connection was cancelled", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                int statusCode = (int)response.StatusCode;

                _logger.LogError("SSE subscription failed: {StatusCode} - {Error}",
                    response.StatusCode, error);

                // Check if this is a permanent error that should not trigger reconnection
                if (NonRetryableStatusCodes.Contains(statusCode))
                {
                    _logger.LogWarning(
                        "SSE permanent error (will not retry): {StatusCode} - {Error}",
                        statusCode, error);

                    // Fire event to notify subscribers of permanent failure
                    PermanentError?.Invoke(this, new SsePermanentErrorEventArgs(statusCode, error));

                    // Throw specific exception that will be caught and NOT retried
                    throw new SsePermanentErrorException(statusCode, error);
                }

                throw new HttpRequestException($"SSE subscription failed: {response.StatusCode}");
            }

            _isConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("SSE connected: {Url}", url);

            // Stream and parse SSE frames
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            // Track stream for force-close on Stop()
            lock (_connectionLock)
            {
                _activeResponseStream = stream;
            }

            // Also register callback to force-close when cancelled
            // This ensures immediate disconnect even if Stop() isn't called directly
            await using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    stream.Close();
                    _logger.LogDebug("SSE stream force-closed via cancellation callback");
                }
                catch
                {
                    // Ignore - stream may already be closed
                }
            });

            try
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                await ParseSseStreamAsync(reader, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Clear stream reference
                lock (_connectionLock)
                {
                    if (_activeResponseStream == stream)
                    {
                        _activeResponseStream = null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parse SSE stream and emit events.
    /// </summary>
    private async Task ParseSseStreamAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var dataBuffer = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("SSE stream reading cancelled");
                    return;
                }
                catch (Exception ex) when (IsCancellationException(ex))
                {
                    _logger.LogInformation("SSE stream stopped: {Message}", ex.Message);
                    return;
                }

                // End of stream
                if (line == null)
                {
                    break;
                }

                // Heartbeat comment frame (e.g., ": heartbeat")
                if (line.StartsWith(':'))
                {
                    _logger.LogDebug("SSE heartbeat received");
                    HeartbeatReceived?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                // Data frame (e.g., "data: {...}")
                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    string data = line[5..].TrimStart();
                    dataBuffer.Append(data);
                    continue;
                }

                // Empty line signals end of event
                if (string.IsNullOrWhiteSpace(line) && dataBuffer.Length > 0)
                {
                    string json = dataBuffer.ToString();
                    dataBuffer.Clear();

                    ProcessEventData(json);
                }
            }
        }
        catch (Exception ex) when (!IsCancellationException(ex))
        {
            _logger.LogError(ex, "Error parsing SSE stream");
            throw;
        }
    }

    /// <summary>
    /// Process a single SSE event data payload.
    /// Uses the "type" discriminator field to determine event type.
    /// </summary>
    private void ProcessEventData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for type discriminator (new consolidated format)
            if (root.TryGetProperty("type", out var typeProp))
            {
                var eventType = typeProp.GetString();
                _logger.LogInformation("SSE event received with type: {Type}", eventType);
                ProcessTypedEvent(root, eventType);
                return;
            }

            _logger.LogWarning("SSE event received without type discriminator: {Json}", json);

            // Fallback: Try parsing as location event (legacy format)
            var locationEvent = JsonSerializer.Deserialize<SseLocationEvent>(json, JsonOptions);
            if (locationEvent != null && !string.IsNullOrEmpty(locationEvent.UserName))
            {
                _logger.LogInformation("SSE location received: {UserName} at {Timestamp}",
                    locationEvent.UserName, locationEvent.TimestampUtc);
                LocationReceived?.Invoke(this, new SseLocationEventArgs(locationEvent));
                return;
            }

            // Fallback: Try parsing as membership event (legacy format)
            var membershipEvent = JsonSerializer.Deserialize<SseMembershipEvent>(json, JsonOptions);
            if (membershipEvent != null && !string.IsNullOrEmpty(membershipEvent.Action))
            {
                _logger.LogInformation("SSE membership event received: {Action} for user {UserId}",
                    membershipEvent.Action, membershipEvent.UserId);
                MembershipReceived?.Invoke(this, new SseMembershipEventArgs(membershipEvent));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse SSE data: {Json}", json);
        }
    }

    /// <summary>
    /// Process an event with a type discriminator.
    /// </summary>
    private void ProcessTypedEvent(JsonElement root, string? eventType)
    {
        switch (eventType)
        {
            case "location":
                var locationEvent = new SseLocationEvent
                {
                    LocationId = root.TryGetProperty("locationId", out var lid) ? lid.GetInt32() : 0,
                    TimestampUtc = root.TryGetProperty("timestampUtc", out var ts) ? ts.GetDateTime() : DateTime.UtcNow,
                    UserId = root.TryGetProperty("userId", out var uid) ? uid.GetString() ?? string.Empty : string.Empty,
                    UserName = root.TryGetProperty("userName", out var un) ? un.GetString() ?? string.Empty : string.Empty,
                    IsLive = root.TryGetProperty("isLive", out var live) && live.GetBoolean(),
                    Type = root.TryGetProperty("locationType", out var lt) ? lt.GetString() : null
                };
                _logger.LogInformation("SSE location received: {UserName} at {Timestamp}",
                    locationEvent.UserName, locationEvent.TimestampUtc);
                LocationReceived?.Invoke(this, new SseLocationEventArgs(locationEvent));
                break;

            case "location-deleted":
                var deleteEvent = new SseLocationDeletedEvent
                {
                    LocationId = root.TryGetProperty("locationId", out var dlid) ? dlid.GetInt32() : 0,
                    UserId = root.TryGetProperty("userId", out var duid) ? duid.GetString() ?? string.Empty : string.Empty
                };
                _logger.LogInformation("SSE location deleted: {LocationId} for user {UserId}",
                    deleteEvent.LocationId, deleteEvent.UserId);
                LocationDeleted?.Invoke(this, new SseLocationDeletedEventArgs(deleteEvent));
                break;

            case "visibility-changed":
            case "member-left":
            case "member-removed":
            case "member-joined":
            case "invite-declined":
            case "invite-revoked":
                var membershipEvent = new SseMembershipEvent
                {
                    Action = eventType,  // Use type as action for compatibility
                    UserId = root.TryGetProperty("userId", out var muid) ? muid.GetString() : null,
                    Disabled = root.TryGetProperty("disabled", out var dis) ? dis.GetBoolean() : null
                };
                _logger.LogInformation("SSE membership event received: {Action} for user {UserId}",
                    membershipEvent.Action, membershipEvent.UserId);
                MembershipReceived?.Invoke(this, new SseMembershipEventArgs(membershipEvent));
                break;

            case "invite-created":
                var inviteEvent = new SseInviteCreatedEvent
                {
                    InvitationId = root.TryGetProperty("invitationId", out var invId) && invId.TryGetGuid(out var guid)
                        ? guid
                        : Guid.Empty
                };
                _logger.LogInformation("SSE invite created: {InvitationId}", inviteEvent.InvitationId);
                InviteCreated?.Invoke(this, new SseInviteCreatedEventArgs(inviteEvent));
                break;

            case "visit_started":
                var visitEvent = new SseVisitStartedEvent
                {
                    VisitId = root.TryGetProperty("visitId", out var vid) && vid.TryGetGuid(out var visitGuid)
                        ? visitGuid
                        : Guid.Empty,
                    TripId = root.TryGetProperty("tripId", out var tid) && tid.TryGetGuid(out var tripGuid)
                        ? tripGuid
                        : Guid.Empty,
                    TripName = root.TryGetProperty("tripName", out var tn) ? tn.GetString() ?? string.Empty : string.Empty,
                    PlaceId = root.TryGetProperty("placeId", out var pid) && pid.TryGetGuid(out var placeGuid)
                        ? placeGuid
                        : null,
                    PlaceName = root.TryGetProperty("placeName", out var pn) ? pn.GetString() ?? string.Empty : string.Empty,
                    RegionName = root.TryGetProperty("regionName", out var rn) ? rn.GetString() ?? string.Empty : string.Empty,
                    ArrivedAtUtc = root.TryGetProperty("arrivedAtUtc", out var arr) ? arr.GetDateTime() : DateTime.UtcNow,
                    Latitude = root.TryGetProperty("latitude", out var lat) && lat.ValueKind != JsonValueKind.Null
                        ? lat.GetDouble()
                        : null,
                    Longitude = root.TryGetProperty("longitude", out var lon) && lon.ValueKind != JsonValueKind.Null
                        ? lon.GetDouble()
                        : null,
                    IconName = root.TryGetProperty("iconName", out var icon) ? icon.GetString() : null,
                    MarkerColor = root.TryGetProperty("markerColor", out var color) ? color.GetString() : null
                };
                _logger.LogInformation("SSE visit started: {PlaceName} in {TripName} (VisitId: {VisitId})",
                    visitEvent.PlaceName, visitEvent.TripName, visitEvent.VisitId);
                VisitStarted?.Invoke(this, new SseVisitStartedEventArgs(visitEvent));
                break;

            default:
                _logger.LogDebug("Unknown SSE event type: {Type}", eventType);
                break;
        }
    }

    /// <summary>
    /// Check if an exception is related to cancellation/disconnection (not a real error).
    /// Includes stream force-close exceptions which are expected during Stop().
    /// </summary>
    private static bool IsCancellationException(Exception ex)
    {
        // Standard cancellation exceptions
        if (ex is OperationCanceledException or TaskCanceledException)
        {
            return true;
        }

        // ObjectDisposedException occurs when stream is force-closed during read
        if (ex is ObjectDisposedException)
        {
            return true;
        }

        // IOException with "closed" typically means stream was force-closed
        if (ex is IOException)
        {
            return true;
        }

        string message = ex.Message.ToLowerInvariant();
        return message.Contains("canceled") ||
               message.Contains("cancelled") ||
               message.Contains("socket closed") ||
               message.Contains("connection closed") ||
               message.Contains("request was canceled") ||
               message.Contains("the request was aborted") ||
               message.Contains("closed") ||
               message.Contains("disposed");
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Dispose resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Stop() handles stream close and cancellation
        Stop();

        lock (_connectionLock)
        {
            // Dispose CTS after Stop() has used it
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _activeResponseStream = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
