using Android.Util;

namespace WayfarerMobile.Platforms.Android;

/// <summary>
/// Issue #185: A wrapper SynchronizationContext that catches ObjectDisposedException
/// from IImageHandler.FireAndForget error callbacks.
///
/// When MAUI's async image loading fails after Activity recreation, the error callback
/// tries to log via IServiceProvider, which is already disposed. This exception propagates
/// through SyncContext.Post and crashes the app because it bypasses all exception handlers.
///
/// This wrapper intercepts Post callbacks and suppresses the specific exception pattern.
/// </summary>
public class SafeSynchronizationContext : SynchronizationContext
{
    private const string LogTag = "WayfarerSyncContext";
    private readonly SynchronizationContext _inner;

    /// <summary>
    /// Creates a new SafeSynchronizationContext wrapping the specified context.
    /// </summary>
    /// <param name="inner">The original SynchronizationContext to wrap.</param>
    public SafeSynchronizationContext(SynchronizationContext inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// Posts a callback to be executed asynchronously, wrapping it to catch
    /// ObjectDisposedException from IServiceProvider disposal during MAUI lifecycle transitions.
    /// </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        _inner.Post(wrappedState =>
        {
            try
            {
                d(wrappedState);
            }
            catch (ObjectDisposedException ex) when (IsFireAndForgetDisposedException(ex))
            {
                // Suppress only FireAndForget callbacks - these are non-critical async operations.
                // UI rendering exceptions are NOT suppressed - they indicate real problems.
                Log.Warn(LogTag, $"Suppressed FireAndForget ObjectDisposedException (MAUI bug, issue #185): {ex.ObjectName}");
                Serilog.Log.Warning("Suppressed FireAndForget ObjectDisposedException (MAUI bug, issue #185): {ObjectName}", ex.ObjectName);
            }
        }, state);
    }

    /// <summary>
    /// Sends a callback to be executed synchronously, wrapping it to catch
    /// ObjectDisposedException from IServiceProvider disposal during MAUI lifecycle transitions.
    /// </summary>
    public override void Send(SendOrPostCallback d, object? state)
    {
        try
        {
            _inner.Send(d, state);
        }
        catch (ObjectDisposedException ex) when (IsFireAndForgetDisposedException(ex))
        {
            Log.Warn(LogTag, $"Suppressed ObjectDisposedException in SyncContext.Send (MAUI bug, issue #185): {ex.ObjectName}");
            Serilog.Log.Warning("Suppressed ObjectDisposedException in SyncContext.Send (MAUI bug, issue #185): {ObjectName}", ex.ObjectName);
            // Swallow the exception - don't rethrow
        }
    }

    /// <summary>
    /// Creates a copy of this SynchronizationContext.
    /// </summary>
    public override SynchronizationContext CreateCopy()
    {
        return new SafeSynchronizationContext(_inner.CreateCopy());
    }

    /// <summary>
    /// Checks if the exception is a fire-and-forget ObjectDisposedException that's safe to suppress.
    /// We ONLY suppress exceptions from async fire-and-forget callbacks (like image loading).
    /// We do NOT suppress exceptions from actual UI rendering - those indicate real problems.
    /// </summary>
    private static bool IsFireAndForgetDisposedException(ObjectDisposedException ex)
    {
        // Must be IServiceProvider disposal
        var isServiceProvider = ex.ObjectName?.Contains("IServiceProvider") == true ||
                                ex.Message?.Contains("IServiceProvider") == true;
        if (!isServiceProvider)
            return false;

        // Check stack trace - ONLY suppress if it's a FireAndForget callback
        var stackTrace = ex.StackTrace;
        if (stackTrace == null)
            return false;

        // Only suppress fire-and-forget async callbacks - these are non-critical
        // Do NOT suppress UI rendering exceptions (Handler.Map*, Element.SetHandler, etc.)
        return stackTrace.Contains("FireAndForget");
    }

    /// <summary>
    /// Installs the SafeSynchronizationContext as the current context if not already installed.
    /// Call this early in Activity.OnCreate before any async operations start.
    /// </summary>
    /// <returns>True if a new wrapper was installed, false if already wrapped.</returns>
    public static bool Install()
    {
        var current = SynchronizationContext.Current;
        if (current == null)
        {
            Log.Warn(LogTag, "No SynchronizationContext to wrap - this should not happen on Android UI thread");
            return false;
        }

        // Don't wrap if already wrapped
        if (current is SafeSynchronizationContext)
        {
            Log.Debug(LogTag, "SafeSynchronizationContext already installed");
            return false;
        }

        var safe = new SafeSynchronizationContext(current);
        SynchronizationContext.SetSynchronizationContext(safe);
        Log.Debug(LogTag, "SafeSynchronizationContext installed");
        Serilog.Log.Information("SafeSynchronizationContext installed for issue #185 workaround");
        return true;
    }
}
