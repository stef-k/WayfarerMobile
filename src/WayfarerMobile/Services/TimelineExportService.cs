using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Exports local timeline data to CSV and GeoJSON formats.
/// </summary>
public class TimelineExportService
{
    private readonly ITimelineRepository _timelineRepository;
    private readonly ILogger<TimelineExportService> _logger;

    /// <summary>
    /// Creates a new instance of TimelineExportService.
    /// </summary>
    /// <param name="timelineRepository">Repository for timeline operations.</param>
    /// <param name="logger">Logger instance.</param>
    public TimelineExportService(
        ITimelineRepository timelineRepository,
        ILogger<TimelineExportService> logger)
    {
        _timelineRepository = timelineRepository ?? throw new ArgumentNullException(nameof(timelineRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Exports timeline data to CSV format.
    /// </summary>
    /// <param name="fromDate">Start date filter (inclusive), or null for all.</param>
    /// <param name="toDate">End date filter (inclusive), or null for all.</param>
    /// <returns>CSV content as string.</returns>
    public async Task<string> ExportToCsvAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var entries = await GetEntriesAsync(fromDate, toDate);
        _logger.LogInformation("Exporting {Count} entries to CSV", entries.Count);

        var sb = new StringBuilder();

        // Header row - PascalCase to match Wayfarer backend import parsers
        sb.AppendLine("Id,ServerId,TimestampUtc,LocalTimestamp,Latitude,Longitude,Accuracy,Altitude,Speed,Bearing,Provider,Address,FullAddress,Place,Region,Country,PostCode,Activity,TimeZoneId,Source,Notes,IsUserInvoked,AppVersion,AppBuild,DeviceModel,OsVersion,BatteryLevel,IsCharging");

        // Data rows
        foreach (var entry in entries)
        {
            sb.AppendLine(ToCsvRow(entry));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports timeline data to GeoJSON format.
    /// </summary>
    /// <param name="fromDate">Start date filter (inclusive), or null for all.</param>
    /// <param name="toDate">End date filter (inclusive), or null for all.</param>
    /// <returns>GeoJSON content as string.</returns>
    public async Task<string> ExportToGeoJsonAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var entries = await GetEntriesAsync(fromDate, toDate);
        _logger.LogInformation("Exporting {Count} entries to GeoJSON", entries.Count);

        var featureCollection = new GeoJsonFeatureCollection
        {
            Type = "FeatureCollection",
            Features = entries.Select(ToGeoJsonFeature).ToList()
        };

        // Use PascalCase to match Wayfarer backend import parsers (no naming policy)
        return JsonSerializer.Serialize(featureCollection, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    /// <summary>
    /// Exports to file and shares using system share dialog.
    /// </summary>
    /// <param name="format">Export format: "csv" or "geojson".</param>
    /// <param name="fromDate">Start date filter (inclusive), or null for all.</param>
    /// <param name="toDate">End date filter (inclusive), or null for all.</param>
    /// <returns>The shared file path, or null if cancelled.</returns>
    public async Task<string?> ShareExportAsync(string format, DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var content = format.ToLowerInvariant() switch
            {
                "csv" => await ExportToCsvAsync(fromDate, toDate),
                "geojson" or "json" => await ExportToGeoJsonAsync(fromDate, toDate),
                _ => throw new ArgumentException($"Unsupported format: {format}")
            };

            var extension = format.ToLowerInvariant() == "csv" ? "csv" : "geojson";
            var fileName = $"timeline-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{extension}";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
            _logger.LogDebug("Export file written to {Path}", filePath);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share Timeline Export",
                File = new ShareFile(filePath)
            });

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share export");
            return null;
        }
    }

    /// <summary>
    /// Gets entries for export based on date range.
    /// </summary>
    private async Task<List<LocalTimelineEntry>> GetEntriesAsync(DateTime? fromDate, DateTime? toDate)
    {
        if (fromDate.HasValue && toDate.HasValue)
        {
            return await _timelineRepository.GetLocalTimelineEntriesInRangeAsync(
                fromDate.Value, toDate.Value);
        }

        return await _timelineRepository.GetAllLocalTimelineEntriesAsync();
    }

    /// <summary>
    /// Converts an entry to a CSV row.
    /// Column order matches header: Id,ServerId,TimestampUtc,LocalTimestamp,Latitude,Longitude,...
    /// </summary>
    private static string ToCsvRow(LocalTimelineEntry entry)
    {
        // LocalTimestamp: if we have timezone info, compute local time; otherwise use UTC
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
            EscapeCsv(entry.Source),
            EscapeCsv(entry.Notes),
            // Capture metadata (optional fields)
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

    /// <summary>
    /// Computes local timestamp from UTC timestamp and timezone ID.
    /// </summary>
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

    /// <summary>
    /// Escapes a CSV field value.
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If contains comma, quote, or newline, wrap in quotes and escape quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Converts an entry to a GeoJSON feature.
    /// Property names match Wayfarer backend import parser expectations.
    /// </summary>
    private static GeoJsonFeature ToGeoJsonFeature(LocalTimelineEntry entry)
    {
        // LocalTimestamp: if we have timezone info, compute local time; otherwise use UTC
        var localTimestamp = ComputeLocalTimestamp(entry.Timestamp, entry.Timezone);

        return new GeoJsonFeature
        {
            Type = "Feature",
            Geometry = new GeoJsonGeometry
            {
                Type = "Point",
                Coordinates = new[] { entry.Longitude, entry.Latitude }
            },
            Properties = new GeoJsonProperties
            {
                Id = entry.Id,
                ServerId = entry.ServerId,
                TimestampUtc = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                LocalTimestamp = localTimestamp.ToString("o", CultureInfo.InvariantCulture),
                Accuracy = entry.Accuracy,
                Altitude = entry.Altitude,
                Speed = entry.Speed,
                Bearing = entry.Bearing,
                Provider = entry.Provider,
                Address = entry.Address,
                FullAddress = entry.FullAddress,
                Place = entry.Place,
                Region = entry.Region,
                Country = entry.Country,
                PostCode = entry.PostCode,
                Activity = entry.ActivityType,
                TimeZoneId = entry.Timezone,
                Source = entry.Source,
                Notes = entry.Notes,
                // Capture metadata (optional fields)
                IsUserInvoked = entry.IsUserInvoked,
                AppVersion = entry.AppVersion,
                AppBuild = entry.AppBuild,
                DeviceModel = entry.DeviceModel,
                OsVersion = entry.OsVersion,
                BatteryLevel = entry.BatteryLevel,
                IsCharging = entry.IsCharging
            }
        };
    }

    #region GeoJSON DTOs

    private class GeoJsonFeatureCollection
    {
        public string Type { get; set; } = "FeatureCollection";
        public List<GeoJsonFeature> Features { get; set; } = new();
    }

    private class GeoJsonFeature
    {
        public string Type { get; set; } = "Feature";
        public GeoJsonGeometry Geometry { get; set; } = new();
        public GeoJsonProperties Properties { get; set; } = new();
    }

    private class GeoJsonGeometry
    {
        public string Type { get; set; } = "Point";
        public double[] Coordinates { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// GeoJSON properties matching Wayfarer backend import parser expectations.
    /// </summary>
    private class GeoJsonProperties
    {
        public int Id { get; set; }
        public int? ServerId { get; set; }
        public string? TimestampUtc { get; set; }
        public string? LocalTimestamp { get; set; }
        public double? Accuracy { get; set; }
        public double? Altitude { get; set; }
        public double? Speed { get; set; }
        public double? Bearing { get; set; }
        public string? Provider { get; set; }
        public string? Address { get; set; }
        public string? FullAddress { get; set; }
        public string? Place { get; set; }
        public string? Region { get; set; }
        public string? Country { get; set; }
        public string? PostCode { get; set; }
        public string? Activity { get; set; }
        public string? TimeZoneId { get; set; }
        public string? Source { get; set; }
        public string? Notes { get; set; }
        // Capture metadata (optional)
        public bool? IsUserInvoked { get; set; }
        public string? AppVersion { get; set; }
        public string? AppBuild { get; set; }
        public string? DeviceModel { get; set; }
        public string? OsVersion { get; set; }
        public int? BatteryLevel { get; set; }
        public bool? IsCharging { get; set; }
    }

    #endregion
}
