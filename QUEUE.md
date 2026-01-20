# Offline Queue Settings - Simplified Specification

Issue: https://github.com/stef-k/WayfarerMobile/issues/152

## Overview

Add user-facing queue management controls to Settings, allowing users to:
- View queue status (count, limit, health, coverage)
- Adjust queue limit (1-100,000 entries)
- Export queue to CSV or GeoJSON
- Clear pending/synced/all entries

Move queue management actions from Diagnostics to Settings; Diagnostics becomes read-only for queue info.

## Design Principles

1. **Minimal new abstractions** - Extend existing services where possible
2. **Simple concurrency** - Boolean flags, not semaphores
3. **Defer optimization** - No streaming export until needed
4. **Proportionate testing** - ~40 core tests, not 200+

---

## 1. Settings Service Changes

### ISettingsService

Add to `src/WayfarerMobile.Core/Interfaces/ISettingsService.cs`:

```csharp
/// <summary>
/// Maximum number of locations to keep in the offline queue.
/// Range: 1-100,000. Default: 25,000.
/// </summary>
int QueueLimitMaxLocations { get; set; }
```

### SettingsService

Add to `src/WayfarerMobile/Services/SettingsService.cs`:

```csharp
public const string QueueLimitMaxLocationsKey = "QueueLimitMaxLocations";
private const int QueueLimitDefault = 25000;
private const int QueueLimitMin = 1;
private const int QueueLimitMax = 100000;

public int QueueLimitMaxLocations
{
    get => Preferences.Get(QueueLimitMaxLocationsKey, QueueLimitDefault);
    set => Preferences.Set(QueueLimitMaxLocationsKey, Math.Clamp(value, QueueLimitMin, QueueLimitMax));
}
```

---

## 2. Repository Changes

### ILocationQueueRepository

Add these methods to the interface:

```csharp
/// <summary>Gets total count of all queued locations.</summary>
Task<int> GetTotalCountAsync();

/// <summary>Gets count of entries currently syncing.</summary>
Task<int> GetSyncingCountAsync();

/// <summary>Gets the newest pending location for coverage calculation.</summary>
Task<QueuedLocation?> GetNewestPendingLocationAsync();

/// <summary>Gets all locations ordered by Timestamp ASC, Id ASC for export.</summary>
Task<List<QueuedLocation>> GetAllQueuedLocationsForExportAsync();

/// <summary>Clears synced and rejected entries.</summary>
Task<int> ClearSyncedAndRejectedQueueAsync();
```

### LocationQueueRepository

Implement the new methods:

```csharp
public async Task<int> GetTotalCountAsync()
{
    var db = await GetConnectionAsync();
    return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
}

public async Task<int> GetSyncingCountAsync()
{
    var db = await GetConnectionAsync();
    return await db.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM QueuedLocations WHERE SyncStatus = ?",
        (int)SyncStatus.Syncing);
}

public async Task<QueuedLocation?> GetNewestPendingLocationAsync()
{
    var db = await GetConnectionAsync();
    return await db.Table<QueuedLocation>()
        .Where(l => l.SyncStatus == SyncStatus.Pending && !l.IsRejected)
        .OrderByDescending(l => l.Timestamp)
        .FirstOrDefaultAsync();
}

public async Task<List<QueuedLocation>> GetAllQueuedLocationsForExportAsync()
{
    var db = await GetConnectionAsync();
    return await db.QueryAsync<QueuedLocation>(
        "SELECT * FROM QueuedLocations ORDER BY Timestamp ASC, Id ASC");
}

public async Task<int> ClearSyncedAndRejectedQueueAsync()
{
    var db = await GetConnectionAsync();
    return await db.ExecuteAsync(
        "DELETE FROM QueuedLocations WHERE (SyncStatus = ? AND IsRejected = 0) OR IsRejected = 1",
        (int)SyncStatus.Synced);
}
```

### Update CleanupOldLocationsAsync

Modify existing method to accept limit parameter:

