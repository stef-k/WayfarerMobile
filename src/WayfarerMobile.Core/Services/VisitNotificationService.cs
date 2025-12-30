using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Services;

/// <summary>
/// Default number of seconds to look back when polling for recent visits.
/// </summary>
internal static class VisitPollConstants
{
    public const int DefaultPollWindowSeconds = 30;
}

/// <summary>
/// Handles visit notifications via SSE and background polling.
/// Subscribes to visit_started events and shows notifications/voice announcements
/// when the user arrives at a trip place.
/// </summary>
/// <remarks>
/// <para>
/// When the app is in foreground, uses SSE for real-time notifications.
/// When backgrounded, SSE dies, so we poll after location sync instead.
/// </para>
/// <para>
/// Background poll is fully isolated with fire-and-forget pattern to ensure
/// it never disrupts the mission-critical location sync service.
/// </para>
/// </remarks>
public class VisitNotificationService : IVisitNotificationService
{
    #region Constants

    /// <summary>
    /// Cooldown period after navigation ends before showing full notifications.
    /// Prevents duplicate announcements when navigation arrival voice is already playing.
    /// </summary>
    private static readonly TimeSpan NavigationEndCooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum number of recent visit IDs to track for deduplication.
    /// </summary>
    private const int MaxRecentVisitIds = 10;

    #endregion

    #region Fields

    private readonly ISettingsService _settings;
    private readonly ISseClientFactory _sseClientFactory;
    private readonly ITextToSpeechService _ttsService;
    private readonly ILocalNotificationService _notificationService;
    private readonly IVisitApiClient _visitApiClient;
    private readonly ILocationSyncEventBridge? _syncEventBridge;
    private readonly ILogger<VisitNotificationService> _logger;

    private ISseClient? _sseClient;
    private CancellationTokenSource? _subscriptionCts;
    private readonly object _stateLock = new();
    private bool _disposed;
    private bool _subscribedToSyncEvents;

    // Navigation conflict tracking
    private bool _isNavigating;
    private Guid? _currentDestinationPlaceId;
    private DateTime? _navigationEndedAt;

    // Deduplication: circular buffer of recent visit IDs
    private readonly Queue<Guid> _recentVisitIds = new();

    #endregion

    #region Properties

    /// <inheritdoc />
    public bool IsSubscribed => _sseClient?.IsConnected ?? false;

    #endregion

    #region Events

