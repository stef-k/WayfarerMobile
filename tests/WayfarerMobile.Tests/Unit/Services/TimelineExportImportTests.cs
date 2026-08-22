using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public class TimelineExportImportTests
{
    [Fact]
    public async Task GeoJsonProductionRoundTrip_RestoresSupportedFieldsWithStandardStructure()
    {
        var source = CreateEntry("GeoJSON notes");
        var destination = new InMemoryTimelineRepository();
        var exporter = CreateExporter(new InMemoryTimelineRepository(source));
        var importer = CreateImporter(destination);

        var exported = await exporter.ExportToGeoJsonAsync();
        using var document = JsonDocument.Parse(exported);
        var root = document.RootElement;
        root.GetProperty("type").GetString().Should().Be("FeatureCollection");
        var feature = root.GetProperty("features")[0];
        feature.GetProperty("type").GetString().Should().Be("Feature");
        var geometry = feature.GetProperty("geometry");
        geometry.GetProperty("type").GetString().Should().Be("Point");
        geometry.GetProperty("coordinates").GetArrayLength().Should().Be(2);
        var properties = feature.GetProperty("properties");
        properties.GetProperty("TimestampUtc").GetString().Should().NotBeNull();
        properties.GetProperty("TimeZoneId").GetString().Should().Be(source.TimeZoneId);

        var result = await importer.ImportFromGeoJsonAsync(ToStream(exported));

        AssertSuccessfulImport(result);
        destination.Entries.Should().ContainSingle();
        AssertRoundTripEntry(destination.Entries[0], source);
    }

    [Fact]
    public async Task CsvProductionRoundTrip_PreservesQuotedMultilineNotesAndSupportedFields()
    {
        const string notes = "First line, with a comma and \"quoted text\"\r\nSecond line\nThird line";
        var source = CreateEntry(notes);
        var destination = new InMemoryTimelineRepository();
        var exporter = CreateExporter(new InMemoryTimelineRepository(source));
        var importer = CreateImporter(destination);

        var exported = await exporter.ExportToCsvAsync();
        exported.Should().Contain("\"First line, with a comma and \"\"quoted text\"\"\r\nSecond line\nThird line\"");
        var result = await importer.ImportFromCsvAsync(ToStream(exported));

        AssertSuccessfulImport(result);
        destination.Entries.Should().ContainSingle();
        AssertRoundTripEntry(destination.Entries[0], source);
        destination.Entries[0].Notes.Should().Be(notes);
    }

    [Fact]
    public async Task GeoJsonImport_AcceptsEarlierMobileStructureAndLowercaseWins()
    {
        const string mixedJson = """
            { "features": [{
                "geometry": { "coordinates": [23.70, 37.90] },
                "Geometry": { "Coordinates": [1.0, 2.0] },
                "properties": { "TimestampUtc": "2026-08-21T09:00:00Z", "Notes": "lowercase wins" },
                "Properties": { "TimestampUtc": "2020-01-01T00:00:00Z", "Notes": "wrong" }
              }], "Features": [] }
            """;
        const string earlierMobileJson = """
            { "Type": "FeatureCollection", "Features": [{
                "Type": "Feature",
                "Geometry": { "Type": "Point", "Coordinates": [23.72, 37.98] },
                "Properties": { "TimestampUtc": "2026-08-20T08:00:00Z", "TimeZoneId": "Europe/Athens" }
              }] }
            """;
        var repository = new InMemoryTimelineRepository();
        var importer = CreateImporter(repository);

        var mixedResult = await importer.ImportFromGeoJsonAsync(ToStream(mixedJson));
        var earlierResult = await importer.ImportFromGeoJsonAsync(ToStream(earlierMobileJson));

        AssertSuccessfulImport(mixedResult);
        AssertSuccessfulImport(earlierResult);
        repository.Entries.Should().HaveCount(2);
        repository.Entries[0].Should().Match<LocalTimelineEntry>(entry =>
            entry.Longitude == 23.70 && entry.Latitude == 37.90 && entry.Notes == "lowercase wins");
        repository.Entries[1].Should().Match<LocalTimelineEntry>(entry =>
            entry.Longitude == 23.72 && entry.Latitude == 37.98 && entry.TimeZoneId == "Europe/Athens");
    }

    [Fact]
    public async Task CsvImport_AcceptsBackendHeaderAliases()
    {
        const string csv = "timestamp,latitude,longitude,full_address,activity_type,timezone,is_user_invoked,app_version,app_build,device_model,os_version,battery_level,is_charging\n" +
                           "2026-08-19T07:00:00Z,37.98,23.72,Backend address,Walking,Europe/Athens,true,2.0,200,Backend device,Backend OS,70,false";
        var repository = new InMemoryTimelineRepository();

        var result = await CreateImporter(repository).ImportFromCsvAsync(ToStream(csv));

        AssertSuccessfulImport(result);
        repository.Entries[0].Should().Match<LocalTimelineEntry>(entry =>
            entry.FullAddress == "Backend address" && entry.ActivityType == "Walking" &&
            entry.TimeZoneId == "Europe/Athens" && entry.IsUserInvoked == true &&
            entry.AppVersion == "2.0" && entry.AppBuild == "200" &&
            entry.DeviceModel == "Backend device" && entry.OsVersion == "Backend OS" &&
            entry.BatteryLevel == 70 && entry.IsCharging == false);
    }

    private static TimelineExportService CreateExporter(ITimelineRepository repository) =>
        new(repository, NullLogger<TimelineExportService>.Instance);

    private static TimelineImportService CreateImporter(ITimelineRepository repository) =>
        new(repository, NullLogger<TimelineImportService>.Instance);

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));

    private static void AssertSuccessfulImport(ImportResult result)
    {
        result.Imported.Should().Be(1);
        result.Updated.Should().Be(0);
        result.Skipped.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    private static LocalTimelineEntry CreateEntry(string notes) => new()
    {
        Id = 73, ServerId = 9073,
        Timestamp = new DateTime(2026, 8, 21, 10, 11, 12, DateTimeKind.Utc),
        Latitude = 37.9838, Longitude = 23.7275, Accuracy = 4.25, Altitude = 91.5,
        Speed = 1.75, Bearing = 182.25, Provider = "gps", Address = "1 Example Street",
        FullAddress = "1 Example Street, Athens, Greece", Place = "Athens", Region = "Attica",
        Country = "Greece", PostCode = "105 57", ActivityType = "Walking",
        TimeZoneId = "Europe/Athens", Source = "mobile-checkin", Notes = notes,
        IsUserInvoked = true, AppVersion = "2.4.3", AppBuild = "243",
        DeviceModel = "Transfer Phone", OsVersion = "Mobile OS 18",
        BatteryLevel = 64, IsCharging = false,
        CreatedAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        LastEnrichedAt = new DateTime(2026, 8, 21, 10, 12, 0, DateTimeKind.Utc),
        QueuedLocationId = 456
    };

    private static void AssertRoundTripEntry(LocalTimelineEntry actual, LocalTimelineEntry expected)
    {
        actual.Should().BeEquivalentTo(expected, options => options
            .Excluding(entry => entry.Id).Excluding(entry => entry.ServerId)
            .Excluding(entry => entry.CreatedAt).Excluding(entry => entry.LastEnrichedAt)
            .Excluding(entry => entry.QueuedLocationId)
            .Excluding(entry => entry.IsSynced).Excluding(entry => entry.IsEnriched));
        actual.Id.Should().BePositive().And.NotBe(expected.Id);
        actual.ServerId.Should().BeNull();
        actual.CreatedAt.Should().NotBe(expected.CreatedAt);
        actual.LastEnrichedAt.Should().BeNull();
        actual.QueuedLocationId.Should().BeNull();
    }

    private sealed class InMemoryTimelineRepository(params LocalTimelineEntry[] entries) : ITimelineRepository
    {
        public List<LocalTimelineEntry> Entries { get; } = [.. entries];

        public Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry)
        {
            entry.Id = Entries.Count == 0 ? 1 : Entries.Max(item => item.Id) + 1;
            Entries.Add(entry);
            return Task.FromResult(entry.Id);
        }

        public Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry) => Task.CompletedTask;
        public Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(DateTime timestamp, int toleranceSeconds = 2) =>
            Task.FromResult(Entries.FirstOrDefault(entry => Math.Abs((entry.Timestamp - timestamp).TotalSeconds) <= toleranceSeconds));
        public Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync() => Task.FromResult(Entries.ToList());
        public Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(DateTime fromDate, DateTime toDate) =>
            Task.FromResult(Entries.Where(entry => entry.Timestamp >= fromDate && entry.Timestamp <= toDate).ToList());

        public Task DeleteLocalTimelineEntryAsync(int id) => throw new NotSupportedException();
        public Task<int> DeleteLocalTimelineEntryByTimestampAsync(DateTime timestamp, double latitude, double longitude, int toleranceSeconds = 2) => throw new NotSupportedException();
        public Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id) => throw new NotSupportedException();
        public Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId) => throw new NotSupportedException();
        public Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync() => throw new NotSupportedException();
        public Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date) => throw new NotSupportedException();
        public Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> items) => throw new NotSupportedException();
        public Task<int> ClearAllLocalTimelineEntriesAsync() => throw new NotSupportedException();
        public Task<bool> UpdateLocalTimelineServerIdAsync(DateTime timestamp, double latitude, double longitude, int serverId, int toleranceSeconds = 2) => throw new NotSupportedException();
        public Task<int> GetLocalTimelineEntryCountAsync() => throw new NotSupportedException();
        public Task<List<LocalTimelineEntry>> GetEntriesMissingServerIdAsync(DateTime? sinceTimestamp = null) => throw new NotSupportedException();
        public Task<bool> UpdateServerIdByQueuedLocationIdAsync(int queuedLocationId, int serverId) => throw new NotSupportedException();
        public Task<int> DeleteByQueuedLocationIdAsync(int queuedLocationId) => throw new NotSupportedException();
        public Task<LocalTimelineEntry?> GetByQueuedLocationIdAsync(int queuedLocationId) => throw new NotSupportedException();
    }
}
