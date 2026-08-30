using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

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
    private readonly HostedRoutingService _hostedRouting;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ITripStateManager _tripState;
    private CancellationTokenSource? _hostedRoutingCancellation;
    private long _hostedRoutingGeneration;

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
        HostedRoutingService hostedRouting,
        ISettingsService settings,
        IDialogService dialogs,
        ITripStateManager tripState,
        ILogger<NavigationCoordinatorViewModel> logger)
    {
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        _visitNotificationService = visitNotificationService;
        _hostedRouting = hostedRouting;
        _settings = settings;
        _dialogs = dialogs;
        _tripState = tripState;
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

        if (route?.IsDirectRoute == true && Guid.TryParse(placeId, out var destinationId))
        {
            var segment = FindCurrentSegment(destinationId, currentLocation.Latitude, currentLocation.Longitude);
            var anchors = ResolveAnchors(segment);
            route = await TryHostedAsync(route, currentLocation.Latitude, currentLocation.Longitude,
                route.Waypoints[^1].Latitude, route.Waypoints[^1].Longitude, route.DestinationName,
                segment?.TransportMode ?? "walk", HostedSegmentProfileIdentity.Get(segment), anchors,
                $"trip-place:{placeId}");
        }

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
        CancelHostedRouting();
        _tripNavigationService.StopNavigation();

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
        var direct = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            fromLat, fromLon,
            toLat, toLon,
            destinationName,
            profile);
        return await TryHostedAsync(direct, fromLat, fromLon, toLat, toLon, destinationName,
            profile, null, [], "ad-hoc-coordinates");
    }

    private async Task<NavigationRoute> TryHostedAsync(NavigationRoute direct, double fromLat, double fromLon,
        double toLat, double toLon, string destinationName, string profile, Guid? savedProfileId,
        IReadOnlyList<HostedRouteCoordinate> anchors, string targetAssociation)
    {
        var generation = Interlocked.Increment(ref _hostedRoutingGeneration);
        CancelHostedRouting(incrementGeneration: false);
        if (profile == "direct") { _hostedRouting.SelectDirect(generation); return direct; }
        _hostedRoutingCancellation = new CancellationTokenSource();
        var context = CreateHostedContext(fromLat, fromLon, toLat, toLon, destinationName, profile,
            generation, savedProfileId, anchors, targetAssociation);
        var result = await _hostedRouting.RequestRouteAsync(context, cancellationToken: _hostedRoutingCancellation.Token);
        if (result.Outcome == HostedRoutingOutcome.RequiresChoice && result.Choices is { Count: > 0 })
        {
            var options = result.Choices.Select(item =>
                $"{item.DisplayName} — {item.ModeKey} ({item.TransportProfileId:D})").ToArray();
            var selected = await _dialogs.SelectAsync("Wayfarer routing profile", options, "Direct");
            var index = selected == null ? -1 : Array.IndexOf(options, selected);
            if (index < 0)
            {
                _hostedRouting.SelectDirect(Interlocked.Increment(ref _hostedRoutingGeneration));
                return direct;
            }
            result = await _hostedRouting.RequestRouteAsync(context, result.Choices[index], _hostedRoutingCancellation.Token);
        }
        if (result.Outcome != HostedRoutingOutcome.Success || result.Route == null) return direct;
        CopyRoute(result.Route, direct);
        return direct;
    }

    private HostedRouteRequestContext CreateHostedContext(double fromLat, double fromLon, double toLat,
        double toLon, string destinationName, string profile, long generation, Guid? savedProfileId,
        IReadOnlyList<HostedRouteCoordinate> anchors, string targetAssociation)
    {
        var mode = profile switch { "foot" => "walk", "car" => "drive", "bike" => "bicycle", _ => profile };
        var server = Uri.TryCreate(_settings.ServerUrl, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority).TrimEnd('/').ToLowerInvariant() : string.Empty;
        var token = _settings.ApiToken ?? string.Empty;
        var sessionAuthority = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return new(savedProfileId, mode, mode, new(fromLon, fromLat), new(toLon, toLat), anchors, destinationName,
            generation, sessionAuthority, server, targetAssociation, "hosted");
    }

    private TripSegment? FindCurrentSegment(Guid destinationId, double latitude, double longitude) =>
        _tripState.LoadedTrip?.Segments.Where(item => item.DestinationId == destinationId)
            .Select(item => (Segment: item, Origin: _tripState.LoadedTrip.AllPlaces
                .SingleOrDefault(place => place.Id == item.OriginId)))
            .Where(item => item.Origin != null)
            .OrderBy(item => Core.Algorithms.GeoMath.CalculateDistance(latitude, longitude,
                item.Origin!.Latitude, item.Origin.Longitude))
            .Select(item => item.Segment).FirstOrDefault();

    private IReadOnlyList<HostedRouteCoordinate> ResolveAnchors(TripSegment? segment)
    {
        if (segment?.Waypoints.Count is not (> 0 and <= 3) || _tripState.LoadedTrip == null) return [];
        var places = _tripState.LoadedTrip.AllPlaces.ToDictionary(item => item.Id);
        var result = new List<HostedRouteCoordinate>(segment.Waypoints.Count);
        foreach (var waypoint in segment.Waypoints.OrderBy(item => item.Position))
        {
            if (!places.TryGetValue(waypoint.PlaceId, out var place)) return [];
            result.Add(new(place.Longitude, place.Latitude));
        }
        return result;
    }

    private static void CopyRoute(NavigationRoute source, NavigationRoute target)
    {
        target.Waypoints = source.Waypoints;
        target.Steps = source.Steps;
        target.DestinationName = source.DestinationName;
        target.TotalDistanceMeters = source.TotalDistanceMeters;
        target.EstimatedDuration = source.EstimatedDuration;
        target.IsDirectRoute = false;
        target.InitialBearing = 0;
        target.Attribution = source.Attribution;
    }

    private void CancelHostedRouting(bool incrementGeneration = true)
    {
        if (incrementGeneration) _hostedRouting.SelectDirect(Interlocked.Increment(ref _hostedRoutingGeneration));
        _hostedRoutingCancellation?.Cancel();
        _hostedRoutingCancellation?.Dispose();
        _hostedRoutingCancellation = null;
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
        CancelHostedRouting();
        _navigationHudViewModel.StopNavigationRequested -= OnStopNavigationRequested;
        _navigationHudViewModel.Dispose();
        base.Cleanup();
    }

    #endregion
}
