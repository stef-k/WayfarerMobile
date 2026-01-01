using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for navigation coordination.
/// Manages navigation state, route calculation, and HUD control.
/// Extracted from MainViewModel to handle navigation-specific concerns.
/// </summary>
public partial class NavigationCoordinatorViewModel : BaseViewModel
{
    #region Fields

    private readonly ITripNavigationService _tripNavigationService;
    private readonly NavigationHudViewModel _navigationHudViewModel;
    private readonly IVisitNotificationService _visitNotificationService;
    private readonly ILogger<NavigationCoordinatorViewModel> _logger;

    // Callbacks to parent ViewModel
    private INavigationCallbacks? _callbacks;

    // Navigation state for visit notification conflict detection
    private Guid? _currentNavigationPlaceId;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether navigation is currently active.
    /// </summary>
    [ObservableProperty]
    private bool _isNavigating;

    /// <summary>
    /// Gets the navigation HUD ViewModel.
    /// </summary>
    public NavigationHudViewModel NavigationHud => _navigationHudViewModel;

    /// <summary>
    /// Gets whether a trip is loaded and ready for navigation.
    /// </summary>
    public bool IsTripLoaded => _tripNavigationService.IsTripLoaded;

    /// <summary>
    /// Gets the active navigation route.
    /// </summary>
    public NavigationRoute? ActiveRoute => _tripNavigationService.ActiveRoute;

    #endregion

    #region Events

