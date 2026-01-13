namespace WayfarerMobile.Messages;

/// <summary>
/// Message sent when a place marker (icon/color) is changed.
/// Subscribers should refresh the places list and map layers.
/// </summary>
public class PlaceMarkerChangedMessage
{
    /// <summary>
    /// Gets the ID of the place whose marker was changed.
    /// </summary>
    public Guid PlaceId { get; }

    /// <summary>
    /// Gets the trip ID containing the place.
    /// </summary>
    public Guid TripId { get; }

    /// <summary>
    /// Creates a new PlaceMarkerChangedMessage.
    /// </summary>
    /// <param name="placeId">The place ID.</param>
    /// <param name="tripId">The trip ID.</param>
    public PlaceMarkerChangedMessage(Guid placeId, Guid tripId)
    {
        PlaceId = placeId;
        TripId = tripId;
    }
}
