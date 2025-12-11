namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for GroupSummary class.
/// </summary>
public class GroupSummaryTests
{
    #region RoleText Tests

    [Fact]
    public void RoleText_IsOwnerTrue_ReturnsOwner()
    {
        // Arrange
        var summary = new GroupSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            IsOwner = true,
            IsManager = false
        };

        // Act
        var result = summary.RoleText;

        // Assert
        result.Should().Be("Owner");
    }

    [Fact]
    public void RoleText_IsOwnerTrueAndIsManagerTrue_ReturnsOwner()
    {
        // Arrange - Owner takes precedence over Manager
        var summary = new GroupSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            IsOwner = true,
            IsManager = true
        };

        // Act
        var result = summary.RoleText;

        // Assert
        result.Should().Be("Owner");
    }

    [Fact]
    public void RoleText_IsManagerTrueAndIsOwnerFalse_ReturnsManager()
    {
        // Arrange
        var summary = new GroupSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            IsOwner = false,
            IsManager = true
        };

        // Act
        var result = summary.RoleText;

        // Assert
        result.Should().Be("Manager");
    }

    [Fact]
    public void RoleText_IsOwnerFalseAndIsManagerFalse_ReturnsMember()
    {
        // Arrange
        var summary = new GroupSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Group",
            IsOwner = false,
            IsManager = false
        };

        // Act
        var result = summary.RoleText;

        // Assert
        result.Should().Be("Member");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void GroupSummary_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var id = Guid.NewGuid();
        var summary = new GroupSummary
        {
            Id = id,
            Name = "Family Group",
            Description = "Our family location sharing group",
            GroupType = "Friends",
            MemberCount = 5,
            IsOwner = true,
            IsManager = false,
            IsMember = true,
            OrgPeerVisibilityEnabled = true,
            HasOrgPeerVisibilityAccess = true
        };

        // Assert
        summary.Id.Should().Be(id);
        summary.Name.Should().Be("Family Group");
        summary.Description.Should().Be("Our family location sharing group");
        summary.GroupType.Should().Be("Friends");
        summary.MemberCount.Should().Be(5);
        summary.IsOwner.Should().BeTrue();
        summary.IsManager.Should().BeFalse();
        summary.IsMember.Should().BeTrue();
        summary.OrgPeerVisibilityEnabled.Should().BeTrue();
        summary.HasOrgPeerVisibilityAccess.Should().BeTrue();
    }

    [Fact]
    public void GroupSummary_DefaultValues_AreCorrect()
    {
        // Arrange
        var summary = new GroupSummary();

        // Assert
        summary.Id.Should().Be(Guid.Empty);
        summary.Name.Should().Be(string.Empty);
        summary.Description.Should().BeNull();
        summary.GroupType.Should().BeNull();
        summary.MemberCount.Should().Be(0);
        summary.IsOwner.Should().BeFalse();
        summary.IsManager.Should().BeFalse();
        summary.IsMember.Should().BeFalse();
        summary.OrgPeerVisibilityEnabled.Should().BeFalse();
        summary.HasOrgPeerVisibilityAccess.Should().BeFalse();
    }

    [Fact]
    public void GroupSummary_RoleText_DefaultMember()
    {
        // Arrange
        var summary = new GroupSummary();

        // Act
        var result = summary.RoleText;

        // Assert
        result.Should().Be("Member");
    }

    #endregion
}
