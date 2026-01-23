using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Global exception handler service that catches unhandled exceptions,
/// logs them, and provides user-friendly error handling.
/// </summary>
public class ExceptionHandlerService : IExceptionHandlerService
{
    private readonly ILogger<ExceptionHandlerService> _logger;
    private bool _isInitialized;

    /// <summary>
    /// Creates a new instance of ExceptionHandlerService.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public ExceptionHandlerService(ILogger<ExceptionHandlerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Initialize()
    {
        if (_isInitialized)
            return;

        // Subscribe to unhandled exception events
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _isInitialized = true;
        _logger.LogDebug("Global exception handler initialized");
    }

    /// <inheritdoc/>
    public void HandleException(Exception exception, string source)
    {
        _logger.LogError(exception, "Exception from {Source}: {Message}", source, exception.Message);

        // Log inner exceptions
        var inner = exception.InnerException;
        var depth = 0;
        while (inner != null && depth < 5)
        {
            _logger.LogError(inner, "Inner exception ({Depth}): {Message}", depth, inner.Message);
            inner = inner.InnerException;
            depth++;
        }
    }

    /// <inheritdoc/>
    public async Task HandleExceptionWithAlertAsync(Exception exception, string userMessage, string source)
    {
        HandleException(exception, source);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page != null)
                {
                    await page.DisplayAlertAsync("Error", userMessage, "OK");
                }
            });
        }
        catch (Exception alertEx)
        {
            _logger.LogWarning(alertEx, "Failed to show error alert to user");
        }
    }

    /// <summary>
    /// Handles unhandled exceptions from the AppDomain.
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger.LogCritical(exception, "Unhandled AppDomain exception (IsTerminating: {IsTerminating})",
                e.IsTerminating);
            HandleException(exception, "AppDomain.UnhandledException");
        }
        else
        {
            _logger.LogCritical("Unhandled AppDomain exception (non-Exception object): {Object}",
                e.ExceptionObject);
        }
    }

    /// <summary>
    /// Handles unobserved task exceptions.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Unobserved task exception");
        HandleException(e.Exception, "TaskScheduler.UnobservedTaskException");

        // Mark as observed to prevent app termination
        e.SetObserved();
    }
}
