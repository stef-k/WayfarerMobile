namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Represents the health status of the tile cache.
/// </summary>
public enum CacheHealthStatus
{
    /// <summary>
    /// Cache status is unknown (not yet checked).
    /// </summary>
    Unknown,

    /// <summary>
    /// Cache is healthy with good hit rate.
    /// </summary>
    Good,

    /// <summary>
    /// Cache has moderate hit rate or is partially populated.
    /// </summary>
    Warning,

    /// <summary>
    /// Cache has issues - high miss rate and no network fallback.
    /// </summary>
    Poor
}
