using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the diagnostics page.
/// Provides comprehensive app diagnostics including location queue, tile cache, and tracking status.
/// </summary>
public partial class DiagnosticsViewModel : BaseViewModel
{
    private readonly ILogger<DiagnosticsViewModel> _logger;
    private readonly DiagnosticService _diagnosticService;
    private readonly AppDiagnosticService _appDiagnosticService;
    private readonly PerformanceMonitorService _performanceService;
    private readonly IToastService _toastService;
    private readonly DatabaseService _databaseService;

    /// <summary>
    /// Initializes a new instance of the DiagnosticsViewModel class.
    /// </summary>
    public DiagnosticsViewModel(
        ILogger<DiagnosticsViewModel> logger,
        DiagnosticService diagnosticService,
        AppDiagnosticService appDiagnosticService,
        PerformanceMonitorService performanceService,
        IToastService toastService,
        DatabaseService databaseService)
    {
        _logger = logger;
        _diagnosticService = diagnosticService;
        _appDiagnosticService = appDiagnosticService;
        _performanceService = performanceService;
        _toastService = toastService;
        _databaseService = databaseService;
        Title = "Diagnostics";

        LogFiles = [];
    }

    #region Health Check Properties

    [ObservableProperty]
    private string _healthStatus = "Unknown";

    [ObservableProperty]
    private Color _healthStatusColor = Colors.Gray;

    [ObservableProperty]
    private bool _hasForegroundLocation;

    [ObservableProperty]
    private bool _hasBackgroundLocation;

    [ObservableProperty]
    private bool _isGpsRunning;

    [ObservableProperty]
    private bool _isTrackingEnabled;

    [ObservableProperty]
    private bool _hasServerConfig;

    [ObservableProperty]
    private bool _hasNetwork;

    #endregion

    #region Location Queue Properties

    [ObservableProperty]
    private string _queueHealthStatus = "Unknown";

    [ObservableProperty]
    private int _pendingLocations;

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

    #endregion

    #region Tile Cache Properties

    [ObservableProperty]
    private string _cacheHealthStatus = "Unknown";

    [ObservableProperty]
    private int _liveTileCount;

    [ObservableProperty]
    private string _liveCacheSize = "0 MB";

    [ObservableProperty]
    private string _liveCacheUsage = "0 MB / 0 MB";

    [ObservableProperty]
    private double _liveCacheUsagePercent;

    [ObservableProperty]
    private int _liveCacheMaxSizeMB;

    [ObservableProperty]
    private int _tripTileCount;

    [ObservableProperty]
    private string _tripCacheSize = "0 MB";

    [ObservableProperty]
    private string _tripCacheUsage = "0 MB / 0 MB";

    [ObservableProperty]
    private int _downloadedTripCount;

    [ObservableProperty]
    private string _totalCacheSize = "0 MB / 0 MB";

    #endregion

    #region Tracking Properties

    [ObservableProperty]
    private string _trackingState = "Unknown";

    [ObservableProperty]
    private string _performanceMode = "Unknown";

    [ObservableProperty]
    private string _lastLocationInfo = "No location";

    [ObservableProperty]
    private string _trackingThresholds = "N/A";

    #endregion

    #region Navigation Properties

    [ObservableProperty]
    private bool _hasCachedRoute;

    [ObservableProperty]
    private string _cachedRouteInfo = "No cached route";

    #endregion

    #region System Properties

    [ObservableProperty]
    private string _platform = string.Empty;

    [ObservableProperty]
    private string _osVersion = string.Empty;

    [ObservableProperty]
    private string _deviceInfo = string.Empty;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _batteryStatus = string.Empty;

    [ObservableProperty]
    private bool _isEnergySaverOn;

    [ObservableProperty]
    private string _memoryUsage = string.Empty;

    [ObservableProperty]
    private long _totalOperations;

    [ObservableProperty]
    private string _sessionDuration = string.Empty;

    [ObservableProperty]
    private string _gcCollections = string.Empty;

    #endregion

    #region Log Properties

