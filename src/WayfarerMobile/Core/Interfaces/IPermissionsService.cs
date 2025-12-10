namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing runtime permissions.
/// </summary>
public interface IPermissionsService
{
    /// <summary>
    /// Gets whether location permission is granted.
    /// </summary>
    Task<bool> IsLocationPermissionGrantedAsync();

    /// <summary>
    /// Gets whether background location permission is granted (Android 10+).
    /// </summary>
    Task<bool> IsBackgroundLocationPermissionGrantedAsync();

    /// <summary>
    /// Gets whether notification permission is granted (Android 13+).
    /// </summary>
    Task<bool> IsNotificationPermissionGrantedAsync();

    /// <summary>
    /// Requests location permission.
    /// </summary>
    /// <returns>True if permission was granted.</returns>
    Task<bool> RequestLocationPermissionAsync();

    /// <summary>
    /// Requests background location permission (Android 10+).
    /// Should only be called after foreground location is granted.
    /// </summary>
    /// <returns>True if permission was granted.</returns>
    Task<bool> RequestBackgroundLocationPermissionAsync();

    /// <summary>
    /// Requests notification permission (Android 13+).
    /// </summary>
    /// <returns>True if permission was granted.</returns>
    Task<bool> RequestNotificationPermissionAsync();

    /// <summary>
    /// Gets whether all required permissions for tracking are granted.
    /// </summary>
    Task<bool> AreTrackingPermissionsGrantedAsync();

    /// <summary>
    /// Requests all required permissions for tracking in the proper order.
    /// </summary>
    /// <returns>True if all required permissions were granted.</returns>
    Task<PermissionRequestResult> RequestTrackingPermissionsAsync();

    /// <summary>
    /// Opens the app settings page for manual permission changes.
    /// </summary>
    void OpenAppSettings();
}

/// <summary>
/// Result of a permission request operation.
/// </summary>
public class PermissionRequestResult
{
    /// <summary>
    /// Gets or sets whether all required permissions are granted.
    /// </summary>
    public bool AllGranted { get; set; }

    /// <summary>
    /// Gets or sets whether location permission is granted.
    /// </summary>
    public bool LocationGranted { get; set; }

    /// <summary>
    /// Gets or sets whether background location is granted.
    /// </summary>
    public bool BackgroundLocationGranted { get; set; }

    /// <summary>
    /// Gets or sets whether notification permission is granted.
    /// </summary>
    public bool NotificationGranted { get; set; }

    /// <summary>
    /// Gets or sets a message describing the result.
    /// </summary>
    public string? Message { get; set; }
}
