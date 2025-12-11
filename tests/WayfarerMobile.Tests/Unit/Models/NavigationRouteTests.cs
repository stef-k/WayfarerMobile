namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for NavigationRoute and NavigationWaypoint classes.
/// </summary>
public class NavigationRouteTests
{
    #region NavigationRoute Property Tests

    [Fact]
    public void NavigationRoute_TotalDistanceMeters_CanBeSetAndRetrieved()
    {
        // Arrange
        var route = new NavigationRoute
        {
            TotalDistanceMeters = 5000.5
        };

        // Assert
        route.TotalDistanceMeters.Should().Be(5000.5);
    }

    [Fact]
    public void NavigationRoute_TotalDistanceMeters_DefaultIsZero()
    {
        // Arrange
        var route = new NavigationRoute();

        // Assert
        route.TotalDistanceMeters.Should().Be(0);
    }

    [Fact]
    public void NavigationRoute_Waypoints_DefaultsToEmptyList()
    {
        // Arrange
        var route = new NavigationRoute();

        // Assert
        route.Waypoints.Should().NotBeNull();
        route.Waypoints.Should().BeEmpty();
    }

    [Fact]
    public void NavigationRoute_Waypoints_CanAddWaypoints()
    {
        // Arrange
        var route = new NavigationRoute();
        var waypoint1 = new NavigationWaypoint { Latitude = 52.52, Longitude = 13.405 };
        var waypoint2 = new NavigationWaypoint { Latitude = 48.8566, Longitude = 2.3522 };

        // Act
        route.Waypoints.Add(waypoint1);
        route.Waypoints.Add(waypoint2);

        // Assert
        route.Waypoints.Should().HaveCount(2);
        route.Waypoints[0].Should().Be(waypoint1);
        route.Waypoints[1].Should().Be(waypoint2);
    }

    [Fact]
    public void NavigationRoute_DestinationName_CanBeSetAndRetrieved()
    {
        // Arrange
        var route = new NavigationRoute
        {
            DestinationName = "Brandenburg Gate"
        };

        // Assert
        route.DestinationName.Should().Be("Brandenburg Gate");
    }

    [Fact]
    public void NavigationRoute_DestinationName_DefaultsToEmptyString()
    {
        // Arrange
        var route = new NavigationRoute();

        // Assert
        route.DestinationName.Should().Be(string.Empty);
    }

    [Fact]
    public void NavigationRoute_EstimatedDuration_CanBeSetAndRetrieved()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(45);
        var route = new NavigationRoute
        {
            EstimatedDuration = duration
        };

        // Assert
        route.EstimatedDuration.Should().Be(duration);
    }

    [Fact]
    public void NavigationRoute_EstimatedDuration_DefaultsToZero()
    {
        // Arrange
        var route = new NavigationRoute();

        // Assert
        route.EstimatedDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NavigationRoute_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var waypoints = new List<NavigationWaypoint>
        {
            new() { Latitude = 52.52, Longitude = 13.405, Name = "Start" },
            new() { Latitude = 48.8566, Longitude = 2.3522, Name = "End" }
        };
        var duration = TimeSpan.FromHours(8.5);

        var route = new NavigationRoute
        {
            Waypoints = waypoints,
            DestinationName = "Paris",
            TotalDistanceMeters = 1050000,
            EstimatedDuration = duration
        };

        // Assert
        route.Waypoints.Should().BeSameAs(waypoints);
        route.DestinationName.Should().Be("Paris");
        route.TotalDistanceMeters.Should().Be(1050000);
        route.EstimatedDuration.Should().Be(duration);
    }

    #endregion

    #region NavigationWaypoint Property Tests

    [Fact]
    public void NavigationWaypoint_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Latitude = 51.5074,
            Longitude = -0.1278,
            Name = "Tower of London",
            Type = WaypointType.Waypoint,
            PlaceId = "place-123"
        };

        // Assert
        waypoint.Latitude.Should().Be(51.5074);
        waypoint.Longitude.Should().Be(-0.1278);
        waypoint.Name.Should().Be("Tower of London");
        waypoint.Type.Should().Be(WaypointType.Waypoint);
        waypoint.PlaceId.Should().Be("place-123");
    }

    [Fact]
    public void NavigationWaypoint_DefaultValues_AreCorrect()
    {
        // Arrange
        var waypoint = new NavigationWaypoint();

        // Assert
        waypoint.Latitude.Should().Be(0);
        waypoint.Longitude.Should().Be(0);
        waypoint.Name.Should().BeNull();
        waypoint.Type.Should().Be(WaypointType.Start); // Default enum value
        waypoint.PlaceId.Should().BeNull();
    }

    [Fact]
    public void NavigationWaypoint_Type_Start_CanBeSet()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Type = WaypointType.Start
        };

        // Assert
        waypoint.Type.Should().Be(WaypointType.Start);
    }

    [Fact]
    public void NavigationWaypoint_Type_Waypoint_CanBeSet()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Type = WaypointType.Waypoint
        };

        // Assert
        waypoint.Type.Should().Be(WaypointType.Waypoint);
    }

    [Fact]
    public void NavigationWaypoint_Type_RoutePoint_CanBeSet()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Type = WaypointType.RoutePoint
        };

        // Assert
        waypoint.Type.Should().Be(WaypointType.RoutePoint);
    }

    [Fact]
    public void NavigationWaypoint_Type_Destination_CanBeSet()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Type = WaypointType.Destination
        };

        // Assert
        waypoint.Type.Should().Be(WaypointType.Destination);
    }

    [Fact]
    public void NavigationWaypoint_NegativeCoordinates_AreAllowed()
    {
        // Arrange
        var waypoint = new NavigationWaypoint
        {
            Latitude = -33.8688,
            Longitude = -70.6483
        };

        // Assert
        waypoint.Latitude.Should().Be(-33.8688);
        waypoint.Longitude.Should().Be(-70.6483);
    }

    #endregion
}
