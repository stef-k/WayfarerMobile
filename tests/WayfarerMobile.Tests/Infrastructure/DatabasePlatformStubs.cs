// Database upgrade tests exercise SQLite; device metadata and native preferences
// are outside that seam. Preference deletion behavior has dedicated migration tests.
public static class Preferences
{
    public static void Remove(string key) { }
}

namespace WayfarerMobile.Helpers
{
    public static class DeviceMetadataHelper
    {
        public static string? GetTimeZoneId() => null;
        public static string? GetAppVersion() => null;
        public static string? GetAppBuild() => null;
        public static string? GetDeviceModel() => null;
        public static string? GetOsVersion() => null;
        public static int? GetBatteryLevel() => null;
        public static bool? GetIsCharging() => null;
    }
}
