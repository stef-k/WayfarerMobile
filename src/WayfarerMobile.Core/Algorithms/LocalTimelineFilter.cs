using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Algorithms;

/// <summary>
/// Filters locations for local timeline storage using AND logic (matching server behavior).
/// A location is stored only if accuracy is acceptable AND BOTH time AND distance thresholds are exceeded.
/// </summary>
/// <remarks>
/// <para>
/// This filter mirrors the server's threshold logic to ensure consistency between
/// local storage and server storage. Thresholds are read from <see cref="ISettingsService"/>
/// which syncs from the server (single source of truth).
/// </para>
/// <para>
/// Accuracy is checked first as a hard reject gate. Locations with poor accuracy
/// are rejected immediately, even for the first location.
/// </para>
/// </remarks>
public class LocalTimelineFilter
{
    private readonly ISettingsService _settings;
    private LocationData? _lastStoredLocation;
    private readonly object _lock = new();

    /// <summary>
    /// Creates a new instance of LocalTimelineFilter.
    /// </summary>
    /// <param name="settings">Settings service for threshold values.</param>
    public LocalTimelineFilter(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Gets the time threshold in minutes from settings.
    /// </summary>
    public int TimeThresholdMinutes => _settings.LocationTimeThresholdMinutes;

    /// <summary>
    /// Gets the distance threshold in meters from settings.
    /// </summary>
    public int DistanceThresholdMeters => _settings.LocationDistanceThresholdMeters;

    /// <summary>
    /// Gets the accuracy threshold in meters from settings.
    /// Locations with accuracy worse (higher) than this value are rejected.
    /// </summary>
    public int AccuracyThresholdMeters => _settings.LocationAccuracyThresholdMeters;

    /// <summary>
    /// Gets the last location that passed the filter.
    /// </summary>
    public LocationData? LastStoredLocation
    {
        get { lock (_lock) return _lastStoredLocation; }
    }

    /// <summary>
    /// Determines if a location should be stored locally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A location passes if ALL conditions are met:
    /// <list type="bullet">
    /// <item>Accuracy is acceptable (≤ AccuracyThresholdMeters) or null</item>
    /// <item>It's the first location, OR both time AND distance thresholds are exceeded</item>
    /// </list>
    /// </para>
    /// <para>
    /// Accuracy is checked first as a hard reject gate. Locations with poor accuracy
    /// are rejected immediately, even for the first location.
    /// </para>
    /// </remarks>
    /// <param name="location">The location to check.</param>
    /// <returns>True if the location should be stored locally.</returns>
    public bool ShouldStore(LocationData location)
    {
        ArgumentNullException.ThrowIfNull(location);

        lock (_lock)
        {
            // Accuracy check is a hard reject gate (checked first, even for first location)
            // Null accuracy is acceptable (older data may lack accuracy info)
            if (location.Accuracy.HasValue && location.Accuracy.Value > AccuracyThresholdMeters)
                return false;

            // First location passes if accuracy is acceptable
            if (_lastStoredLocation == null)
                return true;

            // Check time threshold
            var timeDiff = location.Timestamp - _lastStoredLocation.Timestamp;
            var timeExceeded = timeDiff.TotalMinutes >= TimeThresholdMinutes;

            // Check distance threshold
            var distance = GeoMath.CalculateDistance(
                _lastStoredLocation.Latitude,
                _lastStoredLocation.Longitude,
                location.Latitude,
                location.Longitude);
            var distanceExceeded = distance >= DistanceThresholdMeters;

            // AND logic: both must be true (matches server behavior)
            return timeExceeded && distanceExceeded;
        }
    }

    /// <summary>
    /// Marks a location as stored, updating the last stored location.
    /// Call this after successfully storing a location locally.
    /// </summary>
    /// <param name="location">The location that was stored.</param>
    public void MarkAsStored(LocationData location)
    {
        ArgumentNullException.ThrowIfNull(location);

        lock (_lock)
        {
            _lastStoredLocation = location;
        }
    }

    /// <summary>
    /// Checks if a location should be stored and marks it as stored if so.
    /// Convenience method combining ShouldStore and MarkAsStored.
    /// </summary>
    /// <param name="location">The location to check and potentially store.</param>
    /// <returns>True if the location passed the filter and was marked as stored.</returns>
    public bool TryStore(LocationData location)
    {
        ArgumentNullException.ThrowIfNull(location);

        lock (_lock)
        {
            if (ShouldStore(location))
            {
                _lastStoredLocation = location;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Initializes the filter with a previous location.
    /// Call this on startup with the most recent stored location from the database.
    /// </summary>
    /// <param name="lastLocation">The most recent stored location, or null if none.</param>
    public void Initialize(LocationData? lastLocation)
    {
        lock (_lock)
        {
            _lastStoredLocation = lastLocation;
        }
    }

    /// <summary>
    /// Resets the filter state, clearing the last stored location.
    /// The next location will always pass the filter.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastStoredLocation = null;
        }
    }
}
