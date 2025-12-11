using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Algorithms;

/// <summary>
/// Filters locations based on time and distance thresholds.
/// Used to determine if a location should be logged to the server.
/// </summary>
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
    /// Gets the last location that passed the filter.
    /// </summary>
    public LocationData? LastLoggedLocation
    {
        get { lock (_lock) return _lastLoggedLocation; }
    }

    /// <summary>
    /// Determines if a location passes the threshold filter.
    /// A location passes if either:
    /// - It's the first location
    /// - Enough time has passed since the last logged location
    /// - Enough distance has been traveled since the last logged location
    /// </summary>
    /// <param name="location">The location to check.</param>
    /// <returns>True if the location should be logged.</returns>
    public bool ShouldLog(LocationData location)
    {
        lock (_lock)
        {
            // First location always passes
            if (_lastLoggedLocation == null)
                return true;

            // Check time threshold
            var timeDiff = location.Timestamp - _lastLoggedLocation.Timestamp;
            if (timeDiff.TotalMinutes >= TimeThresholdMinutes)
                return true;

            // Check distance threshold
            var distance = GeoMath.CalculateDistance(
                _lastLoggedLocation.Latitude,
                _lastLoggedLocation.Longitude,
                location.Latitude,
                location.Longitude);

            if (distance >= DistanceThresholdMeters)
                return true;

            return false;
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
    public void UpdateThresholds(int timeMinutes, int distanceMeters)
    {
        TimeThresholdMinutes = timeMinutes;
        DistanceThresholdMeters = distanceMeters;
    }
}
