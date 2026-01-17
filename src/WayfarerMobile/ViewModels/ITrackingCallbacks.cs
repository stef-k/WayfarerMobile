using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for TrackingCoordinatorViewModel to access state and operations from MainViewModel.
/// Enables tracking operations without tight coupling to the parent ViewModel.
/// </summary>
public interface ITrackingCallbacks
{
    #region Services

    /// <summary>
    /// Gets the location bridge service for tracking operations.
    /// </summary>
    ILocationBridge LocationBridge { get; }

    /// <summary>
    /// Gets the permissions service for checking and requesting permissions.
    /// </summary>
    IPermissionsService PermissionsService { get; }

    #endregion

    #region Map Operations

    /// <summary>
    /// Clears the location indicator from the map.
    /// </summary>
    void ClearLocationIndicator();

    #endregion

    #region UI Operations

    /// <summary>
    /// Displays an alert dialog and returns true if accepted.
    /// </summary>
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);

    /// <summary>
    /// Opens the app settings page.
    /// </summary>
    void OpenAppSettings();

    #endregion
}
