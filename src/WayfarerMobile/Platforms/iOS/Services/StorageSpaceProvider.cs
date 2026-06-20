using Foundation;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// Reads storage information for the iOS filesystem containing a path.
/// </summary>
public sealed class StorageSpaceProvider : IStorageSpaceProvider
{
    /// <inheritdoc/>
    public bool TryGetAvailableBytes(string path, out long availableBytes)
    {
        var attributes = NSFileManager.DefaultManager.GetFileSystemAttributes(path, out var error);
        if (attributes == null || error != null)
        {
            availableBytes = 0;
            return false;
        }

        availableBytes = checked((long)attributes.FreeSize);
        return true;
    }
}
