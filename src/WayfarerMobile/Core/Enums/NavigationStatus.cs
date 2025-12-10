namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Navigation status values.
/// </summary>
public enum NavigationStatus
{
    /// <summary>No active route.</summary>
    NoRoute,

    /// <summary>Following the planned route.</summary>
    OnRoute,

    /// <summary>Off the planned route.</summary>
    OffRoute,

    /// <summary>Arrived at destination.</summary>
    Arrived
}
