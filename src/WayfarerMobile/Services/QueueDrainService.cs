using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for draining the offline location queue via the check-in endpoint.
/// Operates independently from live location tracking and applies strict
/// client-side filtering (time AND distance) before sending to server.
/// </summary>
public sealed class QueueDrainService : IDisposable
{
    #region Constants

    /// <summary>
    /// Minimum seconds between check-in requests (conservative vs server's 30s).
    /// </summary>
    private const int MinSecondsBetweenDrains = 65;

    /// <summary>
    /// Maximum check-in syncs per hour (conservative vs server's 60).
    /// </summary>
    private const int MaxDrainsPerHour = 55;

    /// <summary>
    /// Gets time threshold in minutes from settings (synced from server).
    /// </summary>
    private int TimeThresholdMinutes => _settings.LocationTimeThresholdMinutes;

    /// <summary>
    /// Gets distance threshold in meters from settings (synced from server).
    /// </summary>
    private int DistanceThresholdMeters => _settings.LocationDistanceThresholdMeters;

    /// <summary>
    /// Timer interval for checking queue (seconds).
    /// </summary>
    private const int TimerIntervalSeconds = 30;

    /// <summary>
    /// Timeout for drain operations (milliseconds).
    /// </summary>
    private const int DrainTimeoutMs = 10000;

    /// <summary>
    /// Maximum consecutive failures before pausing.
    /// </summary>
    private const int MaxConsecutiveFailures = 5;

    /// <summary>
    /// Maximum age for sync reference before clearing (days).
    /// </summary>
    private const int MaxReferenceAgeDays = 30;

    /// <summary>
    /// Initial delay before first timer run (seconds).
    /// </summary>
    private const int InitialDelaySeconds = 5;

    /// <summary>
    /// Timeout for acquiring drain lock (milliseconds).
    /// </summary>
    private const int DrainLockTimeoutMs = 100;

    /// <summary>
    /// Candidate batch size for atomic claims to avoid no-op cycles under contention.
    /// </summary>
    private const int DrainClaimBatchSize = 5;

    /// <summary>
    /// Maximum jitter to add to initial delay (seconds).
    /// Prevents timer alignment with other services.
    /// </summary>
    private const int MaxJitterSeconds = 5;

    /// <summary>
    /// Random generator for jitter (thread-safe in .NET 6+).
    /// </summary>
    private static readonly Random Jitter = new();

    /// <summary>
    /// Maximum length for stored error messages.
    /// </summary>
    private const int MaxErrorMessageLength = 200;

    #endregion

    #region Fields

    private readonly IApiClient _apiClient;
    private readonly ILocationQueueRepository _locationQueue;
    private readonly ISettingsService _settings;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<QueueDrainService> _logger;

    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private readonly object _rateLimitLock = new();
    private readonly Queue<DateTime> _drainHistory = new();