```csharp
/// <summary>
/// Enforces queue limit by removing oldest safe entries, then oldest pending if needed.
/// Never removes Syncing entries (in-flight protection).
/// </summary>
public async Task CleanupOldLocationsAsync(int maxQueuedLocations)
{
    var db = await GetConnectionAsync();

    var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
    if (count < maxQueuedLocations)
        return;

    var toDelete = count - maxQueuedLocations + 1;

    // Delete oldest safe entries (synced or rejected) first
    var safeDeleted = await db.ExecuteAsync(@"
        DELETE FROM QueuedLocations WHERE Id IN (
            SELECT Id FROM QueuedLocations
            WHERE (SyncStatus = ? AND IsRejected = 0) OR IsRejected = 1
            ORDER BY Timestamp, Id
            LIMIT ?
        )", (int)SyncStatus.Synced, toDelete);

    if (safeDeleted >= toDelete)
        return;

    var remaining = toDelete - safeDeleted;

    // Last resort: delete oldest pending entries (not syncing)
    await db.ExecuteAsync(@"
        DELETE FROM QueuedLocations WHERE Id IN (
            SELECT Id FROM QueuedLocations
            WHERE SyncStatus = ? AND IsRejected = 0
            ORDER BY Timestamp, Id
            LIMIT ?
        )", (int)SyncStatus.Pending, remaining);
}
```

---

## 3. DatabaseService Changes

Update `QueueLocationAsync` to accept and use the limit:

```csharp
public async Task<int> QueueLocationAsync(
    LocationData location,
    int maxQueuedLocations,  // NEW PARAMETER
    bool isUserInvoked = false,
    int? activityTypeId = null,
    string? notes = null)
{
    var db = await GetConnectionAsync();

    // Cleanup before insert
    await EnforceQueueLimitAsync(db, maxQueuedLocations);

    // ... rest of existing implementation
}

private async Task EnforceQueueLimitAsync(SQLiteAsyncConnection db, int maxQueuedLocations)
{
    // Same logic as LocationQueueRepository.CleanupOldLocationsAsync
    var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueuedLocations");
    if (count < maxQueuedLocations)
        return;

    var toDelete = count - maxQueuedLocations + 1;

    var safeDeleted = await db.ExecuteAsync(@"
        DELETE FROM QueuedLocations WHERE Id IN (
            SELECT Id FROM QueuedLocations
            WHERE (SyncStatus = ? AND IsRejected = 0) OR IsRejected = 1
            ORDER BY Timestamp, Id
            LIMIT ?
        )", (int)SyncStatus.Synced, toDelete);

    if (safeDeleted >= toDelete)
        return;

    var remaining = toDelete - safeDeleted;

    await db.ExecuteAsync(@"
        DELETE FROM QueuedLocations WHERE Id IN (
            SELECT Id FROM QueuedLocations
            WHERE SyncStatus = ? AND IsRejected = 0
            ORDER BY Timestamp, Id
            LIMIT ?
        )", (int)SyncStatus.Pending, remaining);
}
```

**Note:** Remove the hardcoded `25000` constant.

---

## 4. QueueStatusInfo Model

Create `src/WayfarerMobile.Core/Models/QueueStatusInfo.cs`:

