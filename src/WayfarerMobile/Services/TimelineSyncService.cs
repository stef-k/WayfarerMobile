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
public sealed partial class TimelineSyncService : ITimelineSyncService
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
    private int _startGuard; // For thread-safe StartAsync via Interlocked
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

        // Atomic guard: only one caller can enter startup
        if (Interlocked.CompareExchange(ref _startGuard, 1, 0) != 0)
        {
            _logger.LogDebug("TimelineSyncService already started");
            return;
        }

        try
        {
            _logger.LogDebug("Starting TimelineSyncService");

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
            _logger.LogDebug("TimelineSyncService started successfully");
        }
        catch (Exception ex)
        {
            // Reset guard to allow retry on next bootstrap attempt
            Interlocked.Exchange(ref _startGuard, 0);
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

        _logger.LogDebug("Stopping TimelineSyncService");

        // Cancel any pending timer callbacks first
        _timerCts?.Cancel();

        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _drainTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _drainTimer?.Dispose();
        _drainTimer = null;

        _timerCts?.Dispose();
        _timerCts = null;

        _isStarted = false;
        _logger.LogDebug("TimelineSyncService stopped");
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
                _logger.LogDebug("Network restored, timeline sync will resume");
                // Reset consecutive failures on network restore
                Interlocked.Exchange(ref _consecutiveFailures, 0);

                // Start drain loop to process any pending mutations
                StartDrainLoop();
            }
            else if (wasOnline && !_isOnline)
            {
                _logger.LogDebug("Network lost, timeline sync paused");
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
                .Where(m => !m.IsRejected && m.AuthorityError == null && m.SyncAttempts < PendingTimelineMutation.MaxSyncAttempts)
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
        var authorityError = await GetMutationAuthorityErrorAsync(mutation);
        if (authorityError != null)
        {
            mutation.AuthorityError = authorityError;
            mutation.LastError = authorityError;
            await _database!.UpdateAsync(mutation);
            _logger.LogWarning("Timeline mutation {Id} held: {Reason}", mutation.Id, authorityError);
            return;
        }

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
}
