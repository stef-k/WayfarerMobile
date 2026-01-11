using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Manages local timeline storage by filtering and persisting location data.
/// Subscribes to location events and sync callbacks to maintain local timeline state.
/// </summary>
/// <remarks>
/// <para>
/// This service bridges GPS location events and sync notifications to local storage.
/// It applies AND filter logic (matching server behavior) to determine which locations
/// should be stored locally.
/// </para>
/// <para>
/// <strong>Lifetime:</strong> Singleton - must subscribe to static events at startup
/// and remain alive for the app duration.
/// </para>
/// </remarks>
public class LocalTimelineStorageService : IDisposable
{
    private readonly ITimelineRepository _timelineRepository;
    private readonly ILocationQueueRepository _locationQueue;
    private readonly ISettingsService _settings;
    private readonly LocalTimelineFilter _filter;
    private readonly ILogger<LocalTimelineStorageService> _logger;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Time window for backfill/reconciliation operations (days).
    /// Matches queue purge retention (7 days) to ensure no orphans.
    /// </summary>
    private const int BackfillWindowDays = 7;

    /// <summary>
    /// Creates a new instance of LocalTimelineStorageService.
    /// </summary>
    /// <param name="timelineRepository">Repository for timeline operations.</param>
    /// <param name="locationQueue">Repository for location queue operations.</param>
    /// <param name="settings">Settings service for threshold values.</param>
    /// <param name="filter">Local timeline filter with AND logic.</param>
    /// <param name="logger">Logger instance.</param>
    public LocalTimelineStorageService(
        ITimelineRepository timelineRepository,
        ILocationQueueRepository locationQueue,
        ISettingsService settings,
        LocalTimelineFilter filter,
        ILogger<LocalTimelineStorageService> logger)
    {
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
        _locationQueue = locationQueue ?? throw new ArgumentNullException(nameof(locationQueue));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the service by loading the last stored location and subscribing to events.
    /// Should be called once at app startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            _logger.LogWarning("LocalTimelineStorageService already initialized");
            return;
        }

