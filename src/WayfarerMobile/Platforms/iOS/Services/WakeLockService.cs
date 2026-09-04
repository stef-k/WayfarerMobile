using UIKit;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Services;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of wake lock service.
/// Uses UIApplication.IdleTimerDisabled to prevent screen from locking.
/// </summary>
public class WakeLockService : IWakeLockService
{
    private readonly WakeLockOwnership _ownership = new();

    /// <summary>
    /// Gets whether a wake lock is currently held.
    /// </summary>
    public bool IsWakeLockHeld => _ownership.IsHeld;

    /// <summary>
    /// Acquires a wake lock to keep the device awake during navigation.
    /// On iOS, this disables the idle timer to prevent screen from locking.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on. On iOS, this always keeps screen on.</param>
    public bool TryAcquireWakeLock(WakeLockOwner owner, bool keepScreenOn = true)
    {
        return _ownership.TryAcquire(owner, () =>
        {
            try
            {
                MainThread.InvokeOnMainThreadAsync(() =>
                    UIApplication.SharedApplication.IdleTimerDisabled = true).GetAwaiter().GetResult();
                Console.WriteLine("[WakeLockService] Idle timer disabled");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WakeLockService] Error acquiring wake lock: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Releases the wake lock, allowing the device to sleep normally.
    /// </summary>
    public void ReleaseWakeLock(WakeLockOwner owner)
    {
        _ownership.Release(owner, () =>
        {
            try
            {
                MainThread.InvokeOnMainThreadAsync(() =>
                    UIApplication.SharedApplication.IdleTimerDisabled = false).GetAwaiter().GetResult();
                Console.WriteLine("[WakeLockService] Idle timer enabled");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WakeLockService] Error releasing wake lock: {ex.Message}");
                return false;
            }
        });
    }
}
