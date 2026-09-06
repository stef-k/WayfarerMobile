using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

public partial class NavigationCoordinatorViewModel
{
    /// <summary>
    /// Owns a dropped-pin attempt from selection through essential startup. The target
    /// resolver must return null when the caller's pin has been replaced or removed.
    /// </summary>
    public async Task<bool> StartNavigationToCoordinatesAsync(double latitude, double longitude,
        string name, Func<Task<bool?>> chooseDirect, Func<HostedRouteCoordinate?> currentTarget)
    {
        var generation = BeginStartup();
        var current = CaptureStartup(generation, new(longitude, latitude), currentTarget);
        try
        {
            if (!UsableCoordinate(latitude, longitude))
                return await StartupFeedbackAsync("Select a valid destination and try Directions again.");
            var direct = await chooseDirect();
            if (direct == null) return false;
            if (!current()) return await StaleStartupAsync();
            var location = _callbacks?.CurrentLocation;
            if (!UsableLocation(location)) return await MissingLocationAsync();
            NavigationRoute? route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
                location!.Latitude, location.Longitude, latitude, longitude, name, activate: false);
            if (!current()) return await StaleStartupAsync();
            if (!direct.Value)
                route = await TryHostedAsync(route, location.Latitude, location.Longitude,
                    latitude, longitude, name, null,
                    HostedRouteTargetOwner.Member(latitude, longitude, "dropped-pin", currentTarget),
                    generation, current, hostedChosen: true);
            else
                _hostedRouting.SelectDirect(generation);
            if (route == null) return false;
            if (!current()) return await StaleStartupAsync();
            return await CommitStartupAsync(route, null);
        }
        catch (Exception exception)
        {
            return await StartupFailedAsync(exception);
        }
    }

    private long BeginStartup()
    {
        var generation = Interlocked.Increment(ref _hostedRoutingGeneration);
        CancelHostedRouting(incrementGeneration: false);
        return generation;
    }

    private Func<bool> CaptureStartup(long generation, HostedRouteCoordinate target,
        Func<HostedRouteCoordinate?> currentTarget)
    {
        var partition = _settings.RoutingAccountPartition;
        var revision = _settings.AuthenticationSessionRevision;
        var server = HostedRouteServerIdentity.Normalize(_settings.ServerUrl);
        return () => generation == _hostedRoutingGeneration
            && partition == _settings.RoutingAccountPartition
            && revision == _settings.AuthenticationSessionRevision
            && server == HostedRouteServerIdentity.Normalize(_settings.ServerUrl)
            && target == currentTarget();
    }

    // Direct preserves destination/account intent while taking a fresh usable origin.
    // Hosted publication continues to use the stricter CurrentRequest comparison.
    private bool IsIntentCurrent(HostedRouteRequestContext context, Guid partition)
    {
        if (_settings.RoutingAccountPartition != partition
            || _hostedRequest?.Generation != context.Generation
            || _hostedRoutingGeneration != context.Generation
            || _settings.AuthenticationSessionRevision != context.AuthenticationSessionRevision
            || HostedRouteServerIdentity.Normalize(_settings.ServerUrl) != context.NormalizedServer)
            return false;
        var owner = _hostedTargetOwner;
        if (owner?.Association != context.TargetAssociation) return false;
        if (owner.TripPlaceId is { } placeId)
        {
            var place = _tripState.LoadedTrip?.AllPlaces.FirstOrDefault(item => item.Id == placeId);
            return place != null && new HostedRouteCoordinate(place.Longitude, place.Latitude) == context.Destination;
        }
        return owner.ResolveDestination() == context.Destination;
    }

    private async Task<NavigationRoute?> CurrentDirectAsync(NavigationRoute original,
        HostedRouteRequestContext context, Guid partition)
    {
        if (!IsIntentCurrent(context, partition)) { await StaleStartupAsync(); return null; }
        var location = _callbacks?.CurrentLocation;
        if (!UsableLocation(location)) { await MissingLocationAsync(); return null; }
        var route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
            location!.Latitude, location.Longitude, context.Destination.Latitude,
            context.Destination.Longitude, original.DestinationName, activate: false);
        if (!IsIntentCurrent(context, partition)) { await StaleStartupAsync(); return null; }
        route.Waypoints[^1].PlaceId = original.Waypoints.LastOrDefault()?.PlaceId;
        _hostedRouting.SelectDirect(context.Generation);
        return route;
    }

    private async Task<bool> CommitStartupAsync(NavigationRoute route, Guid? placeId)
    {
        if (_callbacks == null || route.Waypoints.Count < 2
            || route.Waypoints.Any(point => !UsableCoordinate(point.Latitude, point.Longitude)))
            return await StartupFeedbackAsync("The map or route is unavailable. Reopen the map and try Directions again.");
        try
        {
            // No awaits inside essential activation: location callbacks cannot observe
            // an installed replacement without matching coordinator, visit and HUD state.
            _tripNavigationService.ActivateRoute(route);
            _callbacks.ShowNavigationRoute(route);
            _callbacks.ZoomToNavigationRoute();
            _callbacks.SetFollowingLocation(false);
            _currentNavigationPlaceId = placeId;
            _visitNotificationService.UpdateNavigationState(true, placeId);
            _navigationHudViewModel.StartNavigationDisplay(route);
            IsNavigating = true;
            // Seed the visible instruction immediately; later Main location updates advance it.
            var location = _callbacks.CurrentLocation;
            if (UsableLocation(location)) UpdateLocation(location!.Latitude, location.Longitude);
        }
        catch (Exception exception)
        {
            CleanupFailedStartup();
            return await StartupFailedAsync(exception);
        }
        if (!IsNavigating || !ReferenceEquals(ActiveRoute, route) || !_navigationHudViewModel.IsNavigating)
            return await StartupFeedbackAsync("You are already at the destination, or navigation has ended. Select a destination to try again.");
        _ = _navigationHudViewModel.AnnounceNavigationStartAsync(route);
        return true;
    }

    private void CleanupFailedStartup()
    {
        // Attempt every cleanup even when a platform callback/release also fails.
        CleanupStartupStep(() => _tripNavigationService.StopNavigation());
        _currentNavigationPlaceId = null;
        CleanupStartupStep(() => IsNavigating = false);
        CleanupStartupStep(() => _visitNotificationService.UpdateNavigationState(false, null));
        CleanupStartupStep(() => _callbacks?.ClearNavigationRoute());
        CleanupStartupStep(() => _navigationHudViewModel.StopNavigationDisplay());
    }

    private void CleanupStartupStep(Action cleanup)
    {
        try { cleanup(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Navigation startup cleanup failed"); }
    }

    private Task<bool> StartupFailedAsync(Exception exception)
    {
        _logger.LogError(exception, "Navigation startup failed");
        return StartupFeedbackAsync("Navigation could not start. Reopen the map and try Directions again.");
    }

    private Task<bool> MissingLocationAsync() =>
        StartupFeedbackAsync("Waiting for a usable location. Try Directions again when your location is available.");

    private Task<bool> StaleStartupAsync() =>
        StartupFeedbackAsync("The location, destination or account changed. Try Directions again, or choose Direct for your current location.");

    private async Task<bool> StartupFeedbackAsync(string message)
    {
        await _dialogs.ShowInfoAsync("Navigation", message);
        return false;
    }

    private static bool UsableLocation(LocationData? location) => location != null
        && UsableCoordinate(location.Latitude, location.Longitude);

    private static bool UsableCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) && latitude is >= -90 and <= 90
        && double.IsFinite(longitude) && longitude is >= -180 and <= 180;
}
