using Mapsui;
using Mapsui.Layers;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for CoordinateEditorViewModel to access parent state.
/// </summary>
public interface ICoordinateEditorCallbacks
{
    /// <summary>
    /// Gets the currently selected location for editing.
    /// </summary>
    TimelineLocationDisplay? SelectedLocation { get; }

    /// <summary>
    /// Gets the temp marker layer for visual feedback during coordinate picking.
    /// </summary>
    WritableLayer? TempMarkerLayer { get; }

    /// <summary>
    /// Gets the map instance for coordinate projection.
    /// </summary>
    Mapsui.Map? MapInstance { get; }

    /// <summary>
    /// Gets whether the app is currently online.
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Gets or sets whether the view model is busy.
    /// </summary>
    bool IsBusy { get; set; }

    /// <summary>
    /// Reloads timeline data after coordinate save.
    /// </summary>
    Task ReloadTimelineAsync();

    /// <summary>
    /// Shows location details sheet for a specific location.
    /// </summary>
    /// <param name="locationId">The location ID to show.</param>
    void ShowLocationDetails(int locationId);

    /// <summary>
    /// Opens the location sheet.
    /// </summary>
    void OpenLocationSheet();
}
