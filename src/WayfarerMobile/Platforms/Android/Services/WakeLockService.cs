using Android.Content;
using Android.OS;
using Android.Util;
using Android.Views;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Services;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Android implementation of wake lock service.
/// Manages partial wake locks and screen-on flags during navigation.
/// </summary>
public class WakeLockService : IWakeLockService
{
    private const string LogTag = "WayfarerWakeLock";
    private PowerManager.WakeLock? _wakeLock;
    private bool _keepingScreenOn;
    private readonly WakeLockOwnership _ownership = new();

    /// <summary>
    /// Gets whether a wake lock is currently held.
    /// </summary>
    public bool IsWakeLockHeld => _ownership.IsHeld;

    /// <summary>
    /// Acquires a wake lock to keep the device awake during navigation.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on (true) or just CPU (false).</param>
    public bool TryAcquireWakeLock(WakeLockOwner owner, bool keepScreenOn = true) =>
        _ownership.TryAcquire(owner, keepScreenOn ? AcquireScreenOnFlag : AcquirePartialWakeLock);

    /// <summary>
    /// Releases the wake lock, allowing the device to sleep normally.
    /// </summary>
    public void ReleaseWakeLock(WakeLockOwner owner) =>
        _ownership.Release(owner, ReleasePhysicalWakeLock);

    /// <summary>
    /// Acquires screen-on flag using the activity window.
    /// This is preferred over wake locks for keeping screen on.
    /// </summary>
    private bool AcquireScreenOnFlag()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                Log.Warn(LogTag, "No activity available for screen-on flag");
                return false;
            }

            MainThread.InvokeOnMainThreadAsync(() =>
            {
                var window = activity.Window ?? throw new InvalidOperationException("Activity window unavailable");
                window.AddFlags(WindowManagerFlags.KeepScreenOn);
                _keepingScreenOn = true;
                Log.Debug(LogTag, "Screen-on flag acquired");
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Error acquiring screen-on flag: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Releases the screen-on flag.
    /// </summary>
    private bool ReleaseScreenOnFlag()
    {
        if (!_keepingScreenOn)
            return true;

        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                return false;
            }

            MainThread.InvokeOnMainThreadAsync(() =>
            {
                var window = activity.Window ?? throw new InvalidOperationException("Activity window unavailable");
                window.ClearFlags(WindowManagerFlags.KeepScreenOn);
                _keepingScreenOn = false;
                Log.Debug(LogTag, "Screen-on flag released");
            }).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Error releasing screen-on flag: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Acquires a partial wake lock to keep CPU running.
    /// Only used when screen-on is not needed.
    /// </summary>
    private bool AcquirePartialWakeLock()
    {
        if (_wakeLock?.IsHeld == true)
        {
            Log.Debug(LogTag, "Wake lock already held");
            return true;
        }

        try
        {
            var context = Platform.AppContext;
            var powerManager = (PowerManager?)context.GetSystemService(Context.PowerService);

            if (powerManager == null)
            {
                Log.Warn(LogTag, "PowerManager not available");
                return false;
            }

            _wakeLock = powerManager.NewWakeLock(
                WakeLockFlags.Partial,
                "WayfarerMobile:NavigationWakeLock");

            if (_wakeLock == null)
            {
                Log.Warn(LogTag, "Failed to create wake lock");
                return false;
            }

            // Set timeout to 4 hours max to prevent battery drain from leaks
            _wakeLock.Acquire(4 * 60 * 60 * 1000);

            Log.Debug(LogTag, "Partial wake lock acquired");
            return _wakeLock.IsHeld;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Error acquiring wake lock: {ex.Message}");
            _wakeLock = null;
            return false;
        }
    }

    /// <summary>
    /// Releases the partial wake lock.
    /// </summary>
    private bool ReleasePartialWakeLock()
    {
        if (_wakeLock == null)
            return true;

        try
        {
            if (_wakeLock.IsHeld)
            {
                _wakeLock.Release();
                Log.Debug(LogTag, "Partial wake lock released");
            }
            _wakeLock = null;
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Error releasing wake lock: {ex.Message}");
            return false;
        }
    }

    private bool ReleasePhysicalWakeLock()
    {
        var screenReleased = ReleaseScreenOnFlag();
        var partialReleased = ReleasePartialWakeLock();
        return screenReleased && partialReleased;
    }
}
