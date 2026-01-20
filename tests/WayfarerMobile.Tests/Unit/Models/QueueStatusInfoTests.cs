using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for QueueStatusInfo model focusing on:
/// - Health status calculation
/// - IsOverLimit computation
/// - Remaining headroom calculation
/// - Coverage span calculation
/// </summary>
public class QueueStatusInfoTests
{
    #region HealthStatus Tests

    [Theory]
    [InlineData(0, 1000, "Healthy")]      // 0%
    [InlineData(500, 1000, "Healthy")]    // 50%
    [InlineData(790, 1000, "Healthy")]    // 79%
    [InlineData(800, 1000, "Warning")]    // 80%
    [InlineData(940, 1000, "Warning")]    // 94%
    [InlineData(950, 1000, "Critical")]   // 95%
    [InlineData(1000, 1000, "Critical")]  // 100%
    [InlineData(1001, 1000, "Over Limit")] // 100.1%
    [InlineData(2000, 1000, "Over Limit")] // 200%
    public void HealthStatus_ReturnsCorrectStatus(int totalCount, int queueLimit, string expectedStatus)
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = totalCount,
            QueueLimit = queueLimit,
            UsagePercent = queueLimit > 0 ? (double)totalCount / queueLimit * 100 : 0
        };

        // Act & Assert
        status.HealthStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public void HealthStatus_ZeroLimit_ReturnsUnknown()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = 100,
            QueueLimit = 0,
            UsagePercent = 0
        };

        // Act & Assert
        status.HealthStatus.Should().Be("Unknown");
    }

    #endregion

    #region IsOverLimit Tests

    [Theory]
    [InlineData(999, 1000, false)]   // Under limit
    [InlineData(1000, 1000, false)]  // At limit (not over)
    [InlineData(1001, 1000, true)]   // Over limit
    [InlineData(5000, 1000, true)]   // Way over limit
    public void IsOverLimit_CalculatesCorrectly(int totalCount, int queueLimit, bool expectedOverLimit)
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = totalCount,
            QueueLimit = queueLimit
        };

        // Act & Assert
        status.IsOverLimit.Should().Be(expectedOverLimit);
    }

    #endregion

    #region GetRemainingHeadroom Tests

    [Fact]
    public void GetRemainingHeadroom_CalculatesCorrectly()
    {
        // Arrange - 500 slots remaining, 5 min threshold = 2500 min = ~41.67 hours
        var status = new QueueStatusInfo
        {
            TotalCount = 500,
            QueueLimit = 1000
        };

        // Act
        var headroom = status.GetRemainingHeadroom(timeThresholdMinutes: 5);

        // Assert
        headroom.TotalMinutes.Should().Be(2500); // 500 slots * 5 min
    }

    [Fact]
    public void GetRemainingHeadroom_AtLimit_ReturnsZero()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = 1000,
            QueueLimit = 1000
        };

        // Act
        var headroom = status.GetRemainingHeadroom(timeThresholdMinutes: 5);

        // Assert
        headroom.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetRemainingHeadroom_OverLimit_ReturnsZero()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = 1500,
            QueueLimit = 1000
        };

        // Act
        var headroom = status.GetRemainingHeadroom(timeThresholdMinutes: 5);

        // Assert
        headroom.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetRemainingHeadroom_InvalidThreshold_ReturnsZero(int threshold)
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = 500,
            QueueLimit = 1000
        };

        // Act
        var headroom = status.GetRemainingHeadroom(threshold);

        // Assert
        headroom.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetRemainingHeadroom_ZeroLimit_ReturnsZero()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            TotalCount = 0,
            QueueLimit = 0
        };

        // Act
        var headroom = status.GetRemainingHeadroom(timeThresholdMinutes: 5);

        // Assert
        headroom.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region CurrentCoverageSpan Tests

    [Fact]
    public void CurrentCoverageSpan_WithBothTimestamps_CalculatesSpan()
    {
        // Arrange
        var oldest = DateTime.UtcNow.AddHours(-24);
        var newest = DateTime.UtcNow;
        var status = new QueueStatusInfo
        {
            OldestPendingTimestamp = oldest,
            NewestPendingTimestamp = newest
        };

        // Act & Assert
        status.CurrentCoverageSpan.Should().NotBeNull();
        status.CurrentCoverageSpan!.Value.TotalHours.Should().BeApproximately(24, 0.01);
    }

    [Fact]
    public void CurrentCoverageSpan_MissingOldest_ReturnsNull()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            OldestPendingTimestamp = null,
            NewestPendingTimestamp = DateTime.UtcNow
        };

        // Act & Assert
        status.CurrentCoverageSpan.Should().BeNull();
    }

    [Fact]
    public void CurrentCoverageSpan_MissingNewest_ReturnsNull()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            OldestPendingTimestamp = DateTime.UtcNow,
            NewestPendingTimestamp = null
        };

        // Act & Assert
        status.CurrentCoverageSpan.Should().BeNull();
    }

    [Fact]
    public void CurrentCoverageSpan_BothNull_ReturnsNull()
    {
        // Arrange
        var status = new QueueStatusInfo
        {
            OldestPendingTimestamp = null,
            NewestPendingTimestamp = null
        };

        // Act & Assert
        status.CurrentCoverageSpan.Should().BeNull();
    }

    #endregion
}