```csharp
namespace WayfarerMobile.Core.Models;

/// <summary>
/// Queue status information for Settings UI display.
/// </summary>
public class QueueStatusInfo
{
    public int TotalCount { get; init; }
    public int PendingCount { get; init; }
    public int RetryingCount { get; init; }
    public int SyncingCount { get; init; }
    public int SyncedCount { get; init; }
    public int RejectedCount { get; init; }
    public int QueueLimit { get; init; }
    public DateTime? OldestPendingTimestamp { get; init; }
    public DateTime? NewestPendingTimestamp { get; init; }
    public DateTime? LastSyncedTimestamp { get; init; }
    public double UsagePercent { get; init; }

    /// <summary>Over limit when count exceeds limit (not at exactly 100%).</summary>
    public bool IsOverLimit => TotalCount > QueueLimit;

    /// <summary>Current coverage span (newest - oldest pending).</summary>
    public TimeSpan? CurrentCoverageSpan =>
        OldestPendingTimestamp.HasValue && NewestPendingTimestamp.HasValue
            ? NewestPendingTimestamp.Value - OldestPendingTimestamp.Value
            : null;

    /// <summary>Estimated remaining headroom based on time threshold.</summary>
    public TimeSpan GetRemainingHeadroom(int timeThresholdMinutes)
    {
        if (timeThresholdMinutes <= 0 || QueueLimit <= 0)
            return TimeSpan.Zero;

        var slotsRemaining = Math.Max(0, QueueLimit - TotalCount);
        return TimeSpan.FromMinutes(slotsRemaining * timeThresholdMinutes);
    }

    /// <summary>Health status based on usage percentage.</summary>
    public string HealthStatus
    {
        get
        {
            if (QueueLimit <= 0) return "Unknown";
            if (TotalCount > QueueLimit) return "Over Limit";

            var percent = UsagePercent;
            return percent switch
            {
                >= 95 => "Critical",
                >= 80 => "Warning",
                _ => "Healthy"
            };
        }
    }
}
```

---

## 5. AppDiagnosticService Extension

Add method to existing `AppDiagnosticService` (no new service needed):

```csharp
/// <summary>
/// Gets comprehensive queue status for Settings display.
/// </summary>
public async Task<QueueStatusInfo> GetQueueStatusAsync()
{
    var totalCount = await _locationQueueRepository.GetTotalCountAsync();
    var queueLimit = _settingsService.QueueLimitMaxLocations;

    var allPendingCount = await _locationQueueRepository.GetPendingCountAsync();
    var retryingCount = await _locationQueueRepository.GetRetryingCountAsync();
    var syncingCount = await _locationQueueRepository.GetSyncingCountAsync();
    var syncedCount = await _locationQueueRepository.GetSyncedLocationCountAsync();
    var rejectedCount = await _locationQueueRepository.GetRejectedLocationCountAsync();

    var pendingCount = allPendingCount - retryingCount;

    var oldestPending = await _locationQueueRepository.GetOldestPendingLocationAsync();
    var newestPending = await _locationQueueRepository.GetNewestPendingLocationAsync();
    var lastSynced = await _locationQueueRepository.GetLastSyncedLocationAsync();

    return new QueueStatusInfo
    {
        TotalCount = totalCount,
        PendingCount = pendingCount,
        RetryingCount = retryingCount,
        SyncingCount = syncingCount,
        SyncedCount = syncedCount,
        RejectedCount = rejectedCount,
        QueueLimit = queueLimit,
        OldestPendingTimestamp = oldestPending?.Timestamp,
        NewestPendingTimestamp = newestPending?.Timestamp,
        LastSyncedTimestamp = lastSynced?.Timestamp,
        UsagePercent = queueLimit > 0 ? (double)totalCount / queueLimit * 100 : 0
    };
}
```

---

## 6. QueueExportService

### Interface

Create `src/WayfarerMobile.Core/Interfaces/IQueueExportService.cs`:

```csharp
namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for exporting queue data to various formats.
/// </summary>
public interface IQueueExportService
{
    /// <summary>Exports queue to CSV format.</summary>
    Task<string> ExportToCsvAsync();

    /// <summary>Exports queue to GeoJSON format.</summary>
    Task<string> ExportToGeoJsonAsync();

    /// <summary>Exports and opens share dialog.</summary>
    /// <param name="format">"csv" or "geojson"</param>
    Task ShareExportAsync(string format);
}
```

### Implementation

Create `src/WayfarerMobile/Services/QueueExportService.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

public class QueueExportService : IQueueExportService
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    private readonly ILocationQueueRepository _repository;

    public QueueExportService(ILocationQueueRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

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
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* Best effort cleanup */ }
        }
    }

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

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Formula injection protection
        if (value.Length > 0 && "=+-@|\t".Contains(value[0]))
            value = $"'{value}";

        // Quote if contains special characters
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }
}
```

---

## 7. OfflineQueueSettingsViewModel