    [ObservableProperty]
    private string _recentLogs = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LogFileInfo> _logFiles;

    [ObservableProperty]
    private bool _isLoading;

    #endregion

    #region Commands

    /// <summary>
    /// Loads all diagnostic data.
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;

            // Load all diagnostics in parallel
            var healthTask = _diagnosticService.RunHealthCheckAsync();
            var queueTask = _appDiagnosticService.GetLocationQueueDiagnosticsAsync();
            var cacheTask = _appDiagnosticService.GetTileCacheDiagnosticsAsync();
            var trackingTask = _appDiagnosticService.GetTrackingDiagnosticsAsync();
            var navTask = _appDiagnosticService.GetNavigationDiagnosticsAsync();

            await Task.WhenAll(healthTask, queueTask, cacheTask, trackingTask, navTask);

            // Update UI
            UpdateHealthStatus(await healthTask);
            UpdateLocationQueue(await queueTask);
            UpdateTileCache(await cacheTask);
            UpdateTracking(await trackingTask);
            UpdateNavigation(await navTask);

            // Load queue details
            await LoadQueueDetailsAsync();

            // System info
            var systemInfo = _diagnosticService.GetSystemInfo();
            UpdateSystemInfo(systemInfo);

            // Performance
            UpdatePerformanceMetrics();

            // Logs
            var logFiles = _diagnosticService.GetLogFiles();
            LogFiles.Clear();
            foreach (var file in logFiles)
            {
                LogFiles.Add(file);
            }
            RecentLogs = await _diagnosticService.GetRecentLogsAsync(50);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading diagnostic data");
            await _toastService.ShowErrorAsync("Failed to load diagnostic data");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Generates and shares the full diagnostic report.
    /// </summary>
    [RelayCommand]
    private async Task ShareReportAsync()
    {
        try
        {
            IsLoading = true;

            // Generate both reports
            var systemReport = await _diagnosticService.GenerateDiagnosticReportAsync();
            var appReport = await _appDiagnosticService.GenerateFullReportAsync();

            var fullReport = systemReport + "\n\n" + appReport;

            var fileName = $"wayfarer-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var filePath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(filePath, fullReport);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Share Diagnostic Report",
                File = new ShareFile(filePath)
            });

