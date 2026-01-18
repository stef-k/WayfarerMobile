using System.Text.Json;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for synchronizing timeline location changes with the server.
/// Implements optimistic UI updates with offline queue for resilience.
/// Provides autonomous background processing via timer and drain loop.
///
/// Sync Strategy:
/// 1. Apply optimistic UI update immediately (caller responsibility)
/// 2. Save to local database (both PendingTimelineMutation and LocalTimelineEntry)
/// 3. Attempt server sync in background
/// 4. On 4xx error: Server rejected - revert changes in LocalTimelineEntry, notify caller
/// 5. On 5xx/network error: Queue for retry when online (LocalTimelineEntry keeps optimistic values)
///
/// Background Processing:
/// - Timer-based processing every 60 seconds (lower priority than location sync)
/// - Self-contained connectivity subscription (no ViewModel dependency)
/// - Drain loop for processing multiple pending mutations
/// - Piggybacks on location service wakeups for background sync
///
/// Rollback data is persisted in PendingTimelineMutation to survive app restarts.
/// </summary>
public sealed class TimelineSyncService : ITimelineSyncService
{
    #region Constants

    /// <summary>
    /// Minimum seconds between sync requests.
    /// Timeline mutations are low-volume, so 5s is sufficient.
    /// </summary>
    private const int MinSecondsBetweenSyncs = 5;

    /// <summary>
    /// Timer interval for checking queue (seconds).
    /// Lower priority than location sync (60s vs 30s).
    /// </summary>
    private const int TimerIntervalSeconds = 60;

    /// <summary>
    /// Initial delay before first timer run (seconds).
    /// Longer than QueueDrainService to let it establish first.
    /// </summary>
    private const int InitialDelaySeconds = 10;

    /// <summary>
    /// Maximum jitter to add to initial delay (seconds).
    /// Prevents timer alignment with other services.
    /// </summary>
    private const int MaxJitterSeconds = 5;

    /// <summary>
    /// Maximum consecutive failures before pausing.
    /// Lower than QueueDrainService since mutations are fewer.
    /// </summary>
    private const int MaxConsecutiveFailures = 3;

    /// <summary>
    /// Timeout for sync operations (milliseconds).
    /// </summary>
    private const int SyncTimeoutMs = 10000;

    /// <summary>
    /// Timeout for acquiring drain lock (milliseconds).
    /// </summary>
    private const int DrainLockTimeoutMs = 100;

    /// <summary>
    /// Maximum consecutive failures in drain loop before exiting.
    /// </summary>
    private const int MaxConsecutiveLoopFailures = 3;

    /// <summary>
    /// Delay between drain loop iterations (milliseconds).
    /// Higher than QueueDrainService for lower priority.
    /// </summary>
    private const int DrainLoopDelayMs = 500;

    /// <summary>
    /// Random generator for jitter (thread-safe in .NET 6+).
    /// </summary>
    private static readonly Random Jitter = new();

    #endregion

    #region Fields

    private readonly IApiClient _apiClient;
    private readonly ITimelineRepository _timelineRepository;
    private readonly DatabaseService _databaseService;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<TimelineSyncService> _logger;

    private readonly SemaphoreSlim _drainLock = new(1, 1);

    private SQLiteAsyncConnection? _database;
    private Timer? _drainTimer;
    private CancellationTokenSource? _timerCts;
    private DateTime _lastSyncTime = DateTime.MinValue;
    private int _consecutiveFailures;
    private volatile bool _isOnline;
    private volatile bool _isDisposed;
    private volatile bool _isStarted;
    private bool _initialized;
    private int _disposeGuard; // For thread-safe Dispose via Interlocked
    private int _drainLoopRunning; // For thread-safe drain loop guard via Interlocked

    #endregion

    #region Events

    /// <summary>
    /// Event raised when a sync operation fails with server rejection (4xx).
    /// Caller should revert optimistic UI updates.
    /// </summary>
    public event EventHandler<SyncFailureEventArgs>? SyncRejected;

    /// <summary>
    /// Event raised when a sync is queued for offline retry.
    /// </summary>
    public event EventHandler<SyncQueuedEventArgs>? SyncQueued;