Create `src/WayfarerMobile/ViewModels/Settings/OfflineQueueSettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels.Settings;

public partial class OfflineQueueSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocationQueueRepository _repository;
    private readonly IQueueExportService _exportService;
    private readonly AppDiagnosticService _diagnosticService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly ILogger<OfflineQueueSettingsViewModel> _logger;

    public OfflineQueueSettingsViewModel(
        ISettingsService settingsService,
        ILocationQueueRepository repository,
        IQueueExportService exportService,
        AppDiagnosticService diagnosticService,
        IDialogService dialogService,
        IToastService toastService,
        ILogger<OfflineQueueSettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _repository = repository;
        _exportService = exportService;
        _diagnosticService = diagnosticService;
        _dialogService = dialogService;
        _toastService = toastService;
        _logger = logger;

        _queueLimitText = _settingsService.QueueLimitMaxLocations.ToString();
        _queueLimit = _settingsService.QueueLimitMaxLocations;
    }

    // Status properties
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _retryingCount;
    [ObservableProperty] private int _syncingCount;
    [ObservableProperty] private int _syncedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _queueLimit;
    [ObservableProperty] private string _healthStatus = "Unknown";
    [ObservableProperty] private double _usagePercent;
    [ObservableProperty] private bool _isOverLimit;
    [ObservableProperty] private string _oldestPendingAge = "N/A";
    [ObservableProperty] private string _lastSyncTime = "Never";
    [ObservableProperty] private string _coverageSpan = "N/A";
    [ObservableProperty] private string _remainingHeadroom = "N/A";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _showStorageWarning;
    [ObservableProperty] private string _storageWarningText = "";

    // Limit input
    [ObservableProperty] private string _queueLimitText;

    public bool ShowApproxSuffix => !IsOverLimit;
    partial void OnIsOverLimitChanged(bool value) => OnPropertyChanged(nameof(ShowApproxSuffix));

    public Color HealthStatusColor => HealthStatus switch
    {
        "Over Limit" => Colors.Red,
        "Critical" => Colors.OrangeRed,
        "Warning" => Colors.Orange,
        _ => Colors.Green
    };
    partial void OnHealthStatusChanged(string value) => OnPropertyChanged(nameof(HealthStatusColor));

    /// <summary>
    /// Called when Entry loses focus. Validates and applies the new limit.
    /// </summary>
    [RelayCommand]
    private async Task ApplyQueueLimitAsync()
    {
        if (!int.TryParse(QueueLimitText, out var newLimit))
        {
            QueueLimitText = _settingsService.QueueLimitMaxLocations.ToString();
            return;
        }

        var clamped = Math.Clamp(newLimit, 1, 100000);

        try
        {
            _settingsService.QueueLimitMaxLocations = clamped;
            QueueLimitText = clamped.ToString();
            QueueLimit = clamped;

            UpdateStorageWarning(clamped);

            // Trim safe entries if needed (non-destructive to pending)
            if (TotalCount > clamped)
            {
                await _repository.CleanupOldLocationsAsync(clamped);
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply queue limit");
            await _toastService.ShowErrorAsync("Failed to update queue limit");
            // Revert UI to actual saved value
            QueueLimitText = _settingsService.QueueLimitMaxLocations.ToString();
            QueueLimit = _settingsService.QueueLimitMaxLocations;
        }
    }

    private void UpdateStorageWarning(int limit)
    {
        ShowStorageWarning = limit > 50000;
        if (ShowStorageWarning)
            StorageWarningText = "High queue limits may use significant storage. Ensure sufficient free space.";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_isRefreshing) return;

        try
        {
            IsRefreshing = true;

            var status = await _diagnosticService.GetQueueStatusAsync();
            var timeThreshold = _settingsService.LocationTimeThresholdMinutes;

            TotalCount = status.TotalCount;
            PendingCount = status.PendingCount;
            RetryingCount = status.RetryingCount;
            SyncingCount = status.SyncingCount;
            SyncedCount = status.SyncedCount;
            RejectedCount = status.RejectedCount;
            QueueLimit = status.QueueLimit;
            HealthStatus = status.HealthStatus;
            UsagePercent = status.UsagePercent;
            IsOverLimit = status.IsOverLimit;
            OldestPendingAge = FormatAge(status.OldestPendingTimestamp);
            LastSyncTime = FormatTime(status.LastSyncedTimestamp);
            CoverageSpan = FormatSpan(status.CurrentCoverageSpan);
            RemainingHeadroom = FormatHeadroom(status, timeThreshold);

            UpdateStorageWarning(QueueLimit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh queue status");
            await _toastService.ShowErrorAsync("Failed to refresh queue status");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        try
        {
            var count = await _repository.GetTotalCountAsync();
            if (count == 0)
            {
                await _toastService.ShowAsync("Queue is empty - nothing to export");
                return;
            }
            await _exportService.ShareExportAsync("csv");
            await _toastService.ShowSuccessAsync("Export ready to share");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV export failed");
            await _toastService.ShowErrorAsync("Export failed");
        }
    }

    [RelayCommand]
    private async Task ExportGeoJsonAsync()
    {
        try
        {
            var count = await _repository.GetTotalCountAsync();
            if (count == 0)
            {
                await _toastService.ShowAsync("Queue is empty - nothing to export");
                return;
            }
            await _exportService.ShareExportAsync("geojson");
            await _toastService.ShowSuccessAsync("Export ready to share");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GeoJSON export failed");
            await _toastService.ShowErrorAsync("Export failed");
        }
    }

    [RelayCommand]
    private async Task ClearPendingQueueAsync()
    {
        if (PendingCount + RetryingCount == 0)
        {
            await _dialogService.ShowInfoAsync("Queue Empty", "There are no pending locations to clear.");
            return;
        }

        var confirm = await _dialogService.ShowConfirmAsync(
            "Clear Pending Locations",
            $"Delete {PendingCount + RetryingCount} pending locations? This cannot be undone.",
            "Clear", "Cancel");

        if (!confirm) return;

        try
        {
            var deleted = await _repository.ClearPendingQueueAsync();
            await _toastService.ShowSuccessAsync($"{deleted} pending locations cleared");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing pending queue");
            await _toastService.ShowErrorAsync("Failed to clear pending locations");
            await RefreshAsync(); // Show actual state after partial failure
        }
    }

    [RelayCommand]
    private async Task ClearSyncedQueueAsync()
    {
        if (SyncedCount + RejectedCount == 0)
        {
            await _dialogService.ShowInfoAsync("Nothing to Clear", "There are no synced or rejected locations.");
            return;
        }

        var confirm = await _dialogService.ShowConfirmAsync(
            "Clear Synced Locations",
            $"Delete {SyncedCount} synced and {RejectedCount} rejected locations?",
            "Clear", "Cancel");

        if (!confirm) return;

        try
        {
            var deleted = await _repository.ClearSyncedAndRejectedQueueAsync();
            await _toastService.ShowSuccessAsync($"{deleted} locations cleared");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing synced queue");
            await _toastService.ShowErrorAsync("Failed to clear synced locations");
            await RefreshAsync(); // Show actual state after partial failure
        }
    }

    [RelayCommand]
    private async Task ClearAllQueueAsync()
    {
        if (TotalCount == 0)
        {
            await _dialogService.ShowInfoAsync("Queue Empty", "The queue is already empty.");
            return;
        }

        var hasPending = PendingCount + RetryingCount > 0;
        var message = hasPending
            ? $"Delete ALL {TotalCount} locations including {PendingCount + RetryingCount} unsynced? This cannot be undone."
            : $"Delete all {TotalCount} locations?";

        var confirm = await _dialogService.ShowConfirmAsync("Clear Entire Queue", message, "Delete All", "Cancel");
        if (!confirm) return;

        try
        {
            var deleted = await _repository.ClearAllQueueAsync();
            await _toastService.ShowSuccessAsync($"{deleted} locations cleared");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing all queue");
            await _toastService.ShowErrorAsync("Failed to clear queue");
            await RefreshAsync(); // Show actual state after partial failure
        }
    }

    // Format helpers
    private static string FormatAge(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "N/A";
        var age = DateTime.UtcNow - timestamp.Value;
        if (age < TimeSpan.Zero) return "Just now";
        if (age.TotalDays >= 1) return $"{age.TotalDays:F0}d ago";
        if (age.TotalHours >= 1) return $"{age.TotalHours:F0}h ago";
        if (age.TotalMinutes >= 1) return $"{age.TotalMinutes:F0}m ago";
        return "Just now";
    }

    private static string FormatTime(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "Never";
        var local = timestamp.Value.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today) return $"Today {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"Yesterday {local:HH:mm}";
        return local.ToString("g");
    }

    private static string FormatSpan(TimeSpan? span)
    {
        if (!span.HasValue) return "N/A";
        var s = span.Value;
        if (s.TotalDays >= 1) return $"{s.Days}d {s.Hours}h";
        if (s.TotalHours >= 1) return $"{s.Hours}h {s.Minutes}m";
        if (s.TotalMinutes >= 1) return $"{s.TotalMinutes:F0}m";
        return "< 1m";
    }

    private static string FormatHeadroom(QueueStatusInfo status, int timeThreshold)
    {
        if (status.TotalCount > status.QueueLimit) return "Over limit";
        var headroom = status.GetRemainingHeadroom(timeThreshold);
        if (headroom.TotalDays >= 1) return $"~{headroom.TotalDays:F0} days";
        if (headroom.TotalHours >= 1) return $"~{headroom.TotalHours:F0} hours";
        return $"~{headroom.TotalMinutes:F0} min";
    }
}
```

