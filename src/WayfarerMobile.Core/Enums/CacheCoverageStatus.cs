namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Represents the coverage status of the tile cache at a specific location.
/// Different from CacheHealthStatus which represents overall cache health.
/// </summary>
public enum CacheCoverageStatus
{
    /// <summary>
    /// Coverage status is unknown (not yet checked).
    /// </summary>
    Unknown,

    /// <summary>
    /// No tiles cached for this location.
    /// </summary>
    None,

    /// <summary>
    /// Poor coverage - less than 40% of tiles cached.
    /// </summary>
    Poor,

    /// <summary>
    /// Partial coverage - 40% to 70% of tiles cached.
    /// </summary>
    Partial,

    /// <summary>
    /// Good coverage - 70% to 90% of tiles cached.
    /// </summary>
    Good,

    /// <summary>
    /// Excellent coverage - 90% or more of tiles cached.
    /// </summary>
    Excellent,

    /// <summary>
    /// Error occurred during coverage check.
    /// </summary>
    Error
}