    /// <summary>
    /// Raised when navigation stops and shell navigation is requested.
    /// </summary>
    public event EventHandler<string?>? NavigateToSourcePageRequested;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of NavigationCoordinatorViewModel.
    /// </summary>
    public NavigationCoordinatorViewModel(
        ITripNavigationService tripNavigationService,
        NavigationHudViewModel navigationHudViewModel,
        IVisitNotificationService visitNotificationService,
        ILogger<NavigationCoordinatorViewModel> logger)
    {
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        _visitNotificationService = visitNotificationService;
        _logger = logger;

        // Subscribe to HUD stop navigation request
        _navigationHudViewModel.StopNavigationRequested += OnStopNavigationRequested;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Sets the callback interface to the parent ViewModel.
    /// Must be called before using methods that depend on parent state.
    /// </summary>
    public void SetCallbacks(INavigationCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Starts navigation to a specific place.
    /// </summary>
    /// <param name="placeId">The place ID to navigate to.</param>
    [RelayCommand]
    public async Task StartNavigationToPlaceAsync(string placeId)
    {
        var currentLocation = _callbacks?.CurrentLocation;
        if (currentLocation == null)
        {
            _logger.LogDebug("Cannot start navigation: no current location");
            return;
        }

        if (!_tripNavigationService.IsTripLoaded)
        {
            _logger.LogDebug("Cannot start navigation: no trip loaded");
            return;
        }

        var route = _tripNavigationService.CalculateRouteToPlace(
            currentLocation.Latitude,
            currentLocation.Longitude,
            placeId);

        if (route != null)
        {
            // Track navigation destination for visit notification conflict detection
            _currentNavigationPlaceId = Guid.TryParse(placeId, out var guid) ? guid : null;
            _visitNotificationService.UpdateNavigationState(true, _currentNavigationPlaceId);

            IsNavigating = true;
            _callbacks?.ShowNavigationRoute(route);
            _callbacks?.ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            _callbacks?.SetFollowingLocation(false); // Don't auto-center during navigation

            _logger.LogInformation("Started navigation to place {PlaceId}", placeId);
        }
    }

    /// <summary>
    /// Starts navigation to the next place in the trip sequence.
    /// </summary>
    [RelayCommand]
    public async Task StartNavigationToNextAsync()
    {
        var currentLocation = _callbacks?.CurrentLocation;
        if (currentLocation == null || !_tripNavigationService.IsTripLoaded)
        {
            _logger.LogDebug("Cannot start navigation to next: no location or trip");
            return;
        }

        var route = _tripNavigationService.CalculateRouteToNextPlace(
            currentLocation.Latitude,
            currentLocation.Longitude);

        if (route != null)
        {
            // Track navigation destination for visit notification conflict detection
            // Note: For "next place" we don't have the place ID readily available
            _currentNavigationPlaceId = null;
            _visitNotificationService.UpdateNavigationState(true, null);

            IsNavigating = true;
            _callbacks?.ShowNavigationRoute(route);
            _callbacks?.ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            _callbacks?.SetFollowingLocation(false);

            _logger.LogInformation("Started navigation to next place");
        }
    }

    /// <summary>
    /// Stops current navigation and returns to the prior state.
    /// If navigating to a trip place, zooms back to that place and shows the sheet.
    /// </summary>
    [RelayCommand]
    public void StopNavigation()
    {
        // Notify visit notification service that navigation ended
        _currentNavigationPlaceId = null;
        _visitNotificationService.UpdateNavigationState(false, null);

        IsNavigating = false;
        _callbacks?.ClearNavigationRoute();
        _navigationHudViewModel.StopNavigationDisplay();

        // Return to the selected trip place if one exists
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace != null)
        {
            // Zoom to the selected place
            _callbacks?.CenterOnLocation(selectedPlace.Latitude, selectedPlace.Longitude, zoomLevel: 15);

            // Re-open the trip sheet to show place details
            _callbacks?.OpenTripSheet();
        }
        else
        {
            _callbacks?.SetFollowingLocation(true);
        }

        _logger.LogInformation("Stopped navigation");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Updates navigation state when location changes.
    /// Called from MainViewModel.OnLocationReceived.
    /// </summary>
    /// <param name="latitude">Current latitude.</param>
    /// <param name="longitude">Current longitude.</param>
    public void UpdateLocation(double latitude, double longitude)
    {
        if (!IsNavigating)
            return;

        var state = _tripNavigationService.UpdateLocation(latitude, longitude);

        // Update route progress on map
        var route = _tripNavigationService.ActiveRoute;
        if (route != null)
        {
            _callbacks?.UpdateNavigationRouteProgress(route, latitude, longitude);
        }

        // Check for arrival
        if (state.Status == NavigationStatus.Arrived)
        {
            _logger.LogInformation("Arrived at destination");
            StopNavigation();
        }
    }

    /// <summary>
    /// Calculates a route to arbitrary coordinates (for non-trip navigation like dropped pins).
    /// </summary>
    public async Task<NavigationRoute> CalculateRouteToCoordinatesAsync(
        double fromLat, double fromLon,
        double toLat, double toLon,
        string destinationName,
        string profile = "foot")
    {
        return await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            fromLat, fromLon,
            toLat, toLon,
            destinationName,
            profile);
    }

    /// <summary>
    /// Starts navigation with a pre-calculated route (for non-trip navigation).
    /// </summary>
    public async Task StartNavigationWithRouteAsync(NavigationRoute route)
    {
        _currentNavigationPlaceId = null;
        _visitNotificationService.UpdateNavigationState(true, null);

        IsNavigating = true;
        _callbacks?.ShowNavigationRoute(route);
        _callbacks?.ZoomToNavigationRoute();
        await _navigationHudViewModel.StartNavigationAsync(route);
        _callbacks?.SetFollowingLocation(false);

        _logger.LogInformation("Started navigation to {Destination}: {Distance:F1}km",
            route.DestinationName, route.TotalDistanceMeters / 1000);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles stop navigation request from HUD.
    /// </summary>
    private void OnStopNavigationRequested(object? sender, string? sourcePageRoute)
    {
        StopNavigation();

        // Notify parent to handle shell navigation if needed
        if (!string.IsNullOrEmpty(sourcePageRoute))
        {
            NavigateToSourcePageRequested?.Invoke(this, sourcePageRoute);
        }
    }

    #endregion

    #region Cleanup

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _navigationHudViewModel.StopNavigationRequested -= OnStopNavigationRequested;
        _navigationHudViewModel.Dispose();
        base.Cleanup();
    }

    #endregion
}
