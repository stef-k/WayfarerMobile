namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for SseEventModels (SseLocationEvent, SseMembershipEvent, and EventArgs classes).
/// </summary>
public class SseEventModelsTests
{
    #region SseLocationEvent Tests

    [Fact]
    public void SseLocationEvent_DefaultValues_AreCorrect()
    {
        // Arrange
        var evt = new SseLocationEvent();

        // Assert
        evt.LocationId.Should().Be(0);
        evt.TimestampUtc.Should().Be(default);
        evt.UserId.Should().Be(string.Empty);
        evt.UserName.Should().Be(string.Empty);
        evt.IsLive.Should().BeFalse();
        evt.Type.Should().BeNull();
    }

    [Fact]
    public void SseLocationEvent_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var timestamp = new DateTime(2025, 12, 11, 14, 30, 0, DateTimeKind.Utc);

        var evt = new SseLocationEvent
        {
            LocationId = 54321,
            TimestampUtc = timestamp,
            UserId = "user-123",
            UserName = "john_doe",
            IsLive = true,
            Type = "check-in"
        };

        // Assert
        evt.LocationId.Should().Be(54321);
        evt.TimestampUtc.Should().Be(timestamp);
        evt.UserId.Should().Be("user-123");
        evt.UserName.Should().Be("john_doe");
        evt.IsLive.Should().BeTrue();
        evt.Type.Should().Be("check-in");
    }

    [Fact]
    public void SseLocationEvent_IsLive_CanToggle()
    {
        // Arrange
        var evt = new SseLocationEvent { IsLive = true };

        // Assert
        evt.IsLive.Should().BeTrue();

        // Act
        evt.IsLive = false;

        // Assert
        evt.IsLive.Should().BeFalse();
    }

    [Theory]
    [InlineData("check-in")]
    [InlineData("location-update")]
    [InlineData("manual")]
    public void SseLocationEvent_Type_AcceptsVariousValues(string eventType)
    {
        // Arrange
        var evt = new SseLocationEvent { Type = eventType };

        // Assert
        evt.Type.Should().Be(eventType);
    }

    #endregion

    #region SseLocationDeletedEvent Tests

    [Fact]
    public void SseLocationDeletedEvent_DefaultValues_AreCorrect()
    {
        // Arrange
        var evt = new SseLocationDeletedEvent();

        // Assert
        evt.LocationId.Should().Be(0);
        evt.UserId.Should().Be(string.Empty);
    }

    [Fact]
    public void SseLocationDeletedEvent_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var evt = new SseLocationDeletedEvent
        {
            LocationId = 12345,
            UserId = "user-abc-123"
        };

        // Assert
        evt.LocationId.Should().Be(12345);
        evt.UserId.Should().Be("user-abc-123");
    }

    #endregion

    #region SseLocationDeletedEventArgs Tests

    [Fact]
    public void SseLocationDeletedEventArgs_Constructor_StoresLocationDeleted()
    {
        // Arrange
        var locationDeleted = new SseLocationDeletedEvent
        {
            LocationId = 999,
            UserId = "test-user"
        };

        // Act
        var args = new SseLocationDeletedEventArgs(locationDeleted);

        // Assert
        args.LocationDeleted.Should().BeSameAs(locationDeleted);
        args.LocationDeleted.LocationId.Should().Be(999);
        args.LocationDeleted.UserId.Should().Be("test-user");
    }

    [Fact]
    public void SseLocationDeletedEventArgs_InheritsFromEventArgs()
    {
        // Arrange
        var locationDeleted = new SseLocationDeletedEvent();

        // Act
        var args = new SseLocationDeletedEventArgs(locationDeleted);

        // Assert
        args.Should().BeAssignableTo<EventArgs>();
    }

    #endregion

    #region SseMembershipEvent Tests

    [Fact]
    public void SseMembershipEvent_DefaultValues_AreCorrect()
    {
        // Arrange
        var evt = new SseMembershipEvent();

        // Assert
        evt.Action.Should().Be(string.Empty);
        evt.UserId.Should().BeNull();
        evt.Disabled.Should().BeNull();
    }

    [Fact]
    public void SseMembershipEvent_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var evt = new SseMembershipEvent
        {
            Action = "peer-visibility-changed",
            UserId = "user-abc",
            Disabled = true
        };

        // Assert
        evt.Action.Should().Be("peer-visibility-changed");
        evt.UserId.Should().Be("user-abc");
        evt.Disabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("visibility-changed")]      // New consolidated format
    [InlineData("member-removed")]
    [InlineData("member-left")]
    [InlineData("member-joined")]           // New event type
    [InlineData("invite-declined")]         // New event type
    [InlineData("invite-revoked")]          // New event type
    [InlineData("peer-visibility-changed")] // Legacy format (still supported)
    public void SseMembershipEvent_Action_AcceptsValidValues(string action)
    {
        // Arrange
        var evt = new SseMembershipEvent { Action = action };

        // Assert
        evt.Action.Should().Be(action);
    }

    [Fact]
    public void SseMembershipEvent_VisibilityChanged_WithDisabledTrue()
    {
        // Arrange - New consolidated format
        var evt = new SseMembershipEvent
        {
            Action = "visibility-changed",
            UserId = "user-xyz",
            Disabled = true
        };

        // Assert
        evt.Action.Should().Be("visibility-changed");
        evt.Disabled.Should().BeTrue();
    }

    [Fact]
    public void SseMembershipEvent_VisibilityChanged_WithDisabledFalse()
    {
        // Arrange - New consolidated format
        var evt = new SseMembershipEvent
        {
            Action = "visibility-changed",
            UserId = "user-xyz",
            Disabled = false
        };

        // Assert
        evt.Disabled.Should().BeFalse();
    }

    [Fact]
    public void SseMembershipEvent_MemberJoined_HasUserIdNoDisabled()
    {
        // Arrange - New event type
        var evt = new SseMembershipEvent
        {
            Action = "member-joined",
            UserId = "new-member"
        };

        // Assert
        evt.Action.Should().Be("member-joined");
        evt.UserId.Should().Be("new-member");
        evt.Disabled.Should().BeNull();
    }

    [Fact]
    public void SseMembershipEvent_PeerVisibilityChanged_LegacyFormat_WithDisabledTrue()
    {
        // Arrange - Legacy format (still supported for backward compatibility)
        var evt = new SseMembershipEvent
        {
            Action = "peer-visibility-changed",
            UserId = "user-xyz",
            Disabled = true
        };

        // Assert
        evt.Action.Should().Be("peer-visibility-changed");
        evt.Disabled.Should().BeTrue();
    }

    [Fact]
    public void SseMembershipEvent_MemberRemoved_HasUserIdNoDisabled()
    {
        // Arrange
        var evt = new SseMembershipEvent
        {
            Action = "member-removed",
            UserId = "removed-user"
        };

        // Assert
        evt.Action.Should().Be("member-removed");
        evt.UserId.Should().Be("removed-user");
        evt.Disabled.Should().BeNull();
    }

    [Fact]
    public void SseMembershipEvent_MemberLeft_HasUserIdNoDisabled()
    {
        // Arrange
        var evt = new SseMembershipEvent
        {
            Action = "member-left",
            UserId = "left-user"
        };

        // Assert
        evt.Action.Should().Be("member-left");
        evt.UserId.Should().Be("left-user");
        evt.Disabled.Should().BeNull();
    }

    #endregion

    #region SseLocationEventArgs Tests

    [Fact]
    public void SseLocationEventArgs_Constructor_StoresLocation()
    {
        // Arrange
        var location = new SseLocationEvent
        {
            LocationId = 999,
            UserId = "test-user",
            UserName = "Test User",
            IsLive = true
        };

        // Act
        var args = new SseLocationEventArgs(location);

        // Assert
        args.Location.Should().BeSameAs(location);
        args.Location.LocationId.Should().Be(999);
        args.Location.UserId.Should().Be("test-user");
        args.Location.UserName.Should().Be("Test User");
        args.Location.IsLive.Should().BeTrue();
    }

    [Fact]
    public void SseLocationEventArgs_InheritsFromEventArgs()
    {
        // Arrange
        var location = new SseLocationEvent();

        // Act
        var args = new SseLocationEventArgs(location);

        // Assert
        args.Should().BeAssignableTo<EventArgs>();
    }

    #endregion

    #region SseMembershipEventArgs Tests

    [Fact]
    public void SseMembershipEventArgs_Constructor_StoresMembership()
    {
        // Arrange
        var membership = new SseMembershipEvent
        {
            Action = "member-left",
            UserId = "departing-user"
        };

        // Act
        var args = new SseMembershipEventArgs(membership);

        // Assert
        args.Membership.Should().BeSameAs(membership);
        args.Membership.Action.Should().Be("member-left");
        args.Membership.UserId.Should().Be("departing-user");
    }

    [Fact]
    public void SseMembershipEventArgs_InheritsFromEventArgs()
    {
        // Arrange
        var membership = new SseMembershipEvent();

        // Act
        var args = new SseMembershipEventArgs(membership);

        // Assert
        args.Should().BeAssignableTo<EventArgs>();
    }

    #endregion

    #region SseReconnectEventArgs Tests

    [Fact]
    public void SseReconnectEventArgs_Constructor_StoresAttemptAndDelay()
    {
        // Arrange & Act
        var args = new SseReconnectEventArgs(3, 5000);

        // Assert
        args.Attempt.Should().Be(3);
        args.DelayMs.Should().Be(5000);
    }

    [Fact]
    public void SseReconnectEventArgs_InheritsFromEventArgs()
    {
        // Arrange & Act
        var args = new SseReconnectEventArgs(1, 1000);

        // Assert
        args.Should().BeAssignableTo<EventArgs>();
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(5, 5000)]
    [InlineData(10, 30000)]
    public void SseReconnectEventArgs_VariousAttemptAndDelayValues(int attempt, int delayMs)
    {
        // Arrange & Act
        var args = new SseReconnectEventArgs(attempt, delayMs);

        // Assert
        args.Attempt.Should().Be(attempt);
        args.DelayMs.Should().Be(delayMs);
    }

    [Fact]
    public void SseReconnectEventArgs_FirstAttempt_HasShortDelay()
    {
        // Arrange - First attempt typically has shorter delay
        var args = new SseReconnectEventArgs(1, 1000);

        // Assert
        args.Attempt.Should().Be(1);
        args.DelayMs.Should().BeLessThanOrEqualTo(5000);
    }

    [Fact]
    public void SseReconnectEventArgs_LaterAttempt_HasLongerDelay()
    {
        // Arrange - Later attempts have exponential backoff
        var args = new SseReconnectEventArgs(5, 16000);

        // Assert
        args.Attempt.Should().BeGreaterThan(1);
        args.DelayMs.Should().BeGreaterThan(1000);
    }

    #endregion
}
