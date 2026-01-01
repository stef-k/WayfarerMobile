using Mapsui.Layers;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Service for managing dropped pin marker on the map.
/// Used for the "drop pin" mode where user can tap to place a marker.
/// </summary>
public interface IDroppedPinLayerService
{
    /// <summary>Layer name for dropped pin marker.</summary>
    string DroppedPinLayerName { get; }

    /// <summary>
    /// Shows a dropped pin marker at the specified location.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    void ShowDroppedPin(WritableLayer layer, double latitude, double longitude);

    /// <summary>
    /// Clears the dropped pin marker.
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearDroppedPin(WritableLayer layer);
}
