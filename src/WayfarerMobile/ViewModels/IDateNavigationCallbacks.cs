using Mapsui.Layers;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Shared.Collections;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for DateNavigationViewModel to access state and operations from GroupsViewModel.
/// Enables date navigation without tight coupling to the parent ViewModel.
/// </summary>
public interface IDateNavigationCallbacks
{
    #region State Properties

    /// <summary>
    /// Gets the currently selected group.
    /// </summary>
    GroupSummary? SelectedGroup { get; }

    /// <summary>
    /// Gets the members collection for the selected group.
    /// </summary>
    ObservableRangeCollection<GroupMember> Members { get; }

    /// <summary>
    /// Gets whether map view is currently active.
    /// </summary>
    bool IsMapView { get; }

    /// <summary>
    /// Gets the historical locations layer for rendering breadcrumbs.
    /// </summary>
    WritableLayer? HistoricalLocationsLayer { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Clears historical location markers from the map.
    /// </summary>
    void ClearHistoricalLocations();

    /// <summary>
    /// Updates historical location markers on the map.
    /// </summary>
    /// <param name="locations">The locations to display.</param>
    /// <param name="memberColors">Dictionary mapping user IDs to colors.</param>
    void UpdateHistoricalLocationMarkers(List<GroupLocationResult> locations, Dictionary<string, string> memberColors);

    #endregion

    #region Data Operations

    /// <summary>
    /// Refreshes member locations for live mode.
    /// </summary>
    Task RefreshLocationsAsync();

    /// <summary>
    /// Ensures SSE is connected for live updates.
    /// </summary>
    Task EnsureSseConnectedAsync();

    #endregion

    #region Viewport

    /// <summary>
    /// Gets the current cached viewport bounds for historical location queries.
    /// </summary>
    (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? CachedViewportBounds { get; }

    #endregion
}