---

## 8. SettingsViewModel Changes

Add to `SettingsViewModel`:

```csharp
public OfflineQueueSettingsViewModel OfflineQueue { get; }

// In constructor:
OfflineQueue = offlineQueueSettingsViewModel;
```

---

## 9. Settings UI

Add to `SettingsPage.xaml` (after Timeline Data section):

```xml
<!-- Offline Queue Section -->
<syncfusion:SfExpander x:Name="OfflineQueueExpander"
                       Expanded="OfflineQueueExpander_Expanded">
    <syncfusion:SfExpander.Header>
        <Grid Padding="16,12">
            <Label Text="Offline Queue" Style="{StaticResource ExpanderHeaderLabel}" />
        </Grid>
    </syncfusion:SfExpander.Header>
    <syncfusion:SfExpander.Content>
        <VerticalStackLayout Padding="16" Spacing="12"
                            BindingContext="{Binding OfflineQueue}">

            <!-- Status Summary -->
            <Frame BackgroundColor="{StaticResource Gray200}" Padding="12" CornerRadius="8">
                <Grid ColumnDefinitions="*,Auto" RowDefinitions="Auto,Auto,Auto,Auto">
                    <Label Text="Queue Status" FontAttributes="Bold" />
                    <Label Grid.Column="1" Text="{Binding HealthStatus}"
                           TextColor="{Binding HealthStatusColor}" FontAttributes="Bold" />

                    <Label Grid.Row="1" Text="{Binding TotalCount, StringFormat='{0} entries'}" />
                    <Label Grid.Row="1" Grid.Column="1"
                           Text="{Binding UsagePercent, StringFormat='{0:F0}% of limit'}" />

                    <Label Grid.Row="2" Text="{Binding OldestPendingAge, StringFormat='Oldest: {0}'}" />
                    <Label Grid.Row="2" Grid.Column="1"
                           Text="{Binding LastSyncTime, StringFormat='Last sync: {0}'}" />

                    <Label Grid.Row="3" Text="{Binding CoverageSpan, StringFormat='Coverage: {0}'}" />
                    <HorizontalStackLayout Grid.Row="3" Grid.Column="1">
                        <Label Text="{Binding RemainingHeadroom, StringFormat='Headroom: {0}'}" />
                        <Label Text=" (approx)" IsVisible="{Binding ShowApproxSuffix}"
                               TextColor="{StaticResource Gray500}" />
                    </HorizontalStackLayout>
                </Grid>
            </Frame>

            <!-- Breakdown -->
            <Grid ColumnDefinitions="*,*,*" ColumnSpacing="8">
                <Frame BackgroundColor="{StaticResource Gray200}" Padding="8">
                    <VerticalStackLayout>
                        <Label Text="{Binding PendingCount}" FontAttributes="Bold" HorizontalOptions="Center" />
                        <Label Text="Pending" HorizontalOptions="Center" FontSize="12" />
                    </VerticalStackLayout>
                </Frame>
                <Frame Grid.Column="1" BackgroundColor="{StaticResource Gray200}" Padding="8">
                    <VerticalStackLayout>
                        <Label Text="{Binding SyncedCount}" FontAttributes="Bold" HorizontalOptions="Center" />
                        <Label Text="Synced" HorizontalOptions="Center" FontSize="12" />
                    </VerticalStackLayout>
                </Frame>
                <Frame Grid.Column="2" BackgroundColor="{StaticResource Gray200}" Padding="8">
                    <VerticalStackLayout>
                        <Label Text="{Binding RejectedCount}" FontAttributes="Bold" HorizontalOptions="Center" />
                        <Label Text="Rejected" HorizontalOptions="Center" FontSize="12" />
                    </VerticalStackLayout>
                </Frame>
            </Grid>

            <!-- Queue Limit -->
            <Label Text="Queue Limit" FontAttributes="Bold" />
            <Grid ColumnDefinitions="*,Auto">
                <Entry x:Name="QueueLimitEntry"
                       Text="{Binding QueueLimitText}"
                       Keyboard="Numeric"
                       Unfocused="QueueLimitEntry_Unfocused" />
                <Stepper Grid.Column="1"
                         Minimum="1000" Maximum="100000" Increment="1000"
                         Value="{Binding QueueLimit}" />
            </Grid>
            <Label Text="Range: 1 - 100,000 entries" FontSize="12" TextColor="{StaticResource Gray500}" />
            <Label Text="{Binding StorageWarningText}"
                   IsVisible="{Binding ShowStorageWarning}"
                   TextColor="Orange" FontSize="12" />

            <!-- Export Buttons -->
            <Label Text="Export Queue" FontAttributes="Bold" Margin="0,8,0,0" />
            <HorizontalStackLayout Spacing="8">
                <Button Text="Export CSV" Command="{Binding ExportCsvCommand}" Style="{StaticResource DarkButton}" />
                <Button Text="Export GeoJSON" Command="{Binding ExportGeoJsonCommand}" Style="{StaticResource DarkButton}" />
            </HorizontalStackLayout>

            <!-- Clear Buttons -->
            <Label Text="Clear Queue" FontAttributes="Bold" Margin="0,8,0,0" />
            <HorizontalStackLayout Spacing="8">
                <Button Text="Clear Synced" Command="{Binding ClearSyncedQueueCommand}" Style="{StaticResource DarkButton}" />
                <Button Text="Clear Pending" Command="{Binding ClearPendingQueueCommand}" Style="{StaticResource DangerButton}" />
                <Button Text="Clear All" Command="{Binding ClearAllQueueCommand}" Style="{StaticResource DangerButton}" />
            </HorizontalStackLayout>

            <!-- Refresh -->
            <Button Text="Refresh" Command="{Binding RefreshCommand}"
                    IsEnabled="{Binding IsRefreshing, Converter={StaticResource InvertedBoolConverter}}"
                    HorizontalOptions="End" />
        </VerticalStackLayout>
    </syncfusion:SfExpander.Content>
</syncfusion:SfExpander>
```

