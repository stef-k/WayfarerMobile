using Microsoft.Extensions.Logging;
using Polly;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Service responsible for synchronizing queued locations with the server.
/// Implements rate limiting and retry with exponential backoff.
/// </summary>
public class LocationSyncService : IDisposable
{
    #region Fields

    private readonly IApiClient _apiClient;
    private readonly ILocationQueueRepository _locationQueue;
    private readonly ISettingsService _settings;
    private readonly ILogger<LocationSyncService> _logger;
    private readonly ResiliencePipeline _retryPipeline;

    private Timer? _syncTimer;
    private Timer? _cleanupTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _startStopLock = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isSyncing;
    private int _disposeGuard; // For thread-safe Dispose via Interlocked

    // Rate limiting tracking
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly Queue<DateTime> _syncHistory = new();
    private readonly object _rateLimitLock = new();
    private const int MaxSyncHistorySize = 100; // Prevent unbounded growth

    #endregion

    #region Constants

    /// <summary>
    /// Timer interval for checking if sync should run (seconds).
    /// Actual sync respects MinSecondsBetweenSyncs.
    /// </summary>
    private const int TimerIntervalSeconds = 30;

    /// <summary>
    /// Minimum seconds between sync operations (rate limit).
    /// </summary>
    private const int MinSecondsBetweenSyncs = 65;

    /// <summary>
    /// Maximum syncs allowed per hour (rate limit).
    /// </summary>
    private const int MaxSyncsPerHour = 55;

    /// <summary>
    /// Maximum locations to sync per batch.
    /// </summary>
    private const int BatchSize = 50;

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    private const int MaxRetryAttempts = 3;

    /// <summary>
    /// Cleanup interval in hours (lesson learned: separate cleanup timer).
    /// </summary>
    private const int CleanupIntervalHours = 6;

    /// <summary>
    /// Initial delay before first sync/cleanup run (seconds).
    /// </summary>
    private const int InitialDelaySeconds = 5;

    /// <summary>
    /// Delay before first cleanup run (minutes).
    /// </summary>
    private const int FirstCleanupDelayMinutes = 30;

    /// <summary>
    /// Timeout for graceful Stop() waiting for in-progress sync (seconds).
    /// </summary>
    private const int StopTimeoutSeconds = 30;

    /// <summary>
    /// Poll interval when waiting for sync to complete during Stop() (milliseconds).
    /// </summary>
    private const int StopPollIntervalMs = 100;

    /// <summary>
    /// Threshold for detecting stuck syncing locations during runtime (minutes).
    /// </summary>
    private const int StuckLocationThresholdMinutes = 30;

    /// <summary>
    /// Number of days after which synced locations are purged.
    /// </summary>
    private const int PurgeDaysOld = 7;

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

    #region Constructor

