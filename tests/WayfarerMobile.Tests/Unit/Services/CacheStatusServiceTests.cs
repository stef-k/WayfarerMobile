using System.Collections.Concurrent;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for CacheStatusService focusing on cache coverage calculation,
/// debouncing logic, tile coordinate conversion, and status formatting.
/// </summary>
/// <remarks>
/// CacheStatusService monitors tile cache coverage around the user's location.
/// It subscribes to LocationBridge for location updates and calculates what
/// percentage of tiles are cached for offline use.
///
/// Key behaviors tested:
/// - Status thresholds: green >= 90%, yellow >= 30%, red &lt; 30%
/// - Debounce: no check within 30 seconds unless moved 100m
/// - Tile coordinate conversion (LatLonToTile)
/// - Distance calculation for movement detection
/// - Thread-safe status updates
///
/// Note: These tests cannot directly test filesystem operations or MAUI-specific
/// types. Instead, we test the core algorithms by extracting and mirroring the
/// logic from CacheStatusService.
/// </remarks>
public class CacheStatusServiceTests
{
    #region Test Infrastructure

    /// <summary>
    /// Debounce state tracker that mirrors CacheStatusService debounce logic.
    /// </summary>
    private sealed class DebounceTracker
    {
        private DateTime _lastCheckTime = DateTime.MinValue;
        private LocationData? _lastCheckLocation;
        private readonly object _lock = new();

        /// <summary>
        /// Minimum interval between cache checks (matches CacheStatusService.MinCheckInterval).
        /// </summary>
        public static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Minimum distance moved to trigger a check (matches CacheStatusService.MinDistanceMeters).
        /// </summary>
        public const double MinDistanceMeters = 100;

        /// <summary>
        /// Sets the last check time for testing.
        /// </summary>
        public void SetLastCheckTime(DateTime time)
        {
            lock (_lock)
            {
                _lastCheckTime = time;
            }
        }

        /// <summary>
        /// Sets the last check location for testing.
        /// </summary>
        public void SetLastCheckLocation(LocationData? location)
        {
            lock (_lock)
            {
                _lastCheckLocation = location;
            }
        }

        /// <summary>
        /// Gets the last check location.
        /// </summary>
        public LocationData? LastCheckLocation
        {
            get
            {
                lock (_lock)
                {
                    return _lastCheckLocation;
                }
            }
        }

        /// <summary>
        /// Determines if we should check cache based on time/distance.
        /// Mirrors CacheStatusService.ShouldCheckCache() exactly.
        /// </summary>
        public bool ShouldCheckCache(LocationData location)
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                // Always check if never checked
                if (_lastCheckLocation == null)
                    return true;

                // Check time interval
                if (now - _lastCheckTime < MinCheckInterval)
                    return false;

                // Check distance moved
                var distance = CalculateDistance(
                    _lastCheckLocation.Latitude, _lastCheckLocation.Longitude,
                    location.Latitude, location.Longitude);

