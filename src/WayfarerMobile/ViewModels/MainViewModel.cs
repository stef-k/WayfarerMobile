using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Services.TileCache;
using Map = Mapsui.Map;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the main page showing current location and tracking status.
/// </summary>
public partial class MainViewModel : BaseViewModel
{
    #region Static State

    /// <summary>
    /// The currently loaded trip ID (in-memory, not persisted).
    /// Used by TripsViewModel to show "Back" button for loaded trip.
    /// </summary>
    public static Guid? CurrentLoadedTripId { get; private set; }

    #endregion

    #region Fields

    private readonly ILocationBridge _locationBridge;
    private readonly IPermissionsService _permissionsService;
    private readonly ITripNavigationService _tripNavigationService;
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
    private readonly ITripSyncService _tripSyncService;
    private readonly DatabaseService _databaseService;
    private readonly IVisitNotificationService _visitNotificationService;
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
    private WritableLayer? _placeSelectionLayer;

    // Navigation state for visit notification conflict detection
    private Guid? _currentNavigationPlaceId;

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

    /// <summary>
    /// Gets or sets whether region notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingRegionNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingRegionNotes;

    /// <summary>
    /// Gets or sets the selected region for notes display.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTripRegionNotesHtml))]
    private TripRegion? _selectedTripRegion;

    /// <summary>
    /// Gets or sets whether place coordinate editing mode is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(IsAnyEditModeActive))]
    private bool _isPlaceCoordinateEditMode;

    /// <summary>
    /// Gets or sets the pending latitude during place coordinate editing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingPlaceCoordinatesText))]
    private double? _pendingPlaceLatitude;

    /// <summary>
    /// Gets or sets the pending longitude during place coordinate editing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingPlaceCoordinatesText))]
    private double? _pendingPlaceLongitude;

    /// <summary>
    /// Gets or sets the place being edited for coordinates.
    /// </summary>
    [ObservableProperty]
    private TripPlace? _placeBeingEditedForCoordinates;

    /// <summary>
    /// Tracks whether we're navigating to a sub-editor page (notes, marker, etc.).
    /// When true, OnDisappearingAsync should NOT unload the trip or close sheets.
    /// Also checked by MainPage to avoid clearing selection when navigating to sub-editors.
    /// </summary>
    public bool IsNavigatingToSubEditor { get; private set; }

    /// <summary>
    /// Pending entity ID for selection restoration from sub-editor navigation.
    /// Set by ApplyQueryAttributes, consumed by OnAppearingAsync.
    /// </summary>
    private (string? EntityType, Guid EntityId)? _pendingSelectionRestore;

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
    /// Gets whether place coordinate editing mode is active (alias for binding).
    /// </summary>
    public bool IsEditingPlaceCoordinates => IsPlaceCoordinateEditMode;

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// </summary>
    public bool IsAnyEditModeActive => IsPlaceCoordinateEditMode;

    /// <summary>
    /// Stores entity info for selection restoration after sub-editor navigation.
    /// Called from MainPage.ApplyQueryAttributes when returning from notes/marker editors.
    /// </summary>
    /// <param name="entityType">The entity type (Place, Area, Segment, Region).</param>
    /// <param name="entityId">The entity ID to restore selection for.</param>
    public void RestoreSelectionFromSubEditor(string? entityType, Guid entityId)
    {
        if (string.IsNullOrEmpty(entityType) || entityId == Guid.Empty)
            return;

        _logger.LogDebug("RestoreSelectionFromSubEditor: entityType={Type}, entityId={Id}", entityType, entityId);
        _pendingSelectionRestore = (entityType, entityId);
    }

    /// <summary>
    /// Gets whether there are pending place coordinates to save.
    /// </summary>
    public bool HasPendingPlaceCoordinates => PendingPlaceLatitude.HasValue && PendingPlaceLongitude.HasValue;

    /// <summary>
    /// Gets the pending place coordinates text for display.
    /// </summary>
    public string PendingPlaceCoordinatesText => HasPendingPlaceCoordinates
        ? $"{PendingPlaceLatitude:F6}, {PendingPlaceLongitude:F6}"
        : "Tap on map to set location";

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
        SelectedTripPlace == null && SelectedTripArea == null && SelectedTripSegment == null && !IsShowingTripNotes && !IsShowingRegionNotes;

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
    /// Gets whether trip sheet is showing region notes detail view.
    /// </summary>
    public bool IsTripSheetShowingRegionNotes => IsShowingRegionNotes;

    /// <summary>
    /// Gets whether the scrollable content area should be visible (Overview, Area, or Segment - not Place, TripNotes, AreaNotes, SegmentNotes, or RegionNotes).
    /// </summary>
    public bool IsTripSheetShowingScrollableContent =>
        !IsTripSheetShowingPlace && !IsTripSheetShowingTripNotes && !IsTripSheetShowingAreaNotes && !IsTripSheetShowingSegmentNotes && !IsTripSheetShowingRegionNotes;

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
                return "Trip Overview";
            if (IsShowingAreaNotes && SelectedTripArea != null)
                return $"{SelectedTripArea.Name} - Notes";
            if (SelectedTripArea != null)
                return SelectedTripArea.Name;
            if (IsShowingSegmentNotes && SelectedTripSegment != null)
                return "Segment Notes";
            if (SelectedTripSegment != null)
                return $"Segment: {SelectedTripSegment.TransportMode ?? "Route"}";
            if (IsShowingRegionNotes && SelectedTripRegion != null)
                return $"{SelectedTripRegion.Name} - Notes";
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
    /// Gets or sets whether the place search bar is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isPlaceSearchVisible;

    /// <summary>
    /// Gets or sets the place search query text.
    /// </summary>
    [ObservableProperty]
    private string _placeSearchQuery = string.Empty;

    /// <summary>
    /// Cached search results to avoid recomputation on every property access.
    /// </summary>
    private List<TripPlace> _cachedSearchResults = new();

    /// <summary>
    /// Called when PlaceSearchQuery changes - updates cached results.
    /// </summary>
    partial void OnPlaceSearchQueryChanged(string value)
    {
        UpdateSearchResults();
    }