            await _toastService.ShowSuccessAsync("Report generated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing diagnostic report");
            await _toastService.ShowErrorAsync("Failed to share report");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Copies the diagnostic report to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyReportAsync()
    {
        try
        {
            IsLoading = true;

            var systemReport = await _diagnosticService.GenerateDiagnosticReportAsync();
            var appReport = await _appDiagnosticService.GenerateFullReportAsync();
            var fullReport = systemReport + "\n\n" + appReport;

            await Clipboard.Default.SetTextAsync(fullReport);
            await _toastService.ShowSuccessAsync("Report copied to clipboard");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying diagnostic report");
            await _toastService.ShowErrorAsync("Failed to copy report");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes log content.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        try
        {
            RecentLogs = await _diagnosticService.GetRecentLogsAsync(100);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing logs");
        }
    }

    /// <summary>
    /// Shares an individual log file.
    /// </summary>
    /// <param name="logFile">The log file to share.</param>
    [RelayCommand]
    private async Task ShareLogFileAsync(LogFileInfo? logFile)
    {
        if (logFile == null || string.IsNullOrEmpty(logFile.FilePath))
        {
            return;
        }

        try
        {
            if (!File.Exists(logFile.FilePath))
            {
                await _toastService.ShowErrorAsync("Log file not found");
                return;
            }

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Share {logFile.FileName}",
                File = new ShareFile(logFile.FilePath)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing log file: {FileName}", logFile.FileName);
            await _toastService.ShowErrorAsync("Failed to share log file");
        }
    }

    /// <summary>
    /// Clears synced locations from the queue.
    /// </summary>
    [RelayCommand]
    private async Task ClearSyncedAsync()
    {
        if (SyncedLocations == 0)
        {
            await _toastService.ShowAsync("No synced locations to clear");
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Clear Synced Locations",
            $"This will delete {SyncedLocations} synced locations. These have already been sent to the server.",
            "Clear",
            "Cancel");

        if (confirm)
        {
            try
            {
                var deleted = await _databaseService.ClearSyncedQueueAsync();
                await _toastService.ShowSuccessAsync($"{deleted} synced locations cleared");
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing synced queue");
                await _toastService.ShowErrorAsync("Failed to clear synced locations");
            }
        }
    }

    /// <summary>
    /// Clears all locations from the queue with a destructive warning.
    /// </summary>
    [RelayCommand]
    private async Task ClearAllQueueAsync()
    {
        var total = PendingLocations + SyncedLocations + RejectedLocations;
        if (total == 0)
        {
            await _toastService.ShowAsync("Queue is already empty");
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "⚠️ Clear All Locations",
            $"This will permanently delete ALL {total} locations including {PendingLocations} pending locations that have NOT been synced to the server.\n\nThis action cannot be undone.",
            "Delete All",
            "Cancel");

        if (confirm)
        {
            try
            {
                var deleted = await _databaseService.ClearAllQueueAsync();
                await _toastService.ShowSuccessAsync($"{deleted} locations cleared");
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all queue");
                await _toastService.ShowErrorAsync("Failed to clear queue");
            }
        }
    }

    /// <summary>
    /// Exports all queue data to CSV and shares it.
    /// </summary>
    [RelayCommand]
    private async Task ExportQueueAsync()
    {
        try
        {
            var locations = await _databaseService.GetAllQueuedLocationsAsync();

            if (locations.Count == 0)
            {
                await _toastService.ShowAsync("No locations to export");
                return;
            }

            var csv = new StringBuilder();
            csv.AppendLine("Id,Timestamp,Latitude,Longitude,Altitude,Accuracy,Speed,Bearing,Provider,SyncStatus,SyncAttempts,LastSyncAttempt,IsRejected,RejectionReason,LastError,Notes");

            foreach (var loc in locations)
            {
                var status = loc.SyncStatus switch
                {
                    Core.Enums.SyncStatus.Pending => loc.IsRejected ? "Rejected" :
                                         loc.SyncAttempts > 0 ? $"Retrying({loc.SyncAttempts})" : "Pending",
                    Core.Enums.SyncStatus.Synced => "Synced",
                    Core.Enums.SyncStatus.Failed => "Failed", // Legacy status
                    _ => "Unknown"
                };

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                csv.AppendLine(
                    $"{loc.Id}," +
                    $"{loc.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                    $"{loc.Latitude.ToString("F6", inv)}," +
                    $"{loc.Longitude.ToString("F6", inv)}," +
                    $"{loc.Altitude?.ToString("F1", inv) ?? ""}," +
                    $"{loc.Accuracy?.ToString("F1", inv) ?? ""}," +
                    $"{loc.Speed?.ToString("F1", inv) ?? ""}," +
                    $"{loc.Bearing?.ToString("F1", inv) ?? ""}," +
                    $"\"{loc.Provider ?? ""}\"," +
                    $"{status}," +
                    $"{loc.SyncAttempts}," +
                    $"{(loc.LastSyncAttempt.HasValue ? loc.LastSyncAttempt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "")}," +
                    $"{loc.IsRejected}," +
                    $"\"{loc.RejectionReason?.Replace("\"", "\"\"") ?? ""}\"," +
                    $"\"{loc.LastError?.Replace("\"", "\"\"") ?? ""}\"," +
                    $"\"{loc.Notes?.Replace("\"", "\"\"") ?? ""}\"");
            }

            var fileName = $"wayfarer_locations_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(tempPath, csv.ToString());

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Location Queue",
                File = new ShareFile(tempPath)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting queue");
            await _toastService.ShowErrorAsync("Failed to export queue");
        }
    }

    /// <summary>
    /// Refreshes the location queue data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshQueueAsync()
    {
        try
        {
            var queueDiag = await _appDiagnosticService.GetLocationQueueDiagnosticsAsync();
            UpdateLocationQueue(queueDiag);
            await LoadQueueDetailsAsync();
            await _toastService.ShowSuccessAsync("Queue refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing queue");
            await _toastService.ShowErrorAsync("Failed to refresh queue");
        }
    }

    /// <summary>
    /// Loads recent queue entries for display.
    /// </summary>
    private async Task LoadQueueDetailsAsync()
    {
        try
        {
            var locations = await _databaseService.GetAllQueuedLocationsAsync();

            if (locations.Count == 0)
            {
                QueueDetails = "Queue is empty";
                return;
            }

            // Take most recent 50 entries, ordered by timestamp descending
            var recentLocations = locations
                .OrderByDescending(l => l.Timestamp)
                .Take(50)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Showing {recentLocations.Count} of {locations.Count} entries (newest first)");
            sb.AppendLine(new string('-', 60));

            foreach (var loc in recentLocations)
            {
                var status = loc.SyncStatus switch
                {
                    Core.Enums.SyncStatus.Pending => loc.IsRejected ? "REJECTED" :
                                         loc.SyncAttempts > 0 ? $"RETRY({loc.SyncAttempts})" : "PENDING",
                    Core.Enums.SyncStatus.Synced => "SYNCED",
                    Core.Enums.SyncStatus.Failed => "FAILED", // Legacy status
                    _ => "?"
                };

                var inv = System.Globalization.CultureInfo.InvariantCulture;
                sb.AppendLine($"[{loc.Timestamp:HH:mm:ss}] {status}");
                sb.AppendLine($"  Loc: {loc.Latitude.ToString("F5", inv)}, {loc.Longitude.ToString("F5", inv)}");

                if (loc.Accuracy.HasValue)
                    sb.Append($"  Acc: {loc.Accuracy.Value.ToString("F0", inv)}m");
                if (loc.Speed.HasValue)
                    sb.Append($"  Spd: {loc.Speed.Value.ToString("F1", inv)}m/s");
                if (loc.Accuracy.HasValue || loc.Speed.HasValue)
                    sb.AppendLine();

                if (!string.IsNullOrEmpty(loc.LastError))
                    sb.AppendLine($"  Err: {loc.LastError}");

                sb.AppendLine();
            }

            QueueDetails = sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading queue details");
            QueueDetails = $"Error loading queue details: {ex.Message}";
        }
    }

    #endregion

    #region Update Methods

    private void UpdateHealthStatus(HealthCheckResult result)
    {
        HasForegroundLocation = result.HasForegroundLocationPermission;
        HasBackgroundLocation = result.HasBackgroundLocationPermission;
        IsGpsRunning = result.IsGpsRunning;
        IsTrackingEnabled = result.IsTrackingEnabled;
        HasServerConfig = result.HasServerConfig;
        HasNetwork = result.HasNetworkConnectivity;

        HealthStatus = result.OverallHealth switch
        {
            Services.HealthStatus.Healthy => "Healthy",
            Services.HealthStatus.Warning => "Warning",
            Services.HealthStatus.Critical => "Critical",
            Services.HealthStatus.Error => "Error",
            _ => "Unknown"
        };

        HealthStatusColor = result.OverallHealth switch
        {
            Services.HealthStatus.Healthy => Colors.Green,
            Services.HealthStatus.Warning => Colors.Orange,
            Services.HealthStatus.Critical => Colors.Red,
            Services.HealthStatus.Error => Colors.Red,
            _ => Colors.Gray
        };
    }

    private void UpdateLocationQueue(LocationQueueDiagnostics diag)
    {
        QueueHealthStatus = diag.QueueHealthStatus;
        PendingLocations = diag.PendingCount;
        SyncedLocations = diag.SyncedCount;
        RejectedLocations = diag.RejectedCount;

        if (diag.OldestPendingTimestamp.HasValue)
        {
            var age = DateTime.UtcNow - diag.OldestPendingTimestamp.Value;
            OldestPendingAge = age.TotalHours >= 1
                ? $"{age.TotalHours:F1} hours"
                : $"{age.TotalMinutes:F0} min";
        }
        else
        {
            OldestPendingAge = "N/A";
        }

        LastSyncTime = diag.LastSyncedTimestamp?.ToLocalTime().ToString("g") ?? "Never";
    }

    private void UpdateTileCache(TileCacheDiagnostics diag)
    {
        CacheHealthStatus = diag.CacheHealthStatus;
        LiveTileCount = diag.LiveCacheTileCount;
        LiveCacheSize = $"{diag.LiveCacheSizeMB:F1} MB";
        LiveCacheMaxSizeMB = diag.LiveCacheMaxSizeMB;
        LiveCacheUsage = $"{diag.LiveCacheSizeMB:F0} MB / {diag.LiveCacheMaxSizeMB} MB";
        LiveCacheUsagePercent = diag.LiveCacheUsagePercent;
        TripTileCount = diag.TripCacheTileCount;
        TripCacheSize = $"{diag.TripCacheSizeMB:F1} MB";
        TripCacheUsage = $"{diag.TripCacheSizeMB:F0} MB / {diag.TripCacheMaxSizeMB} MB";
        DownloadedTripCount = diag.DownloadedTripCount;
        var totalMaxMB = diag.LiveCacheMaxSizeMB + diag.TripCacheMaxSizeMB;
        TotalCacheSize = $"{diag.TotalCacheSizeMB:F0} MB / {totalMaxMB} MB";
    }

    private void UpdateTracking(TrackingDiagnostics diag)
    {
        TrackingState = diag.TrackingState;
        PerformanceMode = diag.PerformanceMode;
        TrackingThresholds = $"{diag.TimeThresholdMinutes} min / {diag.DistanceThresholdMeters} m";

        if (diag.LastLocationTimestamp.HasValue)
        {
            LastLocationInfo = $"{diag.LastLocationLatitude:F5}, {diag.LastLocationLongitude:F5} " +
                               $"({diag.LastLocationAccuracy:F0}m) at {diag.LastLocationTimestamp.Value.LocalDateTime:HH:mm:ss}";
        }
        else
        {
            LastLocationInfo = "No location";
        }
    }

    private void UpdateNavigation(NavigationDiagnostics diag)
    {
        HasCachedRoute = diag.HasCachedRoute;

        if (diag.HasCachedRoute)
        {
            CachedRouteInfo = $"{diag.CachedRouteDestination} - {diag.CachedRouteWaypointCount} waypoints, " +
                              $"{diag.CachedRouteDistance:F0}m, " +
                              $"age: {diag.CacheAgeSeconds:F0}s, valid: {diag.IsCacheValid}";
        }
        else
        {
            CachedRouteInfo = "No cached route";
        }
    }

    private void UpdateSystemInfo(SystemInfo info)
    {
        Platform = info.Platform;
        OsVersion = info.OsVersion;
        DeviceInfo = $"{info.DeviceManufacturer} {info.DeviceModel}";
        AppVersion = $"{info.AppVersion} ({info.AppBuild})";

        // Battery info may be unavailable on some devices
        if (info.BatteryLevel >= 0)
        {
            BatteryStatus = $"{info.BatteryLevel:P0} - {info.BatteryState} ({info.PowerSource})";
            IsEnergySaverOn = info.IsEnergySaver;
        }
        else
        {
            BatteryStatus = "Not available";
            IsEnergySaverOn = false;
        }
    }

    private void UpdatePerformanceMetrics()
    {
        var memoryInfo = _performanceService.GetMemoryInfo();
        MemoryUsage = $"{memoryInfo.GcTotalMemory / (1024.0 * 1024.0):N1} MB";
        TotalOperations = _performanceService.TotalOperations;
        SessionDuration = _performanceService.SessionDuration.ToString(@"hh\:mm\:ss");
        GcCollections = $"Gen0: {memoryInfo.Gen0Collections}, Gen1: {memoryInfo.Gen1Collections}, Gen2: {memoryInfo.Gen2Collections}";
    }

    #endregion
}
