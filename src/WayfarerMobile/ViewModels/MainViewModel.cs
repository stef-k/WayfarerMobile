using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services.TileCache;
using Map = Mapsui.Map;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the main page showing current location and tracking status.
/// Coordinates between MapDisplayViewModel, NavigationCoordinatorViewModel, TripSheetViewModel, and CheckInViewModel.
/// </summary>
public partial class MainViewModel : BaseViewModel, IMapDisplayCallbacks, INavigationCallbacks, ITripSheetCallbacks
{
    #region Fields

    private readonly ILocationBridge _locationBridge;
    private readonly IPermissionsService _permissionsService;
    private readonly ITripNavigationService _tripNavigationService;
    private readonly ITripStateManager _tripStateManager;
    private readonly IToastService _toastService;
    private readonly CheckInViewModel _checkInViewModel;
    private readonly UnifiedTileCacheService _tileCacheService;
    private readonly ILogger<MainViewModel> _logger;

    #endregion

    #region Child ViewModels

    /// <summary>
    /// Gets the map display ViewModel for map and layer management.
    /// </summary>
    public MapDisplayViewModel MapDisplay { get; }

    /// <summary>
    /// Gets the navigation coordinator ViewModel for navigation operations.
    /// </summary>
    public NavigationCoordinatorViewModel Navigation { get; }

    /// <summary>
    /// Gets the trip sheet ViewModel for trip display and editing.
    /// </summary>
    public TripSheetViewModel TripSheet { get; }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// Forwards to MapDisplayViewModel.
    /// </summary>
    public Map Map => MapDisplay.Map;

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
    /// Gets whether navigation is currently active.
    /// Forwarded from NavigationCoordinatorViewModel.
    /// </summary>
    public bool IsNavigating => Navigation.IsNavigating;

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
    [NotifyPropertyChangedFor(nameof(IsAnySheetOpen))]
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
    /// Gets whether the trip sheet is open.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool IsTripSheetOpen
    {
        get => TripSheet.IsTripSheetOpen;
        set => TripSheet.IsTripSheetOpen = value;
    }

    /// <summary>
    /// Gets whether place coordinate editing mode is active.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool IsPlaceCoordinateEditMode => TripSheet.IsPlaceCoordinateEditMode;

    /// <summary>
    /// Tracks whether we're navigating to a sub-editor page (notes, marker, etc.).
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool IsNavigatingToSubEditor => TripSheet.IsNavigatingToSubEditor;

    /// <summary>
    /// Gets the selected trip place for INavigationCallbacks.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public TripPlace? SelectedTripPlace => TripSheet.SelectedTripPlace;

    /// <summary>
    /// Gets the selected place (legacy compatibility).
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public TripPlace? SelectedPlace => TripSheet.SelectedPlace;