    private Timer? _drainTimer;
    private CancellationTokenSource? _timerCts;
    private DateTime _lastDrainTime = DateTime.MinValue;
    private int _consecutiveFailures;
    private volatile bool _isOnline;
    private volatile bool _isDisposed;
    private volatile bool _isStarted;
    private int _disposeGuard; // For thread-safe Dispose via Interlocked

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of QueueDrainService.
    /// </summary>
    /// <param name="apiClient">API client for server communication.</param>
    /// <param name="locationQueue">Repository for location queue operations.</param>
    /// <param name="settings">Settings service for configuration.</param>
    /// <param name="connectivity">Connectivity service for network state.</param>
    /// <param name="logger">Logger instance.</param>
    public QueueDrainService(
        IApiClient apiClient,
        ILocationQueueRepository locationQueue,
        ISettingsService settings,
        IConnectivity connectivity,
        ILogger<QueueDrainService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _locationQueue = locationQueue ?? throw new ArgumentNullException(nameof(locationQueue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _isOnline = _connectivity.NetworkAccess == NetworkAccess.Internet;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts the queue drain service.
    /// Should be called after authentication is configured.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Cannot start disposed QueueDrainService");
            return;
        }

        if (_isStarted)
        {
            _logger.LogDebug("QueueDrainService already started");
            return;
        }

        try
        {
            _logger.LogInformation("Starting QueueDrainService");

            // Initialize: validate reference point and reset stuck locations
            await InitializeAsync();

            // Subscribe to connectivity changes
            _connectivity.ConnectivityChanged += OnConnectivityChanged;

            // Create cancellation token for timer callbacks
            _timerCts = new CancellationTokenSource();

            // Add random jitter to initial delay to prevent timer alignment with other services
            var jitteredDelay = InitialDelaySeconds + Jitter.Next(MaxJitterSeconds);

            // Start the drain timer
            _drainTimer = new Timer(
                OnDrainTimerElapsed,
                null,
                TimeSpan.FromSeconds(jitteredDelay),
                TimeSpan.FromSeconds(TimerIntervalSeconds));

            _isStarted = true;
            _logger.LogInformation("QueueDrainService started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start QueueDrainService");
        }
    }

    /// <summary>
    /// Stops the queue drain service.
    /// </summary>
    public void Stop()
    {
        if (!_isStarted)
            return;

        _logger.LogInformation("Stopping QueueDrainService");

        // Cancel any pending timer callbacks first
        _timerCts?.Cancel();

        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _drainTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _drainTimer?.Dispose();
        _drainTimer = null;

        _timerCts?.Dispose();
        _timerCts = null;

        _isStarted = false;
        _logger.LogInformation("QueueDrainService stopped");
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// Thread-safe - uses Interlocked to prevent double-dispose race.
    /// </summary>
    public void Dispose()
    {
        // Thread-safe check-and-set to prevent double-dispose race
        if (Interlocked.Exchange(ref _disposeGuard, 1) != 0)
            return;

        _isDisposed = true;
        Stop();
        _drainLock.Dispose();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Initializes the service on startup.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            // Validate reference point isn't impossibly old (atomic read to avoid race)
            if (_settings.TryGetSyncReference(out _, out _, out var refTime))
            {
                var age = DateTime.UtcNow - refTime;
                if (age.TotalDays > MaxReferenceAgeDays)
                {
                    _logger.LogWarning(
                        "Stale sync reference ({Age:F0} days old), clearing",
                        age.TotalDays);
                    _settings.ClearSyncReference();
                }
            }

            // Clean orphaned "Syncing" states from crash
            var resetCount = await _locationQueue.ResetStuckLocationsAsync();
            if (resetCount > 0)
            {
                _logger.LogInformation(
                    "Reset {Count} stuck locations from previous crash",
                    resetCount);
            }
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error during QueueDrainService initialization");
            // Continue anyway - initialization errors shouldn't prevent service from running
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during QueueDrainService initialization");
            // Continue anyway - initialization errors shouldn't prevent service from running
        }
    }

    #endregion

    #region Timer Callback

    /// <summary>
    /// Timer callback - attempts to drain one location from the queue.
    /// </summary>
    private async void OnDrainTimerElapsed(object? state)
    {
        // Check both flags and cancellation token for robust shutdown
        if (_isDisposed || !_isStarted)
            return;

        var cts = _timerCts;
        if (cts == null || cts.IsCancellationRequested)
            return;

        try
        {
            await DrainOneAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, ignore
        }
        catch (Exception ex)
        {
            // Never let timer callback exceptions crash the app
            _logger.LogError(ex, "Unhandled exception in drain timer callback");
        }
    }

    #endregion

    #region Connectivity

    /// <summary>
    /// Handles connectivity state changes.
    /// </summary>
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        try
        {
            var wasOnline = _isOnline;
            _isOnline = e.NetworkAccess == NetworkAccess.Internet;

            if (!wasOnline && _isOnline)
            {
                _logger.LogInformation("Network restored, queue drain will resume");
                // Reset consecutive failures on network restore
                _consecutiveFailures = 0;
            }
            else if (wasOnline && !_isOnline)
            {
                _logger.LogInformation("Network lost, queue drain paused");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connectivity change");
        }
    }

    #endregion

    #region Drain Logic

    /// <summary>
    /// Attempts to drain one location from the queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for shutdown.</param>
    private async Task DrainOneAsync(CancellationToken cancellationToken)
    {
        // Early exits - use Debug level for routine skip conditions
        if (!_isOnline)
        {
            _logger.LogDebug("QueueDrain: Offline, skipping cycle");
            return;
        }

        if (!_apiClient.IsConfigured)
        {
            _logger.LogDebug("QueueDrain: API not configured, skipping cycle");
            return;
        }

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _logger.LogDebug(
                "QueueDrain: Too many consecutive failures ({Failures}), backing off",
                _consecutiveFailures);
            return;
        }

        if (!CanMakeCheckInRequest())
        {
            var timeSinceLast = DateTime.UtcNow - _lastDrainTime;
            _logger.LogDebug(
                "QueueDrain: Rate limited ({SecondsSinceLast:F0}s since last, need {Required}s)",
                timeSinceLast.TotalSeconds, MinSecondsBetweenDrains);
            return;
        }

        // Try to acquire lock with timeout (non-blocking if busy)
        if (!await _drainLock.WaitAsync(DrainLockTimeoutMs, cancellationToken))
        {
            _logger.LogDebug("Drain lock busy, skipping cycle");
            return;
        }

        QueuedLocation? location = null;

        try
        {
            // CRITICAL FIX: Atomically claim the oldest pending location.
            // This prevents race condition with LocationSyncService's batch claims
            // and avoids no-op cycles when the oldest item is claimed by another service.
            location = await _locationQueue.ClaimOldestPendingLocationAsync(DrainClaimBatchSize);
            if (location == null)
            {
                _logger.LogDebug("QueueDrain: No pending locations to claim");
                return;
            }

            _logger.LogDebug(
                "QueueDrain: Claimed location {Id} from {Timestamp}",
                location.Id, location.Timestamp);

            // Note: Location is already marked as Syncing by ClaimOldestPendingLocationAsync
            // Rate limit is recorded in ProcessLocationAsync AFTER client-side filter passes
        }
        catch (SQLiteException ex)
        {
            // Exception occurs during claim - location is not yet assigned
            _logger.LogError(ex, "Database error claiming pending location from queue");
            return;
        }
        catch (Exception ex)
        {
            // Exception occurs during claim - location is not yet assigned
            _logger.LogError(ex, "Unexpected error claiming pending location from queue");
            return;
        }
        finally
        {
            _drainLock.Release();
        }

        // Process location outside lock (network call)
        try
        {
            await ProcessLocationAsync(location, cancellationToken);
        }
        catch (Exception ex)
        {
            // ProcessLocationAsync handles most exceptions internally,
            // but if something unexpected escapes, ensure we don't leave location stuck
            _logger.LogError(ex, "Unhandled exception processing location {Id}", location.Id);
            await TryResetLocationAsync(location.Id, "Unhandled processing exception");
        }
    }

