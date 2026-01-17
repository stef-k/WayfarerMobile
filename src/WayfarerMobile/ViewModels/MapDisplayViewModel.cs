using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using Map = Mapsui.Map;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for map display and layer management.
/// Extracted from MainViewModel to handle map-specific concerns.
/// </summary>
public partial class MapDisplayViewModel : BaseViewModel
{
    #region Fields

    private readonly IMapBuilder _mapBuilder;
    private readonly ILocationBridge _locationBridge;
    private readonly ILocationLayerService _locationLayerService;
    private readonly ITripLayerService _tripLayerService;
    private readonly IDroppedPinLayerService _droppedPinLayerService;
    private readonly ICacheVisualizationService _cacheService;
    private readonly LocationIndicatorService _indicatorService;
    private readonly IToastService _toastService;
    private readonly ILogger<MapDisplayViewModel> _logger;

    // Callbacks to parent ViewModel
    private IMapDisplayCallbacks? _callbacks;

    // Map instance (lazy-initialized)
    private Map? _map;

    // Layer references
    private WritableLayer? _locationLayer;
    private WritableLayer? _navigationRouteLayer;
    private WritableLayer? _navigationRouteCompletedLayer;
    private WritableLayer? _droppedPinLayer;
    private WritableLayer? _tripPlacesLayer;
    private WritableLayer? _tripAreasLayer;
    private WritableLayer? _tripSegmentsLayer;
    private WritableLayer? _placeSelectionLayer;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets or sets whether to follow location on map.
    /// </summary>
    [ObservableProperty]
    private bool _isFollowingLocation = true;

    /// <summary>
    /// Gets or sets the cache health status.
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
    /// Checks if navigation route is currently displayed.
    /// </summary>
    public bool HasNavigationRoute =>
        _navigationRouteLayer?.GetFeatures().Any() == true;

    /// <summary>
    /// Gets the current smoothed heading in degrees (0-360), or -1 if unavailable.
    /// </summary>
    public double CurrentHeading => _indicatorService?.CurrentHeading ?? -1;

