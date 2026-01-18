using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Helpers;

/// <summary>
/// Extension methods for network-aware logging.
/// Prevents log spam when the device is offline by checking connectivity before logging.
/// </summary>
public static class NetworkLoggingExtensions
{
    /// <summary>
    /// Logs a warning only if the device currently has internet connectivity.
    /// Use this for network errors to avoid log spam when offline (expected condition).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="message">The log message template.</param>
    /// <param name="args">The message template arguments.</param>
    /// <remarks>
    /// <para>
    /// <strong>When to use:</strong> In catch blocks for <see cref="HttpRequestException"/>
    /// where network failure is expected when offline.
    /// </para>
    /// <para>
    /// <strong>Thread safety:</strong> <see cref="Connectivity.Current.NetworkAccess"/> is
    /// thread-safe and can be called from any thread. The check is a point-in-time snapshot;
    /// connectivity may change between the check and the log call, which is acceptable.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// catch (HttpRequestException ex)
    /// {
    ///     logger.LogNetworkWarningIfOnline("Failed to fetch data: {Message}", ex.Message);
    ///     return null;
    /// }
    /// </code>
    /// </example>
    public static void LogNetworkWarningIfOnline(this ILogger logger, string message, params object?[] args)
    {
        // Only log if we thought we were online (unexpected failure)
        // When offline, network errors are expected and logging them is noise
        if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            logger.LogWarning(message, args);
        }
    }

    /// <summary>
    /// Checks if the device currently has internet connectivity.
    /// </summary>
    /// <returns>True if internet is available, false otherwise.</returns>
    /// <remarks>
    /// This is a convenience method for code that needs to check connectivity
    /// without directly depending on MAUI's Connectivity class.
    /// </remarks>
    public static bool HasInternetConnectivity()
    {
        return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    }
}
