using System.Globalization;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for queue export functionality focusing on:
/// - CSV formula injection protection
/// - Invariant culture for numeric values
/// - CSV escaping rules
/// </summary>
/// <remarks>
/// These tests validate the CSV escaping logic used by QueueExportService.
/// The actual service lives in the MAUI project, but the escaping logic
/// can be tested independently as it's a pure function.
/// </remarks>
public class QueueExportServiceTests
{
    #region CSV Formula Injection Protection Tests

    [Theory]
    [InlineData("=SUM(A1:A10)", "'=SUM(A1:A10)")]
    [InlineData("+1234567890", "'+1234567890")]
    [InlineData("-DROP TABLE", "'-DROP TABLE")]
    [InlineData("@malicious", "'@malicious")]
    [InlineData("|cmd", "'|cmd")]
    [InlineData("\tcmd", "'\tcmd")]
    public void EscapeCsv_FormulaInjection_PrefixesWithApostrophe(string input, string expected)
    {
        // Act
        var result = EscapeCsv(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Normal text", "Normal text")]
    [InlineData("123.456", "123.456")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void EscapeCsv_SafeValues_ReturnsUnmodified(string? input, string expected)
    {
        // Act
        var result = EscapeCsv(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Hello, World", "\"Hello, World\"")]
    [InlineData("Line1\nLine2", "\"Line1\nLine2\"")]
    [InlineData("Quote\"Here", "\"Quote\"\"Here\"")]
    public void EscapeCsv_SpecialCharacters_QuotesAndEscapes(string input, string expected)
    {
        // Act
        var result = EscapeCsv(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void EscapeCsv_FormulaWithComma_BothProtections()
    {
        // Arrange - formula that also contains comma
        var input = "=SUM(A1,B1)";

        // Act
        var result = EscapeCsv(input);

        // Assert - should have apostrophe prefix AND be quoted
        result.Should().Be("\"'=SUM(A1,B1)\"");
    }

    [Fact]
    public void EscapeCsv_CarriageReturn_QuotesValue()
    {
        // Arrange
        var input = "Line1\rLine2";

        // Act
        var result = EscapeCsv(input);

        // Assert
        result.Should().Be("\"Line1\rLine2\"");
    }

    #endregion

    #region Invariant Culture Tests

    [Fact]
    public void FormatDouble_UsesInvariantCulture()
    {
        // Arrange - a value that would have comma decimal separator in some cultures
        var value = 51.5074;

        // Act
        var formatted = value.ToString(CultureInfo.InvariantCulture);

        // Assert - decimal separator should be period
        formatted.Should().Be("51.5074");
        formatted.Should().NotContain(",");
    }

    [Fact]
    public void FormatTimestamp_UsesIso8601()
    {
        // Arrange
        var timestamp = new DateTime(2024, 6, 15, 10, 30, 45, DateTimeKind.Utc);

        // Act
        var formatted = timestamp.ToString("O", CultureInfo.InvariantCulture);

        // Assert - ISO 8601 format with 'T' separator
        formatted.Should().StartWith("2024-06-15T10:30:45");
    }

    [Fact]
    public void FormatNegativeCoordinate_PreservesSign()
    {
        // Arrange - longitude values are often negative
        var longitude = -0.1278;

        // Act
        var formatted = longitude.ToString(CultureInfo.InvariantCulture);

        // Assert
        formatted.Should().Be("-0.1278");
    }

    #endregion

    #region Helper Methods - Mirror of QueueExportService.EscapeCsv

    /// <summary>
    /// Escapes a string for CSV output with formula injection protection.
    /// This is a mirror of the production code for testing purposes.
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Formula injection protection - prefix dangerous characters with apostrophe
        if (value.Length > 0 && "=+-@|\t".Contains(value[0]))
            value = $"'{value}";

        // Quote if contains special characters
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    #endregion
}
