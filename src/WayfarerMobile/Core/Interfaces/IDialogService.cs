namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for displaying user-friendly dialogs.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The error message.</param>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a success dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The success message.</param>
    Task ShowSuccessAsync(string title, string message);

    /// <summary>
    /// Shows an info dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The info message.</param>
    Task ShowInfoAsync(string title, string message);

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message.</param>
    /// <param name="accept">The accept button text.</param>
    /// <param name="cancel">The cancel button text.</param>
    /// <returns>True if the user accepted, false otherwise.</returns>
    Task<bool> ShowConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");

    /// <summary>
    /// Shows an error dialog with optional retry action.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The error message.</param>
    /// <param name="retryAction">Optional retry action.</param>
    Task ShowErrorWithRetryAsync(string title, string message, Func<Task>? retryAction = null);
}
