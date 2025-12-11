namespace WayfarerMobile.Tests.Unit.Algorithms;

/// <summary>
/// Unit tests for GeoMath geographic calculation utilities.
/// </summary>
public class GeoMathTests
{
    #region CalculateDistance Tests

    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;

        // Act
        double distance = GeoMath.CalculateDistance(lat, lon, lat, lon);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void CalculateDistance_LondonToNewYork_ReturnsApproximatelyCorrectDistance()
    {
        // Arrange - London to New York
        double londonLat = 51.5074;
        double londonLon = -0.1278;
        double nyLat = 40.7128;
        double nyLon = -74.0060;

        // Act
        double distance = GeoMath.CalculateDistance(londonLat, londonLon, nyLat, nyLon);

        // Assert - Distance should be approximately 5,570 km
        distance.Should().BeApproximately(5_570_000, 50_000); // Within 50km tolerance
    }

    [Fact]
    public void CalculateDistance_ShortDistance_ReturnsAccurateResult()
    {
        // Arrange - Two points approximately 100 meters apart
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        double lat2 = 51.5083;  // ~100m north
        double lon2 = -0.1278;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately 100m
        distance.Should().BeApproximately(100, 10);
    }

    [Fact]
    public void CalculateDistance_AcrossEquator_ReturnsCorrectDistance()
    {
        // Arrange - Point above and below equator
        double lat1 = 1.0;
        double lon1 = 0.0;
        double lat2 = -1.0;
        double lon2 = 0.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - 2 degrees of latitude ≈ 222km
        distance.Should().BeApproximately(222_000, 5_000);
    }

    [Fact]
    public void CalculateDistance_AcrossDateLine_ReturnsCorrectDistance()
    {
        // Arrange - Points across the International Date Line
        double lat1 = 0.0;
        double lon1 = 179.0;
        double lat2 = 0.0;
        double lon2 = -179.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately 222km (2 degrees at equator)
        distance.Should().BeApproximately(222_000, 5_000);
    }

    #endregion

    #region CalculateBearing Tests

    [Fact]
    public void CalculateBearing_DueNorth_Returns0()
    {
        // Arrange
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        double lat2 = 52.5074;  // 1 degree north
        double lon2 = -0.1278;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert
        bearing.Should().BeApproximately(0, 0.1);
    }

    [Fact]
    public void CalculateBearing_DueEast_Returns90()
    {
        // Arrange
        double lat1 = 0.0;
        double lon1 = 0.0;
        double lat2 = 0.0;
        double lon2 = 1.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert
        bearing.Should().BeApproximately(90, 0.1);
    }

    [Fact]
    public void CalculateBearing_DueSouth_Returns180()
    {
        // Arrange
        double lat1 = 1.0;
        double lon1 = 0.0;
        double lat2 = 0.0;
        double lon2 = 0.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert
        bearing.Should().BeApproximately(180, 0.1);
    }

    [Fact]
    public void CalculateBearing_DueWest_Returns270()
    {
        // Arrange
        double lat1 = 0.0;
        double lon1 = 1.0;
        double lat2 = 0.0;
        double lon2 = 0.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert
        bearing.Should().BeApproximately(270, 0.1);
    }

    [Fact]
    public void CalculateBearing_ReturnsValueBetween0And360()
    {
        // Arrange - Various test cases
        var testCases = new[]
        {
            (lat1: 0.0, lon1: 0.0, lat2: 1.0, lon2: 1.0),
            (lat1: 0.0, lon1: 0.0, lat2: -1.0, lon2: -1.0),
            (lat1: 45.0, lon1: 90.0, lat2: -45.0, lon2: -90.0),
        };

        foreach (var (lat1, lon1, lat2, lon2) in testCases)
        {
            // Act
            double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

            // Assert
            bearing.Should().BeGreaterOrEqualTo(0);
            bearing.Should().BeLessThan(360);
        }
    }

    #endregion

    #region CalculateDestination Tests

    [Fact]
    public void CalculateDestination_ZeroDistance_ReturnsSamePoint()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(lat, lon, 0, 0);

