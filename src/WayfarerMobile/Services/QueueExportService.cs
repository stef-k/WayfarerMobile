using System.Globalization;
using System.Text;
using System.Text.Json;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for exporting location queue data to CSV and GeoJSON formats.
/// </summary>
public class QueueExportService : IQueueExportService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    private readonly ILocationQueueRepository _repository;
    private readonly IActivitySyncService _activityService;

    /// <summary>
    /// Creates a new instance of QueueExportService.
    /// </summary>
    /// <param name="repository">The location queue repository.</param>
    /// <param name="activityService">The activity sync service for resolving activity names.</param>
    public QueueExportService(ILocationQueueRepository repository, IActivitySyncService activityService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
    }

    /// <inheritdoc />
    public async Task<string> ExportToCsvAsync()
    {
        var locations = await _repository.GetAllQueuedLocationsForExportAsync() ?? [];
        var activityLookup = await BuildActivityLookupAsync();
        var sb = new StringBuilder();

        // Header: First 20 columns are backend-compatible, remaining 8 are debug fields
        sb.AppendLine("Latitude,Longitude,TimestampUtc,LocalTimestamp,TimeZoneId,Accuracy,Altitude,Speed,Activity,Source,Notes,IsUserInvoked,Provider,Bearing,AppVersion,AppBuild,DeviceModel,OsVersion,BatteryLevel,IsCharging,Id,Status,SyncAttempts,LastSyncAttempt,IsRejected,RejectionReason,LastError,ActivityTypeId");

        foreach (var loc in locations)
        {
            var localTimestamp = ComputeLocalTimestamp(loc.Timestamp, loc.TimeZoneId);
            var activity = ResolveActivityName(loc.ActivityTypeId, activityLookup);
            var status = GetDisplayStatus(loc);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27}",
                // Backend-compatible fields (20 columns)
                loc.Latitude,
                loc.Longitude,
                loc.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                localTimestamp.ToString("O", CultureInfo.InvariantCulture),
                EscapeCsv(loc.TimeZoneId),
                loc.Accuracy,
                loc.Altitude,
                loc.Speed,
                EscapeCsv(activity),
                EscapeCsv(loc.Source),
                EscapeCsv(loc.CheckInNotes),
                loc.IsUserInvoked,
                EscapeCsv(loc.Provider),
                loc.Bearing,
                EscapeCsv(loc.AppVersion),
                EscapeCsv(loc.AppBuild),
                EscapeCsv(loc.DeviceModel),
                EscapeCsv(loc.OsVersion),
                loc.BatteryLevel,
                loc.IsCharging,
                // Debug fields (8 columns)
                loc.Id,
                EscapeCsv(status),
                loc.SyncAttempts,
                loc.LastSyncAttempt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                loc.IsRejected,
                EscapeCsv(loc.RejectionReason),
                EscapeCsv(loc.LastError),
                loc.ActivityTypeId));
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> ExportToGeoJsonAsync()
    {
        var locations = await _repository.GetAllQueuedLocationsForExportAsync() ?? [];
        var activityLookup = await BuildActivityLookupAsync();

        var features = locations.Select(loc =>
        {
            var localTimestamp = ComputeLocalTimestamp(loc.Timestamp, loc.TimeZoneId);
            var activity = ResolveActivityName(loc.ActivityTypeId, activityLookup);

            return new
            {
                type = "Feature",
                geometry = new
                {
                    type = "Point",
                    coordinates = loc.Altitude.HasValue
                        ? new[] { loc.Longitude, loc.Latitude, loc.Altitude.Value }
                        : new[] { loc.Longitude, loc.Latitude }
                },
                properties = new
                {
                    // Backend-compatible fields (PascalCase)
                    TimestampUtc = loc.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    LocalTimestamp = localTimestamp.ToString("O", CultureInfo.InvariantCulture),
                    TimeZoneId = loc.TimeZoneId,
                    Accuracy = loc.Accuracy,
                    Altitude = loc.Altitude,
                    Speed = loc.Speed,
                    Activity = activity,
                    Source = loc.Source,
                    Notes = loc.CheckInNotes,
                    IsUserInvoked = loc.IsUserInvoked,
                    Provider = loc.Provider,
                    Bearing = loc.Bearing,
                    AppVersion = loc.AppVersion,
                    AppBuild = loc.AppBuild,
                    DeviceModel = loc.DeviceModel,
                    OsVersion = loc.OsVersion,
                    BatteryLevel = loc.BatteryLevel,
                    IsCharging = loc.IsCharging,
                    // Debug fields (also PascalCase for consistency)
                    Id = loc.Id,
                    Status = GetDisplayStatus(loc),
                    SyncAttempts = loc.SyncAttempts,
                    LastSyncAttempt = loc.LastSyncAttempt?.ToString("O", CultureInfo.InvariantCulture),
                    IsRejected = loc.IsRejected,
                    RejectionReason = loc.RejectionReason,
                    LastError = loc.LastError,
                    ActivityTypeId = loc.ActivityTypeId
                }
            };
        });

        var geoJson = new
        {
            type = "FeatureCollection",
            features = features.ToList()
        };

        return JsonSerializer.Serialize(geoJson, s_jsonOptions);
    }

    /// <inheritdoc />
    public async Task ShareExportAsync(string format)
    {
        // Clean up old export files from previous sessions (older than 1 hour)
        CleanupOldExportFiles();

        var content = format == "geojson"
            ? await ExportToGeoJsonAsync()
            : await ExportToCsvAsync();

        var extension = format == "geojson" ? "geojson" : "csv";
        var fileName = $"wayfarer_queue_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{extension}";
        var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName);

        await File.WriteAllTextAsync(tempPath, content);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export Location Queue",
            File = new ShareFile(tempPath)
        });

        // Note: File is NOT deleted immediately after share.
        // The share sheet may still be reading the file asynchronously.
        // Cleanup happens on next export via CleanupOldExportFiles().
    }

    /// <summary>
    /// Cleans up old export files from previous sessions.
    /// Files older than 1 hour are deleted to prevent accumulation.
    /// </summary>
    private static void CleanupOldExportFiles()
    {
        try
        {
            var cacheDir = FileSystem.CacheDirectory;
            var cutoff = DateTime.UtcNow.AddHours(-1);

            foreach (var file in Directory.GetFiles(cacheDir, "wayfarer_queue_*.*"))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTimeUtc < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Best effort cleanup - file may be in use
                }
            }
        }
        catch
        {
            // Best effort cleanup - directory access may fail
        }
    }

    /// <summary>
    /// Gets the display status string for a queued location.
    /// </summary>
    private static string GetDisplayStatus(QueuedLocation loc)
    {
        if (loc.IsRejected) return "Rejected";
        if (loc.SyncStatus == SyncStatus.Pending && loc.SyncAttempts > 0)
            return $"Retrying({loc.SyncAttempts})";

        return loc.SyncStatus switch
        {
            SyncStatus.Pending => "Pending",
            SyncStatus.Syncing => "Syncing",
            SyncStatus.Synced => "Synced",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Escapes a string for CSV output with formula injection protection.
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

    /// <summary>
    /// Computes the local timestamp from UTC timestamp and timezone ID.
    /// Falls back to device's current timezone if stored timezone is invalid.
    /// Returns DateTime with Unspecified kind for export (no offset in ISO 8601 output).
    /// </summary>
    private static DateTime ComputeLocalTimestamp(DateTime timestampUtc, string? timeZoneId)
    {
        DateTime localTime;

        if (string.IsNullOrEmpty(timeZoneId))
        {
            // Fallback: use device's current timezone
            localTime = timestampUtc.ToLocalTime();
        }
        else
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                localTime = TimeZoneInfo.ConvertTimeFromUtc(timestampUtc, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                // Invalid timezone stored, fallback to device current
                localTime = timestampUtc.ToLocalTime();
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupted timezone data, fallback to device current
                localTime = timestampUtc.ToLocalTime();
            }
        }

        // Return with Unspecified kind so ISO 8601 format has no offset
        // Backend reads TimeZoneId separately for timezone info
        return DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Builds a lookup dictionary of activity IDs to lowercase names.
    /// Includes both default activities (negative IDs) and server activities (positive IDs).
    /// </summary>
    private async Task<Dictionary<int, string>> BuildActivityLookupAsync()
    {
        try
        {
            // Use GetAllActivityTypesAsync to include both defaults and server activities
            // since queued locations may have IDs from either set depending on when they were queued
            var activities = await _activityService.GetAllActivityTypesAsync();
            return activities
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .ToDictionary(a => a.Id, a => a.Name.ToLowerInvariant());
        }
        catch
        {
            // If activity service fails, return empty lookup
            return [];
        }
    }

    /// <summary>
    /// Resolves activity type ID to lowercase activity name.
    /// Backend expects lowercase names (e.g., "walking", not "Walking").
    /// Uses the pre-loaded activity lookup for both default and server activities.
    /// </summary>
    private static string? ResolveActivityName(int? activityTypeId, Dictionary<int, string> activityLookup)
    {
        if (activityTypeId == null)
            return null;

        return activityLookup.TryGetValue(activityTypeId.Value, out var name) ? name : null;
    }
}
