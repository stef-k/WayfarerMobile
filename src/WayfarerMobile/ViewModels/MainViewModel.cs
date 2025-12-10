using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using Map = Mapsui.Map;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the main page showing current location and tracking status.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    #region Fields

    private readonly ILocationBridge _locationBridge;
    private readonly MapService _mapService;
    private readonly IPermissionsService _permissionsService;
    private readonly TripNavigationService _tripNavigationService;
    private readonly NavigationHudViewModel _navigationHudViewModel;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Map Map => _mapService.Map;

    /// <summary>
    /// Gets or sets the current tracking state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTracking))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TrackingState _trackingState = TrackingState.NotInitialized;

    /// <summary>
    /// Gets or sets the current location data.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private LocationData? _currentLocation;

    /// <summary>
    /// Gets or sets the current performance mode.
    /// </summary>
    [ObservableProperty]
    private PerformanceMode _performanceMode = PerformanceMode.Normal;

    /// <summary>
    /// Gets or sets the location update count.
    /// </summary>
    [ObservableProperty]
    private int _locationCount;

    /// <summary>
    /// Gets or sets whether to follow location on map.
    /// </summary>
    [ObservableProperty]
    private bool _isFollowingLocation = true;

    /// <summary>
    /// Gets or sets whether navigation is active.
    /// </summary>
    [ObservableProperty]
    private bool _isNavigating;

    /// <summary>
    /// Gets or sets whether the trip sidebar is visible.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    private bool _isTripSidebarVisible;

    /// <summary>
    /// Gets or sets the currently loaded trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    [NotifyPropertyChangedFor(nameof(TripPlaceCount))]
    private TripDetails? _loadedTrip;

    /// <summary>
    /// Gets or sets the selected place in the sidebar.
    /// </summary>
    [ObservableProperty]
    private TripPlace? _selectedPlace;

    /// <summary>
    /// Gets the navigation HUD view model for binding.
    /// </summary>
    public NavigationHudViewModel NavigationHud => _navigationHudViewModel;

    /// <summary>
    /// Gets whether tracking is currently active.
    /// </summary>
    public bool IsTracking => TrackingState == TrackingState.Active;

    /// <summary>
    /// Gets the tracking button text based on current state.
    /// </summary>
    public string TrackingButtonText => TrackingState switch
    {
        TrackingState.Active => "Stop Tracking",
        TrackingState.Paused => "Resume Tracking",
        _ => "Start Tracking"
    };

    /// <summary>
    /// Gets the status text based on current state.
    /// </summary>
    public string StatusText => TrackingState switch
    {
        TrackingState.NotInitialized => "Not initialized",
        TrackingState.PermissionsNeeded => "Permissions required",
        TrackingState.PermissionsDenied => "Permissions denied",
        TrackingState.Ready => "Ready to track",
        TrackingState.Starting => "Starting...",
        TrackingState.Active => "Tracking active",
        TrackingState.Paused => "Tracking paused",
        TrackingState.Stopping => "Stopping...",
        TrackingState.Error => "Error occurred",
        _ => "Unknown"
    };

    /// <summary>
    /// Gets the location text to display.
    /// </summary>
    public string LocationText
    {
        get
        {
            if (CurrentLocation == null)
                return "No location yet";

            return $"{CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6}";
        }
    }

    /// <summary>
    /// Gets whether a trip is currently loaded.
    /// </summary>
    public bool HasLoadedTrip => LoadedTrip != null;

    /// <summary>
    /// Gets the number of places in the loaded trip.
    /// </summary>
    public int TripPlaceCount => LoadedTrip?.AllPlaces.Count ?? 0;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MainViewModel.
    /// </summary>
    /// <param name="locationBridge">The location bridge service.</param>
    /// <param name="mapService">The map service.</param>
    /// <param name="permissionsService">The permissions service.</param>
    /// <param name="tripNavigationService">The trip navigation service.</param>
    /// <param name="navigationHudViewModel">The navigation HUD view model.</param>
    public MainViewModel(
        ILocationBridge locationBridge,
        MapService mapService,
        IPermissionsService permissionsService,
        TripNavigationService tripNavigationService,
        NavigationHudViewModel navigationHudViewModel)
    {
        _locationBridge = locationBridge;
        _mapService = mapService;
        _permissionsService = permissionsService;
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        Title = "WayfarerMobile";

        // Subscribe to location events
        _locationBridge.LocationReceived += OnLocationReceived;
        _locationBridge.StateChanged += OnStateChanged;

        // Subscribe to navigation HUD events
        _navigationHudViewModel.StopNavigationRequested += OnStopNavigationRequested;

        // Set default map zoom
        _mapService.SetDefaultZoom();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles location updates from the service.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        CurrentLocation = location;
        LocationCount++;

        // Update map
        _mapService.UpdateLocation(location, centerMap: IsFollowingLocation && !IsNavigating);

        // Update navigation if active
        if (IsNavigating)
        {
            var state = _tripNavigationService.UpdateLocation(location.Latitude, location.Longitude);
            _mapService.UpdateNavigationRouteProgress(location.Latitude, location.Longitude);

            // Check for arrival
            if (state.Status == NavigationStatus.Arrived)
            {
                StopNavigation();
            }
        }
    }

    /// <summary>
    /// Handles stop navigation request from HUD.
    /// </summary>
    private void OnStopNavigationRequested(object? sender, EventArgs e)
    {
        StopNavigation();
    }

    /// <summary>
    /// Handles state changes from the service.
    /// </summary>
    private void OnStateChanged(object? sender, TrackingState state)
    {
        TrackingState = state;

        // Clear track when stopping
        if (state == TrackingState.Ready || state == TrackingState.NotInitialized)
        {
            _mapService.ClearTrack();
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Toggles tracking on/off.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            if (TrackingState == TrackingState.Active)
            {
                await _locationBridge.StopAsync();
            }
            else if (TrackingState == TrackingState.Paused)
            {
                await _locationBridge.ResumeAsync();
            }
            else
            {
                // Check and request permissions before starting
                var permissionsGranted = await CheckAndRequestPermissionsAsync();
                if (!permissionsGranted)
                {
                    TrackingState = TrackingState.PermissionsDenied;
                    return;
                }

                await _locationBridge.StartAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Checks and requests required permissions.
    /// </summary>
    /// <returns>True if all required permissions are granted.</returns>
    private async Task<bool> CheckAndRequestPermissionsAsync()
    {
        // Check if already granted
        if (await _permissionsService.AreTrackingPermissionsGrantedAsync())
        {
            return true;
        }

        // Request permissions
        var result = await _permissionsService.RequestTrackingPermissionsAsync();

        if (!result.LocationGranted)
        {
            // Show alert about required permissions
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                var openSettings = await page.DisplayAlertAsync(
                    "Location Permission Required",
                    "WayfarerMobile needs location permission to track your movements. Please grant permission in Settings.",
                    "Open Settings",
                    "Cancel");

                if (openSettings)
                {
                    _permissionsService.OpenAppSettings();
                }
            }
            return false;
        }

        return result.AllGranted || result.LocationGranted;
    }

    /// <summary>
    /// Opens app settings for permission management.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _permissionsService.OpenAppSettings();
    }

    /// <summary>
    /// Pauses tracking.
    /// </summary>
    [RelayCommand]
    private async Task PauseTrackingAsync()
    {
        if (TrackingState == TrackingState.Active)
        {
            await _locationBridge.PauseAsync();
        }
    }

    /// <summary>
    /// Sets the performance mode.
    /// </summary>
    /// <param name="mode">The mode to set.</param>
    [RelayCommand]
    private async Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        PerformanceMode = mode;
        await _locationBridge.SetPerformanceModeAsync(mode);
    }

    /// <summary>
    /// Centers the map on current location.
    /// </summary>
    [RelayCommand]
    private void CenterOnLocation()
    {
        if (CurrentLocation != null)
        {
            _mapService.CenterOnLocation(CurrentLocation);
            IsFollowingLocation = true;
        }
    }

    /// <summary>
    /// Zooms the map to show the entire track.
    /// </summary>
    [RelayCommand]
    private void ZoomToTrack()
    {
        _mapService.ZoomToTrack();
        IsFollowingLocation = false;
    }

    /// <summary>
    /// Toggles the trip sidebar visibility.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTripSidebarAsync()
    {
        // If sidebar is visible, hide it
        if (IsTripSidebarVisible)
        {
            IsTripSidebarVisible = false;
            return;
        }

        // If no trip is loaded, prompt user to select one
        if (!HasLoadedTrip)
        {
            await Shell.Current.GoToAsync("trips");
            return;
        }

        // Show the sidebar with the loaded trip
        IsTripSidebarVisible = true;
    }

    /// <summary>
    /// Selects a place from the sidebar and centers the map on it.
    /// </summary>
    [RelayCommand]
    private void SelectPlace(TripPlace? place)
    {
        if (place == null)
            return;

        SelectedPlace = place;

        // Center map on selected place
        var location = new LocationData
        {
            Latitude = place.Latitude,
            Longitude = place.Longitude
        };
        _mapService.CenterOnLocation(location);
        IsFollowingLocation = false;
    }

    /// <summary>
    /// Navigates to the selected place.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToSelectedPlaceAsync()
    {
        if (SelectedPlace == null)
            return;

        await StartNavigationToPlaceAsync(SelectedPlace.Id.ToString());
        IsTripSidebarVisible = false;
    }

    /// <summary>
    /// Clears the currently loaded trip.
    /// </summary>
    [RelayCommand]
    private void ClearLoadedTrip()
    {
        UnloadTrip();
        LoadedTrip = null;
        SelectedPlace = null;
        IsTripSidebarVisible = false;
    }

    /// <summary>
    /// Opens the check-in page to record current location.
    /// </summary>
    [RelayCommand]
    private async Task CheckInAsync()
    {
        await Shell.Current.GoToAsync("checkin");
    }

    /// <summary>
    /// Starts navigation to a specific place.
    /// </summary>
    /// <param name="placeId">The place ID to navigate to.</param>
    [RelayCommand]
    private async Task StartNavigationToPlaceAsync(string placeId)
    {
        if (CurrentLocation == null)
        {
            // Need location to start navigation
            return;
        }

        if (!_tripNavigationService.IsTripLoaded)
        {
            // Need a trip loaded first
            return;
        }

        var route = _tripNavigationService.CalculateRouteToPlace(
            CurrentLocation.Latitude,
            CurrentLocation.Longitude,
            placeId);

        if (route != null)
        {
            IsNavigating = true;
            _mapService.ShowNavigationRoute(route);
            _mapService.ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            IsFollowingLocation = false; // Don't auto-center during navigation
        }
    }

    /// <summary>
    /// Starts navigation to the next place in the trip sequence.
    /// </summary>
    [RelayCommand]
    private async Task StartNavigationToNextAsync()
    {
        if (CurrentLocation == null || !_tripNavigationService.IsTripLoaded)
            return;

        var route = _tripNavigationService.CalculateRouteToNextPlace(
            CurrentLocation.Latitude,
            CurrentLocation.Longitude);

        if (route != null)
        {
            IsNavigating = true;
            _mapService.ShowNavigationRoute(route);
            _mapService.ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            IsFollowingLocation = false;
        }
    }

    /// <summary>
    /// Stops current navigation.
    /// </summary>
    [RelayCommand]
    private void StopNavigation()
    {
        IsNavigating = false;
        _mapService.ClearNavigationRoute();
        _navigationHudViewModel.StopNavigationDisplay();
        IsFollowingLocation = true;
    }

    /// <summary>
    /// Loads a trip for navigation.
    /// </summary>
    /// <param name="tripDetails">The trip details to load.</param>
    public async Task LoadTripForNavigationAsync(TripDetails tripDetails)
    {
        LoadedTrip = tripDetails;
        _tripNavigationService.LoadTrip(tripDetails);
        await _mapService.UpdateTripPlacesAsync(tripDetails.AllPlaces);
    }

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    public void UnloadTrip()
    {
        if (IsNavigating)
        {
            StopNavigation();
        }

        LoadedTrip = null;
        SelectedPlace = null;
        _tripNavigationService.UnloadTrip();
        _mapService.ClearTripPlaces();
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Update state from bridge
        TrackingState = _locationBridge.CurrentState;
        CurrentLocation = _locationBridge.LastLocation;

        // Check permissions state
        await CheckPermissionsStateAsync();

        // Update map if we have a location
        if (CurrentLocation != null)
        {
            _mapService.UpdateLocation(CurrentLocation, centerMap: true);
        }

        // Set high performance mode for real-time updates when map is visible
        if (TrackingState == TrackingState.Active)
        {
            await _locationBridge.SetPerformanceModeAsync(PerformanceMode.HighPerformance);
            PerformanceMode = PerformanceMode.HighPerformance;
        }

        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override async Task OnDisappearingAsync()
    {
        // Set normal mode to conserve battery when map is not visible
        if (TrackingState == TrackingState.Active)
        {
            await _locationBridge.SetPerformanceModeAsync(PerformanceMode.Normal);
            PerformanceMode = PerformanceMode.Normal;
        }

        await base.OnDisappearingAsync();
    }

    /// <summary>
    /// Checks the current permissions state and updates TrackingState accordingly.
    /// </summary>
    private async Task CheckPermissionsStateAsync()
    {
        // Only update state if not actively tracking
        if (TrackingState == TrackingState.Active || TrackingState == TrackingState.Paused)
            return;

        var hasPermissions = await _permissionsService.AreTrackingPermissionsGrantedAsync();

        if (!hasPermissions)
        {
            TrackingState = TrackingState.PermissionsNeeded;
        }
        else if (TrackingState == TrackingState.PermissionsNeeded || TrackingState == TrackingState.PermissionsDenied)
        {
            // Permissions were granted, update to ready state
            TrackingState = TrackingState.Ready;
        }
    }

    #endregion
}
