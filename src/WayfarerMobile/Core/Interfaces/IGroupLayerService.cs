using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service for managing group-related map layers.
/// Handles group member markers and historical location breadcrumbs.
/// </summary>
public interface IGroupLayerService
{
    /// <summary>Layer name for group member markers.</summary>
    string GroupMembersLayerName { get; }

    /// <summary>Layer name for historical location breadcrumbs.</summary>
    string HistoricalLocationsLayerName { get; }

    /// <summary>
    /// Updates group member markers on the specified layer.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="members">The group members to display.</param>
    /// <returns>List of points for optional zoom-to-fit.</returns>
    List<MPoint> UpdateGroupMemberMarkers(WritableLayer layer, IEnumerable<GroupMemberLocation> members);

    /// <summary>
    /// Updates historical location markers on the specified layer.
    /// </summary>
    /// <param name="layer">The layer to update.</param>
    /// <param name="locations">The historical locations to display.</param>
    /// <param name="memberColors">Dictionary mapping user IDs to color hex strings.</param>
    void UpdateHistoricalLocationMarkers(
        WritableLayer layer,
        IEnumerable<GroupLocationResult> locations,
        Dictionary<string, string> memberColors);
}
