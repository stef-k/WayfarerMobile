using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for timeline export/import parsing logic.
/// Tests the CSV and GeoJSON format handling without MAUI dependencies.
/// </summary>
/// <remarks>
/// <para>
/// These tests verify the core parsing algorithms used by TimelineExportService
/// and TimelineImportService without requiring the full service dependencies.
/// </para>
/// </remarks>
public class TimelineExportImportTests
{
    #region Test Data

    private record TestTimelineEntry(
        int Id,
        int? ServerId,
        DateTime Timestamp,
        double Latitude,
        double Longitude,
        double? Accuracy,
        double? Altitude,
        double? Speed,
        double? Bearing,
        string? Provider,
        string? Address,
        string? FullAddress,
        string? Place,
        string? Region,
        string? Country,
        string? PostCode,
        string? ActivityType,
        string? Timezone,
        string? Notes,
        // Capture metadata fields
        bool? IsUserInvoked = null,
        string? AppVersion = null,
        string? AppBuild = null,
        string? DeviceModel = null,
        string? OsVersion = null,
        int? BatteryLevel = null,
        bool? IsCharging = null);

    private static TestTimelineEntry CreateSampleEntry(int id = 1) => new(
        Id: id,
        ServerId: 100 + id,
        Timestamp: new DateTime(2024, 12, 27, 10, 30, 0, DateTimeKind.Utc),
        Latitude: 51.5074,
        Longitude: -0.1278,
        Accuracy: 10.5,
        Altitude: 25.0,
        Speed: 1.5,
        Bearing: 180.0,
        Provider: "gps",
        Address: "10 Downing Street",
        FullAddress: "10 Downing Street, Westminster, London SW1A 2AA",
        Place: "London",
        Region: "Greater London",
        Country: "United Kingdom",
        PostCode: "SW1A 2AA",
        ActivityType: "Walking",
        Timezone: "Europe/London",
        Notes: "Test note",
        // Capture metadata
        IsUserInvoked: true,
        AppVersion: "1.2.3",
        AppBuild: "45",
        DeviceModel: "Pixel 7 Pro",
        OsVersion: "Android 14",
        BatteryLevel: 85,
        IsCharging: false);

    #endregion

    #region CSV Export Tests

