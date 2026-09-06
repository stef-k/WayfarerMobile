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
    private readonly RetainedWayfarerRoutingService _retainedRouting;
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
        RetainedWayfarerRoutingService retainedRouting,
        ISettingsService settings,
        IDialogService dialogs,
        ITripStateManager tripState,
        ILogger<NavigationCoordinatorViewModel> logger)
    {
        _tripNavigationService = tripNavigationService;
        _navigationHudViewModel = navigationHudViewModel;
        _visitNotificationService = visitNotificationService;
        _hostedRouting = hostedRouting;
        _retainedRouting = retainedRouting;
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
    public async Task<bool> StartNavigationToPlaceAsync(string placeId)
    {
        var generation = BeginStartup();
        var trip = _tripState.LoadedTrip;
        var destination = Guid.TryParse(placeId, out var destinationId)
            ? trip?.AllPlaces.FirstOrDefault(place => place.Id == destinationId) : null;
        if (!_tripNavigationService.IsTripLoaded || destination == null
            || !UsableCoordinate(destination.Latitude, destination.Longitude))
            return await StartupFeedbackAsync("Reload the Trip and select a valid Place, then try Directions again.");
        var selectedId = _callbacks?.SelectedTripPlace?.Id;
        var target = new HostedRouteCoordinate(destination.Longitude, destination.Latitude);
        var current = CaptureStartup(generation, target, () =>
        {
            if (!ReferenceEquals(_tripState.LoadedTrip, trip)
                || _callbacks?.SelectedTripPlace?.Id != selectedId) return null;
            var place = trip!.AllPlaces.FirstOrDefault(item => item.Id == destination.Id);
            return place == null ? null : new(place.Longitude, place.Latitude);
        });
        try
        {
            var location = _callbacks?.CurrentLocation;
            if (!UsableLocation(location)) return await MissingLocationAsync();
            var route = _tripNavigationService.CalculateRouteToPlace(
                location!.Latitude, location.Longitude, placeId, activate: false);
            if (route == null) return await StartupFeedbackAsync("This Place is unavailable. Reload the Trip and try Directions again.");
            if (route.IsDirectRoute)
            {
                var authority = HostedTripTargetAuthority.Resolve(trip, destination.Id,
                    location.Latitude, location.Longitude);
                route = await TryHostedAsync(route, location.Latitude, location.Longitude,
                    destination.Latitude, destination.Longitude, destination.Name,
                    authority, HostedRouteTargetOwner.Trip(destination.Id), generation, current, hostedChosen: false);
            }
            if (route == null) return false;
            if (!current()) return await StaleStartupAsync();
            return await CommitStartupAsync(route, destination.Id);
        }
        catch (Exception exception)
        {
            return await StartupFailedAsync(exception);
        }
    }

    /// <summary>
    /// Starts navigation to the next place in the trip sequence.
    /// </summary>
    [RelayCommand]
    public async Task StartNavigationToNextAsync()
    {
        var location = _callbacks?.CurrentLocation;
        if (!UsableLocation(location)) { await MissingLocationAsync(); return; }
        var route = _tripNavigationService.CalculateRouteToNextPlace(
            location!.Latitude, location.Longitude, activate: false);
        var placeId = route?.Waypoints.LastOrDefault()?.PlaceId;
        if (placeId == null)
        {
            await StartupFeedbackAsync("No next Place is available. Select a Place and try Directions again.");
            return;
        }
        await StartNavigationToPlaceAsync(placeId);
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
    public async Task<NavigationRoute?> CalculateRouteToCoordinatesAsync(
        double fromLat, double fromLon,
        double toLat, double toLon,
        string destinationName,
        bool direct = false)
    {
        var directRoute = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            fromLat, fromLon,
            toLat, toLon,
            destinationName,
            activate: false);
        var route = direct
            ? SelectDirectRoute(directRoute)
            : await TryHostedAsync(directRoute, fromLat, fromLon, toLat, toLon, destinationName,
                null, HostedRouteTargetOwner.Fixed(toLat, toLon, "ad-hoc-coordinates"));
        if (route != null) _tripNavigationService.ActivateRoute(route);
        return route;
    }

    /// <summary>Routes a non-Trip target through the shared hosted coordinator path.</summary>
    public async Task<NavigationRoute?> CalculateHostedRouteToCoordinatesAsync(
        double fromLat, double fromLon, double toLat, double toLon, string destinationName,
        bool direct, string targetAssociation, Func<HostedRouteCoordinate?> currentTarget)
    {
        var directRoute = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            fromLat, fromLon, toLat, toLon, destinationName, activate: false);
        var route = direct
            ? SelectDirectRoute(directRoute)
            : await TryHostedAsync(directRoute, fromLat, fromLon, toLat, toLon, destinationName,
                null, HostedRouteTargetOwner.Member(toLat, toLon, targetAssociation, currentTarget));
        if (route != null) _tripNavigationService.ActivateRoute(route);
        return route;
    }

    private NavigationRoute SelectDirectRoute(NavigationRoute route)
    {
        _hostedRouting.SelectDirect(Interlocked.Increment(ref _hostedRoutingGeneration));
        CancelHostedRouting(incrementGeneration: false);
        return route;
    }

    private async Task<NavigationRoute?> TryHostedAsync(NavigationRoute direct, double fromLat, double fromLon,
        double toLat, double toLon, string destinationName,
        HostedTripTargetAuthority? tripAuthority, HostedRouteTargetOwner targetOwner,
        long? startupGeneration = null, Func<bool>? startupCurrent = null, bool hostedChosen = true)
    {
        var generation = startupGeneration ?? Interlocked.Increment(ref _hostedRoutingGeneration);
        CancelHostedRouting(incrementGeneration: false);
        _hostedRoutingCancellation = new CancellationTokenSource();
        var context = CreateHostedContext(fromLat, fromLon, toLat, toLon, destinationName,
            generation, tripAuthority, targetOwner.Association);
        _hostedRequest = context;
        _hostedTargetOwner = targetOwner;
        var partition = _settings.RoutingAccountPartition;
        var cancellation = _hostedRoutingCancellation;
        var retainedDecision = await ResolveRetainedChoiceAsync(direct, context, partition);
        if (startupCurrent?.Invoke() == false) { await StaleStartupAsync(); return null; }
        if (retainedDecision.Dismissed) return null;
        if (retainedDecision.RouteComplete)
            return direct.IsDirectRoute ? await CurrentDirectAsync(direct, context, partition) : direct;
        // This local choice must precede discovery. Retained/saved geometry keeps its priority.
        if (retainedDecision.RefreshFallback == null && !hostedChosen)
        {
            var choice = await _dialogs.SelectAsync("Navigate by", ["Wayfarer route", "Direct"], "Cancel");
            if (choice == null || choice == "Cancel")
            {
                ReleaseDismissedInvocation(context, cancellation);
                return null;
            }
            if (startupCurrent?.Invoke() == false || !IsIntentCurrent(context, partition))
            { await StaleStartupAsync(); return null; }
            if (choice == "Direct") return await CurrentDirectAsync(direct, context, partition);
            if (choice != "Wayfarer route") return null;
            if (!IsRequestCurrent(context, partition)) { await StaleStartupAsync(); return null; }
        }
        var retainedFallback = retainedDecision.RefreshFallback;
        var expectedSelection = _hostedRouting.CurrentSelection;
        var result = await RequestFreshRouteAsync(context, generation, partition, retainedFallback, cancellation);
        if (result == null) return null;
        if (startupCurrent?.Invoke() == false) { await StaleStartupAsync(); return null; }
        if (result.Outcome != HostedRoutingOutcome.Success || result.Candidate == null)
        {
            if (result.Outcome == HostedRoutingOutcome.Cancelled)
                return await CurrentDirectAsync(direct, context, partition);
            if (!IsInvocationCurrent(context, partition, cancellation))
            { await StaleStartupAsync(); return null; }
            await StartupFeedbackAsync("Wayfarer routing is unavailable. Try Directions again or choose Direct.");
            if (!IsInvocationCurrent(context, partition, cancellation)) return null;
            if (retainedFallback != null)
                RestoreRetainedSelection(context, partition, retainedFallback);
            else return null;
            return direct;
        }
        if (!IsInvocationCurrent(context, partition, cancellation)) { await StaleStartupAsync(); return null; }
        var published = false;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var candidateSelection = new HostedRouteSelection(result.Candidate.Context.Generation,
                result.Candidate.SelectedProfileId, result.Candidate.SelectedProviderMode,
                result.Candidate.SelectedProfileAuthorityIdentity);
            var live = CreateLiveAuthority(selectionOverride: candidateSelection);
            if (live != null && HostedRoutePublication.Current(result.Candidate, live)
                && _hostedRouting.TrySelectCandidate(result.Candidate, expectedSelection))
                published = HostedRoutePublication.TryPublish(result.Candidate, live, direct);
        });
        if (!published) { await StaleStartupAsync(); return null; }
        await _retainedRouting.SaveAsync(result.Candidate, partition, DateTimeOffset.UtcNow,
            () => IsCandidateCurrent(result.Candidate, partition), cancellation.Token);
        if (startupCurrent?.Invoke() == false || !IsCandidateCurrent(result.Candidate, partition))
        { await StaleStartupAsync(); return null; }
        return direct;
    }

    private async Task<HostedRoutingResult?> RequestFreshRouteAsync(HostedRouteRequestContext context,
        long generation, Guid partition, HostedRouteProvenance? retainedFallback,
        CancellationTokenSource cancellation)
    {
        var catalogRediscoveryAvailable = true;
        var result = await _hostedRouting.RequestRouteAsync(context,
            cancellationToken: cancellation.Token);
        if (result.Outcome == HostedRoutingOutcome.CatalogChanged) catalogRediscoveryAvailable = false;
        var maximumPresentations = result.Outcome switch
        {
            HostedRoutingOutcome.RequiresChoice => 2,
            HostedRoutingOutcome.CatalogChanged => 1,
            _ => 0
        };
        for (var presentation = 0; presentation < maximumPresentations
            && result.Outcome is HostedRoutingOutcome.RequiresChoice or HostedRoutingOutcome.CatalogChanged;
            presentation++)
        {
            if (_hostedRoutingGeneration != generation || _hostedRequest?.Generation != generation
                || result.Choices is not { Count: > 0 }
                || !HostedOpaqueIdentity.IsValid(result.DiscoveryCatalogIdentity))
                return new(HostedRoutingOutcome.Unavailable);
            var options = result.Choices.Select(item =>
                item.Label).Append("Direct").ToArray();
            var selected = await _dialogs.SelectAsync(
                "Provider route mode (separate from the Segment Transport Profile)", options, "Cancel");
            if (selected == null || selected == "Cancel")
            {
                ReleaseDismissedInvocation(context, cancellation);
                return null;
            }
            if (selected == "Direct")
            {
                if (IsIntentCurrent(context, partition))
                    _hostedRouting.SelectDirect(generation);
                return new(HostedRoutingOutcome.Cancelled);
            }
            var index = Array.IndexOf(options, selected);
            if (index < 0) return new(HostedRoutingOutcome.Unavailable);
            if (_hostedRoutingGeneration != generation || _hostedRequest?.Generation != generation
                || !IsRequestCurrent(context, partition)) return new(HostedRoutingOutcome.Unavailable);
            var choiceContext = context with
            {
                ExpectedCatalogIdentity = result.DiscoveryCatalogIdentity,
                ExpectedProvider = result.Provider
            };
            _hostedRequest = choiceContext;
            result = await _hostedRouting.RequestRouteAsync(
                choiceContext, result.Choices[index], cancellation.Token,
                catalogRediscoveryAvailable);
            if (result.Outcome == HostedRoutingOutcome.CatalogChanged) catalogRediscoveryAvailable = false;
        }
        if (result.Outcome != HostedRoutingOutcome.CatalogChanged) return result;
        if (retainedFallback != null)
            RestoreRetainedSelection(context, partition, retainedFallback);
        return new(HostedRoutingOutcome.Unavailable);
    }

    private async Task<RetainedRouteDecision> ResolveRetainedChoiceAsync(NavigationRoute target,
        HostedRouteRequestContext context, Guid partition)
    {
        var retained = await TrySelectRetainedAsync(context, partition);
        if (retained?.HostedProvenance is not { } provenance)
            return new(false, false, null);
        var choice = await _dialogs.SelectAsync("Wayfarer retained route",
            ["Use retained route", "Refresh with Wayfarer", "Direct"], "Cancel");
        if (choice == null || choice == "Cancel")
        {
            ReleaseDismissedInvocation(context, _hostedRoutingCancellation!);
            return new(false, true, null);
        }
        if (choice == "Direct" && IsIntentCurrent(context, partition))
        {
            _hostedRouting.SelectDirect(context.Generation);
            return new(true, false, null);
        }
        if (!IsRequestCurrent(context, partition))
        { await StaleStartupAsync(); return new(false, true, null); }
        if (choice == "Use retained route")
        {
            if (HostedRoutePublication.TryPublishRetained(retained, target))
                _hostedRouting.SelectRetained(context.Generation, provenance.TransportProfileId,
                    provenance.ProviderMode ?? string.Empty, provenance.SelectedProfileAuthorityIdentity);
            return new(true, false, null);
        }
        if (choice != "Refresh with Wayfarer") return new(false, true, null);
        if (!HostedRoutePublication.TryPublishRetained(retained, target)) return new(true, false, null);
        _hostedRouting.SelectRetained(context.Generation, provenance.TransportProfileId,
            provenance.ProviderMode ?? string.Empty, provenance.SelectedProfileAuthorityIdentity);
        return new(false, false, provenance);
    }

    private async Task<NavigationRoute?> TrySelectRetainedAsync(HostedRouteRequestContext context,
        Guid partition)
    {
        var retained = await _retainedRouting.TrySelectOfflineAsync(context, partition,
            DateTimeOffset.UtcNow, () => IsRequestCurrent(context, partition),
            _hostedRoutingCancellation!.Token);
        return retained?.HostedProvenance is not null && IsRequestCurrent(context, partition)
            ? retained : null;
    }

    private void RestoreRetainedSelection(HostedRouteRequestContext context, Guid partition,
        HostedRouteProvenance retained)
    {
        if (IsRequestCurrent(context, partition))
            _hostedRouting.SelectRetained(context.Generation, retained.TransportProfileId,
                retained.ProviderMode ?? string.Empty, retained.SelectedProfileAuthorityIdentity);
    }

    private HostedRouteRequestContext CreateHostedContext(double fromLat, double fromLon, double toLat,
        double toLon, string destinationName, long generation,
        HostedTripTargetAuthority? tripAuthority, string targetAssociation)
    {
        var server = HostedRouteServerIdentity.Normalize(_settings.ServerUrl);
        return new(tripAuthority?.SavedTransportProfileId,
            new(fromLon, fromLat), new(toLon, toLat), tripAuthority?.Anchors ?? [], destinationName,
            generation, _settings.AuthenticationSessionRevision, server, targetAssociation, "hosted",
            tripAuthority?.SegmentId);
    }

    private HostedRouteLiveAuthority? CreateLiveAuthority(bool requireSelection = true,
        HostedRouteSelection? selectionOverride = null)
    {
        var request = _hostedRequest;
        var owner = _hostedTargetOwner;
        var location = _callbacks?.CurrentLocation;
        var selection = selectionOverride ?? _hostedRouting.CurrentSelection;
        if (request == null || owner == null || location == null
            || (requireSelection && selection?.Generation != _hostedRoutingGeneration)) return null;

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

        return new(_hostedRoutingGeneration, _settings.AuthenticationSessionRevision,
            HostedRouteServerIdentity.Normalize(_settings.ServerUrl), new(location.Longitude, location.Latitude), destination,
            tripAuthority?.Anchors ?? [], owner.Association, tripAuthority?.SegmentId,
            tripAuthority?.SavedTransportProfileId, selection?.TransportProfileId,
            selection?.SelectedProfileAuthorityIdentity, "hosted", selection?.ProviderMode);
    }

    private bool IsRequestCurrent(HostedRouteRequestContext context, Guid partition)
    {
        if (_settings.RoutingAccountPartition != partition) return false;
        var live = CreateLiveAuthority(requireSelection: false);
        return live != null && HostedRoutePublication.CurrentRequest(context, live);
    }

    private bool IsCandidateCurrent(HostedRouteCandidate candidate, Guid partition) =>
        _settings.RoutingAccountPartition == partition
        && CreateLiveAuthority() is { } live && HostedRoutePublication.Current(candidate, live);

    private bool IsInvocationCurrent(HostedRouteRequestContext context, Guid partition,
        CancellationTokenSource cancellation) => ReferenceEquals(_hostedRoutingCancellation, cancellation)
        && !cancellation.IsCancellationRequested && IsRequestCurrent(context, partition);

    private void ReleaseDismissedInvocation(HostedRouteRequestContext context,
        CancellationTokenSource cancellation)
    {
        if (_hostedRoutingGeneration != context.Generation) return;
        if (ReferenceEquals(Interlocked.CompareExchange(
                ref _hostedRoutingCancellation, null, cancellation), cancellation))
            cancellation.Dispose();
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

    private sealed record RetainedRouteDecision(bool RouteComplete, bool Dismissed,
        HostedRouteProvenance? RefreshFallback);

    /// <summary>
    /// Starts navigation with a pre-calculated route (for non-trip navigation).
    /// </summary>
    public Task<bool> StartNavigationWithRouteAsync(NavigationRoute route)
    {
        // Group handoffs already calculated their route. Installation still belongs to startup.
        CancelHostedRouting();
        return CommitStartupAsync(route, null);
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
