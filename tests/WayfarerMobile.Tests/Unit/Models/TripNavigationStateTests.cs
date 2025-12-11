namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for TripNavigationState class.
/// </summary>
public class TripNavigationStateTests
{
    #region Status Property Tests

    [Fact]
    public void Status_NoRoute_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            Status = NavigationStatus.NoRoute
        };

        // Assert
        state.Status.Should().Be(NavigationStatus.NoRoute);
    }

    [Fact]
    public void Status_OnRoute_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            Status = NavigationStatus.OnRoute
        };

        // Assert
        state.Status.Should().Be(NavigationStatus.OnRoute);
    }

    [Fact]
    public void Status_OffRoute_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            Status = NavigationStatus.OffRoute
        };

        // Assert
        state.Status.Should().Be(NavigationStatus.OffRoute);
    }

    [Fact]
    public void Status_Arrived_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            Status = NavigationStatus.Arrived
        };

        // Assert
        state.Status.Should().Be(NavigationStatus.Arrived);
    }

    [Fact]
    public void Status_DefaultValue_IsNoRoute()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.Status.Should().Be(NavigationStatus.NoRoute);
    }

    #endregion

    #region Message Property Tests

    [Fact]
    public void Message_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            Message = "Calculating route..."
        };

        // Assert
        state.Message.Should().Be("Calculating route...");
    }

    [Fact]
    public void Message_DefaultValue_IsNull()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.Message.Should().BeNull();
    }

    #endregion

    #region CurrentInstruction Property Tests

    [Fact]
    public void CurrentInstruction_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            CurrentInstruction = "Turn right in 200 meters"
        };

        // Assert
        state.CurrentInstruction.Should().Be("Turn right in 200 meters");
    }

    [Fact]
    public void CurrentInstruction_DefaultValue_IsNull()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.CurrentInstruction.Should().BeNull();
    }

    #endregion

    #region Distance Property Tests

    [Fact]
    public void DistanceToDestinationMeters_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            DistanceToDestinationMeters = 5432.5
        };

        // Assert
        state.DistanceToDestinationMeters.Should().Be(5432.5);
    }

    [Fact]
    public void DistanceToDestinationMeters_DefaultValue_IsZero()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.DistanceToDestinationMeters.Should().Be(0);
    }

    [Fact]
    public void DistanceToNextWaypointMeters_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            DistanceToNextWaypointMeters = 150.75
        };

        // Assert
        state.DistanceToNextWaypointMeters.Should().Be(150.75);
    }

    [Fact]
    public void DistanceToNextWaypointMeters_DefaultValue_IsZero()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.DistanceToNextWaypointMeters.Should().Be(0);
    }

    #endregion

    #region BearingToDestination Property Tests

    [Fact]
    public void BearingToDestination_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            BearingToDestination = 270.5
        };

        // Assert
        state.BearingToDestination.Should().Be(270.5);
    }

    [Fact]
    public void BearingToDestination_DefaultValue_IsZero()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.BearingToDestination.Should().Be(0);
    }

    [Fact]
    public void BearingToDestination_FullCircle_IsAllowed()
    {
        // Arrange
        var state = new TripNavigationState
        {
            BearingToDestination = 359.9
        };

        // Assert
        state.BearingToDestination.Should().Be(359.9);
    }

    #endregion

    #region NextWaypointName Property Tests

    [Fact]
    public void NextWaypointName_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            NextWaypointName = "Eiffel Tower"
        };

        // Assert
        state.NextWaypointName.Should().Be("Eiffel Tower");
    }

    [Fact]
    public void NextWaypointName_DefaultValue_IsNull()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.NextWaypointName.Should().BeNull();
    }

    #endregion

    #region EstimatedTimeRemaining Property Tests

    [Fact]
    public void EstimatedTimeRemaining_CanBeSetAndRetrieved()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(25);
        var state = new TripNavigationState
        {
            EstimatedTimeRemaining = duration
        };

        // Assert
        state.EstimatedTimeRemaining.Should().Be(duration);
    }

    [Fact]
    public void EstimatedTimeRemaining_DefaultValue_IsZero()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.EstimatedTimeRemaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void EstimatedTimeRemaining_LongDuration_IsAllowed()
    {
        // Arrange
        var duration = TimeSpan.FromHours(24);
        var state = new TripNavigationState
        {
            EstimatedTimeRemaining = duration
        };

        // Assert
        state.EstimatedTimeRemaining.Should().Be(duration);
    }

    #endregion

    #region ProgressPercent Property Tests

    [Fact]
    public void ProgressPercent_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new TripNavigationState
        {
            ProgressPercent = 75.5
        };

        // Assert
        state.ProgressPercent.Should().Be(75.5);
    }

    [Fact]
    public void ProgressPercent_DefaultValue_IsZero()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.ProgressPercent.Should().Be(0);
    }

    [Fact]
    public void ProgressPercent_FullProgress_IsAllowed()
    {
        // Arrange
        var state = new TripNavigationState
        {
            ProgressPercent = 100.0
        };

        // Assert
        state.ProgressPercent.Should().Be(100.0);
    }

    [Fact]
    public void ProgressPercent_ZeroProgress_IsAllowed()
    {
        // Arrange
        var state = new TripNavigationState
        {
            ProgressPercent = 0.0
        };

        // Assert
        state.ProgressPercent.Should().Be(0.0);
    }

    #endregion

    #region All Properties Combined Tests

    [Fact]
    public void TripNavigationState_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(15);
        var state = new TripNavigationState
        {
            Status = NavigationStatus.OnRoute,
            Message = "Following route",
            CurrentInstruction = "Continue straight for 500 meters",
            DistanceToDestinationMeters = 2500.0,
            DistanceToNextWaypointMeters = 500.0,
            BearingToDestination = 45.0,
            NextWaypointName = "Central Station",
            EstimatedTimeRemaining = duration,
            ProgressPercent = 60.0
        };

        // Assert
        state.Status.Should().Be(NavigationStatus.OnRoute);
        state.Message.Should().Be("Following route");
        state.CurrentInstruction.Should().Be("Continue straight for 500 meters");
        state.DistanceToDestinationMeters.Should().Be(2500.0);
        state.DistanceToNextWaypointMeters.Should().Be(500.0);
        state.BearingToDestination.Should().Be(45.0);
        state.NextWaypointName.Should().Be("Central Station");
        state.EstimatedTimeRemaining.Should().Be(duration);
        state.ProgressPercent.Should().Be(60.0);
    }

    [Fact]
    public void TripNavigationState_DefaultValues_AreCorrect()
    {
        // Arrange
        var state = new TripNavigationState();

        // Assert
        state.Status.Should().Be(NavigationStatus.NoRoute);
        state.Message.Should().BeNull();
        state.CurrentInstruction.Should().BeNull();
        state.DistanceToDestinationMeters.Should().Be(0);
        state.DistanceToNextWaypointMeters.Should().Be(0);
        state.BearingToDestination.Should().Be(0);
        state.NextWaypointName.Should().BeNull();
        state.EstimatedTimeRemaining.Should().Be(TimeSpan.Zero);
        state.ProgressPercent.Should().Be(0);
    }

    #endregion
}
