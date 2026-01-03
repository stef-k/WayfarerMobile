using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Shared.Collections;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for MemberDetailsViewModel to access state and operations from GroupsViewModel.
/// Enables member details operations without tight coupling to the parent ViewModel.
/// </summary>
public interface IMemberDetailsCallbacks
{
    #region State Properties

    /// <summary>
    /// Gets the members collection for the selected group.
    /// </summary>
    ObservableRangeCollection<GroupMember> Members { get; }

    /// <summary>
    /// Gets the current location from the location bridge.
    /// </summary>
    LocationData? CurrentLocation { get; }

    #endregion

    #region UI Operations

    /// <summary>
    /// Shows the navigation method picker and returns the selected method.
    /// </summary>
    /// <returns>The selected navigation method, or null if cancelled.</returns>
    Task<NavigationMethod?> ShowNavigationPickerAsync();

    /// <summary>
    /// Sets the source page route for navigation return.
    /// </summary>
    void SetNavigationSourcePage(string route);

    /// <summary>
    /// Navigates to the main map with a route.
    /// </summary>
    /// <param name="route">The calculated route to display.</param>
    Task NavigateToMainMapWithRouteAsync(NavigationRoute route);

    #endregion
}
