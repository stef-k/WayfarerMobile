using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

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

    /// <summary>Owns selection and startup, returning true only after guidance is active.</summary>
    Task<bool> StartNavigationToCoordinatesAsync(double latitude, double longitude,
        string name, Func<Task> externalMaps, Func<WayfarerMobile.Services.HostedRouteCoordinate?> currentTarget);

    #endregion

    #region UI Operations

    /// <summary>
    /// Gets the toast service for showing notifications.
    /// </summary>
    IToastService ToastService { get; }


    /// <summary>
    /// Sets the busy state indicator.
    /// </summary>
    bool IsBusy { get; set; }

    #endregion
}
