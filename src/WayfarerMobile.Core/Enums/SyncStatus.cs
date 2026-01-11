namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Represents the synchronization status of a queued location.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Location is queued and waiting to be synced.
    /// </summary>
    Pending,

    /// <summary>
    /// Location is currently being synced to server.
    /// </summary>
    Syncing,

    /// <summary>
    /// Location was successfully synced to server.
    /// </summary>
    Synced
}