    /// <summary>
    /// Gets the navigation HUD view model for binding.
    /// Forwarded from NavigationCoordinatorViewModel.
    /// </summary>
    public NavigationHudViewModel NavigationHud => Navigation.NavigationHud;

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
    /// Uses smoothed heading from LocationIndicatorService to match map indicator.
    /// </summary>
    public string HeadingText
    {
        get
        {
            // Use smoothed heading from MapDisplayViewModel (same as map indicator)
            var heading = MapDisplay.CurrentHeading;
            if (heading < 0)
                return string.Empty;

            var direction = heading switch
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
            return $"{heading:F0}° {direction}";
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
    /// Uses smoothed heading from MapDisplayViewModel to match map indicator.
    /// </summary>
    public bool HasHeading => MapDisplay.HasValidHeading;

    /// <summary>
    /// Gets whether altitude is available.
    /// </summary>
    public bool HasAltitude => CurrentLocation?.Altitude != null;

    /// <summary>
    /// Gets the cache health indicator color.
    /// Forwards to MapDisplayViewModel.
    /// </summary>
    public Color CacheHealthColor => MapDisplay.CacheHealthColor;

    /// <summary>
    /// Gets the cache health tooltip text.
    /// Forwards to MapDisplayViewModel.
    /// </summary>
    public string CacheHealthTooltip => MapDisplay.CacheHealthTooltip;

    /// <summary>
    /// Gets whether a trip is currently loaded.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool HasLoadedTrip => TripSheet.HasLoadedTrip;

    /// <summary>
    /// Gets or sets whether any bottom sheet is open (check-in or trip).
    /// Used for the main SfBottomSheet.IsOpen binding.
    /// </summary>
    public bool IsAnySheetOpen
    {
        get => IsCheckInSheetOpen || IsTripSheetOpen;
        set
        {
            // When the sheet is closed by user swipe (value = false),
            // close whichever sheet is currently open
            if (!value)
            {
                if (IsCheckInSheetOpen)
                    IsCheckInSheetOpen = false;
                if (IsTripSheetOpen)
                    IsTripSheetOpen = false;
            }
        }
    }

    /// <summary>
    /// Gets whether place coordinate editing mode is active (alias for binding).
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool IsEditingPlaceCoordinates => TripSheet.IsEditingPlaceCoordinates;

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool IsAnyEditModeActive => TripSheet.IsAnyEditModeActive;

    /// <summary>
    /// Stores entity info for selection restoration after sub-editor navigation.
    /// Called from MainPage.ApplyQueryAttributes when returning from notes/marker editors.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    /// <param name="entityType">The entity type (Place, Area, Segment, Region).</param>
    /// <param name="entityId">The entity ID to restore selection for.</param>
    public void RestoreSelectionFromSubEditor(string? entityType, Guid entityId)
    {
        if (string.IsNullOrEmpty(entityType) || entityId == Guid.Empty)
            return;

        _logger.LogDebug("RestoreSelectionFromSubEditor: entityType={Type}, entityId={Id}", entityType, entityId);
        TripSheet.RestoreSelectionFromSubEditor(entityType, entityId);
    }

    /// <summary>
    /// Gets whether there are pending place coordinates to save.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public bool HasPendingPlaceCoordinates => TripSheet.HasPendingPlaceCoordinates;

    /// <summary>
    /// Gets the pending place coordinates text for display.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    public string PendingPlaceCoordinatesText => TripSheet.PendingPlaceCoordinatesText;

    /// <summary>
    /// Gets the page title (trip name when loaded, "Map" otherwise).
    /// </summary>
    public string PageTitle => TripSheet.LoadedTrip?.Name ?? "Map";

    /// <summary>
    /// Sets the pending place coordinates from a map tap.
    /// Called by the page code-behind when the map is tapped during coordinate editing.
    /// Forwards to TripSheetViewModel.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void SetPendingPlaceCoordinates(double latitude, double longitude)
    {
        TripSheet.SetPendingPlaceCoordinates(latitude, longitude);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MainViewModel.
    /// </summary>
    /// <param name="locationBridge">The location bridge service.</param>
    /// <param name="permissionsService">The permissions service.</param>
    /// <param name="tripNavigationService">The trip navigation service.</param>
    /// <param name="navigationCoordinator">The navigation coordinator view model.</param>
    /// <param name="tripSheetViewModel">The trip sheet view model.</param>
    /// <param name="toastService">The toast notification service.</param>
    /// <param name="checkInViewModel">The check-in view model.</param>
    /// <param name="tileCacheService">The tile cache service.</param>
    /// <param name="mapDisplayViewModel">The map display view model.</param>
    /// <param name="logger">The logger instance.</param>
    public MainViewModel(
        ILocationBridge locationBridge,
        IPermissionsService permissionsService,
        ITripNavigationService tripNavigationService,
        ITripStateManager tripStateManager,
        NavigationCoordinatorViewModel navigationCoordinator,
        TripSheetViewModel tripSheetViewModel,
        IToastService toastService,
        CheckInViewModel checkInViewModel,
        UnifiedTileCacheService tileCacheService,
        MapDisplayViewModel mapDisplayViewModel,
        ILogger<MainViewModel> logger)
    {
        _locationBridge = locationBridge;
        _permissionsService = permissionsService;
        _tripNavigationService = tripNavigationService;
        _tripStateManager = tripStateManager;
        Navigation = navigationCoordinator;
        TripSheet = tripSheetViewModel;
        _toastService = toastService;
        _checkInViewModel = checkInViewModel;
        _tileCacheService = tileCacheService;
        MapDisplay = mapDisplayViewModel;
        _logger = logger;
        Title = "WayfarerMobile";

        // Set up callbacks from child ViewModels
        MapDisplay.SetCallbacks(this);
        Navigation.SetCallbacks(this);
        TripSheet.SetCallbacks(this);

        // Subscribe to location events
        _locationBridge.LocationReceived += OnLocationReceived;
        _locationBridge.StateChanged += OnStateChanged;

        // Subscribe to navigation shell navigation requests
        Navigation.NavigateToSourcePageRequested += OnNavigateToSourcePageRequested;

        // Subscribe to check-in completion to auto-close sheet
        _checkInViewModel.CheckInCompleted += OnCheckInCompleted;

        // Subscribe to trip sheet events for static state management
        TripSheet.PropertyChanged += OnTripSheetPropertyChanged;
    }

    #endregion

    #region IMapDisplayCallbacks Implementation

    /// <inheritdoc/>
    ITripNavigationService IMapDisplayCallbacks.TripNavigationService => _tripNavigationService;

    #endregion

    #region INavigationCallbacks Implementation

    /// <inheritdoc/>
    LocationData? INavigationCallbacks.CurrentLocation => CurrentLocation;

    /// <inheritdoc/>
    TripPlace? INavigationCallbacks.SelectedTripPlace => SelectedTripPlace;

    /// <inheritdoc/>
    void INavigationCallbacks.ShowNavigationRoute(NavigationRoute route)
        => MapDisplay.ShowNavigationRoute(route);

    /// <inheritdoc/>
    void INavigationCallbacks.ClearNavigationRoute()
        => MapDisplay.ClearNavigationRoute();

    /// <inheritdoc/>
    void INavigationCallbacks.ZoomToNavigationRoute()
        => MapDisplay.ZoomToNavigationRoute();

    /// <inheritdoc/>
    void INavigationCallbacks.UpdateNavigationRouteProgress(NavigationRoute route, double latitude, double longitude)
        => MapDisplay.UpdateNavigationRouteProgress(route, latitude, longitude);

    /// <inheritdoc/>
    void INavigationCallbacks.SetFollowingLocation(bool following)
        => MapDisplay.IsFollowingLocation = following;

    /// <inheritdoc/>
    void INavigationCallbacks.CenterOnLocation(double latitude, double longitude, int? zoomLevel)
        => MapDisplay.CenterOnLocation(latitude, longitude, zoomLevel);

    /// <inheritdoc/>
    void INavigationCallbacks.OpenTripSheet() => IsTripSheetOpen = true;

    /// <inheritdoc/>
    void INavigationCallbacks.CloseTripSheet() => IsTripSheetOpen = false;

    #endregion

    #region ITripSheetCallbacks Implementation

    /// <inheritdoc/>
    LocationData? ITripSheetCallbacks.CurrentLocation => CurrentLocation;

    /// <inheritdoc/>
    void ITripSheetCallbacks.CenterOnLocation(double latitude, double longitude, int? zoomLevel)
        => MapDisplay.CenterOnLocation(latitude, longitude, zoomLevel);

    /// <inheritdoc/>
    void ITripSheetCallbacks.UpdatePlaceSelection(TripPlace? place)
        => MapDisplay.UpdatePlaceSelection(place);

    /// <inheritdoc/>
    void ITripSheetCallbacks.ClearPlaceSelection()
        => MapDisplay.ClearPlaceSelection();

    /// <inheritdoc/>
    void ITripSheetCallbacks.SetFollowingLocation(bool following)
        => MapDisplay.IsFollowingLocation = following;

    /// <inheritdoc/>
    async Task ITripSheetCallbacks.RefreshTripLayersAsync(TripDetails? trip)
    {
        if (trip != null)
        {
            await MapDisplay.RefreshTripLayersAsync(trip);
            MapDisplay.RefreshMap();
        }
    }

    /// <inheritdoc/>
    void ITripSheetCallbacks.UnloadTripFromMap()
        => MapDisplay.ClearTripLayers();

    /// <inheritdoc/>
    Task ITripSheetCallbacks.StartNavigationToPlaceAsync(string placeId)
        => Navigation.StartNavigationToPlaceAsync(placeId);

    /// <inheritdoc/>
    bool ITripSheetCallbacks.IsNavigating => Navigation.IsNavigating;

    /// <inheritdoc/>
    async Task ITripSheetCallbacks.NavigateToPageAsync(string route, IDictionary<string, object>? parameters)
    {
        if (parameters != null)
            await Shell.Current.GoToAsync(route, parameters);
        else
            await Shell.Current.GoToAsync(route);
    }

    /// <inheritdoc/>
    Task<string?> ITripSheetCallbacks.DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
        => Shell.Current.DisplayActionSheet(title, cancel, destruction, buttons);

    /// <inheritdoc/>
    Task<string?> ITripSheetCallbacks.DisplayPromptAsync(string title, string message, string? initialValue)
        => Shell.Current.DisplayPromptAsync(title, message, initialValue: initialValue ?? "");

    /// <inheritdoc/>
    Task<bool> ITripSheetCallbacks.DisplayAlertAsync(string title, string message, string accept, string cancel)
        => Shell.Current.DisplayAlert(title, message, accept, cancel);

    #endregion

    #region Map Helpers

    /// <summary>
    /// Ensures the map is initialized.
    /// </summary>
    private void EnsureMapInitialized()
    {
        MapDisplay.EnsureMapInitialized();
    }

    /// <summary>
    /// Refreshes the map display.
    /// </summary>
    private void RefreshMap()
    {
        MapDisplay.RefreshMap();
    }

    /// <summary>
    /// Zooms the map to fit the current navigation route.
    /// </summary>
    private void ZoomToNavigationRoute()
    {
        MapDisplay.ZoomToNavigationRoute();
    }

    /// <summary>
    /// Shows the navigation route on the map.
    /// </summary>
    private void ShowNavigationRoute(NavigationRoute route)
    {
        MapDisplay.ShowNavigationRoute(route);
    }

    /// <summary>
    /// Clears the navigation route from the map.
    /// </summary>
    private void ClearNavigationRoute()
    {
        MapDisplay.ClearNavigationRoute();
    }

    /// <summary>
    /// Checks if navigation route is currently displayed.
    /// </summary>
    private bool HasNavigationRoute => MapDisplay.HasNavigationRoute;

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles location updates from the service.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        CurrentLocation = location;
        LocationCount++;

        // Update location indicator on map
        MapDisplay.UpdateLocationIndicator(location);

        // Center map if following and not navigating or browsing a trip
        // Don't auto-center when a trip is loaded - user needs to browse places
        if (MapDisplay.IsFollowingLocation && !IsNavigating && !HasLoadedTrip)
        {
            MapDisplay.CenterOnLocation(location.Latitude, location.Longitude);
        }

        // Notify heading properties after LocationLayerService updates the indicator service
        // This ensures HeadingText uses the smoothed heading calculated by LocationIndicatorService
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(HasHeading));

        // Delegate navigation updates to NavigationCoordinator
        Navigation.UpdateLocation(location.Latitude, location.Longitude);
    }

    /// <summary>
    /// Handles shell navigation request from Navigation coordinator after stop.
    /// </summary>
    private async void OnNavigateToSourcePageRequested(object? sender, string? sourcePageRoute)
    {
        if (string.IsNullOrEmpty(sourcePageRoute))
            return;

        try
        {
            await Shell.Current.GoToAsync(sourcePageRoute);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid shell navigation to {Route}", sourcePageRoute);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to navigate to {Route}", sourcePageRoute);
        }
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

        // Clear location indicator when stopping
        if (state == TrackingState.Ready || state == TrackingState.NotInitialized)
        {
            MapDisplay.ClearLocationIndicator();
        }
    }

