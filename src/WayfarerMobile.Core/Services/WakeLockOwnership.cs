namespace WayfarerMobile.Core.Services;

using WayfarerMobile.Core.Interfaces;

/// <summary>Synchronizes idempotent logical owners around one physical wake lock.</summary>
public sealed class WakeLockOwnership
{
    private readonly Lock _lock = new();
    private readonly HashSet<WakeLockOwner> _owners = [];
    private bool _physicalLockHeld;

    /// <summary>Gets whether the physical wake lock is believed to be held.</summary>
    public bool IsHeld
    {
        get { lock (_lock) return _physicalLockHeld; }
    }

    /// <summary>Adds an owner after ensuring the physical wake lock is held.</summary>
    public bool TryAcquire(WakeLockOwner owner, Func<bool> acquirePhysical)
    {
        lock (_lock)
        {
            if (_owners.Contains(owner))
                return true;

            if (!_physicalLockHeld)
            {
                try
                {
                    if (!acquirePhysical())
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            _physicalLockHeld = true;
            _owners.Add(owner);
            return true;
        }
    }

    /// <summary>Removes an owner and releases the physical lock after the final claim.</summary>
    public void Release(WakeLockOwner owner, Func<bool> releasePhysical)
    {
        lock (_lock)
        {
            if (!_owners.Remove(owner) || _owners.Count > 0 || !_physicalLockHeld)
                return;

            try
            {
                if (releasePhysical())
                    _physicalLockHeld = false;
            }
            catch
            {
                // Preserve the held state because the physical release outcome is unknown.
            }
        }
    }
}
