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
public partial class MainViewModel : BaseViewModel, IMapDisplayCallbacks, INavigationCallbacks, ITripSheetCallbacks, IContextMenuCallbacks, ITrackingCallbacks
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

    /// <summary>
    /// Cancellation token source for image loads and other async operations.
    /// Cancelled in OnDisappearingAsync to prevent "destroyed activity" crashes
    /// when image loads complete after the page is detached.
    /// </summary>
    private CancellationTokenSource _pageLifetimeCts = new();

    /// <summary>
    /// Tracks whether the page is currently visible.
    /// Used to suppress image loading when page is not visible.
    /// </summary>
    private bool _isPageVisible;

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

    /// <summary>
    /// Gets the context menu ViewModel for dropped pin and location actions.
    /// </summary>
    public ContextMenuViewModel ContextMenu { get; }

    /// <summary>
    /// Gets the tracking coordinator ViewModel for location tracking lifecycle.
    /// </summary>
    public TrackingCoordinatorViewModel Tracking { get; }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// Forwards to MapDisplayViewModel.
    /// </summary>
    public Map Map => MapDisplay.Map;

    /// <summary>
    /// Gets or sets the current location data.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    [NotifyPropertyChangedFor(nameof(AccuracyText))]
    [NotifyPropertyChangedFor(nameof(HeadingText))]
    [NotifyPropertyChangedFor(nameof(AltitudeText))]
    [NotifyPropertyChangedFor(nameof(HasAccuracy))]
    [NotifyPropertyChangedFor(nameof(HasAltitude))]
    private LocationData? _currentLocation;

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
    /// Gets or sets whether the check-in sheet is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnySheetOpen))]
    private bool _isCheckInSheetOpen;

    /// <summary>
    /// Gets or sets whether a trip is currently loaded.
    /// This is an observable backing field to ensure bindings update correctly.
    /// Updated when ITripStateManager.LoadedTrip changes.
    /// </summary>
    [ObservableProperty]
    private bool _hasLoadedTrip;

    /// <summary>
    /// Gets or sets whether the trip sheet is open.
    /// This is an observable backing field to ensure compiled bindings work correctly.
    /// Synchronized with TripSheetViewModel.IsTripSheetOpen via PropertyChanged handlers.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnySheetOpen))]
    private bool _isTripSheetOpen;

    /// <summary>
    /// Gets or sets the page title (trip name when loaded, "Map" otherwise).
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private string _pageTitle = "Map";

    /// <summary>
    /// Gets or sets the status text based on current state.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Gets or sets whether heading is available.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private bool _hasHeading;

    /// <summary>
    /// Gets or sets whether the context menu is visible.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private bool _isContextMenuVisible;

    /// <summary>
    /// Gets or sets the context menu latitude.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLatitude;

    /// <summary>
    /// Gets or sets the context menu longitude.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLongitude;

    #region Tracking Forwarding Properties

    /// <summary>
    /// Gets the current tracking state.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public TrackingState TrackingState => Tracking.TrackingState;

    /// <summary>
    /// Gets the current performance mode.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public PerformanceMode PerformanceMode => Tracking.PerformanceMode;

    /// <summary>
    /// Gets the location update count.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public int LocationCount => Tracking.LocationCount;

    /// <summary>
    /// Gets whether tracking is currently active.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public bool IsTracking => Tracking.IsTracking;

    /// <summary>
    /// Gets the tracking button text based on current state.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public string TrackingButtonText => Tracking.TrackingButtonText;

    /// <summary>
    /// Gets the tracking button icon based on current state.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public string TrackingButtonIcon => Tracking.TrackingButtonIcon;

    /// <summary>
    /// Gets the tracking button image source based on current state.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public string TrackingButtonImage => Tracking.TrackingButtonImage;

    /// <summary>
    /// Gets the tracking button color based on current state.
    /// Forwards to TrackingCoordinatorViewModel.
    /// </summary>
    public Color TrackingButtonColor => Tracking.TrackingButtonColor;


    #endregion

    #region Context Menu Forwarding Properties

    /// <summary>
    /// Gets whether drop pin mode is active.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public bool IsDropPinModeActive => ContextMenu.IsDropPinModeActive;


    /// <summary>
    /// Gets whether a dropped pin is visible on the map.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public bool HasDroppedPin => ContextMenu.HasDroppedPin;

    /// <summary>
    /// Gets the dropped pin latitude.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public double DroppedPinLatitude => ContextMenu.DroppedPinLatitude;

    /// <summary>
    /// Gets the dropped pin longitude.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public double DroppedPinLongitude => ContextMenu.DroppedPinLongitude;

    #endregion


    /// <summary>
    /// Gets whether place coordinate editing mode is active.
    /// Forwards to TripItemEditorViewModel.
    /// </summary>
    public bool IsPlaceCoordinateEditMode => TripSheet.Editor.IsPlaceCoordinateEditMode;

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
    /// Forwards to TripItemEditorViewModel.
    /// </summary>
    public bool IsEditingPlaceCoordinates => TripSheet.Editor.IsEditingPlaceCoordinates;

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// Forwards to TripItemEditorViewModel.
    /// </summary>
    public bool IsAnyEditModeActive => TripSheet.Editor.IsAnyEditModeActive;

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
    /// Forwards to TripItemEditorViewModel.
    /// </summary>
    public bool HasPendingPlaceCoordinates => TripSheet.Editor.HasPendingPlaceCoordinates;

    /// <summary>
    /// Gets the pending place coordinates text for display.
    /// Forwards to TripItemEditorViewModel.
    /// </summary>
    public string PendingPlaceCoordinatesText => TripSheet.Editor.PendingPlaceCoordinatesText;


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

    /// <summary>
    /// Gets the command to toggle the trip sheet.
    /// Exposed directly on MainViewModel because MAUI compiled bindings
    /// may not reliably resolve commands through property paths.
    /// </summary>
    public IAsyncRelayCommand ToggleTripSheetCommand => TripSheet.ToggleTripSheetCommand;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MainViewModel.
    /// </summary>
    /// <param name="locationBridge">The location bridge service.</param>
    /// <param name="permissionsService">The permissions service.</param>
    /// <param name="tripNavigationService">The trip navigation service.</param>
    /// <param name="tripStateManager">The trip state manager.</param>
    /// <param name="navigationCoordinator">The navigation coordinator view model.</param>
    /// <param name="tripSheetViewModel">The trip sheet view model.</param>
    /// <param name="contextMenuViewModel">The context menu view model.</param>
    /// <param name="trackingCoordinatorViewModel">The tracking coordinator view model.</param>
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
        ContextMenuViewModel contextMenuViewModel,
        TrackingCoordinatorViewModel trackingCoordinatorViewModel,
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
        ContextMenu = contextMenuViewModel;
        Tracking = trackingCoordinatorViewModel;
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
        ContextMenu.SetCallbacks(this);
        Tracking.SetCallbacks(this);

        // Subscribe to location events
        _locationBridge.LocationReceived += OnLocationReceived;

        // Subscribe to navigation shell navigation requests and property changes
        Navigation.NavigateToSourcePageRequested += OnNavigateToSourcePageRequested;
        Navigation.PropertyChanged += OnNavigationPropertyChanged;

        // Subscribe to check-in completion to auto-close sheet
        _checkInViewModel.CheckInCompleted += OnCheckInCompleted;

        // Subscribe to trip sheet events for static state management
        TripSheet.PropertyChanged += OnTripSheetPropertyChanged;
        TripSheet.TripSheetOpenChanged += OnTripSheetOpenChanged;
        TripSheet.Editor.PropertyChanged += OnEditorPropertyChanged;

        // Subscribe to tracking and context menu property changes
        Tracking.PropertyChanged += OnTrackingPropertyChanged;
        ContextMenu.PropertyChanged += OnContextMenuPropertyChanged;

        // Initialize observable properties from child VMs
        // This ensures bindings have correct initial values even if TripStateManager already has a trip loaded
        _hasLoadedTrip = TripSheet.HasLoadedTrip;
        _isTripSheetOpen = TripSheet.IsTripSheetOpen;
        _pageTitle = TripSheet.LoadedTrip?.Name ?? "Map";
        _statusText = Tracking.StatusText;
        _hasHeading = MapDisplay.CurrentHeading >= 0;
        _isContextMenuVisible = ContextMenu.IsContextMenuVisible;
        _contextMenuLatitude = ContextMenu.ContextMenuLatitude;
        _contextMenuLongitude = ContextMenu.ContextMenuLongitude;
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
        => Shell.Current.DisplayActionSheetAsync(title, cancel, destruction, buttons);

    /// <inheritdoc/>
    Task<string?> ITripSheetCallbacks.DisplayPromptAsync(string title, string message, string? initialValue)
        => Shell.Current.DisplayPromptAsync(title, message, initialValue: initialValue ?? "");

    /// <inheritdoc/>
    Task<bool> ITripSheetCallbacks.DisplayAlertAsync(string title, string message, string accept, string cancel)
        => Shell.Current.DisplayAlertAsync(title, message, accept, cancel);

    #endregion

    #region IContextMenuCallbacks Implementation

    /// <inheritdoc/>
    LocationData? IContextMenuCallbacks.CurrentLocation => CurrentLocation;

    /// <inheritdoc/>
    ILocationBridge IContextMenuCallbacks.LocationBridge => _locationBridge;

    /// <inheritdoc/>
    void IContextMenuCallbacks.ShowDroppedPin(double latitude, double longitude)
        => MapDisplay.ShowDroppedPin(latitude, longitude);

    /// <inheritdoc/>
    void IContextMenuCallbacks.ClearDroppedPinFromMap()
        => MapDisplay.ClearDroppedPin();

    /// <inheritdoc/>
    Task<NavigationRoute> IContextMenuCallbacks.CalculateRouteToCoordinatesAsync(
        double fromLat, double fromLon, double toLat, double toLon,
        string destinationName, string profile)
        => Navigation.CalculateRouteToCoordinatesAsync(fromLat, fromLon, toLat, toLon, destinationName, profile);

    /// <inheritdoc/>
    Task IContextMenuCallbacks.StartNavigationWithRouteAsync(NavigationRoute route)
        => Navigation.StartNavigationWithRouteAsync(route);

    /// <inheritdoc/>
    IToastService IContextMenuCallbacks.ToastService => _toastService;

    /// <inheritdoc/>
    async Task<Views.Controls.NavigationMethod?> IContextMenuCallbacks.ShowNavigationPickerAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return null;

        var mainPage = page as MainPage ?? (Shell.Current?.CurrentPage as MainPage);

        if (mainPage != null)
        {
            return await mainPage.ShowNavigationPickerAsync();
        }

        // Fallback to action sheet if page reference not available
        var result = await page.DisplayActionSheetAsync(
            "Navigate by", "Cancel", null,
            "🚶 Walk", "🚗 Drive", "🚴 Bike", "📍 External Maps");

        return result switch
        {
            "🚶 Walk" => Views.Controls.NavigationMethod.Walk,
            "🚗 Drive" => Views.Controls.NavigationMethod.Drive,
            "🚴 Bike" => Views.Controls.NavigationMethod.Bike,
            "📍 External Maps" => Views.Controls.NavigationMethod.ExternalMaps,
            _ => null
        };
    }

    /// <inheritdoc/>
    bool IContextMenuCallbacks.IsBusy
    {
        get => IsBusy;
        set => IsBusy = value;
    }

    #endregion

    #region ITrackingCallbacks Implementation

    /// <inheritdoc/>
    ILocationBridge ITrackingCallbacks.LocationBridge => _locationBridge;

    /// <inheritdoc/>
    IPermissionsService ITrackingCallbacks.PermissionsService => _permissionsService;

    /// <inheritdoc/>
    void ITrackingCallbacks.ClearLocationIndicator()
        => MapDisplay.ClearLocationIndicator();

    /// <inheritdoc/>
    Task<bool> ITrackingCallbacks.DisplayAlertAsync(string title, string message, string accept, string cancel)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return Task.FromResult(false);
        return page.DisplayAlertAsync(title, message, accept, cancel);
    }

    /// <inheritdoc/>
    void ITrackingCallbacks.OpenAppSettings()
        => _permissionsService.OpenAppSettings();

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
        Tracking.IncrementLocationCount();

        // Update location indicator on map
        MapDisplay.UpdateLocationIndicator(location);

        // Center map if following and not navigating or browsing a trip
        // Don't auto-center when a trip is loaded - user needs to browse places
        if (MapDisplay.IsFollowingLocation && !IsNavigating && !HasLoadedTrip)
        {
            MapDisplay.CenterOnLocation(location.Latitude, location.Longitude);
        }

        // Update heading properties after LocationLayerService updates the indicator service
        // This ensures HeadingText uses the smoothed heading calculated by LocationIndicatorService
        OnPropertyChanged(nameof(HeadingText));
        // Set observable property to ensure compiled bindings update correctly
        HasHeading = MapDisplay.CurrentHeading >= 0;

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
    /// Handles property changes from NavigationCoordinatorViewModel.
    /// Forwards property change notifications for UI bindings.
    /// </summary>
    private void OnNavigationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NavigationCoordinatorViewModel.IsNavigating):
                OnPropertyChanged(nameof(IsNavigating));
                break;
        }
    }

    /// <summary>
    /// Handles property changes from TripSheetViewModel.
    /// Forwards property change notifications for UI bindings.
    /// Note: ITripStateManager is the source of truth for LoadedTrip state.
    /// </summary>
    private void OnTripSheetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TripSheetViewModel.LoadedTrip):
                // Update observable properties from actual state
                HasLoadedTrip = TripSheet.HasLoadedTrip;
                PageTitle = TripSheet.LoadedTrip?.Name ?? "Map";
                break;

            case nameof(TripSheetViewModel.HasLoadedTrip):
                // Also handle HasLoadedTrip directly (TripSheetViewModel raises both LoadedTrip and HasLoadedTrip)
                HasLoadedTrip = TripSheet.HasLoadedTrip;
                break;

            case nameof(TripSheetViewModel.IsTripSheetOpen):
                // Sync from TripSheetViewModel to MainViewModel (bidirectional sync)
                IsTripSheetOpen = TripSheet.IsTripSheetOpen;
                break;

            case nameof(TripSheetViewModel.SelectedTripPlace):
                OnPropertyChanged(nameof(SelectedTripPlace));
                OnPropertyChanged(nameof(SelectedPlace));
                break;
        }
    }

    /// <summary>
    /// Handles TripSheetOpenChanged event from TripSheetViewModel.
    /// Synchronizes IsTripSheetOpen state to MainViewModel.
    /// </summary>
    private void OnTripSheetOpenChanged(object? sender, bool isOpen)
    {
        IsTripSheetOpen = isOpen;
    }

    /// <summary>
    /// Handles Editor property changes for forwarding coordinate editing state.
    /// </summary>
    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TripItemEditorViewModel.IsPlaceCoordinateEditMode):
                OnPropertyChanged(nameof(IsPlaceCoordinateEditMode));
                OnPropertyChanged(nameof(IsEditingPlaceCoordinates));
                OnPropertyChanged(nameof(IsAnyEditModeActive));
                break;

            case nameof(TripItemEditorViewModel.HasPendingPlaceCoordinates):
                OnPropertyChanged(nameof(HasPendingPlaceCoordinates));
                break;

            case nameof(TripItemEditorViewModel.PendingPlaceCoordinatesText):
                OnPropertyChanged(nameof(PendingPlaceCoordinatesText));
                break;
        }
    }

    /// <summary>
    /// Handles property changes from TrackingCoordinatorViewModel.
    /// Forwards property change notifications for UI bindings.
    /// </summary>
    private void OnTrackingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TrackingCoordinatorViewModel.TrackingState):
                OnPropertyChanged(nameof(TrackingState));
                OnPropertyChanged(nameof(IsTracking));
                OnPropertyChanged(nameof(TrackingButtonText));
                OnPropertyChanged(nameof(TrackingButtonIcon));
                OnPropertyChanged(nameof(TrackingButtonImage));
                OnPropertyChanged(nameof(TrackingButtonColor));
                // Set observable property to ensure compiled bindings update
                StatusText = Tracking.StatusText;
                break;

            case nameof(TrackingCoordinatorViewModel.PerformanceMode):
                OnPropertyChanged(nameof(PerformanceMode));
                break;

            case nameof(TrackingCoordinatorViewModel.LocationCount):
                OnPropertyChanged(nameof(LocationCount));
                break;
        }
    }

    /// <summary>
    /// Handles property changes from ContextMenuViewModel.
    /// Forwards property change notifications for UI bindings.
    /// Sets observable properties to ensure compiled bindings update correctly.
    /// </summary>
    private void OnContextMenuPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ContextMenuViewModel.IsDropPinModeActive):
                OnPropertyChanged(nameof(IsDropPinModeActive));
                break;

            case nameof(ContextMenuViewModel.IsContextMenuVisible):
                // Set observable property to ensure compiled bindings update
                IsContextMenuVisible = ContextMenu.IsContextMenuVisible;
                break;

            case nameof(ContextMenuViewModel.ContextMenuLatitude):
                // Set observable property to ensure compiled bindings update
                ContextMenuLatitude = ContextMenu.ContextMenuLatitude;
                break;

            case nameof(ContextMenuViewModel.ContextMenuLongitude):
                // Set observable property to ensure compiled bindings update
                ContextMenuLongitude = ContextMenu.ContextMenuLongitude;
                break;

            case nameof(ContextMenuViewModel.HasDroppedPin):
                OnPropertyChanged(nameof(HasDroppedPin));
                break;

            case nameof(ContextMenuViewModel.DroppedPinLatitude):
                OnPropertyChanged(nameof(DroppedPinLatitude));
                break;

            case nameof(ContextMenuViewModel.DroppedPinLongitude):
                OnPropertyChanged(nameof(DroppedPinLongitude));
                break;
        }
    }

    #endregion

    #region Partial Methods

    /// <summary>
    /// Called when IsTripSheetOpen changes.
    /// Propagates the change to TripSheetViewModel for bidirectional sync.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsTripSheetOpenChanged(bool value)
    {
        // Propagate to TripSheetViewModel if different (avoid infinite loop)
        if (TripSheet.IsTripSheetOpen != value)
        {
            TripSheet.IsTripSheetOpen = value;
        }
    }

    #endregion

    #region Commands

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

    #endregion

    #region Context Menu Forwarding Methods

    /// <summary>
    /// Shows the context menu at the specified coordinates with a visual pin marker.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public void ShowContextMenu(double latitude, double longitude)
        => ContextMenu.ShowContextMenu(latitude, longitude);

    /// <summary>
    /// Reopens the context menu at the dropped pin location.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public void ReopenContextMenuFromPin()
        => ContextMenu.ReopenContextMenuFromPin();

    /// <summary>
    /// Checks if the specified coordinates are near the dropped pin.
    /// Forwards to ContextMenuViewModel.
    /// </summary>
    public bool IsNearDroppedPin(double latitude, double longitude, double toleranceMeters = 50)
        => ContextMenu.IsNearDroppedPin(latitude, longitude, toleranceMeters);

    #endregion

    #region Trip Management

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

        // Set loaded trip via ITripStateManager (source of truth)
        _tripStateManager.SetLoadedTrip(tripDetails);
        _logger.LogDebug("After SetLoadedTrip: HasLoadedTrip={HasTrip}, TripSheet.HasLoadedTrip={TsHasTrip}",
            HasLoadedTrip, TripSheet.HasLoadedTrip);
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

        // Ensure HasLoadedTrip is set (should already be set via OnTripSheetPropertyChanged,
        // but set directly here as well to ensure binding updates during navigation)
        HasLoadedTrip = true;
        OnPropertyChanged(nameof(PageTitle));
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

        // Clear loaded trip via ITripStateManager (source of truth)
        _tripStateManager.SetLoadedTrip(null);
        HasLoadedTrip = false;
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
        // Mark page visible and restore image bindings
        MarkPageVisible();

        // Process any pending selection restore from sub-editor navigation
        await TripSheet.ProcessPendingSelectionRestoreAsync();

        // Ensure map is initialized
        EnsureMapInitialized();

        // Update location from bridge
        CurrentLocation = _locationBridge.LastLocation;

        // Check permissions state via Tracking child VM
        await Tracking.CheckPermissionsStateAsync();

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
        if (Tracking.TrackingState == TrackingState.Active)
        {
            await Tracking.SetPerformanceModeCommand.ExecuteAsync(PerformanceMode.HighPerformance);
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
        // D5 fix (after-detach path): Cancel any pending image loads and async operations
        // to prevent "destroyed activity" crashes when loads complete after page detaches
        CancelPendingOperations();

        // Close the trip sheet when navigating away (but keep trip loaded in memory)
        // Don't close if navigating to sub-editors (notes, marker)
        if (!IsNavigatingToSubEditor)
        {
            IsTripSheetOpen = false;
        }

        // Set normal mode to conserve battery when map is not visible
        if (Tracking.TrackingState == TrackingState.Active)
        {
            await Tracking.SetPerformanceModeCommand.ExecuteAsync(PerformanceMode.Normal);
        }

        await base.OnDisappearingAsync();
    }

    /// <summary>
    /// Gets the cancellation token for page-lifetime operations.
    /// Use this for any async operations that should be cancelled when the page disappears.
    /// </summary>
    public CancellationToken PageLifetimeToken => _pageLifetimeCts.Token;

    /// <summary>
    /// Gets the trip cover image URL, or null when page is not visible.
    /// Bind to this instead of TripSheet.LoadedTrip.CleanCoverImageUrl to enable
    /// automatic cancellation of image loads when the page disappears.
    /// </summary>
    public string? TripCoverImageUrl => _isPageVisible ? TripSheet.LoadedTrip?.CleanCoverImageUrl : null;

    /// <summary>
    /// Cancels all pending page-lifetime operations and creates a fresh CTS.
    /// Called in OnDisappearingAsync to abort in-flight image loads and other async work.
    /// </summary>
    private void CancelPendingOperations()
    {
        _isPageVisible = false;

        // Cancel and dispose the old CTS
        _pageLifetimeCts.Cancel();
        _pageLifetimeCts.Dispose();

        // Create a fresh CTS for the next appearance
        _pageLifetimeCts = new CancellationTokenSource();

        // Set TripCoverImageUrl to null to cancel any pending image load
        // MAUI will cancel the native load when the ImageSource is set to null
        OnPropertyChanged(nameof(TripCoverImageUrl));
    }

    /// <summary>
    /// Marks the page as visible and restores image bindings.
    /// Called at start of OnAppearingAsync.
    /// </summary>
    private void MarkPageVisible()
    {
        _isPageVisible = true;
        // Restore TripCoverImageUrl binding (will now return actual URL)
        OnPropertyChanged(nameof(TripCoverImageUrl));
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// Note: MainViewModel is Transient, but most child VMs are Singleton.
    /// We only unsubscribe from events here - Singleton children must NOT be disposed
    /// as they'll be reused by the next MainViewModel instance.
    /// </summary>
    protected override void Cleanup()
    {
        // Cancel and dispose the page lifetime CTS
        _pageLifetimeCts.Cancel();
        _pageLifetimeCts.Dispose();

        // Unsubscribe from location bridge events
        _locationBridge.LocationReceived -= OnLocationReceived;

        // Unsubscribe from Singleton child ViewModel events (do NOT dispose them)
        Navigation.NavigateToSourcePageRequested -= OnNavigateToSourcePageRequested;
        Navigation.PropertyChanged -= OnNavigationPropertyChanged;
        // Navigation is Singleton - do not dispose

        _checkInViewModel.CheckInCompleted -= OnCheckInCompleted;
        // CheckInViewModel is Transient but injected, let DI handle it

        // MapDisplay is Singleton - do not dispose
        // (holds expensive Map instance that persists across page navigations)

        // Unsubscribe from TripSheet events and dispose it (Transient, same lifetime as MainViewModel)
        TripSheet.PropertyChanged -= OnTripSheetPropertyChanged;
        TripSheet.TripSheetOpenChanged -= OnTripSheetOpenChanged;
        TripSheet.Editor.PropertyChanged -= OnEditorPropertyChanged;
        TripSheet.Dispose(); // Triggers TripSheetViewModel.Cleanup() to unsubscribe from ITripStateManager

        // Tracking is Singleton - do not dispose
        Tracking.PropertyChanged -= OnTrackingPropertyChanged;

        // ContextMenu is Singleton - do not dispose
        ContextMenu.PropertyChanged -= OnContextMenuPropertyChanged;

        base.Cleanup();
    }

    #endregion
}
