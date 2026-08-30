using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>Owns read-only location-queue diagnostics presentation.</summary>
public partial class DiagnosticsViewModel
{
    [ObservableProperty]
    private string _queueHealthStatus = "Unknown";

    [ObservableProperty]
    private int _pendingLocations;

    [ObservableProperty]
    private int _retryingLocations;

    [ObservableProperty]
    private int _syncedLocations;

    [ObservableProperty]
    private int _rejectedLocations;

    [ObservableProperty]
    private string _oldestPendingAge = "N/A";

    [ObservableProperty]
    private string _lastSyncTime = "Never";

    [ObservableProperty]
    private string _queueDetails = "No queue data";

    [RelayCommand]
    private async Task RefreshQueueAsync()
    {
        try
        {
            var queueDiagnostics = await _appDiagnosticService.GetLocationQueueDiagnosticsAsync();
            UpdateLocationQueue(queueDiagnostics);
            await LoadQueueDetailsAsync();
            await _toastService.ShowSuccessAsync("Queue refreshed");
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error refreshing queue");
            await _toastService.ShowErrorAsync("Database error refreshing queue");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing queue");
            await _toastService.ShowErrorAsync("Failed to refresh queue");
        }
    }

    private async Task LoadQueueDetailsAsync()
    {
        try
        {
            var locations = await _locationQueueRepository.GetAllQueuedLocationsAsync();
            if (locations.Count == 0)
            {
                QueueDetails = "Queue is empty";
                return;
            }

            var recentLocations = locations.OrderByDescending(location => location.Timestamp).Take(50).ToList();
            var details = new StringBuilder();
            details.AppendLine($"Showing {recentLocations.Count} of {locations.Count} entries (newest first)");
            details.AppendLine(new string('-', 60));

            foreach (var location in recentLocations)
            {
                AppendQueueLocation(details, location);
            }

            QueueDetails = details.ToString();
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error loading queue details");
            QueueDetails = "Database error loading queue details";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading queue details");
            QueueDetails = $"Error loading queue details: {ex.Message}";
        }
    }

    private static void AppendQueueLocation(StringBuilder details, Data.Entities.QueuedLocation location)
    {
        var status = location.SyncStatus switch
        {
            Core.Enums.SyncStatus.Pending => location.IsRejected ? "REJECTED" :
                location.SyncAttempts > 0 ? $"RETRY({location.SyncAttempts})" : "PENDING",
            Core.Enums.SyncStatus.Syncing => "SYNCING",
            Core.Enums.SyncStatus.Synced => "SYNCED",
            _ => "?"
        };
        var userTag = location.IsUserInvoked ? " [USER]" : "";
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        details.AppendLine($"[{location.Timestamp:HH:mm:ss}] {status}{userTag}");
        details.AppendLine($"  Loc: {location.Latitude.ToString("F5", invariant)}, {location.Longitude.ToString("F5", invariant)}");
        if (location.Accuracy.HasValue)
            details.Append($"  Acc: {location.Accuracy.Value.ToString("F0", invariant)}m");
        if (location.Speed.HasValue)
            details.Append($"  Spd: {location.Speed.Value.ToString("F1", invariant)}m/s");
        if (location.Accuracy.HasValue || location.Speed.HasValue)
            details.AppendLine();
        if (!string.IsNullOrEmpty(location.CheckInNotes))
            details.AppendLine($"  Notes: {location.CheckInNotes}");
        if (!string.IsNullOrEmpty(location.LastError))
            details.AppendLine($"  Err: {location.LastError}");
        details.AppendLine();
    }

    private void UpdateLocationQueue(LocationQueueDiagnostics diagnostics)
    {
        QueueHealthStatus = diagnostics.QueueHealthStatus;
        PendingLocations = diagnostics.PendingCount;
        RetryingLocations = diagnostics.RetryingCount;
        SyncedLocations = diagnostics.SyncedCount;
        RejectedLocations = diagnostics.RejectedCount;
        OldestPendingAge = FormatAge(diagnostics.OldestPendingTimestamp);
        LastSyncTime = diagnostics.LastSyncedTimestamp?.ToLocalTime().ToString("g") ?? "Never";
    }

    private static string FormatAge(DateTime? timestamp)
    {
        if (!timestamp.HasValue) return "N/A";
        var age = DateTime.UtcNow - timestamp.Value;
        return age.TotalHours >= 1 ? $"{age.TotalHours:F1} hours" : $"{age.TotalMinutes:F0} min";
    }
}
