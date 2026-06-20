namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Evaluates whether a filesystem has enough available storage.
/// </summary>
public interface IStorageSpaceService
{
    /// <summary>
    /// Checks whether the filesystem containing a path meets a byte requirement.
    /// Storage lookup failures fail open.
    /// </summary>
    bool HasSufficientStorage(string path, long requiredBytes);
}
