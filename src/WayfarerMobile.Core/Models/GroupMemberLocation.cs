namespace WayfarerMobile.Core.Models;

/// <summary>
/// Represents a group member's location for map display.
/// </summary>
public class GroupMemberLocation
{
    /// <summary>
    /// Gets or sets the member's user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the marker color in hex format.
    /// </summary>
    public string? ColorHex { get; set; }

    /// <summary>
    /// Gets or sets whether this is a live location.
    /// </summary>
    public bool IsLive { get; set; }
}
