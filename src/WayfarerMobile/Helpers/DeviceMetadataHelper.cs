namespace WayfarerMobile.Helpers;

/// <summary>
/// Static helper class for capturing device and app metadata.
/// Provides safe access to platform services with exception handling.
/// </summary>
/// <remarks>
/// This class is intentionally static because the MAUI platform services
/// (AppInfo, DeviceInfo, Battery) are static and cannot be injected via DI.
/// All methods handle exceptions gracefully, returning null on failure.
/// </remarks>
public static class DeviceMetadataHelper
{
    /// <summary>
    /// Gets the device's current timezone ID.
    /// </summary>
    /// <returns>IANA timezone ID (e.g., "Europe/Athens") or null if unavailable.</returns>
    public static string? GetTimeZoneId()
    {
        try
        {
            return TimeZoneInfo.Local.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the app version string.
    /// </summary>
    /// <returns>Version string (e.g., "1.2.3") or null if unavailable.</returns>
    public static string? GetAppVersion()
    {
        try
        {
            return AppInfo.VersionString;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the app build number.
    /// </summary>
    /// <returns>Build string (e.g., "45") or null if unavailable.</returns>
    public static string? GetAppBuild()
    {
        try
        {
            return AppInfo.BuildString;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the device model.
    /// </summary>
    /// <returns>Device model (e.g., "Pixel 7 Pro", "iPhone 14") or null if unavailable.</returns>
    public static string? GetDeviceModel()
    {
        try
        {
            return DeviceInfo.Model;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the OS version string.
    /// </summary>
    /// <returns>Platform and version (e.g., "Android 14", "iOS 17.2") or null if unavailable.</returns>
    public static string? GetOsVersion()
    {
        try
        {
            return $"{DeviceInfo.Platform} {DeviceInfo.VersionString}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the battery level (0-100) or null if unavailable.
    /// </summary>
    /// <returns>Battery level percentage (0-100) or null if unavailable or negative.</returns>
    public static int? GetBatteryLevel()
    {
        try
        {
            var level = Battery.ChargeLevel;
            return level >= 0 ? (int)(level * 100) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets whether the device is charging, or null if unavailable.
    /// </summary>
    /// <returns>True if charging or full, false if not charging, null if unavailable.</returns>
    public static bool? GetIsCharging()
    {
        try
        {
            var state = Battery.State;
            return state == BatteryState.Charging || state == BatteryState.Full;
        }
        catch
        {
            return null;
        }
    }
}
