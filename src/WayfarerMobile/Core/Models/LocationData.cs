namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a location data point with coordinates and metadata.
/// </summary>
public class LocationData
{
    /// <summary>
    /// Gets or sets the latitude in degrees.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude in degrees.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the altitude in meters above sea level.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the horizontal accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the vertical accuracy in meters.
    /// </summary>
    public double? VerticalAccuracy { get; set; }

    /// <summary>
    /// Gets or sets the speed in meters per second.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the bearing/heading in degrees (0-360).
    /// </summary>
    public double? Bearing { get; set; }

    /// <summary>
    /// Gets or sets the bearing/compass accuracy in degrees.
    /// Lower values = better calibration. Typical range: 1-45 degrees.
    /// Used to determine direction cone width (Google Maps style).
    /// </summary>
    public double? BearingAccuracy { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this location was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the location provider (GPS, Network, Fused, etc.).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Creates a new instance of LocationData with the current timestamp.
    /// </summary>
    public LocationData()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a new instance of LocationData with specified coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    public LocationData(double latitude, double longitude) : this()
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>
    /// Returns a string representation of the location.
    /// </summary>
    public override string ToString()
    {
        return $"({Latitude:F6}, {Longitude:F6}) @ {Timestamp:HH:mm:ss}";
    }
}
