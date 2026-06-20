namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Provides available storage information for the filesystem containing a path.
/// </summary>
public interface IStorageSpaceProvider
{
    /// <summary>
    /// Attempts to get the bytes available to the current application.
    /// </summary>
    /// <param name="path">A path on the filesystem to inspect.</param>
    /// <param name="availableBytes">The available bytes when successful.</param>
    /// <returns>True when storage information was retrieved; otherwise false.</returns>
    bool TryGetAvailableBytes(string path, out long availableBytes);
}
