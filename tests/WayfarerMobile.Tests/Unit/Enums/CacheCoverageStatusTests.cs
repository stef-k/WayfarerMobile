using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Tests.Unit.Enums;

/// <summary>
/// Unit tests for CacheCoverageStatus enum.
/// </summary>
public class CacheCoverageStatusTests
{
    #region Enum Value Tests

    [Fact]
    public void CacheCoverageStatus_HasAllExpectedValues()
    {
        // Assert
        var values = Enum.GetValues<CacheCoverageStatus>();
        values.Should().HaveCount(7);
        values.Should().Contain(CacheCoverageStatus.Unknown);
        values.Should().Contain(CacheCoverageStatus.None);
        values.Should().Contain(CacheCoverageStatus.Poor);
        values.Should().Contain(CacheCoverageStatus.Partial);
        values.Should().Contain(CacheCoverageStatus.Good);
        values.Should().Contain(CacheCoverageStatus.Excellent);
        values.Should().Contain(CacheCoverageStatus.Error);
    }

    [Theory]
    [InlineData(CacheCoverageStatus.Unknown, 0)]
    [InlineData(CacheCoverageStatus.None, 1)]
    [InlineData(CacheCoverageStatus.Poor, 2)]
    [InlineData(CacheCoverageStatus.Partial, 3)]
    [InlineData(CacheCoverageStatus.Good, 4)]
    [InlineData(CacheCoverageStatus.Excellent, 5)]
    [InlineData(CacheCoverageStatus.Error, 6)]
    public void CacheCoverageStatus_HasExpectedOrdinalValues(CacheCoverageStatus status, int expectedValue)
    {
        // Assert
        ((int)status).Should().Be(expectedValue);
    }

    [Fact]
    public void CacheCoverageStatus_DefaultValue_IsUnknown()
    {
        // Arrange & Act
        CacheCoverageStatus defaultStatus = default;

        // Assert
        defaultStatus.Should().Be(CacheCoverageStatus.Unknown);
    }

    #endregion

    #region ToString Tests

    [Theory]
    [InlineData(CacheCoverageStatus.Unknown, "Unknown")]
    [InlineData(CacheCoverageStatus.None, "None")]
    [InlineData(CacheCoverageStatus.Poor, "Poor")]
    [InlineData(CacheCoverageStatus.Partial, "Partial")]
    [InlineData(CacheCoverageStatus.Good, "Good")]
    [InlineData(CacheCoverageStatus.Excellent, "Excellent")]
    [InlineData(CacheCoverageStatus.Error, "Error")]
    public void CacheCoverageStatus_ToString_ReturnsExpectedName(CacheCoverageStatus status, string expected)
    {
        // Act & Assert
        status.ToString().Should().Be(expected);
    }

    #endregion

    #region Parse Tests

    [Theory]
    [InlineData("Unknown", CacheCoverageStatus.Unknown)]
    [InlineData("None", CacheCoverageStatus.None)]
    [InlineData("Poor", CacheCoverageStatus.Poor)]
    [InlineData("Partial", CacheCoverageStatus.Partial)]
    [InlineData("Good", CacheCoverageStatus.Good)]
    [InlineData("Excellent", CacheCoverageStatus.Excellent)]
    [InlineData("Error", CacheCoverageStatus.Error)]
    public void CacheCoverageStatus_Parse_FromValidString_ReturnsExpectedValue(string input, CacheCoverageStatus expected)
    {
        // Act
        var result = Enum.Parse<CacheCoverageStatus>(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("NONE")]
    [InlineData("poor")]
    public void CacheCoverageStatus_Parse_CaseSensitive_ThrowsException(string input)
    {
        // Act & Assert
        var act = () => Enum.Parse<CacheCoverageStatus>(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("unknown", CacheCoverageStatus.Unknown)]
    [InlineData("NONE", CacheCoverageStatus.None)]
    [InlineData("poor", CacheCoverageStatus.Poor)]
    [InlineData("EXCELLENT", CacheCoverageStatus.Excellent)]
    public void CacheCoverageStatus_Parse_IgnoreCase_ReturnsExpectedValue(string input, CacheCoverageStatus expected)
    {
        // Act
        var result = Enum.Parse<CacheCoverageStatus>(input, ignoreCase: true);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Coverage Level Ordering Tests

    [Fact]
    public void CacheCoverageStatus_CoverageLevels_AreInAscendingOrder()
    {
        // The enum should be ordered from Unknown/None (worst) to Excellent (best)
        // with Error being a separate error state

        // Assert - coverage levels increase in ordinal value
        ((int)CacheCoverageStatus.None).Should().BeGreaterThan((int)CacheCoverageStatus.Unknown);
        ((int)CacheCoverageStatus.Poor).Should().BeGreaterThan((int)CacheCoverageStatus.None);
        ((int)CacheCoverageStatus.Partial).Should().BeGreaterThan((int)CacheCoverageStatus.Poor);
        ((int)CacheCoverageStatus.Good).Should().BeGreaterThan((int)CacheCoverageStatus.Partial);
        ((int)CacheCoverageStatus.Excellent).Should().BeGreaterThan((int)CacheCoverageStatus.Good);
    }

    [Theory]
    [InlineData(0.0, CacheCoverageStatus.None)]
    [InlineData(0.01, CacheCoverageStatus.Poor)]
    [InlineData(0.39, CacheCoverageStatus.Poor)]
    [InlineData(0.40, CacheCoverageStatus.Partial)]
    [InlineData(0.69, CacheCoverageStatus.Partial)]
    [InlineData(0.70, CacheCoverageStatus.Good)]
    [InlineData(0.89, CacheCoverageStatus.Good)]
    [InlineData(0.90, CacheCoverageStatus.Excellent)]
    [InlineData(1.0, CacheCoverageStatus.Excellent)]
    public void CacheCoverageStatus_CoveragePercentToStatus_MapsCorrectly(double percent, CacheCoverageStatus expected)
    {
        // This tests the documented coverage thresholds:
        // - None: 0%
        // - Poor: > 0% and < 40%
        // - Partial: >= 40% and < 70%
        // - Good: >= 70% and < 90%
        // - Excellent: >= 90%

        // Act
        var status = DetermineStatus(percent);

        // Assert
        status.Should().Be(expected);
    }

    /// <summary>
    /// Helper method that implements the documented status thresholds.
    /// </summary>
    private static CacheCoverageStatus DetermineStatus(double coveragePercent)
    {
        return coveragePercent switch
        {
            0.0 => CacheCoverageStatus.None,
            >= 0.90 => CacheCoverageStatus.Excellent,
            >= 0.70 => CacheCoverageStatus.Good,
            >= 0.40 => CacheCoverageStatus.Partial,
            _ => CacheCoverageStatus.Poor
        };
    }

    #endregion
}