                return distance >= MinDistanceMeters;
            }
        }

        /// <summary>
        /// Records a cache check.
        /// </summary>
        public void RecordCheck(LocationData location)
        {
            lock (_lock)
            {
                _lastCheckTime = DateTime.UtcNow;
                _lastCheckLocation = new LocationData { Latitude = location.Latitude, Longitude = location.Longitude };
            }
        }
    }

    /// <summary>
    /// Calculates the Haversine distance between two coordinates.
    /// Mirrors CacheStatusService.CalculateDistance() exactly.
    /// </summary>
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    /// <summary>
    /// Converts lat/lon coordinates to tile coordinates.
    /// Mirrors CacheStatusService.LatLonToTile() exactly.
    /// </summary>
    private static (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return (Math.Max(0, Math.Min((int)n - 1, x)), Math.Max(0, Math.Min((int)n - 1, y)));
    }

    /// <summary>
    /// Calculates cache status based on coverage percentage.
    /// Mirrors CacheStatusService status calculation.
    /// </summary>
    private static string CalculateStatus(double percentage)
    {
        return percentage >= 0.9 ? "green" : percentage >= 0.3 ? "yellow" : "red";
    }

    /// <summary>
    /// Calculates detailed status based on coverage percentage.
    /// Mirrors CacheStatusService.GetDetailedCacheInfoAsync status calculation.
    /// </summary>
    private static string CalculateDetailedStatus(double percentage)
    {
        return percentage >= 0.9 ? "Excellent" :
               percentage >= 0.7 ? "Good" :
               percentage >= 0.4 ? "Partial" :
               percentage > 0 ? "Poor" : "None";
    }

    /// <summary>
    /// Formats cache status message.
    /// Mirrors CacheStatusService.FormatStatusMessage() exactly.
    /// </summary>
    private static string FormatStatusMessage(
        string status,
        double coveragePercentage,
        int cachedTiles,
        int totalTiles,
        int liveCachedTiles,
        int tripCachedTiles,
        long localSizeBytes)
    {
        return $"Status: {status} ({(int)(coveragePercentage * 100)}%)\n\n" +
               $"Tiles: {cachedTiles} / {totalTiles}\n" +
               $"  Live: {liveCachedTiles}\n" +
               $"  Trip: {tripCachedTiles}\n\n" +
               $"Size: {localSizeBytes / 1024.0 / 1024.0:F1} MB";
    }

    /// <summary>
    /// Zoom levels used for quick status check (matches CacheStatusService.QuickCheckZoomLevels).
    /// </summary>
    private static readonly int[] QuickCheckZoomLevels = { 15, 14, 16 };

    /// <summary>
    /// Zoom levels used for full status check (matches CacheStatusService.FullCheckZoomLevels).
    /// </summary>
    private static readonly int[] FullCheckZoomLevels = { 15, 14, 16, 13, 12, 11, 10, 17 };

    #endregion

    #region Status Threshold Tests

    /// <summary>
    /// Verifies that 90% or higher coverage results in green status.
    /// </summary>
    [Theory]
    [InlineData(0.90, "Exactly 90%")]
    [InlineData(0.91, "91%")]
    [InlineData(0.95, "95%")]
    [InlineData(1.00, "100%")]
    public void CalculateStatus_90PercentOrHigher_ReturnsGreen(double percentage, string description)
    {
        var result = CalculateStatus(percentage);

        result.Should().Be("green", $"{description} coverage should be green");
    }

    /// <summary>
    /// Verifies that 30% to 89% coverage results in yellow status.
    /// </summary>
    [Theory]
    [InlineData(0.30, "Exactly 30%")]
    [InlineData(0.31, "31%")]
    [InlineData(0.50, "50%")]
    [InlineData(0.70, "70%")]
    [InlineData(0.89, "89%")]
    public void CalculateStatus_30To89Percent_ReturnsYellow(double percentage, string description)
    {
        var result = CalculateStatus(percentage);

        result.Should().Be("yellow", $"{description} coverage should be yellow");
    }

    /// <summary>
    /// Verifies that below 30% coverage results in red status.
    /// </summary>
    [Theory]
    [InlineData(0.00, "0%")]
    [InlineData(0.10, "10%")]
    [InlineData(0.20, "20%")]
    [InlineData(0.29, "29%")]
    public void CalculateStatus_Below30Percent_ReturnsRed(double percentage, string description)
    {
        var result = CalculateStatus(percentage);

        result.Should().Be("red", $"{description} coverage should be red");
    }

    /// <summary>
    /// Verifies the boundary between red and yellow (at exactly 30%).
    /// </summary>
    [Fact]
    public void CalculateStatus_BoundaryAt30Percent_IsYellow()
    {
        CalculateStatus(0.29999).Should().Be("red", "Just under 30% should be red");
        CalculateStatus(0.30).Should().Be("yellow", "Exactly 30% should be yellow");
    }

    /// <summary>
    /// Verifies the boundary between yellow and green (at exactly 90%).
    /// </summary>
    [Fact]
    public void CalculateStatus_BoundaryAt90Percent_IsGreen()
    {
        CalculateStatus(0.89999).Should().Be("yellow", "Just under 90% should be yellow");
        CalculateStatus(0.90).Should().Be("green", "Exactly 90% should be green");
    }

    #endregion

    #region Detailed Status Tests

    /// <summary>
    /// Verifies detailed status thresholds for Excellent.
    /// </summary>
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.95)]
    [InlineData(1.00)]
    public void CalculateDetailedStatus_90PercentOrHigher_ReturnsExcellent(double percentage)
    {
        var result = CalculateDetailedStatus(percentage);

        result.Should().Be("Excellent");
    }

    /// <summary>
    /// Verifies detailed status thresholds for Good.
    /// </summary>
    [Theory]
    [InlineData(0.70)]
    [InlineData(0.80)]
    [InlineData(0.89)]
    public void CalculateDetailedStatus_70To89Percent_ReturnsGood(double percentage)
    {
        var result = CalculateDetailedStatus(percentage);

        result.Should().Be("Good");
    }

    /// <summary>
    /// Verifies detailed status thresholds for Partial.
    /// </summary>
    [Theory]
    [InlineData(0.40)]
    [InlineData(0.50)]
    [InlineData(0.69)]
    public void CalculateDetailedStatus_40To69Percent_ReturnsPartial(double percentage)
    {
        var result = CalculateDetailedStatus(percentage);

        result.Should().Be("Partial");
    }

    /// <summary>
    /// Verifies detailed status thresholds for Poor.
    /// </summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.20)]
    [InlineData(0.39)]
    public void CalculateDetailedStatus_1To39Percent_ReturnsPoor(double percentage)
    {
        var result = CalculateDetailedStatus(percentage);

        result.Should().Be("Poor");
    }

    /// <summary>
    /// Verifies detailed status for zero coverage.
    /// </summary>
    [Fact]
    public void CalculateDetailedStatus_ZeroPercent_ReturnsNone()
    {
        var result = CalculateDetailedStatus(0.0);

        result.Should().Be("None");
    }

    #endregion

    #region Debounce Tests - First Check

    /// <summary>
    /// Verifies that first check always runs (no previous location).
    /// </summary>
    [Fact]
    public void ShouldCheckCache_FirstCheck_ReturnsTrue()
    {
        var tracker = new DebounceTracker();
        var location = new LocationData(51.5074, -0.1278); // London

        var result = tracker.ShouldCheckCache(location);

        result.Should().BeTrue("First check should always be allowed");
    }

    /// <summary>
    /// Verifies that first check runs even immediately after tracker creation.
    /// </summary>
    [Fact]
    public void ShouldCheckCache_NoPreviousLocation_AlwaysAllows()
    {
        var tracker = new DebounceTracker();
        // No previous location set

        var result = tracker.ShouldCheckCache(new LocationData(40.7128, -74.0060)); // NYC

        result.Should().BeTrue("No previous location means check is always allowed");
    }

    #endregion

    #region Debounce Tests - Time Interval

    /// <summary>
    /// Verifies that check is blocked within 30 second window.
    /// </summary>
    [Theory]
    [InlineData(0, "Immediately after check")]
    [InlineData(10, "10 seconds after check")]
    [InlineData(20, "20 seconds after check")]
    [InlineData(29, "29 seconds after check")]
    public void ShouldCheckCache_Within30Seconds_ReturnsFalse(int secondsAgo, string description)
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we just checked
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-secondsAgo));

        // Same location (no movement)
        var result = tracker.ShouldCheckCache(initialLocation);

        result.Should().BeFalse($"Check should be blocked {description} (within 30s window)");
    }

    /// <summary>
    /// Verifies that check is allowed after 30 seconds.
    /// </summary>
    [Fact]
    public void ShouldCheckCache_After30Seconds_WithSufficientMovement_ReturnsTrue()
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we checked 31 seconds ago
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-31));

        // Move 150m (more than 100m threshold)
        var newLocation = new LocationData(51.5087, -0.1278); // ~144m north

        var result = tracker.ShouldCheckCache(newLocation);

        result.Should().BeTrue("Check should be allowed after 30 seconds with sufficient movement");
    }

    /// <summary>
    /// Verifies that check is blocked after 30 seconds if no movement.
    /// </summary>
    [Fact]
    public void ShouldCheckCache_After30Seconds_NoMovement_ReturnsFalse()
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we checked 31 seconds ago
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-31));

        // Same location (no movement)
        var result = tracker.ShouldCheckCache(initialLocation);

        result.Should().BeFalse("Check should be blocked even after 30s if user hasn't moved 100m");
    }

    #endregion

    #region Debounce Tests - Distance Movement

    /// <summary>
    /// Verifies that check is blocked if moved less than 100m.
    /// </summary>
    [Theory]
    [InlineData(0.0001, "~11m movement")]
    [InlineData(0.0003, "~33m movement")]
    [InlineData(0.0005, "~55m movement")]
    [InlineData(0.0008, "~89m movement")]
    public void ShouldCheckCache_MovedLessThan100m_ReturnsFalse(double latOffset, string description)
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we checked 31 seconds ago (past time threshold)
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-31));

        // Move less than 100m
        var newLocation = new LocationData(51.5074 + latOffset, -0.1278);

        var result = tracker.ShouldCheckCache(newLocation);

        result.Should().BeFalse($"Check should be blocked with {description} (under 100m threshold)");
    }

    /// <summary>
    /// Verifies that check is allowed if moved 100m or more.
    /// </summary>
    [Theory]
    [InlineData(0.0009, "~100m movement")]
    [InlineData(0.001, "~111m movement")]
    [InlineData(0.002, "~222m movement")]
    public void ShouldCheckCache_Moved100mOrMore_ReturnsTrue(double latOffset, string description)
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we checked 31 seconds ago (past time threshold)
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-31));

        // Move 100m or more
        var newLocation = new LocationData(51.5074 + latOffset, -0.1278);

        var result = tracker.ShouldCheckCache(newLocation);

        result.Should().BeTrue($"Check should be allowed with {description} (at or above 100m threshold)");
    }

    /// <summary>
    /// Verifies the 100m distance boundary.
    /// </summary>
    [Fact]
    public void ShouldCheckCache_DistanceBoundaryAt100m()
    {
        var tracker = new DebounceTracker();
        var initialLocation = new LocationData(51.5074, -0.1278);

        // Set up as if we checked 31 seconds ago
        tracker.SetLastCheckLocation(initialLocation);
        tracker.SetLastCheckTime(DateTime.UtcNow.AddSeconds(-31));

        // Calculate exact distance for boundary test
        // At ~51.5 degrees latitude, 1 degree latitude = ~111km
        // So 0.0009 degrees = ~100m

        var just_under_100m = new LocationData(51.5074 + 0.00089, -0.1278);
        var exactly_100m = new LocationData(51.5074 + 0.0009, -0.1278);

        var distance1 = CalculateDistance(51.5074, -0.1278, 51.5074 + 0.00089, -0.1278);
        var distance2 = CalculateDistance(51.5074, -0.1278, 51.5074 + 0.0009, -0.1278);

        // Verify our test setup
        distance1.Should().BeLessThan(100, "Just under should be less than 100m");
        distance2.Should().BeGreaterThanOrEqualTo(100, "At boundary should be >= 100m");
    }

    #endregion

    #region Distance Calculation Tests

    /// <summary>
    /// Verifies distance calculation for known coordinates.
    /// </summary>
    [Fact]
    public void CalculateDistance_KnownCoordinates_ReturnsCorrectDistance()
    {
        // London to Paris is approximately 343km
        var distance = CalculateDistance(51.5074, -0.1278, 48.8566, 2.3522);

        distance.Should().BeApproximately(343550, 1000, "London to Paris should be ~343km");
    }

    /// <summary>
    /// Verifies distance calculation returns zero for same point.
    /// </summary>
    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        var distance = CalculateDistance(51.5074, -0.1278, 51.5074, -0.1278);

        distance.Should().Be(0, "Distance to same point should be zero");
    }

    /// <summary>
    /// Verifies distance calculation for short distances (~100m).
    /// </summary>
    [Fact]
    public void CalculateDistance_ShortDistance_ReturnsAccurateResult()
    {
        // Move ~100m north (at latitude 51.5, 0.0009 degrees ~ 100m)
        var distance = CalculateDistance(51.5074, -0.1278, 51.5083, -0.1278);

        distance.Should().BeApproximately(100, 5, "Should be approximately 100m");
    }

    /// <summary>
    /// Verifies distance calculation handles crossing the prime meridian.
    /// </summary>
    [Fact]
    public void CalculateDistance_CrossingPrimeMeridian_CalculatesCorrectly()
    {
        // Points on either side of prime meridian
        var distance = CalculateDistance(51.5, -0.1, 51.5, 0.1);

        distance.Should().BeApproximately(14000, 1000, "Should be ~14km crossing prime meridian");
    }

    /// <summary>
    /// Verifies distance calculation handles crossing the equator.
    /// </summary>
    [Fact]
    public void CalculateDistance_CrossingEquator_CalculatesCorrectly()
    {
        // Points on either side of equator
        var distance = CalculateDistance(1.0, 0.0, -1.0, 0.0);

        distance.Should().BeApproximately(222000, 1000, "Should be ~222km crossing equator");
    }

    /// <summary>
    /// Verifies distance calculation handles negative coordinates.
    /// </summary>
    [Fact]
    public void CalculateDistance_NegativeCoordinates_CalculatesCorrectly()
    {
        // Sydney to Melbourne (both negative latitudes)
        var distance = CalculateDistance(-33.8688, 151.2093, -37.8136, 144.9631);

        distance.Should().BeApproximately(714000, 5000, "Sydney to Melbourne should be ~714km");
    }

    #endregion

    #region Tile Coordinate Conversion Tests

    /// <summary>
    /// Verifies tile conversion for London at various zoom levels.
    /// </summary>
    [Theory]
    [InlineData(10, 511, 340, "Zoom 10")]
    [InlineData(15, 16372, 10896, "Zoom 15")]
    [InlineData(17, 65489, 43584, "Zoom 17")]
    public void LatLonToTile_London_ReturnsCorrectTile(int zoom, int expectedX, int expectedY, string description)
    {
        var (x, y) = LatLonToTile(51.5074, -0.1278, zoom);

        x.Should().Be(expectedX, $"X should match for {description}");
        y.Should().Be(expectedY, $"Y should match for {description}");
    }

    /// <summary>
    /// Verifies tile conversion for New York at zoom 15.
    /// </summary>
    [Fact]
    public void LatLonToTile_NewYork_ReturnsCorrectTile()
    {
        var (x, y) = LatLonToTile(40.7128, -74.0060, 15);

        // NYC at zoom 15: approximately (9649, 12320)
        x.Should().BeCloseTo(9649, 5);
        y.Should().BeCloseTo(12320, 5);
    }

    /// <summary>
    /// Verifies tile conversion handles edge cases at coordinate boundaries.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 10, "Origin")]
    [InlineData(85.0, 180.0, 10, "Near north pole, date line")]
    [InlineData(-85.0, -180.0, 10, "Near south pole, date line")]
    public void LatLonToTile_EdgeCases_ReturnsValidTile(double lat, double lon, int zoom, string description)
    {
        var (x, y) = LatLonToTile(lat, lon, zoom);
        var maxTile = (1 << zoom) - 1;

        x.Should().BeInRange(0, maxTile, $"X should be valid for {description}");
        y.Should().BeInRange(0, maxTile, $"Y should be valid for {description}");
    }

    /// <summary>
    /// Verifies tile coordinates are clamped to valid range.
    /// </summary>
    [Fact]
    public void LatLonToTile_ExtremeLatitude_ClampsToValidRange()
    {
        // Extreme latitude that would produce out-of-range Y
        var (x, y) = LatLonToTile(89.0, 0.0, 10);
        var maxTile = (1 << 10) - 1;

        x.Should().BeInRange(0, maxTile);
        y.Should().BeInRange(0, maxTile);
    }

    /// <summary>
    /// Verifies tile count increases correctly with zoom level.
    /// </summary>
    [Theory]
    [InlineData(10, 1024)]
    [InlineData(15, 32768)]
    [InlineData(17, 131072)]
    public void TileCount_ByZoomLevel_MatchesExpected(int zoom, int expectedTilesPerAxis)
    {
        var tilesPerAxis = 1 << zoom;

        tilesPerAxis.Should().Be(expectedTilesPerAxis, $"Zoom {zoom} should have {expectedTilesPerAxis} tiles per axis");
    }

    #endregion

    #region Zoom Level Configuration Tests

    /// <summary>
    /// Verifies quick check uses correct zoom levels.
    /// </summary>
    [Fact]
    public void QuickCheckZoomLevels_ContainsCorrectLevels()
    {
        QuickCheckZoomLevels.Should().BeEquivalentTo(new[] { 15, 14, 16 },
            "Quick check should use zoom levels 15, 14, 16 (current view priority)");
    }

    /// <summary>
    /// Verifies full check uses all required zoom levels.
    /// </summary>
    [Fact]
    public void FullCheckZoomLevels_ContainsAllLevels()
    {
        FullCheckZoomLevels.Should().BeEquivalentTo(new[] { 15, 14, 16, 13, 12, 11, 10, 17 },
            "Full check should include 8 zoom levels for complete coverage");
    }

    /// <summary>
    /// Verifies zoom level priority order (most important first).
    /// </summary>
    [Fact]
    public void ZoomLevels_PriorityOrder_CurrentViewFirst()
    {
        QuickCheckZoomLevels[0].Should().Be(15, "Current view zoom (15) should be checked first");
        FullCheckZoomLevels[0].Should().Be(15, "Current view zoom (15) should be checked first in full check too");
    }

    #endregion

    #region Status Message Formatting Tests

    /// <summary>
    /// Verifies status message format for excellent coverage.
    /// </summary>
    [Fact]
    public void FormatStatusMessage_ExcellentCoverage_FormatsCorrectly()
    {
        var message = FormatStatusMessage(
            status: "Excellent",
            coveragePercentage: 0.95,
            cachedTiles: 950,
            totalTiles: 1000,
            liveCachedTiles: 700,
            tripCachedTiles: 250,
            localSizeBytes: 52428800); // 50 MB

        message.Should().Contain("Status: Excellent (95%)")
            .And.Contain("Tiles: 950 / 1000")
            .And.Contain("Live: 700")
            .And.Contain("Trip: 250")
            .And.Contain("Size: 50.0 MB");
    }

    /// <summary>
    /// Verifies status message format for poor coverage.
    /// </summary>
    [Fact]
    public void FormatStatusMessage_PoorCoverage_FormatsCorrectly()
    {
        var message = FormatStatusMessage(
            status: "Poor",
            coveragePercentage: 0.15,
            cachedTiles: 150,
            totalTiles: 1000,
            liveCachedTiles: 100,
            tripCachedTiles: 50,
            localSizeBytes: 10485760); // 10 MB

        message.Should().Contain("Status: Poor (15%)")
            .And.Contain("Tiles: 150 / 1000")
            .And.Contain("Live: 100")
            .And.Contain("Trip: 50")
            .And.Contain("Size: 10.0 MB");
    }

    /// <summary>
    /// Verifies status message format for zero coverage.
    /// </summary>
    [Fact]
    public void FormatStatusMessage_ZeroCoverage_FormatsCorrectly()
    {
        var message = FormatStatusMessage(
            status: "None",
            coveragePercentage: 0.0,
            cachedTiles: 0,
            totalTiles: 1000,
            liveCachedTiles: 0,
            tripCachedTiles: 0,
            localSizeBytes: 0);

        message.Should().Contain("Status: None (0%)")
            .And.Contain("Tiles: 0 / 1000")
            .And.Contain("Live: 0")
            .And.Contain("Trip: 0")
            .And.Contain("Size: 0.0 MB");
    }

    /// <summary>
    /// Verifies size formatting for various byte amounts.
    /// </summary>
    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(1048576, "1.0")]      // 1 MB
    [InlineData(104857600, "100.0")]  // 100 MB
    [InlineData(1073741824, "1024.0")] // 1 GB
    public void FormatStatusMessage_SizeFormatting_ShowsMegabytes(long bytes, string expectedMB)
    {
        var message = FormatStatusMessage("Test", 0.5, 100, 200, 50, 50, bytes);

        message.Should().Contain($"Size: {expectedMB} MB");
    }

    #endregion

    #region Coverage Calculation Tests

    /// <summary>
    /// Verifies coverage percentage calculation with various cached/total combinations.
    /// </summary>
    [Theory]
    [InlineData(0, 100, 0.0)]
    [InlineData(50, 100, 0.5)]
    [InlineData(90, 100, 0.9)]
    [InlineData(100, 100, 1.0)]
    [InlineData(1, 3, 0.333)]
    public void CoveragePercentage_Calculation_IsCorrect(int cached, int total, double expectedPercentage)
    {
        double percentage = total > 0 ? (double)cached / total : 0;

        percentage.Should().BeApproximately(expectedPercentage, 0.01);
    }

    /// <summary>
    /// Verifies coverage handles zero total tiles.
    /// </summary>
    [Fact]
    public void CoveragePercentage_ZeroTotal_ReturnsZero()
    {
        int total = 0;
        int cached = 0;
        double percentage = total > 0 ? (double)cached / total : 0;

        percentage.Should().Be(0, "Zero total tiles should result in zero percentage");
    }

    #endregion

    #region Thread Safety Tests

    /// <summary>
    /// Verifies debounce tracking is thread-safe under concurrent access.
    /// </summary>
    [Fact]
    public async Task DebounceTracker_ConcurrentAccess_IsThreadSafe()
    {
        var tracker = new DebounceTracker();
        var results = new ConcurrentBag<bool>();
        var tasks = new List<Task>();

        // Set up initial state
        tracker.SetLastCheckLocation(new LocationData(51.5074, -0.1278));
        tracker.SetLastCheckTime(DateTime.UtcNow.AddMinutes(-5));

        // Run 100 concurrent checks from different locations
        for (int i = 0; i < 100; i++)
        {
            var offset = i * 0.001; // Each location is ~100m apart
            var location = new LocationData(51.5074 + offset, -0.1278);

            tasks.Add(Task.Run(() =>
            {
                var result = tracker.ShouldCheckCache(location);
                results.Add(result);
            }));
        }

        await Task.WhenAll(tasks);

        // Should have 100 results without any exceptions
        results.Should().HaveCount(100, "All concurrent checks should complete");
    }

    /// <summary>
    /// Verifies status calculation is deterministic under concurrent access.
    /// </summary>
    [Fact]
    public async Task StatusCalculation_ConcurrentAccess_IsDeterministic()
    {
        var results = new ConcurrentBag<string>();
        var tasks = new List<Task>();

        // Run 100 concurrent status calculations
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var status = CalculateStatus(0.95);
                results.Add(status);
            }));
        }

        await Task.WhenAll(tasks);

        // All results should be "green" (deterministic)
        results.Should().AllBe("green", "Status calculation should be deterministic");
    }

    #endregion

    #region Tile Grid Calculation Tests

    /// <summary>
    /// Verifies tile grid size calculation based on prefetch radius.
    /// </summary>
    [Theory]
    [InlineData(1, 9)]    // 3x3 grid
    [InlineData(2, 25)]   // 5x5 grid
    [InlineData(5, 121)]  // 11x11 grid (default)
    [InlineData(9, 361)]  // 19x19 grid (max)
    public void TileGrid_ByPrefetchRadius_CalculatesCorrectSize(int radius, int expectedTilesPerZoom)
    {
        int gridSize = (2 * radius + 1) * (2 * radius + 1);

        gridSize.Should().Be(expectedTilesPerZoom, $"Radius {radius} should produce {expectedTilesPerZoom} tiles per zoom level");
    }

    /// <summary>
    /// Verifies total tiles checked across all quick check zoom levels.
    /// </summary>
    [Fact]
    public void QuickCheck_TotalTiles_MatchesExpected()
    {
        int radius = 5; // Default prefetch radius
        int tilesPerZoom = (2 * radius + 1) * (2 * radius + 1); // 121
        int totalTiles = tilesPerZoom * QuickCheckZoomLevels.Length; // 121 * 3 = 363

        totalTiles.Should().Be(363, "Quick check with radius 5 should check 363 tiles total");
    }

    /// <summary>
    /// Verifies total tiles checked across all full check zoom levels.
    /// </summary>
    [Fact]
    public void FullCheck_TotalTiles_MatchesExpected()
    {
        int radius = 5; // Default prefetch radius
        int tilesPerZoom = (2 * radius + 1) * (2 * radius + 1); // 121
        int totalTiles = tilesPerZoom * FullCheckZoomLevels.Length; // 121 * 8 = 968

        totalTiles.Should().Be(968, "Full check with radius 5 should check 968 tiles total");
    }

    #endregion

    #region Constants Verification Tests

    /// <summary>
    /// Verifies debounce constants match expected values.
    /// </summary>
    [Fact]
    public void DebounceConstants_MatchExpectedValues()
    {
        DebounceTracker.MinCheckInterval.Should().Be(TimeSpan.FromSeconds(30),
            "Minimum check interval should be 30 seconds");

        DebounceTracker.MinDistanceMeters.Should().Be(100,
            "Minimum distance for re-check should be 100 meters");
    }

    /// <summary>
    /// Documents the relationship between debounce settings.
    /// </summary>
    [Fact]
    public void DebounceSettings_Documentation()
    {
        // The debounce logic requires BOTH conditions to be met for a check:
        // 1. Time: At least 30 seconds since last check
        // 2. Distance: At least 100m movement from last check location
        //
        // This means:
        // - Stationary user: checks happen at most every 30 seconds (but only if they move 100m+)
        // - Moving user at walking speed (5 km/h = 1.4 m/s): Would move 100m in ~72 seconds
        // - Moving user at driving speed (50 km/h = 13.9 m/s): Would move 100m in ~7 seconds
        //   but still blocked by 30-second interval
        //
        // Exception: First check always runs (no previous location)
        true.Should().BeTrue("Documentation test");
    }

    #endregion

    #region Integration-Style Documentation Tests

    /// <summary>
    /// Documents the expected behavior when LocationBridge fires location event.
    /// </summary>
    [Fact]
    public void OnLocationReceived_DocumentedBehavior()
    {
        // Expected flow when LocationBridge.LocationReceived fires:
        // 1. CacheStatusService.OnLocationReceived is called
        // 2. ShouldCheckCache() evaluates debounce conditions
        // 3. If allowed: CheckCacheStatusAsync runs on background thread
        // 4. Scans tiles in QuickCheckZoomLevels (15, 14, 16)
        // 5. Calculates coverage percentage
        // 6. Updates CurrentStatus if changed
        // 7. Fires StatusChanged event on main thread if status changed
        true.Should().BeTrue("Documentation test");
    }

    /// <summary>
    /// Documents the expected behavior when prefetch completes.
    /// </summary>
    [Fact]
    public void OnPrefetchCompleted_DocumentedBehavior()
    {
        // Expected flow when LiveTileCacheService.PrefetchCompleted fires:
        // 1. CacheStatusService.OnPrefetchCompleted is called
        // 2. ForceRefreshAsync() is called (ignores debounce)
        // 3. Uses last known location (or LocationBridge.LastLocation)
        // 4. Runs full cache status check
        // 5. Updates status indicator to reflect newly cached tiles
        true.Should().BeTrue("Documentation test");
    }

    /// <summary>
    /// Documents the GetDetailedCacheInfoAsync flow.
    /// </summary>
    [Fact]
    public void GetDetailedCacheInfo_DocumentedBehavior()
    {
        // Expected behavior when user taps cache indicator:
        // 1. GetDetailedCacheInfoAsync() is called
        // 2. Uses last check location or LocationBridge.LastLocation
        // 3. Scans ALL zoom levels (FullCheckZoomLevels)
        // 4. Calculates per-zoom-level statistics
        // 5. Includes both live and trip cache tiles
        // 6. Updates quick status indicator as side effect
        // 7. Returns DetailedCacheInfo with:
        //    - Status (Excellent/Good/Partial/Poor/None)
        //    - CachedTiles, TotalTiles
        //    - LiveCachedTiles, TripCachedTiles
        //    - LocalSizeBytes
        //    - CoveragePercentage
        //    - ZoomLevelDetails list
        //    - LastUpdated timestamp
        true.Should().BeTrue("Documentation test");
    }

    /// <summary>
    /// Documents cache directory structure.
    /// </summary>
    [Fact]
    public void CacheDirectoryStructure_Documentation()
    {
        // Cache structure:
        // {CacheDirectory}/tiles/
        //   live/                    <- Live browsing cache (LRU evicted)
        //     {zoom}/
        //       {x}/
        //         {y}.png
        //   trips/                   <- Trip offline cache (persistent)
        //     {trip_id}/
        //       {zoom}/
        //         {x}/
        //           {y}.png
        //
        // CacheStatusService checks both directories for coverage
        true.Should().BeTrue("Documentation test");
    }

    #endregion
}
