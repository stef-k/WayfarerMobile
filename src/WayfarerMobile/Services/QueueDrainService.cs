using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

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
    /// Time threshold in minutes for client-side filtering (mirrors server's log-location).
    /// </summary>
    private const int TimeThresholdMinutes = 5;

    /// <summary>
    /// Distance threshold in meters for client-side filtering (mirrors server's log-location).
    /// </summary>
    private const int DistanceThresholdMeters = 100;

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

    #endregion

    #region Fields

    private readonly IApiClient _apiClient;
    private readonly DatabaseService _database;
    private readonly ISettingsService _settings;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<QueueDrainService> _logger;

    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private readonly object _rateLimitLock = new();
    private readonly Queue<DateTime> _drainHistory = new();

    private Timer? _drainTimer;
    private DateTime _lastDrainTime = DateTime.MinValue;
    private int _consecutiveFailures;
    private bool _isOnline;
    private bool _isDisposed;
    private bool _isStarted;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of QueueDrainService.
    /// </summary>
    public QueueDrainService(
        IApiClient apiClient,
        DatabaseService database,
        ISettingsService settings,
        IConnectivity connectivity,
        ILogger<QueueDrainService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _database = database ?? throw new ArgumentNullException(nameof(database));
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

            // Start the drain timer
            _drainTimer = new Timer(
                OnDrainTimerElapsed,
                null,
                TimeSpan.FromSeconds(5), // Initial delay
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

        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _drainTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _drainTimer?.Dispose();
        _drainTimer = null;

        _isStarted = false;
        _logger.LogInformation("QueueDrainService stopped");
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        Stop();
        _drainLock.Dispose();
        _isDisposed = true;
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
            // Validate reference point isn't impossibly old
            if (_settings.HasValidSyncReference() &&
                _settings.LastSyncedTimestamp.HasValue)
            {
                var age = DateTime.UtcNow - _settings.LastSyncedTimestamp.Value;
                if (age.TotalDays > MaxReferenceAgeDays)
                {
                    _logger.LogWarning(
                        "Stale sync reference ({Age:F0} days old), clearing",
                        age.TotalDays);
                    _settings.ClearSyncReference();
                }
            }

            // Clean orphaned "Syncing" states from crash
            var resetCount = await _database.ResetStuckLocationsAsync();
            if (resetCount > 0)
            {
                _logger.LogInformation(
                    "Reset {Count} stuck locations from previous crash",
                    resetCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during QueueDrainService initialization");
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
        if (_isDisposed || !_isStarted)
            return;

        try
        {
            await DrainOneAsync();
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
    private async Task DrainOneAsync()
    {
        // Early exits
        if (!_isOnline)
        {
            _logger.LogDebug("Offline, skipping drain cycle");
            return;
        }

        if (!_apiClient.IsConfigured)
        {
            _logger.LogDebug("API not configured, skipping drain cycle");
            return;
        }

        if (_consecutiveFailures >= MaxConsecutiveFailures)
        {
            _logger.LogDebug(
                "Too many consecutive failures ({Failures}), backing off",
                _consecutiveFailures);
            return;
        }

        if (!CanMakeCheckInRequest())
        {
            _logger.LogDebug("Rate limited, skipping drain cycle");
            return;
        }

        // Try to acquire lock with timeout (non-blocking if busy)
        if (!await _drainLock.WaitAsync(100))
        {
            _logger.LogDebug("Drain lock busy, skipping cycle");
            return;
        }

        QueuedLocation? location = null;

        try
        {
            // Get oldest pending location
            location = await _database.GetOldestPendingForDrainAsync();
            if (location == null)
            {
                _logger.LogDebug("No pending locations in queue");
                return;
            }

            _logger.LogDebug(
                "Processing queued location {Id} from {Timestamp}",
                location.Id, location.Timestamp);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending location from queue");
            return;
        }
        finally
        {
            _drainLock.Release();
        }

        // Process location outside lock (network call)
        if (location != null)
        {
            await ProcessLocationAsync(location);
        }
    }

    /// <summary>
    /// Processes a single location - filters and syncs if appropriate.
    /// </summary>
    private async Task ProcessLocationAsync(QueuedLocation location)
    {
        try
        {
            // Check threshold filter
            var filterResult = ShouldSyncLocation(location);

            if (!filterResult.ShouldSync)
            {
                // Mark as filtered and continue to next
                await _database.MarkLocationFilteredAsync(location.Id, filterResult.Reason!);
                _logger.LogDebug(
                    "Location {Id} filtered: {Reason}",
                    location.Id, filterResult.Reason);
                return;
            }

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

            using var cts = new CancellationTokenSource(DrainTimeoutMs);
            var result = await _apiClient.CheckInAsync(request, cts.Token);

            // Record drain attempt for rate limiting
            RecordDrainAttempt();

            if (result.Success)
            {
                // Mark synced and update reference point
                await _database.MarkLocationSyncedAsync(location.Id);
                _settings.UpdateLastSyncedLocation(location.Latitude, location.Longitude);

                _consecutiveFailures = 0;
                _logger.LogDebug(
                    "Location {Id} synced successfully via check-in",
                    location.Id);
            }
            else if (result.Skipped)
            {
                // Server skipped due to its own thresholds - mark as rejected
                await _database.MarkLocationServerRejectedAsync(
                    location.Id,
                    result.Message ?? "Server skipped");
                _consecutiveFailures = 0;
                _logger.LogDebug(
                    "Location {Id} skipped by server: {Message}",
                    location.Id, result.Message);
            }
            else if (result.StatusCode.HasValue && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                // Client error (4xx) - server rejection, don't retry
                await _database.MarkLocationServerRejectedAsync(
                    location.Id,
                    result.Message ?? $"HTTP {result.StatusCode}");
                _consecutiveFailures = 0;
                _logger.LogWarning(
                    "Location {Id} rejected by server (HTTP {StatusCode}): {Message}",
                    location.Id, result.StatusCode, result.Message);
            }
            else
            {
                // Technical failure (5xx, network) - increment retry
                await _database.IncrementRetryCountAsync(location.Id);
                _consecutiveFailures++;
                _logger.LogWarning(
                    "Location {Id} sync failed (attempt {Attempts}): {Message}",
                    location.Id, location.SyncAttempts + 1, result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
            await _database.IncrementRetryCountAsync(location.Id);
            _consecutiveFailures++;
            _logger.LogWarning("Location {Id} sync timed out", location.Id);
        }
        catch (Exception ex)
        {
            // Unexpected error
            await _database.MarkLocationFailedAsync(location.Id, ex.Message);
            _consecutiveFailures++;
            _logger.LogError(ex, "Error processing location {Id}", location.Id);
        }
    }

    #endregion

    #region Threshold Filtering

    /// <summary>
    /// Determines if a location should be synced based on time AND distance thresholds.
    /// </summary>
    private (bool ShouldSync, string? Reason) ShouldSyncLocation(QueuedLocation location)
    {
        // First sync - no reference point, always sync
        if (!_settings.HasValidSyncReference())
        {
            _logger.LogDebug("No sync reference, first location will sync");
            return (true, null);
        }

        var refLat = _settings.LastSyncedLatitude!.Value;
        var refLon = _settings.LastSyncedLongitude!.Value;
        var refTime = _settings.LastSyncedTimestamp!.Value;

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
            reasons.Add($"Time: {timeSince.TotalMinutes:F1}min < {TimeThresholdMinutes}min");
        if (!distanceThresholdMet)
            reasons.Add($"Distance: {distance:F0}m < {DistanceThresholdMeters}m");

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
}