    /// <summary>
    /// Handles property changes from TripSheetViewModel.
    /// Updates static state and forwards property change notifications.
    /// </summary>
    private void OnTripSheetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TripSheetViewModel.LoadedTrip):
                // Update ITripStateManager for cross-ViewModel access
                var loadedTrip = TripSheet.LoadedTrip;
                _tripStateManager.SetCurrentTrip(loadedTrip?.Id, loadedTrip?.Name);
                _logger.LogDebug("TripSheet.LoadedTrip changed: ITripStateManager set to {TripId}", loadedTrip?.Id);
                // Forward property changes that MainViewModel exposes
                OnPropertyChanged(nameof(HasLoadedTrip));
                OnPropertyChanged(nameof(PageTitle));
                break;

            case nameof(TripSheetViewModel.IsTripSheetOpen):
                OnPropertyChanged(nameof(IsTripSheetOpen));
                OnPropertyChanged(nameof(IsAnySheetOpen));
                break;

            case nameof(TripSheetViewModel.IsPlaceCoordinateEditMode):
                OnPropertyChanged(nameof(IsPlaceCoordinateEditMode));
                OnPropertyChanged(nameof(IsEditingPlaceCoordinates));
                OnPropertyChanged(nameof(IsAnyEditModeActive));
                break;

            case nameof(TripSheetViewModel.SelectedTripPlace):
                OnPropertyChanged(nameof(SelectedTripPlace));
                OnPropertyChanged(nameof(SelectedPlace));
                break;

            case nameof(TripSheetViewModel.HasPendingPlaceCoordinates):
                OnPropertyChanged(nameof(HasPendingPlaceCoordinates));
                break;

            case nameof(TripSheetViewModel.PendingPlaceCoordinatesText):
                OnPropertyChanged(nameof(PendingPlaceCoordinatesText));
                break;
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

                // CRITICAL: Use MainThread.BeginInvokeOnMainThread to ensure we're at the end of
                // any queued main thread work. This prevents Android's 5-second foreground service
                // timeout from expiring if the main thread has pending work.
                var tcs = new TaskCompletionSource();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await _locationBridge.StartAsync();
                        tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task;
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

        MapDisplay.ShowDroppedPin(latitude, longitude);
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

        MapDisplay.ClearDroppedPin();
    }

    /// <summary>
    /// Navigates to the context menu location with choice of internal or external navigation.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToContextLocationAsync()
    {
        // Ask user for navigation method using the styled picker
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        // Get the MainPage to access the navigation picker
        var mainPage = page as MainPage ?? (Shell.Current?.CurrentPage as MainPage);

        Views.Controls.NavigationMethod? navMethod = null;
        if (mainPage != null)
        {
            navMethod = await mainPage.ShowNavigationPickerAsync();
        }
        else
        {
            // Fallback to action sheet if page reference not available
            var result = await page.DisplayActionSheetAsync(
                "Navigate by", "Cancel", null,
                "🚶 Walk", "🚗 Drive", "🚴 Bike", "📍 External Maps");

            navMethod = result switch
            {
                "🚶 Walk" => Views.Controls.NavigationMethod.Walk,
                "🚗 Drive" => Views.Controls.NavigationMethod.Drive,
                "🚴 Bike" => Views.Controls.NavigationMethod.Bike,
                "📍 External Maps" => Views.Controls.NavigationMethod.ExternalMaps,
                _ => null
            };
        }

        if (navMethod == null)
            return;

        HideContextMenu();

        // Handle external maps
        if (navMethod == Views.Controls.NavigationMethod.ExternalMaps)
        {
            await OpenExternalMapsAsync(ContextMenuLatitude, ContextMenuLongitude);
            return;
        }

        // Get current location for internal navigation
        var currentLocation = CurrentLocation ?? _locationBridge.LastLocation;
        if (currentLocation == null)
        {
            await _toastService.ShowWarningAsync("Waiting for your location...");
            return;
        }

        // Map selection to OSRM profile
        var osrmProfile = navMethod switch
        {
            Views.Controls.NavigationMethod.Walk => "foot",
            Views.Controls.NavigationMethod.Drive => "car",
            Views.Controls.NavigationMethod.Bike => "bike",
            _ => "foot"
        };

        try
        {
            IsBusy = true;

            // Calculate route using OSRM with straight line fallback
            var route = await Navigation.CalculateRouteToCoordinatesAsync(
                currentLocation.Latitude,
                currentLocation.Longitude,
                ContextMenuLatitude,
                ContextMenuLongitude,
                "Dropped Pin",
                osrmProfile);

            // Clear dropped pin and start navigation
            ClearDroppedPin();

            // Start navigation via coordinator
            await Navigation.StartNavigationWithRouteAsync(route);

            _logger.LogInformation("Started navigation to dropped pin: {Distance:F1}km", route.TotalDistanceMeters / 1000);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error calculating route");
            await _toastService.ShowErrorAsync("Network error. Please check your connection.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Route calculation timed out");
            await _toastService.ShowErrorAsync("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start navigation");
            await _toastService.ShowErrorAsync("Failed to start navigation");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens external maps app for navigation.
    /// </summary>
    private async Task OpenExternalMapsAsync(double lat, double lon)
    {
        try
        {
            var location = new Location(lat, lon);
            var options = new MapLaunchOptions { NavigationMode = NavigationMode.Walking };

            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (FeatureNotSupportedException)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to open external maps via fallback URL");
                await _toastService.ShowErrorAsync("Unable to open maps");
            }
        }
        catch (Exception ex)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(ex, "Failed to open external maps");
                _logger.LogError(fallbackEx, "Fallback URL also failed");
                await _toastService.ShowErrorAsync($"Unable to open maps: {ex.Message}");
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
    /// Loads a trip for navigation.
    /// </summary>
    /// <param name="tripDetails">The trip details to load.</param>
    public async Task LoadTripForNavigationAsync(TripDetails tripDetails)
    {
        _logger.LogInformation("Loading trip: {TripName} ({PlaceCount} places, {SegmentCount} segments, {AreaCount} areas)",
            tripDetails.Name, tripDetails.AllPlaces.Count, tripDetails.Segments.Count, tripDetails.AllAreas.Count);

        // Debug: Log regions and their areas
        _logger.LogDebug("Trip has {RegionCount} regions", tripDetails.Regions.Count);
        foreach (var region in tripDetails.Regions)
        {
            _logger.LogDebug("Region '{Name}': {PlaceCount} places, {AreaCount} areas",
                region.Name, region.Places.Count, region.Areas.Count);
        }

        // Debug: Log place coordinates to verify data
        foreach (var place in tripDetails.AllPlaces.Take(5))
        {
            _logger.LogDebug("Place '{Name}': Lat={Lat}, Lon={Lon}, Icon={Icon}",
                place.Name, place.Latitude, place.Longitude, place.Icon ?? "null");
        }

        // Reset any previous selection state
        TripSheet.ClearTripSheetSelection();

        TripSheet.LoadedTrip = tripDetails;
        _tripNavigationService.LoadTrip(tripDetails);

        // Show trip layers on map
        var placePoints = await MapDisplay.ShowTripLayersAsync(tripDetails);
        _logger.LogDebug("Updated {Count} places on map layer (from {Total} total)", placePoints.Count, tripDetails.AllPlaces.Count);

        // Zoom map to fit all trip places
        if (placePoints.Count > 0)
        {
            MapDisplay.ZoomToPoints(placePoints);
            MapDisplay.IsFollowingLocation = false; // Don't auto-center on user location
            _logger.LogInformation("Zoomed map to fit {Count} trip places", placePoints.Count);
        }
        else if (tripDetails.BoundingBox != null)
        {
            // Fallback: use trip bounding box center
            var bb = tripDetails.BoundingBox;
            var centerLat = (bb.North + bb.South) / 2;
            var centerLon = (bb.East + bb.West) / 2;
            MapDisplay.CenterOnLocation(centerLat, centerLon, zoomLevel: 12);
            MapDisplay.IsFollowingLocation = false;
            _logger.LogInformation("Centered map on trip bounding box center");
        }

        // Force map refresh to ensure layers are rendered
        MapDisplay.RefreshMap();
    }

    /// <summary>
    /// Refreshes the trip display on the map.
    /// Call this after modifying places, areas, or segments.
    /// </summary>
    private async Task RefreshTripOnMapAsync()
    {
        if (TripSheet.LoadedTrip == null)
            return;

        await MapDisplay.RefreshTripLayersAsync(TripSheet.LoadedTrip);
        MapDisplay.RefreshMap();
    }

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    public void UnloadTrip()
    {
        if (Navigation.IsNavigating)
        {
            Navigation.StopNavigation();
        }

        TripSheet.LoadedTrip = null;
        TripSheet.SelectedPlace = null;
        _tripNavigationService.UnloadTrip();

        // Clear all trip layers
        MapDisplay.ClearTripLayers();

        // Resume following user location when trip is unloaded
        MapDisplay.IsFollowingLocation = true;

        // Recenter map on user location
        var location = CurrentLocation ?? _locationBridge.LastLocation;
        if (location != null)
        {
            MapDisplay.CenterOnLocation(location.Latitude, location.Longitude);
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Process any pending selection restore from sub-editor navigation
        await TripSheet.ProcessPendingSelectionRestoreAsync();

        // Ensure map is initialized
        EnsureMapInitialized();

        // Update state from bridge
        TrackingState = _locationBridge.CurrentState;
        CurrentLocation = _locationBridge.LastLocation;

        // Check permissions state
        await CheckPermissionsStateAsync();

        // Update map if we have a location
        if (CurrentLocation != null)
        {
            MapDisplay.UpdateLocationIndicator(CurrentLocation);
            // Only center on user if no trip is loaded
            if (!HasLoadedTrip)
            {
                MapDisplay.CenterOnLocation(CurrentLocation.Latitude, CurrentLocation.Longitude);
            }
        }

        // Refresh map to fix any layout issues (e.g., after bottom sheet closes)
        MapDisplay.RefreshMap();

        // Cache health is updated by CacheStatusService when location changes - NOT here on startup

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
    /// Keeps trip loaded so user can return via "To Trip" button in My Trips.
    /// Trip is only unloaded when user explicitly taps "Unload Trip" button.
    /// </summary>
    public override async Task OnDisappearingAsync()
    {
        // Close the trip sheet when navigating away (but keep trip loaded in memory)
        // Don't close if navigating to sub-editors (notes, marker)
        if (!IsNavigatingToSubEditor)
        {
            IsTripSheetOpen = false;
        }

        // Set normal mode to conserve battery when map is not visible
        if (TrackingState == TrackingState.Active)
        {
            await _locationBridge.SetPerformanceModeAsync(PerformanceMode.Normal);
            PerformanceMode = PerformanceMode.Normal;
        }

        await base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from location bridge events
        _locationBridge.LocationReceived -= OnLocationReceived;
        _locationBridge.StateChanged -= OnStateChanged;

        // Unsubscribe from child ViewModel events and dispose
        Navigation.NavigateToSourcePageRequested -= OnNavigateToSourcePageRequested;
        Navigation.Dispose();
        _checkInViewModel.CheckInCompleted -= OnCheckInCompleted;

        // Dispose map display ViewModel (handles cache service, location animation, map)
        MapDisplay.Dispose();

        // Dispose trip sheet ViewModel
        TripSheet.PropertyChanged -= OnTripSheetPropertyChanged;
        TripSheet.Dispose();

        base.Cleanup();
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
