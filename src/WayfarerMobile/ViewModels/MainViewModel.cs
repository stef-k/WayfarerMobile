using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
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
    private readonly IPermissionsService _permissionsService;
    private readonly TripNavigationService _tripNavigationService;
    private readonly NavigationHudViewModel _navigationHudViewModel;
    private readonly IToastService _toastService;
    private readonly CheckInViewModel _checkInViewModel;
    private readonly UnifiedTileCacheService _tileCacheService;
    private readonly CacheStatusService _cacheStatusService;
    private readonly CacheOverlayService _cacheOverlayService;
    private readonly LocationIndicatorService _indicatorService;

    // Map composition services
    private readonly IMapBuilder _mapBuilder;
    private readonly ILocationLayerService _locationLayerService;
    private readonly IDroppedPinLayerService _droppedPinLayerService;
    private readonly ITripLayerService _tripLayerService;
    private readonly IWikipediaService _wikipediaService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainViewModel> _logger;

    // Map and layers (owned by this ViewModel)
    private Map? _map;
    private WritableLayer? _locationLayer;
    private WritableLayer? _navigationRouteLayer;
    private WritableLayer? _navigationRouteCompletedLayer;
    private WritableLayer? _droppedPinLayer;
    private WritableLayer? _tripPlacesLayer;
    private WritableLayer? _tripAreasLayer;
    private WritableLayer? _tripSegmentsLayer;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Map Map => _map ??= CreateMap();

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
    /// Gets or sets whether the trip sheet is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    [NotifyPropertyChangedFor(nameof(IsAnySheetOpen))]
    private bool _isTripSheetOpen;

    /// <summary>
    /// Gets or sets the currently loaded trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    [NotifyPropertyChangedFor(nameof(TripPlaceCount))]
    [NotifyPropertyChangedFor(nameof(HasTripSegments))]
    [NotifyPropertyChangedFor(nameof(TripNotesPreview))]
    [NotifyPropertyChangedFor(nameof(TripNotesHtml))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    private TripDetails? _loadedTrip;

    /// <summary>
    /// Gets or sets the selected place in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingPlace))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(SelectedTripPlaceNotesHtml))]
    private TripPlace? _selectedTripPlace;

    /// <summary>
    /// Gets or sets the selected area in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingArea))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripAreaNotesHtml))]
    private TripArea? _selectedTripArea;

    /// <summary>
    /// Gets or sets the selected segment in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingSegment))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripSegmentNotesHtml))]
    private TripSegment? _selectedTripSegment;

    /// <summary>
    /// Gets or sets whether trip notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingTripNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    private bool _isShowingTripNotes;

    /// <summary>
    /// Gets or sets whether area notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingAreaNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingAreaNotes;

    /// <summary>
    /// Gets or sets whether segment notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingSegmentNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingSegmentNotes;

    // Legacy property for compatibility
    private TripPlace? _selectedPlace;

    /// <summary>
    /// Gets or sets the selected place (legacy compatibility).
    /// </summary>
    public TripPlace? SelectedPlace
    {
        get => _selectedPlace;
        set => SetProperty(ref _selectedPlace, value);
    }

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
    /// Uses smoothed heading from LocationIndicatorService to match map indicator.
    /// </summary>
    public string HeadingText
    {
        get
        {
            // Use smoothed heading from LocationIndicatorService (same as map indicator)
            var heading = _indicatorService?.CurrentHeading ?? -1;
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
    /// Uses smoothed heading from LocationIndicatorService to match map indicator.
    /// </summary>
    public bool HasHeading => _indicatorService?.HasValidHeading ?? false;

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
    /// Gets the page title (trip name when loaded, "Map" otherwise).
    /// </summary>
    public string PageTitle => LoadedTrip?.Name ?? "Map";

    /// <summary>
    /// Gets the number of places in the loaded trip.
    /// </summary>
    public int TripPlaceCount => LoadedTrip?.AllPlaces.Count ?? 0;

    /// <summary>
    /// Gets whether the loaded trip has segments.
    /// </summary>
    public bool HasTripSegments => LoadedTrip?.Segments.Count > 0;

    /// <summary>
    /// Gets a preview of trip notes (first 200 chars).
    /// </summary>
    public string? TripNotesPreview => LoadedTrip?.Notes?.Length > 200
        ? LoadedTrip.Notes[..200] + "..."
        : LoadedTrip?.Notes;

    /// <summary>
    /// Gets the trip notes as HtmlWebViewSource for WebView rendering.
    /// Uses notes-viewer.html template for proper CSP and image handling.
    /// </summary>
    public HtmlWebViewSource? TripNotesHtml
    {
        get
        {
            if (string.IsNullOrEmpty(LoadedTrip?.Notes))
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(LoadedTrip.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets whether trip sheet is showing overview (no item selected).
    /// </summary>
    public bool IsTripSheetShowingOverview =>
        SelectedTripPlace == null && SelectedTripArea == null && SelectedTripSegment == null && !IsShowingTripNotes;

    /// <summary>
    /// Gets whether trip sheet is showing trip notes detail view.
    /// </summary>
    public bool IsTripSheetShowingTripNotes => IsShowingTripNotes;

    /// <summary>
    /// Gets whether trip sheet is showing area notes detail view.
    /// </summary>
    public bool IsTripSheetShowingAreaNotes => IsShowingAreaNotes;

    /// <summary>
    /// Gets whether trip sheet is showing segment notes detail view.
    /// </summary>
    public bool IsTripSheetShowingSegmentNotes => IsShowingSegmentNotes;

    /// <summary>
    /// Gets whether the scrollable content area should be visible (Overview, Area, or Segment - not Place, TripNotes, AreaNotes, or SegmentNotes).
    /// </summary>
    public bool IsTripSheetShowingScrollableContent =>
        !IsTripSheetShowingPlace && !IsTripSheetShowingTripNotes && !IsTripSheetShowingAreaNotes && !IsTripSheetShowingSegmentNotes;

    /// <summary>
    /// Gets whether trip sheet is showing place details.
    /// </summary>
    public bool IsTripSheetShowingPlace => SelectedTripPlace != null;

    /// <summary>
    /// Gets whether trip sheet is showing area details.
    /// </summary>
    public bool IsTripSheetShowingArea => SelectedTripArea != null;

    /// <summary>
    /// Gets whether trip sheet is showing segment details.
    /// </summary>
    public bool IsTripSheetShowingSegment => SelectedTripSegment != null;

    /// <summary>
    /// Gets whether trip sheet is showing any details (not overview).
    /// </summary>
    public bool IsTripSheetShowingDetails => !IsTripSheetShowingOverview;

    /// <summary>
    /// Gets the trip sheet title.
    /// </summary>
    public string TripSheetTitle
    {
        get
        {
            if (SelectedTripPlace != null)
                return SelectedTripPlace.Name;
            if (IsShowingAreaNotes && SelectedTripArea != null)
                return $"{SelectedTripArea.Name} - Notes";
            if (SelectedTripArea != null)
                return SelectedTripArea.Name;
            if (IsShowingSegmentNotes && SelectedTripSegment != null)
                return "Segment Notes";
            if (SelectedTripSegment != null)
                return $"Segment: {SelectedTripSegment.TransportMode ?? "Route"}";
            if (IsShowingTripNotes)
                return "Trip Notes";
            return LoadedTrip?.Name ?? "Trip";
        }
    }

    /// <summary>
    /// Gets the trip sheet subtitle.
    /// </summary>
    public string? TripSheetSubtitle
    {
        get
        {
            if (IsTripSheetShowingOverview && LoadedTrip != null)
            {
                var parts = new List<string>();
                var placeCount = LoadedTrip.AllPlaces.Count;
                var areaCount = LoadedTrip.AllAreas.Count;
                var segmentCount = LoadedTrip.Segments.Count;
                if (placeCount > 0) parts.Add($"{placeCount} places");
                if (areaCount > 0) parts.Add($"{areaCount} areas");
                if (segmentCount > 0) parts.Add($"{segmentCount} segments");
                return string.Join(" • ", parts);
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the selected trip place coordinates as text.
    /// </summary>
    public string? SelectedTripPlaceCoordinates => SelectedTripPlace != null
        ? $"{SelectedTripPlace.Latitude:F5}, {SelectedTripPlace.Longitude:F5}"
        : null;

    /// <summary>
    /// Gets the HTML content for the selected trip place notes, wrapped for WebView display.
    /// Uses notes-viewer.html template for proper CSP and image handling.
    /// </summary>
    public HtmlWebViewSource? SelectedTripPlaceNotesHtml
    {
        get
        {
            if (SelectedTripPlace?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripPlace.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets the HTML content for the selected trip area notes, wrapped for WebView display.
    /// Uses notes-viewer.html template for proper CSP and image handling.
    /// </summary>
    public HtmlWebViewSource? SelectedTripAreaNotesHtml
    {
        get
        {
            if (SelectedTripArea?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripArea.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets the HTML content for the selected trip segment notes, wrapped for WebView display.
    /// Uses notes-viewer.html template for proper CSP and image handling.
    /// </summary>
    public HtmlWebViewSource? SelectedTripSegmentNotesHtml
    {
        get
        {
            if (SelectedTripSegment?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripSegment.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MainViewModel.
    /// </summary>
    /// <param name="locationBridge">The location bridge service.</param>
    /// <param name="permissionsService">The permissions service.</param>
    /// <param name="tripNavigationService">The trip navigation service.</param>
    /// <param name="navigationHudViewModel">The navigation HUD view model.</param>
    /// <param name="toastService">The toast notification service.</param>
    /// <param name="checkInViewModel">The check-in view model.</param>
    /// <param name="tileCacheService">The tile cache service.</param>
    /// <param name="cacheStatusService">The cache status service.</param>
    /// <param name="cacheOverlayService">The cache overlay service.</param>
    /// <param name="indicatorService">The location indicator service for smoothed heading.</param>
    /// <param name="mapBuilder">The map builder for creating isolated map instances.</param>
    /// <param name="locationLayerService">The location layer service for rendering current location.</param>
    /// <param name="droppedPinLayerService">The dropped pin layer service.</param>
    /// <param name="tripLayerService">The trip layer service for places and segments.</param>
    /// <param name="wikipediaService">The Wikipedia geosearch service.</param>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="logger">The logger instance.</param>
    public MainViewModel(
        ILocationBridge locationBridge,
        IPermissionsService permissionsService,
        TripNavigationService tripNavigationService,
        NavigationHudViewModel navigationHudViewModel,
        IToastService toastService,
        CheckInViewModel checkInViewModel,
        UnifiedTileCacheService tileCacheService,
        CacheStatusService cacheStatusService,
        CacheOverlayService cacheOverlayService,
        LocationIndicatorService indicatorService,
        IMapBuilder mapBuilder,
        ILocationLayerService locationLayerService,
        IDroppedPinLayerService droppedPinLayerService,
        ITripLayerService tripLayerService,
        IWikipediaService wikipediaService,
        ISettingsService settingsService,
        ILogger<MainViewModel> logger)
    {
        _locationBridge = locationBridge;
        _permissionsService = permissionsService;
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        _toastService = toastService;
        _checkInViewModel = checkInViewModel;
        _tileCacheService = tileCacheService;
        _cacheStatusService = cacheStatusService;
        _cacheOverlayService = cacheOverlayService;
        _indicatorService = indicatorService;
        _mapBuilder = mapBuilder;
        _locationLayerService = locationLayerService;
        _droppedPinLayerService = droppedPinLayerService;
        _tripLayerService = tripLayerService;
        _wikipediaService = wikipediaService;
        _settingsService = settingsService;
        _logger = logger;
        Title = "WayfarerMobile";

        // Subscribe to location events
        _locationBridge.LocationReceived += OnLocationReceived;
        _locationBridge.StateChanged += OnStateChanged;

        // Subscribe to navigation HUD events
        _navigationHudViewModel.StopNavigationRequested += OnStopNavigationRequested;

        // Subscribe to check-in completion to auto-close sheet
        _checkInViewModel.CheckInCompleted += OnCheckInCompleted;

        // Subscribe to cache status updates (updates when location changes, NOT on startup)
        _cacheStatusService.StatusChanged += OnCacheStatusChanged;
    }

    #endregion

    #region Map Creation

    /// <summary>
    /// Creates and configures this ViewModel's private map instance.
    /// </summary>
    private Map CreateMap()
    {
        // Create layers for Main page features
        _locationLayer = _mapBuilder.CreateLayer(_locationLayerService.LocationLayerName);
        _tripAreasLayer = _mapBuilder.CreateLayer(_tripLayerService.TripAreasLayerName);
        _tripSegmentsLayer = _mapBuilder.CreateLayer(_tripLayerService.TripSegmentsLayerName);
        _tripPlacesLayer = _mapBuilder.CreateLayer(_tripLayerService.TripPlacesLayerName);
        _navigationRouteCompletedLayer = _mapBuilder.CreateLayer("NavigationRouteCompleted");
        _navigationRouteLayer = _mapBuilder.CreateLayer("NavigationRoute");
        _droppedPinLayer = _mapBuilder.CreateLayer(_droppedPinLayerService.DroppedPinLayerName);

        // Create map with all layers (order: areas under segments under places under location)
        var map = _mapBuilder.CreateMap(
            _tripAreasLayer,
            _tripSegmentsLayer,
            _navigationRouteCompletedLayer,
            _navigationRouteLayer,
            _tripPlacesLayer,
            _droppedPinLayer,
            _locationLayer);

        // Set default zoom
        SetDefaultZoom(map);

        return map;
    }

    /// <summary>
    /// Sets the default zoom level for the map.
    /// </summary>
    private static void SetDefaultZoom(Map map)
    {
        // Zoom level 15 is good for street-level view
        if (map.Navigator.Resolutions?.Count > 15)
        {
            map.Navigator.ZoomTo(map.Navigator.Resolutions[15]);
        }
    }

    /// <summary>
    /// Ensures the map is initialized.
    /// </summary>
    private void EnsureMapInitialized()
    {
        _ = Map; // Force lazy initialization
    }

    /// <summary>
    /// Refreshes the map display.
    /// </summary>
    private void RefreshMap()
    {
        _map?.Refresh();
    }

    /// <summary>
    /// Zooms the map to fit the current navigation route.
    /// </summary>
    private void ZoomToNavigationRoute()
    {
        var route = _tripNavigationService.ActiveRoute;
        if (route?.Waypoints == null || route.Waypoints.Count < 2 || _map == null)
            return;

        var points = route.Waypoints
            .Select(w => Mapsui.Projections.SphericalMercator.FromLonLat(w.Longitude, w.Latitude))
            .Select(p => new Mapsui.MPoint(p.x, p.y))
            .ToList();

        _mapBuilder.ZoomToPoints(_map, points);
    }

    /// <summary>
    /// Shows the navigation route on the map.
    /// </summary>
    private void ShowNavigationRoute(NavigationRoute route)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null || _map == null)
            return;

        _mapBuilder.UpdateNavigationRoute(_navigationRouteLayer, _navigationRouteCompletedLayer, route);
    }

    /// <summary>
    /// Clears the navigation route from the map.
    /// </summary>
    private void ClearNavigationRoute()
    {
        _navigationRouteLayer?.Clear();
        _navigationRouteLayer?.DataHasChanged();
        _navigationRouteCompletedLayer?.Clear();
        _navigationRouteCompletedLayer?.DataHasChanged();
    }

    /// <summary>
    /// Checks if navigation route is currently displayed.
    /// </summary>
    private bool HasNavigationRoute =>
        _navigationRouteLayer?.GetFeatures().Any() == true;

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
        if (_locationLayer != null)
        {
            _locationLayerService.UpdateLocation(_locationLayer, location);

            // Center map if following and not navigating or browsing a trip
            // Don't auto-center when a trip is loaded - user needs to browse places
            if (IsFollowingLocation && !IsNavigating && !HasLoadedTrip && _map != null)
            {
                _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude);
            }
        }

        // Notify heading properties after LocationLayerService updates the indicator service
        // This ensures HeadingText uses the smoothed heading calculated by LocationIndicatorService
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(HasHeading));

        // Update navigation if active
        if (IsNavigating && _navigationRouteLayer != null && _navigationRouteCompletedLayer != null)
        {
            var state = _tripNavigationService.UpdateLocation(location.Latitude, location.Longitude);

            // Update route progress visualization
            var route = _tripNavigationService.ActiveRoute;
            if (route != null)
            {
                _mapBuilder.UpdateNavigationRouteProgress(
                    _navigationRouteLayer,
                    _navigationRouteCompletedLayer,
                    route,
                    location.Latitude,
                    location.Longitude);
            }

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
    private async void OnStopNavigationRequested(object? sender, string? sourcePageRoute)
    {
        StopNavigation();

        // Navigate back to source page if specified
        if (!string.IsNullOrEmpty(sourcePageRoute))
        {
            try
            {
                await Shell.Current.GoToAsync(sourcePageRoute);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Failed to navigate to {sourcePageRoute}: {ex.Message}");
            }
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
        if ((state == TrackingState.Ready || state == TrackingState.NotInitialized) && _locationLayer != null)
        {
            _locationLayerService.ClearLocation(_locationLayer);
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

        if (location != null && _map != null)
        {
            _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude);
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
    /// Zooms the map to show all relevant features (location, route, places).
    /// </summary>
    [RelayCommand]
    private void ZoomToTrack()
    {
        // Zoom to navigation route if active, otherwise just center on location
        if (IsNavigating && _tripNavigationService.ActiveRoute != null)
        {
            ZoomToNavigationRoute();
        }
        else if (CurrentLocation != null && _map != null)
        {
            _mapBuilder.CenterOnLocation(_map, CurrentLocation.Latitude, CurrentLocation.Longitude, 15);
        }
        IsFollowingLocation = false;
    }

    /// <summary>
    /// Resets the map rotation to north (0 degrees).
    /// </summary>
    [RelayCommand]
    private void ResetNorth()
    {
        _map?.Navigator.RotateTo(0);
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

        if (_droppedPinLayer != null)
        {
            _droppedPinLayerService.ShowDroppedPin(_droppedPinLayer, latitude, longitude);
        }
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

        if (_droppedPinLayer != null)
        {
            _droppedPinLayerService.ClearDroppedPin(_droppedPinLayer);
        }
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
            var route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
                currentLocation.Latitude,
                currentLocation.Longitude,
                ContextMenuLatitude,
                ContextMenuLongitude,
                "Dropped Pin",
                osrmProfile);

            // Clear dropped pin and start navigation
            ClearDroppedPin();

            // Display route and start navigation
            IsNavigating = true;
            ShowNavigationRoute(route);
            ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            IsFollowingLocation = false;

            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Started navigation to dropped pin: {route.TotalDistanceMeters / 1000:F1}km");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Failed to start navigation: {ex.Message}");
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
        catch (Exception ex)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch
            {
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
    /// Toggles the trip sheet visibility.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTripSheetAsync()
    {
        // If sheet is open, close it
        if (IsTripSheetOpen)
        {
            IsTripSheetOpen = false;
            return;
        }

        // If no trip is loaded, prompt user to select one
        if (!HasLoadedTrip)
        {
            await Shell.Current.GoToAsync("trips");
            return;
        }

        // Show the sheet with the loaded trip
        ClearTripSheetSelection();
        IsTripSheetOpen = true;
    }

    /// <summary>
    /// Closes the trip sheet.
    /// </summary>
    [RelayCommand]
    private void CloseTripSheet()
    {
        IsTripSheetOpen = false;
    }

    /// <summary>
    /// Goes back from details to overview in trip sheet.
    /// Handles nested navigation (notes views go back to their parent item).
    /// </summary>
    [RelayCommand]
    private void TripSheetBack()
    {
        // If showing area notes, go back to area details
        if (IsShowingAreaNotes)
        {
            IsShowingAreaNotes = false;
            return;
        }

        // If showing segment notes, go back to segment details
        if (IsShowingSegmentNotes)
        {
            IsShowingSegmentNotes = false;
            return;
        }

        // Otherwise, go back to overview
        ClearTripSheetSelection();
    }

    /// <summary>
    /// Clears trip sheet item selection (returns to overview).
    /// </summary>
    private void ClearTripSheetSelection()
    {
        SelectedTripPlace = null;
        SelectedTripArea = null;
        SelectedTripSegment = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
    }

    /// <summary>
    /// Shows the trip notes detail view.
    /// </summary>
    [RelayCommand]
    private void ShowTripNotes()
    {
        IsShowingTripNotes = true;
    }

    /// <summary>
    /// Shows the area notes detail view.
    /// </summary>
    [RelayCommand]
    private void ShowAreaNotes()
    {
        IsShowingAreaNotes = true;
    }

    /// <summary>
    /// Shows the segment notes detail view.
    /// </summary>
    [RelayCommand]
    private void ShowSegmentNotes()
    {
        IsShowingSegmentNotes = true;
    }

    /// <summary>
    /// Selects a place from the trip and shows details.
    /// </summary>
    [RelayCommand]
    private void SelectTripPlace(TripPlace? place)
    {
        if (place == null)
            return;

        ClearTripSheetSelection();
        SelectedTripPlace = place;
        SelectedPlace = place; // Legacy compatibility

        // Center map on selected place
        if (_map != null)
        {
            _mapBuilder.CenterOnLocation(_map, place.Latitude, place.Longitude);
        }
        IsFollowingLocation = false;
    }

    /// <summary>
    /// Selects an area from the trip and shows details.
    /// </summary>
    [RelayCommand]
    private void SelectTripArea(TripArea? area)
    {
        if (area == null)
            return;

        ClearTripSheetSelection();
        SelectedTripArea = area;

        // Center map on area center
        var center = area.Center;
        if (_map != null && center != null)
        {
            _mapBuilder.CenterOnLocation(_map, center.Latitude, center.Longitude);
        }
        IsFollowingLocation = false;
    }

    /// <summary>
    /// Selects a segment from the trip and shows details.
    /// </summary>
    [RelayCommand]
    private void SelectTripSegment(TripSegment? segment)
    {
        if (segment == null)
            return;

        ClearTripSheetSelection();
        SelectedTripSegment = segment;
    }

    /// <summary>
    /// Navigates to the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        await StartNavigationToPlaceAsync(SelectedTripPlace.Id.ToString());
        IsTripSheetOpen = false;
    }

    /// <summary>
    /// Opens selected trip place in external maps app.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceInMapsAsync()
    {
        if (SelectedTripPlace == null)
            return;

        try
        {
            var location = new Location(SelectedTripPlace.Latitude, SelectedTripPlace.Longitude);
            await Microsoft.Maui.ApplicationModel.Map.OpenAsync(location, new MapLaunchOptions
            {
                Name = SelectedTripPlace.Name,
                NavigationMode = NavigationMode.None
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open maps");
            await _toastService.ShowErrorAsync("Failed to open maps");
        }
    }

    /// <summary>
    /// Copies selected trip place coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyTripPlaceCoordsAsync()
    {
        if (SelectedTripPlace == null)
            return;

        var coords = $"{SelectedTripPlace.Latitude:F6}, {SelectedTripPlace.Longitude:F6}";
        await Clipboard.SetTextAsync(coords);
        await _toastService.ShowSuccessAsync("Coordinates copied");
    }

    /// <summary>
    /// Shares selected trip place location.
    /// </summary>
    [RelayCommand]
    private async Task ShareTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        try
        {
            var mapsUrl = $"https://www.google.com/maps/search/?api=1&query={SelectedTripPlace.Latitude},{SelectedTripPlace.Longitude}";
            await Share.RequestAsync(new ShareTextRequest
            {
                Title = SelectedTripPlace.Name,
                Text = $"{SelectedTripPlace.Name}\n{mapsUrl}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share place");
            await _toastService.ShowErrorAsync("Failed to share");
        }
    }

    /// <summary>
    /// Opens Wikipedia geosearch for selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceWikipediaAsync()
    {
        if (SelectedTripPlace == null)
            return;

        var found = await _wikipediaService.OpenNearbyArticleAsync(
            SelectedTripPlace.Latitude,
            SelectedTripPlace.Longitude);

        if (!found)
        {
            await _toastService.ShowWarningAsync("No Wikipedia article found nearby");
        }
    }

    /// <summary>
    /// Opens the edit page for the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task EditTripPlaceAsync()
    {
        if (SelectedTripPlace == null || LoadedTrip == null)
            return;

        // TODO: Navigate to place edit page when implemented
        await _toastService.ShowAsync("Edit feature coming soon");
    }

    /// <summary>
    /// Opens selected trip area in external maps app.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripAreaInMapsAsync()
    {
        var center = SelectedTripArea?.Center;
        if (center == null)
            return;

        try
        {
            var location = new Location(center.Latitude, center.Longitude);
            await Microsoft.Maui.ApplicationModel.Map.OpenAsync(location, new MapLaunchOptions
            {
                Name = SelectedTripArea?.Name ?? "Area",
                NavigationMode = NavigationMode.None
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open maps");
            await _toastService.ShowErrorAsync("Failed to open maps");
        }
    }

    /// <summary>
    /// Opens Wikipedia geosearch for selected trip area.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripAreaWikipediaAsync()
    {
        var center = SelectedTripArea?.Center;
        if (center == null)
            return;

        var found = await _wikipediaService.OpenNearbyArticleAsync(center.Latitude, center.Longitude);

        if (!found)
        {
            await _toastService.ShowWarningAsync("No Wikipedia article found nearby");
        }
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
        ClearTripSheetSelection();
        IsTripSheetOpen = false;
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
            ShowNavigationRoute(route);
            ZoomToNavigationRoute();
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
            ShowNavigationRoute(route);
            ZoomToNavigationRoute();
            await _navigationHudViewModel.StartNavigationAsync(route);
            IsFollowingLocation = false;
        }
    }

    /// <summary>
    /// Stops current navigation and returns to the prior state.
    /// If navigating to a trip place, zooms back to that place and shows the sheet.
    /// </summary>
    [RelayCommand]
    private void StopNavigation()
    {
        IsNavigating = false;
        ClearNavigationRoute();
        _navigationHudViewModel.StopNavigationDisplay();

        // Return to the selected trip place if one exists
        if (SelectedTripPlace != null && _map != null)
        {
            // Zoom to the selected place
            var sphericalPoint = Mapsui.Projections.SphericalMercator.FromLonLat(
                SelectedTripPlace.Longitude,
                SelectedTripPlace.Latitude);
            _map.Navigator.CenterOnAndZoomTo(new Mapsui.MPoint(sphericalPoint.x, sphericalPoint.y), 2000);

            // Re-open the trip sheet to show place details
            IsTripSheetOpen = true;
        }
        else
        {
            IsFollowingLocation = true;
        }
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
        ClearTripSheetSelection();

        LoadedTrip = tripDetails;
        _tripNavigationService.LoadTrip(tripDetails);

        var placePoints = new List<Mapsui.MPoint>();

        // Update places layer
        if (_tripPlacesLayer != null)
        {
            placePoints = await _tripLayerService.UpdateTripPlacesAsync(_tripPlacesLayer, tripDetails.AllPlaces);
            _logger.LogDebug("Updated {Count} places on map layer (from {Total} total)", placePoints.Count, tripDetails.AllPlaces.Count);
        }

        // Update areas layer
        if (_tripAreasLayer != null)
        {
            _tripLayerService.UpdateTripAreas(_tripAreasLayer, tripDetails.AllAreas);
        }

        // Update segments layer
        if (_tripSegmentsLayer != null)
        {
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer, tripDetails.Segments);
        }

        // Zoom map to fit all trip places
        if (_map != null && placePoints.Count > 0)
        {
            _mapBuilder.ZoomToPoints(_map, placePoints);
            IsFollowingLocation = false; // Don't auto-center on user location
            _logger.LogInformation("Zoomed map to fit {Count} trip places", placePoints.Count);
        }
        else if (_map != null && tripDetails.BoundingBox != null)
        {
            // Fallback: use trip bounding box center
            var bb = tripDetails.BoundingBox;
            var centerLat = (bb.North + bb.South) / 2;
            var centerLon = (bb.East + bb.West) / 2;
            _mapBuilder.CenterOnLocation(_map, centerLat, centerLon, zoomLevel: 12);
            IsFollowingLocation = false;
            _logger.LogInformation("Centered map on trip bounding box center");
        }

        // Force map refresh to ensure layers are rendered
        RefreshMap();
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

        // Clear all trip layers
        if (_tripPlacesLayer != null)
        {
            _tripLayerService.ClearTripPlaces(_tripPlacesLayer);
        }

        if (_tripAreasLayer != null)
        {
            _tripLayerService.ClearTripAreas(_tripAreasLayer);
        }

        if (_tripSegmentsLayer != null)
        {
            _tripLayerService.ClearTripSegments(_tripSegmentsLayer);
        }

        // Resume following user location when trip is unloaded
        IsFollowingLocation = true;

        // Recenter map on user location
        var location = CurrentLocation ?? _locationBridge.LastLocation;
        if (location != null && _map != null)
        {
            _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude);
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Ensure map is initialized
        EnsureMapInitialized();

        // Update state from bridge
        TrackingState = _locationBridge.CurrentState;
        CurrentLocation = _locationBridge.LastLocation;

        // Check permissions state
        await CheckPermissionsStateAsync();

        // Update map if we have a location
        if (CurrentLocation != null && _locationLayer != null && _map != null)
        {
            _locationLayerService.UpdateLocation(_locationLayer, CurrentLocation);
            // Only center on user if no trip is loaded
            if (!HasLoadedTrip)
            {
                _mapBuilder.CenterOnLocation(_map, CurrentLocation.Latitude, CurrentLocation.Longitude);
            }
        }

        // Refresh map to fix any layout issues (e.g., after bottom sheet closes)
        RefreshMap();

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
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from location bridge events
        _locationBridge.LocationReceived -= OnLocationReceived;
        _locationBridge.StateChanged -= OnStateChanged;

        // Unsubscribe from child ViewModel events and dispose
        _navigationHudViewModel.StopNavigationRequested -= OnStopNavigationRequested;
        _navigationHudViewModel.Dispose();
        _checkInViewModel.CheckInCompleted -= OnCheckInCompleted;

        // Unsubscribe from cache status events
        _cacheStatusService.StatusChanged -= OnCacheStatusChanged;

        // Stop location animation
        _locationLayerService.StopAnimation();

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

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

    /// <summary>
    /// Handles cache status changes from CacheStatusService.
    /// </summary>
    private void OnCacheStatusChanged(object? sender, string status)
    {
        CacheHealth = status switch
        {
            "green" => CacheHealthStatus.Good,
            "yellow" => CacheHealthStatus.Warning,
            "red" => CacheHealthStatus.Poor,
            _ => CacheHealthStatus.Unknown
        };
    }

    /// <summary>
    /// Shows cache status details in a dialog with option to show/hide overlay on map.
    /// </summary>
    [RelayCommand]
    private async Task ShowCacheStatusAsync()
    {
        try
        {
            var info = await _cacheStatusService.GetDetailedCacheInfoAsync();
            var message = _cacheStatusService.FormatStatusMessage(info);

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            // Button text depends on whether overlay is currently visible
            var buttonText = _cacheOverlayService.IsVisible ? "Hide Overlay" : "Show on Map";

            var toggleOverlay = await page.DisplayAlertAsync(
                "Cache Status",
                message,
                buttonText,
                "Close");

            if (toggleOverlay && CurrentLocation != null)
            {
                await _cacheOverlayService.ToggleOverlayAsync(
                    Map, CurrentLocation.Latitude, CurrentLocation.Longitude);
            }
            else if (toggleOverlay)
            {
                await _toastService.ShowWarningAsync("No location available for overlay");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error showing cache status: {ex.Message}");
            await _toastService.ShowErrorAsync("Could not load cache status");
        }
    }

    #endregion
}