    /// <summary>
    /// Gets whether a valid heading is available.
    /// </summary>
    public bool HasValidHeading => _indicatorService?.HasValidHeading ?? false;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MapDisplayViewModel.
    /// </summary>
    public MapDisplayViewModel(
        IMapBuilder mapBuilder,
        ILocationBridge locationBridge,
        ILocationLayerService locationLayerService,
        ITripLayerService tripLayerService,
        IDroppedPinLayerService droppedPinLayerService,
        ICacheVisualizationService cacheService,
        LocationIndicatorService indicatorService,
        IToastService toastService,
        ILogger<MapDisplayViewModel> logger)
    {
        _mapBuilder = mapBuilder;
        _locationBridge = locationBridge;
        _locationLayerService = locationLayerService;
        _tripLayerService = tripLayerService;
        _droppedPinLayerService = droppedPinLayerService;
        _cacheService = cacheService;
        _indicatorService = indicatorService;
        _toastService = toastService;
        _logger = logger;

        // Subscribe to cache status updates
        _cacheService.StatusChanged += OnCacheStatusChanged;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Sets the callback interface to the parent ViewModel.
    /// Must be called before using methods that depend on parent state.
    /// </summary>
    public void SetCallbacks(IMapDisplayCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Creates and configures the map instance.
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
    /// </summary>
    private void SetInitialMapPosition(Map map)
    {
        var lastLocation = _locationBridge.LastLocation;

        if (lastLocation != null)
        {
            _mapBuilder.CenterOnLocation(map, lastLocation.Latitude, lastLocation.Longitude, zoomLevel: 15);
            _logger.LogDebug("Map initialized at last known location: {Lat}, {Lon}",
                lastLocation.Latitude, lastLocation.Longitude);
        }
        else
        {
            // No location available - show globe view (zoom 2)
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
    public void EnsureMapInitialized()
    {
        _ = Map; // Force lazy initialization
    }

    #endregion

    #region Commands

    /// <summary>
    /// Centers the map on current location.
    /// </summary>
    [RelayCommand]
    private async Task CenterOnLocationAsync()
    {
        var location = _callbacks?.CurrentLocation ?? _locationBridge.LastLocation;

        if (location != null && _map != null)
        {
            _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude, zoomLevel: 16);
            IsFollowingLocation = true;
        }
        else
        {
            await _toastService.ShowWarningAsync("No location available");
        }
    }

    /// <summary>
    /// Zooms the map to show all relevant features (location, route, places).
    /// </summary>
    [RelayCommand]
    private void ZoomToTrack()
    {
        // Zoom to navigation route if active, otherwise just center on location
        var isNavigating = _callbacks?.IsNavigating ?? false;
        var navService = _callbacks?.TripNavigationService;

        if (isNavigating && navService?.ActiveRoute != null)
        {
            ZoomToNavigationRoute();
        }
        else if (_callbacks?.CurrentLocation != null && _map != null)
        {
            var location = _callbacks.CurrentLocation;
            _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude, 15);
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

    /// <summary>
    /// Shows cache status details in a dialog with option to show/hide overlay on map.
    /// </summary>
    [RelayCommand]
    private async Task ShowCacheStatusAsync()
    {
        try
        {
            var info = await _cacheService.GetDetailedCacheInfoAsync();
            var message = _cacheService.FormatStatusMessage(info);

            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page == null) return;

            // Button text depends on whether overlay is currently visible
            var buttonText = _cacheService.IsOverlayVisible ? "Hide Overlay" : "Show on Map";

            var toggleOverlay = await page.DisplayAlertAsync(
                "Cache Status",
                message,
                buttonText,
                "Close");

            var location = _callbacks?.CurrentLocation;
            if (toggleOverlay && location != null)
            {
                await _cacheService.ToggleOverlayAsync(
                    Map, location.Latitude, location.Longitude);
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

    #region Public Methods for MainViewModel

    /// <summary>
    /// Updates the location indicator on the map.
    /// </summary>
    public void UpdateLocationIndicator(LocationData location)
    {
        if (_locationLayer != null)
        {
            _locationLayerService.UpdateLocation(_locationLayer, location);
        }
    }

    /// <summary>
    /// Clears the location indicator from the map.
    /// </summary>
    public void ClearLocationIndicator()
    {
        if (_locationLayer != null)
        {
            _locationLayerService.ClearLocation(_locationLayer);
        }
    }

    /// <summary>
    /// Centers the map on a specific location.
    /// </summary>
    public void CenterOnLocation(double latitude, double longitude, int? zoomLevel = null)
    {
        if (_map != null)
        {
            _mapBuilder.CenterOnLocation(_map, latitude, longitude, zoomLevel);
        }
    }

    /// <summary>
    /// Shows the navigation route on the map.
    /// </summary>
    public void ShowNavigationRoute(NavigationRoute route)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null || _map == null)
            return;

        _mapBuilder.UpdateNavigationRoute(_navigationRouteLayer, _navigationRouteCompletedLayer, route);
    }

    /// <summary>
    /// Updates navigation route progress display.
    /// </summary>
    public void UpdateNavigationRouteProgress(NavigationRoute route, double currentLat, double currentLon)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null)
            return;

        _mapBuilder.UpdateNavigationRouteProgress(
            _navigationRouteLayer,
            _navigationRouteCompletedLayer,
            route,
            currentLat,
            currentLon);
    }

    /// <summary>
    /// Clears the navigation route from the map.
    /// </summary>
    public void ClearNavigationRoute()
    {
        _navigationRouteLayer?.Clear();
        _navigationRouteLayer?.DataHasChanged();
        _navigationRouteCompletedLayer?.Clear();
        _navigationRouteCompletedLayer?.DataHasChanged();
    }

    /// <summary>
    /// Zooms the map to fit the current navigation route.
    /// </summary>
    public void ZoomToNavigationRoute()
    {
        var route = _callbacks?.TripNavigationService?.ActiveRoute;
        if (route?.Waypoints == null || route.Waypoints.Count < 2 || _map == null)
            return;

        var points = route.Waypoints
            .Select(w => SphericalMercator.FromLonLat(w.Longitude, w.Latitude))
            .Select(p => new MPoint(p.x, p.y))
            .ToList();

        _mapBuilder.ZoomToPoints(_map, points);
    }

    /// <summary>
    /// Shows trip layers on the map.
    /// </summary>
    public async Task<List<MPoint>> ShowTripLayersAsync(TripDetails trip)
    {
        var placePoints = new List<MPoint>();

        if (_tripPlacesLayer != null)
            placePoints = await _tripLayerService.UpdateTripPlacesAsync(_tripPlacesLayer, trip.AllPlaces);

        if (_tripAreasLayer != null)
            _tripLayerService.UpdateTripAreas(_tripAreasLayer, trip.AllAreas);

        if (_tripSegmentsLayer != null)
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer, trip.Segments);

        return placePoints;
    }

    /// <summary>
    /// Clears trip layers from the map.
    /// </summary>
    public void ClearTripLayers()
    {
        _tripPlacesLayer?.Clear();
        _tripPlacesLayer?.DataHasChanged();
        _tripAreasLayer?.Clear();
        _tripAreasLayer?.DataHasChanged();
        _tripSegmentsLayer?.Clear();
        _tripSegmentsLayer?.DataHasChanged();
        ClearPlaceSelection();
    }

    /// <summary>
    /// Updates the place selection ring on the map.
    /// </summary>
    public void UpdatePlaceSelection(TripPlace? place)
    {
        if (_placeSelectionLayer == null) return;

        _tripLayerService.UpdatePlaceSelection(_placeSelectionLayer, place);
    }

    /// <summary>
    /// Clears the place selection ring from the map.
    /// </summary>
    public void ClearPlaceSelection()
    {
        _placeSelectionLayer?.Clear();
        _placeSelectionLayer?.DataHasChanged();
    }

    /// <summary>
    /// Zooms the map to fit the specified points.
    /// </summary>
    public void ZoomToPoints(List<MPoint> points)
    {
        if (_map != null && points.Count > 0)
        {
            _mapBuilder.ZoomToPoints(_map, points);
        }
    }

    /// <summary>
    /// Refreshes the trip layers on the map.
    /// Called after editing a trip to update the display.
    /// </summary>
    public async Task RefreshTripLayersAsync(TripDetails trip)
    {
        if (_tripPlacesLayer != null)
            await _tripLayerService.UpdateTripPlacesAsync(_tripPlacesLayer, trip.AllPlaces);

        if (_tripAreasLayer != null)
            _tripLayerService.UpdateTripAreas(_tripAreasLayer, trip.AllAreas);

        if (_tripSegmentsLayer != null)
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer, trip.Segments);
    }

