using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Algorithms;

/// <summary>
/// Filters locations based on time, distance, and accuracy thresholds.
/// Used to determine if a location should be queued for server upload.
/// </summary>
/// <remarks>
/// <para>
/// This filter uses AND logic: a location passes only if:
/// - Accuracy is acceptable (≤ threshold)
/// - BOTH time AND distance thresholds are exceeded
/// </para>
/// <para>
/// The AND logic matches server behavior, ensuring only locations that will
/// actually be stored on the server are queued for upload.
/// </para>
/// </remarks>
public class ThresholdFilter
{
    private LocationData? _lastLoggedLocation;
    private readonly object _lock = new();

    /// <summary>
    /// Gets or sets the minimum time in minutes between logged locations.
    /// </summary>
    public int TimeThresholdMinutes { get; set; } = 1;

    /// <summary>
    /// Gets or sets the minimum distance in meters between logged locations.
    /// </summary>
    public int DistanceThresholdMeters { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum acceptable GPS accuracy in meters.
    /// Locations with accuracy worse (higher) than this are rejected.
    /// </summary>
    public int AccuracyThresholdMeters { get; set; } = 50;

    /// <summary>
    /// Gets the last location that passed the filter.
    /// </summary>
    public LocationData? LastLoggedLocation
    {
        get { lock (_lock) return _lastLoggedLocation; }
    }

    /// <summary>
    /// Determines if a location passes the threshold filter.
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
    /// Accuracy is checked first as a hard reject gate. If accuracy is poor,
    /// the location is rejected immediately without checking time/distance.
    /// </para>
    /// </remarks>
    /// <param name="location">The location to check.</param>
    /// <returns>True if the location should be logged.</returns>
    public bool ShouldLog(LocationData location)
    {
        lock (_lock)
        {
            // Accuracy check is a hard reject gate (checked first)
            // Null accuracy is acceptable (older data may lack accuracy info)
            if (location.Accuracy.HasValue && location.Accuracy.Value > AccuracyThresholdMeters)
                return false;

            // First location always passes (if accuracy is acceptable)
            if (_lastLoggedLocation == null)
                return true;

            // Check time threshold
            var timeDiff = location.Timestamp - _lastLoggedLocation.Timestamp;
            var timeExceeded = timeDiff.TotalMinutes >= TimeThresholdMinutes;

            // Check distance threshold
            var distance = GeoMath.CalculateDistance(
                _lastLoggedLocation.Latitude,
                _lastLoggedLocation.Longitude,
                location.Latitude,
                location.Longitude);
            var distanceExceeded = distance >= DistanceThresholdMeters;

            // AND logic: both time AND distance must be exceeded
            return timeExceeded && distanceExceeded;
        }
    }

    /// <summary>
    /// Marks a location as logged, updating the last logged location.
    /// Call this after successfully logging a location.
    /// </summary>
    /// <param name="location">The location that was logged.</param>
    public void MarkAsLogged(LocationData location)
    {
        lock (_lock)
        {
            _lastLoggedLocation = location;
        }
    }

    /// <summary>
    /// Checks if a location should be logged and marks it as logged if so.
    /// Convenience method combining ShouldLog and MarkAsLogged.
    /// </summary>
    /// <param name="location">The location to check and potentially log.</param>
    /// <returns>True if the location passed the filter and was marked as logged.</returns>
    public bool TryLog(LocationData location)
    {
        lock (_lock)
        {
            if (ShouldLog(location))
            {
                _lastLoggedLocation = location;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Resets the filter state, clearing the last logged location.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _lastLoggedLocation = null;
        }
    }

    /// <summary>
    /// Updates the threshold settings.
    /// </summary>
    /// <param name="timeMinutes">New time threshold in minutes.</param>
    /// <param name="distanceMeters">New distance threshold in meters.</param>
    /// <param name="accuracyMeters">New accuracy threshold in meters.</param>
    public void UpdateThresholds(int timeMinutes, int distanceMeters, int accuracyMeters)
    {
        lock (_lock)
        {
            TimeThresholdMinutes = timeMinutes;
            DistanceThresholdMeters = distanceMeters;
            AccuracyThresholdMeters = accuracyMeters;
        }
    }

    /// <summary>
    /// Gets the number of seconds until the next time-based log is due.
    /// Returns 0 or negative if a log is already due.
    /// Returns null if no location has been logged yet.
    /// </summary>
    /// <remarks>
    /// This is the single source of truth for wake/sleep timing in the location service.
    /// The service should wake up (buffer) seconds before this value reaches 0.
    /// </remarks>
    public double? GetSecondsUntilNextLog()
    {
        lock (_lock)
        {
            if (_lastLoggedLocation == null)
                return null;

            var secondsSinceLastLog = (DateTime.UtcNow - _lastLoggedLocation.Timestamp).TotalSeconds;
            var thresholdSeconds = TimeThresholdMinutes * 60.0;

            return thresholdSeconds - secondsSinceLastLog;
        }
    }
}
