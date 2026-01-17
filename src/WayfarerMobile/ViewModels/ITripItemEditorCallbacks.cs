using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for TripItemEditorViewModel to access state and operations from TripSheetViewModel.
/// Enables trip item editing operations without tight coupling to the parent ViewModel.
/// </summary>
public interface ITripItemEditorCallbacks
{
    #region State Access

    /// <summary>
    /// Gets the currently loaded trip details.
    /// </summary>
    TripDetails? LoadedTrip { get; }

    /// <summary>
    /// Gets the currently selected place.
    /// </summary>
    TripPlace? SelectedTripPlace { get; }

    /// <summary>
    /// Gets the currently selected area.
    /// </summary>
    TripArea? SelectedTripArea { get; }

    /// <summary>
    /// Gets the currently selected segment.
    /// </summary>
    TripSegment? SelectedTripSegment { get; }

    /// <summary>
    /// Gets the currently selected region.
    /// </summary>
    TripRegion? SelectedTripRegion { get; }

    /// <summary>
    /// Gets the current location data from tracking.
    /// </summary>
    LocationData? CurrentLocation { get; }

    /// <summary>
    /// Gets whether navigation is currently active.
    /// </summary>
    bool IsNavigating { get; }

    #endregion

    #region Selection Control

    /// <summary>
    /// Selects a place in the trip sheet.
    /// </summary>
    void SelectPlace(TripPlace? place);

    /// <summary>
    /// Clears all trip sheet selection.
    /// </summary>
    void ClearSelection();

    /// <summary>
    /// Unloads the current trip from the map and clears all trip state.
    /// </summary>
    void UnloadTrip();

    #endregion

    #region Sheet Control

    /// <summary>
    /// Opens the trip sheet.
    /// </summary>
    void OpenTripSheet();

    /// <summary>
    /// Closes the trip sheet.
    /// </summary>
    void CloseTripSheet();

    #endregion

    #region Map Operations

    /// <summary>
    /// Refreshes the trip layers on the map.
    /// </summary>
    Task RefreshTripLayersAsync(TripDetails? trip);

    /// <summary>
    /// Centers the map on a location.
    /// </summary>
    void CenterOnLocation(double latitude, double longitude, int? zoomLevel = null);

    /// <summary>
    /// Updates the place selection ring on the map.
    /// </summary>
    void UpdatePlaceSelection(TripPlace? place);

    #endregion

    #region Navigation Operations

    /// <summary>
    /// Starts navigation to a trip place.
    /// </summary>
    Task StartNavigationToPlaceAsync(string placeId);

    #endregion

    #region UI Operations

    /// <summary>
    /// Displays an action sheet and returns the selected option.
    /// </summary>
    Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons);

    /// <summary>
    /// Displays a prompt dialog and returns the entered text.
    /// </summary>
    Task<string?> DisplayPromptAsync(string title, string message, string? initialValue = null);

    /// <summary>
    /// Displays an alert dialog and returns true if accepted.
    /// </summary>
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);

    /// <summary>
    /// Navigates to a page using Shell navigation.
    /// </summary>
    Task NavigateToPageAsync(string route, IDictionary<string, object>? parameters = null);

    #endregion

    #region UI Refresh Notifications

    /// <summary>
    /// Notifies that the trip header (name) has changed.
    /// </summary>
    void NotifyTripHeaderChanged();

    /// <summary>
    /// Notifies that the trip places list has changed (add/remove/reorder).
    /// </summary>
    void NotifyTripPlacesChanged();

    /// <summary>
    /// Notifies that the trip regions list has changed (add/remove/reorder).
    /// </summary>
    void NotifyTripRegionsChanged();

    #endregion
}
