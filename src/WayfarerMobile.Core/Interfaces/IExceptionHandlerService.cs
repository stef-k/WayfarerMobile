namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for handling unhandled exceptions globally.
/// Provides centralized exception logging and user notification.
/// </summary>
public interface IExceptionHandlerService
{
    /// <summary>
    /// Initializes the exception handler and subscribes to global exception events.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Handles an exception that was caught but needs centralized processing.
    /// </summary>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="source">The source of the exception for logging context.</param>
    void HandleException(Exception exception, string source);

    /// <summary>
    /// Handles an exception and shows a user-friendly message.
    /// </summary>
    /// <param name="exception">The exception to handle.</param>
    /// <param name="userMessage">The user-friendly message to display.</param>
    /// <param name="source">The source of the exception for logging context.</param>
    Task HandleExceptionWithAlertAsync(Exception exception, string userMessage, string source);
}
