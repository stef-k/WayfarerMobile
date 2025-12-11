using System.Text;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Diagnostic service to help troubleshoot background tracking and system issues.
/// Provides comprehensive status reporting and health checks.
/// </summary>
public class DiagnosticService
{
    private readonly ILogger<DiagnosticService> _logger;
    private readonly ILocationBridge _locationBridge;
    private readonly SettingsService _settingsService;
    private readonly IPermissionsService _permissionsService;

    /// <summary>
    /// Initializes a new instance of the DiagnosticService class.
    /// </summary>
    public DiagnosticService(
        ILogger<DiagnosticService> logger,
        ILocationBridge locationBridge,
        SettingsService settingsService,
        IPermissionsService permissionsService)
    {
        _logger = logger;
        _locationBridge = locationBridge;
        _settingsService = settingsService;
        _permissionsService = permissionsService;
    }

    /// <summary>
    /// Generate comprehensive diagnostic report.
    /// </summary>
    /// <returns>Diagnostic report as text.</returns>
    public async Task<string> GenerateDiagnosticReportAsync()
    {
        var report = new StringBuilder();

        try
        {
            report.AppendLine("WAYFARER DIAGNOSTIC REPORT");
            report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine(new string('=', 50));

            // 1. Health Check Summary
            var healthCheck = await RunHealthCheckAsync();
            AppendHealthCheckSummary(report, healthCheck);

            // 2. Permission Status
            await AppendPermissionStatus(report);

            // 3. Location Services Status
            AppendLocationServicesStatus(report);

            // 4. Server Configuration
            AppendServerConfigurationStatus(report);

            // 5. System Information
            AppendSystemInformation(report);

            // 6. App Settings
            AppendAppSettingsStatus(report);

            // 7. Log File Information
            AppendLogFileInfo(report);

            report.AppendLine(new string('=', 50));
            report.AppendLine("End of diagnostic report");

            _logger.LogInformation("Diagnostic report generated successfully");
            return report.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating diagnostic report");
            return $"Error generating diagnostic report: {ex.Message}";
        }
    }

