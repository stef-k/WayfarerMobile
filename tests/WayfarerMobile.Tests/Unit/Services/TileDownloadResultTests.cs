using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TileDownloadResult record.
/// Tests factory methods and property behaviors.
/// </summary>
public class TileDownloadResultTests
{
    #region Succeeded Factory Tests

    [Fact]
    public void Succeeded_ReturnsResultWithCorrectProperties()
    {
        // Act
        var result = TileDownloadResult.Succeeded(1024);

        // Assert
        result.Success.Should().BeTrue();
        result.BytesDownloaded.Should().Be(1024);
        result.Error.Should().BeNull();
        result.IsNetworkError.Should().BeFalse();
        result.WasSkipped.Should().BeFalse();
    }

    [Fact]
    public void Succeeded_WithZeroBytes_StillMarksAsSuccess()
    {
        // Act
        var result = TileDownloadResult.Succeeded(0);

        // Assert
        result.Success.Should().BeTrue();
        result.BytesDownloaded.Should().Be(0);
    }

    [Fact]
    public void Succeeded_WithLargeBytes_HandlesCorrectly()
    {
        // Act
        var result = TileDownloadResult.Succeeded(long.MaxValue);

        // Assert
        result.Success.Should().BeTrue();
        result.BytesDownloaded.Should().Be(long.MaxValue);
    }

    #endregion

    #region Failed Factory Tests

    [Fact]
    public void Failed_WithNetworkError_HasCorrectProperties()
    {
        // Act
        var result = TileDownloadResult.Failed("Connection timeout", isNetworkError: true);

        // Assert
        result.Success.Should().BeFalse();
        result.BytesDownloaded.Should().Be(0);
        result.Error.Should().Be("Connection timeout");
        result.IsNetworkError.Should().BeTrue();
        result.WasSkipped.Should().BeFalse();
    }

    [Fact]
    public void Failed_WithoutNetworkError_HasCorrectProperties()
    {
        // Act
        var result = TileDownloadResult.Failed("Invalid PNG signature");

        // Assert
        result.Success.Should().BeFalse();
        result.BytesDownloaded.Should().Be(0);
        result.Error.Should().Be("Invalid PNG signature");
        result.IsNetworkError.Should().BeFalse();
        result.WasSkipped.Should().BeFalse();
    }

    [Fact]
    public void Failed_DefaultIsNotNetworkError()
    {
        // Act
        var result = TileDownloadResult.Failed("Some error");

        // Assert
        result.IsNetworkError.Should().BeFalse();
    }

    #endregion

    #region Skipped Factory Tests

    [Fact]
    public void Skipped_HasCorrectProperties()
    {
        // Act
        var result = TileDownloadResult.Skipped();

        // Assert
        result.Success.Should().BeTrue();
        result.WasSkipped.Should().BeTrue();
        result.BytesDownloaded.Should().Be(0);
        result.Error.Should().BeNull();
        result.IsNetworkError.Should().BeFalse();
    }

    [Fact]
    public void Skipped_IsConsideredSuccessful()
    {
        // Act
        var result = TileDownloadResult.Skipped();

        // Assert
        result.Success.Should().BeTrue("skipped tiles are considered successful");
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void TwoSucceededResults_WithSameBytes_AreEqual()
    {
        // Arrange
        var result1 = TileDownloadResult.Succeeded(1024);
        var result2 = TileDownloadResult.Succeeded(1024);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void TwoSucceededResults_WithDifferentBytes_AreNotEqual()
    {
        // Arrange
        var result1 = TileDownloadResult.Succeeded(1024);
        var result2 = TileDownloadResult.Succeeded(2048);

        // Assert
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void TwoFailedResults_WithSameError_AreEqual()
    {
        // Arrange
        var result1 = TileDownloadResult.Failed("Network error", isNetworkError: true);
        var result2 = TileDownloadResult.Failed("Network error", isNetworkError: true);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void SucceededAndFailed_AreNotEqual()
    {
        // Arrange
        var succeeded = TileDownloadResult.Succeeded(1024);
        var failed = TileDownloadResult.Failed("Error");

        // Assert
        succeeded.Should().NotBe(failed);
    }

    [Fact]
    public void SucceededAndSkipped_AreNotEqual()
    {
        // Both have Success=true but different semantics
        var succeeded = TileDownloadResult.Succeeded(0);
        var skipped = TileDownloadResult.Skipped();

        // Assert
        succeeded.Should().NotBe(skipped);
    }

    #endregion
}
