namespace WayfarerMobile.Tests.Unit.Algorithms;

/// <summary>
/// Unit tests for ThresholdFilter location filtering logic.
/// </summary>
public class ThresholdFilterTests
{
    private readonly ThresholdFilter _filter;

    public ThresholdFilterTests()
    {
        _filter = new ThresholdFilter
        {
            TimeThresholdMinutes = 1,
            DistanceThresholdMeters = 50
        };
    }

    #region ShouldLog Tests

    [Fact]
    public void ShouldLog_FirstLocation_ReturnsTrue()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        bool result = _filter.ShouldLog(location);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_SameLocationWithinTimeThreshold_ReturnsFalse()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(30)); // 30s later

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldLog_SameLocationAfterTimeThreshold_ReturnsTrue()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2)); // 2 min later

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_DifferentLocationExceedingDistanceThreshold_ReturnsTrue()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~100m north (exceeds 50m threshold)
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10));

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_DifferentLocationWithinDistanceThreshold_ReturnsFalse()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move ~30m north (within 50m threshold)
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 30);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10));

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldLog_LocationAtExactTimeThreshold_ReturnsTrue()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(1)); // Exactly 1 minute

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldLog_LocationAtExactDistanceThreshold_ReturnsTrue()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // Move slightly more than 50m to ensure we pass the threshold
        // (accounts for floating-point precision in haversine calculation)
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 51);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10));

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region MarkAsLogged Tests

    [Fact]
    public void MarkAsLogged_UpdatesLastLoggedLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        _filter.MarkAsLogged(location);

        // Assert
        _filter.LastLoggedLocation.Should().Be(location);
    }

    [Fact]
    public void MarkAsLogged_OverwritesPreviousLocation()
    {
        // Arrange
        var location1 = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);
        var location2 = CreateLocation(52.0, -0.5, DateTime.UtcNow.AddMinutes(5));

        // Act
        _filter.MarkAsLogged(location1);
        _filter.MarkAsLogged(location2);

        // Assert
        _filter.LastLoggedLocation.Should().Be(location2);
    }

    #endregion

    #region TryLog Tests

    [Fact]
    public void TryLog_FirstLocation_ReturnsTrueAndMarksAsLogged()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);

        // Act
        bool result = _filter.TryLog(location);

        // Assert
        result.Should().BeTrue();
        _filter.LastLoggedLocation.Should().Be(location);
    }

    [Fact]
    public void TryLog_LocationBelowThresholds_ReturnsFalseAndDoesNotMark()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10)); // Within both thresholds

        _filter.TryLog(location1);

        // Act
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeFalse();
        _filter.LastLoggedLocation.Should().Be(location1);
    }

    [Fact]
    public void TryLog_LocationExceedsDistanceThreshold_ReturnsTrueAndMarks()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10));

        _filter.TryLog(location1);

        // Act
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeTrue();
        _filter.LastLoggedLocation.Should().Be(location2);
    }

    [Fact]
    public void TryLog_MultipleLocationsInSequence_FiltersCorrectly()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var locations = new[]
        {
            CreateLocation(51.5074, -0.1278, baseTime),                    // Should log (first)
            CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10)),     // Should NOT log
            CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(30)),     // Should NOT log
            CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2)),      // Should log (time)
            CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2).AddSeconds(10)), // Should NOT log
        };

        // Act & Assert
        _filter.TryLog(locations[0]).Should().BeTrue();
        _filter.TryLog(locations[1]).Should().BeFalse();
        _filter.TryLog(locations[2]).Should().BeFalse();
        _filter.TryLog(locations[3]).Should().BeTrue();
        _filter.TryLog(locations[4]).Should().BeFalse();
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsLastLoggedLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow);
        _filter.MarkAsLogged(location);

        // Act
        _filter.Reset();

        // Assert
        _filter.LastLoggedLocation.Should().BeNull();
    }

    [Fact]
    public void Reset_AllowsNextLocationToPass()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10)); // Normally would fail

        _filter.TryLog(location1);
        _filter.TryLog(location2).Should().BeFalse(); // Verify it fails

        // Act
        _filter.Reset();
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region UpdateThresholds Tests

    [Fact]
    public void UpdateThresholds_ChangesTimeThreshold()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(3)); // 3 minutes

        _filter.TryLog(location1);

        // Initially with 1 minute threshold, 3 minutes should pass
        _filter.ShouldLog(location2).Should().BeTrue();

        // Act - Increase threshold to 5 minutes
        _filter.UpdateThresholds(5, 50);

        // Assert - Now 3 minutes should NOT pass
        _filter.ShouldLog(location2).Should().BeFalse();
    }

    [Fact]
    public void UpdateThresholds_ChangesDistanceThreshold()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime);

        // 75m away
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 75);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10));

        _filter.TryLog(location1);

        // Initially with 50m threshold, 75m should pass
        _filter.ShouldLog(location2).Should().BeTrue();

        // Act - Increase threshold to 100m
        _filter.UpdateThresholds(1, 100);

        // Assert - Now 75m should NOT pass
        _filter.ShouldLog(location2).Should().BeFalse();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task TryLog_ConcurrentCalls_DoesNotThrow()
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
                    51.5074 + (index * 0.0001),
                    -0.1278,
                    baseTime.AddSeconds(index));
                _filter.TryLog(location);
            }));
        }

        // Assert - Should complete without exceptions
        await Task.WhenAll(tasks);
        _filter.LastLoggedLocation.Should().NotBeNull();
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