    /// <inheritdoc />
    public event EventHandler<VisitNotificationEventArgs>? NotificationDisplayed;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of VisitNotificationService.
    /// </summary>
    /// <param name="settings">Settings service.</param>
    /// <param name="sseClientFactory">Factory for creating SSE clients.</param>
    /// <param name="ttsService">Text-to-speech service.</param>
    /// <param name="notificationService">Local notification service.</param>
    /// <param name="visitApiClient">API client for polling visits.</param>
    /// <param name="syncEventBridge">Optional bridge for location sync events (enables background poll).</param>
    /// <param name="logger">Logger instance.</param>
    public VisitNotificationService(
        ISettingsService settings,
        ISseClientFactory sseClientFactory,
        ITextToSpeechService ttsService,
        ILocalNotificationService notificationService,
        IVisitApiClient visitApiClient,
        ILocationSyncEventBridge? syncEventBridge,
        ILogger<VisitNotificationService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sseClientFactory = sseClientFactory ?? throw new ArgumentNullException(nameof(sseClientFactory));
        _ttsService = ttsService ?? throw new ArgumentNullException(nameof(ttsService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _visitApiClient = visitApiClient ?? throw new ArgumentNullException(nameof(visitApiClient));
        _syncEventBridge = syncEventBridge; // Optional - background poll disabled if null
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task StartAsync()
    {
        if (_disposed)
        {
            _logger.LogWarning("Cannot start VisitNotificationService: already disposed");
            return;
        }

        // Check if feature is enabled
        if (!_settings.VisitNotificationsEnabled)
        {
            _logger.LogInformation("Visit notifications are disabled in settings, not subscribing");
            return;
        }

        // Check if app is configured (API token available)
        // This prevents 401 errors when SSE starts before settings are loaded
        if (!_settings.IsConfigured)
        {
            _logger.LogInformation("Visit SSE skipped: app not configured (no API token)");
            return;
        }

        // Subscribe to location sync events for background polling
        // This runs even if SSE fails - provides fallback when app is backgrounded
        SubscribeToSyncEvents();

        // Check if already subscribed to SSE
        if (_sseClient != null)
        {
            _logger.LogDebug("Visit SSE already active, skipping start");
            return;
        }

        try
        {
            // Create new SSE client for visits (independent from group SSE)
            _sseClient = _sseClientFactory.Create();
            _sseClient.VisitStarted += OnVisitStarted;
            _sseClient.Connected += OnConnected;
            _sseClient.Reconnecting += OnReconnecting;
            _sseClient.PermanentError += OnPermanentError;

            // Create cancellation token for subscription
            _subscriptionCts = new CancellationTokenSource();

            _logger.LogInformation("Starting visit SSE subscription");

            // Start subscription in background (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sseClient.SubscribeToVisitsAsync(_subscriptionCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Visit SSE subscription cancelled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Visit SSE subscription error");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start visit notification service");
            CleanupClient();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        _logger.LogInformation("Stopping visit notification service");
        UnsubscribeFromSyncEvents();
        CleanupClient();
    }

    /// <inheritdoc />
    public void UpdateNavigationState(bool isNavigating, Guid? destinationPlaceId)
    {
        lock (_stateLock)
        {
            bool wasNavigating = _isNavigating;

            _isNavigating = isNavigating;
            _currentDestinationPlaceId = isNavigating ? destinationPlaceId : null;

            // Track when navigation ended for cooldown
            if (wasNavigating && !isNavigating)
            {
                _navigationEndedAt = DateTime.UtcNow;
                _logger.LogDebug("Navigation ended, starting cooldown period");
            }
            else if (isNavigating)
            {
                _navigationEndedAt = null;
            }

            _logger.LogDebug(
                "Navigation state updated: isNavigating={IsNavigating}, destinationPlaceId={PlaceId}",
                isNavigating, destinationPlaceId);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Handles visit started events from SSE.
    /// </summary>
    private async void OnVisitStarted(object? sender, SseVisitStartedEventArgs e)
    {
        try
        {
            await HandleVisitEventAsync(e.Visit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling visit event");
        }
    }

    /// <summary>
    /// Processes a visit event with conflict detection and deduplication.
    /// </summary>
    private async Task HandleVisitEventAsync(SseVisitStartedEvent visit)
    {
        // Check feature is still enabled
        if (!_settings.VisitNotificationsEnabled)
        {
            _logger.LogDebug("Visit notifications disabled, ignoring event");
            return;
        }

        // Deduplicate by visit ID
        if (IsRecentVisit(visit.VisitId))
        {
            _logger.LogDebug("Duplicate visit event ignored: {VisitId}", visit.VisitId);
            return;
        }

        // Track this visit ID
        AddRecentVisit(visit.VisitId);

        // Determine notification mode based on navigation state
        var mode = DetermineNotificationMode(visit);

        _logger.LogInformation(
            "Visit notification: {PlaceName} in {TripName}, mode={Mode}",
            visit.PlaceName, visit.TripName, mode);

        if (mode == VisitNotificationMode.Suppressed)
        {
            NotificationDisplayed?.Invoke(this, new VisitNotificationEventArgs(visit, mode));
            return;
        }

        // Get notification style from settings
        var style = _settings.VisitNotificationStyle;
        bool showNotification = style is "notification" or "both";
        bool speakAnnouncement = style is "voice" or "both";

        // Handle based on mode
        if (mode == VisitNotificationMode.Silent)
        {
            // Silent mode: notification only, no sound/vibration, no voice
            if (showNotification)
            {
                await ShowNotificationAsync(visit, silent: true);
            }
        }
        else // Full mode
        {
            // Full notification with sound
            if (showNotification)
            {
                await ShowNotificationAsync(visit, silent: false);
            }

            // Voice announcement
            if (speakAnnouncement && _settings.VisitVoiceAnnouncementEnabled)
            {
                await SpeakAnnouncementAsync(visit);
            }
        }

        NotificationDisplayed?.Invoke(this, new VisitNotificationEventArgs(visit, mode));
    }

    /// <summary>
    /// Determines the notification mode based on current navigation state.
    /// </summary>
    private VisitNotificationMode DetermineNotificationMode(SseVisitStartedEvent visit)
    {
        lock (_stateLock)
        {
            // Check if we're in the cooldown period after navigation ended
            if (_navigationEndedAt.HasValue)
            {
                var elapsed = DateTime.UtcNow - _navigationEndedAt.Value;
                if (elapsed < NavigationEndCooldown)
                {
                    _logger.LogDebug(
                        "In navigation cooldown ({Elapsed}s), suppressing visit notification",
                        elapsed.TotalSeconds);
                    return VisitNotificationMode.Suppressed;
                }
            }

            // If not navigating, show full notification
            if (!_isNavigating)
            {
                return VisitNotificationMode.Full;
            }

            // Navigating to the same place - suppress (navigation will announce arrival)
            if (_currentDestinationPlaceId.HasValue &&
                visit.PlaceId.HasValue &&
                _currentDestinationPlaceId.Value == visit.PlaceId.Value)
            {
                _logger.LogDebug(
                    "Suppressing visit notification - navigating to same place: {PlaceId}",
                    visit.PlaceId);
                return VisitNotificationMode.Suppressed;
            }

            // Navigating to a different place - silent notification
            _logger.LogDebug(
                "Silent notification - navigating to different place. Current: {CurrentPlace}, Visit: {VisitPlace}",
                _currentDestinationPlaceId, visit.PlaceId);
            return VisitNotificationMode.Silent;
        }
    }

    /// <summary>
    /// Shows a local notification for the visit.
    /// </summary>
    private async Task ShowNotificationAsync(SseVisitStartedEvent visit, bool silent)
    {
        try
        {
            var title = $"Arrived at {visit.PlaceName}";
            var message = string.IsNullOrEmpty(visit.RegionName)
                ? visit.TripName
                : $"{visit.TripName} • {visit.RegionName}";

            var data = new Dictionary<string, string>
            {
                ["visitId"] = visit.VisitId.ToString(),
                ["tripId"] = visit.TripId.ToString(),
                ["placeId"] = visit.PlaceId?.ToString() ?? string.Empty
            };

            await _notificationService.ShowAsync(title, message, silent, data);

            _logger.LogDebug("Showed visit notification: {Title} (silent: {Silent})", title, silent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show visit notification");
        }
    }

    /// <summary>
    /// Speaks a voice announcement for the visit.
    /// </summary>
    private async Task SpeakAnnouncementAsync(SseVisitStartedEvent visit)
    {
        try
        {
            var announcement = $"You've arrived at {visit.PlaceName}";

            await _ttsService.SpeakAsync(announcement);

            _logger.LogDebug("Spoke visit announcement: {Announcement}", announcement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to speak visit announcement");
        }
    }

    /// <summary>
    /// Checks if a visit ID was recently processed (deduplication).
    /// </summary>
    private bool IsRecentVisit(Guid visitId)
    {
        lock (_recentVisitIds)
        {
            return _recentVisitIds.Contains(visitId);
        }
    }

    /// <summary>
    /// Adds a visit ID to the recent list (circular buffer).
    /// </summary>
    private void AddRecentVisit(Guid visitId)
    {
        lock (_recentVisitIds)
        {
            _recentVisitIds.Enqueue(visitId);

            // Trim to max size
            while (_recentVisitIds.Count > MaxRecentVisitIds)
            {
                _recentVisitIds.Dequeue();
            }
        }
    }

    #region Background Poll Methods

    /// <summary>
    /// Subscribes to location sync events for background polling.
    /// </summary>
    private void SubscribeToSyncEvents()
    {
        if (_syncEventBridge == null)
        {
            _logger.LogDebug("No sync event bridge configured, background poll disabled");
            return;
        }

        if (_subscribedToSyncEvents)
        {
            return;
        }

        _syncEventBridge.LocationSynced += OnLocationSyncedSafe;
        _subscribedToSyncEvents = true;
        _logger.LogDebug("Subscribed to location sync events for background visit polling");
    }

    /// <summary>
    /// Unsubscribes from location sync events.
    /// </summary>
    private void UnsubscribeFromSyncEvents()
    {
        if (_syncEventBridge == null || !_subscribedToSyncEvents)
        {
            return;
        }

        _syncEventBridge.LocationSynced -= OnLocationSyncedSafe;
        _subscribedToSyncEvents = false;
        _logger.LogDebug("Unsubscribed from location sync events");
    }

    /// <summary>
    /// Handles location sync events by polling for recent visits.
    /// Fully isolated with fire-and-forget to never disrupt LocationSyncService.
    /// </summary>
    private async void OnLocationSyncedSafe(object? sender, LocationSyncedBridgeEventArgs e)
    {
        // Skip if foreground SSE is active and handling it
        if (IsSubscribed)
        {
            _logger.LogDebug("SSE active, skipping background visit poll");
            return;
        }

        // Skip if feature is disabled
        if (!_settings.VisitNotificationsEnabled)
        {
            return;
        }

        // Fire-and-forget, fully isolated from caller
        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Polling for recent visits after location sync");

                var visits = await _visitApiClient.GetRecentVisitsAsync(
                    VisitPollConstants.DefaultPollWindowSeconds);

                if (visits.Count == 0)
                {
                    _logger.LogDebug("No recent visits found");
                    return;
                }

                _logger.LogDebug("Found {Count} recent visits, processing", visits.Count);

                foreach (var visit in visits)
                {
                    try
                    {
                        await HandleVisitEventAsync(visit);
                    }
                    catch (Exception ex)
                    {
                        // Log but continue processing other visits
                        _logger.LogDebug(ex, "Error processing visit {VisitId}", visit.VisitId);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-critical - log at debug level and swallow
                _logger.LogDebug(ex, "Visit poll failed (non-critical)");
            }
        });
    }

    #endregion

    /// <summary>
    /// Cleans up the SSE client resources.
    /// </summary>
    private void CleanupClient()
    {
        if (_sseClient != null)
        {
            _sseClient.VisitStarted -= OnVisitStarted;
            _sseClient.Connected -= OnConnected;
            _sseClient.Reconnecting -= OnReconnecting;
            _sseClient.PermanentError -= OnPermanentError;
            _sseClient.Stop();
            _sseClient.Dispose();
            _sseClient = null;
        }

        if (_subscriptionCts != null)
        {
            _subscriptionCts.Cancel();
            _subscriptionCts.Dispose();
            _subscriptionCts = null;
        }
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Visit SSE connected");
    }

    private void OnReconnecting(object? sender, SseReconnectEventArgs e)
    {
        _logger.LogInformation(
            "Visit SSE reconnecting: attempt {Attempt}, delay {Delay}ms",
            e.Attempt, e.DelayMs);
    }

    private void OnPermanentError(object? sender, SsePermanentErrorEventArgs e)
    {
        _logger.LogWarning(
            "Visit SSE permanent error (stopping): {StatusCode} - {Message}",
            e.StatusCode, e.Message);

        // Stop retrying - the error is permanent (401/403/404)
        // Background polling will continue to work as fallback
        CleanupClient();
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnsubscribeFromSyncEvents();
        CleanupClient();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
