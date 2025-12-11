namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Represents the current state of the location tracking service.
/// </summary>
public enum TrackingState
{
    /// <summary>
    /// App just installed, tracking never initialized.
    /// </summary>
    NotInitialized,

    /// <summary>
    /// Required permissions have not been granted.
    /// </summary>
    PermissionsNeeded,

    /// <summary>
    /// User denied required permissions.
    /// </summary>
    PermissionsDenied,

    /// <summary>
    /// Has all permissions, ready to start tracking.
    /// </summary>
    Ready,

    /// <summary>
    /// Transitioning from stopped to active.
    /// </summary>
    Starting,

    /// <summary>
    /// GPS running, logging locations.
    /// </summary>
    Active,

    /// <summary>
    /// User paused tracking, service alive but not logging.
    /// </summary>
    Paused,

    /// <summary>
    /// Transitioning from active to stopped.
    /// </summary>
    Stopping,

    /// <summary>
    /// An error occurred during tracking.
    /// </summary>
    Error
}
