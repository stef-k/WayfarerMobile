using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Result of a timeline import operation.
/// </summary>
/// <param name="Imported">Number of new entries imported.</param>
/// <param name="Updated">Number of existing entries updated.</param>
/// <param name="Skipped">Number of entries skipped (duplicates).</param>
/// <param name="Errors">List of error messages for malformed rows.</param>
public record ImportResult(int Imported, int Updated, int Skipped, List<string> Errors);

/// <summary>
/// Imports CSV and GeoJSON files into local timeline.
/// </summary>
public class TimelineImportService
{
    private readonly ITimelineRepository _timelineRepository;
    private readonly ILogger<TimelineImportService> _logger;
    private const int TimestampToleranceSeconds = 2;

    /// <summary>
    /// Creates a new instance of TimelineImportService.
    /// </summary>
    /// <param name="timelineRepository">Repository for timeline operations.</param>
    /// <param name="logger">Logger instance.</param>
    public TimelineImportService(
        ITimelineRepository timelineRepository,
        ILogger<TimelineImportService> logger)
    {
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Imports timeline data from a CSV file.
    /// </summary>
    /// <param name="fileStream">The file stream to read from.</param>
    /// <returns>Import result with counts and errors.</returns>
    public async Task<ImportResult> ImportFromCsvAsync(Stream fileStream)
    {
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        try
        {
            using var reader = new StreamReader(fileStream);
            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(headerLine))
            {
                errors.Add("Empty file or missing header");
                return new ImportResult(imported, updated, skipped, errors);
            }

            var headers = ParseCsvLine(headerLine);
            var columnMap = BuildColumnMap(headers);

            var lineNumber = 1;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var values = ParseCsvLine(line);
                    var entry = ParseCsvEntry(values, columnMap);

                    if (entry == null)
                    {
                        errors.Add($"Line {lineNumber}: Invalid or missing required data");
                        continue;
                    }

                    var result = await ImportEntryAsync(entry);
                    switch (result)
                    {
                        case ImportAction.Imported:
                            imported++;
                            break;
                        case ImportAction.Updated:
                            updated++;
                            break;
                        case ImportAction.Skipped:
                            skipped++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Line {lineNumber}: {ex.Message}");
                }
            }

            _logger.LogInformation(
                "CSV import complete: {Imported} imported, {Updated} updated, {Skipped} skipped, {Errors} errors",
                imported, updated, skipped, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import CSV");
            errors.Add($"Import failed: {ex.Message}");
        }

        return new ImportResult(imported, updated, skipped, errors);
    }

