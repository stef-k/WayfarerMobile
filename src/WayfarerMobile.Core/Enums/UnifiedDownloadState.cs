namespace WayfarerMobile.Core.Enums;

/// <summary>Provider-independent local Trip data state.</summary>
public enum UnifiedDownloadState
{
    ServerOnly = 0,
    Downloading = 1,
    Failed = 20,
    Downloaded = 30
}

public static class UnifiedDownloadStateExtensions
{
    public static bool IsDownloading(this UnifiedDownloadState state) => state == UnifiedDownloadState.Downloading;
    public static bool HasLocalData(this UnifiedDownloadState state) => state == UnifiedDownloadState.Downloaded;
    public static bool CanLoadToMap(this UnifiedDownloadState state) => state == UnifiedDownloadState.Downloaded;
    public static bool HasMetadata(this UnifiedDownloadState state) => state == UnifiedDownloadState.Downloaded;
    public static string GetGroupName(this UnifiedDownloadState state) => state switch
    {
        UnifiedDownloadState.Downloaded => "Downloaded",
        UnifiedDownloadState.Downloading => "In Progress",
        UnifiedDownloadState.Failed => "Failed",
        _ => "Available on Server"
    };
    public static string GetStatusText(this UnifiedDownloadState state) => state switch
    {
        UnifiedDownloadState.Downloaded => "Downloaded",
        UnifiedDownloadState.Downloading => "Downloading…",
        UnifiedDownloadState.Failed => "Failed",
        _ => "Online"
    };
}
