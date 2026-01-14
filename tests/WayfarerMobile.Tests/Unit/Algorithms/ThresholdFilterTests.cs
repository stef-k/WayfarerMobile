namespace WayfarerMobile.Tests.Unit.Algorithms;

/// <summary>
/// Unit tests for ThresholdFilter location filtering logic.
/// Tests AND logic: location passes only if BOTH time AND distance thresholds are exceeded,
/// and accuracy is within threshold.
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
        _filter.UpdateThresholds(1, 50, 50); // 50m accuracy threshold
    }

    #region ShouldLog Tests - AND Logic

    [Fact]
    public void ShouldLog_FirstLocation_ReturnsTrue()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 10);

        // Act
        bool result = _filter.ShouldLog(location);

        // Assert
        result.Should().BeTrue("first location should always pass");
    }

    [Fact]
    public void ShouldLog_FirstLocation_PoorAccuracy_ReturnsFalse()
    {
        // Arrange - First location but accuracy too poor
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 100);

        // Act
        bool result = _filter.ShouldLog(location);

        // Assert
        result.Should().BeFalse("first location with poor accuracy should be rejected");
    }

    [Fact]
    public void ShouldLog_SameLocationWithinTimeThreshold_ReturnsFalse()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(30), accuracy: 10);

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("neither time nor distance threshold exceeded");
    }

    [Fact]
    public void ShouldLog_OnlyTimeThresholdExceeded_ReturnsFalse()
    {
        // Arrange - AND logic: time exceeded but distance NOT exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2), accuracy: 10); // Same location, 2 min later

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("only time threshold exceeded, distance not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldLog_OnlyDistanceThresholdExceeded_ReturnsFalse()
    {
        // Arrange - AND logic: distance exceeded but time NOT exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        // Move ~100m north (exceeds 50m threshold), but only 10 seconds later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10), accuracy: 10);

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("only distance threshold exceeded, time not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldLog_BothThresholdsExceeded_ReturnsTrue()
    {
        // Arrange - AND logic: BOTH time AND distance exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        // Move ~100m north AND 2 minutes later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 10);

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue("both time AND distance thresholds exceeded");
    }

    [Fact]
    public void ShouldLog_BothThresholdsExceeded_PoorAccuracy_ReturnsFalse()
    {
        // Arrange - Both time and distance exceeded, but accuracy too poor
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 100); // Poor accuracy

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("accuracy threshold exceeded even though time and distance passed");
    }

    [Fact]
    public void ShouldLog_LocationAtExactTimeThreshold_OnlyTimeExceeded_ReturnsFalse()
    {
        // Arrange - Time at exact threshold but distance not exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(1), accuracy: 10); // Exactly 1 minute, same location

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("only time at threshold, distance not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldLog_LocationAtExactDistanceThreshold_OnlyDistanceExceeded_ReturnsFalse()
    {
        // Arrange - Distance at exact threshold but time not exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        // Move slightly more than 50m to account for floating-point precision
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 51);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10), accuracy: 10); // 10s, not 1 min

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("only distance at threshold, time not exceeded (AND logic)");
    }

    [Fact]
    public void ShouldLog_BothAtExactThreshold_ReturnsTrue()
    {
        // Arrange - Both at exact threshold
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 51);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(1), accuracy: 10);

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue("both thresholds at exact boundary");
    }

    #endregion

    #region Accuracy Tests

    [Fact]
    public void ShouldLog_PoorAccuracy_ReturnsFalse()
    {
        // Arrange - Accuracy worse than threshold
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 100); // 100m > 50m threshold

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeFalse("accuracy 100m exceeds 50m threshold");
    }

    [Fact]
    public void ShouldLog_GoodAccuracy_PassesToTimeDistanceCheck()
    {
        // Arrange - Good accuracy, both thresholds exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 30); // 30m < 50m threshold

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue("good accuracy and both thresholds exceeded");
    }

    [Fact]
    public void ShouldLog_NullAccuracy_PassesToTimeDistanceCheck()
    {
        // Arrange - Null accuracy should not cause rejection
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: null);

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue("null accuracy should pass to time/distance check");
    }

    [Fact]
    public void ShouldLog_AccuracyAtExactThreshold_Passes()
    {
        // Arrange - Accuracy exactly at threshold (50m = 50m)
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 50); // Exactly at threshold

        _filter.MarkAsLogged(location1);

        // Act
        bool result = _filter.ShouldLog(location2);

        // Assert
        result.Should().BeTrue("accuracy at exact threshold should pass");
    }

    [Fact]
    public void AccuracyThresholdMeters_ReturnsCurrentValue()
    {
        // Arrange
        _filter.UpdateThresholds(1, 50, 75);

        // Act
        var result = _filter.AccuracyThresholdMeters;

        // Assert
        result.Should().Be(75);
    }

    #endregion

    #region MarkAsLogged Tests

    [Fact]
    public void MarkAsLogged_UpdatesLastLoggedLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 10);

        // Act
        _filter.MarkAsLogged(location);

        // Assert
        _filter.LastLoggedLocation.Should().Be(location);
    }

    [Fact]
    public void MarkAsLogged_OverwritesPreviousLocation()
    {
        // Arrange
        var location1 = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 10);
        var location2 = CreateLocation(52.0, -0.5, DateTime.UtcNow.AddMinutes(5), accuracy: 10);

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
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 10);

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
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10), accuracy: 10); // Within both thresholds

        _filter.TryLog(location1);

        // Act
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeFalse();
        _filter.LastLoggedLocation.Should().Be(location1);
    }

    [Fact]
    public void TryLog_OnlyDistanceThresholdExceeded_ReturnsFalseAndDoesNotMark()
    {
        // Arrange - AND logic: distance exceeded but time NOT exceeded
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddSeconds(10), accuracy: 10);

        _filter.TryLog(location1);

        // Act
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeFalse("AND logic: distance only is not enough");
        _filter.LastLoggedLocation.Should().Be(location1);
    }

    [Fact]
    public void TryLog_BothThresholdsExceeded_ReturnsTrueAndMarks()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 10);

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
        // Arrange - Testing AND logic throughout a sequence
        var baseTime = DateTime.UtcNow;
        var (lat100m, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);

        var locations = new[]
        {
            CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10),                     // Should log (first)
            CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10), accuracy: 10),      // Should NOT log (neither exceeded)
            CreateLocation(51.5074, -0.1278, baseTime.AddMinutes(2), accuracy: 10),       // Should NOT log (only time exceeded)
            CreateLocation(lat100m, -0.1278, baseTime.AddSeconds(30), accuracy: 10),      // Should NOT log (only distance exceeded)
            CreateLocation(lat100m, -0.1278, baseTime.AddMinutes(2), accuracy: 10),       // Should log (both exceeded)
        };

        // Act & Assert
        _filter.TryLog(locations[0]).Should().BeTrue("first location");
        _filter.TryLog(locations[1]).Should().BeFalse("neither threshold exceeded");
        _filter.TryLog(locations[2]).Should().BeFalse("only time exceeded - AND requires both");
        _filter.TryLog(locations[3]).Should().BeFalse("only distance exceeded - AND requires both");
        _filter.TryLog(locations[4]).Should().BeTrue("both thresholds exceeded");
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Reset_ClearsLastLoggedLocation()
    {
        // Arrange
        var location = CreateLocation(51.5074, -0.1278, DateTime.UtcNow, accuracy: 10);
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
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);
        var location2 = CreateLocation(51.5074, -0.1278, baseTime.AddSeconds(10), accuracy: 10); // Normally would fail

        _filter.TryLog(location1);
        _filter.TryLog(location2).Should().BeFalse(); // Verify it fails

        // Act
        _filter.Reset();
        bool result = _filter.TryLog(location2);

        // Assert
        result.Should().BeTrue("after reset, any location should pass as first");
    }

    #endregion

    #region UpdateThresholds Tests

    [Fact]
    public void UpdateThresholds_ChangesTimeThreshold()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        // 100m away and 3 minutes later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(3), accuracy: 10);

        _filter.TryLog(location1);

        // Initially with 1 minute threshold, both conditions pass (distance=100m>50m, time=3min>1min)
        _filter.ShouldLog(location2).Should().BeTrue("initial thresholds: 3min > 1min AND 100m > 50m");

        // Act - Increase time threshold to 5 minutes
        _filter.UpdateThresholds(5, 50, 50);

        // Assert - Now time doesn't pass (3min < 5min)
        _filter.ShouldLog(location2).Should().BeFalse("3 minutes < 5 minutes threshold");
    }

    [Fact]
    public void UpdateThresholds_ChangesDistanceThreshold()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        // 75m away and 2 minutes later
        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 75);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 10);

        _filter.TryLog(location1);

        // Initially with 50m threshold, both conditions pass (distance=75m>50m, time=2min>1min)
        _filter.ShouldLog(location2).Should().BeTrue("initial thresholds: 75m > 50m AND 2min > 1min");

        // Act - Increase distance threshold to 100m
        _filter.UpdateThresholds(1, 100, 50);

        // Assert - Now distance doesn't pass (75m < 100m)
        _filter.ShouldLog(location2).Should().BeFalse("75m < 100m threshold");
    }

    [Fact]
    public void UpdateThresholds_ChangesAccuracyThreshold()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var location1 = CreateLocation(51.5074, -0.1278, baseTime, accuracy: 10);

        var (newLat, _) = GeoMath.CalculateDestination(51.5074, -0.1278, 0, 100);
        var location2 = CreateLocation(newLat, -0.1278, baseTime.AddMinutes(2), accuracy: 75); // 75m accuracy

        _filter.TryLog(location1);

        // Initially with 50m accuracy threshold, 75m accuracy fails
        _filter.ShouldLog(location2).Should().BeFalse("75m accuracy > 50m threshold");

        // Act - Increase accuracy threshold to 100m
        _filter.UpdateThresholds(1, 50, 100);

        // Assert - Now accuracy passes (75m < 100m)
        _filter.ShouldLog(location2).Should().BeTrue("75m accuracy < 100m threshold");
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
                    51.5074 + (index * 0.001), // Move enough to exceed distance
                    -0.1278,
                    baseTime.AddMinutes(index), // Move enough to exceed time
                    accuracy: 10);
                _filter.TryLog(location);
            }));
        }

        // Assert - Should complete without exceptions
        await Task.WhenAll(tasks);
        _filter.LastLoggedLocation.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private static LocationData CreateLocation(double latitude, double longitude, DateTime timestamp, double? accuracy)
    {
        return new LocationData
        {
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp,
            Accuracy = accuracy,
            Provider = "test"
        };
    }

    #endregion
}
