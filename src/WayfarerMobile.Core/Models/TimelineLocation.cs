using System.Text.Json.Serialization;

namespace WayfarerMobile.Core.Models;

/// <summary>
/// Timeline location details for API responses and local display, with explicit originating identity.
/// </summary>
public class TimelineLocation
{
    /// <summary>
    /// Gets or sets the API's server ID, or zero for an unlinked local display entry.
    /// Selection and mutation authority come from <see cref="Identity"/>.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the originating record identity. Numeric display IDs do not grant mutation authority.
    /// Local conversion and the server-response boundary supply this independently of <see cref="Id"/>.
    /// </summary>
    [JsonIgnore]
    public TimelineEntryIdentity Identity { get; set; } = TimelineEntryIdentity.Unknown;

    /// <summary>
    /// Gets or sets the server timestamp (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the local timestamp (converted to user's timezone).
    /// </summary>
    public DateTime LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the coordinates.
    /// </summary>
    public TimelineCoordinates? Coordinates { get; set; }

    /// <summary>
    /// Gets or sets the timezone identifier.
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Gets or sets the accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the altitude.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the speed.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the location type.
    /// </summary>
    public string? LocationType { get; set; }

    /// <summary>
    /// Gets or sets the activity type name.
    /// </summary>
    public string? ActivityType { get; set; }

    /// <summary>
    /// Gets or sets the short address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the full address.
    /// </summary>
    public string? FullAddress { get; set; }

    /// <summary>
    /// Gets or sets the street name.
    /// </summary>
    public string? StreetName { get; set; }

    /// <summary>
    /// Gets or sets the postal code.
    /// </summary>
    public string? PostCode { get; set; }

    /// <summary>
    /// Gets or sets the place (city).
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// Gets or sets the region (state/province).
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the country.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether this is the latest location.
    /// </summary>
    public bool IsLatestLocation { get; set; }

    /// <summary>
    /// Gets or sets the location time threshold (minutes).
    /// </summary>
    public int LocationTimeThresholdMinutes { get; set; }

    /// <summary>
    /// Gets the latitude from coordinates.
    /// </summary>
    [JsonIgnore]
    public double Latitude => Coordinates?.Y ?? 0;

    /// <summary>
    /// Gets the longitude from coordinates.
    /// </summary>
    [JsonIgnore]
    public double Longitude => Coordinates?.X ?? 0;

    /// <summary>
    /// Gets a display string for the location.
    /// </summary>
    [JsonIgnore]
    public string DisplayLocation
    {
        get
        {
            if (!string.IsNullOrEmpty(Place) && !string.IsNullOrEmpty(Country))
                return $"{Place}, {Country}";
            if (!string.IsNullOrEmpty(Place))
                return Place;
            if (!string.IsNullOrEmpty(Country))
                return Country;
            return $"{Latitude:F4}, {Longitude:F4}";
        }
    }
}
