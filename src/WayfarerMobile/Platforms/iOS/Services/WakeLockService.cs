using UIKit;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of wake lock service.
/// Uses UIApplication.IdleTimerDisabled to prevent screen from locking.
/// </summary>
public class WakeLockService : IWakeLockService
{
    private bool _isWakeLockHeld;
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
                return _isWakeLockHeld;
            }
        }
    }

    /// <summary>
    /// Acquires a wake lock to keep the device awake during navigation.
    /// On iOS, this disables the idle timer to prevent screen from locking.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on. On iOS, this always keeps screen on.</param>
    public void AcquireWakeLock(bool keepScreenOn = true)
    {
        lock (_lock)
        {
            if (_isWakeLockHeld)
            {
                Console.WriteLine("[WakeLockService] Wake lock already held");
                return;
            }
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    UIApplication.SharedApplication.IdleTimerDisabled = true;
                    lock (_lock)
                    {
                        _isWakeLockHeld = true;
                    }
                    Console.WriteLine("[WakeLockService] Idle timer disabled (screen will stay on)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WakeLockService] Error in wake lock callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WakeLockService] Error acquiring wake lock: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the wake lock, allowing the device to sleep normally.
    /// </summary>
    public void ReleaseWakeLock()
    {
        lock (_lock)
        {
            if (!_isWakeLockHeld)
                return;
        }

        try
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    UIApplication.SharedApplication.IdleTimerDisabled = false;
                    lock (_lock)
                    {
                        _isWakeLockHeld = false;
                    }
                    Console.WriteLine("[WakeLockService] Idle timer enabled (screen can turn off)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WakeLockService] Error in release wake lock callback: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WakeLockService] Error releasing wake lock: {ex.Message}");
        }
    }
}
