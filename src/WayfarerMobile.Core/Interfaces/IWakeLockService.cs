namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing device wake locks during navigation.
/// Prevents the device from sleeping during active navigation.
/// </summary>
public interface IWakeLockService
{
    /// <summary>
    /// Gets whether a wake lock is currently held.
    /// </summary>
    bool IsWakeLockHeld { get; }

    /// <summary>
    /// Acquires a wake lock to keep the device awake during navigation.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on (true) or just CPU (false).</param>
    bool TryAcquireWakeLock(WakeLockOwner owner, bool keepScreenOn = true);

    /// <summary>
    /// Releases the wake lock, allowing the device to sleep normally.
    /// </summary>
    void ReleaseWakeLock(WakeLockOwner owner);
}

/// <summary>Identifies the current application behavior that owns a wake-lock claim.</summary>
public enum WakeLockOwner
{
    /// <summary>The persistent Keep Screen On application behavior.</summary>
    Persistent,

    /// <summary>An active in-app navigation session.</summary>
    Navigation
}
