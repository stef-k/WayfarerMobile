using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for calculating stable heading/bearing and managing location indicator state.
/// Uses circular averaging to fix the 0°/360° wrap-around problem and provides
/// jitter filtering for smooth heading display.
/// </summary>
public class LocationIndicatorService : IDisposable
{
    #region Constants

    /// <summary>
    /// Maximum bearing history samples for smoothing.
    /// </summary>
    private const int MaxBearingHistory = 3;

    /// <summary>
    /// Minimum speed (m/s) required to trust GPS course.
    /// ~3.6 km/h - below this, GPS course is unreliable.
    /// </summary>
    private const double MinSpeedForGpsCourse = 1.0;

    /// <summary>
    /// Minimum bearing change (degrees) to trigger an update.
    /// Prevents jittery updates from small fluctuations.
    /// </summary>
    private const double MinBearingChange = 15.0;

    /// <summary>
    /// Minimum distance (meters) required between positions to calculate bearing from movement.
    /// </summary>
    private const double MinDistanceForBearing = 10.0;

    /// <summary>
    /// Duration (seconds) to hold the last valid bearing when GPS is unavailable.
    /// </summary>
    private const double BearingHoldDurationSeconds = 20.0;

    /// <summary>
    /// Duration (seconds) after which a location is considered stale.
    /// After this time, show gray dot instead of blue.
    /// </summary>
    private const double LocationStaleDurationSeconds = 30.0;

    /// <summary>
    /// Minimum cone angle (degrees) for well-calibrated compass.
    /// </summary>
    private const double MinConeAngle = 30.0;

    /// <summary>
    /// Maximum cone angle (degrees) for poorly calibrated compass.
    /// </summary>
    private const double MaxConeAngle = 90.0;

    /// <summary>
    /// Default bearing accuracy when not provided (degrees).
    /// </summary>
    private const double DefaultBearingAccuracy = 15.0;

    #endregion

    #region Private Fields

    private readonly ILogger<LocationIndicatorService> _logger;
    private readonly Queue<BearingSample> _bearingHistory = new();

    private LocationData? _previousLocation;
    private LocationData? _lastKnownLocation;
    private double _lastValidBearing = -1;
    private DateTime _lastBearingUpdate = DateTime.MinValue;
    private double _calculatedHeading = -1;
    private DateTime _lastHeadingCalculation = DateTime.MinValue;
    private DateTime _lastLocationUpdate = DateTime.MinValue;
    private double _lastBearingAccuracy = DefaultBearingAccuracy;
    private bool _disposed;

    // Animation state
    private double _pulsePhase;
    private DateTime _lastAnimationUpdate = DateTime.UtcNow;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether navigation is active (affects visual state).
    /// </summary>
    public bool IsNavigating { get; set; }

    /// <summary>
    /// Gets or sets whether currently on route (affects color).
    /// </summary>
    public bool IsOnRoute { get; set; } = true;

    /// <summary>
    /// Gets the current pulse scale factor for animation (0.85 to 1.15).
    /// </summary>
    public double PulseScale { get; private set; } = 1.0;

    /// <summary>
    /// Gets the last calculated heading in degrees (0-360) or -1 if unavailable.
    /// </summary>
    public double CurrentHeading => _calculatedHeading;

    /// <summary>
    /// Gets whether a valid heading is available.
    /// </summary>
    public bool HasValidHeading => _calculatedHeading >= 0;

    /// <summary>
    /// Gets whether the location is stale (no updates for LocationStaleDurationSeconds).
    /// When true, show gray dot instead of blue.
    /// </summary>
    public bool IsLocationStale =>
        _lastLocationUpdate != DateTime.MinValue &&
        (DateTime.UtcNow - _lastLocationUpdate).TotalSeconds > LocationStaleDurationSeconds;

    /// <summary>
    /// Gets the last known location (for gray dot fallback).
    /// </summary>
    public LocationData? LastKnownLocation => _lastKnownLocation;

    /// <summary>
    /// Gets the cone angle in degrees based on bearing accuracy (Google Maps style).
    /// Lower accuracy = wider cone. Range: 30° (excellent) to 90° (poor).
    /// </summary>
    public double ConeAngle
    {
        get
        {
            // Map bearing accuracy to cone angle
            // Accuracy 1° = 30° cone (narrow, well calibrated)
            // Accuracy 45°+ = 90° cone (wide, needs calibration)
            var accuracy = _lastBearingAccuracy;
            if (accuracy <= 1) return MinConeAngle;
            if (accuracy >= 45) return MaxConeAngle;

            // Linear interpolation between min and max
            var ratio = (accuracy - 1) / (45 - 1);
            return MinConeAngle + ratio * (MaxConeAngle - MinConeAngle);
        }
    }

