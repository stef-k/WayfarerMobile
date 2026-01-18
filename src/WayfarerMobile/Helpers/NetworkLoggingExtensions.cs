using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Helpers;

/// <summary>
/// Extension methods for network-aware logging with rate limiting.
/// Prevents log spam when the device is offline or when the same error occurs repeatedly.
/// </summary>
public static class NetworkLoggingExtensions
{
    /// <summary>
    /// Minimum interval between logging the same message template.
    /// </summary>
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tracks the last log time and suppression count for each message template.
    /// Key: message template string, Value: (lastLogTime, suppressedCount).
    /// </summary>
    private static readonly ConcurrentDictionary<string, (DateTime LastLogTime, int SuppressedCount)> ThrottleState = new();

    /// <summary>
    /// Logs a warning only if the device currently has internet connectivity,
    /// with rate limiting to prevent log spam during sustained failures.
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
    /// <strong>Rate limiting:</strong> Messages with the same template are logged at most
    /// once per 30 seconds. When logging resumes after throttling, the message includes
    /// the count of suppressed warnings (e.g., "(suppressed 5 similar warnings)").
    /// Different message templates are tracked independently.
    /// </para>
    /// <para>
    /// <strong>Thread safety:</strong> This method is thread-safe. Both the connectivity
    /// check and throttle state use thread-safe operations. The throttle check is a
    /// point-in-time snapshot; minor timing variations under high concurrency are acceptable.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// catch (HttpRequestException ex)
    /// {
    ///     // Logs at most once per 30 seconds for this message template
    ///     logger.LogNetworkWarningIfOnline("Failed to fetch data: {Message}", ex.Message);
    ///     return null;
    /// }
    /// </code>
    /// </example>
    public static void LogNetworkWarningIfOnline(this ILogger logger, string message, params object?[] args)
    {
        // Only log if we thought we were online (unexpected failure)
        // When offline, network errors are expected and logging them is noise
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // Check throttle state for this message template
        if (ThrottleState.TryGetValue(message, out var state))
        {
            var elapsed = now - state.LastLogTime;

            if (elapsed < ThrottleInterval)
            {
                // Within throttle window - increment suppressed count and skip logging
                ThrottleState.AddOrUpdate(
                    message,
                    (now, 1),
                    (_, existing) => (existing.LastLogTime, existing.SuppressedCount + 1));
                return;
            }

            // Throttle window expired - log with suppression count if any
            if (state.SuppressedCount > 0)
            {
                logger.LogWarning($"{message} (suppressed {state.SuppressedCount} similar warnings)", args);
            }
            else
            {
                logger.LogWarning(message, args);
            }

            // Reset state for new throttle window
            ThrottleState[message] = (now, 0);
        }
        else
        {
            // First occurrence of this message template - log normally
            logger.LogWarning(message, args);
            ThrottleState[message] = (now, 0);
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
