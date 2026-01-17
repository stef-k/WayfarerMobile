using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for ContextMenuViewModel to access state and operations from MainViewModel.
/// Enables context menu operations without tight coupling to the parent ViewModel.
/// </summary>
public interface IContextMenuCallbacks
{
    #region State Access

    /// <summary>
    /// Gets the current location data from tracking.
    /// </summary>
    LocationData? CurrentLocation { get; }

    /// <summary>
    /// Gets the location bridge for fallback location access.
    /// </summary>
    ILocationBridge LocationBridge { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Shows a dropped pin marker on the map at the specified coordinates.
    /// </summary>
    void ShowDroppedPin(double latitude, double longitude);

    /// <summary>
    /// Clears the dropped pin marker from the map.
    /// </summary>
    void ClearDroppedPinFromMap();

    #endregion

    #region Navigation Operations

    /// <summary>
    /// Calculates a route to the specified coordinates.
    /// </summary>
    Task<NavigationRoute> CalculateRouteToCoordinatesAsync(
        double fromLat, double fromLon,
        double toLat, double toLon,
        string destinationName,
        string profile);

    /// <summary>
    /// Starts navigation with the calculated route.
    /// </summary>
    Task StartNavigationWithRouteAsync(NavigationRoute route);

    #endregion

    #region UI Operations

    /// <summary>
    /// Gets the toast service for showing notifications.
    /// </summary>
    IToastService ToastService { get; }

    /// <summary>
    /// Shows the navigation method picker and returns the selected method.
    /// </summary>
    Task<NavigationMethod?> ShowNavigationPickerAsync();

    /// <summary>
    /// Sets the busy state indicator.
    /// </summary>
    bool IsBusy { get; set; }

    #endregion
}
