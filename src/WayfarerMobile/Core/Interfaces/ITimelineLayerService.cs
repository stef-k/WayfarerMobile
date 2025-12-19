using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing timeline-related map layers.
/// Handles timeline location markers for the timeline page.
/// </summary>
public interface ITimelineLayerService
{
    /// <summary>Layer name for timeline locations.</summary>
    string TimelineLayerName { get; }

    /// <summary>
    /// Updates the timeline location markers on the specified layer.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="locations">The list of timeline locations.</param>
    /// <returns>List of points for optional zoom-to-fit.</returns>
    List<MPoint> UpdateTimelineMarkers(WritableLayer layer, IEnumerable<TimelineLocation> locations);

    /// <summary>
    /// Clears all timeline markers.
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearTimelineMarkers(WritableLayer layer);
}