    /// <summary>
    /// Creates a new instance of LocationSyncService.
    /// </summary>
    /// <param name="apiClient">API client for server communication.</param>
    /// <param name="locationQueue">Repository for location queue operations.</param>
    /// <param name="settings">Settings service for configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public LocationSyncService(
        IApiClient apiClient,
        ILocationQueueRepository locationQueue,
        ISettingsService settings,
        ILogger<LocationSyncService> logger)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(locationQueue);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _apiClient = apiClient;
        _locationQueue = locationQueue;
        _settings = settings;
        _logger = logger;

        // Configure retry pipeline with exponential backoff for transient failures
        _retryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry {RetryCount}/{MaxRetries} after {Delay}s due to: {Message}",
                        args.AttemptNumber + 1, MaxRetryAttempts, args.RetryDelay.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "Unknown");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether sync is currently in progress.
    /// </summary>
    public bool IsSyncing => _isSyncing;

    /// <summary>
    /// Gets the count of pending locations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        return await _locationQueue.GetPendingCountAsync();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts the background sync timer and cleanup timer.
    /// Thread-safe: uses lock to prevent race conditions when called concurrently.
    /// </summary>
    public void Start()
    {
        lock (_startStopLock)
        {
            if (_syncTimer != null)
                return;

            _logger.LogInformation("Starting location sync service");

            // Create cancellation token source for graceful shutdown
            _cancellationTokenSource = new CancellationTokenSource();

            // Crash recovery: Reset locations stuck in Syncing state from previous session
            // This runs async without blocking Start() to avoid delaying app startup
            _ = Task.Run(async () =>
            {
                try
                {
                    var resetCount = await _locationQueue.ResetStuckLocationsAsync();
                    if (resetCount > 0)
                    {
                        _logger.LogInformation("Crash recovery: Reset {Count} stuck locations to appropriate state", resetCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during crash recovery reset of stuck locations");
                }
            });

            // Add random jitter to initial delay to prevent timer alignment with other services
            var jitteredDelay = InitialDelaySeconds + Jitter.Next(MaxJitterSeconds);

            // Sync timer - checks every 30 seconds if sync should run
            // Note: Timer callbacks compile to async void, so we must catch all exceptions
            _syncTimer = new Timer(
                async _ =>
                {
                    try
                    {
                        await TrySyncAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in sync timer callback");
                    }
                },
                null,
                TimeSpan.FromSeconds(jitteredDelay),
                TimeSpan.FromSeconds(TimerIntervalSeconds));

            // Cleanup timer - runs every 6 hours (lesson learned: separate from sync)
            // Note: Timer callbacks compile to async void, so we must catch all exceptions
            _cleanupTimer = new Timer(
                async _ =>
                {
                    try
                    {
                        await RunCleanupAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in cleanup timer callback");
                    }
                },
                null,
                TimeSpan.FromMinutes(FirstCleanupDelayMinutes),
                TimeSpan.FromHours(CleanupIntervalHours));
        }
    }

    /// <summary>
    /// Stops the background sync timer and cleanup timer.
    /// Waits for any in-progress sync to complete.
    /// Note: This method blocks the calling thread. Use <see cref="StopAsync"/> for non-blocking stop.
    /// </summary>
    public void Stop()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Asynchronously stops the background sync timer and cleanup timer.
    /// Waits for any in-progress sync to complete without blocking the calling thread.
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping location sync service");

        // Stop timers first to prevent new syncs from starting
        _syncTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        // Signal cancellation for in-progress operations
        _cancellationTokenSource?.Cancel();

        // Wait for in-progress sync to complete (with timeout)
        if (_isSyncing)
        {
            _logger.LogDebug("Waiting for in-progress sync to complete...");
            var waitStart = DateTime.UtcNow;
            while (_isSyncing && (DateTime.UtcNow - waitStart).TotalSeconds < StopTimeoutSeconds)
            {
                await Task.Delay(StopPollIntervalMs);
            }

            if (_isSyncing)
            {
                _logger.LogWarning("Sync did not complete within timeout, proceeding with stop");
            }
        }
    }

    /// <summary>
    /// Runs cleanup of old locations and stuck location detection.
    /// </summary>
    private async Task RunCleanupAsync()
    {
        try
        {
            _logger.LogDebug("Running scheduled location cleanup");

            // Reset locations stuck in Syncing state for too long
            // This handles edge cases where sync operations fail without proper cleanup
            var resetCount = await _locationQueue.ResetTimedOutSyncingLocationsAsync(
                stuckThresholdMinutes: StuckLocationThresholdMinutes);
            if (resetCount > 0)
            {
                _logger.LogInformation("Reset {Count} timed-out syncing locations to Pending", resetCount);
            }

            await PurgeOldLocationsAsync();
        }
        catch (SQLiteException ex)
        {
            _logger.LogWarning(ex, "Database error during scheduled cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during scheduled cleanup");
        }
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Checks if sync is allowed based on rate limits.
    /// </summary>
    /// <returns>True if sync is allowed, false if rate limited.</returns>
    private bool IsRateLimitAllowed()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;

            // Check minimum time between syncs
            var secondsSinceLastSync = (now - _lastSyncTime).TotalSeconds;
            if (secondsSinceLastSync < MinSecondsBetweenSyncs)
            {
                _logger.LogDebug(
                    "Rate limited: {Seconds}s since last sync, minimum is {Min}s",
                    (int)secondsSinceLastSync, MinSecondsBetweenSyncs);
                return false;
            }

            // Clean up old history entries (older than 1 hour)
            var oneHourAgo = now.AddHours(-1);
            while (_syncHistory.Count > 0 && _syncHistory.Peek() < oneHourAgo)
            {
                _syncHistory.Dequeue();
            }

            // Check max syncs per hour
            if (_syncHistory.Count >= MaxSyncsPerHour)
            {
                _logger.LogDebug(
                    "Rate limited: {Count} syncs in last hour, maximum is {Max}",
                    _syncHistory.Count, MaxSyncsPerHour);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Records a successful sync for rate limiting.
    /// </summary>
    private void RecordSync()
    {
        lock (_rateLimitLock)
        {
            var now = DateTime.UtcNow;
            _lastSyncTime = now;
            _syncHistory.Enqueue(now);

            // Enforce max size to prevent unbounded growth from clock manipulation
            while (_syncHistory.Count > MaxSyncHistorySize)
            {
                _syncHistory.Dequeue();
            }
        }
    }

    #endregion

    #region Sync Methods

    /// <summary>
    /// Called by timer to attempt sync with rate limiting.
    /// </summary>
    private async Task TrySyncAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return;
        }

        await SyncAsync();
    }

    /// <summary>
    /// Triggers an immediate sync of pending locations.
    /// </summary>
    /// <returns>Number of locations successfully synced.</returns>
    public async Task<int> SyncAsync()
    {
        if (!_settings.TimelineTrackingEnabled || !_apiClient.IsConfigured)
        {
            _logger.LogDebug("Sync skipped - tracking disabled or API not configured");
            return 0;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("Sync already in progress");
            return 0;
        }

        try
        {
            _isSyncing = true;
            var count = await SyncPendingLocationsAsync();
            RecordSync(); // Record for rate limiting
            return count;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during location sync");
            return 0;
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error during location sync");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during location sync");
            return 0;
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    /// <summary>
    /// Syncs pending locations to the server with retry for transient failures.
    /// Uses atomic claim pattern to prevent race conditions with QueueDrainService.
    /// </summary>
    private async Task<int> SyncPendingLocationsAsync()
    {
        // CRITICAL FIX: Atomically claim locations (marks as Syncing in single operation)
        // This prevents QueueDrainService from processing the same locations
        var claimed = await _locationQueue.ClaimPendingLocationsAsync(BatchSize);
        if (claimed.Count == 0)
        {
            _logger.LogDebug("No pending locations to sync (or all claimed by drain service)");
            return 0;
        }

        _logger.LogInformation("Claimed and syncing {Count} locations", claimed.Count);

        var successfulIds = new List<int>();
        var failedIds = new List<int>();
        var rejectedIds = new HashSet<int>(); // Track rejected IDs separately

        // Capture CTS reference safely to avoid ObjectDisposedException during token access
        // If CTS is null or disposed, use CancellationToken.None for graceful handling
        var cts = _cancellationTokenSource;
        CancellationToken cancellationToken;
        try
        {
            cancellationToken = cts?.Token ?? CancellationToken.None;
        }
        catch (ObjectDisposedException)
        {
            // CTS was disposed between null check and Token access - use empty token
            cancellationToken = CancellationToken.None;
        }

        var stopProcessing = false;
        var processedCount = 0;

        try
        {
            foreach (var location in claimed)
            {
                // Check for cancellation (graceful shutdown)
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Sync cancelled, resetting remaining {Count} locations",
                        claimed.Count - processedCount);
                    stopProcessing = true;
                }

                if (stopProcessing)
                {
                    // Add unprocessed locations to failed list for reset
                    failedIds.Add(location.Id);
                    continue;
                }

                processedCount++;
                var (result, shouldContinue) = await SyncLocationWithRetryAsync(location, cancellationToken);

                switch (result)
                {
                    case SyncResult.Success:
                        successfulIds.Add(location.Id);
                        break;
                    case SyncResult.Rejected:
                        // Already marked as rejected in SyncLocationWithRetryAsync
                        // Track so we don't accidentally reset them in cleanup
                        rejectedIds.Add(location.Id);
                        break;
                    case SyncResult.Failed:
                        failedIds.Add(location.Id);
                        break;
                }

                if (!shouldContinue)
                {
                    stopProcessing = true;
                }
            }
        }
        catch (Exception ex)
        {
            // Unexpected exception during processing - reset all unprocessed claimed locations
            _logger.LogError(ex, "Unexpected error during batch sync at location {ProcessedCount}/{Total}",
                processedCount, claimed.Count);

            // Reset any claimed locations that weren't processed (not in success, failed, or rejected)
            var processedIds = new HashSet<int>(successfulIds);
            processedIds.UnionWith(failedIds);
            processedIds.UnionWith(rejectedIds);

            var unprocessedIds = claimed
                .Where(l => !processedIds.Contains(l.Id))
                .Select(l => l.Id)
                .ToList();

            if (unprocessedIds.Count > 0)
            {
                try
                {
                    await _locationQueue.ResetLocationsBatchToPendingAsync(unprocessedIds);
                    _logger.LogWarning("Reset {Count} unprocessed locations to Pending after error", unprocessedIds.Count);
                }
                catch (Exception resetEx)
                {
                    _logger.LogError(resetEx, "Failed to reset unprocessed locations - they may be stuck in Syncing state");
                }
            }

            // Still try to finalize what we processed successfully
        }

        // Batch update successful syncs
        if (successfulIds.Count > 0)
        {
            await _locationQueue.MarkLocationsSyncedAsync(successfulIds);
            _logger.LogDebug("Batch marked {Count} locations as synced", successfulIds.Count);

            // Update last sync time in settings for UI display
            _settings.LastSyncTime = DateTime.UtcNow;
        }

        // Reset failed/unprocessed locations back to Pending for retry
        // This allows QueueDrainService to pick them up later
        // Note: Rejected locations are NOT reset - they're already marked appropriately
        if (failedIds.Count > 0)
        {
            await _locationQueue.ResetLocationsBatchToPendingAsync(failedIds);
            _logger.LogDebug("Reset {Count} failed locations to Pending for retry", failedIds.Count);
        }

        _logger.LogInformation("Sync complete: {Success}/{Total} locations ({Failed} reset for retry, {Rejected} rejected)",
            successfulIds.Count, claimed.Count, failedIds.Count, rejectedIds.Count);

        return successfulIds.Count;
    }

    /// <summary>
    /// Result of syncing a single location.
    /// </summary>
    private enum SyncResult
    {
        /// <summary>Location was synced successfully.</summary>
        Success,
        /// <summary>Location was rejected by server (threshold not met, etc.) - don't retry.</summary>
        Rejected,
        /// <summary>Location sync failed (network, server error, etc.) - should retry.</summary>
        Failed
    }

    /// <summary>
    /// Syncs a single location with retry policy for transient failures.
    /// Does not update database status for success - caller handles batch updates.
    /// Rejected locations are marked in the database directly.
    /// </summary>
    /// <returns>Tuple of (SyncResult, shouldContinue).</returns>
    private async Task<(SyncResult Result, bool ShouldContinue)> SyncLocationWithRetryAsync(
        QueuedLocation location,
        CancellationToken cancellationToken)
    {
        try
        {
            // Convert to API request model
            var request = new LocationLogRequest
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Altitude = location.Altitude,
                Accuracy = location.Accuracy,
                Speed = location.Speed,
                Timestamp = location.Timestamp,
                Provider = location.Provider ?? "location-sync"
            };

            // Use Polly retry for transient network failures
            var result = await _retryPipeline.ExecuteAsync(async ct =>
                await _apiClient.LogLocationAsync(request, location.IdempotencyKey, ct), cancellationToken);

            if (result.Success)
            {
                if (result.Skipped)
                {
                    // Server received but did NOT store - mark as rejected, not synced
                    // These won't appear on server timeline
                    await _locationQueue.MarkLocationRejectedAsync(location.Id, "Server: distance/time threshold not met");
                    _logger.LogDebug("Location {Id} skipped by server (thresholds) - not marking as synced", location.Id);

                    // Notify listeners that location was skipped (for local timeline cleanup)
                    LocationSyncCallbacks.NotifyLocationSkipped(
                        location.Id,
                        location.Timestamp,
                        location.Latitude,
                        location.Longitude,
                        "Threshold not met");

                    return (SyncResult.Rejected, true); // Rejected by server, continue with next
                }

                // CRITICAL: Mark ServerConfirmed IMMEDIATELY after API success
                // This ensures crash recovery marks as Synced instead of resetting to Pending
                await _locationQueue.MarkServerConfirmedAsync(location.Id);

                _logger.LogDebug("Location {Id} synced successfully", location.Id);

                // Notify listeners that location was synced (for local timeline ServerId update)
                if (result.LocationId.HasValue)
                {
                    LocationSyncCallbacks.NotifyLocationSynced(
                        location.Id,
                        result.LocationId.Value,
                        location.Timestamp,
                        location.Latitude,
                        location.Longitude);
                }

                return (SyncResult.Success, true);
            }

            // Classify the failure
            var failureType = ClassifyFailure(result.StatusCode);

            switch (failureType)
            {
                case FailureType.ServerRejection:
                    // Server explicitly rejected - mark as rejected, don't retry
                    // Lesson learned: Use dedicated field instead of MarkLocationFailedAsync
                    await _locationQueue.MarkLocationRejectedAsync(location.Id, $"Server: {result.Message ?? "rejected"}");
                    _logger.LogWarning(
                        "Server rejected location {Id}: {Message} (HTTP {StatusCode})",
                        location.Id, result.Message, result.StatusCode);
                    return (SyncResult.Rejected, true); // Rejected, continue with next

                case FailureType.AuthenticationError:
                    // Auth error - stop all syncing
                    await _locationQueue.MarkLocationFailedAsync(location.Id, "Authentication failed");
                    _logger.LogWarning(
                        "Authentication error for location {Id}, stopping sync (HTTP {StatusCode})",
                        location.Id, result.StatusCode);
                    return (SyncResult.Failed, false); // Failed, stop syncing

                case FailureType.RateLimited:
                    // Rate limited by server - stop and wait
                    _logger.LogWarning(
                        "Server rate limited request (HTTP {StatusCode}), will retry later",
                        result.StatusCode);
                    return (SyncResult.Failed, false); // Failed, stop syncing

                case FailureType.ServerError:
                    // Server error - might be temporary, continue with next
                    await _locationQueue.IncrementRetryCountAsync(location.Id);
                    _logger.LogWarning(
                        "Server error for location {Id}: {Message} (HTTP {StatusCode})",
                        location.Id, result.Message, result.StatusCode);
                    return (SyncResult.Failed, true); // Failed, continue with next

                default:
                    await _locationQueue.MarkLocationFailedAsync(location.Id, result.Message ?? "Unknown error");
                    _logger.LogWarning(
                        "Failed to sync location {Id}: {Message}",
                        location.Id, result.Message);
                    return (SyncResult.Failed, true);
            }
        }
        catch (HttpRequestException ex)
        {
            // Network error after all retries exhausted
            await _locationQueue.IncrementRetryCountAsync(location.Id);
            _logger.LogError(ex, "Network error syncing location {Id} after retries", location.Id);
            return (SyncResult.Failed, true); // Continue with next
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await _locationQueue.IncrementRetryCountAsync(location.Id);
            _logger.LogWarning(ex, "Timeout syncing location {Id}", location.Id);
            return (SyncResult.Failed, true);
        }
        catch (Exception ex)
        {
            await _locationQueue.MarkLocationFailedAsync(location.Id, $"Unexpected: {SanitizeErrorMessage(ex.Message)}");
            _logger.LogError(ex, "Unexpected error syncing location {Id}", location.Id);
            return (SyncResult.Failed, true);
        }
    }

    /// <summary>
    /// Purges old synced locations from the database.
    /// </summary>
    private async Task PurgeOldLocationsAsync()
    {
        try
        {
            var purged = await _locationQueue.PurgeSyncedLocationsAsync(daysOld: PurgeDaysOld);
            if (purged > 0)
            {
                _logger.LogDebug("Purged {Count} old synced locations", purged);
            }
        }
        catch (SQLiteException ex)
        {
            _logger.LogWarning(ex, "Database error purging old locations");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error purging old locations");
        }
    }

    #endregion

    #region Failure Classification

    /// <summary>
    /// Types of sync failures for different handling strategies.
    /// </summary>
    private enum FailureType
    {
        /// <summary>Server explicitly rejected the request (400, 422).</summary>
        ServerRejection,

        /// <summary>Authentication/authorization failed (401, 403).</summary>
        AuthenticationError,

        /// <summary>Server rate limiting (429).</summary>
        RateLimited,

        /// <summary>Server-side error, possibly temporary (5xx).</summary>
        ServerError,

        /// <summary>Unknown or unclassified failure.</summary>
        Unknown
    }

    /// <summary>
    /// Classifies HTTP status codes into failure types.
    /// </summary>
    private static FailureType ClassifyFailure(int? statusCode)
    {
        return statusCode switch
        {
            400 or 422 => FailureType.ServerRejection,
            401 or 403 => FailureType.AuthenticationError,
            429 => FailureType.RateLimited,
            >= 500 and < 600 => FailureType.ServerError,
            _ => FailureType.Unknown
        };
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

    #region IDisposable

    /// <summary>
    /// Disposes resources. Calls Stop() first to ensure graceful shutdown.
    /// Thread-safe - uses Interlocked to prevent double-dispose race.
    /// </summary>
    public void Dispose()
    {
        // Thread-safe check-and-set to prevent double-dispose race
        if (Interlocked.Exchange(ref _disposeGuard, 1) != 0)
            return;

        // Stop first to wait for in-progress operations and stop timers
        Stop();

        _cancellationTokenSource?.Dispose();
        _syncTimer?.Dispose();
        _cleanupTimer?.Dispose();
        _syncLock.Dispose();
    }

    #endregion
}
