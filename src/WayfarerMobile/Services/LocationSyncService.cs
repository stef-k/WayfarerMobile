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
    private bool _isSyncing;
    private bool _disposed;

    // Rate limiting tracking
    private DateTime _lastSyncTime = DateTime.MinValue;
    private readonly Queue<DateTime> _syncHistory = new();
    private readonly object _rateLimitLock = new();

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
    /// </summary>
    public void Start()
    {
        if (_syncTimer != null)
            return;

        _logger.LogInformation("Starting location sync service");

        // Sync timer - checks every 30 seconds if sync should run
        _syncTimer = new Timer(
            async _ => await TrySyncAsync(),
            null,
            TimeSpan.FromSeconds(5), // Initial delay
            TimeSpan.FromSeconds(TimerIntervalSeconds));

        // Cleanup timer - runs every 6 hours (lesson learned: separate from sync)
        _cleanupTimer = new Timer(
            async _ => await RunCleanupAsync(),
            null,
            TimeSpan.FromMinutes(30), // First run after 30 minutes
            TimeSpan.FromHours(CleanupIntervalHours));
    }

    /// <summary>
    /// Stops the background sync timer and cleanup timer.
    /// </summary>
    public void Stop()
    {
        _logger.LogInformation("Stopping location sync service");
        _syncTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cleanupTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Runs cleanup of old locations (separate from sync for reliability).
    /// </summary>
    private async Task RunCleanupAsync()
    {
        try
        {
            _logger.LogDebug("Running scheduled location cleanup");
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
        var cancellationToken = CancellationToken.None;
        var stopProcessing = false;

        foreach (var location in claimed)
        {
            if (stopProcessing)
            {
                // Add unprocessed locations to failed list for reset
                failedIds.Add(location.Id);
                continue;
            }

            var (success, shouldContinue) = await SyncLocationWithRetryAsync(location, cancellationToken);

            if (success)
            {
                successfulIds.Add(location.Id);
            }
            else
            {
                failedIds.Add(location.Id);
            }

            if (!shouldContinue)
            {
                stopProcessing = true;
            }
        }

        // Batch update successful syncs
        if (successfulIds.Count > 0)
        {
            await _locationQueue.MarkLocationsSyncedAsync(successfulIds);
            _logger.LogDebug("Batch marked {Count} locations as synced", successfulIds.Count);

            // Update last sync time in settings for UI display
            _settings.LastSyncTime = DateTime.UtcNow;
        }

        // CRITICAL: Reset failed/unprocessed locations back to Pending for retry
        // This allows QueueDrainService to pick them up later
        if (failedIds.Count > 0)
        {
            await _locationQueue.ResetLocationsBatchToPendingAsync(failedIds);
            _logger.LogDebug("Reset {Count} failed locations to Pending for retry", failedIds.Count);
        }

        _logger.LogInformation("Sync complete: {Success}/{Total} locations ({Failed} reset for retry)",
            successfulIds.Count, claimed.Count, failedIds.Count);

        // Cleanup old synced locations
        await PurgeOldLocationsAsync();

        return successfulIds.Count;
    }

    /// <summary>
    /// Syncs a single location with retry policy for transient failures.
    /// Does not update database status - caller handles batch updates for successful syncs.
    /// </summary>
    /// <returns>Tuple of (success, shouldContinue).</returns>
    private async Task<(bool Success, bool ShouldContinue)> SyncLocationWithRetryAsync(
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
                Provider = location.Provider
            };

            // Use Polly retry for transient network failures
            var result = await _retryPipeline.ExecuteAsync(async ct =>
                await _apiClient.LogLocationAsync(request, ct), cancellationToken);

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
                        "Threshold not met");

                    return (false, true); // Not success (won't be marked synced), continue with next
                }

                _logger.LogDebug("Location {Id} synced successfully", location.Id);

                // Notify listeners that location was synced (for local timeline ServerId update)
                if (result.LocationId.HasValue)
                {
                    LocationSyncCallbacks.NotifyLocationSynced(
                        location.Id,
                        result.LocationId.Value,
                        location.Timestamp);
                }

                return (true, true);
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
                    return (false, true); // Continue with next location

                case FailureType.AuthenticationError:
                    // Auth error - stop all syncing
                    await _locationQueue.MarkLocationFailedAsync(location.Id, "Authentication failed");
                    _logger.LogWarning(
                        "Authentication error for location {Id}, stopping sync (HTTP {StatusCode})",
                        location.Id, result.StatusCode);
                    return (false, false); // Stop syncing

                case FailureType.RateLimited:
                    // Rate limited by server - stop and wait
                    _logger.LogWarning(
                        "Server rate limited request (HTTP {StatusCode}), will retry later",
                        result.StatusCode);
                    return (false, false); // Stop syncing

                case FailureType.ServerError:
                    // Server error - might be temporary, continue with next
                    await _locationQueue.IncrementRetryCountAsync(location.Id);
                    _logger.LogWarning(
                        "Server error for location {Id}: {Message} (HTTP {StatusCode})",
                        location.Id, result.Message, result.StatusCode);
                    return (false, true); // Continue with next location

                default:
                    await _locationQueue.MarkLocationFailedAsync(location.Id, result.Message ?? "Unknown error");
                    _logger.LogWarning(
                        "Failed to sync location {Id}: {Message}",
                        location.Id, result.Message);
                    return (false, true);
            }
        }
        catch (HttpRequestException ex)
        {
            // Network error after all retries exhausted
            await _locationQueue.IncrementRetryCountAsync(location.Id);
            _logger.LogError(ex, "Network error syncing location {Id} after retries", location.Id);
            return (false, true); // Continue with next
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await _locationQueue.IncrementRetryCountAsync(location.Id);
            _logger.LogWarning(ex, "Timeout syncing location {Id}", location.Id);
            return (false, true);
        }
        catch (Exception ex)
        {
            await _locationQueue.MarkLocationFailedAsync(location.Id, $"Unexpected: {ex.Message}");
            _logger.LogError(ex, "Unexpected error syncing location {Id}", location.Id);
            return (false, true);
        }
    }

    /// <summary>
    /// Purges old synced locations from the database.
    /// </summary>
    private async Task PurgeOldLocationsAsync()
    {
        try
        {
            var purged = await _locationQueue.PurgeSyncedLocationsAsync(daysOld: 7);
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

    #region IDisposable

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _syncTimer?.Dispose();
        _cleanupTimer?.Dispose();
        _syncLock.Dispose();
    }

    #endregion
}
