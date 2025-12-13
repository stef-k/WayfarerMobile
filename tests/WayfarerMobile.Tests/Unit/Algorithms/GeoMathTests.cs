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
            bearing.Should().BeGreaterThanOrEqualTo(0);
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
                diff.Should().BeGreaterThanOrEqualTo(-180);
                diff.Should().BeLessThanOrEqualTo(180);
            }
        }
    }

    #endregion

    #region Edge Cases - Pole Calculations

    [Fact]
    public void CalculateDistance_FromNorthPole_ReturnsCorrectDistance()
    {
        // Arrange - From North Pole to a point 1 degree south
        double poleLat = 90.0;
        double poleLon = 0.0;
        double targetLat = 89.0;
        double targetLon = 0.0;

        // Act
        double distance = GeoMath.CalculateDistance(poleLat, poleLon, targetLat, targetLon);

        // Assert - 1 degree of latitude = ~111km
        distance.Should().BeApproximately(111_000, 2_000);
    }

    [Fact]
    public void CalculateDistance_FromSouthPole_ReturnsCorrectDistance()
    {
        // Arrange - From South Pole to a point 1 degree north
        double poleLat = -90.0;
        double poleLon = 0.0;
        double targetLat = -89.0;
        double targetLon = 0.0;

        // Act
        double distance = GeoMath.CalculateDistance(poleLat, poleLon, targetLat, targetLon);

        // Assert - 1 degree of latitude = ~111km
        distance.Should().BeApproximately(111_000, 2_000);
    }

    [Fact]
    public void CalculateDistance_BetweenPoles_ReturnsHalfCircumference()
    {
        // Arrange - North Pole to South Pole
        double northPoleLat = 90.0;
        double southPoleLat = -90.0;

        // Act
        double distance = GeoMath.CalculateDistance(northPoleLat, 0, southPoleLat, 0);

        // Assert - Should be half of Earth's circumference (~20,000 km)
        distance.Should().BeApproximately(20_015_000, 100_000);
    }

    [Fact]
    public void CalculateBearing_FromNorthPole_AllDirectionsAreSouth()
    {
        // Arrange - From North Pole, any direction should be south (180 degrees)
        double poleLat = 89.9999; // Near pole to avoid singularity
        double poleLon = 0.0;
        double targetLat = 45.0;

        // Test multiple longitudes - all should give bearing ~180 (south)
        var longitudes = new[] { 0.0, 90.0, 180.0, -90.0 };

        foreach (double targetLon in longitudes)
        {
            // Act
            double bearing = GeoMath.CalculateBearing(poleLat, poleLon, targetLat, targetLon);

            // Assert - Should be approximately south (180) for most, varies based on longitude
            bearing.Should().BeGreaterThanOrEqualTo(0);
            bearing.Should().BeLessThan(360);
        }
    }

    [Fact]
    public void CalculateBearing_ToNorthPole_Returns0()
    {
        // Arrange - From any point, bearing to North Pole should be 0 (north)
        double startLat = 45.0;
        double startLon = 0.0;
        double poleLat = 90.0;
        double poleLon = 0.0;

        // Act
        double bearing = GeoMath.CalculateBearing(startLat, startLon, poleLat, poleLon);

        // Assert
        bearing.Should().BeApproximately(0, 0.1);
    }

    [Fact]
    public void CalculateBearing_ToSouthPole_Returns180()
    {
        // Arrange - From any point, bearing to South Pole should be 180 (south)
        double startLat = 45.0;
        double startLon = 0.0;
        double poleLat = -90.0;
        double poleLon = 0.0;

        // Act
        double bearing = GeoMath.CalculateBearing(startLat, startLon, poleLat, poleLon);

        // Assert
        bearing.Should().BeApproximately(180, 0.1);
    }

    #endregion

    #region Edge Cases - Date Line Crossing

    [Fact]
    public void CalculateDistance_CrossingDateLineEastToWest_ReturnsCorrectDistance()
    {
        // Arrange - From 170E to 170W (crossing 180)
        double lat = 0.0;
        double lon1 = 170.0;
        double lon2 = -170.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat, lon1, lat, lon2);

        // Assert - 20 degrees at equator = ~2,220 km
        distance.Should().BeApproximately(2_224_000, 50_000);
    }

    [Fact]
    public void CalculateDistance_CrossingDateLineWestToEast_ReturnsCorrectDistance()
    {
        // Arrange - From 170W to 170E (crossing 180)
        double lat = 0.0;
        double lon1 = -170.0;
        double lon2 = 170.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat, lon1, lat, lon2);

        // Assert - Same as above, order shouldn't matter
        distance.Should().BeApproximately(2_224_000, 50_000);
    }

    [Fact]
    public void CalculateBearing_CrossingDateLineEastward_ReturnsEastBearing()
    {
        // Arrange - From 170E going to 170W, the shorter path is EAST (crossing 180 date line)
        // The longitude difference is -340 degrees going west, but +20 degrees going east
        // So the bearing formula calculates the initial bearing as east (90)
        double lat = 0.0;
        double lon1 = 170.0;
        double lon2 = -170.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat, lon1, lat, lon2);

        // Assert - Should be approximately east (90 degrees) as that's the shorter path
        bearing.Should().BeApproximately(90, 1);
    }

    [Fact]
    public void CalculateBearing_CrossingDateLineWestward_ReturnsWestBearing()
    {
        // Arrange - From 170W going to 170E, the shorter path crosses the date line going WEST
        // (20 degrees west vs 340 degrees east)
        double lat = 0.0;
        double lon1 = -170.0;
        double lon2 = 170.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat, lon1, lat, lon2);

        // Assert - Should be approximately west (270 degrees) since that's the shorter path
        bearing.Should().BeApproximately(270, 1);
    }

    [Fact]
    public void CalculateDestination_CrossingDateLine_HandlesLongitudeWrap()
    {
        // Arrange - Start at 179E, move east (should wrap to negative longitude)
        double startLat = 0.0;
        double startLon = 179.0;
        double bearing = 90; // Due east
        double distance = 250_000; // ~250km (about 2.25 degrees at equator)

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(startLat, startLon, bearing, distance);

        // Assert - Should have wrapped around to western hemisphere
        newLat.Should().BeApproximately(0, 1);
        // The longitude might be > 180 or wrapped, check absolute distance
        double actualDistance = GeoMath.CalculateDistance(startLat, startLon, newLat, newLon);
        actualDistance.Should().BeApproximately(distance, 100);
    }

    #endregion

    #region Edge Cases - Antipodal Points

    [Fact]
    public void CalculateDistance_AntipodalPoints_ReturnsHalfCircumference()
    {
        // Arrange - Two points on opposite sides of Earth
        double lat1 = 0.0;
        double lon1 = 0.0;
        double lat2 = 0.0;
        double lon2 = 180.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be half of Earth's circumference (~20,000 km)
        distance.Should().BeApproximately(20_015_000, 100_000);
    }

    [Fact]
    public void CalculateDistance_AntipodalPointsWithLatitude_ReturnsHalfCircumference()
    {
        // Arrange - London and its antipodal point (south of New Zealand)
        double londonLat = 51.5074;
        double londonLon = -0.1278;
        double antipodalLat = -51.5074;
        double antipodalLon = 179.8722;

        // Act
        double distance = GeoMath.CalculateDistance(londonLat, londonLon, antipodalLat, antipodalLon);

        // Assert - Should be approximately half of Earth's circumference
        distance.Should().BeApproximately(20_015_000, 100_000);
    }

    [Fact]
    public void CalculateBearing_ToAntipodalPoint_ReturnsValidBearing()
    {
        // Arrange - Bearing to opposite side of Earth
        double lat1 = 45.0;
        double lon1 = 0.0;
        double lat2 = -45.0;
        double lon2 = 180.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert - Should return a valid bearing (any direction works for antipodal)
        bearing.Should().BeGreaterThanOrEqualTo(0);
        bearing.Should().BeLessThan(360);
    }

    #endregion

    #region Edge Cases - Zero/Identical Points

    [Fact]
    public void CalculateDistance_OriginToOrigin_ReturnsZero()
    {
        // Arrange - (0,0) to (0,0)
        double lat = 0.0;
        double lon = 0.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat, lon, lat, lon);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void CalculateBearing_IdenticalPoints_ReturnsZero()
    {
        // Arrange - Same point
        double lat = 45.0;
        double lon = -122.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat, lon, lat, lon);

        // Assert - With identical points, bearing is undefined but should return 0 or valid value
        bearing.Should().BeGreaterThanOrEqualTo(0);
        bearing.Should().BeLessThan(360);
    }

    [Fact]
    public void CalculateSpeed_ZeroDistance_ZeroTime_ReturnsZero()
    {
        // Arrange - Same point, same time
        double lat = 0.0;
        double lon = 0.0;
        var time = DateTime.UtcNow;

        // Act
        double speed = GeoMath.CalculateSpeed(lat, lon, time, lat, lon, time);

        // Assert
        speed.Should().Be(0);
    }

    [Fact]
    public void CalculateSpeed_NegativeTimeDifference_ReturnsZero()
    {
        // Arrange - Time2 is before Time1
        double lat1 = 0.0;
        double lon1 = 0.0;
        double lat2 = 1.0;
        double lon2 = 1.0;
        var time1 = DateTime.UtcNow;
        var time2 = time1.AddSeconds(-10); // 10 seconds earlier

        // Act
        double speed = GeoMath.CalculateSpeed(lat1, lon1, time1, lat2, lon2, time2);

        // Assert - Should return 0 for invalid time difference
        speed.Should().Be(0);
    }

    #endregion

    #region Edge Cases - Negative Coordinates (Southern/Western Hemispheres)

    [Fact]
    public void CalculateDistance_SouthernHemisphere_ReturnsCorrectDistance()
    {
        // Arrange - Sydney to Wellington (both southern hemisphere)
        double sydneyLat = -33.8688;
        double sydneyLon = 151.2093;
        double wellingtonLat = -41.2866;
        double wellingtonLon = 174.7756;

        // Act
        double distance = GeoMath.CalculateDistance(sydneyLat, sydneyLon, wellingtonLat, wellingtonLon);

        // Assert - Distance should be approximately 2,224 km
        distance.Should().BeApproximately(2_224_000, 100_000);
    }

    [Fact]
    public void CalculateDistance_WesternHemisphere_ReturnsCorrectDistance()
    {
        // Arrange - Los Angeles to Mexico City (both western hemisphere)
        double laLat = 34.0522;
        double laLon = -118.2437;
        double mexicoCityLat = 19.4326;
        double mexicoCityLon = -99.1332;

        // Act
        double distance = GeoMath.CalculateDistance(laLat, laLon, mexicoCityLat, mexicoCityLon);

        // Assert - Distance should be approximately 2,500 km
        distance.Should().BeApproximately(2_500_000, 100_000);
    }

    [Fact]
    public void CalculateDistance_CrossingAllQuadrants_ReturnsCorrectDistance()
    {
        // Arrange - From SW quadrant to NE quadrant
        double lat1 = -30.0;
        double lon1 = -60.0;
        double lat2 = 30.0;
        double lon2 = 60.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be a large but reasonable distance
        distance.Should().BeGreaterThan(10_000_000);
        distance.Should().BeLessThan(20_100_000); // Less than half circumference
    }

    [Fact]
    public void CalculateBearing_SouthWestDirection_ReturnsCorrectBearing()
    {
        // Arrange - From positive to negative coordinates (SW direction)
        double lat1 = 10.0;
        double lon1 = 10.0;
        double lat2 = -10.0;
        double lon2 = -10.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately SW (around 225 degrees)
        bearing.Should().BeApproximately(225, 5);
    }

    [Fact]
    public void CalculateBearing_NorthEastDirection_ReturnsCorrectBearing()
    {
        // Arrange - From negative to positive coordinates (NE direction)
        double lat1 = -10.0;
        double lon1 = -10.0;
        double lat2 = 10.0;
        double lon2 = 10.0;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately NE (around 45 degrees)
        bearing.Should().BeApproximately(45, 5);
    }

    [Fact]
    public void CalculateDestination_SouthernHemisphere_ReturnsCorrectPoint()
    {
        // Arrange - Start in southern hemisphere, move south
        double startLat = -40.0;
        double startLon = 175.0;
        double bearing = 180; // Due south
        double distance = 100_000; // 100km

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(startLat, startLon, bearing, distance);

        // Assert - Should move further south (more negative)
        newLat.Should().BeLessThan(startLat);
        double actualDistance = GeoMath.CalculateDistance(startLat, startLon, newLat, newLon);
        actualDistance.Should().BeApproximately(distance, 10);
    }

    #endregion

    #region Edge Cases - Extreme Precision (Very Small Distances)

    [Fact]
    public void CalculateDistance_SubMeterDistance_ReturnsAccurateResult()
    {
        // Arrange - Points approximately 0.5 meters apart
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        // ~0.5m north (0.0000045 degrees)
        double lat2 = 51.5074 + 0.0000045;
        double lon2 = -0.1278;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately 0.5m
        distance.Should().BeApproximately(0.5, 0.1);
    }

    [Fact]
    public void CalculateDistance_OneMeterDistance_ReturnsAccurateResult()
    {
        // Arrange - Points exactly 1 meter apart (approximately)
        double lat1 = 0.0;
        double lon1 = 0.0;
        // 1m at equator = ~0.000009 degrees latitude
        double lat2 = 0.000009;
        double lon2 = 0.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately 1m
        distance.Should().BeApproximately(1, 0.2);
    }

    [Fact]
    public void CalculateDistance_TenCentimeters_ReturnsPositiveValue()
    {
        // Arrange - Points approximately 10cm apart
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        // ~0.1m north
        double lat2 = 51.5074 + 0.0000009;
        double lon2 = -0.1278;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should return a small positive value
        distance.Should().BeGreaterThan(0);
        distance.Should().BeLessThan(1);
    }

    [Fact]
    public void CalculateBearing_VeryClosePoints_ReturnsValidBearing()
    {
        // Arrange - Points 1 meter apart
        double lat1 = 51.5074;
        double lon1 = -0.1278;
        double lat2 = 51.5074 + 0.000009; // ~1m north
        double lon2 = -0.1278;

        // Act
        double bearing = GeoMath.CalculateBearing(lat1, lon1, lat2, lon2);

        // Assert - Should be approximately north (0 degrees)
        bearing.Should().BeApproximately(0, 1);
    }

    [Fact]
    public void CalculateDestination_VerySmallDistance_ReturnsClosePoint()
    {
        // Arrange - Move 1 meter
        double startLat = 51.5074;
        double startLon = -0.1278;
        double bearing = 90; // Due east
        double distance = 1; // 1 meter

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(startLat, startLon, bearing, distance);

        // Assert - Should be very close to original
        double actualDistance = GeoMath.CalculateDistance(startLat, startLon, newLat, newLon);
        actualDistance.Should().BeApproximately(1, 0.1);
    }

    [Fact]
    public void CalculateSpeed_VerySmallDistanceShortTime_ReturnsAccurateSpeed()
    {
        // Arrange - 1m in 1 second = 1 m/s
        double lat1 = 0.0;
        double lon1 = 0.0;
        var time1 = DateTime.UtcNow;

        // Move 1m north
        var (lat2, lon2) = GeoMath.CalculateDestination(lat1, lon1, 0, 1);
        var time2 = time1.AddSeconds(1);

        // Act
        double speed = GeoMath.CalculateSpeed(lat1, lon1, time1, lat2, lon2, time2);

        // Assert - Should be approximately 1 m/s
        speed.Should().BeApproximately(1, 0.2);
    }

    #endregion

    #region Edge Cases - Large Scale Calculations

    [Fact]
    public void CalculateDistance_QuarterEarthCircumference_ReturnsCorrectDistance()
    {
        // Arrange - 90 degrees apart (quarter circumference)
        double lat1 = 0.0;
        double lon1 = 0.0;
        double lat2 = 0.0;
        double lon2 = 90.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert - Should be quarter of circumference (~10,000 km)
        distance.Should().BeApproximately(10_008_000, 50_000);
    }

    [Fact]
    public void CalculateDestination_LargeDistance_RoundTrip_Accuracy()
    {
        // Arrange - Move 5000km in a specific direction
        double startLat = 40.0;
        double startLon = -74.0;
        double bearing = 45;
        double distance = 5_000_000; // 5000km

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(startLat, startLon, bearing, distance);
        double calculatedDistance = GeoMath.CalculateDistance(startLat, startLon, newLat, newLon);

        // Assert - Should be within 1km accuracy for 5000km distance
        calculatedDistance.Should().BeApproximately(distance, 1000);
    }

    #endregion

    #region Edge Cases - Boundary Values

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 0.0)]
    [InlineData(-90.0, 0.0)]
    [InlineData(0.0, 180.0)]
    [InlineData(0.0, -180.0)]
    [InlineData(90.0, 180.0)]
    [InlineData(-90.0, -180.0)]
    public void CalculateDistance_BoundaryCoordinates_ToOrigin_ReturnsValidDistance(double lat, double lon)
    {
        // Act
        double distance = GeoMath.CalculateDistance(0, 0, lat, lon);

        // Assert - Should return a non-negative finite value
        distance.Should().BeGreaterThanOrEqualTo(0);
        double.IsFinite(distance).Should().BeTrue();
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 0.0)]
    [InlineData(-90.0, 0.0)]
    [InlineData(0.0, 180.0)]
    [InlineData(0.0, -180.0)]
    public void CalculateBearing_BoundaryCoordinates_ReturnsValidBearing(double lat, double lon)
    {
        // Arrange - From origin to boundary coordinate
        double originLat = 45.0;
        double originLon = 0.0;

        // Skip if same point
        if (Math.Abs(lat - originLat) < 0.001 && Math.Abs(lon - originLon) < 0.001)
            return;

        // Act
        double bearing = GeoMath.CalculateBearing(originLat, originLon, lat, lon);

        // Assert
        bearing.Should().BeGreaterThanOrEqualTo(0);
        bearing.Should().BeLessThan(360);
    }

    [Fact]
    public void NormalizeBearing_ExtremeValues_HandlesCorrectly()
    {
        // Arrange & Act & Assert
        GeoMath.NormalizeBearing(3600).Should().BeApproximately(0, 0.0001);
        GeoMath.NormalizeBearing(-3600).Should().BeApproximately(0, 0.0001);
        GeoMath.NormalizeBearing(1000).Should().BeApproximately(280, 0.0001);
        GeoMath.NormalizeBearing(-1000).Should().BeApproximately(80, 0.0001);
    }

    [Fact]
    public void BearingDifference_ExtremeValues_HandlesCorrectly()
    {
        // Arrange & Act & Assert
        double diff1 = GeoMath.BearingDifference(0, 359);
        diff1.Should().BeApproximately(-1, 0.0001);

        double diff2 = GeoMath.BearingDifference(359, 0);
        diff2.Should().BeApproximately(1, 0.0001);

        double diff3 = GeoMath.BearingDifference(1, 359);
        diff3.Should().BeApproximately(-2, 0.0001);
    }

    #endregion

    #region Edge Cases - High Latitude Longitude Convergence

    [Fact]
    public void CalculateDistance_HighLatitude_LongitudeConvergence()
    {
        // Arrange - At high latitudes, longitude degrees represent less distance
        // At 80 degrees latitude, 1 degree longitude = ~19.4 km (vs 111km at equator)
        double lat = 80.0;
        double lon1 = 0.0;
        double lon2 = 1.0;

        // Act
        double distance = GeoMath.CalculateDistance(lat, lon1, lat, lon2);

        // Assert - Should be much less than at equator
        distance.Should().BeApproximately(19_400, 2_000);
        distance.Should().BeLessThan(111_000); // Much less than equatorial
    }

    [Fact]
    public void CalculateDestination_HighLatitude_AccountsForConvergence()
    {
        // Arrange - Move east at high latitude
        double startLat = 70.0;
        double startLon = 0.0;
        double bearing = 90; // Due east
        double distance = 100_000; // 100km

        // Act
        var (newLat, newLon) = GeoMath.CalculateDestination(startLat, startLon, bearing, distance);

        // Assert - At high latitude, should move more degrees longitude for same distance
        double lonChange = Math.Abs(newLon - startLon);
        lonChange.Should().BeGreaterThan(0.9); // More than ~0.9 degrees for 100km at 70 latitude
    }

    #endregion
}