    /// <summary>
    /// Run automated health check and return status.
    /// </summary>
    /// <returns>Health check result.</returns>
    public async Task<HealthCheckResult> RunHealthCheckAsync()
    {
        try
        {
            var result = new HealthCheckResult();

            // Check permissions
            result.HasForegroundLocationPermission = await _permissionsService.IsLocationPermissionGrantedAsync();
            result.HasBackgroundLocationPermission = await _permissionsService.IsBackgroundLocationPermissionGrantedAsync();

            // Check GPS status
            result.IsGpsRunning = _locationBridge.CurrentState == Core.Enums.TrackingState.Active;

            // Check tracking settings
            result.IsTrackingEnabled = _settingsService.TimelineTrackingEnabled;

            // Check server configuration
            result.HasServerConfig = !string.IsNullOrEmpty(_settingsService.ServerUrl) &&
                                     !string.IsNullOrEmpty(_settingsService.ApiToken);

            // Check network connectivity
            result.HasNetworkConnectivity = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            // Calculate overall health
            result.OverallHealth = CalculateOverallHealth(result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running health check");
            return new HealthCheckResult { OverallHealth = HealthStatus.Error };
        }
    }

    /// <summary>
    /// Gets system information.
    /// </summary>
    /// <returns>System information object.</returns>
    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            Platform = DeviceInfo.Platform.ToString(),
            OsVersion = DeviceInfo.VersionString,
            DeviceModel = DeviceInfo.Model,
            DeviceManufacturer = DeviceInfo.Manufacturer,
            DeviceType = DeviceInfo.DeviceType.ToString(),
            AppVersion = AppInfo.Current.VersionString,
            AppBuild = AppInfo.Current.BuildString,
            BatteryLevel = Battery.Default.ChargeLevel,
            BatteryState = Battery.Default.State.ToString(),
            PowerSource = Battery.Default.PowerSource.ToString(),
            IsEnergySaver = Battery.Default.EnergySaverStatus == EnergySaverStatus.On
        };
    }

    /// <summary>
    /// Gets the path to the log directory.
    /// </summary>
    /// <returns>Log directory path.</returns>
    public string GetLogDirectoryPath()
    {
        return Path.Combine(FileSystem.AppDataDirectory, "logs");
    }

    /// <summary>
    /// Gets the list of log files.
    /// </summary>
    /// <returns>List of log file information.</returns>
    public List<LogFileInfo> GetLogFiles()
    {
        var logDirectory = GetLogDirectoryPath();
        var logFiles = new List<LogFileInfo>();

        if (Directory.Exists(logDirectory))
        {
            foreach (var file in Directory.GetFiles(logDirectory, "*.log").OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                var fileInfo = new FileInfo(file);
                logFiles.Add(new LogFileInfo
                {
                    FileName = fileInfo.Name,
                    FilePath = fileInfo.FullName,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                });
            }
        }

        return logFiles;
    }

    /// <summary>
    /// Reads the last N lines from the current log file.
    /// </summary>
    /// <param name="lineCount">Number of lines to read.</param>
    /// <returns>Log content.</returns>
    public async Task<string> GetRecentLogsAsync(int lineCount = 100)
    {
        try
        {
            var logFiles = GetLogFiles();
            if (logFiles.Count == 0)
            {
                return "No log files found.";
            }

            var currentLog = logFiles.First();
            var lines = await File.ReadAllLinesAsync(currentLog.FilePath);
            var recentLines = lines.TakeLast(lineCount);
            return string.Join(Environment.NewLine, recentLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading recent logs");
            return $"Error reading logs: {ex.Message}";
        }
    }

    private void AppendHealthCheckSummary(StringBuilder report, HealthCheckResult result)
    {
        report.AppendLine("\nHEALTH CHECK SUMMARY:");
        report.AppendLine($"   Overall Status: {GetHealthStatusText(result.OverallHealth)}");
        report.AppendLine($"   Foreground Location: {(result.HasForegroundLocationPermission ? "OK" : "MISSING")}");
        report.AppendLine($"   Background Location: {(result.HasBackgroundLocationPermission ? "OK" : "MISSING")}");
        report.AppendLine($"   GPS Running: {(result.IsGpsRunning ? "YES" : "NO")}");
        report.AppendLine($"   Tracking Enabled: {(result.IsTrackingEnabled ? "YES" : "NO")}");
        report.AppendLine($"   Server Configured: {(result.HasServerConfig ? "YES" : "NO")}");
        report.AppendLine($"   Network Available: {(result.HasNetworkConnectivity ? "YES" : "NO")}");
    }

    private async Task AppendPermissionStatus(StringBuilder report)
    {
        report.AppendLine("\nPERMISSION STATUS:");

        try
        {
            var foreground = await _permissionsService.IsLocationPermissionGrantedAsync();
            var background = await _permissionsService.IsBackgroundLocationPermissionGrantedAsync();

            report.AppendLine($"   Foreground Location: {(foreground ? "Granted" : "Denied")}");
            report.AppendLine($"   Background Location: {(background ? "Granted" : "Denied")}");

#if ANDROID
            if (DeviceInfo.Version.Major >= 13)
            {
                report.AppendLine("   POST_NOTIFICATIONS: Check Android 13+ notification settings");
            }
#endif
        }
        catch (Exception ex)
        {
            report.AppendLine($"   Error checking permissions: {ex.Message}");
        }
    }

    private void AppendLocationServicesStatus(StringBuilder report)
    {
        report.AppendLine("\nLOCATION SERVICES:");
        report.AppendLine($"   Tracking State: {_locationBridge.CurrentState}");
        report.AppendLine($"   Performance Mode: {_locationBridge.CurrentMode}");

        var lastLocation = _locationBridge.LastLocation;
        if (lastLocation != null)
        {
            report.AppendLine($"   Last Location: {lastLocation.Latitude:F6}, {lastLocation.Longitude:F6}");
            report.AppendLine($"   Last Location Time: {lastLocation.Timestamp:HH:mm:ss}");
            report.AppendLine($"   Accuracy: {lastLocation.Accuracy:F1}m");
        }
        else
        {
            report.AppendLine("   Last Location: None");
        }
    }

    private void AppendServerConfigurationStatus(StringBuilder report)
    {
        report.AppendLine("\nSERVER CONFIGURATION:");

        var serverUrl = _settingsService.ServerUrl;
        var apiToken = _settingsService.ApiToken;

        report.AppendLine($"   Server URL: {(string.IsNullOrEmpty(serverUrl) ? "Not configured" : "Configured")}");
        report.AppendLine($"   API Token: {(string.IsNullOrEmpty(apiToken) ? "Not configured" : "Configured")}");

        if (!string.IsNullOrEmpty(serverUrl))
        {
            report.AppendLine($"   URL: {serverUrl}");
        }

        if (!string.IsNullOrEmpty(apiToken) && apiToken.Length > 8)
        {
            report.AppendLine($"   Token: {apiToken[..8]}...");
        }
    }

    private void AppendSystemInformation(StringBuilder report)
    {
        var info = GetSystemInfo();
        report.AppendLine("\nSYSTEM INFORMATION:");
        report.AppendLine($"   Platform: {info.Platform}");
        report.AppendLine($"   OS Version: {info.OsVersion}");
        report.AppendLine($"   Device: {info.DeviceManufacturer} {info.DeviceModel}");
        report.AppendLine($"   Device Type: {info.DeviceType}");
        report.AppendLine($"   App Version: {info.AppVersion} ({info.AppBuild})");
        report.AppendLine($"   Battery: {info.BatteryLevel:P0} ({info.BatteryState})");
        report.AppendLine($"   Power Source: {info.PowerSource}");
        report.AppendLine($"   Energy Saver: {(info.IsEnergySaver ? "ON" : "OFF")}");
    }

    private void AppendAppSettingsStatus(StringBuilder report)
    {
        report.AppendLine("\nAPP SETTINGS:");
        report.AppendLine($"   Timeline Tracking: {(_settingsService.TimelineTrackingEnabled ? "Enabled" : "Disabled")}");
        report.AppendLine($"   Time Threshold: {_settingsService.LocationTimeThresholdMinutes} min");
        report.AppendLine($"   Distance Threshold: {_settingsService.LocationDistanceThresholdMeters} m");
        report.AppendLine($"   Offline Cache: {(_settingsService.MapOfflineCacheEnabled ? "Enabled" : "Disabled")}");
        report.AppendLine($"   Dark Mode: {(_settingsService.DarkModeEnabled ? "Enabled" : "Disabled")}");
    }

    private void AppendLogFileInfo(StringBuilder report)
    {
        report.AppendLine("\nLOG FILES:");

        var logFiles = GetLogFiles();
        if (logFiles.Count == 0)
        {
            report.AppendLine("   No log files found");
            return;
        }

        foreach (var file in logFiles.Take(5))
        {
            report.AppendLine($"   {file.FileName} - {file.Size / 1024.0:F1} KB - {file.LastModified:yyyy-MM-dd HH:mm}");
        }
    }

    private static string GetHealthStatusText(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "HEALTHY",
        HealthStatus.Warning => "WARNING",
        HealthStatus.Critical => "CRITICAL",
        HealthStatus.Error => "ERROR",
        _ => "UNKNOWN"
    };

    private static HealthStatus CalculateOverallHealth(HealthCheckResult result)
    {
        // GPS permissions are critical
        if (!result.HasForegroundLocationPermission)
        {
            return HealthStatus.Critical;
        }

        // Background permission missing is a warning
        if (!result.HasBackgroundLocationPermission)
        {
            return HealthStatus.Warning;
        }

        // GPS not running is a warning (user may have intentionally stopped it)
        if (!result.IsGpsRunning)
        {
            return HealthStatus.Warning;
        }

        // Missing server config when tracking is enabled
        if (result.IsTrackingEnabled && !result.HasServerConfig)
        {
            return HealthStatus.Warning;
        }

        // No network when tracking is enabled
        if (result.IsTrackingEnabled && !result.HasNetworkConnectivity)
        {
            return HealthStatus.Warning;
        }

        return HealthStatus.Healthy;
    }
}

