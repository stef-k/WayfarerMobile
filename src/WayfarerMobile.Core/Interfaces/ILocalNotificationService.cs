namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Cross-platform service for showing local notifications.
/// Platform-specific implementations handle Android/iOS notification APIs.
/// </summary>
public interface ILocalNotificationService
{
    /// <summary>
    /// Shows a local notification.
    /// </summary>
    /// <param name="title">The notification title.</param>
    /// <param name="message">The notification body message.</param>
    /// <param name="silent">Whether to show silently (no sound/vibration).</param>
    /// <param name="data">Optional data dictionary for notification actions.</param>
    /// <returns>The notification ID, or -1 if failed.</returns>
    Task<int> ShowAsync(string title, string message, bool silent = false, Dictionary<string, string>? data = null);

    /// <summary>
    /// Cancels a specific notification by ID.
    /// </summary>
    /// <param name="notificationId">The notification ID to cancel.</param>
    void Cancel(int notificationId);

    /// <summary>
    /// Cancels all notifications from this app.
    /// </summary>
    void CancelAll();

    /// <summary>
    /// Requests notification permissions if not already granted.
    /// </summary>
    /// <returns>True if permissions are granted.</returns>
    Task<bool> RequestPermissionAsync();
}