        try
        {
            // Load the most recent stored location to initialize the filter
            var lastEntry = await _timelineRepository.GetMostRecentLocalTimelineEntryAsync();
            if (lastEntry != null)
            {
                var lastLocation = new LocationData
                {
                    Latitude = lastEntry.Latitude,
                    Longitude = lastEntry.Longitude,
                    Timestamp = lastEntry.Timestamp,
                    Accuracy = lastEntry.Accuracy,
                    Altitude = lastEntry.Altitude,
                    Speed = lastEntry.Speed,
                    Bearing = lastEntry.Bearing,
                    Provider = lastEntry.Provider
                };
                _filter.Initialize(lastLocation);
                _logger.LogInformation(
                    "Initialized filter with last stored location from {Timestamp:u}",
                    lastEntry.Timestamp);
            }
            else
            {
                _logger.LogInformation("No previous timeline entries found, filter will accept first location");
            }

            // Subscribe to events BEFORE running recovery to avoid missing new events
            SubscribeToEvents();

            // EDGE-14: Reconcile missing ServerIds from confirmed queue entries
            // This handles crash recovery where API succeeded but callback didn't update local timeline
            await ReconcileMissingServerIdsAsync();

            // EDGE-1: Backfill any queue entries missed during initialization race
            // This handles the case where background service queued locations before we subscribed
            await BackfillMissedQueueEntriesAsync();

            _isInitialized = true;
            _logger.LogInformation("LocalTimelineStorageService initialized");
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error initializing LocalTimelineStorageService");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error initializing LocalTimelineStorageService");
            throw;
        }
    }

    /// <summary>
    /// EDGE-14: Reconciles missing ServerIds on local timeline entries.
    /// Matches confirmed queue entries (with ServerId) to local entries missing ServerId.
    /// </summary>
    private async Task ReconcileMissingServerIdsAsync()
    {
        try
        {
            var backfillSince = DateTime.UtcNow.AddDays(-BackfillWindowDays);

            // Get local entries missing ServerId
            var entriesMissingServerId = await _timelineRepository.GetEntriesMissingServerIdAsync(backfillSince);
            if (entriesMissingServerId.Count == 0)
            {
                _logger.LogDebug("No local entries missing ServerId");
                return;
            }

            // Get confirmed queue entries with ServerId
            var confirmedEntries = await _locationQueue.GetConfirmedEntriesWithServerIdAsync(backfillSince);
            if (confirmedEntries.Count == 0)
            {
                _logger.LogDebug("No confirmed queue entries with ServerId for reconciliation");
                return;
            }

            // Build lookup by timestamp+coordinates for efficient matching
            // Use ToLookup instead of ToDictionary to handle duplicate keys safely
            var confirmedLookup = confirmedEntries
                .Where(e => e.ServerId.HasValue)
                .ToLookup(
                    e => (e.Timestamp, e.Latitude, e.Longitude),
                    e => e.ServerId!.Value);

            var reconciled = 0;
            foreach (var entry in entriesMissingServerId)
            {
                var key = (entry.Timestamp, entry.Latitude, entry.Longitude);
                var serverIds = confirmedLookup[key];
                // Note: Server IDs are always > 0 (auto-increment primary keys)
                // Use Any() to safely check for matches without assuming ID values
                if (serverIds.Any())
                {
                    var serverId = serverIds.First();
                    var updated = await _timelineRepository.UpdateLocalTimelineServerIdAsync(
                        entry.Timestamp,
                        entry.Latitude,
                        entry.Longitude,
                        serverId);

                    if (updated)
                    {
                        reconciled++;
                    }
                }
            }

            if (reconciled > 0)
            {
                _logger.LogInformation(
                    "EDGE-14 reconciliation: Updated {Count} local entries with missing ServerId",
                    reconciled);
            }
        }
        catch (Exception ex)
        {
            // Don't fail initialization for reconciliation errors
            _logger.LogWarning(ex, "Error during ServerId reconciliation (non-fatal)");
        }
    }

    /// <summary>
    /// EDGE-1: Backfills local timeline entries for queue entries that may have been
    /// missed during the initialization race (background service already running).
    /// Uses isolated filter state to avoid racing with concurrent real-time events.
    /// </summary>
    private async Task BackfillMissedQueueEntriesAsync()
    {
        try
        {
            var backfillSince = DateTime.UtcNow.AddDays(-BackfillWindowDays);

            // Get ALL non-rejected queue entries (pending/syncing/synced) - not just confirmed
            // This ensures offline-first behavior: pending entries queued before subscription are included
            var queuedLocations = await _locationQueue.GetNonRejectedEntriesForBackfillAsync(backfillSince);
            if (queuedLocations.Count == 0)
            {
                _logger.LogDebug("No queue entries in backfill window");
                return;
            }

            // Get existing local timeline entries in the same window
            var existingEntries = await _timelineRepository.GetLocalTimelineEntriesInRangeAsync(
                backfillSince, DateTime.UtcNow);

            // Build lookup for existing entries by timestamp+coordinates
            var existingLookup = new HashSet<(DateTime Timestamp, double Lat, double Lon)>(
                existingEntries.Select(e => (e.Timestamp, e.Latitude, e.Longitude)));

            // ISOLATED FILTER STATE: Don't modify main filter during backfill
            // Track last backfilled location separately to avoid racing with concurrent events
            // Initialize from main filter's current state (captures last stored location from DB)
            var lastBackfillLocation = _filter.LastStoredLocation;

            // Get threshold values for isolated filter logic
            var timeThresholdMinutes = _settings.LocationTimeThresholdMinutes;
            var distanceThresholdMeters = _settings.LocationDistanceThresholdMeters;

            var backfilled = 0;
            var filtered = 0;

            // Process in timestamp order (queue entries already sorted by timestamp)
            foreach (var queued in queuedLocations)
            {
                var key = (queued.Timestamp, queued.Latitude, queued.Longitude);
                if (existingLookup.Contains(key))
                {
                    continue; // Already exists in local timeline
                }

                // Apply isolated filter logic (AND: both time AND distance must exceed threshold)
                if (lastBackfillLocation != null)
                {
                    var timeDiff = queued.Timestamp - lastBackfillLocation.Timestamp;
                    var timeExceeded = timeDiff.TotalMinutes >= timeThresholdMinutes;

                    var distance = GeoMath.CalculateDistance(
                        lastBackfillLocation.Latitude,
                        lastBackfillLocation.Longitude,
                        queued.Latitude,
                        queued.Longitude);
                    var distanceExceeded = distance >= distanceThresholdMeters;

                    if (!timeExceeded || !distanceExceeded)
                    {
                        filtered++;
                        continue; // Doesn't pass filter
                    }
                }

                // Create the missing local entry
                var entry = new LocalTimelineEntry
                {
                    Latitude = queued.Latitude,
                    Longitude = queued.Longitude,
                    Timestamp = queued.Timestamp,
                    Accuracy = queued.Accuracy,
                    Altitude = queued.Altitude,
                    Speed = queued.Speed,
                    Bearing = queued.Bearing,
                    Provider = queued.Provider,
                    ServerId = queued.ServerId, // May be null for pending entries, that's OK
                    CreatedAt = DateTime.UtcNow
                };

                await _timelineRepository.InsertLocalTimelineEntryAsync(entry);

                // Update isolated filter state (not main filter)
                lastBackfillLocation = new LocationData
                {
                    Latitude = queued.Latitude,
                    Longitude = queued.Longitude,
                    Timestamp = queued.Timestamp,
                    Accuracy = queued.Accuracy,
                    Altitude = queued.Altitude,
                    Speed = queued.Speed,
                    Bearing = queued.Bearing,
                    Provider = queued.Provider
                };

                existingLookup.Add(key); // Prevent duplicates in this batch
                backfilled++;
            }

            // Update main filter with the most recent backfilled location to prevent over-accept
            // Real-time events will now correctly filter against backfilled entries
            if (lastBackfillLocation != null && backfilled > 0)
            {
                _filter.MarkAsStored(lastBackfillLocation);
                _logger.LogDebug(
                    "Updated main filter with last backfilled location from {Timestamp:u}",
                    lastBackfillLocation.Timestamp);
            }

            if (backfilled > 0 || filtered > 0)
            {
                _logger.LogInformation(
                    "EDGE-1 backfill: Created {Backfilled} local timeline entries from queue ({Filtered} filtered)",
                    backfilled, filtered);
            }
        }
        catch (Exception ex)
        {
            // Don't fail initialization for backfill errors
            _logger.LogWarning(ex, "Error during queue backfill (non-fatal)");
        }
    }

    /// <summary>
    /// Subscribes to location and sync events.
    /// </summary>
    private void SubscribeToEvents()
    {
        // Subscribe to LocationQueued (not LocationReceived) to ensure we store
        // the same coordinates that will be synced. On Android, these may differ
        // due to best-wake-sample optimization.
        LocationServiceCallbacks.LocationQueued += OnLocationQueued;
        LocationSyncCallbacks.LocationSynced += OnLocationSynced;
        LocationSyncCallbacks.LocationSkipped += OnLocationSkipped;

        _logger.LogDebug("Subscribed to location queue and sync events");
    }

    /// <summary>
    /// Unsubscribes from all events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        LocationServiceCallbacks.LocationQueued -= OnLocationQueued;
        LocationSyncCallbacks.LocationSynced -= OnLocationSynced;
        LocationSyncCallbacks.LocationSkipped -= OnLocationSkipped;

        _logger.LogDebug("Unsubscribed from location queue and sync events");
    }

    /// <summary>
    /// Handles queued locations by filtering and storing to local timeline.
    /// Uses queued coordinates to ensure matching with sync callbacks.
    /// </summary>
    private async void OnLocationQueued(object? sender, LocationData location)
    {
        try
        {
            if (!_filter.ShouldStore(location))
            {
                _logger.LogDebug(
                    "Location at {Timestamp:u} filtered out (thresholds: {TimeMin}min, {DistM}m)",
                    location.Timestamp,
                    _filter.TimeThresholdMinutes,
                    _filter.DistanceThresholdMeters);
                return;
            }

            // Create local entry (ServerId = null until sync confirms)
            var entry = new LocalTimelineEntry
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Timestamp = location.Timestamp,
                Accuracy = location.Accuracy,
                Altitude = location.Altitude,
                Speed = location.Speed,
                Bearing = location.Bearing,
                Provider = location.Provider,
                CreatedAt = DateTime.UtcNow
            };

            await _timelineRepository.InsertLocalTimelineEntryAsync(entry);
            _filter.MarkAsStored(location);

            _logger.LogDebug(
                "Stored local timeline entry: ({Lat:F4}, {Lon:F4}) at {Timestamp:u}",
                location.Latitude,
                location.Longitude,
                location.Timestamp);
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error storing location to local timeline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error storing location to local timeline");
        }
    }

    /// <summary>
    /// Handles sync completion by updating the ServerId on the matching local entry.
    /// </summary>
    /// <remarks>
    /// Uses async void because this is an event handler. Exceptions are caught and logged
    /// to prevent them from crashing the app. The work runs on a background thread via
    /// Task.Run to avoid blocking the MainThread (callbacks are dispatched to MainThread).
    /// </remarks>
    private async void OnLocationSynced(object? sender, LocationSyncedEventArgs e)
    {
        try
        {
            // Run on background thread to avoid blocking MainThread
            await Task.Run(async () =>
            {
                var updated = await _timelineRepository.UpdateLocalTimelineServerIdAsync(
                    e.Timestamp,
                    e.Latitude,
                    e.Longitude,
                    e.ServerId);

                if (updated)
                {
                    _logger.LogDebug(
                        "Updated local entry with ServerId {ServerId} for timestamp {Timestamp:u}",
                        e.ServerId,
                        e.Timestamp);
                }
                else
                {
                    _logger.LogDebug(
                        "No matching local entry found for timestamp {Timestamp:u} to update ServerId",
                        e.Timestamp);
                }
            });
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error updating ServerId for synced location");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating ServerId for synced location");
        }
    }

    /// <summary>
    /// Handles sync skip by removing the entry from local timeline.
    /// Server's AND filter is stricter - if server skipped it, we should too.
    /// </summary>
    /// <remarks>
    /// Uses async void because this is an event handler. Exceptions are caught and logged
    /// to prevent them from crashing the app. The work runs on a background thread via
    /// Task.Run to avoid blocking the MainThread (callbacks are dispatched to MainThread).
    /// </remarks>
    private async void OnLocationSkipped(object? sender, LocationSkippedEventArgs e)
    {
        try
        {
            // Run on background thread to avoid blocking MainThread
            await Task.Run(async () =>
            {
                var deleted = await _timelineRepository.DeleteLocalTimelineEntryByTimestampAsync(
                    e.Timestamp,
                    e.Latitude,
                    e.Longitude);

                if (deleted > 0)
                {
                    _logger.LogDebug(
                        "Removed {Count} local entry for skipped location at {Timestamp:u}: {Reason}",
                        deleted,
                        e.Timestamp,
                        e.Reason);
                }
                else
                {
                    _logger.LogDebug(
                        "No matching local entry found for skipped timestamp {Timestamp:u}",
                        e.Timestamp);
                }
            });
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error removing skipped location from local timeline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error removing skipped location from local timeline");
        }
    }

    /// <summary>
    /// Gets the current filter threshold values (for diagnostics).
    /// </summary>
    public (int TimeMinutes, int DistanceMeters) GetCurrentThresholds()
    {
        return (_filter.TimeThresholdMinutes, _filter.DistanceThresholdMeters);
    }

    /// <summary>
    /// Resets the filter state, causing the next location to be stored.
    /// </summary>
    public void ResetFilter()
    {
        _filter.Reset();
        _logger.LogInformation("Local timeline filter reset");
    }

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        UnsubscribeFromEvents();
        _disposed = true;

        _logger.LogDebug("LocalTimelineStorageService disposed");
    }
}