    /// <summary>
    /// Updates the cached search results.
    /// </summary>
    private void UpdateSearchResults()
    {
        if (LoadedTrip == null || string.IsNullOrWhiteSpace(PlaceSearchQuery))
        {
            _cachedSearchResults = new List<TripPlace>();
        }
        else
        {
            var query = PlaceSearchQuery.Trim();
            _cachedSearchResults = LoadedTrip.AllPlaces
                .Where(p =>
                    (p.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                    (p.Address?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        OnPropertyChanged(nameof(PlaceSearchResults));
        OnPropertyChanged(nameof(HasPlaceSearchResults));
    }

    /// <summary>
    /// Gets the filtered list of places matching the search query.
    /// </summary>
    public List<TripPlace> PlaceSearchResults => _cachedSearchResults;

    /// <summary>
    /// Gets whether there are search results to display.
    /// </summary>
    public bool HasPlaceSearchResults => _cachedSearchResults.Count > 0;

    /// <summary>
    /// Toggles the place search bar visibility.
    /// </summary>
    [RelayCommand]
    private void TogglePlaceSearch()
    {
        IsPlaceSearchVisible = !IsPlaceSearchVisible;
        if (!IsPlaceSearchVisible)
        {
            // Clear search when closing
            PlaceSearchQuery = string.Empty;
        }
    }

    /// <summary>
    /// Closes the place search and resets to normal view.
    /// </summary>
    private void CloseSearchIfActive()
    {
        if (IsPlaceSearchVisible)
        {
            IsPlaceSearchVisible = false;
            PlaceSearchQuery = string.Empty;
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

    /// <summary>
    /// Gets the HTML content for the selected trip region notes, wrapped for WebView display.
    /// Uses notes-viewer.html template for proper CSP and image handling.
    /// </summary>
    public HtmlWebViewSource? SelectedTripRegionNotesHtml
    {
        get
        {
            if (SelectedTripRegion?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripRegion.Notes, _settingsService.ServerUrl, isDark);
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
    /// <param name="tripSyncService">The trip sync service.</param>
    /// <param name="databaseService">The database service for refreshing edited entities.</param>
    /// <param name="visitNotificationService">The visit notification service for SSE visit events.</param>
    /// <param name="logger">The logger instance.</param>
    public MainViewModel(
        ILocationBridge locationBridge,
        IPermissionsService permissionsService,
        ITripNavigationService tripNavigationService,
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
        ITripSyncService tripSyncService,
        DatabaseService databaseService,
        IVisitNotificationService visitNotificationService,
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
        _tripSyncService = tripSyncService;
        _databaseService = databaseService;
        _visitNotificationService = visitNotificationService;
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
        _placeSelectionLayer = _mapBuilder.CreateLayer(_tripLayerService.PlaceSelectionLayerName);
        _tripPlacesLayer = _mapBuilder.CreateLayer(_tripLayerService.TripPlacesLayerName);
        _navigationRouteCompletedLayer = _mapBuilder.CreateLayer("NavigationRouteCompleted");
        _navigationRouteLayer = _mapBuilder.CreateLayer("NavigationRoute");
        _droppedPinLayer = _mapBuilder.CreateLayer(_droppedPinLayerService.DroppedPinLayerName);

        // Create map with all layers (order: areas under segments under selection under places under location)
        var map = _mapBuilder.CreateMap(
            _tripAreasLayer,
            _tripSegmentsLayer,
            _navigationRouteCompletedLayer,
            _navigationRouteLayer,
            _placeSelectionLayer,
            _tripPlacesLayer,
            _droppedPinLayer,
            _locationLayer);

        // Set initial map position based on last known location
        SetInitialMapPosition(map);

        return map;
    }

    /// <summary>
    /// Sets the initial map position based on last known location.
    /// If no location is available, shows globe view instead of zooming into null island (0,0).
    /// </summary>
    private void SetInitialMapPosition(Map map)
    {
        var lastLocation = _locationBridge.LastLocation;

        if (lastLocation != null)
        {
            // We have a cached location - center on it at street level
            _mapBuilder.CenterOnLocation(map, lastLocation.Latitude, lastLocation.Longitude, zoomLevel: 15);
            _logger.LogDebug("Map initialized at last known location: {Lat}, {Lon}",
                lastLocation.Latitude, lastLocation.Longitude);
        }
        else
        {
            // No location available - show globe view (zoom 2) so user sees the full globe
            // instead of being zoomed into the ocean at 0,0
            if (map.Navigator.Resolutions?.Count > 2)
            {
                map.Navigator.ZoomTo(map.Navigator.Resolutions[2]);
            }
            _logger.LogDebug("Map initialized at globe view (no location available)");
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
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid shell navigation to {Route}", sourcePageRoute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to navigate to {Route}", sourcePageRoute);
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
    /// Navigates to My Trips tab in the Trips page.
    /// </summary>
    [RelayCommand]
    private async Task GoToMyTripsAsync()
    {
        IsTripSheetOpen = false;
        await Shell.Current.GoToAsync("//trips");
    }

    /// <summary>
    /// Goes back from details to overview in trip sheet.
    /// Handles nested navigation (notes views go back to their parent item).
    /// </summary>
    [RelayCommand]
    private void TripSheetBack()
    {
        _logger.LogDebug("TripSheetBack: SelectedPlace={Place}", SelectedTripPlace?.Name ?? "null");

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

        // If showing region notes, go back to overview
        if (IsShowingRegionNotes)
        {
            IsShowingRegionNotes = false;
            SelectedTripRegion = null;
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
        _logger.LogDebug("ClearTripSheetSelection: Clearing selection (was: {Place})", SelectedTripPlace?.Name ?? "null");
        SelectedTripPlace = null;
        SelectedTripArea = null;
        SelectedTripSegment = null;
        SelectedTripRegion = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
        IsShowingRegionNotes = false;

        // Clear selection ring on map
        if (_placeSelectionLayer != null)
        {
            _logger.LogDebug("ClearPlaceSelection: layer features before={Count}",
                _placeSelectionLayer.GetFeatures().Count());
            _tripLayerService.ClearPlaceSelection(_placeSelectionLayer);
        }
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
    /// Shows the region notes detail view.
    /// </summary>
    [RelayCommand]
    private void ShowRegionNotes(TripRegion? region)
    {
        if (region == null)
            return;

        SelectedTripRegion = region;
        IsShowingRegionNotes = true;
    }

    /// <summary>
    /// Selects a place from the trip and shows details.
    /// </summary>
    [RelayCommand]
    private void SelectTripPlace(TripPlace? place)
    {
        if (place == null)
            return;

        // Close search if active (user selected from search results)
        CloseSearchIfActive();

        ClearTripSheetSelection();
        SelectedTripPlace = place;
        SelectedPlace = place; // Legacy compatibility

        // Show selection ring on map
        if (_placeSelectionLayer != null)
        {
            _logger.LogDebug("UpdatePlaceSelection: place={PlaceId}, layer features before={Count}",
                place.Id, _placeSelectionLayer.GetFeatures().Count());
            _tripLayerService.UpdatePlaceSelection(_placeSelectionLayer, place);
        }

        // Center and zoom in on selected place for better view
        if (_map != null)
        {
            _mapBuilder.CenterOnLocation(_map, place.Latitude, place.Longitude, zoomLevel: 16);
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
    /// Prompts the user to select a transport mode before starting navigation.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        // Show transport mode picker
        var transportModes = new[] { "Walk", "Drive", "Cycle" };

        var selected = await Shell.Current.DisplayActionSheetAsync(
            "Navigation Mode",
            "Cancel",
            null,
            transportModes);

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        // Map display name to OSRM profile
        var profile = selected switch
        {
            "Walk" => "foot",
            "Drive" => "car",
            "Cycle" => "bike",
            _ => "foot"
        };

        // Save selection for next time
        _settingsService.LastTransportMode = profile;

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
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Maps feature not supported on this device");
            await _toastService.ShowErrorAsync("Maps not available on this device");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening maps");
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
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported on this device");
            await _toastService.ShowErrorAsync("Share not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sharing place");
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

        var action = await Shell.Current.DisplayActionSheetAsync(
            $"Edit: {SelectedTripPlace.Name}",
            "Cancel",
            "Delete",
            "Edit Name",
            "Edit Notes",
            "Edit Coordinates",
            "Edit Marker");

        switch (action)
        {
            case "Edit Name":
                await EditPlaceNameAsync(SelectedTripPlace);
                break;
            case "Edit Notes":
                await EditPlaceNotesAsync(SelectedTripPlace);
                break;
            case "Edit Coordinates":
                EnterPlaceCoordinateEditMode(SelectedTripPlace);
                break;
            case "Edit Marker":
                await EditPlaceMarkerAsync(SelectedTripPlace);
                break;
            case "Delete":
                await DeletePlaceAsync(SelectedTripPlace);
                break;
        }
    }

    /// <summary>
    /// Edits the name of a trip place using an inline prompt.
    /// </summary>
    private async Task EditPlaceNameAsync(TripPlace place)
    {
        var newName = await Shell.Current.DisplayPromptAsync(
            "Edit Place Name",
            "Enter new name:",
            initialValue: place.Name,
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(newName) || newName == place.Name)
            return;

        try
        {
            // Find the region containing this place
            var region = LoadedTrip!.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
            if (region == null)
            {
                await _toastService.ShowErrorAsync("Place not found in trip");
                return;
            }

            // Find the actual place in the region
            var actualPlace = region.Places.FirstOrDefault(p => p.Id == place.Id);
            if (actualPlace == null)
            {
                await _toastService.ShowErrorAsync("Place not found");
                return;
            }

            // Optimistically update UI
            actualPlace.Name = newName.Trim();

            // Update the selected place reference too
            SelectedTripPlace = actualPlace;

            // Explicitly notify TripSheetTitle since place name changed
            OnPropertyChanged(nameof(TripSheetTitle));

            // Refresh the sorted regions view
            LoadedTrip.NotifySortedRegionsChanged();

            // Queue server sync
            await _tripSyncService.UpdatePlaceAsync(
                place.Id,
                LoadedTrip.Id,
                name: newName.Trim());

            await _toastService.ShowSuccessAsync("Place name updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update place name");
            await _toastService.ShowErrorAsync($"Failed to update: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates to the notes editor for a trip place.
    /// </summary>
    private async Task EditPlaceNotesAsync(TripPlace place)
    {
        _logger.LogDebug("EditPlaceNotesAsync: Setting IsNavigatingToSubEditor=true, navigating to notesEditor");
        IsNavigatingToSubEditor = true;

        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip!.Id.ToString() },
            { "entityId", place.Id.ToString() },
            { "notes", place.Notes ?? string.Empty },
            { "entityType", "Place" }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
    }

    /// <summary>
    /// Deletes a trip place after confirmation.
    /// </summary>
    private async Task DeletePlaceAsync(TripPlace place)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var confirm = await page.DisplayAlertAsync(
            "Delete Place",
            $"Are you sure you want to delete \"{place.Name}\"?\n\nThis action cannot be undone.",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            // Find the region containing this place
            var region = LoadedTrip!.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
            if (region == null)
            {
                await _toastService.ShowErrorAsync("Place not found in trip");
                return;
            }

            // Optimistically remove from UI
            var actualPlace = region.Places.FirstOrDefault(p => p.Id == place.Id);
            if (actualPlace != null)
            {
                region.Places.Remove(actualPlace);
            }

            // Clear selection and go back to overview
            ClearTripSheetSelection();

            // Refresh the sorted regions view
            LoadedTrip.NotifySortedRegionsChanged();

            // Update map layers
            await RefreshTripOnMapAsync();

            // Queue server sync
            await _tripSyncService.DeletePlaceAsync(place.Id, LoadedTrip.Id);

            await _toastService.ShowSuccessAsync("Place deleted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete place");
            await _toastService.ShowErrorAsync($"Failed to delete: {ex.Message}");
        }
    }

    #region Place Coordinate Editing

    /// <summary>
    /// Enters coordinate editing mode for a place.
    /// Closes the bottom sheet and shows the coordinate edit overlay.
    /// </summary>
    /// <param name="place">The place to edit coordinates for.</param>
    private void EnterPlaceCoordinateEditMode(TripPlace place)
    {
        if (place == null || LoadedTrip == null)
            return;

        // Store the place being edited
        PlaceBeingEditedForCoordinates = place;

        // Set initial pending coordinates to current place location
        PendingPlaceLatitude = place.Latitude;
        PendingPlaceLongitude = place.Longitude;

        // Enter edit mode (triggers UI changes)
        IsPlaceCoordinateEditMode = true;

        // Close the trip sheet to expose the map
        IsTripSheetOpen = false;

        // The page code-behind will handle showing the temp marker
    }

    /// <summary>
    /// Sets the pending place coordinates from a map tap.
    /// Called by the page code-behind when the map is tapped during coordinate editing.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void SetPendingPlaceCoordinates(double latitude, double longitude)
    {
        if (!IsPlaceCoordinateEditMode)
            return;

        PendingPlaceLatitude = latitude;
        PendingPlaceLongitude = longitude;

        // The page code-behind will handle updating the temp marker
    }

    /// <summary>
    /// Saves the pending place coordinates.
    /// </summary>
    [RelayCommand]
    private async Task SavePlaceCoordinatesAsync()
    {
        if (PlaceBeingEditedForCoordinates == null || !HasPendingPlaceCoordinates || LoadedTrip == null)
            return;

        try
        {
            var place = PlaceBeingEditedForCoordinates;
            var newLat = PendingPlaceLatitude!.Value;
            var newLon = PendingPlaceLongitude!.Value;

            // Find the actual place in the region
            var region = LoadedTrip.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
            var actualPlace = region?.Places.FirstOrDefault(p => p.Id == place.Id);

            if (actualPlace != null)
            {
                // Optimistically update UI
                actualPlace.Latitude = newLat;
                actualPlace.Longitude = newLon;
            }

            // Exit edit mode
            ExitPlaceCoordinateEditMode();

            // Refresh map markers
            await RefreshTripOnMapAsync();

            // Queue server sync
            await _tripSyncService.UpdatePlaceAsync(
                place.Id,
                LoadedTrip.Id,
                latitude: newLat,
                longitude: newLon);

            await _toastService.ShowSuccessAsync("Location updated");

            // Reopen the trip sheet with the updated place selected
            if (actualPlace != null)
            {
                SelectedTripPlace = actualPlace;
                IsTripSheetOpen = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update place coordinates");
            await _toastService.ShowErrorAsync($"Failed to update: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels place coordinate editing.
    /// </summary>
    [RelayCommand]
    private void CancelPlaceCoordinateEditing()
    {
        var place = PlaceBeingEditedForCoordinates;
        ExitPlaceCoordinateEditMode();

        // Reopen the trip sheet with the original place selected
        if (place != null)
        {
            SelectedTripPlace = place;
            IsTripSheetOpen = true;
        }
    }

    /// <summary>
    /// Exits place coordinate editing mode and cleans up.
    /// </summary>
    private void ExitPlaceCoordinateEditMode()
    {
        IsPlaceCoordinateEditMode = false;
        PendingPlaceLatitude = null;
        PendingPlaceLongitude = null;
        PlaceBeingEditedForCoordinates = null;
        // The page code-behind will handle removing the temp marker
    }

    #endregion

    /// <summary>
    /// Navigates to the marker editor for a trip place.
    /// </summary>
    private async Task EditPlaceMarkerAsync(TripPlace place)
    {
        if (place == null || LoadedTrip == null)
            return;

        _logger.LogDebug("EditPlaceMarkerAsync: Setting IsNavigatingToSubEditor=true, navigating to markerEditor");
        IsNavigatingToSubEditor = true;

        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "placeId", place.Id.ToString() },
            { "currentColor", place.MarkerColor ?? IconCatalog.DefaultColor },
            { "currentIcon", place.Icon ?? IconCatalog.DefaultIcon }
        };

        await Shell.Current.GoToAsync("markerEditor", navParams);
    }

    /// <summary>
    /// Opens edit options for a trip region.
    /// </summary>
    [RelayCommand]
    private async Task EditRegionAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        // Don't allow editing the "Unassigned Places" region
        if (region.Name == TripRegion.UnassignedPlacesName)
        {
            await _toastService.ShowWarningAsync("Cannot edit the Unassigned Places region");
            return;
        }

        var action = await Shell.Current.DisplayActionSheetAsync(
            $"Edit: {region.Name}",
            "Cancel",
            null,
            "Edit Name",
            "Edit Notes");

        switch (action)
        {
            case "Edit Name":
                await EditRegionNameAsync(region);
                break;
            case "Edit Notes":
                await EditRegionNotesAsync(region);
                break;
        }
    }

    /// <summary>
    /// Edits the name of a trip region using an inline prompt.
    /// </summary>
    private async Task EditRegionNameAsync(TripRegion region)
    {
        var newName = await Shell.Current.DisplayPromptAsync(
            "Edit Region Name",
            "Enter new name:",
            initialValue: region.Name,
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(newName) || newName == region.Name)
            return;

        // Don't allow renaming to reserved name
        if (newName.Trim().Equals(TripRegion.UnassignedPlacesName, StringComparison.OrdinalIgnoreCase))
        {
            await _toastService.ShowWarningAsync("This region name is reserved");
            return;
        }

        try
        {
            // Find the actual region in the Regions list (SortedRegions creates copies)
            var actualRegion = LoadedTrip!.Regions.FirstOrDefault(r => r.Id == region.Id);
            if (actualRegion == null)
            {
                await _toastService.ShowErrorAsync("Region not found");
                return;
            }

            // Optimistically update UI
            actualRegion.Name = newName.Trim();

            // Refresh the sorted regions view
            LoadedTrip.NotifySortedRegionsChanged();

            // Queue server sync
            await _tripSyncService.UpdateRegionAsync(
                region.Id,
                LoadedTrip.Id,
                name: newName.Trim());

            await _toastService.ShowSuccessAsync("Region name updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update region name");
            await _toastService.ShowErrorAsync($"Failed to update: {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates to the notes editor for a trip region.
    /// </summary>
    private async Task EditRegionNotesAsync(TripRegion region)
    {
        IsNavigatingToSubEditor = true;

        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip!.Id.ToString() },
            { "entityId", region.Id.ToString() },
            { "notes", region.Notes ?? string.Empty },
            { "entityType", "Region" }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
    }

    /// <summary>
    /// Deletes a trip region after confirmation.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRegionAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        // Don't allow deleting the "Unassigned Places" region
        if (region.Name == TripRegion.UnassignedPlacesName)
        {
            await _toastService.ShowWarningAsync("Cannot delete the Unassigned Places region");
            return;
        }

        // Show confirmation dialog
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var placesCount = region.Places.Count;
        var message = placesCount > 0
            ? $"Delete region '{region.Name}' and its {placesCount} place{(placesCount == 1 ? "" : "s")}? This action cannot be undone."
            : $"Delete region '{region.Name}'? This action cannot be undone.";

        var confirm = await page.DisplayAlertAsync(
            "Delete Region",
            message,
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            // Queue server sync for deletion
            await _tripSyncService.DeleteRegionAsync(region.Id, LoadedTrip.Id);

            // Find the actual region in the Regions list (SortedRegions creates copies)
            var actualRegion = LoadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
            if (actualRegion != null)
            {
                LoadedTrip.Regions.Remove(actualRegion);
                LoadedTrip.NotifySortedRegionsChanged();
            }

            await _toastService.ShowSuccessAsync("Region deleted");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete region");
            await _toastService.ShowErrorAsync($"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a region up in the display order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionUpAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip?.Regions == null)
            return;

        // Don't allow moving the "Unassigned Places" region
        if (region.Name == TripRegion.UnassignedPlacesName)
            return;

        // Find the actual region in the Regions list (SortedRegions creates copies)
        var actualRegion = LoadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
        if (actualRegion == null)
            return;

        var regions = LoadedTrip.Regions
            .Where(r => r.Name != TripRegion.UnassignedPlacesName)
            .OrderBy(r => r.SortOrder)
            .ToList();

        var currentIndex = regions.IndexOf(actualRegion);
        if (currentIndex <= 0)
            return; // Already at top

        // Swap with previous region
        var previousRegion = regions[currentIndex - 1];
        var tempOrder = actualRegion.SortOrder;
        actualRegion.SortOrder = previousRegion.SortOrder;
        previousRegion.SortOrder = tempOrder;

        // Queue server syncs for both regions
        await _tripSyncService.UpdateRegionAsync(actualRegion.Id, LoadedTrip.Id, displayOrder: actualRegion.SortOrder);
        await _tripSyncService.UpdateRegionAsync(previousRegion.Id, LoadedTrip.Id, displayOrder: previousRegion.SortOrder);

        // Refresh the sorted regions view
        LoadedTrip.NotifySortedRegionsChanged();

        await _toastService.ShowSuccessAsync("Region moved up");
    }

    /// <summary>
    /// Moves a region down in the display order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionDownAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip?.Regions == null)
            return;

        // Don't allow moving the "Unassigned Places" region
        if (region.Name == TripRegion.UnassignedPlacesName)
            return;

        // Find the actual region in the Regions list (SortedRegions creates copies)
        var actualRegion = LoadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
        if (actualRegion == null)
            return;

        var regions = LoadedTrip.Regions
            .Where(r => r.Name != TripRegion.UnassignedPlacesName)
            .OrderBy(r => r.SortOrder)
            .ToList();

        var currentIndex = regions.IndexOf(actualRegion);
        if (currentIndex < 0 || currentIndex >= regions.Count - 1)
            return; // Already at bottom

        // Swap with next region
        var nextRegion = regions[currentIndex + 1];
        var tempOrder = actualRegion.SortOrder;
        actualRegion.SortOrder = nextRegion.SortOrder;
        nextRegion.SortOrder = tempOrder;

        // Queue server syncs for both regions
        await _tripSyncService.UpdateRegionAsync(actualRegion.Id, LoadedTrip.Id, displayOrder: actualRegion.SortOrder);
        await _tripSyncService.UpdateRegionAsync(nextRegion.Id, LoadedTrip.Id, displayOrder: nextRegion.SortOrder);

        // Refresh the sorted regions view
        LoadedTrip.NotifySortedRegionsChanged();

        await _toastService.ShowSuccessAsync("Region moved down");
    }

    /// <summary>
    /// Moves a place up in the display order within its region.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceUpAsync(TripPlace? place)
    {
        if (place == null || LoadedTrip == null)
            return;

        // Find the region containing this place
        var region = LoadedTrip.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
        if (region == null)
            return;

        // Get ordered places in this region
        var places = region.Places.OrderBy(p => p.SortOrder ?? 0).ToList();
        var currentIndex = places.FindIndex(p => p.Id == place.Id);

        if (currentIndex <= 0)
            return; // Already at top

        // Find the actual place objects
        var actualPlace = places[currentIndex];
        var previousPlace = places[currentIndex - 1];

        // Swap sort orders
        var tempOrder = actualPlace.SortOrder ?? currentIndex;
        actualPlace.SortOrder = previousPlace.SortOrder ?? (currentIndex - 1);
        previousPlace.SortOrder = tempOrder;

        // Queue server syncs for both places
        await _tripSyncService.UpdatePlaceAsync(actualPlace.Id, LoadedTrip.Id, displayOrder: actualPlace.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(previousPlace.Id, LoadedTrip.Id, displayOrder: previousPlace.SortOrder);

        // Refresh the sorted regions view
        LoadedTrip.NotifySortedRegionsChanged();

        await _toastService.ShowSuccessAsync("Place moved up");
    }

    /// <summary>
    /// Moves a place down in the display order within its region.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceDownAsync(TripPlace? place)
    {
        if (place == null || LoadedTrip == null)
            return;

        // Find the region containing this place
        var region = LoadedTrip.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
        if (region == null)
            return;

        // Get ordered places in this region
        var places = region.Places.OrderBy(p => p.SortOrder ?? 0).ToList();
        var currentIndex = places.FindIndex(p => p.Id == place.Id);

        if (currentIndex < 0 || currentIndex >= places.Count - 1)
            return; // Already at bottom

        // Find the actual place objects
        var actualPlace = places[currentIndex];
        var nextPlace = places[currentIndex + 1];

        // Swap sort orders
        var tempOrder = actualPlace.SortOrder ?? currentIndex;
        actualPlace.SortOrder = nextPlace.SortOrder ?? (currentIndex + 1);
        nextPlace.SortOrder = tempOrder;

        // Queue server syncs for both places
        await _tripSyncService.UpdatePlaceAsync(actualPlace.Id, LoadedTrip.Id, displayOrder: actualPlace.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(nextPlace.Id, LoadedTrip.Id, displayOrder: nextPlace.SortOrder);

        // Refresh the sorted regions view
        LoadedTrip.NotifySortedRegionsChanged();

        await _toastService.ShowSuccessAsync("Place moved down");
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
    /// Shows edit options for the selected area (currently just notes).
    /// </summary>
    [RelayCommand]
    private async Task EditAreaAsync()
    {
        if (SelectedTripArea == null || LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;

        // Navigate to notes editor
        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", SelectedTripArea.Id.ToString() },
            { "notes", SelectedTripArea.Notes ?? string.Empty },
            { "entityType", "Area" }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
    }

    /// <summary>
    /// Shows edit options for the selected segment (currently just notes).
    /// </summary>
    [RelayCommand]
    private async Task EditSegmentAsync()
    {
        if (SelectedTripSegment == null || LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;

        // Navigate to notes editor
        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", SelectedTripSegment.Id.ToString() },
            { "notes", SelectedTripSegment.Notes ?? string.Empty },
            { "entityType", "Segment" }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
    }

    /// <summary>
    /// Shows options to add a new region or place to the trip.
    /// </summary>
    [RelayCommand]
    private async Task AddToTripAsync()
    {
        if (LoadedTrip == null)
            return;

        var action = await Shell.Current.DisplayActionSheetAsync(
            "Add to Trip",
            "Cancel",
            null,
            "Add Region",
            "Add Place (to current location)");

        switch (action)
        {
            case "Add Region":
                await AddRegionAsync();
                break;
            case "Add Place (to current location)":
                await AddPlaceToCurrentLocationAsync();
                break;
        }
    }

    private async Task AddRegionAsync()
    {
        if (LoadedTrip == null)
            return;

        var name = await Shell.Current.DisplayPromptAsync(
            "Add Region",
            "Enter region name:",
            placeholder: "Region name",
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(name))
            return;

        try
        {
            // Create new region with a temporary client-side ID
            var tempId = Guid.NewGuid();
            var newRegion = new TripRegion
            {
                Id = tempId,
                Name = name.Trim(),
                SortOrder = LoadedTrip.Regions.Count
            };

            // Add to local collection
            LoadedTrip.Regions.Add(newRegion);
            LoadedTrip.NotifySortedRegionsChanged();

            // Queue server sync
            await _tripSyncService.CreateRegionAsync(LoadedTrip.Id, name.Trim(), null, null, null, null, newRegion.SortOrder);

            await _toastService.ShowSuccessAsync("Region added");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add region");
            await _toastService.ShowErrorAsync($"Failed to add region: {ex.Message}");
        }
    }

    private async Task AddPlaceToCurrentLocationAsync()
    {
        if (LoadedTrip == null || CurrentLocation == null)
        {
            await _toastService.ShowWarningAsync("Current location not available");
            return;
        }

        // Get list of regions to choose from
        var regions = LoadedTrip.Regions
            .Where(r => r.Name != TripRegion.UnassignedPlacesName)
            .ToList();

        if (regions.Count == 0)
        {
            await _toastService.ShowWarningAsync("Create a region first");
            return;
        }

        // Ask for region selection
        var regionNames = regions.Select(r => r.Name).ToArray();
        var selectedRegionName = await Shell.Current.DisplayActionSheetAsync(
            "Select Region",
            "Cancel",
            null,
            regionNames);

        if (selectedRegionName == null || selectedRegionName == "Cancel")
            return;

        var selectedRegion = regions.FirstOrDefault(r => r.Name == selectedRegionName);
        if (selectedRegion == null)
            return;

        // Ask for place name
        var placeName = await Shell.Current.DisplayPromptAsync(
            "Add Place",
            "Enter place name:",
            placeholder: "Place name",
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(placeName))
            return;

        try
        {
            var tempId = Guid.NewGuid();
            var newPlace = new TripPlace
            {
                Id = tempId,
                Name = placeName.Trim(),
                Latitude = CurrentLocation.Latitude,
                Longitude = CurrentLocation.Longitude,
                SortOrder = selectedRegion.Places.Count
            };

            // Add to local collection
            selectedRegion.Places.Add(newPlace);
            LoadedTrip.NotifySortedRegionsChanged();

            // Queue server sync
            await _tripSyncService.CreatePlaceAsync(
                LoadedTrip.Id,
                selectedRegion.Id,
                placeName.Trim(),
                CurrentLocation.Latitude,
                CurrentLocation.Longitude,
                null,
                null,
                null,
                newPlace.SortOrder);

            await _toastService.ShowSuccessAsync("Place added");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add place");
            await _toastService.ShowErrorAsync($"Failed to add place: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows edit options for the loaded trip (name/notes).
    /// </summary>
    [RelayCommand]
    private async Task EditLoadedTripAsync()
    {
        if (LoadedTrip == null)
            return;

        var action = await Shell.Current.DisplayActionSheetAsync(
            $"Edit: {LoadedTrip.Name}",
            "Cancel",
            null,
            "Edit Name",
            "Edit Notes");

        switch (action)
        {
            case "Edit Name":
                await EditLoadedTripNameAsync();
                break;
            case "Edit Notes":
                await EditLoadedTripNotesAsync();
                break;
        }
    }

    private async Task EditLoadedTripNameAsync()
    {
        if (LoadedTrip == null)
            return;

        var newName = await Shell.Current.DisplayPromptAsync(
            "Edit Trip Name",
            "Enter new name:",
            initialValue: LoadedTrip.Name,
            maxLength: 200,
            keyboard: Keyboard.Text);

        if (string.IsNullOrWhiteSpace(newName) || newName == LoadedTrip.Name)
            return;

        try
        {
            LoadedTrip.Name = newName.Trim();

            // Queue server sync (updates both local storage and server)
            await _tripSyncService.UpdateTripAsync(LoadedTrip.Id, name: newName.Trim());

            await _toastService.ShowSuccessAsync("Trip name updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update trip name");
            await _toastService.ShowErrorAsync($"Failed to update: {ex.Message}");
        }
    }

    private async Task EditLoadedTripNotesAsync()
    {
        if (LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;

        var navParams = new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "notes", LoadedTrip.Notes ?? string.Empty },
            { "entityType", "Trip" }
        };

        await Shell.Current.GoToAsync("notesEditor", navParams);
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
            // Track navigation destination for visit notification conflict detection
            _currentNavigationPlaceId = Guid.TryParse(placeId, out var guid) ? guid : null;
            _visitNotificationService.UpdateNavigationState(true, _currentNavigationPlaceId);

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
            // Track navigation destination for visit notification conflict detection
            // Note: For "next place" we don't have the place ID readily available
            // The navigation graph knows the destination but it's not exposed here
            _currentNavigationPlaceId = null;
            _visitNotificationService.UpdateNavigationState(true, null);

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
        // Notify visit notification service that navigation ended
        _currentNavigationPlaceId = null;
        _visitNotificationService.UpdateNavigationState(false, null);

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
    /// Refreshes the trip display on the map.
    /// Call this after modifying places, areas, or segments.
    /// </summary>
    private async Task RefreshTripOnMapAsync()
    {
        if (LoadedTrip == null)
            return;

        // Update places layer
        if (_tripPlacesLayer != null)
        {
            await _tripLayerService.UpdateTripPlacesAsync(_tripPlacesLayer, LoadedTrip.AllPlaces);
        }

        // Update areas layer
        if (_tripAreasLayer != null)
        {
            _tripLayerService.UpdateTripAreas(_tripAreasLayer, LoadedTrip.AllAreas);
        }

        // Update segments layer
        if (_tripSegmentsLayer != null)
        {
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer, LoadedTrip.Segments);
        }

        // Force map refresh
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

        if (_placeSelectionLayer != null)
        {
            _logger.LogDebug("UnloadTrip.ClearPlaceSelection: layer features before={Count}",
                _placeSelectionLayer.GetFeatures().Count());
            _tripLayerService.ClearPlaceSelection(_placeSelectionLayer);
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

    /// <summary>
    /// Called when LoadedTrip changes - saves/clears the trip ID in Preferences.
    /// </summary>
    partial void OnLoadedTripChanged(TripDetails? value)
    {
        // Update static property for cross-ViewModel access
        CurrentLoadedTripId = value?.Id;
        _logger.LogDebug("OnLoadedTripChanged: CurrentLoadedTripId set to {TripId}", CurrentLoadedTripId);

        // Reset search state when trip changes
        IsPlaceSearchVisible = false;
        PlaceSearchQuery = string.Empty;
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Check if we have a pending selection to restore from sub-editor navigation
        var pendingRestore = _pendingSelectionRestore;
        _pendingSelectionRestore = null;

        // Check if we just returned from a sub-editor
        var wasInSubEditor = IsNavigatingToSubEditor;
        IsNavigatingToSubEditor = false;

        _logger.LogDebug("OnAppearingAsync: wasInSubEditor={WasInSubEditor}, pendingRestore={Restore}, SelectedPlace={Place}",
            wasInSubEditor, pendingRestore?.EntityType ?? "null", SelectedTripPlace?.Name ?? "null");

        // If we have a pending selection restore, apply it
        if (pendingRestore.HasValue && LoadedTrip != null)
        {
            await ApplyPendingSelectionRestoreAsync(pendingRestore.Value.EntityType, pendingRestore.Value.EntityId);
        }
        // Otherwise, if we just returned from sub-editor, refresh in place
        else if (wasInSubEditor && LoadedTrip != null)
        {
            await RefreshEditedEntitiesAsync();
        }

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
    /// Applies pending selection restoration after returning from a sub-editor.
    /// Finds the entity in the loaded trip and restores selection, then refreshes from database.
    /// </summary>
    private async Task ApplyPendingSelectionRestoreAsync(string? entityType, Guid entityId)
    {
        if (LoadedTrip == null || string.IsNullOrEmpty(entityType))
            return;

        _logger.LogDebug("ApplyPendingSelectionRestoreAsync: type={Type}, id={Id}", entityType, entityId);

        try
        {
            switch (entityType)
            {
                case "Place":
                    var place = LoadedTrip.AllPlaces.FirstOrDefault(p => p.Id == entityId);
                    if (place != null)
                    {
                        // Refresh place data from database
                        var offlinePlace = await _databaseService.GetOfflinePlaceByServerIdAsync(entityId);
                        if (offlinePlace != null)
                        {
                            place.Name = offlinePlace.Name;
                            place.Notes = offlinePlace.Notes;
                            place.Icon = offlinePlace.IconName;
                            place.MarkerColor = offlinePlace.MarkerColor;
                            place.Latitude = offlinePlace.Latitude;
                            place.Longitude = offlinePlace.Longitude;
                        }

                        // Restore selection
                        SelectedTripPlace = place;
                        IsTripSheetOpen = true;

                        // Update UI
                        OnPropertyChanged(nameof(TripSheetTitle));
                        LoadedTrip.NotifySortedRegionsChanged();
                        await RefreshTripOnMapAsync();

                        _logger.LogDebug("ApplyPendingSelectionRestoreAsync: Restored place selection to {Name}", place.Name);
                    }
                    break;

                case "Area":
                    var area = LoadedTrip.AllAreas.FirstOrDefault(a => a.Id == entityId);
                    if (area != null)
                    {
                        var offlinePolygon = await _databaseService.GetOfflinePolygonByServerIdAsync(entityId);
                        if (offlinePolygon != null)
                        {
                            area.Notes = offlinePolygon.Notes;
                        }

                        SelectedTripArea = area;
                        IsTripSheetOpen = true;
                        _logger.LogDebug("ApplyPendingSelectionRestoreAsync: Restored area selection");
                    }
                    break;

                case "Segment":
                    var segment = LoadedTrip.Segments.FirstOrDefault(s => s.Id == entityId);
                    if (segment != null)
                    {
                        var offlineSegment = await _databaseService.GetOfflineSegmentByServerIdAsync(entityId);
                        if (offlineSegment != null)
                        {
                            segment.Notes = offlineSegment.Notes;
                        }

                        SelectedTripSegment = segment;
                        IsTripSheetOpen = true;
                        _logger.LogDebug("ApplyPendingSelectionRestoreAsync: Restored segment selection");
                    }
                    break;

                case "Region":
                    var region = LoadedTrip.Regions.FirstOrDefault(r => r.Id == entityId);
                    if (region != null)
                    {
                        var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(entityId);
                        if (offlineArea != null)
                        {
                            region.Name = offlineArea.Name;
                            region.Notes = offlineArea.Notes;
                        }

                        SelectedTripRegion = region;
                        IsShowingRegionNotes = true;
                        IsTripSheetOpen = true;
                        LoadedTrip.NotifySortedRegionsChanged();
                        _logger.LogDebug("ApplyPendingSelectionRestoreAsync: Restored region selection");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore selection for {Type}/{Id}", entityType, entityId);
        }
    }

    /// <summary>
    /// Refreshes edited entities from the database after returning from sub-editor pages.
    /// Updates the in-memory LoadedTrip with the latest values from the offline database.
    /// </summary>
    private async Task RefreshEditedEntitiesAsync()
    {
        try
        {
            // Refresh selected place if one is selected
            if (SelectedTripPlace != null)
            {
                var offlinePlace = await _databaseService.GetOfflinePlaceByServerIdAsync(SelectedTripPlace.Id);
                if (offlinePlace != null)
                {
                    // Find the place in LoadedTrip and update it
                    foreach (var region in LoadedTrip!.Regions)
                    {
                        var place = region.Places.FirstOrDefault(p => p.Id == SelectedTripPlace.Id);
                        if (place != null)
                        {
                            place.Name = offlinePlace.Name;
                            place.Notes = offlinePlace.Notes;
                            place.Icon = offlinePlace.IconName;
                            place.MarkerColor = offlinePlace.MarkerColor;
                            place.Latitude = offlinePlace.Latitude;
                            place.Longitude = offlinePlace.Longitude;

                            // Reassign SelectedTripPlace to trigger property change notifications
                            SelectedTripPlace = place;

                            // Explicitly notify TripSheetTitle (reference may not have changed)
                            OnPropertyChanged(nameof(TripSheetTitle));

                            // Refresh the sorted regions view (for trip overview list)
                            LoadedTrip.NotifySortedRegionsChanged();

                            // Update the map to reflect marker changes
                            await RefreshTripOnMapAsync();

                            break;
                        }
                    }
                }
            }

            // Refresh selected area if one is selected
            if (SelectedTripArea != null)
            {
                var offlinePolygon = await _databaseService.GetOfflinePolygonByServerIdAsync(SelectedTripArea.Id);
                if (offlinePolygon != null)
                {
                    var area = LoadedTrip!.AllAreas.FirstOrDefault(a => a.Id == SelectedTripArea.Id);
                    if (area != null)
                    {
                        area.Notes = offlinePolygon.Notes;
                        SelectedTripArea = area;
                    }
                }
            }

            // Refresh selected segment if one is selected
            if (SelectedTripSegment != null)
            {
                var offlineSegment = await _databaseService.GetOfflineSegmentByServerIdAsync(SelectedTripSegment.Id);
                if (offlineSegment != null)
                {
                    var segment = LoadedTrip!.Segments.FirstOrDefault(s => s.Id == SelectedTripSegment.Id);
                    if (segment != null)
                    {
                        segment.Notes = offlineSegment.Notes;
                        SelectedTripSegment = segment;
                    }
                }
            }

            // Refresh selected region if viewing region notes
            if (IsShowingRegionNotes && SelectedTripRegion != null)
            {
                var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(SelectedTripRegion.Id);
                if (offlineArea != null)
                {
                    var region = LoadedTrip!.Regions.FirstOrDefault(r => r.Id == SelectedTripRegion.Id);
                    if (region != null)
                    {
                        region.Name = offlineArea.Name;
                        region.Notes = offlineArea.Notes;
                        SelectedTripRegion = region;
                        LoadedTrip.NotifySortedRegionsChanged();
                    }
                }
            }

            // Refresh trip notes if showing trip notes
            if (IsShowingTripNotes)
            {
                var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(LoadedTrip!.Id);
                if (downloadedTrip != null)
                {
                    LoadedTrip.Name = downloadedTrip.Name;
                    LoadedTrip.Notes = downloadedTrip.Notes;
                    OnPropertyChanged(nameof(PageTitle));
                    OnPropertyChanged(nameof(TripSheetTitle));
                    OnPropertyChanged(nameof(TripNotesPreview));
                    OnPropertyChanged(nameof(TripNotesHtml));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh edited entities from database");
        }
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
            _logger.LogWarning(ex, "Error showing cache status");
            await _toastService.ShowErrorAsync("Could not load cache status");
        }
    }

    #endregion
}