    /// <summary>
    /// Imports timeline data from a GeoJSON file.
    /// </summary>
    /// <param name="fileStream">The file stream to read from.</param>
    /// <returns>Import result with counts and errors.</returns>
    public async Task<ImportResult> ImportFromGeoJsonAsync(Stream fileStream)
    {
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        try
        {
            using var reader = new StreamReader(fileStream);
            var json = await reader.ReadToEndAsync();

            var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("features", out var features) ||
                features.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Invalid GeoJSON: missing features array");
                return new ImportResult(imported, updated, skipped, errors);
            }

            var featureIndex = 0;
            foreach (var feature in features.EnumerateArray())
            {
                featureIndex++;
                try
                {
                    var entry = ParseGeoJsonFeature(feature);

                    if (entry == null)
                    {
                        errors.Add($"Feature {featureIndex}: Invalid or missing required data");
                        continue;
                    }

                    var result = await ImportEntryAsync(entry);
                    switch (result)
                    {
                        case ImportAction.Imported:
                            imported++;
                            break;
                        case ImportAction.Updated:
                            updated++;
                            break;
                        case ImportAction.Skipped:
                            skipped++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Feature {featureIndex}: {ex.Message}");
                }
            }

            _logger.LogInformation(
                "GeoJSON import complete: {Imported} imported, {Updated} updated, {Skipped} skipped, {Errors} errors",
                imported, updated, skipped, errors.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse GeoJSON");
            errors.Add($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import GeoJSON");
            errors.Add($"Import failed: {ex.Message}");
        }

        return new ImportResult(imported, updated, skipped, errors);
    }

    /// <summary>
    /// Imports a single entry, handling duplicates and updates.
    /// </summary>
    private async Task<ImportAction> ImportEntryAsync(LocalTimelineEntry entry)
    {
        // Check for existing entry by timestamp
        var existing = await _timelineRepository.GetLocalTimelineEntryByTimestampAsync(
            entry.Timestamp,
            TimestampToleranceSeconds);

        if (existing != null)
        {
            // Check if it's essentially the same location (within ~10m)
            var distance = CalculateDistance(
                existing.Latitude, existing.Longitude,
                entry.Latitude, entry.Longitude);

            if (distance < 10)
            {
                // Same location - update if entry has more data
                if (HasMoreData(entry, existing))
                {
                    UpdateExisting(existing, entry);
                    await _timelineRepository.UpdateLocalTimelineEntryAsync(existing);
                    return ImportAction.Updated;
                }
                return ImportAction.Skipped;
            }
        }

        // New entry
        entry.CreatedAt = DateTime.UtcNow;
        entry.ServerId = null; // Imported entries are local-only
        await _timelineRepository.InsertLocalTimelineEntryAsync(entry);
        return ImportAction.Imported;
    }

    /// <summary>
    /// Parses a CSV line handling quoted fields.
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var inQuotes = false;
        var currentValue = new System.Text.StringBuilder();

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentValue.Append('"');
                    i++; // Skip escaped quote
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

    /// <summary>
    /// Builds a column name to index map.
    /// </summary>
    private static Dictionary<string, int> BuildColumnMap(List<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            map[headers[i].Trim()] = i;
        }
        return map;
    }

    /// <summary>
    /// Parses a CSV row into a LocalTimelineEntry.
    /// </summary>
    private static LocalTimelineEntry? ParseCsvEntry(List<string> values, Dictionary<string, int> columnMap)
    {
        // Required fields: timestamp, latitude, longitude
        if (!TryGetValue(values, columnMap, "timestamp", out var timestampStr) ||
            !TryGetValue(values, columnMap, "latitude", out var latStr) ||
            !TryGetValue(values, columnMap, "longitude", out var lonStr))
        {
            return null;
        }

        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) ||
            !double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
            !double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return null;
        }

        // Ensure timestamp is UTC
        if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();
        else if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        var entry = new LocalTimelineEntry
        {
            Timestamp = timestamp,
            Latitude = latitude,
            Longitude = longitude
        };

        // Optional fields
        if (TryGetValue(values, columnMap, "accuracy", out var accuracyStr) &&
            double.TryParse(accuracyStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var accuracy))
            entry.Accuracy = accuracy;

        if (TryGetValue(values, columnMap, "altitude", out var altitudeStr) &&
            double.TryParse(altitudeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var altitude))
            entry.Altitude = altitude;

