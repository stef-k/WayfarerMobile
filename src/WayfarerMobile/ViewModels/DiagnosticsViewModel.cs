using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
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
    private readonly ILocationBridge _locationBridge;
    private readonly DiagnosticService _diagnosticService;
    private readonly AppDiagnosticService _appDiagnosticService;
    private readonly PerformanceMonitorService _performanceService;
    private readonly IToastService _toastService;
    private readonly DatabaseService _databaseService;
    private bool _isSubscribed;

    /// <summary>
    /// Initializes a new instance of the DiagnosticsViewModel class.
    /// </summary>
    public DiagnosticsViewModel(
        ILogger<DiagnosticsViewModel> logger,
        ILocationBridge locationBridge,
        DiagnosticService diagnosticService,
        AppDiagnosticService appDiagnosticService,
        PerformanceMonitorService performanceService,
        IToastService toastService,
        DatabaseService databaseService)
    {
        _logger = logger;
        _locationBridge = locationBridge;
        _diagnosticService = diagnosticService;
        _appDiagnosticService = appDiagnosticService;
        _performanceService = performanceService;
        _toastService = toastService;
        _databaseService = databaseService;
        Title = "Diagnostics";

        LogFiles = [];
    }

    #region Lifecycle Methods

    /// <summary>
    /// Called when the page appears. Subscribes to location events for real-time updates.
    /// </summary>
    public void OnAppearing()
    {
        if (!_isSubscribed)
        {
            _locationBridge.StateChanged += OnLocationStateChanged;
            _locationBridge.LocationReceived += OnLocationReceived;
            _isSubscribed = true;
            _logger.LogDebug("Subscribed to location bridge events");

            // Initialize from current state immediately
            var currentState = _locationBridge.CurrentState;
            IsGpsRunning = currentState == Core.Enums.TrackingState.Active;
            TrackingState = currentState.ToString();
        }
    }

    /// <summary>
    /// Called when the page disappears. Unsubscribes from location events.
    /// </summary>
    public void OnDisappearing()
    {
        if (_isSubscribed)
        {
            _locationBridge.StateChanged -= OnLocationStateChanged;
            _locationBridge.LocationReceived -= OnLocationReceived;
            _isSubscribed = false;
            _logger.LogDebug("Unsubscribed from location bridge events");
        }
    }

    /// <summary>
    /// Handles location state changes to update GPS status in real-time.
    /// </summary>
    private void OnLocationStateChanged(object? sender, TrackingState newState)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var wasRunning = IsGpsRunning;
            IsGpsRunning = newState == Core.Enums.TrackingState.Active;

            // Always update the tracking state display
            TrackingState = newState.ToString();

            if (wasRunning != IsGpsRunning)
            {
                _logger.LogDebug("GPS status changed: {OldState} -> {NewState}", wasRunning, IsGpsRunning);

                // Update overall health status
                UpdateHealthStatusFromCurrentState();
            }
        });
    }

    /// <summary>
    /// Handles new location updates to refresh tracking info.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update last location info display
            LastLocationInfo = $"{location.Latitude:F5}, {location.Longitude:F5} " +
                               $"({location.Accuracy:F0}m) at {location.Timestamp.ToLocalTime():HH:mm:ss}";

            // If GPS wasn't marked as running, update it now since we got a location
            if (!IsGpsRunning)
            {
                IsGpsRunning = true;
                UpdateHealthStatusFromCurrentState();
            }
        });
    }

    /// <summary>
    /// Updates the health status display based on current property values.
    /// </summary>
    private void UpdateHealthStatusFromCurrentState()
    {
        // Recalculate overall health based on current state
        var overallHealth = CalculateOverallHealthFromProperties();

        HealthStatus = overallHealth switch
        {
            Services.HealthStatus.Healthy => "Healthy",
            Services.HealthStatus.Warning => "Warning",
            Services.HealthStatus.Critical => "Critical",
            Services.HealthStatus.Error => "Error",
            _ => "Unknown"
        };

        HealthStatusColor = overallHealth switch
        {
            Services.HealthStatus.Healthy => Colors.Green,
            Services.HealthStatus.Warning => Colors.Orange,
            Services.HealthStatus.Critical => Colors.Red,
            Services.HealthStatus.Error => Colors.Red,
            _ => Colors.Gray
        };
    }

    /// <summary>
    /// Calculates overall health status from current property values.
    /// </summary>
    private Services.HealthStatus CalculateOverallHealthFromProperties()
    {
        // GPS permissions are critical
        if (!HasForegroundLocation)
        {
            return Services.HealthStatus.Critical;
        }

        // Background permission missing is a warning
        if (!HasBackgroundLocation)
        {
            return Services.HealthStatus.Warning;
        }

        // GPS not running is a warning
        if (!IsGpsRunning)
        {
            return Services.HealthStatus.Warning;
        }

        // Missing server config when tracking is enabled
        if (IsTrackingEnabled && !HasServerConfig)
        {
            return Services.HealthStatus.Warning;
        }

        // No network when tracking is enabled
        if (IsTrackingEnabled && !HasNetwork)
        {
            return Services.HealthStatus.Warning;
        }

        return Services.HealthStatus.Healthy;
    }

    #endregion

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
    private int _failedLocations;

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

    #region Zoom Coverage Properties

    [ObservableProperty]
    private bool _hasZoomCoverage;

    [ObservableProperty]
    private string _overallCoverage = "—";

    [ObservableProperty]
    private ObservableCollection<ZoomCoverageItem> _zoomCoverageItems = [];

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

            // Load zoom coverage (requires current location)
            var location = _locationBridge.LastLocation;
            if (location != null)
            {
                var coverage = await _appDiagnosticService.GetCacheCoverageAsync(
                    location.Latitude, location.Longitude);
                UpdateZoomCoverage(coverage);
            }
            else
            {
                HasZoomCoverage = false;
            }

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
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error loading diagnostic data");
            await _toastService.ShowErrorAsync("Database error loading diagnostics");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File I/O error loading diagnostic data");
            await _toastService.ShowErrorAsync("Failed to read diagnostic files");
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
        catch (IOException ex)
        {
            _logger.LogError(ex, "File I/O error sharing diagnostic report");
            await _toastService.ShowErrorAsync("Failed to write report file");
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported on this device");
            await _toastService.ShowErrorAsync("Sharing not available on this device");
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
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Clipboard feature not supported on this device");
            await _toastService.ShowErrorAsync("Clipboard not available on this device");
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
        catch (IOException ex)
        {
            _logger.LogError(ex, "File I/O error refreshing logs");
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
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported for log file: {FileName}", logFile.FileName);
            await _toastService.ShowErrorAsync("Sharing not available on this device");
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
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, "Database error clearing synced queue");
                await _toastService.ShowErrorAsync("Database error clearing synced locations");
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
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, "Database error clearing all queue");
                await _toastService.ShowErrorAsync("Database error clearing queue");
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
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error exporting queue");
            await _toastService.ShowErrorAsync("Database error exporting queue");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "File I/O error exporting queue");
            await _toastService.ShowErrorAsync("Failed to write export file");
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported for export");
            await _toastService.ShowErrorAsync("Sharing not available on this device");
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
        FailedLocations = diag.FailedCount;

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

    private void UpdateZoomCoverage(CacheCoverageInfo? info)
    {
        if (info == null || info.CoverageByZoom.Count == 0)
        {
            HasZoomCoverage = false;
            return;
        }

        HasZoomCoverage = true;
        OverallCoverage = $"{info.OverallCoveragePercent:F0}%";

        ZoomCoverageItems.Clear();
        foreach (var (zoom, coverage) in info.CoverageByZoom.OrderBy(kv => kv.Key))
        {
            ZoomCoverageItems.Add(new ZoomCoverageItem
            {
                ZoomLevel = zoom,
                Coverage = $"{coverage.CoveragePercent:F0}%",
                Tiles = $"{coverage.CachedTiles}/{coverage.TotalTiles}"
            });
        }
    }

    #endregion
}

/// <summary>
/// Display model for zoom level cache coverage in diagnostics.
/// </summary>
public partial class ZoomCoverageItem : ObservableObject
{
    /// <summary>
    /// The zoom level (8-17).
    /// </summary>
    [ObservableProperty]
    private int _zoomLevel;

    /// <summary>
    /// Coverage percentage formatted as string (e.g., "85%").
    /// </summary>
    [ObservableProperty]
    private string _coverage = "0%";

    /// <summary>
    /// Tile counts formatted as string (e.g., "95/121").
    /// </summary>
    [ObservableProperty]
    private string _tiles = "0/0";
}
