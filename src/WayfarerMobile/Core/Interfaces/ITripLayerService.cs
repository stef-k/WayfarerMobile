using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing trip-related map layers.
/// Handles trip place markers and segment polylines.
/// </summary>
public interface ITripLayerService
{
    /// <summary>Layer name for trip place markers.</summary>
    string TripPlacesLayerName { get; }

    /// <summary>Layer name for trip segment polylines.</summary>
    string TripSegmentsLayerName { get; }

    /// <summary>
    /// Updates the trip place markers on the specified layer.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="places">The list of trip places.</param>
    /// <returns>List of points for optional zoom-to-fit.</returns>
    Task<List<MPoint>> UpdateTripPlacesAsync(WritableLayer layer, IEnumerable<TripPlace> places);

    /// <summary>
    /// Clears all trip place markers.
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearTripPlaces(WritableLayer layer);

    /// <summary>
    /// Updates the trip segments on the specified layer.
    /// Segments are drawn as polylines between places, with different styles per transport mode.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="segments">The list of trip segments with geometry.</param>
    void UpdateTripSegments(WritableLayer layer, IEnumerable<TripSegment> segments);

    /// <summary>
    /// Clears all trip segments.
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearTripSegments(WritableLayer layer);

    /// <summary>
    /// Gets priority icons that have been validated to exist in app resources.
    /// Results are cached after first call.
    /// </summary>
    /// <param name="color">Color variant to validate (default: bg-blue).</param>
    /// <returns>Array of priority icon names that exist.</returns>
    Task<string[]> GetValidatedPriorityIconsAsync(string? color = null);
}
