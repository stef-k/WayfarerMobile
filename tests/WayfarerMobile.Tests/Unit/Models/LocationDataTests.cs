namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for LocationData class.
/// </summary>
public class LocationDataTests
{
    #region ToString Tests

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        // Arrange
        var timestamp = new DateTime(2025, 12, 11, 14, 30, 45, DateTimeKind.Utc);
        var location = new LocationData
        {
            Latitude = 52.520008,
            Longitude = 13.404954,
            Timestamp = timestamp
        };

        // Act
        var result = location.ToString();

        // Assert
        result.Should().Be("(52.520008, 13.404954) @ 14:30:45");
    }

    [Fact]
    public void ToString_NegativeCoordinates_ReturnsExpectedFormat()
    {
        // Arrange
        var timestamp = new DateTime(2025, 12, 11, 8, 15, 30, DateTimeKind.Utc);
        var location = new LocationData
        {
            Latitude = -33.868820,
            Longitude = 151.209296,
            Timestamp = timestamp
        };

        // Act
        var result = location.ToString();

        // Assert
        result.Should().Be("(-33.868820, 151.209296) @ 08:15:30");
    }

    [Fact]
    public void ToString_ZeroCoordinates_ReturnsExpectedFormat()
    {
        // Arrange
        var timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var location = new LocationData
        {
            Latitude = 0,
            Longitude = 0,
            Timestamp = timestamp
        };

        // Act
        var result = location.ToString();

        // Assert
        result.Should().Be("(0.000000, 0.000000) @ 00:00:00");
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithLatLon_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var location = new LocationData(48.8566, 2.3522);

        // Assert
        location.Latitude.Should().Be(48.8566);
        location.Longitude.Should().Be(2.3522);
    }

    [Fact]
    public void Constructor_WithLatLon_InitializesTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var location = new LocationData(48.8566, 2.3522);

        // Assert
        var after = DateTime.UtcNow;
        location.Timestamp.Should().BeOnOrAfter(before);
        location.Timestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void DefaultConstructor_InitializesTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var location = new LocationData();

        // Assert
        var after = DateTime.UtcNow;
        location.Timestamp.Should().BeOnOrAfter(before);
        location.Timestamp.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void DefaultConstructor_DefaultCoordinatesAreZero()
    {
        // Arrange & Act
        var location = new LocationData();

        // Assert
        location.Latitude.Should().Be(0);
        location.Longitude.Should().Be(0);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void LocationData_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var location = new LocationData
        {
            Latitude = 51.5074,
            Longitude = -0.1278,
            Altitude = 11.5,
            Accuracy = 5.0,
            VerticalAccuracy = 3.0,
            Speed = 1.5,
            Bearing = 180.0,
            BearingAccuracy = 10.0,
            Timestamp = timestamp,
            Provider = "GPS"
        };

        // Assert
        location.Latitude.Should().Be(51.5074);
        location.Longitude.Should().Be(-0.1278);
        location.Altitude.Should().Be(11.5);
        location.Accuracy.Should().Be(5.0);
        location.VerticalAccuracy.Should().Be(3.0);
        location.Speed.Should().Be(1.5);
        location.Bearing.Should().Be(180.0);
        location.BearingAccuracy.Should().Be(10.0);
        location.Timestamp.Should().Be(timestamp);
        location.Provider.Should().Be("GPS");
    }

    [Fact]
    public void LocationData_NullableProperties_CanBeNull()
    {
        // Arrange
        var location = new LocationData
        {
            Latitude = 51.5074,
            Longitude = -0.1278,
            Altitude = null,
            Accuracy = null,
            VerticalAccuracy = null,
            Speed = null,
            Bearing = null,
            BearingAccuracy = null,
            Provider = null
        };

        // Assert
        location.Altitude.Should().BeNull();
        location.Accuracy.Should().BeNull();
        location.VerticalAccuracy.Should().BeNull();
        location.Speed.Should().BeNull();
        location.Bearing.Should().BeNull();
        location.BearingAccuracy.Should().BeNull();
        location.Provider.Should().BeNull();
    }

    [Fact]
    public void LocationData_ExtremeBearing_HandlesFullCircle()
    {
        // Arrange
        var location = new LocationData
        {
            Latitude = 0,
            Longitude = 0,
            Bearing = 359.9
        };

        // Assert
        location.Bearing.Should().Be(359.9);
    }

    [Fact]
    public void LocationData_NegativeSpeed_IsAllowed()
    {
        // Arrange - negative speed might indicate reverse (depends on platform)
        var location = new LocationData
        {
            Latitude = 0,
            Longitude = 0,
            Speed = -1.0
        };

        // Assert
        location.Speed.Should().Be(-1.0);
    }

    #endregion
}
