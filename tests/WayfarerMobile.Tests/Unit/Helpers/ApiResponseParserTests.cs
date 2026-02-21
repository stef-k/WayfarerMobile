using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for <see cref="ApiResponseParser"/>.
/// Covers both server response formats (log-location and check-in) and edge cases.
/// See #216 Bug 2: check-in response format was not being parsed correctly.
/// </summary>
public class ApiResponseParserTests
{
    #region Log-Location Format (flat locationId)

    [Fact]
    public void Parse_LogLocationFormat_ExtractsLocationId()
    {
        // Arrange — log-location returns flat locationId
        var json = """{"success": true, "skipped": false, "locationId": 12345, "message": "Location logged"}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.LocationId.Should().Be(12345);
        result.Message.Should().Be("Location logged");
        result.Skipped.Should().BeFalse();
    }

    [Fact]
    public void Parse_LogLocationSkipped_SetsSkippedTrue()
    {
        // Arrange — server skipped due to threshold
        var json = """{"success": true, "skipped": true, "message": "Location skipped (too close)"}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.Skipped.Should().BeTrue();
        result.LocationId.Should().BeNull();
        result.Message.Should().Contain("skipped");
    }

    #endregion

    #region Check-In Format (nested location.id)

    [Fact]
    public void Parse_CheckInFormat_ExtractsNestedLocationId()
    {
        // Arrange — check-in returns nested { "location": { "id": N } }
        // This is the exact format from the server's LocationController.CheckIn
        var json = """{"message": "Check-in logged successfully", "location": {"id": 96988, "latitude": 40.123, "longitude": 23.456}}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.LocationId.Should().Be(96988);
        result.Message.Should().Be("Check-in logged successfully");
        result.Skipped.Should().BeFalse();
    }

    [Fact]
    public void Parse_CheckInFormat_PrefersFlatlocationIdOverNested()
    {
        // Arrange — hypothetical response with both formats
        var json = """{"message": "Success", "locationId": 100, "location": {"id": 200}}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert — flat format should win
        result.LocationId.Should().Be(100);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_NoLocationId_ReturnsNull()
    {
        // Arrange — response with neither locationId format
        var json = """{"message": "Success", "success": true}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.LocationId.Should().BeNull();
        result.Skipped.Should().BeFalse();
    }

    [Fact]
    public void Parse_NoMessage_DefaultsToSuccess()
    {
        // Arrange — response without message field
        var json = """{"locationId": 42}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.Message.Should().Be("Success");
        result.LocationId.Should().Be(42);
    }

    [Fact]
    public void Parse_LocationPropertyIsNotObject_IgnoresIt()
    {
        // Arrange — location is a string, not an object
        var json = """{"message": "OK", "location": "some-string"}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert — should not crash, locationId should be null
        result.LocationId.Should().BeNull();
    }

    [Fact]
    public void Parse_LocationObjectWithoutId_ReturnsNullLocationId()
    {
        // Arrange — location object exists but has no "id" property
        var json = """{"message": "OK", "location": {"latitude": 40.0, "longitude": 23.0}}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.LocationId.Should().BeNull();
    }

    [Fact]
    public void Parse_LocationIdIsString_ReturnsNullLocationId()
    {
        // Arrange — locationId is present but wrong type
        var json = """{"message": "OK", "locationId": "not-a-number"}""";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.LocationId.Should().BeNull();
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsDefaultResult()
    {
        // Arrange
        var notJson = "this is not JSON";

        // Act
        var result = ApiResponseParser.Parse(notJson);

        // Assert
        result.Message.Should().Be("Location logged");
        result.LocationId.Should().BeNull();
        result.Skipped.Should().BeFalse();
    }

    [Fact]
    public void Parse_EmptyObject_ReturnsDefaultMessage()
    {
        // Arrange
        var json = "{}";

        // Act
        var result = ApiResponseParser.Parse(json);

        // Assert
        result.Message.Should().Be("Success");
        result.LocationId.Should().BeNull();
        result.Skipped.Should().BeFalse();
    }

    #endregion
}