### Code-Behind

Add to `SettingsPage.xaml.cs`:

```csharp
private void OfflineQueueExpander_Expanded(object sender, ExpandedAndCollapsedEventArgs e)
{
    if (e.IsExpanded && BindingContext is SettingsViewModel vm)
        vm.OfflineQueue.RefreshCommand.Execute(null);
}

private void QueueLimitEntry_Unfocused(object sender, FocusEventArgs e)
{
    if (BindingContext is SettingsViewModel vm)
        vm.OfflineQueue.ApplyQueueLimitCommand.Execute(null);
}
```

---

## 10. DiagnosticsViewModel Cleanup

Remove from `DiagnosticsViewModel`:
- `ExportQueueCommand` and `ExportQueueAsync()`
- `ClearSyncedQueueCommand` and related method
- `ClearAllQueueCommand` and related method

Keep read-only queue status display.

---

## 11. Platform Service Updates

Update Android `LocationTrackingService` to use the setting:

```csharp
var limit = Preferences.Get(SettingsService.QueueLimitMaxLocationsKey, 25000);
await _databaseService.QueueLocationAsync(location, limit, isUserInvoked);
```

---

## 12. DI Registration

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<IQueueExportService, QueueExportService>();
builder.Services.AddTransient<OfflineQueueSettingsViewModel>();
```

---

## File Changes Summary

| Action | File |
|--------|------|
| **Modify** | `ISettingsService.cs` - add QueueLimitMaxLocations |
| **Modify** | `SettingsService.cs` - implement property + constant |
| **Modify** | `ILocationQueueRepository.cs` - add 5 methods |
| **Modify** | `LocationQueueRepository.cs` - implement methods, update cleanup |
| **Modify** | `DatabaseService.cs` - add limit parameter, remove hardcoded value |
| **Create** | `QueueStatusInfo.cs` - status model |
| **Create** | `IQueueExportService.cs` - export interface |
| **Create** | `QueueExportService.cs` - export implementation |
| **Create** | `OfflineQueueSettingsViewModel.cs` - ViewModel |
| **Modify** | `AppDiagnosticService.cs` - add GetQueueStatusAsync |
| **Modify** | `SettingsViewModel.cs` - add OfflineQueue property |
| **Modify** | `SettingsPage.xaml` - add UI section |
| **Modify** | `SettingsPage.xaml.cs` - add event handlers |
| **Modify** | `DiagnosticsViewModel.cs` - remove queue actions |
| **Modify** | `MauiProgram.cs` - register services |
| **Modify** | Android `LocationTrackingService.cs` - use limit setting |

**Total: 4 new files, 12 modified files**

---

## Test Coverage

### Core Tests (~40 tests)

**Settings:**
- [ ] QueueLimitMaxLocations clamping (1, 100000, negative, overflow)
- [ ] Setting persists across app restart
- [ ] Default value is 25000

**Cleanup:**
- [ ] Cleanup respects limit parameter
- [ ] Safe entries deleted before pending
- [ ] Syncing entries never deleted
- [ ] Limit=1 keeps exactly one entry

**Export:**
- [ ] CSV format correct with all fields
- [ ] GeoJSON format valid
- [ ] Empty queue shows message
- [ ] Status mapping (rejected shown as Rejected, not Synced)
- [ ] Formula injection protection (=, +, -, @)
- [ ] Invariant culture for numbers

**ViewModel:**
- [ ] Refresh populates all properties
- [ ] Clear operations update counts
- [ ] Health status calculation correct
- [ ] Coverage span calculation correct
- [ ] Headroom shows "Over limit" when exceeded

**UI:**
- [ ] Expander triggers refresh
- [ ] Entry unfocus applies limit
- [ ] Stepper updates limit

---

## Limitations & Future Enhancements

1. **Large exports (>50K entries with notes)** may be slow or use significant memory. Add streaming export if users report issues.

2. **Concurrent access** uses simple boolean flags. If race conditions cause issues in practice, add proper synchronization.

3. **No automatic cleanup notification** - users must check Settings to see queue status.

---

## Acceptance Criteria

- [ ] Users can see queue count, limit, and usage in Settings
- [ ] Users can adjust queue limit (1-100K) and it persists
- [ ] Users can see coverage span and headroom estimate (marked approximate)
- [ ] Users can export to CSV and GeoJSON
- [ ] Users can clear pending/synced/all with confirmation
- [ ] Diagnostics is read-only for queue information
- [ ] Queue limit affects cleanup behavior in repository and DatabaseService
