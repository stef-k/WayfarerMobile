namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for GroupMember and MemberLocation classes.
/// </summary>
public class GroupMemberTests
{
    #region DisplayText Tests

    [Fact]
    public void DisplayText_DisplayNamePresent_ReturnsDisplayName()
    {
        // Arrange
        var member = new GroupMember
        {
            UserName = "john_doe",
            DisplayName = "John Doe"
        };

        // Act
        var result = member.DisplayText;

        // Assert
        result.Should().Be("John Doe");
    }

    [Fact]
    public void DisplayText_DisplayNameNull_ReturnsUserName()
    {
        // Arrange
        var member = new GroupMember
        {
            UserName = "john_doe",
            DisplayName = null
        };

        // Act
        var result = member.DisplayText;

        // Assert
        result.Should().Be("john_doe");
    }

    [Fact]
    public void DisplayText_DisplayNameEmpty_ReturnsUserName()
    {
        // Arrange
        var member = new GroupMember
        {
            UserName = "john_doe",
            DisplayName = string.Empty
        };

        // Act
        var result = member.DisplayText;

        // Assert
        result.Should().Be("john_doe");
    }

    [Fact]
    public void DisplayText_DisplayNameWhitespace_ReturnsWhitespace()
    {
        // Arrange - Note: The implementation uses IsNullOrEmpty, not IsNullOrWhiteSpace
        // Whitespace-only display names are returned as-is (edge case)
        var member = new GroupMember
        {
            UserName = "john_doe",
            DisplayName = "   "
        };

        // Act
        var result = member.DisplayText;

        // Assert - whitespace is not empty, so it's returned
        result.Should().Be("   ");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void GroupMember_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var member = new GroupMember
        {
            UserId = "user-123",
            UserName = "testuser",
            DisplayName = "Test User",
            GroupRole = "Manager",
            Status = "Active",
            ColorHex = "#FF5733",
            IsSelf = true,
            SseChannel = "channel-abc",
            OrgPeerVisibilityAccessDisabled = false,
            LastLocation = new MemberLocation
            {
                Latitude = 52.52,
                Longitude = 13.405
            }
        };

        // Assert
        member.UserId.Should().Be("user-123");
        member.UserName.Should().Be("testuser");
        member.DisplayName.Should().Be("Test User");
        member.GroupRole.Should().Be("Manager");
        member.Status.Should().Be("Active");
        member.ColorHex.Should().Be("#FF5733");
        member.IsSelf.Should().BeTrue();
        member.SseChannel.Should().Be("channel-abc");
        member.OrgPeerVisibilityAccessDisabled.Should().BeFalse();
        member.LastLocation.Should().NotBeNull();
        member.LastLocation!.Latitude.Should().Be(52.52);
        member.LastLocation.Longitude.Should().Be(13.405);
    }

    [Fact]
    public void GroupMember_DefaultValues_AreCorrect()
    {
        // Arrange
        var member = new GroupMember();

        // Assert
        member.UserId.Should().Be(string.Empty);
        member.UserName.Should().Be(string.Empty);
        member.DisplayName.Should().BeNull();
        member.GroupRole.Should().Be(string.Empty);
        member.Status.Should().Be(string.Empty);
        member.ColorHex.Should().BeNull();
        member.IsSelf.Should().BeFalse();
        member.SseChannel.Should().BeNull();
        member.OrgPeerVisibilityAccessDisabled.Should().BeFalse();
        member.LastLocation.Should().BeNull();
    }

    #endregion

    #region MemberLocation Tests

    [Fact]
    public void MemberLocation_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var location = new MemberLocation
        {
            Latitude = 48.8566,
            Longitude = 2.3522,
            Timestamp = timestamp,
            IsLive = true,
            Address = "Paris, France"
        };

        // Assert
        location.Latitude.Should().Be(48.8566);
        location.Longitude.Should().Be(2.3522);
        location.Timestamp.Should().Be(timestamp);
        location.IsLive.Should().BeTrue();
        location.Address.Should().Be("Paris, France");
    }

    [Fact]
    public void MemberLocation_DefaultValues_AreCorrect()
    {
        // Arrange
        var location = new MemberLocation();

        // Assert
        location.Latitude.Should().Be(0);
        location.Longitude.Should().Be(0);
        location.Timestamp.Should().Be(default);
        location.IsLive.Should().BeFalse();
        location.Address.Should().BeNull();
    }

    #endregion
}