    /// <summary>
    /// Safely attempts to reset a location to Pending, logging any errors.
    /// </summary>
    private async Task TryResetLocationAsync(int locationId, string reason)
    {
        try
        {
            await _locationQueue.ResetLocationToPendingAsync(locationId);
            _logger.LogWarning("Reset location {Id} to Pending: {Reason}", locationId, reason);
        }
        catch (Exception resetEx)
        {
            _logger.LogError(resetEx,
                "Failed to reset location {Id} to Pending - may be stuck in Syncing state",
                locationId);
        }
    }

    /// <summary>
    /// Processes a single location - filters and syncs if appropriate.
    /// </summary>
    /// <param name="location">The location to process.</param>
    /// <param name="cancellationToken">Cancellation token for shutdown.</param>
    private async Task ProcessLocationAsync(QueuedLocation location, CancellationToken cancellationToken)
    {
        try
        {
            // Check threshold filter
            var filterResult = ShouldSyncLocation(location);

            if (!filterResult.ShouldSync)
            {
                // Mark as rejected - no API call needed, no rate limit recorded
                await _locationQueue.MarkLocationRejectedAsync(location.Id, $"Client: {filterResult.Reason}");
                _logger.LogDebug(
                    "QueueDrain: Location {Id} rejected: {Reason}",
                    location.Id, filterResult.Reason);
                return;
            }

            // FIX: Record rate limit AFTER filter passes, BEFORE API call
            // This ensures client-rejected locations don't count against rate limit
            RecordDrainAttempt();

            // Send via check-in endpoint
            var request = new LocationLogRequest
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Altitude = location.Altitude,
                Accuracy = location.Accuracy,
                Speed = location.Speed,
                Timestamp = location.Timestamp, // Original timestamp, not now
                Provider = location.Provider ?? "queue-drain"
            };

            // Use provided cancellation token combined with timeout
            using var timeoutCts = new CancellationTokenSource(DrainTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var result = await _apiClient.CheckInAsync(request, location.IdempotencyKey, linkedCts.Token);

            if (result.Success)
            {
                // CRITICAL: Mark ServerConfirmed IMMEDIATELY after API success
                // This ensures crash recovery marks as Synced instead of resetting to Pending
                await _locationQueue.MarkServerConfirmedAsync(location.Id);

                // Mark synced and update reference point with location's actual timestamp
                await _locationQueue.MarkLocationSyncedAsync(location.Id);
                _settings.UpdateLastSyncedLocation(
                    location.Latitude,
                    location.Longitude,
                    location.Timestamp);

                _consecutiveFailures = 0;
                _logger.LogInformation(
                    "QueueDrain: Location {Id} synced successfully via check-in",
                    location.Id);

                // Notify listeners (e.g., LocalTimelineStorageService) for ServerId update
                if (result.LocationId.HasValue)
                {
                    LocationSyncCallbacks.NotifyLocationSynced(
                        location.Id,
                        result.LocationId.Value,
                        location.Timestamp,
                        location.Latitude,
                        location.Longitude);
                }
            }
            else if (result.Skipped)
            {
                // Server skipped due to its own thresholds - mark as rejected
                await _locationQueue.MarkLocationRejectedAsync(
                    location.Id,
                    $"Server: {result.Message ?? "skipped"}");
                _consecutiveFailures = 0;
                _logger.LogDebug(
                    "Location {Id} skipped by server: {Message}",
                    location.Id, result.Message);

                // Notify listeners for local timeline cleanup
                LocationSyncCallbacks.NotifyLocationSkipped(
                    location.Id,
                    location.Timestamp,
                    location.Latitude,
                    location.Longitude,
                    result.Message ?? "Threshold not met");
            }
            else if (result.StatusCode.HasValue && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                // Client error (4xx) - server rejection, don't retry
                await _locationQueue.MarkLocationRejectedAsync(
                    location.Id,
                    $"Server: {result.Message ?? $"HTTP {result.StatusCode}"}");
                _consecutiveFailures = 0;
                _logger.LogWarning(
                    "Location {Id} rejected by server (HTTP {StatusCode}): {Message}",
                    location.Id, result.StatusCode, result.Message);
            }
            else
            {
                // Technical failure (5xx, network) - reset to pending for retry
                await _locationQueue.ResetLocationToPendingAsync(location.Id);
                _consecutiveFailures++;
                _logger.LogWarning(
                    "Location {Id} sync failed (attempt {Attempts}): {Message}",
                    location.Id, location.SyncAttempts + 1, result.Message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown - reset to pending so it can be retried
            await _locationQueue.ResetLocationToPendingAsync(location.Id);
            throw; // Re-throw to propagate cancellation
        }
        catch (OperationCanceledException)
        {
            // Timeout - reset to pending for retry
            await _locationQueue.ResetLocationToPendingAsync(location.Id);
            _consecutiveFailures++;
            _logger.LogWarning("Location {Id} sync timed out", location.Id);
        }
        catch (HttpRequestException ex)
        {
            // Network error - reset to pending for retry
            await _locationQueue.ResetLocationToPendingAsync(location.Id);
            _consecutiveFailures++;
            _logger.LogWarning(ex, "Network error syncing location {Id}", location.Id);
        }
        catch (Exception ex)
        {
            // Unexpected error - mark as failed (sanitize message for safe storage)
            await _locationQueue.MarkLocationFailedAsync(location.Id, $"Unexpected: {SanitizeErrorMessage(ex.Message)}");
            _consecutiveFailures++;
            _logger.LogError(ex, "Unexpected error processing location {Id}", location.Id);
        }
    }

    #endregion

    #region Threshold Filtering

    /// <summary>
    /// Determines if a location should be synced based on time AND distance thresholds.
    /// </summary>
    private (bool ShouldSync, string? Reason) ShouldSyncLocation(QueuedLocation location)
    {
        // Atomically get sync reference to avoid race with logout/clear
        if (!_settings.TryGetSyncReference(out var refLat, out var refLon, out var refTime))
        {
            // First sync - no reference point, always sync
            _logger.LogDebug("No sync reference, first location will sync");
            return (true, null);
        }

        // MEDIUM FIX: Handle out-of-order locations
        // If location is older than or equal to reference, skip it
        // This can happen if queue processing was interrupted and resumed
        if (location.Timestamp <= refTime)
        {
            _logger.LogDebug(
                "Location {Id} is out-of-order (timestamp {LocationTime} <= reference {RefTime})",
                location.Id, location.Timestamp, refTime);
            return (false, $"Out-of-order: timestamp {location.Timestamp:u} <= reference {refTime:u}");
        }

        // Calculate time since reference
        var timeSince = location.Timestamp - refTime;
        var timeThresholdMet = timeSince.TotalMinutes >= TimeThresholdMinutes;

        // Calculate distance from reference
        var distance = CalculateDistanceMeters(refLat, refLon, location.Latitude, location.Longitude);
        var distanceThresholdMet = distance >= DistanceThresholdMeters;

        // Server uses AND logic - BOTH must be met
        if (timeThresholdMet && distanceThresholdMet)
        {
            return (true, null);
        }

        // Build filter reason
        var reasons = new List<string>();
        if (!timeThresholdMet)
            reasons.Add($"Time: {timeSince.TotalMinutes:F1}min, threshold {TimeThresholdMinutes}min");
        if (!distanceThresholdMet)
            reasons.Add($"Distance: {distance:F0}m, threshold {DistanceThresholdMeters}m");

        return (false, string.Join("; ", reasons));
    }

    /// <summary>
    /// Calculates distance between two coordinates using Haversine formula.
    /// </summary>
    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusMeters = 6371000;

        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Checks if a check-in request can be made based on rate limits.
    /// </summary>
    private bool CanMakeCheckInRequest()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;

            // Check minimum interval since last drain
            if ((now - _lastDrainTime).TotalSeconds < MinSecondsBetweenDrains)
            {
                return false;
            }

            // Clean old history entries
            while (_drainHistory.Count > 0 && (now - _drainHistory.Peek()).TotalHours >= 1)
            {
                _drainHistory.Dequeue();
            }

            // Check hourly limit
            return _drainHistory.Count < MaxDrainsPerHour;
        }
    }

    /// <summary>
    /// Records a drain attempt for rate limiting.
    /// </summary>
    private void RecordDrainAttempt()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            _lastDrainTime = now;
            _drainHistory.Enqueue(now);
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sanitizes exception messages for safe storage.
    /// Truncates to maximum length and removes newlines.
    /// </summary>
    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "Unknown error";

        // Replace newlines with spaces
        var sanitized = message.Replace('\n', ' ').Replace('\r', ' ');

        // Truncate if too long
        if (sanitized.Length > MaxErrorMessageLength)
        {
            sanitized = sanitized[..MaxErrorMessageLength] + "...";
        }

        return sanitized;
    }

    #endregion
}