/// <summary>
/// Result of automated health check.
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// Gets or sets whether foreground location permission is granted.
    /// </summary>
    public bool HasForegroundLocationPermission { get; set; }

    /// <summary>
    /// Gets or sets whether background location permission is granted.
    /// </summary>
    public bool HasBackgroundLocationPermission { get; set; }

    /// <summary>
    /// Gets or sets whether GPS is currently running.
    /// </summary>
    public bool IsGpsRunning { get; set; }

    /// <summary>
    /// Gets or sets whether tracking is enabled.
    /// </summary>
    public bool IsTrackingEnabled { get; set; }

    /// <summary>
    /// Gets or sets whether server configuration is present.
    /// </summary>
    public bool HasServerConfig { get; set; }

    /// <summary>
    /// Gets or sets whether network connectivity is available.
    /// </summary>
    public bool HasNetworkConnectivity { get; set; }

    /// <summary>
    /// Gets or sets the overall health status.
    /// </summary>
    public HealthStatus OverallHealth { get; set; }
}

/// <summary>
/// Overall health status enumeration.
/// </summary>
public enum HealthStatus
{
    /// <summary>System is healthy and operational.</summary>
    Healthy,

    /// <summary>System has warnings but is operational.</summary>
    Warning,

    /// <summary>System has critical issues.</summary>
    Critical,

    /// <summary>System encountered an error.</summary>
    Error
}

/// <summary>
/// System information model.
/// </summary>
public class SystemInfo
{
    /// <summary>Gets or sets the platform.</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Gets or sets the OS version.</summary>
    public string OsVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the device model.</summary>
    public string DeviceModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the device manufacturer.</summary>
    public string DeviceManufacturer { get; set; } = string.Empty;

    /// <summary>Gets or sets the device type.</summary>
    public string DeviceType { get; set; } = string.Empty;

    /// <summary>Gets or sets the app version.</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the app build number.</summary>
    public string AppBuild { get; set; } = string.Empty;

    /// <summary>Gets or sets the battery level.</summary>
    public double BatteryLevel { get; set; }

    /// <summary>Gets or sets the battery state.</summary>
    public string BatteryState { get; set; } = string.Empty;

    /// <summary>Gets or sets the power source.</summary>
    public string PowerSource { get; set; } = string.Empty;

    /// <summary>Gets or sets whether energy saver is on.</summary>
    public bool IsEnergySaver { get; set; }
}

/// <summary>
/// Log file information model.
/// </summary>
public class LogFileInfo
{
    /// <summary>Gets or sets the file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Gets or sets the full file path.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets the last modified date.</summary>
    public DateTime LastModified { get; set; }
}
