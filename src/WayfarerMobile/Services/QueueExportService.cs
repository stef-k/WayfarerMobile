using System.Globalization;
using System.Text;
using System.Text.Json;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for exporting location queue data to CSV and GeoJSON formats.
/// </summary>
public class QueueExportService : IQueueExportService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    private readonly ILocationQueueRepository _repository;

    /// <summary>
    /// Creates a new instance of QueueExportService.
    /// </summary>
    /// <param name="repository">The location queue repository.</param>
    public QueueExportService(ILocationQueueRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public async Task<string> ExportToCsvAsync()
    {
        var locations = await _repository.GetAllQueuedLocationsForExportAsync() ?? [];
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("Id,Timestamp,Latitude,Longitude,Altitude,Accuracy,Speed,Bearing,Provider,SyncStatus,SyncAttempts,LastSyncAttempt,IsRejected,RejectionReason,LastError,IsUserInvoked,ActivityTypeId,CheckInNotes");

        foreach (var loc in locations)
        {
            var status = GetDisplayStatus(loc);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17}",
                loc.Id,
                loc.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                loc.Latitude,
                loc.Longitude,
                loc.Altitude,
                loc.Accuracy,
                loc.Speed,
                loc.Bearing,
                EscapeCsv(loc.Provider),
                EscapeCsv(status),
                loc.SyncAttempts,
                loc.LastSyncAttempt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                loc.IsRejected,
                EscapeCsv(loc.RejectionReason),
                EscapeCsv(loc.LastError),
                loc.IsUserInvoked,
                loc.ActivityTypeId,
                EscapeCsv(loc.CheckInNotes)));
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<string> ExportToGeoJsonAsync()
    {
        var locations = await _repository.GetAllQueuedLocationsForExportAsync() ?? [];

        var features = locations.Select(loc => new
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
                id = loc.Id,
                timestamp = loc.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                accuracy = loc.Accuracy,
                speed = loc.Speed,
                bearing = loc.Bearing,
                provider = loc.Provider,
                status = GetDisplayStatus(loc),
                syncAttempts = loc.SyncAttempts,
                lastSyncAttempt = loc.LastSyncAttempt?.ToString("O", CultureInfo.InvariantCulture),
                isRejected = loc.IsRejected,
                rejectionReason = loc.RejectionReason,
                lastError = loc.LastError,
                isUserInvoked = loc.IsUserInvoked,
                activityTypeId = loc.ActivityTypeId,
                checkInNotes = loc.CheckInNotes
            }
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
        var content = format == "geojson"
            ? await ExportToGeoJsonAsync()
            : await ExportToCsvAsync();

        var extension = format == "geojson" ? "geojson" : "csv";
        var fileName = $"wayfarer_queue_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{extension}";
        var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName);

        try
        {
            await File.WriteAllTextAsync(tempPath, content);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Location Queue",
                File = new ShareFile(tempPath)
            });
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup
            }
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
}
