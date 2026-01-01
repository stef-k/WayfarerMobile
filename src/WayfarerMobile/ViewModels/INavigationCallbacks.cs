using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for NavigationCoordinatorViewModel to access state from MainViewModel.
/// Enables navigation operations without tight coupling to the parent ViewModel.
/// </summary>
public interface INavigationCallbacks
{
    #region State Queries

    /// <summary>
    /// Gets the current location data from tracking.
    /// </summary>
    LocationData? CurrentLocation { get; }

    /// <summary>
    /// Gets the currently selected trip place.
    /// </summary>
    TripPlace? SelectedTripPlace { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Shows a navigation route on the map.
    /// </summary>
    void ShowNavigationRoute(NavigationRoute route);

    /// <summary>
    /// Clears the navigation route from the map.
    /// </summary>
    void ClearNavigationRoute();

    /// <summary>
    /// Zooms the map to fit the current navigation route.
    /// </summary>
    void ZoomToNavigationRoute();

    /// <summary>
    /// Updates the navigation route progress display on the map.
    /// </summary>
    void UpdateNavigationRouteProgress(NavigationRoute route, double latitude, double longitude);

    /// <summary>
    /// Sets whether the map should follow location.
    /// </summary>
    void SetFollowingLocation(bool following);

    /// <summary>
    /// Centers the map on a location.
    /// </summary>
    void CenterOnLocation(double latitude, double longitude, int? zoomLevel = null);

    #endregion

    #region Trip Sheet Control

    /// <summary>
    /// Opens the trip sheet.
    /// </summary>
    void OpenTripSheet();

    /// <summary>
    /// Closes the trip sheet.
    /// </summary>
    void CloseTripSheet();

    #endregion
}
