using Android.OS;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Reads storage information for the Android filesystem containing a path.
/// </summary>
public sealed class StorageSpaceProvider : IStorageSpaceProvider
{
    /// <inheritdoc/>
    public bool TryGetAvailableBytes(string path, out long availableBytes)
    {
        using var statistics = new StatFs(path);
        availableBytes = statistics.AvailableBytes;
        return true;
    }
}
