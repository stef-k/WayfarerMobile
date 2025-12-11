using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

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

/// <summary>
/// Implementation of download notification service.
/// </summary>
public class DownloadNotificationService : IDownloadNotificationService
{
    private readonly ILogger<DownloadNotificationService> _logger;
    private readonly IToastService _toastService;

    /// <summary>
    /// Creates a new instance of DownloadNotificationService.
    /// </summary>
    public DownloadNotificationService(
        ILogger<DownloadNotificationService> logger,
        IToastService toastService)
    {
        _logger = logger;
        _toastService = toastService;
    }

    /// <inheritdoc/>
    public async Task<bool> ShowDownloadGuidanceAsync(string tripName, double estimatedSizeMb)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return false;

        var sizeText = estimatedSizeMb >= 1000
            ? $"{estimatedSizeMb / 1000:F1} GB"
            : $"{estimatedSizeMb:F0} MB";

        var message = $"Download \"{tripName}\" for offline use?\n\n" +
                      $"Estimated size: {sizeText}\n\n" +
                      "Tips for best results:\n" +
                      "• Keep the app open during download\n" +
                      "• Use a stable WiFi connection\n" +
                      "• Ensure sufficient storage space\n" +
                      "• You can pause and resume later";

        if (estimatedSizeMb > 100)
        {
            message += "\n\n⚠️ Large download - WiFi recommended";
        }

        return await page.DisplayAlertAsync("Download Trip", message, "Download", "Cancel");
    }

    /// <inheritdoc/>
    public async Task NotifyInterruptedDownloadsAsync(List<InterruptedDownloadInfo> interruptedDownloads)
    {
        if (interruptedDownloads.Count == 0)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        string message;
        if (interruptedDownloads.Count == 1)
        {
            var download = interruptedDownloads[0];
            message = $"The download of \"{download.TripName}\" was interrupted at {download.ProgressPercent}%.\n\n" +
                      $"Reason: {GetInterruptionReasonText(download.Reason)}\n\n" +
                      "Would you like to go to the Trips page to resume?";
        }
        else
        {
            var tripNames = string.Join(", ", interruptedDownloads.Take(3).Select(d => d.TripName));
            if (interruptedDownloads.Count > 3)
            {
                tripNames += $" and {interruptedDownloads.Count - 3} more";
            }
            message = $"The following downloads were interrupted:\n{tripNames}\n\n" +
                      "Would you like to go to the Trips page to resume?";
        }

        var goToTrips = await page.DisplayAlertAsync("Interrupted Downloads", message, "Go to Trips", "Later");
        if (goToTrips)
        {
            await Shell.Current.GoToAsync("//Trips");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CheckStorageBeforeDownloadAsync(double requiredMb)
    {
        try
        {
            // Get available space (platform-specific)
            var cacheDir = FileSystem.CacheDirectory;
            var driveInfo = new DriveInfo(Path.GetPathRoot(cacheDir) ?? cacheDir);
            var availableMb = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0);

            // Require at least 1GB buffer
            var requiredWithBuffer = requiredMb + 1024;

            if (availableMb < requiredWithBuffer)
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page != null)
                {
                    var availableText = availableMb >= 1000
                        ? $"{availableMb / 1000:F1} GB"
                        : $"{availableMb:F0} MB";
                    var requiredText = requiredMb >= 1000
                        ? $"{requiredMb / 1000:F1} GB"
                        : $"{requiredMb:F0} MB";

                    await page.DisplayAlertAsync(
                        "Insufficient Storage",
                        $"This download requires approximately {requiredText}, but only {availableText} is available.\n\n" +
                        "Please free up some space and try again.",
                        "OK");
                }
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check storage space");
            // If we can't check, proceed anyway
            return true;
        }
    }

    /// <inheritdoc/>
    public async Task HandleUnexpectedInterruptionAsync(string tripName, DownloadInterruptionReason reason)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var reasonText = GetInterruptionReasonText(reason);
        var suggestion = GetInterruptionSuggestion(reason);

        await page.DisplayAlertAsync(
            "Download Interrupted",
            $"The download of \"{tripName}\" was interrupted.\n\n" +
            $"Reason: {reasonText}\n\n" +
            $"{suggestion}\n\n" +
            "Your progress has been saved. You can resume the download from the Trips page.",
            "OK");
    }

    /// <inheritdoc/>
    public async Task NotifyDownloadCompletedAsync(string tripName, double totalSizeMb)
    {
        var sizeText = totalSizeMb >= 1000
            ? $"{totalSizeMb / 1000:F1} GB"
            : $"{totalSizeMb:F1} MB";

        await _toastService.ShowSuccessAsync($"Downloaded \"{tripName}\" ({sizeText})");
        _logger.LogInformation("Trip '{TripName}' downloaded successfully ({SizeMb:F1} MB)", tripName, totalSizeMb);
    }

    private static string GetInterruptionReasonText(DownloadInterruptionReason reason)
    {
        return reason switch
        {
            DownloadInterruptionReason.UserPause => "You paused the download",
            DownloadInterruptionReason.AppTerminated => "The app was closed",
            DownloadInterruptionReason.NetworkLost => "Network connection was lost",
            DownloadInterruptionReason.StorageLow => "Storage space ran low",
            DownloadInterruptionReason.StorageError => "A storage error occurred",
            DownloadInterruptionReason.DownloadFailed => "The download failed",
            _ => "An unexpected error occurred"
        };
    }

    private static string GetInterruptionSuggestion(DownloadInterruptionReason reason)
    {
        return reason switch
        {
            DownloadInterruptionReason.NetworkLost => "Please check your internet connection and try again.",
            DownloadInterruptionReason.StorageLow => "Please free up some storage space and try again.",
            DownloadInterruptionReason.StorageError => "Please check your device storage and try again.",
            DownloadInterruptionReason.DownloadFailed => "Please try again. If the problem persists, check the tile server status.",
            _ => "Please try again when ready."
        };
    }
}
