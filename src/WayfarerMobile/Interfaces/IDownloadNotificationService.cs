namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for managing download notifications and user guidance.
/// Provides pre-download guidance, progress notifications, and error handling.
/// </summary>
public interface IDownloadNotificationService
{
    /// <summary>
    /// Shows guidance dialog before starting a download.
    /// </summary>
    /// <param name="tripName">Name of the trip being downloaded.</param>
    /// <param name="estimatedSizeMb">Estimated download size in MB.</param>
    /// <returns>True if user confirmed to proceed, false otherwise.</returns>
    Task<bool> ShowDownloadGuidanceAsync(string tripName, double estimatedSizeMb);

    /// <summary>
    /// Shows notification about interrupted downloads that can be resumed.
    /// </summary>
    /// <param name="interruptedDownloads">List of interrupted download info.</param>
    Task NotifyInterruptedDownloadsAsync(List<InterruptedDownloadInfo> interruptedDownloads);

    /// <summary>
    /// Checks available storage before download and warns if insufficient.
    /// </summary>
    /// <param name="requiredMb">Required space in MB.</param>
    /// <returns>True if storage is sufficient, false otherwise.</returns>
    Task<bool> CheckStorageBeforeDownloadAsync(double requiredMb);

    /// <summary>
    /// Handles unexpected download interruption by showing appropriate message.
    /// </summary>
    /// <param name="tripName">Name of the trip.</param>
    /// <param name="reason">Reason for interruption.</param>
    Task HandleUnexpectedInterruptionAsync(string tripName, DownloadInterruptionReason reason);

    /// <summary>
    /// Shows download completed notification.
    /// </summary>
    /// <param name="tripName">Name of the completed trip.</param>
    /// <param name="totalSizeMb">Total downloaded size in MB.</param>
    Task NotifyDownloadCompletedAsync(string tripName, double totalSizeMb);
}

/// <summary>
/// Information about an interrupted download.
/// </summary>
public class InterruptedDownloadInfo
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    public Guid TripId { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string TripName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; set; }

    /// <summary>
    /// Gets or sets when the download was interrupted.
    /// </summary>
    public DateTime InterruptedAt { get; set; }

    /// <summary>
    /// Gets or sets the interruption reason.
    /// </summary>
    public DownloadInterruptionReason Reason { get; set; }
}

/// <summary>
/// Reasons for download interruption.
/// </summary>
public enum DownloadInterruptionReason
{
    /// <summary>
    /// User paused the download.
    /// </summary>
    UserPause,

    /// <summary>
    /// App was terminated.
    /// </summary>
    AppTerminated,

    /// <summary>
    /// Network connection was lost.
    /// </summary>
    NetworkLost,

    /// <summary>
    /// Storage space ran low.
    /// </summary>
    StorageLow,

    /// <summary>
    /// Storage error occurred.
    /// </summary>
    StorageError,

    /// <summary>
    /// Download failed due to server or other error.
    /// </summary>
    DownloadFailed,

    /// <summary>
    /// Unknown error.
    /// </summary>
    Unknown
}
