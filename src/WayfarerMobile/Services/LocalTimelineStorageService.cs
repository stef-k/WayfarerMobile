using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Algorithms;
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
    private readonly LocalTimelineFilter _filter;
    private readonly ILogger<LocalTimelineStorageService> _logger;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of LocalTimelineStorageService.
    /// </summary>
    /// <param name="timelineRepository">Repository for timeline operations.</param>
    /// <param name="filter">Local timeline filter with AND logic.</param>
    /// <param name="logger">Logger instance.</param>
    public LocalTimelineStorageService(
        ITimelineRepository timelineRepository,
        LocalTimelineFilter filter,
        ILogger<LocalTimelineStorageService> logger)
    {
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
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

            // Subscribe to events
            SubscribeToEvents();

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
    /// Subscribes to location and sync events.
    /// </summary>
    private void SubscribeToEvents()
    {
        LocationServiceCallbacks.LocationReceived += OnLocationReceived;
        LocationSyncCallbacks.LocationSynced += OnLocationSynced;
        LocationSyncCallbacks.LocationSkipped += OnLocationSkipped;

        _logger.LogDebug("Subscribed to location and sync events");
    }

    /// <summary>
    /// Unsubscribes from all events.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        LocationServiceCallbacks.LocationReceived -= OnLocationReceived;
        LocationSyncCallbacks.LocationSynced -= OnLocationSynced;
        LocationSyncCallbacks.LocationSkipped -= OnLocationSkipped;

        _logger.LogDebug("Unsubscribed from location and sync events");
    }

    /// <summary>
    /// Handles incoming GPS locations by filtering and storing to local timeline.
    /// </summary>
    private async void OnLocationReceived(object? sender, LocationData location)
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
    private async void OnLocationSynced(object? sender, LocationSyncedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
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
            }
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, "Database error updating ServerId for synced location");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating ServerId for synced location");
            }
        });
    }

    /// <summary>
    /// Handles sync skip by removing the entry from local timeline.
    /// Server's AND filter is stricter - if server skipped it, we should too.
    /// </summary>
    private async void OnLocationSkipped(object? sender, LocationSkippedEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
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
            }
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, "Database error removing skipped location from local timeline");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error removing skipped location from local timeline");
            }
        });
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
