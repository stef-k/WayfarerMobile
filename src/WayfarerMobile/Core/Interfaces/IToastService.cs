namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Shows a toast notification.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="duration">Duration in milliseconds (default 3000).</param>
    Task ShowAsync(string message, int duration = 3000);

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    /// <param name="message">The message to display.</param>
    Task ShowSuccessAsync(string message);

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    /// <param name="message">The message to display.</param>
    Task ShowErrorAsync(string message);

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    /// <param name="message">The message to display.</param>
    Task ShowWarningAsync(string message);
}
