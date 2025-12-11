namespace WayfarerMobile.Core.Enums;

/// <summary>
/// Detected transportation mode based on speed and movement patterns.
/// Used for GPS accuracy filtering and threshold adjustments.
/// </summary>
public enum TransportationMode
{
    /// <summary>
    /// Stationary or very slow movement (less than 1 m/s).
    /// </summary>
    Stationary,

    /// <summary>
    /// Walking speed (1-2 m/s, ~3.6-7.2 km/h).
    /// </summary>
    Walking,

    /// <summary>
    /// Cycling speed (2-10 m/s, ~7.2-36 km/h).
    /// </summary>
    Cycling,

    /// <summary>
    /// Driving in vehicle (10-40 m/s, ~36-144 km/h).
    /// </summary>
    Driving,

    /// <summary>
    /// High-speed train (40-100 m/s, ~144-360 km/h).
    /// </summary>
    Train,

    /// <summary>
    /// Air travel (over 100 m/s, ~360 km/h).
    /// </summary>
    Air
}
