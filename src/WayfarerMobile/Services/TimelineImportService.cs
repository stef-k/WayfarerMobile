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
    /// Supports both PascalCase (current export format) and snake_case (legacy format) column names.
    /// </summary>
    private static LocalTimelineEntry? ParseCsvEntry(List<string> values, Dictionary<string, int> columnMap)
    {
        // Required fields: timestamp, latitude, longitude
        // Support both formats: PascalCase (current export) and snake_case (legacy)
        if (!TryGetValueWithAlias(values, columnMap, "TimestampUtc", "timestamp", out var timestampStr) ||
            !TryGetValueWithAlias(values, columnMap, "Latitude", "latitude", out var latStr) ||
            !TryGetValueWithAlias(values, columnMap, "Longitude", "longitude", out var lonStr))
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

        // Optional fields - support both PascalCase (current) and snake_case (legacy)
        if (TryGetValueWithAlias(values, columnMap, "Accuracy", "accuracy", out var accuracyStr) &&
            double.TryParse(accuracyStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var accuracy))
            entry.Accuracy = accuracy;

        if (TryGetValueWithAlias(values, columnMap, "Altitude", "altitude", out var altitudeStr) &&
            double.TryParse(altitudeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var altitude))
            entry.Altitude = altitude;

        if (TryGetValueWithAlias(values, columnMap, "Speed", "speed", out var speedStr) &&
            double.TryParse(speedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            entry.Speed = speed;

        if (TryGetValueWithAlias(values, columnMap, "Bearing", "bearing", out var bearingStr) &&
            double.TryParse(bearingStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var bearing))
            entry.Bearing = bearing;

        if (TryGetValueWithAlias(values, columnMap, "Provider", "provider", out var provider))
            entry.Provider = provider;

        if (TryGetValueWithAlias(values, columnMap, "Address", "address", out var address))
            entry.Address = address;

        if (TryGetValueWithAlias(values, columnMap, "FullAddress", "full_address", out var fullAddress))
            entry.FullAddress = fullAddress;

        if (TryGetValueWithAlias(values, columnMap, "Place", "place", out var place))
            entry.Place = place;

        if (TryGetValueWithAlias(values, columnMap, "Region", "region", out var region))
            entry.Region = region;

        if (TryGetValueWithAlias(values, columnMap, "Country", "country", out var country))
            entry.Country = country;

        if (TryGetValueWithAlias(values, columnMap, "PostCode", "postcode", out var postcode))
            entry.PostCode = postcode;

        // Activity: PascalCase "Activity" or snake_case "activity_type"
        if (TryGetValueWithAlias(values, columnMap, "Activity", "activity_type", out var activityType))
            entry.ActivityType = activityType;

        // Timezone: PascalCase "TimeZoneId" or snake_case "timezone"
        if (TryGetValueWithAlias(values, columnMap, "TimeZoneId", "timezone", out var timezone))
            entry.TimeZoneId = timezone;

        if (TryGetValueWithAlias(values, columnMap, "Notes", "notes", out var notes))
            entry.Notes = notes;

        // Source: PascalCase "Source" or snake_case "source"
        if (TryGetValueWithAlias(values, columnMap, "Source", "source", out var source))
            entry.Source = source;

        // Capture metadata (optional fields) - support both formats
        if (TryGetValueWithAlias(values, columnMap, "IsUserInvoked", "is_user_invoked", out var isUserInvokedStr) &&
            bool.TryParse(isUserInvokedStr, out var isUserInvoked))
            entry.IsUserInvoked = isUserInvoked;

        if (TryGetValueWithAlias(values, columnMap, "AppVersion", "app_version", out var appVersion))
            entry.AppVersion = appVersion;

        if (TryGetValueWithAlias(values, columnMap, "AppBuild", "app_build", out var appBuild))
            entry.AppBuild = appBuild;

        if (TryGetValueWithAlias(values, columnMap, "DeviceModel", "device_model", out var deviceModel))
            entry.DeviceModel = deviceModel;

        if (TryGetValueWithAlias(values, columnMap, "OsVersion", "os_version", out var osVersion))
            entry.OsVersion = osVersion;

        if (TryGetValueWithAlias(values, columnMap, "BatteryLevel", "battery_level", out var batteryLevelStr) &&
            int.TryParse(batteryLevelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var batteryLevel))
            entry.BatteryLevel = batteryLevel;

        if (TryGetValueWithAlias(values, columnMap, "IsCharging", "is_charging", out var isChargingStr) &&
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
    /// Tries to get a value from CSV values by column name with an alias fallback.
    /// Primary name is PascalCase (current export format), alias is snake_case (legacy format).
    /// </summary>
    private static bool TryGetValueWithAlias(List<string> values, Dictionary<string, int> columnMap, string primaryName, string aliasName, out string value)
    {
        // Try primary name first (PascalCase - matches current export format)
        if (TryGetValue(values, columnMap, primaryName, out value))
            return true;

        // Fall back to alias (snake_case - legacy format)
        return TryGetValue(values, columnMap, aliasName, out value);
    }

    /// <summary>
    /// Parses a GeoJSON feature into a LocalTimelineEntry.
    /// Supports both PascalCase (current export format) and camelCase (legacy format) property names.
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

        // Required: timestamp - support both "TimestampUtc" (PascalCase) and "timestamp" (camelCase)
        var timestampStr = GetStringWithAlias(properties, "TimestampUtc", "timestamp");
        if (timestampStr == null)
            return null;

        if (!DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture,
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

        // Optional properties - support both PascalCase (current) and camelCase (legacy)
        entry.Accuracy = GetDoubleWithAlias(properties, "Accuracy", "accuracy");
        entry.Altitude = GetDoubleWithAlias(properties, "Altitude", "altitude");
        entry.Speed = GetDoubleWithAlias(properties, "Speed", "speed");
        entry.Bearing = GetDoubleWithAlias(properties, "Bearing", "bearing");
        entry.Provider = GetStringWithAlias(properties, "Provider", "provider");
        entry.Address = GetStringWithAlias(properties, "Address", "address");
        entry.FullAddress = GetStringWithAlias(properties, "FullAddress", "fullAddress");
        entry.Place = GetStringWithAlias(properties, "Place", "place");
        entry.Region = GetStringWithAlias(properties, "Region", "region");
        entry.Country = GetStringWithAlias(properties, "Country", "country");
        entry.PostCode = GetStringWithAlias(properties, "PostCode", "postCode");

        // Activity: PascalCase "Activity" or camelCase "activityType"
        entry.ActivityType = GetStringWithAlias(properties, "Activity", "activityType");

        // Timezone: PascalCase "TimeZoneId" or camelCase "timezone"
        entry.TimeZoneId = GetStringWithAlias(properties, "TimeZoneId", "timezone");

        entry.Notes = GetStringWithAlias(properties, "Notes", "notes");

        // Source: PascalCase "Source" or camelCase "source"
        entry.Source = GetStringWithAlias(properties, "Source", "source");

        // Capture metadata (optional fields) - support both formats
        entry.IsUserInvoked = GetBoolWithAlias(properties, "IsUserInvoked", "isUserInvoked");
        entry.AppVersion = GetStringWithAlias(properties, "AppVersion", "appVersion");
        entry.AppBuild = GetStringWithAlias(properties, "AppBuild", "appBuild");
        entry.DeviceModel = GetStringWithAlias(properties, "DeviceModel", "deviceModel");
        entry.OsVersion = GetStringWithAlias(properties, "OsVersion", "osVersion");
        entry.BatteryLevel = GetIntWithAlias(properties, "BatteryLevel", "batteryLevel");
        entry.IsCharging = GetBoolWithAlias(properties, "IsCharging", "isCharging");

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
               (!string.IsNullOrEmpty(import.Notes) && string.IsNullOrEmpty(existing.Notes)) ||
               (!string.IsNullOrEmpty(import.Source) && string.IsNullOrEmpty(existing.Source)) ||
               // Capture metadata fields
               (import.IsUserInvoked.HasValue && !existing.IsUserInvoked.HasValue) ||
               (!string.IsNullOrEmpty(import.AppVersion) && string.IsNullOrEmpty(existing.AppVersion)) ||
               (!string.IsNullOrEmpty(import.AppBuild) && string.IsNullOrEmpty(existing.AppBuild)) ||
               (!string.IsNullOrEmpty(import.DeviceModel) && string.IsNullOrEmpty(existing.DeviceModel)) ||
               (!string.IsNullOrEmpty(import.OsVersion) && string.IsNullOrEmpty(existing.OsVersion)) ||
               (import.BatteryLevel.HasValue && !existing.BatteryLevel.HasValue) ||
               (import.IsCharging.HasValue && !existing.IsCharging.HasValue);
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

        if (string.IsNullOrEmpty(existing.TimeZoneId) && !string.IsNullOrEmpty(import.TimeZoneId))
            existing.TimeZoneId = import.TimeZoneId;

        if (string.IsNullOrEmpty(existing.Notes) && !string.IsNullOrEmpty(import.Notes))
            existing.Notes = import.Notes;

        if (string.IsNullOrEmpty(existing.Source) && !string.IsNullOrEmpty(import.Source))
            existing.Source = import.Source;

        // Capture metadata fields
        if (!existing.IsUserInvoked.HasValue && import.IsUserInvoked.HasValue)
            existing.IsUserInvoked = import.IsUserInvoked;

        if (string.IsNullOrEmpty(existing.AppVersion) && !string.IsNullOrEmpty(import.AppVersion))
            existing.AppVersion = import.AppVersion;

        if (string.IsNullOrEmpty(existing.AppBuild) && !string.IsNullOrEmpty(import.AppBuild))
            existing.AppBuild = import.AppBuild;

        if (string.IsNullOrEmpty(existing.DeviceModel) && !string.IsNullOrEmpty(import.DeviceModel))
            existing.DeviceModel = import.DeviceModel;

        if (string.IsNullOrEmpty(existing.OsVersion) && !string.IsNullOrEmpty(import.OsVersion))
            existing.OsVersion = import.OsVersion;

        if (!existing.BatteryLevel.HasValue && import.BatteryLevel.HasValue)
            existing.BatteryLevel = import.BatteryLevel;

        if (!existing.IsCharging.HasValue && import.IsCharging.HasValue)
            existing.IsCharging = import.IsCharging;
    }

    #region GeoJSON Helper Methods

    /// <summary>
    /// Gets a string property with alias fallback for GeoJSON.
    /// </summary>
    private static string? GetStringWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    /// <summary>
    /// Gets a double property with alias fallback for GeoJSON.
    /// </summary>
    private static double? GetDoubleWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return null;
    }

    /// <summary>
    /// Gets an int property with alias fallback for GeoJSON.
    /// </summary>
    private static int? GetIntWithAlias(JsonElement element, string primaryName, string aliasName)
    {
        if (element.TryGetProperty(primaryName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        if (element.TryGetProperty(aliasName, out prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }

    /// <summary>
    /// Gets a bool property with alias fallback for GeoJSON.
    /// </summary>
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

    #endregion

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
