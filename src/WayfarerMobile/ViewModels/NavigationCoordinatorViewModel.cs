using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
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
    private HostedRouteRequestContext? _hostedRequest;
    private HostedRouteTargetOwner? _hostedTargetOwner;

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

        CancelHostedRouting();

        var route = _tripNavigationService.CalculateRouteToPlace(
            currentLocation.Latitude,
            currentLocation.Longitude,
            placeId);

        if (route?.IsDirectRoute == true && Guid.TryParse(placeId, out var destinationId))
        {
            var authority = HostedTripTargetAuthority.Resolve(_tripState.LoadedTrip, destinationId,
                currentLocation.Latitude, currentLocation.Longitude);
            if (authority != null)
            {
                route = await TryHostedAsync(route, currentLocation.Latitude, currentLocation.Longitude,
                    authority.Destination.Latitude, authority.Destination.Longitude, route.DestinationName,
                    authority.ModeKey, authority, HostedRouteTargetOwner.Trip(destinationId));
            }
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

        CancelHostedRouting();

        var route = _tripNavigationService.CalculateRouteToNextPlace(
            currentLocation.Latitude,
            currentLocation.Longitude);
        Guid? destinationPlaceId = null;

        if (route?.IsDirectRoute == true && route.Waypoints.Count > 0)
        {
            var destination = route.Waypoints[^1];
            destinationPlaceId = Guid.TryParse(destination.PlaceId, out var parsedId) ? parsedId : null;
            var authority = destinationPlaceId is { } exactId
                ? HostedTripTargetAuthority.Resolve(_tripState.LoadedTrip, exactId,
                    currentLocation.Latitude, currentLocation.Longitude)
                : null;
            if (authority != null)
            {
                route = await TryHostedAsync(route, currentLocation.Latitude, currentLocation.Longitude,
                    authority.Destination.Latitude, authority.Destination.Longitude, route.DestinationName,
                    authority.ModeKey, authority, HostedRouteTargetOwner.Trip(authority.DestinationPlaceId));
            }
        }
        else if (route?.Waypoints.Count > 0)
        {
            destinationPlaceId = Guid.TryParse(route.Waypoints[^1].PlaceId, out var parsedId) ? parsedId : null;
        }

        if (route != null)
        {
            // Track navigation destination for visit notification conflict detection
            _currentNavigationPlaceId = destinationPlaceId;
            _visitNotificationService.UpdateNavigationState(true, destinationPlaceId);

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
            profile, null, HostedRouteTargetOwner.Fixed(toLat, toLon, "ad-hoc-coordinates"));
    }

    /// <summary>Routes a non-Trip target through the shared hosted coordinator path.</summary>
    public async Task<NavigationRoute> CalculateHostedRouteToCoordinatesAsync(
        double fromLat, double fromLon, double toLat, double toLon, string destinationName,
        string profile, string targetAssociation, Func<HostedRouteCoordinate?> currentTarget)
    {
        var direct = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            fromLat, fromLon, toLat, toLon, destinationName, profile);
        return await TryHostedAsync(direct, fromLat, fromLon, toLat, toLon, destinationName,
            profile, null, HostedRouteTargetOwner.Member(toLat, toLon, targetAssociation, currentTarget));
    }

    private async Task<NavigationRoute> TryHostedAsync(NavigationRoute direct, double fromLat, double fromLon,
        double toLat, double toLon, string destinationName, string profile,
        HostedTripTargetAuthority? tripAuthority, HostedRouteTargetOwner targetOwner)
    {
        var generation = Interlocked.Increment(ref _hostedRoutingGeneration);
        CancelHostedRouting(incrementGeneration: false);
        if (profile == "direct")
        {
            _hostedRouting.SelectDirect(generation);
            return direct;
        }
        _hostedRoutingCancellation = new CancellationTokenSource();
        var context = CreateHostedContext(fromLat, fromLon, toLat, toLon, destinationName, profile,
            generation, tripAuthority, targetOwner.Association);
        _hostedRequest = context;
        _hostedTargetOwner = targetOwner;
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
            if (_hostedRoutingGeneration != generation || _hostedRequest?.Generation != generation) return direct;
            result = await _hostedRouting.RequestRouteAsync(context, result.Choices[index], _hostedRoutingCancellation.Token);
        }
        if (result.Outcome != HostedRoutingOutcome.Success || result.Candidate == null) return direct;
        if (_hostedRoutingGeneration != generation || _hostedRequest?.Generation != generation) return direct;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var live = CreateLiveAuthority();
            if (live != null) HostedRoutePublication.TryPublish(result.Candidate, live, direct);
        });
        return direct;
    }

    private HostedRouteRequestContext CreateHostedContext(double fromLat, double fromLon, double toLat,
        double toLon, string destinationName, string profile, long generation,
        HostedTripTargetAuthority? tripAuthority, string targetAssociation)
    {
        var mode = NormalizeMode(tripAuthority?.ModeKey ?? profile);
        var category = NormalizeMode(tripAuthority?.Category ?? mode);
        var server = NormalizeServer(_settings.ServerUrl);
        return new(tripAuthority?.SavedTransportProfileId, mode, category,
            new(fromLon, fromLat), new(toLon, toLat), tripAuthority?.Anchors ?? [], destinationName,
            generation, _settings.AuthenticationSessionRevision, server, targetAssociation, "hosted",
            tripAuthority?.SegmentId);
    }

    private HostedRouteLiveAuthority? CreateLiveAuthority()
    {
        var request = _hostedRequest;
        var owner = _hostedTargetOwner;
        var location = _callbacks?.CurrentLocation;
        var selection = _hostedRouting.CurrentSelection;
        if (request == null || owner == null || location == null
            || selection?.Generation != _hostedRoutingGeneration) return null;

        HostedTripTargetAuthority? tripAuthority = null;
        HostedRouteCoordinate? destination;
        if (owner.TripPlaceId is { } tripPlaceId)
        {
            tripAuthority = HostedTripTargetAuthority.Resolve(_tripState.LoadedTrip, tripPlaceId,
                location.Latitude, location.Longitude);
            destination = tripAuthority?.Destination;
        }
        else
        {
            destination = owner.ResolveDestination();
        }
        if (destination == null) return null;

        var mode = NormalizeMode(tripAuthority?.ModeKey ?? request.ModeKey ?? string.Empty);
        var category = NormalizeMode(tripAuthority?.Category ?? request.Category ?? string.Empty);
        return new(_hostedRoutingGeneration, _settings.AuthenticationSessionRevision,
            NormalizeServer(_settings.ServerUrl), new(location.Longitude, location.Latitude), destination,
            tripAuthority?.Anchors ?? [], owner.Association, tripAuthority?.SegmentId,
            tripAuthority?.SavedTransportProfileId, mode, category, selection.TransportProfileId,
            selection.SelectedProfileAuthorityIdentity, "hosted");
    }

    private static string NormalizeMode(string profile) => profile switch
    {
        "foot" or "walking" => "walk",
        "car" or "driving" => "drive",
        "bike" or "cycling" => "bicycle",
        _ => profile
    };

    private static string NormalizeServer(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
        var authority = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return $"{authority}{uri.AbsolutePath}".TrimEnd('/');
    }

    private void CancelHostedRouting(bool incrementGeneration = true)
    {
        if (incrementGeneration) _hostedRouting.SelectDirect(Interlocked.Increment(ref _hostedRoutingGeneration));
        _hostedRequest = null;
        _hostedTargetOwner = null;
        _hostedRoutingCancellation?.Cancel();
        _hostedRoutingCancellation?.Dispose();
        _hostedRoutingCancellation = null;
    }

    private sealed record HostedRouteTargetOwner(string Association, Guid? TripPlaceId,
        HostedRouteCoordinate InitialDestination, Func<HostedRouteCoordinate?>? CurrentDestination)
    {
        public static HostedRouteTargetOwner Fixed(double latitude, double longitude, string association) =>
            new(association, null, new(longitude, latitude), null);

        public static HostedRouteTargetOwner Member(double latitude, double longitude, string association,
            Func<HostedRouteCoordinate?> currentDestination) =>
            new(association, null, new(longitude, latitude), currentDestination);

        public static HostedRouteTargetOwner Trip(Guid placeId) =>
            new($"trip-place:{placeId:D}", placeId, new(0, 0), null);

        public HostedRouteCoordinate? ResolveDestination() =>
            CurrentDestination == null ? InitialDestination : CurrentDestination();
    }

    /// <summary>
    /// Starts navigation with a pre-calculated route (for non-trip navigation).
    /// </summary>
    public async Task StartNavigationWithRouteAsync(NavigationRoute route)
    {
        CancelHostedRouting();
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
