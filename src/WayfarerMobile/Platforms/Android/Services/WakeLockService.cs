using Android.Content;
using Android.OS;
using Android.Views;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Android implementation of wake lock service.
/// Manages partial wake locks and screen-on flags during navigation.
/// </summary>
public class WakeLockService : IWakeLockService
{
    private PowerManager.WakeLock? _wakeLock;
    private bool _keepingScreenOn;
    private readonly object _lock = new();

    /// <summary>
    /// Gets whether a wake lock is currently held.
    /// </summary>
    public bool IsWakeLockHeld
    {
        get
        {
            lock (_lock)
            {
                return _wakeLock?.IsHeld == true || _keepingScreenOn;
            }
        }
    }

    /// <summary>
    /// Acquires a wake lock to keep the device awake during navigation.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on (true) or just CPU (false).</param>
    public void AcquireWakeLock(bool keepScreenOn = true)
    {
        lock (_lock)
        {
            if (keepScreenOn)
            {
                // Use Window.KeepScreenOn flag - more battery friendly
                AcquireScreenOnFlag();
            }
            else
            {
                // Use partial wake lock for CPU only
                AcquirePartialWakeLock();
            }
        }
    }

    /// <summary>
    /// Releases the wake lock, allowing the device to sleep normally.
    /// </summary>
    public void ReleaseWakeLock()
    {
        lock (_lock)
        {
            ReleaseScreenOnFlag();
            ReleasePartialWakeLock();
        }
    }

    /// <summary>
    /// Acquires screen-on flag using the activity window.
    /// This is preferred over wake locks for keeping screen on.
    /// </summary>
    private void AcquireScreenOnFlag()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                System.Diagnostics.Debug.WriteLine("[WakeLockService] No activity available for screen-on flag");
                return;
            }

            activity.RunOnUiThread(() =>
            {
                activity.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
                _keepingScreenOn = true;
                System.Diagnostics.Debug.WriteLine("[WakeLockService] Screen-on flag acquired");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WakeLockService] Error acquiring screen-on flag: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the screen-on flag.
    /// </summary>
    private void ReleaseScreenOnFlag()
    {
        if (!_keepingScreenOn)
            return;

        try
        {
            var activity = Platform.CurrentActivity;
            if (activity == null)
            {
                _keepingScreenOn = false;
                return;
            }

            activity.RunOnUiThread(() =>
            {
                activity.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
                _keepingScreenOn = false;
                System.Diagnostics.Debug.WriteLine("[WakeLockService] Screen-on flag released");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WakeLockService] Error releasing screen-on flag: {ex.Message}");
        }
    }

    /// <summary>
    /// Acquires a partial wake lock to keep CPU running.
    /// Only used when screen-on is not needed.
    /// </summary>
    private void AcquirePartialWakeLock()
    {
        if (_wakeLock?.IsHeld == true)
        {
            System.Diagnostics.Debug.WriteLine("[WakeLockService] Wake lock already held");
            return;
        }

        try
        {
            var context = Platform.AppContext;
            var powerManager = (PowerManager?)context.GetSystemService(Context.PowerService);

            if (powerManager == null)
            {
                System.Diagnostics.Debug.WriteLine("[WakeLockService] PowerManager not available");
                return;
            }

            _wakeLock = powerManager.NewWakeLock(
                WakeLockFlags.Partial,
                "WayfarerMobile:NavigationWakeLock");

            if (_wakeLock == null)
            {
                System.Diagnostics.Debug.WriteLine("[WakeLockService] Failed to create wake lock");
                return;
            }

            // Set timeout to 4 hours max to prevent battery drain from leaks
            _wakeLock.Acquire(4 * 60 * 60 * 1000);

            System.Diagnostics.Debug.WriteLine("[WakeLockService] Partial wake lock acquired");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WakeLockService] Error acquiring wake lock: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the partial wake lock.
    /// </summary>
    private void ReleasePartialWakeLock()
    {
        if (_wakeLock == null)
            return;

        try
        {
            if (_wakeLock.IsHeld)
            {
                _wakeLock.Release();
                System.Diagnostics.Debug.WriteLine("[WakeLockService] Partial wake lock released");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WakeLockService] Error releasing wake lock: {ex.Message}");
        }
        finally
        {
            _wakeLock = null;
        }
    }
}