    /// <summary>
    /// Shows a dropped pin on the map.
    /// </summary>
    public void ShowDroppedPin(double latitude, double longitude)
    {
        if (_droppedPinLayer != null)
        {
            _droppedPinLayerService.ShowDroppedPin(_droppedPinLayer, latitude, longitude);
        }
    }

    /// <summary>
    /// Clears the dropped pin from the map.
    /// </summary>
    public void ClearDroppedPin()
    {
        if (_droppedPinLayer != null)
        {
            _droppedPinLayerService.ClearDroppedPin(_droppedPinLayer);
        }
    }

    /// <summary>
    /// Refreshes the map display.
    /// </summary>
    public void RefreshMap()
    {
        _map?.Refresh();
    }

    /// <summary>
    /// Gets the current viewport bounds of the map.
    /// </summary>
    public (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? GetViewportBounds()
    {
        return _map != null ? _mapBuilder.GetViewportBounds(_map) : null;
    }

    /// <summary>
    /// Stops the location animation.
    /// </summary>
    public void StopLocationAnimation()
    {
        _locationLayerService.StopAnimation();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles cache status changes from CacheVisualizationService.
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

    #endregion

    #region Cleanup

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _cacheService.StatusChanged -= OnCacheStatusChanged;
        _locationLayerService.StopAnimation();
        base.Cleanup();
    }

    #endregion
}
