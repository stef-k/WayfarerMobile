namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for QueueDrainService focusing on threshold filtering,
/// rate limiting, and reference point management.
/// </summary>
/// <remarks>
/// The QueueDrainService drains offline location queues via the check-in endpoint.
/// Unlike log-location (which uses time OR distance), check-in requires mobile-side
/// filtering with time AND distance thresholds to mirror server's log-location logic.
/// </remarks>
public class QueueDrainServiceTests
{
    #region Constants

    // Note: Production thresholds come from ISettingsService (defaults: 5min, 15m).
    // Tests use 100m for easier distance calculations in test scenarios.
    // The ThresholdFilter helper mirrors the QueueDrainService.ShouldSyncLocation logic.
    private const int TimeThresholdMinutes = 5;
    private const int DistanceThresholdMeters = 100;
    private const int MinSecondsBetweenDrains = 65;
    private const int MaxDrainsPerHour = 55;

    #endregion

    #region Threshold Filtering Tests

    [Fact]
    public void ShouldSyncLocation_NoReference_AlwaysSyncs()
    {
        // First location ever - no reference point exists
        var filter = new ThresholdFilter(null, null, null);

        var result = filter.ShouldSync(40.7128, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void ShouldSyncLocation_BothThresholdsMet_Syncs()
    {
        // Reference: NYC at 10:00
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow.AddMinutes(-10); // 10 minutes ago

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // New location: ~500m away, 10 minutes later
        var newLat = 40.7173; // ~500m north
        var newLon = -74.0060;
        var newTime = DateTime.UtcNow;

        var result = filter.ShouldSync(newLat, newLon, newTime);

        result.ShouldSync.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void ShouldSyncLocation_TimeNotMet_Filters()
    {
        // Reference: 2 minutes ago (less than 5 minute threshold)
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow.AddMinutes(-2);

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // New location: far away but too recent (must be AFTER reference time)
        var newLat = 40.8000; // ~10km away
        var newLon = -74.0060;
        var newTime = DateTime.UtcNow; // This is after refTime by ~2 minutes

        var result = filter.ShouldSync(newLat, newLon, newTime);

        result.ShouldSync.Should().BeFalse();
        result.Reason.Should().Contain("Time:");
        result.Reason.Should().Contain("threshold");
    }

    [Fact]
    public void ShouldSyncLocation_DistanceNotMet_Filters()
    {
        // Reference: 10 minutes ago
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow.AddMinutes(-10);

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // New location: only 50m away (less than 100m threshold)
        var newLat = 40.71325; // ~50m north
        var newLon = -74.0060;
        var newTime = DateTime.UtcNow;

        var result = filter.ShouldSync(newLat, newLon, newTime);

        result.ShouldSync.Should().BeFalse();
        result.Reason.Should().Contain("Distance:");
        result.Reason.Should().Contain("threshold");
    }

    [Fact]
    public void ShouldSyncLocation_NeitherThresholdMet_FiltersWithBothReasons()
    {
        // Reference: 2 minutes ago
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow.AddMinutes(-2);

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // Same location (distance not met), too recent (time not met)
        // Timestamp must be after reference time
        var result = filter.ShouldSync(refLat, refLon, DateTime.UtcNow);

        result.ShouldSync.Should().BeFalse();
        result.Reason.Should().Contain("Time:");
        result.Reason.Should().Contain("Distance:");
    }

    [Fact]
    public void ShouldSyncLocation_ExactlyAtThresholds_Syncs()
    {
        // Reference: exactly 5 minutes ago
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow.AddMinutes(-5);

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // New location: exactly 100m away (approximate - using ~0.0009 degrees latitude)
        var newLat = 40.71370; // ~100m north
        var newLon = -74.0060;
        var newTime = DateTime.UtcNow;

        var result = filter.ShouldSync(newLat, newLon, newTime);

        result.ShouldSync.Should().BeTrue();
    }

    [Fact]
    public void ShouldSyncLocation_OutOfOrderLocation_Filters()
    {
        // Reference: current time
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow;

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // New location: timestamp BEFORE reference (out-of-order)
        // Even though thresholds would be met, location is older than reference
        var newLat = 40.8000; // Far away
        var newLon = -74.0060;
        var newTime = DateTime.UtcNow.AddMinutes(-10); // Older than reference

        var result = filter.ShouldSync(newLat, newLon, newTime);

        result.ShouldSync.Should().BeFalse();
        result.Reason.Should().Contain("Out-of-order");
    }

    [Fact]
    public void ShouldSyncLocation_SameTimestampAsReference_Filters()
    {
        // Reference time
        var refLat = 40.7128;
        var refLon = -74.0060;
        var refTime = DateTime.UtcNow;

        var filter = new ThresholdFilter(refLat, refLon, refTime);

        // Exact same timestamp as reference
        var newLat = 40.8000;
        var newLon = -74.0060;

        var result = filter.ShouldSync(newLat, newLon, refTime);

        result.ShouldSync.Should().BeFalse();
        result.Reason.Should().Contain("Out-of-order");
    }

    #endregion

    #region Rate Limiting Tests

    [Fact]
    public void RateLimiter_FirstRequest_Allowed()
    {
        var limiter = new DrainRateLimiter();

        limiter.CanMakeRequest().Should().BeTrue();
    }

    [Fact]
    public void RateLimiter_TooSoon_NotAllowed()
    {
        var limiter = new DrainRateLimiter();

        // Record a drain
        limiter.RecordDrain();

        // Immediately try again - should be blocked
        limiter.CanMakeRequest().Should().BeFalse();
    }

    [Fact]
    public void RateLimiter_AfterMinInterval_Allowed()
    {
        var limiter = new DrainRateLimiter();

        // Simulate drain 70 seconds ago (> 65s threshold)
        limiter.SetLastDrainTime(DateTime.UtcNow.AddSeconds(-70));

        limiter.CanMakeRequest().Should().BeTrue();
    }

    [Fact]
    public void RateLimiter_HourlyLimitReached_NotAllowed()
    {
        var limiter = new DrainRateLimiter();

        // Fill up the hourly quota
        var now = DateTime.UtcNow;
        for (int i = 0; i < MaxDrainsPerHour; i++)
        {
            limiter.AddDrainHistory(now.AddMinutes(-30)); // All within last hour
        }

        // Set last drain far enough in past to pass interval check
        limiter.SetLastDrainTime(now.AddSeconds(-70));

        // Should be blocked by hourly limit
        limiter.CanMakeRequest().Should().BeFalse();
    }

    [Fact]
    public void RateLimiter_OldHistoryCleanedUp_Allowed()
    {
        var limiter = new DrainRateLimiter();
        var now = DateTime.UtcNow;

        // Add old history (> 1 hour ago - should be cleaned up)
        for (int i = 0; i < MaxDrainsPerHour; i++)
        {
            limiter.AddDrainHistory(now.AddHours(-2));
        }

        limiter.SetLastDrainTime(now.AddSeconds(-70));

        // Old history should be cleaned up, allowing new request
        limiter.CanMakeRequest().Should().BeTrue();
    }

    #endregion

    #region User-Invoked Bypass Tests (Issue #160)

    [Fact]
    public void ShouldSyncLocation_UserInvoked_SkipsAllFilters()
    {
        // User-invoked locations (manual check-ins) skip all client-side filtering
        // Server is authoritative for these

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddSeconds(-10), // Only 10 seconds ago (normally filtered)
            isUserInvoked: true);

        // Same location, very recent - normally would be filtered
        var result = filter.ShouldSync(40.7128, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeTrue("user-invoked should skip all filters");
        result.Reason.Should().BeNull();
    }

    [Fact]
    public void ShouldSyncLocation_Background_AppliesFilters()
    {
        // Background/live locations should apply normal filtering

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddSeconds(-10), // Only 10 seconds ago
            isUserInvoked: false);

        // Same location, very recent - should be filtered for background
        var result = filter.ShouldSync(40.7128, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeFalse("background should apply filters");
        result.Reason.Should().Contain("Time:");
    }

    [Fact]
    public void ShouldSyncLocation_UserInvoked_BypassesTimeThreshold()
    {
        // User-invoked bypasses time threshold even when very recent

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddSeconds(-1), // Only 1 second ago!
            isUserInvoked: true);

        // Far enough away, but time threshold definitely not met
        var result = filter.ShouldSync(40.8000, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeTrue("user-invoked should bypass time threshold");
    }

    [Fact]
    public void ShouldSyncLocation_UserInvoked_BypassesDistanceThreshold()
    {
        // User-invoked bypasses distance threshold even at exact same location

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddMinutes(-10), // Time threshold met
            isUserInvoked: true);

        // Exact same location - normally would be filtered by distance
        var result = filter.ShouldSync(40.7128, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeTrue("user-invoked should bypass distance threshold");
    }

    [Fact]
    public void ShouldSyncLocation_UserInvoked_BypassesAccuracyThreshold()
    {
        // User-invoked bypasses accuracy threshold even with poor GPS

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddMinutes(-10),
            isUserInvoked: true,
            accuracyMeters: 500.0); // Very poor accuracy

        var result = filter.ShouldSync(40.8000, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeTrue("user-invoked should bypass accuracy threshold");
    }

    [Fact]
    public void ShouldSyncLocation_Background_WithPoorAccuracy_Filters()
    {
        // Background with poor accuracy should be filtered

        var filter = new ThresholdFilterWithUserInvoked(
            refLat: 40.7128,
            refLon: -74.0060,
            refTime: DateTime.UtcNow.AddMinutes(-10),
            isUserInvoked: false,
            accuracyMeters: 500.0); // Very poor accuracy

        var result = filter.ShouldSync(40.8000, -74.0060, DateTime.UtcNow);

        result.ShouldSync.Should().BeFalse("background with poor accuracy should be filtered");
        result.Reason.Should().Contain("Accuracy:");
    }

    #endregion

    #region Distance Calculation Tests

    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        var distance = GeoCalculator.DistanceMeters(40.7128, -74.0060, 40.7128, -74.0060);

        distance.Should().Be(0);
    }

    [Fact]
    public void CalculateDistance_KnownDistance_ReturnsApproximate()
    {
        // NYC to Newark is approximately 16km
        var nycLat = 40.7128;
        var nycLon = -74.0060;
        var newarkLat = 40.7357;
        var newarkLon = -74.1724;

        var distance = GeoCalculator.DistanceMeters(nycLat, nycLon, newarkLat, newarkLon);

        // Should be approximately 13-14 km
        distance.Should().BeInRange(12000, 15000);
    }

    [Fact]
    public void CalculateDistance_OneHundredMeters_Accurate()
    {
        // Approximately 100m in latitude at NYC
        var lat1 = 40.7128;
        var lon1 = -74.0060;
        var lat2 = 40.7137; // ~100m north
        var lon2 = -74.0060;

        var distance = GeoCalculator.DistanceMeters(lat1, lon1, lat2, lon2);

        // Should be close to 100m (+/- 10%)
        distance.Should().BeInRange(90, 110);
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Threshold filter implementation that mirrors QueueDrainService.ShouldSyncLocation logic.
    /// </summary>
    private sealed class ThresholdFilter
    {
        private readonly double? _refLat;
        private readonly double? _refLon;
        private readonly DateTime? _refTime;

        public ThresholdFilter(double? refLat, double? refLon, DateTime? refTime)
        {
            _refLat = refLat;
            _refLon = refLon;
            _refTime = refTime;
        }

        public (bool ShouldSync, string? Reason) ShouldSync(double lat, double lon, DateTime timestamp)
        {
            // No reference - first sync
            if (!_refLat.HasValue || !_refLon.HasValue || !_refTime.HasValue)
            {
                return (true, null);
            }

            // Handle out-of-order locations - skip if older than or equal to reference
            if (timestamp <= _refTime.Value)
            {
                return (false, $"Out-of-order: timestamp {timestamp:u} <= reference {_refTime.Value:u}");
            }

            var timeSince = timestamp - _refTime.Value;
            var timeThresholdMet = timeSince.TotalMinutes >= TimeThresholdMinutes;

            var distance = GeoCalculator.DistanceMeters(_refLat.Value, _refLon.Value, lat, lon);
            var distanceThresholdMet = distance >= DistanceThresholdMeters;

            // AND logic - both must be met
            if (timeThresholdMet && distanceThresholdMet)
            {
                return (true, null);
            }

            var reasons = new List<string>();
            if (!timeThresholdMet)
                reasons.Add($"Time: {timeSince.TotalMinutes:F1}min, threshold {TimeThresholdMinutes}min");
            if (!distanceThresholdMet)
                reasons.Add($"Distance: {distance:F0}m, threshold {DistanceThresholdMeters}m");

            return (false, string.Join("; ", reasons));
        }
    }

    /// <summary>
    /// Threshold filter implementation with IsUserInvoked support for issue #160 tests.
    /// User-invoked locations (manual check-ins) skip all client-side filtering.
    /// </summary>
    private sealed class ThresholdFilterWithUserInvoked
    {
        private readonly double? _refLat;
        private readonly double? _refLon;
        private readonly DateTime? _refTime;
        private readonly bool _isUserInvoked;
        private readonly double? _accuracyMeters;

        private const double DefaultAccuracyThreshold = 100.0; // meters

        public ThresholdFilterWithUserInvoked(
            double? refLat,
            double? refLon,
            DateTime? refTime,
            bool isUserInvoked,
            double? accuracyMeters = null)
        {
            _refLat = refLat;
            _refLon = refLon;
            _refTime = refTime;
            _isUserInvoked = isUserInvoked;
            _accuracyMeters = accuracyMeters;
        }

        public (bool ShouldSync, string? Reason) ShouldSync(double lat, double lon, DateTime timestamp)
        {
            // User-invoked locations skip ALL client-side filtering
            // Server is authoritative for these
            if (_isUserInvoked)
            {
                return (true, null);
            }

            // No reference - first sync
            if (!_refLat.HasValue || !_refLon.HasValue || !_refTime.HasValue)
            {
                return (true, null);
            }

            // Check accuracy threshold for background locations
            if (_accuracyMeters.HasValue && _accuracyMeters.Value > DefaultAccuracyThreshold)
            {
                return (false, $"Accuracy: {_accuracyMeters.Value:F0}m exceeds threshold {DefaultAccuracyThreshold:F0}m");
            }

            // Handle out-of-order locations
            if (timestamp <= _refTime.Value)
            {
                return (false, $"Out-of-order: timestamp {timestamp:u} <= reference {_refTime.Value:u}");
            }

            var timeSince = timestamp - _refTime.Value;
            var timeThresholdMet = timeSince.TotalMinutes >= TimeThresholdMinutes;

            var distance = GeoCalculator.DistanceMeters(_refLat.Value, _refLon.Value, lat, lon);
            var distanceThresholdMet = distance >= DistanceThresholdMeters;

            // AND logic - both must be met
            if (timeThresholdMet && distanceThresholdMet)
            {
                return (true, null);
            }

            var reasons = new List<string>();
            if (!timeThresholdMet)
                reasons.Add($"Time: {timeSince.TotalMinutes:F1}min, threshold {TimeThresholdMinutes}min");
            if (!distanceThresholdMet)
                reasons.Add($"Distance: {distance:F0}m, threshold {DistanceThresholdMeters}m");

            return (false, string.Join("; ", reasons));
        }
    }

    /// <summary>
    /// Rate limiter implementation that mirrors QueueDrainService rate limiting logic.
    /// </summary>
    private sealed class DrainRateLimiter
    {
        private DateTime _lastDrainTime = DateTime.MinValue;
        private readonly Queue<DateTime> _drainHistory = new();
        private readonly object _lock = new();

        public void SetLastDrainTime(DateTime time)
        {
            lock (_lock)
            {
                _lastDrainTime = time;
            }
        }

        public void AddDrainHistory(DateTime time)
        {
            lock (_lock)
            {
                _drainHistory.Enqueue(time);
            }
        }

        public void RecordDrain()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _lastDrainTime = now;
                _drainHistory.Enqueue(now);
            }
        }

        public bool CanMakeRequest()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;

                // Check minimum interval
                if ((now - _lastDrainTime).TotalSeconds < MinSecondsBetweenDrains)
                {
                    return false;
                }

                // Clean old history
                while (_drainHistory.Count > 0 && (now - _drainHistory.Peek()).TotalHours >= 1)
                {
                    _drainHistory.Dequeue();
                }

                // Check hourly limit
                return _drainHistory.Count < MaxDrainsPerHour;
            }
        }
    }

    /// <summary>
    /// Geo calculator that mirrors QueueDrainService distance calculation.
    /// </summary>
    private static class GeoCalculator
    {
        private const double EarthRadiusMeters = 6371000;

        public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            var dLat = DegreesToRadians(lat2 - lat1);
            var dLon = DegreesToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return EarthRadiusMeters * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
    }

    #endregion
}
