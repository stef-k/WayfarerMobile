namespace WayfarerMobile.Tests.Unit.Algorithms;

/// <summary>
/// Unit tests for LocalTimelineFilter AND logic (matching server behavior).
/// Unlike ThresholdFilter which uses OR logic, this filter requires BOTH
/// time AND distance thresholds to be exceeded.
/// </summary>
public class LocalTimelineFilterTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly LocalTimelineFilter _filter;

    public LocalTimelineFilterTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockSettings.Setup(s => s.LocationTimeThresholdMinutes).Returns(1);
        _mockSettings.Setup(s => s.LocationDistanceThresholdMeters).Returns(50);

        _filter = new LocalTimelineFilter(_mockSettings.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LocalTimelineFilter(null!));
    }

    #endregion

    #region ShouldStore Tests - AND Logic

    [Fact]
    public void ShouldStore_FirstLocation_ReturnsTrue()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        var result = _filter.ShouldStore(location);

        // Assert
        result.Should().BeTrue("first location should always pass");
    }

    [Fact]
    public void ShouldStore_NullLocation_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _filter.ShouldStore(null!));
    }

    [Fact]
    public void ShouldStore_OnlyTimeThresholdExceeded_ReturnsFalse()
    {
        // Arrange - AND logic: time exceeded but distance not exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2)); // Same location, 2 min later

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeFalse("only time threshold exceeded, distance not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldStore_OnlyDistanceThresholdExceeded_ReturnsFalse()
    {
        // Arrange - AND logic: distance exceeded but time not exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~100m north, but only 30 seconds later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(30));

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeFalse("only distance threshold exceeded, time not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldStore_BothThresholdsExceeded_ReturnsTrue()
    {
        // Arrange - AND logic: both time AND distance exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~100m north AND 2 minutes later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2));

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeTrue("both time AND distance thresholds exceeded");
    }

    [Fact]
    public void ShouldStore_NeitherThresholdExceeded_ReturnsFalse()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~30m north, only 30 seconds later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 30);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(30));

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeFalse("neither threshold exceeded");
    }

    [Fact]
    public void ShouldStore_ExactlyAtTimeThreshold_ReturnsTrue_WhenDistanceAlsoExceeded()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~100m north, exactly 1 minute later (at threshold)
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(1));

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeTrue("exactly at time threshold with distance exceeded");
    }

    [Fact]
    public void ShouldStore_ExactlyAtDistanceThreshold_ReturnsTrue_WhenTimeAlsoExceeded()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move slightly more than 50m north to account for floating-point precision, 2 minutes later
        // The haversine round-trip (calculate destination then calculate distance) has small precision errors
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 51);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2));

        _filter.MarkAsStored(location1);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert
        result.Should().BeTrue("at distance threshold with time exceeded");
    }

    #endregion

    #region Threshold Property Tests

    [Fact]
    public void TimeThresholdMinutes_ReturnsValueFromSettings()
    {
        // Arrange
        _mockSettings.Setup(s => s.LocationTimeThresholdMinutes).Returns(5);

        // Act
        var result = _filter.TimeThresholdMinutes;

        // Assert
        result.Should().Be(5);
    }

    [Fact]
    public void DistanceThresholdMeters_ReturnsValueFromSettings()
    {
        // Arrange
        _mockSettings.Setup(s => s.LocationDistanceThresholdMeters).Returns(100);

        // Act
        var result = _filter.DistanceThresholdMeters;

        // Assert
        result.Should().Be(100);
    }

    #endregion

    #region MarkAsStored Tests

    [Fact]
    public void MarkAsStored_UpdatesLastStoredLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        _filter.MarkAsStored(location);

        // Assert
        _filter.LastStoredLocation.Should().Be(location);
    }

    [Fact]
    public void MarkAsStored_NullLocation_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _filter.MarkAsStored(null!));
    }

    [Fact]
    public void MarkAsStored_OverwritesPreviousLocation()
    {
        // Arrange
        var location1 = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);
        var location2 = CreateLocation(52.0, -0.5, DateTime.UtcNow.AddHours(1));

        // Act
        _filter.MarkAsStored(location1);
        _filter.MarkAsStored(location2);

        // Assert
        _filter.LastStoredLocation.Should().Be(location2);
    }

    #endregion

    #region TryStore Tests

    [Fact]
    public void TryStore_FirstLocation_ReturnsTrueAndMarksAsStored()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        var result = _filter.TryStore(location);

        // Assert
        result.Should().BeTrue();
        _filter.LastStoredLocation.Should().Be(location);
    }

    [Fact]
    public void TryStore_NullLocation_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _filter.TryStore(null!));
    }

    [Fact]
    public void TryStore_BelowThresholds_ReturnsFalseAndDoesNotMark()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(30)); // Within both thresholds

        _filter.TryStore(location1);

        // Act
        var result = _filter.TryStore(location2);

        // Assert
        result.Should().BeFalse();
        _filter.LastStoredLocation.Should().Be(location1);
    }

    [Fact]
    public void TryStore_ExceedsBothThresholds_ReturnsTrueAndMarks()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2));

        _filter.TryStore(location1);

        // Act
        var result = _filter.TryStore(location2);

        // Assert
        result.Should().BeTrue();
        _filter.LastStoredLocation.Should().Be(location2);
    }

    [Fact]
    public void TryStore_MultipleLocationsInSequence_FiltersCorrectly()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var (lat100m, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);

        var locations = new[]
        {
            CreateLocation(51.5074, -0.1278, baseTime),                     // Should store (first)
            CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10)),      // Should NOT store (neither exceeded)
            CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2)),       // Should NOT store (only time exceeded)
            CreateLocation(lat100m, -0.1278, baseTime.AddSeconds(30)),      // Should NOT store (only distance exceeded)
            CreateLocation(lat100m, -0.1278, baseTime.AddMinutes(2)),       // Should store (both exceeded)
        };

        // Act & Assert
        _filter.TryStore(locations[0]).Should().BeTrue("first location");
        _filter.TryStore(locations[1]).Should().BeFalse("neither threshold exceeded");
        _filter.TryStore(locations[2]).Should().BeFalse("only time exceeded");
        _filter.TryStore(locations[3]).Should().BeFalse("only distance exceeded");
        _filter.TryStore(locations[4]).Should().BeTrue("both thresholds exceeded");
    }

    #endregion

    #region Initialize Tests

    [Fact]
    public void Initialize_SetsLastStoredLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        _filter.Initialize(location);

        // Assert
        _filter.LastStoredLocation.Should().Be(location);
    }

    [Fact]
    public void Initialize_WithNull_SetsLastStoredLocationToNull()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);
        _filter.MarkAsStored(location);

        // Act
        _filter.Initialize(null);

        // Assert
        _filter.LastStoredLocation.Should().BeNull();
    }

    [Fact]
    public void Initialize_AllowsSubsequentFiltering()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var initialLocation = CreateLocation(51.5074, -0.1278, baseTime.AddHours(-1));
        _filter.Initialize(initialLocation);

        // Move 100m and 2 minutes from the initialized location's time
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var newLocation = CreateLocation(newLat, -0.1278, baseTime.AddHours(-1).AddMinutes(2));

        // Act
        var result = _filter.ShouldStore(newLocation);

        // Assert
        result.Should().BeTrue("both thresholds exceeded from initialized location");
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsLastStoredLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);
        _filter.MarkAsStored(location);

        // Act
        _filter.Reset();

        // Assert
        _filter.LastStoredLocation.Should().BeNull();
    }

    [Fact]
    public void Reset_AllowsNextLocationToPass()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10)); // Would normally fail

        _filter.TryStore(location1);
        _filter.TryStore(location2).Should().BeFalse("verify it fails before reset");

        // Act
        _filter.Reset();
        var result = _filter.TryStore(location2);

        // Assert
        result.Should().BeTrue("first location after reset should pass");
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task TryStore_ConcurrentCalls_DoesNotThrow()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var tasks = new List<Task>();

        // Act - Simulate concurrent location updates
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                var location = CreateLocation(
                    51.5074 + (index * 0.001), // Move enough to exceed distance
                    -0.1278,
                    baseTime.AddMinutes(index)); // Move enough to exceed time
                _filter.TryStore(location);
            }));
        }

        // Assert - Should complete without exceptions
        await Task.WhenAll(tasks);
        _filter.LastStoredLocation.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldStore_ConcurrentReadsAndWrites_DoesNotThrow()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        _filter.MarkAsStored(CreateLocation(51.5074, -0.1278, baseTime));

        var tasks = new List<Task>();

        // Act - Mix of reads and writes
        for (int i = 0; i < 100; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                var location = CreateLocation(
                    51.5074 + (index * 0.001),
                    -0.1278,
                    baseTime.AddMinutes(index));

                // Alternate between read and write
                if (index % 2 == 0)
                    _filter.ShouldStore(location);
                else
                    _filter.TryStore(location);
            }));
        }

        // Assert
        await Task.WhenAll(tasks);
    }

    #endregion

    #region Dynamic Threshold Tests

    [Fact]
    public void ShouldStore_UsesCurrentThresholds_FromSettings()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        _filter.MarkAsStored(location1);

        // Move 75m and 3 minutes - initially this should fail with 50m distance threshold
        var (lat75m, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 75);
        var location2 = CreateLocation(lat75m, -0.1278, baseTime.AddMinutes(3));

        // Initially: time=1min (exceeded), distance=50m (75m exceeds)
        _filter.ShouldStore(location2).Should().BeTrue("initial thresholds: 75m > 50m AND 3min > 1min");

        // Change thresholds dynamically
        _mockSettings.Setup(s => s.LocationDistanceThresholdMeters).Returns(100);

        // Act
        var result = _filter.ShouldStore(location2);

        // Assert - Now 75m < 100m so should fail
        result.Should().BeFalse("75m < 100m new threshold (AND logic requires both)");
    }

    #endregion

    #region Helper Methods

    private static LocationData CreateLocation(double latitude, double longitude, DateTime timestamp)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp,
            Accuracy = 10,
            Provider = "test"
        };
    }

    #endregion
}
