using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for MapDisplayViewModel to access state from MainViewModel.
/// Enables map display operations without tight coupling to the parent ViewModel.
/// </summary>
public interface IMapDisplayCallbacks
{
    /// <summary>
    /// Gets the current location data from tracking.
    /// </summary>
    LocationData? CurrentLocation { get; }

    /// <summary>
    /// Gets whether navigation is currently active.
    /// </summary>
    bool IsNavigating { get; }

    /// <summary>
    /// Gets whether a trip is currently loaded.
    /// </summary>
    bool HasLoadedTrip { get; }

    /// <summary>
    /// Gets the trip navigation service for route access.
    /// </summary>
    ITripNavigationService TripNavigationService { get; }
}
