namespace WayfarerMobile.Core.Enums;

/// <summary>
/// GPS polling performance modes for the location tracking service.
/// </summary>
public enum PerformanceMode
{
    /// <summary>
    /// High performance mode with 1-second updates.
    /// Used when MainPage is visible or navigation is active.
    /// </summary>
    HighPerformance,

    /// <summary>
    /// Normal mode using server-configured interval (typically 60 seconds).
    /// Used when app is in background or on other pages.
    /// </summary>
    Normal,

    /// <summary>
    /// Power saver mode with 5-minute updates.
    /// Used when battery is below 20%.
    /// </summary>
    PowerSaver
}