        // Assert
        newLat.Should().BeApproximately(lat, 0.0001);
        newLon.Should().BeApproximately(lon, 0.0001);
    }

    [Fact]
    public void CalculateDestination_NorthByKnownDistance_ReturnsCorrectPoint()
    {
        // Arrange - Move 1km north from equator
        double lat = 0.0;
        double lon = 0.0;
        double bearing = 0; // Due north
        double distance = 1000; // 1km

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(lat, lon, bearing, distance);

        // Assert - 1km north at equator ≈ 0.009 degrees
        newLat.Should().BeApproximately(0.009, 0.001);
        newLon.Should().BeApproximately(0.0, 0.001);
    }

    [Fact]
    public void CalculateDestination_RoundTrip_ReturnsOriginalDistance()
    {
        // Arrange
        double lat = 51.5074;
        double lon = -0.1278;
        double bearing = 45;
        double distance = 10000; // 10km

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(lat, lon, bearing, distance);
        double calculatedDistance = GeoMath.CalculateDistance(lat, lon, newLat, newLon);

        // Assert
        calculatedDistance.Should().BeApproximately(distance, 1); // Within 1 meter
    }

    #endregion

    #region CalculateSpeed Tests

    [Fact]
    public void CalculateSpeed_SameTime_ReturnsZero()
    {
        // Arrange
        var time = DateTime.UtcNow;

        // Act
        double speed = GeoMath.CalculateSpeed(0, 0, time, 1, 1, time);

        // Assert
        speed.Should().Be(0);
    }

    [Fact]
    public void CalculateSpeed_KnownDistanceAndTime_ReturnsCorrectSpeed()
    {
        // Arrange - 100m in 10 seconds = 10 m/s
        double lat1 = 0.0;
        double lon1 = 0.0;
        var time1 = DateTime.UtcNow;

        // Calculate point 100m north
        var (lat2, lon2) = GeoMath.CalculateDestination(lat1, lon1, 0, 100);
        var time2 = time1.AddSeconds(10);

        // Act
        double speed = GeoMath.CalculateSpeed(lat1, lon1, time1, lat2, lon2, time2);

        // Assert
        speed.Should().BeApproximately(10, 0.5); // 10 m/s ± 0.5
    }

    [Fact]
    public void CalculateSpeed_WalkingSpeed_ReturnsReasonableValue()
    {
        // Arrange - ~5 km/h walking speed (1.4 m/s)
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        var time1 = DateTime.UtcNow;

        // 1.4 m/s for 60 seconds = 84 meters
        var (lat2, lon2) = GeoMath.CalculateDestination(lat1, lon1, 45, 84);
        var time2 = time1.AddSeconds(60);

        // Act
        double speed = GeoMath.CalculateSpeed(lat1, lon1, time1, lat2, lon2, time2);

        // Assert
        speed.Should().BeApproximately(1.4, 0.1);
    }

    #endregion

    #region ToRadians / ToDegrees Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, Math.PI / 2)]
    [InlineData(180, Math.PI)]
    [InlineData(360, 2 * Math.PI)]
    [InlineData(-90, -Math.PI / 2)]
    public void ToRadians_KnownValues_ReturnsCorrectResult(double degrees, double expectedRadians)
    {
        // Act
        double radians = GeoMath.ToRadians(degrees);

        // Assert
        radians.Should().BeApproximately(expectedRadians, 0.0001);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Math.PI / 2, 90)]
    [InlineData(Math.PI, 180)]
    [InlineData(2 * Math.PI, 360)]
    [InlineData(-Math.PI / 2, -90)]
    public void ToDegrees_KnownValues_ReturnsCorrectResult(double radians, double expectedDegrees)
    {
        // Act
        double degrees = GeoMath.ToDegrees(radians);

        // Assert
        degrees.Should().BeApproximately(expectedDegrees, 0.0001);
    }

    [Fact]
    public void ToRadians_ToDegrees_RoundTrip_ReturnsOriginalValue()
    {
        // Arrange
        double original = 123.456;

        // Act
        double result = GeoMath.ToDegrees(GeoMath.ToRadians(original));

        // Assert
        result.Should().BeApproximately(original, 0.0001);
    }

    #endregion

    #region NormalizeBearing Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(180, 180)]
    [InlineData(270, 270)]
    [InlineData(359.9, 359.9)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(-90, 270)]
    [InlineData(-180, 180)]
    [InlineData(-270, 90)]
    [InlineData(-360, 0)]
    [InlineData(720, 0)]
    public void NormalizeBearing_VariousInputs_ReturnsValueBetween0And360(double input, double expected)
    {
        // Act
        double normalized = GeoMath.NormalizeBearing(input);

        // Assert
        normalized.Should().BeApproximately(expected, 0.0001);
    }

    #endregion

    #region BearingDifference Tests

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 90, 90)]
    [InlineData(90, 0, -90)]
    [InlineData(0, 180, 180)]
    [InlineData(180, 0, -180)]
    [InlineData(350, 10, 20)]      // Crossing 0
    [InlineData(10, 350, -20)]     // Crossing 0 other direction
    [InlineData(45, 315, -90)]
    [InlineData(315, 45, 90)]
    public void BearingDifference_VariousInputs_ReturnsCorrectDifference(
        double bearing1, double bearing2, double expectedDiff)
    {
        // Act
        double diff = GeoMath.BearingDifference(bearing1, bearing2);

        // Assert
        diff.Should().BeApproximately(expectedDiff, 0.0001);
    }

    [Fact]
    public void BearingDifference_AlwaysReturnsBetweenNeg180And180()
    {
        // Arrange - Test many combinations
        for (double b1 = 0; b1 < 360; b1 += 30)
        {
            for (double b2 = 0; b2 < 360; b2 += 30)
            {
                // Act
                double diff = GeoMath.BearingDifference(b1, b2);

                // Assert
                diff.Should().BeGreaterOrEqualTo(-180);
                diff.Should().BeLessOrEqualTo(180);
            }
        }
    }

    #endregion
}