    [Fact]
    public void ToCsvRow_SimpleEntry_FormatsCorrectly()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("1,101");
        csvRow.Should().Contain("51.5074");
        csvRow.Should().Contain("-0.1278");
        csvRow.Should().Contain("10 Downing Street");
        csvRow.Should().Contain("Walking");
    }

    [Fact]
    public void ToCsvRow_NullOptionalFields_FormatsAsEmpty()
    {
        // Arrange
        var entry = new TestTimelineEntry(
            Id: 1,
            ServerId: null,
            Timestamp: DateTime.UtcNow,
            Latitude: 51.5074,
            Longitude: -0.1278,
            Accuracy: null,
            Altitude: null,
            Speed: null,
            Bearing: null,
            Provider: null,
            Address: null,
            FullAddress: null,
            Place: null,
            Region: null,
            Country: null,
            PostCode: null,
            ActivityType: null,
            Timezone: null,
            Notes: null);

        // Act
        var csvRow = ToCsvRow(entry);
        var parts = ParseCsvLine(csvRow);

        // Assert - Column order: Id,ServerId,TimestampUtc,LocalTimestamp,Latitude,Longitude,Accuracy,...
        parts[1].Should().BeEmpty("null ServerId should be empty");
        parts[6].Should().BeEmpty("null Accuracy should be empty (index 6 after LocalTimestamp)");
        parts[11].Should().BeEmpty("null Address should be empty (index 11 after LocalTimestamp)");
    }

    [Fact]
    public void ToCsvRow_WithCommaInField_EscapesWithQuotes()
    {
        // Arrange
        var entry = CreateSampleEntry() with { Address = "123 Main St, Suite 100" };

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("\"123 Main St, Suite 100\"");
    }

    [Fact]
    public void ToCsvRow_WithQuoteInField_EscapesQuotes()
    {
        // Arrange
        var entry = CreateSampleEntry() with { Notes = "He said \"hello\"" };

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("\"He said \"\"hello\"\"\"");
    }

    [Fact]
    public void ToCsvRow_WithNewlineInField_EscapesWithQuotes()
    {
        // Arrange
        var entry = CreateSampleEntry() with { Notes = "Line 1\nLine 2" };

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("\"Line 1\nLine 2\"");
    }

    [Fact]
    public void ToCsvRow_TimestampFormat_UsesIso8601()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("2024-12-27T10:30:00");
    }

    [Fact]
    public void ToCsvRow_NumericFields_UseInvariantCulture()
    {
        // Arrange
        var entry = CreateSampleEntry() with { Latitude = 51.1234567, Accuracy = 10.5 };

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert
        csvRow.Should().Contain("51.1234567");
        csvRow.Should().Contain("10.5");
        csvRow.Should().NotContain("51,1234567", "should not use comma as decimal separator");
    }

    [Fact]
    public void ToCsvRow_WithMetadata_IncludesAllMetadataFields()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var csvRow = ToCsvRow(entry);

        // Assert - verify metadata fields are present
        csvRow.Should().Contain("True"); // IsUserInvoked
        csvRow.Should().Contain("1.2.3"); // AppVersion
        csvRow.Should().Contain("45"); // AppBuild
        csvRow.Should().Contain("Pixel 7 Pro"); // DeviceModel
        csvRow.Should().Contain("Android 14"); // OsVersion
        csvRow.Should().Contain("85"); // BatteryLevel
        csvRow.Should().Contain("False"); // IsCharging
    }

    #endregion

    #region CSV Parsing Tests

    [Fact]
    public void ParseCsvLine_SimpleRow_ParsesCorrectly()
    {
        // Arrange
        var line = "value1,value2,value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "value1", "value2", "value3" });
    }

    [Fact]
    public void ParseCsvLine_QuotedField_ParsesWithoutQuotes()
    {
        // Arrange
        var line = "value1,\"quoted, with comma\",value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "value1", "quoted, with comma", "value3" });
    }

    [Fact]
    public void ParseCsvLine_EscapedQuotes_ParsesCorrectly()
    {
        // Arrange
        var line = "value1,\"He said \"\"hello\"\"\",value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "value1", "He said \"hello\"", "value3" });
    }

    [Fact]
    public void ParseCsvLine_EmptyFields_PreservesEmpty()
    {
        // Arrange
        var line = "value1,,value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "value1", "", "value3" });
    }

    [Fact]
    public void ParseCsvLine_NewlineInQuotedField_ParsesCorrectly()
    {
        // Arrange
        var line = "value1,\"line1\nline2\",value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "value1", "line1\nline2", "value3" });
    }

    [Fact]
    public void ParseCsvLine_OnlyQuotedFields_ParsesAll()
    {
        // Arrange
        var line = "\"a\",\"b\",\"c\"";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void ParseCsvLine_TrailingEmptyField_ParsesCorrectly()
    {
        // Arrange
        var line = "value1,value2,";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().HaveCount(3);
        values[2].Should().BeEmpty();
    }

    [Fact]
    public void ParseCsvLine_LeadingEmptyField_ParsesCorrectly()
    {
        // Arrange
        var line = ",value2,value3";

        // Act
        var values = ParseCsvLine(line);

        // Assert
        values.Should().HaveCount(3);
        values[0].Should().BeEmpty();
    }

    #endregion

    #region CSV Column Mapping Tests

    [Fact]
    public void BuildColumnMap_StandardHeaders_MapsCorrectly()
    {
        // Arrange
        var headers = new List<string>
        {
            "id", "server_id", "timestamp", "latitude", "longitude"
        };

        // Act
        var map = BuildColumnMap(headers);

        // Assert
        map["id"].Should().Be(0);
        map["server_id"].Should().Be(1);
        map["timestamp"].Should().Be(2);
        map["latitude"].Should().Be(3);
        map["longitude"].Should().Be(4);
    }

    [Fact]
    public void BuildColumnMap_CaseInsensitive_MapsCorrectly()
    {
        // Arrange
        var headers = new List<string> { "TIMESTAMP", "Latitude", "longitude" };

        // Act
        var map = BuildColumnMap(headers);

        // Assert
        map.ContainsKey("timestamp").Should().BeTrue();
        map.ContainsKey("TIMESTAMP").Should().BeTrue();
        map.ContainsKey("Timestamp").Should().BeTrue();
    }

    [Fact]
    public void BuildColumnMap_WithSpaces_TrimsHeaders()
    {
        // Arrange
        var headers = new List<string> { " timestamp ", " latitude ", " longitude " };

        // Act
        var map = BuildColumnMap(headers);

        // Assert
        map.ContainsKey("timestamp").Should().BeTrue();
        map.ContainsKey("latitude").Should().BeTrue();
    }

    #endregion

    #region GeoJSON Export Tests

    [Fact]
    public void ToGeoJsonFeature_SimpleEntry_FormatsCorrectly()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var json = ToGeoJsonFeature(entry);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        root.GetProperty("type").GetString().Should().Be("Feature");
        root.GetProperty("geometry").GetProperty("type").GetString().Should().Be("Point");

        var coords = root.GetProperty("geometry").GetProperty("coordinates");
        coords[0].GetDouble().Should().Be(-0.1278);
        coords[1].GetDouble().Should().Be(51.5074);

        // PascalCase property names matching Wayfarer backend
        var props = root.GetProperty("properties");
        props.GetProperty("Id").GetInt32().Should().Be(1);
        props.GetProperty("ServerId").GetInt32().Should().Be(101);
        props.GetProperty("Place").GetString().Should().Be("London");
        props.GetProperty("TimestampUtc").GetString().Should().Contain("2024-12-27");
        props.GetProperty("LocalTimestamp").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToGeoJsonFeature_NullFields_OmittedFromJson()
    {
        // Arrange
        var entry = new TestTimelineEntry(
            Id: 1,
            ServerId: null,
            Timestamp: DateTime.UtcNow,
            Latitude: 51.5074,
            Longitude: -0.1278,
            Accuracy: null,
            Altitude: null,
            Speed: null,
            Bearing: null,
            Provider: null,
            Address: null,
            FullAddress: null,
            Place: null,
            Region: null,
            Country: null,
            PostCode: null,
            ActivityType: null,
            Timezone: null,
            Notes: null);

        // Act
        var json = ToGeoJsonFeature(entry);
        var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("properties");

        // Assert - null fields should be omitted (PascalCase property names)
        props.TryGetProperty("ServerId", out _).Should().BeFalse("null should be omitted");
        props.TryGetProperty("Accuracy", out _).Should().BeFalse("null should be omitted");
        props.TryGetProperty("Place", out _).Should().BeFalse("null should be omitted");
    }

    [Fact]
    public void ToGeoJsonFeature_CoordinatesOrder_LongitudeFirst()
    {
        // Arrange - GeoJSON spec: [longitude, latitude]
        var entry = CreateSampleEntry() with { Latitude = 51.5074, Longitude = -0.1278 };

        // Act
        var json = ToGeoJsonFeature(entry);
        var doc = JsonDocument.Parse(json);
        var coords = doc.RootElement.GetProperty("geometry").GetProperty("coordinates");

        // Assert
        coords[0].GetDouble().Should().Be(-0.1278, "first coordinate should be longitude");
        coords[1].GetDouble().Should().Be(51.5074, "second coordinate should be latitude");
    }

    [Fact]
    public void ToGeoJsonFeature_WithMetadata_IncludesAllMetadataFields()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var json = ToGeoJsonFeature(entry);
        var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("properties");

        // Assert - verify metadata fields are present (PascalCase)
        props.GetProperty("IsUserInvoked").GetBoolean().Should().BeTrue();
        props.GetProperty("AppVersion").GetString().Should().Be("1.2.3");
        props.GetProperty("AppBuild").GetString().Should().Be("45");
        props.GetProperty("DeviceModel").GetString().Should().Be("Pixel 7 Pro");
        props.GetProperty("OsVersion").GetString().Should().Be("Android 14");
        props.GetProperty("BatteryLevel").GetInt32().Should().Be(85);
        props.GetProperty("IsCharging").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ToGeoJsonFeature_ExportFormat_MatchesWayfarerBackend()
    {
        // Arrange
        var entry = CreateSampleEntry();

        // Act
        var json = ToGeoJsonFeature(entry);
        var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.GetProperty("properties");

        // Assert - verify backend-expected property names exist
        props.TryGetProperty("TimestampUtc", out _).Should().BeTrue("backend expects TimestampUtc");
        props.TryGetProperty("LocalTimestamp", out _).Should().BeTrue("backend expects LocalTimestamp");
        props.TryGetProperty("Activity", out _).Should().BeTrue("backend expects Activity (not activityType)");
        props.TryGetProperty("TimeZoneId", out _).Should().BeTrue("backend expects TimeZoneId (not timezone)");

        // Verify old names are NOT present
        props.TryGetProperty("timestamp", out _).Should().BeFalse("old camelCase should not be present");
        props.TryGetProperty("activityType", out _).Should().BeFalse("old name should not be present");
        props.TryGetProperty("timezone", out _).Should().BeFalse("old name should not be present");
    }

    #endregion

    #region GeoJSON Parsing Tests

    [Fact]
    public void ParseGeoJsonFeature_CamelCaseFormat_ParsesCorrectly()
    {
        // Arrange - Old camelCase format (for backwards compatibility)
        var json = @"{
            ""type"": ""Feature"",
            ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [-0.1278, 51.5074]
            },
            ""properties"": {
                ""timestamp"": ""2024-12-27T10:30:00Z"",
                ""place"": ""London"",
                ""country"": ""United Kingdom""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeature(json);

        // Assert
        entry.Should().NotBeNull();
        entry!.Longitude.Should().Be(-0.1278);
        entry.Latitude.Should().Be(51.5074);
        entry.Place.Should().Be("London");
        entry.Country.Should().Be("United Kingdom");
    }

    [Fact]
    public void ParseGeoJsonFeature_PascalCaseFormat_ParsesCorrectly()
    {
        // Arrange - New PascalCase format (Wayfarer backend compatible)
        var json = @"{
            ""type"": ""Feature"",
            ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [-0.1278, 51.5074]
            },
            ""properties"": {
                ""TimestampUtc"": ""2024-12-27T10:30:00Z"",
                ""LocalTimestamp"": ""2024-12-27T10:30:00Z"",
                ""Place"": ""London"",
                ""Country"": ""United Kingdom"",
                ""Activity"": ""Walking"",
                ""TimeZoneId"": ""Europe/London""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeatureWithAlias(json);

        // Assert
        entry.Should().NotBeNull();
        entry!.Longitude.Should().Be(-0.1278);
        entry.Latitude.Should().Be(51.5074);
        entry.Place.Should().Be("London");
        entry.Country.Should().Be("United Kingdom");
        entry.ActivityType.Should().Be("Walking");
        entry.Timezone.Should().Be("Europe/London");
    }

    [Fact]
    public void ParseGeoJsonFeature_MissingGeometry_ReturnsNull()
    {
        // Arrange
        var json = @"{
            ""type"": ""Feature"",
            ""properties"": {
                ""timestamp"": ""2024-12-27T10:30:00Z""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeature(json);

        // Assert
        entry.Should().BeNull();
    }

    [Fact]
    public void ParseGeoJsonFeature_MissingCoordinates_ReturnsNull()
    {
        // Arrange
        var json = @"{
            ""type"": ""Feature"",
            ""geometry"": {
                ""type"": ""Point""
            },
            ""properties"": {
                ""timestamp"": ""2024-12-27T10:30:00Z""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeature(json);

        // Assert
        entry.Should().BeNull();
    }

    [Fact]
    public void ParseGeoJsonFeature_MissingTimestamp_ReturnsNull()
    {
        // Arrange
        var json = @"{
            ""type"": ""Feature"",
            ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [-0.1278, 51.5074]
            },
            ""properties"": {
                ""place"": ""London""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeature(json);

        // Assert
        entry.Should().BeNull("timestamp is a required field");
    }

    [Fact]
    public void ParseGeoJsonFeature_AllOptionalFields_ParsesComplete()
    {
        // Arrange
        var json = @"{
            ""type"": ""Feature"",
            ""geometry"": {
                ""type"": ""Point"",
                ""coordinates"": [-0.1278, 51.5074]
            },
            ""properties"": {
                ""id"": 1,
                ""serverId"": 101,
                ""timestamp"": ""2024-12-27T10:30:00Z"",
                ""accuracy"": 10.5,
                ""altitude"": 25.0,
                ""speed"": 1.5,
                ""bearing"": 180.0,
                ""provider"": ""gps"",
                ""address"": ""10 Downing Street"",
                ""fullAddress"": ""10 Downing Street, London"",
                ""place"": ""London"",
                ""region"": ""Greater London"",
                ""country"": ""United Kingdom"",
                ""postCode"": ""SW1A 2AA"",
                ""activityType"": ""Walking"",
                ""timezone"": ""Europe/London"",
                ""notes"": ""Test note""
            }
        }";

        // Act
        var entry = ParseGeoJsonFeature(json);

        // Assert
        entry.Should().NotBeNull();
        entry!.Accuracy.Should().Be(10.5);
        entry.Altitude.Should().Be(25.0);
        entry.Speed.Should().Be(1.5);
        entry.Bearing.Should().Be(180.0);
        entry.Provider.Should().Be("gps");
        entry.Address.Should().Be("10 Downing Street");
        entry.Place.Should().Be("London");
        entry.ActivityType.Should().Be("Walking");
        entry.Timezone.Should().Be("Europe/London");
        entry.Notes.Should().Be("Test note");
    }

    #endregion

    #region Timestamp Handling Tests

    [Theory]
    [InlineData("2024-12-27T10:30:00Z", DateTimeKind.Utc)]
    [InlineData("2024-12-27T10:30:00+00:00", DateTimeKind.Utc)]
    [InlineData("2024-12-27T10:30:00", DateTimeKind.Utc)] // Unspecified becomes UTC
    public void ParseTimestamp_VariousFormats_NormalizesToUtc(string timestampStr, DateTimeKind expectedKind)
    {
        // Act
        var result = ParseAndNormalizeTimestamp(timestampStr);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(expectedKind);
    }

    [Fact]
    public void ParseTimestamp_InvalidFormat_ReturnsNull()
    {
        // Arrange
        var invalidTimestamp = "not-a-date";

        // Act
        var result = ParseAndNormalizeTimestamp(invalidTimestamp);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseTimestamp_LocalTime_ConvertedToUtc()
    {
        // Arrange - Local timestamp with offset
        var localTimestamp = "2024-12-27T10:30:00+02:00";

        // Act
        var result = ParseAndNormalizeTimestamp(localTimestamp);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.Value.Hour.Should().Be(8, "10:30 +02:00 should become 08:30 UTC");
    }

    #endregion

    #region Distance Calculation Tests

    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        // Arrange
        double lat = 51.5074, lon = -0.1278;

        // Act
        var distance = CalculateDistance(lat, lon, lat, lon);

        // Assert
        distance.Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void CalculateDistance_KnownPoints_ReturnsCorrectDistance()
    {
        // Arrange - London to Paris approximately 343 km
        double londonLat = 51.5074, londonLon = -0.1278;
        double parisLat = 48.8566, parisLon = 2.3522;

        // Act
        var distance = CalculateDistance(londonLat, londonLon, parisLat, parisLon);

        // Assert - Should be approximately 343 km
        distance.Should().BeInRange(340000, 350000);
    }

    [Fact]
    public void CalculateDistance_SmallDistance_AccurateWithin1Meter()
    {
        // Arrange - 100m north
        double lat1 = 51.5074, lon1 = -0.1278;
        var (lat2, lon2) = GeoMath.CalculateDestination(lat1, lon1, 0, 100);

        // Act
        var distance = CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert
        distance.Should().BeApproximately(100, 1.0);
    }

    #endregion

    #region Import Deduplication Tests

    [Fact]
    public void HasMoreData_ImportHasMoreEnrichment_ReturnsTrue()
    {
        // Arrange
        var import = CreateSampleEntry() with { Address = "10 Downing Street" };
        var existing = CreateSampleEntry() with { Address = null };

        // Act
        var result = HasMoreData(import, existing);

        // Assert
        result.Should().BeTrue("import has address, existing does not");
    }

    [Fact]
    public void HasMoreData_ExistingHasAllData_ReturnsFalse()
    {
        // Arrange
        var import = CreateSampleEntry();
        var existing = CreateSampleEntry();

        // Act
        var result = HasMoreData(import, existing);

        // Assert
        result.Should().BeFalse("existing already has all data");
    }

    [Fact]
    public void HasMoreData_MultipleNewFields_ReturnsTrue()
    {
        // Arrange
        var import = CreateSampleEntry() with { Place = "London", Country = "UK", ActivityType = "Walking" };
        var existing = CreateSampleEntry() with { Place = null, Country = null, ActivityType = null };

        // Act
        var result = HasMoreData(import, existing);

        // Assert
        result.Should().BeTrue("import has multiple new fields");
    }

    #endregion

    #region Helper Method Implementations (Mirroring Service Logic)

    /// <summary>
    /// Converts to CSV row using PascalCase column order matching Wayfarer backend.
    /// Column order: Id,ServerId,TimestampUtc,LocalTimestamp,Latitude,Longitude,...
    /// </summary>
    private static string ToCsvRow(TestTimelineEntry entry)
    {
        // Compute local timestamp from timezone if available
        var localTimestamp = ComputeLocalTimestamp(entry.Timestamp, entry.Timezone);

        var values = new[]
        {
            entry.Id.ToString(CultureInfo.InvariantCulture),
            entry.ServerId?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            localTimestamp.ToString("o", CultureInfo.InvariantCulture),
            entry.Latitude.ToString(CultureInfo.InvariantCulture),
            entry.Longitude.ToString(CultureInfo.InvariantCulture),
            entry.Accuracy?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.Altitude?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.Speed?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.Bearing?.ToString(CultureInfo.InvariantCulture) ?? "",
            EscapeCsv(entry.Provider),
            EscapeCsv(entry.Address),
            EscapeCsv(entry.FullAddress),
            EscapeCsv(entry.Place),
            EscapeCsv(entry.Region),
            EscapeCsv(entry.Country),
            EscapeCsv(entry.PostCode),
            EscapeCsv(entry.ActivityType),
            EscapeCsv(entry.Timezone),
            EscapeCsv(entry.Notes),
            // Capture metadata fields
            entry.IsUserInvoked?.ToString(CultureInfo.InvariantCulture) ?? "",
            EscapeCsv(entry.AppVersion),
            EscapeCsv(entry.AppBuild),
            EscapeCsv(entry.DeviceModel),
            EscapeCsv(entry.OsVersion),
            entry.BatteryLevel?.ToString(CultureInfo.InvariantCulture) ?? "",
            entry.IsCharging?.ToString(CultureInfo.InvariantCulture) ?? ""
        };

        return string.Join(",", values);
    }

    private static DateTime ComputeLocalTimestamp(DateTime utcTimestamp, string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return utcTimestamp;

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return utcTimestamp;
        }
        catch (InvalidTimeZoneException)
        {
            return utcTimestamp;
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var inQuotes = false;
        var currentValue = new StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentValue.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(currentValue.ToString());
                currentValue.Clear();
            }
            else
            {
                currentValue.Append(c);
            }
        }

        values.Add(currentValue.ToString());
        return values;
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            map[headers[i].Trim()] = i;
        }
        return map;
    }

    private static string ToGeoJsonFeature(TestTimelineEntry entry)
    {
        var feature = new Dictionary<string, object>
        {
            ["type"] = "Feature",
            ["geometry"] = new Dictionary<string, object>
            {
                ["type"] = "Point",
                ["coordinates"] = new[] { entry.Longitude, entry.Latitude }
            },
            ["properties"] = BuildProperties(entry)
        };

        return JsonSerializer.Serialize(feature, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Builds GeoJSON properties using PascalCase matching Wayfarer backend.
    /// </summary>
    private static Dictionary<string, object?> BuildProperties(TestTimelineEntry entry)
    {
        var localTimestamp = ComputeLocalTimestamp(entry.Timestamp, entry.Timezone);

        var props = new Dictionary<string, object?>
        {
            ["Id"] = entry.Id,
            ["ServerId"] = entry.ServerId,
            ["TimestampUtc"] = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            ["LocalTimestamp"] = localTimestamp.ToString("o", CultureInfo.InvariantCulture),
            ["Accuracy"] = entry.Accuracy,
            ["Altitude"] = entry.Altitude,
            ["Speed"] = entry.Speed,
            ["Bearing"] = entry.Bearing,
            ["Provider"] = entry.Provider,
            ["Address"] = entry.Address,
            ["FullAddress"] = entry.FullAddress,
            ["Place"] = entry.Place,
            ["Region"] = entry.Region,
            ["Country"] = entry.Country,
            ["PostCode"] = entry.PostCode,
            ["Activity"] = entry.ActivityType,
            ["TimeZoneId"] = entry.Timezone,
            ["Notes"] = entry.Notes,
            // Capture metadata fields
            ["IsUserInvoked"] = entry.IsUserInvoked,
            ["AppVersion"] = entry.AppVersion,
            ["AppBuild"] = entry.AppBuild,
            ["DeviceModel"] = entry.DeviceModel,
            ["OsVersion"] = entry.OsVersion,
            ["BatteryLevel"] = entry.BatteryLevel,
            ["IsCharging"] = entry.IsCharging
        };

        // Remove null values
        var keysToRemove = props.Where(p => p.Value == null).Select(p => p.Key).ToList();
        foreach (var key in keysToRemove)
            props.Remove(key);

        return props;
    }

    private record ParsedEntry(
        double Latitude,
        double Longitude,
        DateTime Timestamp,
        double? Accuracy = null,
        double? Altitude = null,
        double? Speed = null,
        double? Bearing = null,
        string? Provider = null,
        string? Address = null,
        string? Place = null,
        string? Country = null,
        string? ActivityType = null,
        string? Timezone = null,
        string? Notes = null,
        // Capture metadata fields
        bool? IsUserInvoked = null,
        string? AppVersion = null,
        string? AppBuild = null,
        string? DeviceModel = null,
        string? OsVersion = null,
        int? BatteryLevel = null,
        bool? IsCharging = null);

    /// <summary>
    /// Parses GeoJSON using camelCase property names (old format).
    /// </summary>
    private static ParsedEntry? ParseGeoJsonFeature(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("geometry", out var geometry) ||
                !geometry.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var coordArray = coordinates.EnumerateArray().ToList();
            if (coordArray.Count < 2)
                return null;

            var longitude = coordArray[0].GetDouble();
            var latitude = coordArray[1].GetDouble();

            if (!root.TryGetProperty("properties", out var properties))
                return null;

            if (!properties.TryGetProperty("timestamp", out var timestampProp))
                return null;

            if (!DateTime.TryParse(timestampProp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
                return null;

            if (timestamp.Kind == DateTimeKind.Local)
                timestamp = timestamp.ToUniversalTime();
            else if (timestamp.Kind == DateTimeKind.Unspecified)
                timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

            return new ParsedEntry(
                Latitude: latitude,
                Longitude: longitude,
                Timestamp: timestamp,
                Accuracy: GetNullableDouble(properties, "accuracy"),
                Altitude: GetNullableDouble(properties, "altitude"),
                Speed: GetNullableDouble(properties, "speed"),
                Bearing: GetNullableDouble(properties, "bearing"),
                Provider: GetNullableString(properties, "provider"),
                Address: GetNullableString(properties, "address"),
                Place: GetNullableString(properties, "place"),
                Country: GetNullableString(properties, "country"),
                ActivityType: GetNullableString(properties, "activityType"),
                Timezone: GetNullableString(properties, "timezone"),
                Notes: GetNullableString(properties, "notes"),
                // Capture metadata fields
                IsUserInvoked: GetNullableBool(properties, "isUserInvoked"),
                AppVersion: GetNullableString(properties, "appVersion"),
                AppBuild: GetNullableString(properties, "appBuild"),
                DeviceModel: GetNullableString(properties, "deviceModel"),
                OsVersion: GetNullableString(properties, "osVersion"),
                BatteryLevel: GetNullableInt(properties, "batteryLevel"),
                IsCharging: GetNullableBool(properties, "isCharging"));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses GeoJSON supporting both camelCase (old) and PascalCase (new) property names.
    /// Mirrors the alias support in TimelineImportService.
    /// </summary>
    private static ParsedEntry? ParseGeoJsonFeatureWithAlias(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("geometry", out var geometry) ||
                !geometry.TryGetProperty("coordinates", out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var coordArray = coordinates.EnumerateArray().ToList();
            if (coordArray.Count < 2)
                return null;

            var longitude = coordArray[0].GetDouble();
            var latitude = coordArray[1].GetDouble();

            if (!root.TryGetProperty("properties", out var properties))
                return null;

            // Support both "timestamp" (camelCase) and "TimestampUtc" (PascalCase)
            var timestampStr = GetStringWithAlias(properties, "timestamp", "TimestampUtc");
            if (timestampStr == null)
                return null;

            if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var timestamp))
                return null;

            if (timestamp.Kind == DateTimeKind.Local)
                timestamp = timestamp.ToUniversalTime();
            else if (timestamp.Kind == DateTimeKind.Unspecified)
                timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

            return new ParsedEntry(
                Latitude: latitude,
                Longitude: longitude,
                Timestamp: timestamp,
                Accuracy: GetDoubleWithAlias(properties, "accuracy", "Accuracy"),
                Altitude: GetDoubleWithAlias(properties, "altitude", "Altitude"),
                Speed: GetDoubleWithAlias(properties, "speed", "Speed"),
                Bearing: GetDoubleWithAlias(properties, "bearing", "Bearing"),
                Provider: GetStringWithAlias(properties, "provider", "Provider"),
                Address: GetStringWithAlias(properties, "address", "Address"),
                Place: GetStringWithAlias(properties, "place", "Place"),
                Country: GetStringWithAlias(properties, "country", "Country"),
                ActivityType: GetStringWithAlias(properties, "activityType", "Activity"),
                Timezone: GetStringWithAlias(properties, "timezone", "TimeZoneId"),
                Notes: GetStringWithAlias(properties, "notes", "Notes"),
                // Capture metadata fields
                IsUserInvoked: GetBoolWithAlias(properties, "isUserInvoked", "IsUserInvoked"),
                AppVersion: GetStringWithAlias(properties, "appVersion", "AppVersion"),
                AppBuild: GetStringWithAlias(properties, "appBuild", "AppBuild"),
                DeviceModel: GetStringWithAlias(properties, "deviceModel", "DeviceModel"),
                OsVersion: GetStringWithAlias(properties, "osVersion", "OsVersion"),
                BatteryLevel: GetIntWithAlias(properties, "batteryLevel", "BatteryLevel"),
                IsCharging: GetBoolWithAlias(properties, "isCharging", "IsCharging"));
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static double? GetDoubleWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return null;
    }

    private static int? GetIntWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }

    private static bool? GetBoolWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            return prop.GetBoolean();
        if (element.TryGetProperty(aliasName, out prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            return prop.GetBoolean();
        return null;
    }

    private static double? GetNullableDouble(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return null;
    }

    private static string? GetNullableString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            return prop.GetBoolean();
        return null;
    }

    private static int? GetNullableInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }

    private static DateTime? ParseAndNormalizeTimestamp(string timestampStr)
    {
        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var timestamp))
            return null;

        if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();
        else if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        return timestamp;
    }

    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusMeters = 6371000;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static bool HasMoreData(TestTimelineEntry import, TestTimelineEntry existing)
    {
        return (!string.IsNullOrEmpty(import.Address) && string.IsNullOrEmpty(existing.Address)) ||
               (!string.IsNullOrEmpty(import.Place) && string.IsNullOrEmpty(existing.Place)) ||
               (!string.IsNullOrEmpty(import.Country) && string.IsNullOrEmpty(existing.Country)) ||
               (!string.IsNullOrEmpty(import.ActivityType) && string.IsNullOrEmpty(existing.ActivityType)) ||
               (!string.IsNullOrEmpty(import.Notes) && string.IsNullOrEmpty(existing.Notes)) ||
               // Capture metadata fields
               (import.IsUserInvoked.HasValue && !existing.IsUserInvoked.HasValue) ||
               (!string.IsNullOrEmpty(import.AppVersion) && string.IsNullOrEmpty(existing.AppVersion)) ||
               (!string.IsNullOrEmpty(import.AppBuild) && string.IsNullOrEmpty(existing.AppBuild)) ||
               (!string.IsNullOrEmpty(import.DeviceModel) && string.IsNullOrEmpty(existing.DeviceModel)) ||
               (!string.IsNullOrEmpty(import.OsVersion) && string.IsNullOrEmpty(existing.OsVersion)) ||
               (import.BatteryLevel.HasValue && !existing.BatteryLevel.HasValue) ||
               (import.IsCharging.HasValue && !existing.IsCharging.HasValue);
    }

    #endregion
}
