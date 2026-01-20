using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for offline queue settings management.
/// </summary>
public partial class OfflineQueueSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocationQueueRepository _repository;
    private readonly IQueueExportService _exportService;
    private readonly AppDiagnosticService _diagnosticService;
    private readonly IDialogService _dialogService;
    private readonly IToastService _toastService;
    private readonly ILogger<OfflineQueueSettingsViewModel> _logger;

    /// <summary>
    /// Creates a new instance of OfflineQueueSettingsViewModel.
    /// </summary>
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

    #region Status Properties

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

    #endregion

    #region Limit Input

    [ObservableProperty] private string _queueLimitText;

    /// <summary>
    /// Gets whether to show the approximate suffix on headroom.
    /// </summary>
    public bool ShowApproxSuffix => !IsOverLimit;

    partial void OnIsOverLimitChanged(bool value) => OnPropertyChanged(nameof(ShowApproxSuffix));

    /// <summary>
    /// Gets the color for the health status text.
    /// </summary>
    public Color HealthStatusColor => HealthStatus switch
    {
        "Over Limit" => Colors.Red,
        "Critical" => Colors.OrangeRed,
        "Warning" => Colors.Orange,
        _ => Colors.Green
    };

    partial void OnHealthStatusChanged(string value) => OnPropertyChanged(nameof(HealthStatusColor));

    #endregion

    #region Commands

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
                await RefreshCoreAsync();
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

    /// <summary>
    /// Refreshes the queue status from the database.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Skip if already refreshing (prevents double-refresh from UI)
        // Internal callers should use RefreshCoreAsync directly
        if (IsRefreshing) return;
        await RefreshCoreAsync();
    }

    /// <summary>
    /// Core refresh logic without reentrancy guard.
    /// Use this from Clear commands to ensure UI updates.
    /// </summary>
    private async Task RefreshCoreAsync()
    {
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
            QueueLimitText = status.QueueLimit.ToString(); // Sync text with actual limit
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

    /// <summary>
    /// Exports the queue to CSV format.
    /// </summary>
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

    /// <summary>
    /// Exports the queue to GeoJSON format.
    /// </summary>
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

    /// <summary>
    /// Clears pending locations from the queue.
    /// </summary>
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
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing pending queue");
            await _toastService.ShowErrorAsync("Failed to clear pending locations");
            await RefreshCoreAsync(); // Show actual state after partial failure
        }
    }

    /// <summary>
    /// Clears synced and rejected locations from the queue.
    /// </summary>
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
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing synced queue");
            await _toastService.ShowErrorAsync("Failed to clear synced locations");
            await RefreshCoreAsync(); // Show actual state after partial failure
        }
    }

    /// <summary>
    /// Clears all locations from the queue.
    /// </summary>
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
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing all queue");
            await _toastService.ShowErrorAsync("Failed to clear queue");
            await RefreshCoreAsync(); // Show actual state after partial failure
        }
    }

    #endregion

    #region Helpers

    private void UpdateStorageWarning(int limit)
    {
        ShowStorageWarning = limit > 50000;
        if (ShowStorageWarning)
            StorageWarningText = "High queue limits may use significant storage. Ensure sufficient free space.";
    }

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

    #endregion
}
