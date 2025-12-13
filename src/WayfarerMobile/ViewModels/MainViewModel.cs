using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using WayfarerMobile.Services.TileCache;
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
    private readonly IToastService _toastService;
    private readonly CheckInViewModel _checkInViewModel;
    private readonly UnifiedTileCacheService _tileCacheService;

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
    [NotifyPropertyChangedFor(nameof(TrackingButtonIcon))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonImage))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonColor))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TrackingState _trackingState = TrackingState.NotInitialized;

    /// <summary>
    /// Gets or sets the current location data.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    [NotifyPropertyChangedFor(nameof(AccuracyText))]
    [NotifyPropertyChangedFor(nameof(HeadingText))]
    [NotifyPropertyChangedFor(nameof(AltitudeText))]
    [NotifyPropertyChangedFor(nameof(HasAccuracy))]
    [NotifyPropertyChangedFor(nameof(HasHeading))]
    [NotifyPropertyChangedFor(nameof(HasAltitude))]
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
    /// Gets or sets whether drop pin mode is active.
    /// When active, tapping the map shows a context menu at that location.
    /// </summary>
    [ObservableProperty]
    private bool _isDropPinModeActive;

    /// <summary>
    /// Gets or sets whether the context menu is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isContextMenuVisible;

    /// <summary>
    /// Gets or sets whether the check-in sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isCheckInSheetOpen;

    /// <summary>
    /// Gets or sets the context menu latitude.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLatitude;

    /// <summary>
    /// Gets or sets the context menu longitude.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLongitude;

    /// <summary>
    /// Gets or sets whether a dropped pin is visible on the map.
    /// Pin persists after context menu closes and can be tapped to reopen menu.
    /// </summary>
    [ObservableProperty]
    private bool _hasDroppedPin;

    /// <summary>
    /// Gets or sets the dropped pin latitude.
    /// </summary>
    [ObservableProperty]
    private double _droppedPinLatitude;

    /// <summary>
    /// Gets or sets the dropped pin longitude.
    /// </summary>
    [ObservableProperty]
    private double _droppedPinLongitude;

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
    /// Gets the check-in view model for binding.
    /// </summary>
    public CheckInViewModel CheckInViewModel => _checkInViewModel;

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
    /// Gets the tracking button icon based on current state.
    /// </summary>
    public string TrackingButtonIcon => TrackingState switch
    {
        TrackingState.Active => "⏹",
        TrackingState.Paused => "▶",
        _ => "▶"
    };

    /// <summary>
    /// Gets the tracking button image source based on current state.
    /// </summary>
    public string TrackingButtonImage => TrackingState switch
    {
        TrackingState.Active => "stop.png",
        TrackingState.Paused => "play_start.png",
        _ => "play_start.png"
    };

    /// <summary>
    /// Gets the tracking button color based on current state.
    /// </summary>
    public Color TrackingButtonColor => TrackingState switch
    {
        TrackingState.Active => Colors.Red,
        TrackingState.Paused => Colors.Orange,
        _ => Application.Current?.Resources["Primary"] as Color ?? Colors.Blue
    };

    /// <summary>
    /// Gets the status text based on current state.
    /// </summary>
    public string StatusText => TrackingState switch
    {
        TrackingState.NotInitialized => "Ready",
        TrackingState.PermissionsNeeded => "Permissions required",
        TrackingState.PermissionsDenied => "Permissions denied",
        TrackingState.Ready => "Ready",
        TrackingState.Starting => "Starting...",
        TrackingState.Active => "Tracking",
        TrackingState.Paused => "Paused",
        TrackingState.Stopping => "Stopping...",
        TrackingState.Error => "Error",
        _ => "Ready"
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
    /// Gets the accuracy text to display.
    /// </summary>
    public string AccuracyText
    {
        get
        {
            if (CurrentLocation?.Accuracy == null)
                return string.Empty;

            return $"±{CurrentLocation.Accuracy:F0}m";
        }
    }

    /// <summary>
    /// Gets the heading/bearing text to display.
    /// </summary>
    public string HeadingText
    {
        get
        {
            if (CurrentLocation?.Bearing == null)
                return string.Empty;

            var bearing = CurrentLocation.Bearing.Value;
            var direction = bearing switch
            {
                >= 337.5 or < 22.5 => "N",
                >= 22.5 and < 67.5 => "NE",
                >= 67.5 and < 112.5 => "E",
                >= 112.5 and < 157.5 => "SE",
                >= 157.5 and < 202.5 => "S",
                >= 202.5 and < 247.5 => "SW",
                >= 247.5 and < 292.5 => "W",
                _ => "NW"
            };
            return $"{bearing:F0}° {direction}";
        }
    }

    /// <summary>
    /// Gets the altitude text to display.
    /// </summary>
    public string AltitudeText
    {
        get
        {
            if (CurrentLocation?.Altitude == null)
                return string.Empty;

            return $"{CurrentLocation.Altitude:F0}m";
        }
    }

    /// <summary>
    /// Gets whether accuracy is available.
    /// </summary>
    public bool HasAccuracy => CurrentLocation?.Accuracy != null;

    /// <summary>
    /// Gets whether heading is available.
    /// </summary>
    public bool HasHeading => CurrentLocation?.Bearing != null;

    /// <summary>
    /// Gets whether altitude is available.
    /// </summary>
    public bool HasAltitude => CurrentLocation?.Altitude != null;

    /// <summary>
    /// Gets or sets the cache health status (green/orange/red).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheHealthColor))]
    [NotifyPropertyChangedFor(nameof(CacheHealthTooltip))]
    private CacheHealthStatus _cacheHealth = CacheHealthStatus.Unknown;

    /// <summary>
    /// Gets the cache health indicator color.
    /// </summary>
    public Color CacheHealthColor => CacheHealth switch
    {
        CacheHealthStatus.Good => Colors.LimeGreen,
        CacheHealthStatus.Warning => Colors.Orange,
        CacheHealthStatus.Poor => Colors.Red,
        _ => Colors.Gray
    };

    /// <summary>
    /// Gets the cache health tooltip text.
    /// </summary>
    public string CacheHealthTooltip => CacheHealth switch
    {
        CacheHealthStatus.Good => "Cache healthy",
        CacheHealthStatus.Warning => "Cache partial",
        CacheHealthStatus.Poor => "Cache issues",
        _ => "Cache status unknown"
    };

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
    /// <param name="toastService">The toast notification service.</param>
    /// <param name="checkInViewModel">The check-in view model.</param>
    /// <param name="tileCacheService">The tile cache service.</param>
    public MainViewModel(
        ILocationBridge locationBridge,
        MapService mapService,
        IPermissionsService permissionsService,
        TripNavigationService tripNavigationService,
        NavigationHudViewModel navigationHudViewModel,
        IToastService toastService,
        CheckInViewModel checkInViewModel,
        UnifiedTileCacheService tileCacheService)
    {
        _locationBridge = locationBridge;
        _mapService = mapService;
        _permissionsService = permissionsService;
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        _toastService = toastService;
        _checkInViewModel = checkInViewModel;
        _tileCacheService = tileCacheService;
        Title = "WayfarerMobile";

        // Subscribe to location events
        _locationBridge.LocationReceived += OnLocationReceived;
        _locationBridge.StateChanged += OnStateChanged;

        // Subscribe to navigation HUD events
        _navigationHudViewModel.StopNavigationRequested += OnStopNavigationRequested;

        // Subscribe to check-in completion to auto-close sheet
        _checkInViewModel.CheckInCompleted += OnCheckInCompleted;

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
    /// Handles check-in completion - closes the sheet and cleans up.
    /// </summary>
    private async void OnCheckInCompleted(object? sender, EventArgs e)
    {
        IsCheckInSheetOpen = false;
        await OnCheckInSheetClosedAsync();
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
    private async Task CenterOnLocationAsync()
    {
        var location = CurrentLocation ?? _locationBridge.LastLocation;

        if (location != null)
        {
            _mapService.CenterOnLocation(location);
            IsFollowingLocation = true;
        }
        else
        {
            // Tracking not active or no location yet
            if (TrackingState == TrackingState.Active || TrackingState == TrackingState.Starting)
            {
                await _toastService.ShowAsync("Waiting for location fix...");
            }
            else
            {
                await _toastService.ShowWarningAsync("Start tracking to get your location");
            }
        }
    }

    /// <summary>
    /// Copies current location coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (CurrentLocation == null)
        {
            await _toastService.ShowWarningAsync("No location available");
            return;
        }

        var coords = $"{CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6}";
        await Clipboard.SetTextAsync(coords);
        await _toastService.ShowAsync("Coordinates copied");
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
    /// Resets the map rotation to north (0 degrees).
    /// </summary>
    [RelayCommand]
    private void ResetNorth()
    {
        _mapService.ResetMapRotation();
    }

    #region Context Menu Commands

    /// <summary>
    /// Shows the context menu at the specified coordinates with a visual pin marker.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void ShowContextMenu(double latitude, double longitude)
    {
        ContextMenuLatitude = latitude;
        ContextMenuLongitude = longitude;
        IsContextMenuVisible = true;
        IsDropPinModeActive = false;

        // Store pin location and show visual marker on map
        DroppedPinLatitude = latitude;
        DroppedPinLongitude = longitude;
        HasDroppedPin = true;
        _mapService.ShowDroppedPin(latitude, longitude);
    }

    /// <summary>
    /// Reopens the context menu at the dropped pin location.
    /// Called when user taps on an existing dropped pin.
    /// </summary>
    public void ReopenContextMenuFromPin()
    {
        if (!HasDroppedPin)
            return;

        ContextMenuLatitude = DroppedPinLatitude;
        ContextMenuLongitude = DroppedPinLongitude;
        IsContextMenuVisible = true;
    }

    /// <summary>
    /// Checks if the specified coordinates are near the dropped pin.
    /// </summary>
    /// <param name="latitude">The latitude to check.</param>
    /// <param name="longitude">The longitude to check.</param>
    /// <param name="toleranceMeters">Distance tolerance in meters (default 50m).</param>
    /// <returns>True if within tolerance of the dropped pin.</returns>
    public bool IsNearDroppedPin(double latitude, double longitude, double toleranceMeters = 50)
    {
        if (!HasDroppedPin)
            return false;

        // Simple distance calculation (Haversine for accuracy)
        const double earthRadiusMeters = 6371000;
        var dLat = (latitude - DroppedPinLatitude) * Math.PI / 180;
        var dLon = (longitude - DroppedPinLongitude) * Math.PI / 180;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DroppedPinLatitude * Math.PI / 180) * Math.Cos(latitude * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = earthRadiusMeters * c;

        return distance <= toleranceMeters;
    }

    /// <summary>
    /// Hides the context menu but keeps the dropped pin visible.
    /// The pin remains tappable to reopen the context menu.
    /// </summary>
    [RelayCommand]
    private void HideContextMenu()
    {
        IsContextMenuVisible = false;
        // Pin stays visible - user can tap it to reopen context menu
    }

    /// <summary>
    /// Clears the dropped pin from the map and hides the context menu.
    /// </summary>
    [RelayCommand]
    private void ClearDroppedPin()
    {
        IsContextMenuVisible = false;
        HasDroppedPin = false;
        DroppedPinLatitude = 0;
        DroppedPinLongitude = 0;
        _mapService.ClearDroppedPin();
    }

    /// <summary>
    /// Navigates to the context menu location using device's native navigation app.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToContextLocationAsync()
    {
        try
        {
            var location = new Location(ContextMenuLatitude, ContextMenuLongitude);
            var options = new MapLaunchOptions { NavigationMode = NavigationMode.Walking };

            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
            HideContextMenu();
        }
        catch (Exception ex)
        {
            // Fallback to Google Maps URL if native map fails
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={ContextMenuLatitude},{ContextMenuLongitude}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
                HideContextMenu();
            }
            catch
            {
                await _toastService.ShowErrorAsync($"Unable to open navigation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shares the context menu location.
    /// </summary>
    [RelayCommand]
    private async Task ShareContextLocationAsync()
    {
        var text = $"Location: {ContextMenuLatitude:F6}, {ContextMenuLongitude:F6}\n" +
                   $"https://maps.google.com/?q={ContextMenuLatitude},{ContextMenuLongitude}";

        await Share.RequestAsync(new ShareTextRequest
        {
            Title = "Share Location",
            Text = text
        });

        HideContextMenu();
    }

    /// <summary>
    /// Searches Wikipedia for articles near the context menu location.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{ContextMenuLatitude},{ContextMenuLongitude}";
        await Launcher.OpenAsync(new Uri(url));
        HideContextMenu();
    }

    /// <summary>
    /// Opens the context menu location in Google Maps.
    /// </summary>
    [RelayCommand]
    private async Task OpenInGoogleMapsAsync()
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={ContextMenuLatitude},{ContextMenuLongitude}";
        await Launcher.OpenAsync(new Uri(url));
        HideContextMenu();
    }

    #endregion

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
    /// Opens the check-in sheet to record current location.
    /// </summary>
    [RelayCommand]
    private async Task CheckInAsync()
    {
        // Reset form state before opening
        _checkInViewModel.ResetForm();

        // Initialize the check-in view model when sheet opens
        await _checkInViewModel.OnAppearingAsync();
        IsCheckInSheetOpen = true;
    }

    /// <summary>
    /// Called when the check-in sheet is closed.
    /// Handles cleanup of the CheckInViewModel.
    /// </summary>
    public async Task OnCheckInSheetClosedAsync()
    {
        // Properly clean up the check-in view model
        await _checkInViewModel.OnDisappearingAsync();
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
        // Ensure map is initialized
        _mapService.EnsureInitialized();

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

        // Refresh map to fix any layout issues (e.g., after bottom sheet closes)
        _mapService.RefreshMap();

        // Update cache health indicator
        await UpdateCacheHealthAsync();

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

    /// <summary>
    /// Updates the cache health indicator based on tile cache statistics.
    /// </summary>
    private async Task UpdateCacheHealthAsync()
    {
        try
        {
            var stats = await _tileCacheService.GetStatisticsAsync();
            var hitSummary = _tileCacheService.GetHitStatsSummary();

            // Parse hit rate from summary (format: "Tiles: X (Live:X Trip:X DL:X Miss:X) Hit:XX%")
            var hasNetwork = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

            if (stats.LiveTileCount == 0 && stats.TripCacheSizeBytes == 0)
            {
                // No cache at all
                CacheHealth = hasNetwork ? CacheHealthStatus.Warning : CacheHealthStatus.Poor;
            }
            else if (hitSummary.Contains("Hit:"))
            {
                // Extract hit rate percentage
                var hitIndex = hitSummary.IndexOf("Hit:");
                if (hitIndex >= 0)
                {
                    var hitPart = hitSummary.Substring(hitIndex + 4).TrimEnd('%', ')');
                    if (int.TryParse(hitPart, out var hitRate))
                    {
                        if (hitRate >= 70)
                        {
                            CacheHealth = CacheHealthStatus.Good;
                        }
                        else if (hitRate >= 30 || hasNetwork)
                        {
                            CacheHealth = CacheHealthStatus.Warning;
                        }
                        else
                        {
                            CacheHealth = CacheHealthStatus.Poor;
                        }
                        return;
                    }
                }
            }

            // Default: if we have tiles and network, assume good
            CacheHealth = (stats.LiveTileCount > 0 || stats.TripCacheSizeBytes > 0)
                ? CacheHealthStatus.Good
                : (hasNetwork ? CacheHealthStatus.Warning : CacheHealthStatus.Poor);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error updating cache health: {ex.Message}");
            CacheHealth = CacheHealthStatus.Unknown;
        }
    }

    #endregion
}
