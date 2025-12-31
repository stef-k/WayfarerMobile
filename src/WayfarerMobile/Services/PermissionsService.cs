using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Cross-platform service for managing runtime permissions using MAUI Permissions API.
/// </summary>
public class PermissionsService : IPermissionsService
{
    private readonly ILogger<PermissionsService> _logger;

    /// <summary>
    /// Creates a new instance of PermissionsService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public PermissionsService(ILogger<PermissionsService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsLocationPermissionGrantedAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            return status == PermissionStatus.Granted;
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Location permission check not supported on this platform");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking location permission");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsBackgroundLocationPermissionGrantedAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
            return status == PermissionStatus.Granted;
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Background location permission check not supported on this platform");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking background location permission");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsNotificationPermissionGrantedAsync()
    {
#if ANDROID
        // Android 13+ requires notification permission
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<NotificationPermission>();
                return status == PermissionStatus.Granted;
            }
            catch (FeatureNotSupportedException ex)
            {
                _logger.LogWarning(ex, "Notification permission check not supported on this platform");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking notification permission");
                return false;
            }
        }
#endif
        // Pre-Android 13 or iOS doesn't require explicit notification permission for foreground services
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestLocationPermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

            if (status == PermissionStatus.Granted)
            {
                _logger.LogDebug("Location permission already granted");
                return true;
            }

            if (status == PermissionStatus.Denied && DeviceInfo.Platform == DevicePlatform.iOS)
            {
                _logger.LogWarning("Location permission previously denied on iOS");
                return false;
            }

            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            _logger.LogInformation("Location permission request result: {Status}", status);

            return status == PermissionStatus.Granted;
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Location permission request not supported on this platform");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting location permission");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RequestBackgroundLocationPermissionAsync()
    {
        try
        {
            // First ensure foreground location is granted
            if (!await IsLocationPermissionGrantedAsync())
            {
                _logger.LogWarning("Cannot request background location without foreground location");
                return false;
            }

            var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();

            if (status == PermissionStatus.Granted)
            {
                _logger.LogDebug("Background location permission already granted");
                return true;
            }

            status = await Permissions.RequestAsync<Permissions.LocationAlways>();
            _logger.LogInformation("Background location permission request result: {Status}", status);

            return status == PermissionStatus.Granted;
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Background location permission request not supported on this platform");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting background location permission");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RequestNotificationPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<NotificationPermission>();

                if (status == PermissionStatus.Granted)
                {
                    _logger.LogDebug("Notification permission already granted");
                    return true;
                }

                status = await Permissions.RequestAsync<NotificationPermission>();
                _logger.LogInformation("Notification permission request result: {Status}", status);

                return status == PermissionStatus.Granted;
            }
            catch (FeatureNotSupportedException ex)
            {
                _logger.LogWarning(ex, "Notification permission request not supported on this platform");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting notification permission");
                return false;
            }
        }
#endif
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> AreTrackingPermissionsGrantedAsync()
    {
        var locationGranted = await IsLocationPermissionGrantedAsync();
        if (!locationGranted)
            return false;

        // Background location is optional but recommended
        var backgroundGranted = await IsBackgroundLocationPermissionGrantedAsync();

        // Notification permission required on Android 13+
        var notificationGranted = await IsNotificationPermissionGrantedAsync();

        return locationGranted && notificationGranted;
    }

    /// <inheritdoc/>
    public async Task<PermissionRequestResult> RequestTrackingPermissionsAsync()
    {
        var result = new PermissionRequestResult();

        // Step 1: Request foreground location
        result.LocationGranted = await RequestLocationPermissionAsync();
        if (!result.LocationGranted)
        {
            result.Message = "Location permission is required for tracking.";
            return result;
        }

        // Step 2: Request notification permission (Android 13+)
        result.NotificationGranted = await RequestNotificationPermissionAsync();
        if (!result.NotificationGranted)
        {
            result.Message = "Notification permission is recommended for background tracking.";
            // Don't return - continue even without notifications
        }

        // Step 3: Request background location (optional but recommended)
        result.BackgroundLocationGranted = await RequestBackgroundLocationPermissionAsync();
        if (!result.BackgroundLocationGranted)
        {
            _logger.LogWarning("Background location not granted - tracking may stop when app is backgrounded");
        }

        result.AllGranted = result.LocationGranted && result.NotificationGranted;
        result.Message = result.AllGranted
            ? "All permissions granted"
            : "Some permissions were not granted. Tracking may be limited.";

        return result;
    }

    /// <inheritdoc/>
    public void OpenAppSettings()
    {
        try
        {
            AppInfo.ShowSettingsUI();
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Opening app settings not supported on this platform");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening app settings");
        }
    }
}

#if ANDROID
/// <summary>
/// Custom permission for Android 13+ POST_NOTIFICATIONS.
/// </summary>
[SupportedOSPlatform("android33.0")]
public class NotificationPermission : Permissions.BasePlatformPermission
{
    /// <inheritdoc/>
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new List<(string, bool)>
        {
            (Android.Manifest.Permission.PostNotifications, true)
        }.ToArray();
}
#endif