    /// <summary>
    /// Event raised when a sync completes successfully.
    /// </summary>
    public event EventHandler<SyncSuccessEventArgs>? SyncCompleted;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TimelineSyncService.
    /// </summary>
    /// <param name="apiClient">API client for server communication.</param>
    /// <param name="timelineRepository">Repository for timeline operations.</param>
    /// <param name="databaseService">Database service for connection access.</param>
    /// <param name="connectivity">Connectivity service for network state.</param>
    /// <param name="logger">Logger instance.</param>
    public TimelineSyncService(
        IApiClient apiClient,
        ITimelineRepository timelineRepository,
        DatabaseService databaseService,
        IConnectivity connectivity,
        ILogger<TimelineSyncService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _isOnline = _connectivity.NetworkAccess == NetworkAccess.Internet;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets whether the drain loop is currently running.
    /// </summary>
    /// <remarks>
    /// Used by callers to avoid unnecessary <see cref="StartDrainLoop"/> calls
    /// when the loop is already active.
    /// </remarks>
    public bool IsDrainLoopRunning => Volatile.Read(ref _drainLoopRunning) != 0;

    #endregion

    #region Lifecycle Methods

    /// <summary>
    /// Starts the timeline sync service.
    /// Should be called after authentication is configured.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isDisposed)
        {
            _logger.LogWarning("Cannot start disposed TimelineSyncService");
            return;
        }

        if (_isStarted)
        {
            _logger.LogDebug("TimelineSyncService already started");
            return;
        }

        try
        {
            _logger.LogInformation("Starting TimelineSyncService");

            // Ensure database is initialized
            await EnsureInitializedAsync();

            // Subscribe to connectivity changes
            _connectivity.ConnectivityChanged += OnConnectivityChanged;

            // Create cancellation token for timer callbacks
            _timerCts = new CancellationTokenSource();

            // Add random jitter to initial delay to prevent timer alignment
            var jitteredDelay = InitialDelaySeconds + Jitter.Next(MaxJitterSeconds);

            // Start the drain timer
            _drainTimer = new Timer(
                OnDrainTimerElapsed,
                null,
                TimeSpan.FromSeconds(jitteredDelay),
                TimeSpan.FromSeconds(TimerIntervalSeconds));

            _isStarted = true;
            _logger.LogInformation("TimelineSyncService started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start TimelineSyncService");
        }
    }

