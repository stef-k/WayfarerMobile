using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for DownloadState record and related types.
/// </summary>
public class DownloadStateTests
{
    #region DownloadState Tests

    [Fact]
    public void DownloadState_DefaultValues_AreCorrect()
    {
        // Act
        var state = new DownloadState();

        // Assert
        state.TripId.Should().Be(0);
        state.TripServerId.Should().Be(Guid.Empty);
        state.TripName.Should().BeEmpty();
        state.RemainingTiles.Should().BeEmpty();
        state.CompletedTileCount.Should().Be(0);
        state.TotalTileCount.Should().Be(0);
        state.DownloadedBytes.Should().Be(0);
        state.Status.Should().Be(DownloadStatus.Paused);
        state.InterruptionReason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 100, 100)]
    [InlineData(75, 200, 37)]
    [InlineData(1, 3, 33)]
    public void ProgressPercent_CalculatesCorrectly(int completed, int total, int expectedPercent)
    {
        // Arrange
        var state = new DownloadState
        {
            CompletedTileCount = completed,
            TotalTileCount = total
        };

        // Act & Assert
        state.ProgressPercent.Should().Be(expectedPercent);
    }

    [Fact]
    public void ProgressPercent_WithZeroTotal_ReturnsZero()
    {
        // Arrange
        var state = new DownloadState
        {
            CompletedTileCount = 50,
            TotalTileCount = 0
        };

        // Act & Assert
        state.ProgressPercent.Should().Be(0);
    }

    [Theory]
    [InlineData(DownloadStatus.Paused, true)]
    [InlineData(DownloadStatus.InProgress, true)]
    [InlineData(DownloadStatus.LimitReached, true)]
    [InlineData(DownloadStatus.Cancelled, false)]
    public void CanResume_ReturnsCorrectValue(DownloadStatus status, bool expectedCanResume)
    {
        // Arrange
        var state = new DownloadState { Status = status };

        // Act & Assert
        state.CanResume.Should().Be(expectedCanResume);
    }

    [Fact]
    public void DownloadState_WithRemainingTiles_PreservesTiles()
    {
        // Arrange
        var tiles = new List<WayfarerMobile.Core.Models.TileCoordinate>
        {
            new() { Zoom = 10, X = 100, Y = 200 },
            new() { Zoom = 11, X = 201, Y = 402 }
        };

        // Act
        var state = new DownloadState
        {
            TripId = 1,
            TripName = "Test Trip",
            RemainingTiles = tiles,
            CompletedTileCount = 50,
            TotalTileCount = 100
        };

        // Assert
        state.RemainingTiles.Should().HaveCount(2);
        state.RemainingTiles[0].Zoom.Should().Be(10);
        state.RemainingTiles[1].X.Should().Be(201);
    }

    #endregion

    #region DownloadStatus Tests

    [Fact]
    public void DownloadStatus_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<DownloadStatus>().Should().HaveCount(4);
        Enum.IsDefined(DownloadStatus.Paused).Should().BeTrue();
        Enum.IsDefined(DownloadStatus.InProgress).Should().BeTrue();
        Enum.IsDefined(DownloadStatus.Cancelled).Should().BeTrue();
        Enum.IsDefined(DownloadStatus.LimitReached).Should().BeTrue();
    }

    #endregion

    #region DownloadStopReason Tests

    [Fact]
    public void DownloadStopReason_Constants_HaveExpectedValues()
    {
        // Assert
        DownloadStopReason.UserPause.Should().Be("user_pause");
        DownloadStopReason.UserCancel.Should().Be("user_cancel");
        DownloadStopReason.NetworkLost.Should().Be("network_lost");
        DownloadStopReason.StorageLow.Should().Be("storage_low");
        DownloadStopReason.CacheLimitReached.Should().Be("cache_limit_reached");
        DownloadStopReason.PeriodicSave.Should().Be("periodic_save");
        DownloadStopReason.AppInterrupted.Should().Be("app_interrupted");
    }

    [Fact]
    public void DownloadStopReason_AreDistinct()
    {
        // Arrange
        var reasons = new[]
        {
            DownloadStopReason.UserPause,
            DownloadStopReason.UserCancel,
            DownloadStopReason.NetworkLost,
            DownloadStopReason.StorageLow,
            DownloadStopReason.CacheLimitReached,
            DownloadStopReason.PeriodicSave,
            DownloadStopReason.AppInterrupted
        };

        // Assert
        reasons.Distinct().Should().HaveCount(reasons.Length);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void TwoStates_WithSameValues_AreEquivalent()
    {
        // Arrange - use explicit UpdatedAt to ensure equality
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var state1 = new DownloadState
        {
            TripId = 1,
            TripServerId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            TripName = "Test",
            CompletedTileCount = 50,
            TotalTileCount = 100,
            Status = DownloadStatus.Paused,
            UpdatedAt = timestamp
        };

        var state2 = new DownloadState
        {
            TripId = 1,
            TripServerId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            TripName = "Test",
            CompletedTileCount = 50,
            TotalTileCount = 100,
            Status = DownloadStatus.Paused,
            UpdatedAt = timestamp
        };

        // Assert - use BeEquivalentTo for structural comparison (handles collection properties)
        state1.Should().BeEquivalentTo(state2);
    }

    [Fact]
    public void TwoStates_WithDifferentStatus_AreNotEqual()
    {
        // Arrange
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var state1 = new DownloadState { TripId = 1, Status = DownloadStatus.Paused, UpdatedAt = timestamp };
        var state2 = new DownloadState { TripId = 1, Status = DownloadStatus.InProgress, UpdatedAt = timestamp };

        // Assert
        state1.Should().NotBe(state2);
    }

    [Fact]
    public void DownloadState_CanBeClonedWithWith()
    {
        // Arrange
        var original = new DownloadState
        {
            TripId = 1,
            TripName = "Original",
            CompletedTileCount = 50,
            TotalTileCount = 100,
            Status = DownloadStatus.Paused
        };

        // Act
        var updated = original with { Status = DownloadStatus.InProgress, CompletedTileCount = 75 };

        // Assert
        updated.TripId.Should().Be(1);
        updated.TripName.Should().Be("Original");
        updated.CompletedTileCount.Should().Be(75);
        updated.TotalTileCount.Should().Be(100);
        updated.Status.Should().Be(DownloadStatus.InProgress);

        // Original unchanged
        original.CompletedTileCount.Should().Be(50);
        original.Status.Should().Be(DownloadStatus.Paused);
    }

    #endregion
}