    /// <summary>
    /// Gets the time since last location update in seconds.
    /// </summary>
    public double SecondsSinceLastUpdate =>
        _lastLocationUpdate == DateTime.MinValue ? double.MaxValue :
        (DateTime.UtcNow - _lastLocationUpdate).TotalSeconds;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of LocationIndicatorService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public LocationIndicatorService(ILogger<LocationIndicatorService> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Calculates the best available heading from GPS or movement with stability filtering.
    /// Uses circular averaging to properly handle the 0°/360° boundary.
    /// </summary>
    /// <param name="currentLocation">Current location data.</param>
    /// <returns>Heading in degrees (0-360) or -1 if unavailable.</returns>
    public double CalculateBestHeading(LocationData currentLocation)
    {
        if (_disposed || currentLocation == null)
            return -1;

        // Track location freshness for gray dot fallback
        _lastLocationUpdate = DateTime.UtcNow;
        _lastKnownLocation = currentLocation;

        // Track bearing accuracy for cone width (Google Maps style)
        if (currentLocation.BearingAccuracy.HasValue && currentLocation.BearingAccuracy > 0)
        {
            _lastBearingAccuracy = currentLocation.BearingAccuracy.Value;
        }

        try
        {
            double rawBearing = -1;

            // 1. Try GPS course first (highest priority when moving)
            if (IsValidGpsCourse(currentLocation))
            {
                rawBearing = currentLocation.Bearing!.Value;
                _calculatedHeading = rawBearing;
                _lastHeadingCalculation = DateTime.UtcNow;
                _logger.LogDebug("Using GPS course: {Bearing:F1}° (speed: {Speed:F1} m/s)",
                    rawBearing, currentLocation.Speed);
                return rawBearing;
            }

            // 2. Calculate from movement (fallback)
            if (_previousLocation != null)
            {
                double distance = GeoMath.CalculateDistance(
                    _previousLocation.Latitude, _previousLocation.Longitude,
                    currentLocation.Latitude, currentLocation.Longitude);

                if (distance >= MinDistanceForBearing)
                {
                    double bearing = GeoMath.CalculateBearing(
                        _previousLocation.Latitude, _previousLocation.Longitude,
                        currentLocation.Latitude, currentLocation.Longitude);

                    // Add to smoothing history with circular averaging
                    AddBearingSample(bearing, currentLocation.Accuracy ?? 50);
                    double smoothedBearing = CalculateSmoothedBearing();

                    // Only update if change is significant (jitter filtering)
                    if (ShouldUpdateBearing(smoothedBearing))
                    {
                        _calculatedHeading = smoothedBearing;
                        _lastHeadingCalculation = DateTime.UtcNow;
                        _lastValidBearing = smoothedBearing;
                        _lastBearingUpdate = DateTime.UtcNow;
                    }

                    _logger.LogDebug("Calculated bearing from movement: {Bearing:F1}° (distance: {Distance:F1}m)",
                        _calculatedHeading, distance);

                    _previousLocation = currentLocation;
                    return _calculatedHeading;
                }
            }

            // 3. Use cached bearing if still valid (persistence)
            if (_calculatedHeading >= 0)
            {
                var timeSinceLastCalculation = DateTime.UtcNow - _lastHeadingCalculation;
                if (timeSinceLastCalculation.TotalSeconds < BearingHoldDurationSeconds)
                {
                    _logger.LogDebug("Using cached heading: {Bearing:F1}° (age: {Age:F1}s)",
                        _calculatedHeading, timeSinceLastCalculation.TotalSeconds);

                    _previousLocation = currentLocation;
                    return _calculatedHeading;
                }

                _logger.LogDebug("Cached heading expired after {Age:F1}s", timeSinceLastCalculation.TotalSeconds);
            }

            // Update previous location for next calculation
            _previousLocation = currentLocation;
            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating heading");
            return _calculatedHeading >= 0 ? _calculatedHeading : -1;
        }
    }

    /// <summary>
    /// Updates the animation state (call at ~60 FPS for smooth pulsing).
    /// </summary>
    public void UpdateAnimation()
    {
        if (!IsNavigating)
        {
            PulseScale = 1.0;
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (now - _lastAnimationUpdate).TotalSeconds;
        _lastAnimationUpdate = now;

        // Pulse frequency: ~1 Hz (one cycle per second)
        _pulsePhase += elapsed * 2 * Math.PI;
        if (_pulsePhase > 2 * Math.PI)
            _pulsePhase -= 2 * Math.PI;

        // Pulse scale: 0.85 to 1.15 (15% variation)
        PulseScale = 1.0 + Math.Sin(_pulsePhase) * 0.15;
    }

    /// <summary>
    /// Gets the indicator color based on navigation and GPS state.
    /// </summary>
    /// <returns>Color in hex format (#RRGGBB).</returns>
    public string GetIndicatorColor()
    {
        // Gray when GPS is unavailable/stale (Google Maps style)
        if (IsLocationStale)
            return "#9E9E9E"; // Material Gray 500

        // Orange when off-route during navigation
        if (IsNavigating && !IsOnRoute)
            return "#FBBC04"; // Google Yellow/Orange (off-route warning)

        // Blue (default - on route or just tracking)
        return "#4285F4"; // Google Blue
    }

    /// <summary>
    /// Resets the bearing history and cached values.
    /// Call when starting a new tracking session.
    /// </summary>
    public void Reset()
    {
        _bearingHistory.Clear();
        _previousLocation = null;
        _lastValidBearing = -1;
        _lastBearingUpdate = DateTime.MinValue;
        _calculatedHeading = -1;
        _lastHeadingCalculation = DateTime.MinValue;
        _pulsePhase = 0;
        PulseScale = 1.0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _bearingHistory.Clear();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Validates if GPS course can be trusted based on speed.
    /// </summary>
    private bool IsValidGpsCourse(LocationData location)
    {
        return location.Bearing.HasValue &&
               location.Bearing >= 0 &&
               location.Bearing < 360 &&
               location.Speed.HasValue &&
               location.Speed >= MinSpeedForGpsCourse;
    }

    /// <summary>
    /// Adds a bearing sample to history for smoothing.
    /// </summary>
    private void AddBearingSample(double bearing, double accuracy)
    {
        var sample = new BearingSample
        {
            Bearing = bearing,
            Accuracy = accuracy,
            Timestamp = DateTime.UtcNow,
            Weight = CalculateWeight(accuracy)
        };

        _bearingHistory.Enqueue(sample);

        // Keep only recent samples
        while (_bearingHistory.Count > MaxBearingHistory)
        {
            _bearingHistory.Dequeue();
        }
    }

    /// <summary>
    /// Calculates smoothed bearing using circular weighted average.
    /// This properly handles the 0°/360° wrap-around problem.
    /// </summary>
    private double CalculateSmoothedBearing()
    {
        if (_bearingHistory.Count == 0)
            return -1;

        if (_bearingHistory.Count == 1)
            return _bearingHistory.First().Bearing;

        // CRITICAL: Use circular averaging for proper bearing smoothing
        // This fixes the bug where 359° and 1° average to 180° instead of 0°
        double sinSum = 0;
        double cosSum = 0;
        double totalWeight = 0;

        foreach (var sample in _bearingHistory)
        {
            double radians = sample.Bearing * Math.PI / 180;
            sinSum += Math.Sin(radians) * sample.Weight;
            cosSum += Math.Cos(radians) * sample.Weight;
            totalWeight += sample.Weight;
        }

        if (totalWeight == 0)
            return _bearingHistory.Last().Bearing;

        double avgRadians = Math.Atan2(sinSum / totalWeight, cosSum / totalWeight);
        double avgBearing = avgRadians * 180 / Math.PI;

        // Normalize to 0-360
        return (avgBearing + 360) % 360;
    }

    /// <summary>
    /// Calculates sample weight based on GPS accuracy.
    /// Higher accuracy = higher weight.
    /// </summary>
    private static double CalculateWeight(double accuracy)
    {
        return accuracy switch
        {
            <= 5 => 1.0,   // Excellent
            <= 15 => 0.8,  // Good
            <= 30 => 0.6,  // Fair
            _ => 0.3       // Poor
        };
    }

    /// <summary>
    /// Determines if a bearing change is significant enough to update.
    /// Prevents jittery updates from small fluctuations.
    /// </summary>
    private bool ShouldUpdateBearing(double newBearing)
    {
        if (_lastValidBearing < 0)
            return true; // First bearing

        double difference = Math.Abs(newBearing - _lastValidBearing);

        // Handle wrap-around (e.g., 350° to 10° = 20° difference, not 340°)
        if (difference > 180)
            difference = 360 - difference;

        return difference >= MinBearingChange;
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Bearing sample for circular averaging.
    /// </summary>
    private class BearingSample
    {
        public double Bearing { get; set; }
        public double Accuracy { get; set; }
        public DateTime Timestamp { get; set; }
        public double Weight { get; set; }
    }

    #endregion
}