    /// <summary>
    /// Stops the timeline sync service.
    /// </summary>
    public void Stop()
    {
        if (!_isStarted)
            return;

        _logger.LogInformation("Stopping TimelineSyncService");

        // Cancel any pending timer callbacks first
        _timerCts?.Cancel();

        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _drainTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _drainTimer?.Dispose();
        _drainTimer = null;

        _timerCts?.Dispose();
        _timerCts = null;

        _isStarted = false;
        _logger.LogInformation("TimelineSyncService stopped");
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

    #region Timer Callback

    /// <summary>
    /// Timer callback - attempts to drain pending mutations.
    /// CRITICAL: Must catch all exceptions to protect background services.
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
            // CRITICAL: Never let timer callback exceptions crash the app
            _logger.LogError(ex, "Unhandled exception in timeline sync timer callback");
        }
    }

    #endregion

    #region Connectivity

    /// <summary>
    /// Handles connectivity state changes.
    /// CRITICAL: Must catch all exceptions (async void pattern).
    /// </summary>
    /// <remarks>
    /// Intentionally uses only <see cref="NetworkAccess.Internet"/> and excludes
    /// <see cref="NetworkAccess.ConstrainedInternet"/> to avoid syncing on metered
    /// or restricted connections. The ViewModel may show "online" for UI purposes
    /// while this service waits for full connectivity.
    /// </remarks>
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        try
        {
            var wasOnline = _isOnline;
            _isOnline = e.NetworkAccess == NetworkAccess.Internet;

            if (!wasOnline && _isOnline)
            {
                _logger.LogInformation("Network restored, timeline sync will resume");
                // Reset consecutive failures on network restore
                Interlocked.Exchange(ref _consecutiveFailures, 0);

                // Start drain loop to process any pending mutations
                StartDrainLoop();
            }
            else if (wasOnline && !_isOnline)
            {
                _logger.LogInformation("Network lost, timeline sync paused");
            }
        }
        catch (Exception ex)
        {
            // CRITICAL: Never let connectivity handler exceptions propagate
            _logger.LogError(ex, "Error handling connectivity change in TimelineSyncService");
        }
    }

    #endregion

    #region Drain Loop

    /// <summary>
    /// Starts a drain loop if not already running. Safe to call frequently.
    /// Called by background location services to piggyback on location wakeups.
    /// </summary>
    /// <remarks>
    /// CRITICAL: This method is called from background location services.
    /// It MUST be completely fire-and-forget and NEVER throw exceptions.
    /// </remarks>
    public void StartDrainLoop()
    {
        // Early exit if not ready
        if (_isDisposed || !_isStarted)
            return;

        // Already running? Do nothing.
        if (Interlocked.CompareExchange(ref _drainLoopRunning, 1, 0) != 0)
            return;

        // Fire-and-forget with full isolation
        _ = Task.Run(async () =>
        {
            try
            {
                await DrainLoopAsync();
            }
            catch (Exception ex)
            {
                // CRITICAL: Never propagate exceptions to caller
                _logger.LogError(ex, "Timeline sync drain loop crashed unexpectedly");
            }
            finally
            {
                Interlocked.Exchange(ref _drainLoopRunning, 0);
            }
        });
    }

    /// <summary>
    /// Triggers an immediate drain cycle outside the normal timer schedule.
    /// Used by AppLifecycleService to flush pending mutations on suspend/resume.
    /// </summary>
    /// <remarks>
    /// This is a best-effort operation that respects rate limits.
    /// If the service is not started or disposed, returns immediately.
    /// </remarks>
    public async Task TriggerDrainAsync()
    {
        if (_isDisposed || !_isStarted)
        {
            _logger.LogDebug("TriggerDrainAsync: Service not ready (disposed={Disposed}, started={Started})",
                _isDisposed, _isStarted);
            return;
        }

        var cts = _timerCts;
        if (cts == null || cts.IsCancellationRequested)
            return;

        try
        {
            _logger.LogDebug("TriggerDrainAsync: Manual drain triggered");
            await DrainOneAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown, ignore
        }
        catch (Exception ex)
        {
            // Never throw from TriggerDrainAsync
            _logger.LogError(ex, "Error during manual timeline sync trigger");
        }
    }

    /// <summary>
    /// Internal drain loop that keeps draining until queue is empty or conditions prevent further draining.
    /// </summary>
    private async Task DrainLoopAsync()
    {
        var loopFailures = 0;

        _logger.LogDebug("Timeline sync drain loop started");

        while (!_isDisposed && _isStarted)
        {
            try
            {
                // Check 1: Connectivity
                if (!_isOnline)
                {
                    _logger.LogDebug("Timeline sync drain loop: Offline, exiting");
                    break;
                }

                // Check 2: API configured
                if (!_apiClient.IsConfigured)
                {
                    _logger.LogDebug("Timeline sync drain loop: API not configured, exiting");
                    break;
                }

                // Check 3: Queue size
                var pendingCount = await GetPendingCountAsync();
                if (pendingCount == 0)
                {
                    _logger.LogDebug("Timeline sync drain loop: Queue empty, exiting");
                    break;
                }

                // Check 4: Rate limit
                if (!CanMakeSyncRequest())
                {
                    // Wait until rate limit allows, then continue
                    var waitTime = GetTimeUntilNextAllowedSync();
                    _logger.LogDebug("Timeline sync drain loop: Rate limited, waiting {Seconds}s", waitTime.TotalSeconds);
                    await Task.Delay(waitTime);
                    continue;
                }

                // Check 5: Too many failures
                if (loopFailures >= MaxConsecutiveLoopFailures)
                {
                    _logger.LogWarning("Timeline sync drain loop: Too many failures ({Count}), exiting", loopFailures);
                    break;
                }

                // Attempt drain
                var cts = _timerCts;
                if (cts == null || cts.IsCancellationRequested)
                    break;

                var success = await DrainOneWithResultAsync(cts.Token);

                if (success)
                {
                    loopFailures = 0; // Reset on success
                }
                else
                {
                    loopFailures++;
                }

                // Delay between iterations (lower priority than location sync)
                await Task.Delay(DrainLoopDelayMs);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Timeline sync drain loop: Cancelled");
                break;
            }
            catch (Exception ex)
            {
                // Log but continue - one bad iteration should not kill the loop
                _logger.LogWarning(ex, "Timeline sync drain loop iteration failed");
                loopFailures++;

                if (loopFailures >= MaxConsecutiveLoopFailures)
                    break;

                // Back off on errors
                await Task.Delay(TimeSpan.FromSeconds(loopFailures * 2));
            }
        }

        _logger.LogDebug("Timeline sync drain loop exited");
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Checks if a sync request can be made based on rate limits.
    /// </summary>
    private bool CanMakeSyncRequest()
    {
        var now = DateTime.UtcNow;
        return (now - _lastSyncTime).TotalSeconds >= MinSecondsBetweenSyncs;
    }

    /// <summary>
    /// Records a sync attempt for rate limiting.
    /// </summary>
    private void RecordSyncAttempt()
    {
        _lastSyncTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Calculates time until next sync is allowed by rate limit.
    /// </summary>
    private TimeSpan GetTimeUntilNextAllowedSync()
    {
        var elapsed = DateTime.UtcNow - _lastSyncTime;
        var remaining = TimeSpan.FromSeconds(MinSecondsBetweenSyncs) - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    #endregion

    #region Drain Logic

    /// <summary>
    /// Result of attempting to process a mutation.
    /// </summary>
    private enum DrainAttemptResult
    {
        /// <summary>Mutation was processed (success or rejection).</summary>
        Processed,

        /// <summary>Skipped due to early exit condition (offline, rate limited, etc.).</summary>
        Skipped,

        /// <summary>Failed during processing.</summary>
        Failed
    }

    /// <summary>
    /// Attempts to drain one mutation from the queue.
    /// </summary>
    private async Task DrainOneAsync(CancellationToken cancellationToken)
    {
        await ClaimAndProcessOneMutationAsync(cancellationToken, verboseLogging: true);
    }

    /// <summary>
    /// Drains one mutation and returns success/failure.
    /// </summary>
    private async Task<bool> DrainOneWithResultAsync(CancellationToken cancellationToken)
    {
        var result = await ClaimAndProcessOneMutationAsync(cancellationToken, verboseLogging: false);
        return result == DrainAttemptResult.Processed;
    }

    /// <summary>
    /// Claims and processes a single mutation from the queue.
    /// </summary>
    private async Task<DrainAttemptResult> ClaimAndProcessOneMutationAsync(
        CancellationToken cancellationToken,
        bool verboseLogging)
    {
        // Early exits
        if (!_isOnline)
        {
            if (verboseLogging)
                _logger.LogDebug("TimelineSync: Offline, skipping cycle");
            return DrainAttemptResult.Skipped;
        }

        if (!_apiClient.IsConfigured)
        {
            if (verboseLogging)
                _logger.LogDebug("TimelineSync: API not configured, skipping cycle");
            return DrainAttemptResult.Skipped;
        }

        var failures = Volatile.Read(ref _consecutiveFailures);
        if (failures >= MaxConsecutiveFailures)
        {
            if (verboseLogging)
            {
                _logger.LogDebug(
                    "TimelineSync: Too many consecutive failures ({Failures}), backing off",
                    failures);
            }
            return DrainAttemptResult.Skipped;
        }

        if (!CanMakeSyncRequest())
        {
            if (verboseLogging)
            {
                var timeSinceLast = DateTime.UtcNow - _lastSyncTime;
                _logger.LogDebug(
                    "TimelineSync: Rate limited ({SecondsSinceLast:F0}s since last, need {Required}s)",
                    timeSinceLast.TotalSeconds, MinSecondsBetweenSyncs);
            }
            return DrainAttemptResult.Skipped;
        }

        // Try to acquire lock with timeout (non-blocking if busy)
        if (!await _drainLock.WaitAsync(DrainLockTimeoutMs, cancellationToken))
        {
            if (verboseLogging)
                _logger.LogDebug("Timeline sync drain lock busy, skipping cycle");
            return DrainAttemptResult.Skipped;
        }

        PendingTimelineMutation? mutation = null;

        try
        {
            await EnsureInitializedAsync();

            // Get next pending mutation
            mutation = await _database!.Table<PendingTimelineMutation>()
                .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
                .OrderBy(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            if (mutation == null)
            {
                if (verboseLogging)
                    _logger.LogDebug("TimelineSync: No pending mutations");
                return DrainAttemptResult.Skipped;
            }

            _logger.LogDebug(
                "TimelineSync: Processing mutation {Id} for location {LocationId}",
                mutation.Id, mutation.LocationId);
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting pending mutation");
            return DrainAttemptResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting pending mutation");
            return DrainAttemptResult.Failed;
        }
        finally
        {
            _drainLock.Release();
        }

        // Process mutation outside lock (network call)
        try
        {
            await ProcessMutationAsync(mutation, cancellationToken);
            return DrainAttemptResult.Processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing mutation {Id}", mutation.Id);
            return DrainAttemptResult.Failed;
        }
    }

    /// <summary>
    /// Processes a single mutation - syncs to server.
    /// </summary>
    private async Task ProcessMutationAsync(PendingTimelineMutation mutation, CancellationToken cancellationToken)
    {
        // Record rate limit before API call
        RecordSyncAttempt();

        try
        {
            mutation.SyncAttempts++;
            mutation.LastSyncAttempt = DateTime.UtcNow;

            // TODO: Pass cancellation token to API client when it supports cancellation.
            // For now, timeouts are handled by HttpClient's default timeout.
            _ = cancellationToken; // Suppress unused parameter warning

            bool success;
            if (mutation.OperationType == "Delete")
            {
                success = await _apiClient.DeleteTimelineLocationAsync(mutation.LocationId);
            }
            else
            {
                var request = new TimelineLocationUpdateRequest
                {
                    Latitude = mutation.Latitude,
                    Longitude = mutation.Longitude,
                    LocalTimestamp = mutation.LocalTimestamp,
                    Notes = mutation.IncludeNotes ? mutation.Notes : null,
                    ActivityTypeId = mutation.ActivityTypeId,
                    ClearActivity = mutation.ClearActivity ? true : null
                };

                var response = await _apiClient.UpdateTimelineLocationAsync(mutation.LocationId, request);
                success = response?.Success == true;
            }

            if (success)
            {
                // Success - remove from queue
                await _database!.DeleteAsync(mutation);
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                _logger.LogInformation(
                    "TimelineSync: Mutation {Id} for location {LocationId} synced successfully",
                    mutation.Id, mutation.LocationId);
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
            }
            else
            {
                // No response - will retry
                mutation.LastError = "No response from server";
                await _database!.UpdateAsync(mutation);
                Interlocked.Increment(ref _consecutiveFailures);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown - will retry on next start
            mutation.LastError = "Service shutdown";
            await _database!.UpdateAsync(mutation);
            throw; // Re-throw to propagate cancellation
        }
        catch (OperationCanceledException)
        {
            // Timeout - will retry
            mutation.LastError = "Request timed out";
            await _database!.UpdateAsync(mutation);
            Interlocked.Increment(ref _consecutiveFailures);
            _logger.LogWarning("TimelineSync: Mutation {Id} timed out", mutation.Id);
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // Server rejected - mark as rejected and revert local changes
            mutation.IsRejected = true;
            mutation.RejectionReason = $"Server: {ex.Message}";
            mutation.LastError = ex.Message;
            await _database!.UpdateAsync(mutation);

            await RevertLocalEntryFromMutationAsync(mutation);

            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _logger.LogWarning(
                "TimelineSync: Mutation {Id} rejected by server: {Message}",
                mutation.Id, ex.Message);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = ex.Message,
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            // Network error - will retry
            mutation.LastError = $"Network error: {ex.Message}";
            await _database!.UpdateAsync(mutation);
            Interlocked.Increment(ref _consecutiveFailures);
            _logger.LogWarning("TimelineSync: Network error for mutation {Id}: {Message}", mutation.Id, ex.Message);
        }
        catch (Exception ex)
        {
            // Unexpected error - will retry
            mutation.LastError = $"Unexpected: {ex.Message}";
            await _database!.UpdateAsync(mutation);
            Interlocked.Increment(ref _consecutiveFailures);
            _logger.LogError(ex, "TimelineSync: Unexpected error processing mutation {Id}", mutation.Id);
        }
    }

    #endregion

    #region Database Initialization

    /// <summary>
    /// Ensures the database connection is initialized.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database = await _databaseService.GetConnectionAsync();
        await _database.CreateTableAsync<PendingTimelineMutation>();
        _initialized = true;
    }

    #endregion

    #region Public Mutation Methods

    /// <summary>
    /// Updates a timeline location with optimistic UI pattern.
    /// Also updates LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task UpdateLocationAsync(
        int locationId,
        double? latitude = null,
        double? longitude = null,
        DateTime? localTimestamp = null,
        string? notes = null,
        bool includeNotes = false,
        int? activityTypeId = null,
        bool clearActivity = false,
        string? activityTypeName = null)
    {
        await EnsureInitializedAsync();

        // Get original values for rollback before applying changes
        var originalValues = await GetOriginalValuesAsync(locationId);

        // Apply optimistic update to LocalTimelineEntry
        await ApplyLocalEntryUpdateAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeName, clearActivity);

        // Build request
        var request = new TimelineLocationUpdateRequest
        {
            Latitude = latitude,
            Longitude = longitude,
            LocalTimestamp = localTimestamp,
            Notes = includeNotes ? notes : null,
            ActivityTypeId = activityTypeId,
            ClearActivity = clearActivity ? true : null
        };

        // Check connectivity first
        if (!_isOnline)
        {
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Saved offline - will sync when online" });
            return;
        }

        // Try server sync
        try
        {
            var response = await _apiClient.UpdateTimelineLocationAsync(locationId, request);

            if (response != null && response.Success)
            {
                // Success - no need to store rollback data
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            // Null or failed response - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Sync failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, revert local changes using original values
            await RevertLocalEntryFromValuesAsync(locationId, originalValues);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected changes: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            // Network error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Timeout - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            // Unexpected error - queue for retry (keep local changes)
            await EnqueueMutationWithRollbackAsync(locationId, latitude, longitude, localTimestamp, notes, includeNotes, activityTypeId, clearActivity, originalValues);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Deletes a timeline location with optimistic UI pattern.
    /// Also deletes from LocalTimelineEntry for offline viewing consistency.
    /// </summary>
    public async Task DeleteLocationAsync(int locationId)
    {
        await EnsureInitializedAsync();

        // Get the full entry before deleting (for rollback)
        var deletedEntryJson = await GetDeletedEntryJsonAsync(locationId);

        // Delete from local storage
        await ApplyLocalEntryDeleteAsync(locationId);

        if (!_isOnline)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Deleted offline - will sync when online" });
            return;
        }

        try
        {
            var success = await _apiClient.DeleteTimelineLocationAsync(locationId);

            if (success)
            {
                // Success - no rollback needed
                SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = Guid.Empty });
                return;
            }

            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs { EntityId = Guid.Empty, Message = "Delete failed - will retry" });
        }
        catch (HttpRequestException ex) when (IsClientError(ex))
        {
            // 4xx error - server rejected, restore local entry from JSON
            await RestoreDeletedEntryAsync(deletedEntryJson);

            SyncRejected?.Invoke(this, new SyncFailureEventArgs
            {
                EntityId = Guid.Empty,
                ErrorMessage = $"Server rejected: {ex.Message}",
                IsClientError = true
            });
        }
        catch (HttpRequestException ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Network error: {ex.Message} - will retry"
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = "Request timed out - will retry"
            });
        }
        catch (Exception ex)
        {
            await EnqueueDeleteMutationWithRollbackAsync(locationId, deletedEntryJson);
            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = Guid.Empty,
                Message = $"Unexpected error: {ex.Message} - will retry"
            });
        }
    }

    /// <summary>
    /// Process pending mutations (call when connectivity is restored).
    /// Uses persisted rollback data from mutations to revert on server rejection.
    /// </summary>
    /// <remarks>
    /// This method is kept for backward compatibility but the service now
    /// processes mutations automatically via timer and drain loop.
    /// Prefer calling <see cref="StartDrainLoop"/> directly for fire-and-forget,
    /// or <see cref="TriggerDrainAsync"/> for awaitable single-cycle drain.
    /// </remarks>
    [Obsolete("Use StartDrainLoop() for fire-and-forget or TriggerDrainAsync() for awaitable drain.")]
    public Task ProcessPendingMutationsAsync()
    {
        // Start the drain loop which will process all pending mutations
        StartDrainLoop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get count of pending mutations.
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await EnsureInitializedAsync();
        // Inline CanSync expression - SQLite-net can't translate computed properties
        return await _database!.Table<PendingTimelineMutation>()
            .Where(m => !m.IsRejected && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
            .CountAsync();
    }

    /// <summary>
    /// Clear rejected mutations (user acknowledged).
    /// </summary>
    public async Task ClearRejectedMutationsAsync()
    {
        await EnsureInitializedAsync();
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.IsRejected)
            .DeleteAsync();
    }

    #endregion

    #region Helpers

    private static bool IsClientError(HttpRequestException ex)
    {
        // Check if it's a 4xx status code
        return ex.StatusCode.HasValue &&
               (int)ex.StatusCode.Value >= 400 &&
               (int)ex.StatusCode.Value < 500;
    }

    #endregion

    #region LocalTimelineEntry Integration (Persisted Rollback)

    /// <summary>
    /// Gets original values from LocalTimelineEntry for rollback support.
    /// </summary>
    private async Task<(int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType)> GetOriginalValuesAsync(int locationId)
    {
        var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null)
            return (null, null, null, null, null, null);

        return (localEntry.Id, localEntry.Latitude, localEntry.Longitude, localEntry.Timestamp, localEntry.Notes, localEntry.ActivityType);
    }

    /// <summary>
    /// Applies an update to the local timeline entry (optimistic update).
    /// </summary>
    private async Task ApplyLocalEntryUpdateAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes,
        string? activityTypeName,
        bool clearActivity)
    {
        var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        // Apply optimistic update
        if (latitude.HasValue) localEntry.Latitude = latitude.Value;
        if (longitude.HasValue) localEntry.Longitude = longitude.Value;
        if (localTimestamp.HasValue) localEntry.Timestamp = localTimestamp.Value;
        if (includeNotes) localEntry.Notes = notes;
        // Activity: set name if provided, clear if requested
        if (!string.IsNullOrEmpty(activityTypeName)) localEntry.ActivityType = activityTypeName;
        if (clearActivity) localEntry.ActivityType = null;

        await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Enqueues a mutation with rollback data persisted in the mutation entity.
    /// </summary>
    private async Task EnqueueMutationWithRollbackAsync(
        int locationId,
        double? latitude,
        double? longitude,
        DateTime? localTimestamp,
        string? notes,
        bool includeNotes,
        int? activityTypeId,
        bool clearActivity,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType) originalValues)
    {
        // Check if there's already a pending mutation for this location
        var existing = await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId && !m.IsRejected)
            .FirstOrDefaultAsync();

        if (existing != null)
        {
            // Merge with existing mutation (latest values win, keep original rollback data)
            if (latitude.HasValue) existing.Latitude = latitude;
            if (longitude.HasValue) existing.Longitude = longitude;
            if (localTimestamp.HasValue) existing.LocalTimestamp = localTimestamp;
            if (includeNotes)
            {
                existing.Notes = notes;
                existing.IncludeNotes = true;
            }
            // Activity: setting an activity clears the clear flag, clearing removes any pending activity
            if (activityTypeId.HasValue)
            {
                existing.ActivityTypeId = activityTypeId;
                existing.ClearActivity = false;
            }
            if (clearActivity)
            {
                existing.ClearActivity = true;
                existing.ActivityTypeId = null;
            }
            existing.CreatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
        }
        else
        {
            var mutation = new PendingTimelineMutation
            {
                OperationType = "Update",
                LocationId = locationId,
                LocalEntryId = originalValues.localEntryId,
                Latitude = latitude,
                Longitude = longitude,
                LocalTimestamp = localTimestamp,
                Notes = notes,
                IncludeNotes = includeNotes,
                ActivityTypeId = activityTypeId,
                ClearActivity = clearActivity,
                // Persist original values for rollback
                OriginalLatitude = originalValues.lat,
                OriginalLongitude = originalValues.lng,
                OriginalTimestamp = originalValues.timestamp,
                OriginalNotes = originalValues.notes,
                OriginalActivityType = originalValues.activityType,
                CreatedAt = DateTime.UtcNow
            };
            await _database.InsertAsync(mutation);
        }
    }

    /// <summary>
    /// Reverts local entry using provided original values.
    /// </summary>
    private async Task RevertLocalEntryFromValuesAsync(
        int locationId,
        (int? localEntryId, double? lat, double? lng, DateTime? timestamp, string? notes, string? activityType) originalValues)
    {
        if (!originalValues.localEntryId.HasValue) return;

        var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        if (originalValues.lat.HasValue) localEntry.Latitude = originalValues.lat.Value;
        if (originalValues.lng.HasValue) localEntry.Longitude = originalValues.lng.Value;
        if (originalValues.timestamp.HasValue) localEntry.Timestamp = originalValues.timestamp.Value;
        localEntry.Notes = originalValues.notes;
        localEntry.ActivityType = originalValues.activityType;

        await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
    }

    /// <summary>
    /// Gets the full entry serialized as JSON for delete rollback.
    /// </summary>
    private async Task<string?> GetDeletedEntryJsonAsync(int locationId)
    {
        var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return null;

        return JsonSerializer.Serialize(localEntry);
    }

    /// <summary>
    /// Deletes the local timeline entry (optimistic delete).
    /// </summary>
    private async Task ApplyLocalEntryDeleteAsync(int locationId)
    {
        var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(locationId);
        if (localEntry == null) return;

        await _timelineRepository.DeleteLocalTimelineEntryAsync(localEntry.Id);
    }

    /// <summary>
    /// Enqueues a delete mutation with rollback data (full entry as JSON).
    /// </summary>
    private async Task EnqueueDeleteMutationWithRollbackAsync(int locationId, string? deletedEntryJson)
    {
        // Remove any pending updates for this location
        await _database!.Table<PendingTimelineMutation>()
            .Where(m => m.LocationId == locationId)
            .DeleteAsync();

        var mutation = new PendingTimelineMutation
        {
            OperationType = "Delete",
            LocationId = locationId,
            DeletedEntryJson = deletedEntryJson,
            CreatedAt = DateTime.UtcNow
        };
        await _database.InsertAsync(mutation);
    }

    /// <summary>
    /// Restores a deleted entry from JSON.
    /// </summary>
    private async Task RestoreDeletedEntryAsync(string? deletedEntryJson)
    {
        if (string.IsNullOrEmpty(deletedEntryJson)) return;

        try
        {
            var entry = JsonSerializer.Deserialize<LocalTimelineEntry>(deletedEntryJson);
            if (entry == null) return;

            entry.Id = 0; // Reset ID for new insert
            await _timelineRepository.InsertLocalTimelineEntryAsync(entry);
        }
        catch (JsonException)
        {
            // JSON deserialization failed - entry cannot be restored
        }
    }

    /// <summary>
    /// Reverts local entry using rollback data persisted in the mutation.
    /// </summary>
    private async Task RevertLocalEntryFromMutationAsync(PendingTimelineMutation mutation)
    {
        if (mutation.OperationType == "Delete")
        {
            // Restore deleted entry from JSON
            await RestoreDeletedEntryAsync(mutation.DeletedEntryJson);
        }
        else
        {
            // Revert updated fields
            if (!mutation.HasRollbackData) return;

            var localEntry = await _timelineRepository.GetLocalTimelineEntryByServerIdAsync(mutation.LocationId);
            if (localEntry == null) return;

            if (mutation.OriginalLatitude.HasValue) localEntry.Latitude = mutation.OriginalLatitude.Value;
            if (mutation.OriginalLongitude.HasValue) localEntry.Longitude = mutation.OriginalLongitude.Value;
            if (mutation.OriginalTimestamp.HasValue) localEntry.Timestamp = mutation.OriginalTimestamp.Value;
            localEntry.Notes = mutation.OriginalNotes;
            localEntry.ActivityType = mutation.OriginalActivityType;

            await _timelineRepository.UpdateLocalTimelineEntryAsync(localEntry);
        }
    }

    #endregion
}
