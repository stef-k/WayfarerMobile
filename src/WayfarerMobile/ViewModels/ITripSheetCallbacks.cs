using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for TripSheetViewModel to access state and operations from MainViewModel.
/// Enables trip sheet operations without tight coupling to the parent ViewModel.
/// </summary>
public interface ITripSheetCallbacks
{
    #region State Queries

    /// <summary>
    /// Gets the current location data from tracking.
    /// </summary>
    LocationData? CurrentLocation { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Centers the map on a location.
    /// </summary>
    void CenterOnLocation(double latitude, double longitude, int? zoomLevel = null);

    /// <summary>
    /// Updates the place selection ring on the map.
    /// </summary>
    void UpdatePlaceSelection(TripPlace? place);

    /// <summary>
    /// Clears the place selection ring from the map.
    /// </summary>
    void ClearPlaceSelection();

    /// <summary>
    /// Sets whether the map should follow location.
    /// </summary>
    void SetFollowingLocation(bool following);

    /// <summary>
    /// Refreshes the trip layers on the map.
    /// </summary>
    Task RefreshTripLayersAsync(TripDetails? trip);

    /// <summary>
    /// Clears all trip layers from the map.
    /// </summary>
    void UnloadTripFromMap();

    #endregion

    #region Navigation Operations

    /// <summary>
    /// Starts navigation to a trip place.
    /// </summary>
    Task StartNavigationToPlaceAsync(string placeId);

    /// <summary>
    /// Gets whether navigation is currently active.
    /// </summary>
    bool IsNavigating { get; }

    #endregion

    #region Shell/UI Operations

    /// <summary>
    /// Navigates to a page using Shell navigation.
    /// </summary>
    Task NavigateToPageAsync(string route, IDictionary<string, object>? parameters = null);

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

    #endregion
}