        if (TryGetValue(values, columnMap, "speed", out var speedStr) &&
            double.TryParse(speedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            entry.Speed = speed;

        if (TryGetValue(values, columnMap, "bearing", out var bearingStr) &&
            double.TryParse(bearingStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bearing))
            entry.Bearing = bearing;

        if (TryGetValue(values, columnMap, "provider", out var provider))
            entry.Provider = provider;

        if (TryGetValue(values, columnMap, "address", out var address))
            entry.Address = address;

        if (TryGetValue(values, columnMap, "full_address", out var fullAddress))
            entry.FullAddress = fullAddress;

        if (TryGetValue(values, columnMap, "place", out var place))
            entry.Place = place;

        if (TryGetValue(values, columnMap, "region", out var region))
            entry.Region = region;

        if (TryGetValue(values, columnMap, "country", out var country))
            entry.Country = country;

        if (TryGetValue(values, columnMap, "postcode", out var postcode))
            entry.PostCode = postcode;

        if (TryGetValue(values, columnMap, "activity_type", out var activityType))
            entry.ActivityType = activityType;

        if (TryGetValue(values, columnMap, "timezone", out var timezone))
            entry.Timezone = timezone;

        if (TryGetValue(values, columnMap, "notes", out var notes))
            entry.Notes = notes;

        // Capture metadata (optional fields)
        if (TryGetValue(values, columnMap, "is_user_invoked", out var isUserInvokedStr) &&
            bool.TryParse(isUserInvokedStr, out var isUserInvoked))
            entry.IsUserInvoked = isUserInvoked;

        if (TryGetValue(values, columnMap, "app_version", out var appVersion))
            entry.AppVersion = appVersion;

        if (TryGetValue(values, columnMap, "app_build", out var appBuild))
            entry.AppBuild = appBuild;

        if (TryGetValue(values, columnMap, "device_model", out var deviceModel))
            entry.DeviceModel = deviceModel;

        if (TryGetValue(values, columnMap, "os_version", out var osVersion))
            entry.OsVersion = osVersion;

        if (TryGetValue(values, columnMap, "battery_level", out var batteryLevelStr) &&
            int.TryParse(batteryLevelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batteryLevel))
            entry.BatteryLevel = batteryLevel;

        if (TryGetValue(values, columnMap, "is_charging", out var isChargingStr) &&
            bool.TryParse(isChargingStr, out var isCharging))
            entry.IsCharging = isCharging;

        return entry;
    }

    /// <summary>
    /// Tries to get a value from CSV values by column name.
    /// </summary>
    private static bool TryGetValue(List<string> values, Dictionary<string, int> columnMap, string columnName, out string value)
    {
        value = string.Empty;
        if (!columnMap.TryGetValue(columnName, out var index) || index >= values.Count)
            return false;

        value = values[index].Trim();
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Parses a GeoJSON feature into a LocalTimelineEntry.
    /// </summary>
    private static LocalTimelineEntry? ParseGeoJsonFeature(JsonElement feature)
    {
        // Get coordinates from geometry
        if (!feature.TryGetProperty("geometry", out var geometry) ||
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

        // Get properties
        if (!feature.TryGetProperty("properties", out var properties))
            return null;

        // Required: timestamp
        if (!properties.TryGetProperty("timestamp", out var timestampProp))
            return null;

        if (!DateTime.TryParse(timestampProp.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var timestamp))
            return null;

        // Ensure timestamp is UTC
        if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();
        else if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        var entry = new LocalTimelineEntry
        {
            Timestamp = timestamp,
            Latitude = latitude,
            Longitude = longitude
        };

        // Optional properties
        if (properties.TryGetProperty("accuracy", out var accuracy) &&
            accuracy.ValueKind == JsonValueKind.Number)
            entry.Accuracy = accuracy.GetDouble();

        if (properties.TryGetProperty("altitude", out var altitude) &&
            altitude.ValueKind == JsonValueKind.Number)
            entry.Altitude = altitude.GetDouble();

        if (properties.TryGetProperty("speed", out var speed) &&
            speed.ValueKind == JsonValueKind.Number)
            entry.Speed = speed.GetDouble();

        if (properties.TryGetProperty("bearing", out var bearing) &&
            bearing.ValueKind == JsonValueKind.Number)
            entry.Bearing = bearing.GetDouble();

        if (properties.TryGetProperty("provider", out var provider) &&
            provider.ValueKind == JsonValueKind.String)
            entry.Provider = provider.GetString();

        if (properties.TryGetProperty("address", out var address) &&
            address.ValueKind == JsonValueKind.String)
            entry.Address = address.GetString();

        if (properties.TryGetProperty("fullAddress", out var fullAddress) &&
            fullAddress.ValueKind == JsonValueKind.String)
            entry.FullAddress = fullAddress.GetString();

        if (properties.TryGetProperty("place", out var place) &&
            place.ValueKind == JsonValueKind.String)
            entry.Place = place.GetString();

        if (properties.TryGetProperty("region", out var region) &&
            region.ValueKind == JsonValueKind.String)
            entry.Region = region.GetString();

        if (properties.TryGetProperty("country", out var country) &&
            country.ValueKind == JsonValueKind.String)
            entry.Country = country.GetString();

        if (properties.TryGetProperty("postCode", out var postCode) &&
            postCode.ValueKind == JsonValueKind.String)
            entry.PostCode = postCode.GetString();

        if (properties.TryGetProperty("activityType", out var activityType) &&
            activityType.ValueKind == JsonValueKind.String)
            entry.ActivityType = activityType.GetString();

        if (properties.TryGetProperty("timezone", out var timezone) &&
            timezone.ValueKind == JsonValueKind.String)
            entry.Timezone = timezone.GetString();

        if (properties.TryGetProperty("notes", out var notes) &&
            notes.ValueKind == JsonValueKind.String)
            entry.Notes = notes.GetString();

        // Capture metadata (optional fields)
        if (properties.TryGetProperty("isUserInvoked", out var isUserInvoked) &&
            (isUserInvoked.ValueKind == JsonValueKind.True || isUserInvoked.ValueKind == JsonValueKind.False))
            entry.IsUserInvoked = isUserInvoked.GetBoolean();

        if (properties.TryGetProperty("appVersion", out var appVersion) &&
            appVersion.ValueKind == JsonValueKind.String)
            entry.AppVersion = appVersion.GetString();

        if (properties.TryGetProperty("appBuild", out var appBuild) &&
            appBuild.ValueKind == JsonValueKind.String)
            entry.AppBuild = appBuild.GetString();

        if (properties.TryGetProperty("deviceModel", out var deviceModel) &&
            deviceModel.ValueKind == JsonValueKind.String)
            entry.DeviceModel = deviceModel.GetString();

        if (properties.TryGetProperty("osVersion", out var osVersion) &&
            osVersion.ValueKind == JsonValueKind.String)
            entry.OsVersion = osVersion.GetString();

        if (properties.TryGetProperty("batteryLevel", out var batteryLevel) &&
            batteryLevel.ValueKind == JsonValueKind.Number)
            entry.BatteryLevel = batteryLevel.GetInt32();

        if (properties.TryGetProperty("isCharging", out var isCharging) &&
            (isCharging.ValueKind == JsonValueKind.True || isCharging.ValueKind == JsonValueKind.False))
            entry.IsCharging = isCharging.GetBoolean();

        return entry;
    }

    /// <summary>
    /// Checks if the import entry has more data than the existing entry.
    /// </summary>
    private static bool HasMoreData(LocalTimelineEntry import, LocalTimelineEntry existing)
    {
        // Check if import has more enrichment data
        return (!string.IsNullOrEmpty(import.Address) && string.IsNullOrEmpty(existing.Address)) ||
               (!string.IsNullOrEmpty(import.Place) && string.IsNullOrEmpty(existing.Place)) ||
               (!string.IsNullOrEmpty(import.Country) && string.IsNullOrEmpty(existing.Country)) ||
               (!string.IsNullOrEmpty(import.ActivityType) && string.IsNullOrEmpty(existing.ActivityType)) ||
               (!string.IsNullOrEmpty(import.Notes) && string.IsNullOrEmpty(existing.Notes));
    }

    /// <summary>
    /// Updates an existing entry with data from the import.
    /// </summary>
    private static void UpdateExisting(LocalTimelineEntry existing, LocalTimelineEntry import)
    {
        // Only update fields that are missing in existing
        if (string.IsNullOrEmpty(existing.Address) && !string.IsNullOrEmpty(import.Address))
            existing.Address = import.Address;

        if (string.IsNullOrEmpty(existing.FullAddress) && !string.IsNullOrEmpty(import.FullAddress))
            existing.FullAddress = import.FullAddress;

        if (string.IsNullOrEmpty(existing.Place) && !string.IsNullOrEmpty(import.Place))
            existing.Place = import.Place;

        if (string.IsNullOrEmpty(existing.Region) && !string.IsNullOrEmpty(import.Region))
            existing.Region = import.Region;

        if (string.IsNullOrEmpty(existing.Country) && !string.IsNullOrEmpty(import.Country))
            existing.Country = import.Country;

        if (string.IsNullOrEmpty(existing.PostCode) && !string.IsNullOrEmpty(import.PostCode))
            existing.PostCode = import.PostCode;

        if (string.IsNullOrEmpty(existing.ActivityType) && !string.IsNullOrEmpty(import.ActivityType))
            existing.ActivityType = import.ActivityType;

        if (string.IsNullOrEmpty(existing.Timezone) && !string.IsNullOrEmpty(import.Timezone))
            existing.Timezone = import.Timezone;

        if (string.IsNullOrEmpty(existing.Notes) && !string.IsNullOrEmpty(import.Notes))
            existing.Notes = import.Notes;
    }

    /// <summary>
    /// Calculates distance between two points using Haversine formula.
    /// </summary>
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

    private enum ImportAction
    {
        Imported,
        Updated,
        Skipped
    }
}
