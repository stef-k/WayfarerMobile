using System.Text;
using System.Text.Json;
using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for UtcDateTimeConverter JSON serialization.
/// Ensures timestamps are correctly serialized with UTC "Z" suffix to prevent
/// double timezone conversion on the server.
/// </summary>
public class UtcDateTimeConverterTests
{
    private readonly JsonSerializerOptions _options;

    public UtcDateTimeConverterTests()
    {
        _options = new JsonSerializerOptions
        {
            Converters = { new UtcDateTimeConverter() }
        };
    }

    #region Write Tests

    [Fact]
    public void Write_UtcDateTime_SerializesWithZSuffix()
    {
        // Arrange
        var utcDateTime = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Utc);

        // Act
        var json = JsonSerializer.Serialize(utcDateTime, _options);

        // Assert
        json.Should().Be("\"2025-12-14T12:53:00.000Z\"");
    }

    [Fact]
    public void Write_LocalDateTime_ConvertsToUtcWithZSuffix()
    {
        // Arrange - Create a local time
        var localDateTime = new DateTime(2025, 12, 14, 14, 53, 0, DateTimeKind.Local);
        var expectedUtc = localDateTime.ToUniversalTime();

        // Act
        var json = JsonSerializer.Serialize(localDateTime, _options);

        // Assert - Should contain Z suffix and be converted to UTC
        json.Should().EndWith("Z\"");

        // Deserialize and verify it matches the expected UTC time
        var deserialized = JsonSerializer.Deserialize<DateTime>(json, _options);
        deserialized.Kind.Should().Be(DateTimeKind.Utc);
        deserialized.Should().BeCloseTo(expectedUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Write_UnspecifiedDateTime_TreatedAsUtcWithoutConversion()
    {
        // Arrange - Unspecified kind (common when reading from SQLite)
        // CRITICAL: SQLite-net-pcl doesn't preserve DateTimeKind, so timestamps
        // retrieved from the database become Unspecified even if they were UTC.
        var unspecifiedDateTime = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Unspecified);

        // Act
        var json = JsonSerializer.Serialize(unspecifiedDateTime, _options);

        // Assert - Should have Z suffix AND preserve the exact time value
        // The converter treats Unspecified as UTC (not local time), so 12:53 stays 12:53Z
        // NOT converted as if it were local time (which would produce wrong offset)
        json.Should().Be("\"2025-12-14T12:53:00.000Z\"");
    }

    [Fact]
    public void Write_DateTimeWithMilliseconds_PreservesMilliseconds()
    {
        // Arrange
        var utcDateTime = new DateTime(2025, 12, 14, 12, 53, 45, 123, DateTimeKind.Utc);

        // Act
        var json = JsonSerializer.Serialize(utcDateTime, _options);

        // Assert
        json.Should().Be("\"2025-12-14T12:53:45.123Z\"");
    }

    [Fact]
    public void Write_MinValue_SerializesCorrectly()
    {
        // Arrange
        var minDateTime = DateTime.MinValue;

        // Act
        var json = JsonSerializer.Serialize(minDateTime, _options);

        // Assert - Should not throw and should have valid format
        json.Should().EndWith("Z\"");
        json.Should().StartWith("\"0001-01-01");
    }

    #endregion

    #region Read Tests

    [Fact]
    public void Read_UtcStringWithZ_ReturnsUtcDateTime()
    {
        // Arrange
        var json = "\"2025-12-14T12:53:00Z\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Year.Should().Be(2025);
        result.Month.Should().Be(12);
        result.Day.Should().Be(14);
        result.Hour.Should().Be(12);
        result.Minute.Should().Be(53);
    }

    [Fact]
    public void Read_UtcStringWithOffset_ReturnsUtcDateTime()
    {
        // Arrange - String with +00:00 offset (equivalent to Z)
        var json = "\"2025-12-14T12:53:00+00:00\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Hour.Should().Be(12);
    }

    [Fact]
    public void Read_StringWithPositiveOffset_ConvertsToUtc()
    {
        // Arrange - Athens time (UTC+2)
        var json = "\"2025-12-14T14:53:00+02:00\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert - Should be converted to UTC (14:53 Athens = 12:53 UTC)
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Hour.Should().Be(12);
    }

    [Fact]
    public void Read_StringWithNegativeOffset_ConvertsToUtc()
    {
        // Arrange - New York time (UTC-5)
        var json = "\"2025-12-14T07:53:00-05:00\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert - Should be converted to UTC (07:53 NY = 12:53 UTC)
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Hour.Should().Be(12);
    }

    [Fact]
    public void Read_StringWithoutTimezone_TreatsAsUtc()
    {
        // Arrange - No timezone info (the bug scenario we're fixing)
        var json = "\"2025-12-14T12:53:00\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert - Should be treated as UTC, NOT local
        result.Kind.Should().Be(DateTimeKind.Utc);
        result.Hour.Should().Be(12);
    }

    [Fact]
    public void Read_StringWithMilliseconds_PreservesMilliseconds()
    {
        // Arrange
        var json = "\"2025-12-14T12:53:45.123Z\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert
        result.Millisecond.Should().Be(123);
    }

    [Fact]
    public void Read_EmptyString_ReturnsMinValue()
    {
        // Arrange
        var json = "\"\"";

        // Act
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert
        result.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void Read_NullString_ReturnsMinValue()
    {
        // Arrange - Deserializing null for nullable DateTime should work
        var json = "null";

        // Act - Nullable DateTime can handle null
        var result = JsonSerializer.Deserialize<DateTime?>(json, _options);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public void Roundtrip_UtcDateTime_PreservesValue()
    {
        // Arrange
        var original = new DateTime(2025, 12, 14, 12, 53, 45, 123, DateTimeKind.Utc);

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var result = JsonSerializer.Deserialize<DateTime>(json, _options);

        // Assert
        result.Should().Be(original);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Roundtrip_ObjectWithDateTime_PreservesValue()
    {
        // Arrange - Simulate the LocationPayload scenario
        var original = new TestPayload
        {
            Latitude = 37.9838,
            Longitude = 23.7275,
            Timestamp = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Utc)
        };

        // Act
        var json = JsonSerializer.Serialize(original, _options);
        var result = JsonSerializer.Deserialize<TestPayload>(json, _options);

        // Assert
        result.Should().NotBeNull();
        result!.Timestamp.Should().Be(original.Timestamp);
        result.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
        json.Should().Contain("\"2025-12-14T12:53:00.000Z\"");
    }

    #endregion

    #region Bug Reproduction Tests

    [Fact]
    public void BugScenario_SqliteLosesDateTimeKind_ConverterPreservesValue()
    {
        // This test documents the SQLite DateTimeKind bug that was fixed:
        // 1. Android location.Time is converted to UTC DateTime
        // 2. Stored in SQLite queue
        // 3. When retrieved, SQLite-net-pcl loses DateTimeKind (becomes Unspecified)
        // 4. Old converter called .ToUniversalTime() which treats Unspecified as LOCAL
        // 5. This caused incorrect timezone conversion (e.g., 12:53 -> 10:53 in UTC+2)

        // Arrange - Simulate timestamp that went through SQLite
        // Original: 12:53 UTC, but after SQLite retrieval it's Unspecified
        var afterSqliteRetrieval = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Unspecified);

        // Act - Serialize with our converter
        var json = JsonSerializer.Serialize(afterSqliteRetrieval, _options);

        // Assert - Value should be preserved as 12:53Z (NOT converted as local time)
        // If the converter incorrectly called .ToUniversalTime(), this would fail
        // because 12:53 Unspecified would become 10:53 UTC (assuming UTC+2 timezone)
        json.Should().Be("\"2025-12-14T12:53:00.000Z\"");

        // Verify the deserialized value is correct UTC
        var deserialized = JsonSerializer.Deserialize<DateTime>(json, _options);
        deserialized.Kind.Should().Be(DateTimeKind.Utc);
        deserialized.Hour.Should().Be(12); // NOT shifted by timezone
    }

    [Fact]
    public void BugScenario_MobileClientSendsUtc_ServerReceivesUtc()
    {
        // This test documents the bug that was fixed:
        // Mobile app converts Android location.Time to UTC but when serialized
        // without explicit Z suffix, server may double-convert thinking it's local time.

        // Arrange - Mobile creates UTC timestamp from Android location
        // Using a known UTC timestamp that the mobile app would generate
        var mobileUtcTimestamp = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Utc);

        // Act - Serialize with our converter (adds Z suffix)
        var json = JsonSerializer.Serialize(mobileUtcTimestamp, _options);

        // Assert - JSON must have Z suffix so server knows it's UTC
        json.Should().EndWith("Z\"");
        json.Should().Contain("2025-12-14T12:53:00");

        // Verify server-side deserialization would get UTC
        var serverReceived = JsonSerializer.Deserialize<DateTime>(json, _options);
        serverReceived.Kind.Should().Be(DateTimeKind.Utc);
        serverReceived.Year.Should().Be(2025);
        serverReceived.Month.Should().Be(12);
        serverReceived.Day.Should().Be(14);
        serverReceived.Hour.Should().Be(12);
        serverReceived.Minute.Should().Be(53);
    }

    [Fact]
    public void BugScenario_WithoutConverter_ServerMisinterprets()
    {
        // This shows what happens WITHOUT the converter (the bug)
        // When DateTime is serialized without Z suffix, it may be misinterpreted

        // Arrange
        var defaultOptions = new JsonSerializerOptions(); // No converter
        var utcTimestamp = new DateTime(2025, 12, 14, 12, 53, 0, DateTimeKind.Utc);

        // Act - Default serialization
        var defaultJson = JsonSerializer.Serialize(utcTimestamp, defaultOptions);

        // With our converter
        var fixedJson = JsonSerializer.Serialize(utcTimestamp, _options);

        // Assert - Our converter ensures Z suffix is present
        fixedJson.Should().EndWith("Z\"");

        // Note: Default behavior may or may not include Z depending on .NET version
        // The important thing is our converter ALWAYS includes it
    }

    #endregion

    /// <summary>
    /// Test payload class simulating the ApiClient's LocationPayload.
    /// </summary>
    private class TestPayload
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
