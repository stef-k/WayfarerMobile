using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>Owns transient authenticated hosted-route orchestration and final publication validation.</summary>
public sealed class HostedRoutingService
{
    private const int MaximumGeometry = 10000;
    private const int MaximumInstructions = 1000;
    private readonly IHostedRoutingApiClient api;
    private readonly ILogger<HostedRoutingService> logger;
    private readonly HostedRoutingState? currentState;
    private readonly object stateLock = new();
    private HostedRoutingState? activeState;
    public bool IsLoading { get; private set; }

    public HostedRoutingService(IHostedRoutingApiClient api, ILogger<HostedRoutingService> logger,
        HostedRoutingState? currentState = null)
    {
        this.api = api;
        this.logger = logger;
        this.currentState = currentState;
    }

    public async Task<HostedRoutingResult> RequestRouteAsync(HostedRouteRequestContext context,
        HostedRoutingProfile? explicitChoice = null, CancellationToken cancellationToken = default)
    {
        Begin(context);
        try
        {
            var catalog = await api.DiscoverAsync(cancellationToken);
            if (!AvailableCatalog(catalog)) return new(HostedRoutingOutcome.Unavailable);
            if (context.ExpectedCatalogIdentity != null
                && context.ExpectedCatalogIdentity != catalog.DiscoveryCatalogIdentity)
                return new(HostedRoutingOutcome.Stale);

            var selection = explicitChoice == null
                ? HostedProfileSelector.Select(context.SavedTransportProfileId, context.ModeKey, context.Category, catalog)
                : new HostedProfileSelection(HostedProfileSelectionKind.Selected,
                    HostedProfileSelector.Confirm(explicitChoice, catalog), catalog.Profiles);
            if (selection.Profile == null)
                return new(HostedRoutingOutcome.RequiresChoice, Choices: selection.Choices);
            UpdateSelection(context.Generation, selection.Profile.TransportProfileId, null);

            var capability = await api.GetCapabilityAsync(selection.Profile.TransportProfileId,
                catalog.DiscoveryCatalogIdentity!, cancellationToken);
            if (!ValidCapability(capability, selection.Profile.TransportProfileId, catalog.DiscoveryCatalogIdentity!))
                return new(capability.Outcome == "catalog-changed" ? HostedRoutingOutcome.Stale : HostedRoutingOutcome.Unavailable);
            UpdateSelection(context.Generation, selection.Profile.TransportProfileId,
                capability.SelectedProfileAuthorityIdentity);

            var request = new HostedRouteRequest(selection.Profile.TransportProfileId, context.Origin,
                context.Destination, context.Anchors, capability.SelectedProfileAuthorityIdentity!);
            var response = await api.GetRouteAsync(request, cancellationToken);
            if (!ValidResponse(response, request)) return new(HostedRoutingOutcome.InvalidResponse);
            if (!Current(context, selection.Profile.TransportProfileId, capability.SelectedProfileAuthorityIdentity!))
                return new(HostedRoutingOutcome.Stale);
            return new(HostedRoutingOutcome.Success, BuildRoute(response, context.DestinationName));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(HostedRoutingOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Hosted routing failed locally");
            return new(HostedRoutingOutcome.Unavailable);
        }
        finally
        {
            lock (stateLock)
                if ((currentState ?? activeState)?.Generation == context.Generation) IsLoading = false;
        }
    }

    public void SelectDirect(long generation)
    {
        lock (stateLock)
        {
            activeState = activeState is { } state
                ? state with { Generation = generation, NavigationChoice = "direct", SelectedProfileId = null,
                    SelectedAuthorityIdentity = null }
                : null;
            IsLoading = false;
        }
    }

    private bool Current(HostedRouteRequestContext context, Guid profileId, string authority)
    {
        HostedRoutingState? state;
        lock (stateLock) state = currentState ?? activeState;
        if (state == null) return true;
        var points = new[] { context.Origin }.Concat(context.Anchors).Append(context.Destination);
        return state.Generation == context.Generation
            && state.SessionAuthority == context.SessionAuthority
            && state.NormalizedServer == context.NormalizedServer
            && state.SelectedProfileId == profileId
            && state.SelectedAuthorityIdentity == authority
            && state.TargetAssociation == context.TargetAssociation
            && state.NavigationChoice == context.NavigationChoice
            && state.CanonicalCoordinates.SequenceEqual(HostedRouteIdentity.Canonicalize(points));
    }

    private void Begin(HostedRouteRequestContext context)
    {
        if (currentState != null) return;
        var points = new[] { context.Origin }.Concat(context.Anchors).Append(context.Destination);
        lock (stateLock)
        {
            activeState = new(context.Generation, context.SessionAuthority, context.NormalizedServer, null, null,
                context.TargetAssociation, context.NavigationChoice, HostedRouteIdentity.Canonicalize(points));
            IsLoading = true;
        }
    }

    private void UpdateSelection(long generation, Guid profileId, string? authority)
    {
        if (currentState != null) return;
        lock (stateLock)
            if (activeState?.Generation == generation)
                activeState = activeState with { SelectedProfileId = profileId,
                    SelectedAuthorityIdentity = authority ?? activeState.SelectedAuthorityIdentity };
    }

    private static bool AvailableCatalog(HostedRoutingCatalog value) => value.Outcome == "available"
        && ValidIdentity(value.DiscoveryCatalogIdentity) && value.Profiles.Count is > 0 and <= 100
        && value.Profiles.Select(item => item.TransportProfileId).Distinct().Count() == value.Profiles.Count
        && value.Profiles.All(item => item.TransportProfileId != Guid.Empty && Bounded(item.DisplayName, 200)
            && Bounded(item.ModeKey, 100) && Bounded(item.Category, 100));

    private static bool ValidCapability(HostedRoutingCapability value, Guid profileId, string catalogIdentity) =>
        value.Outcome == "available" && value.TransportProfileId == profileId
        && value.DiscoveryCatalogIdentity == catalogIdentity && ValidIdentity(value.SelectedProfileAuthorityIdentity)
        && ValidAttribution(value.Attribution);

    private static bool ValidResponse(HostedRouteResponse value, HostedRouteRequest request)
    {
        if (!value.Succeeded || value.Outcome != "available" || value.TransportProfileId != request.TransportProfileId
            || value.SelectedProfileAuthorityIdentity != request.SelectedProfileAuthorityIdentity
            || value.GeneratedAt is null || value.Geometry is not { Count: >= 2 and <= MaximumGeometry }
            || value.MatchPoints is null || value.DistanceMetres is not double distance || distance < 0 || !double.IsFinite(distance)
            || value.DurationSeconds is not double duration || duration < 0 || !double.IsFinite(duration)
            || value.Instructions is null || value.Instructions.Count > MaximumInstructions
            || !ValidAttribution(value.Attribution) || !Bounded(value.StorageMode, 100)) return false;
        var inputs = new[] { request.Origin }.Concat(request.Anchors).Append(request.Destination).ToArray();
        return value.MatchPoints.SequenceEqual(inputs) && value.Geometry.All(ValidCoordinate)
            && value.Instructions.All(item => Bounded(item.Text, 500) && Bounded(item.Type, 100)
                && item.FromIndex >= 0 && item.ToIndex >= item.FromIndex && item.ToIndex < value.Geometry.Count
                && double.IsFinite(item.DistanceMetres) && item.DistanceMetres >= 0
                && double.IsFinite(item.DurationSeconds) && item.DurationSeconds >= 0);
    }

    private static NavigationRoute BuildRoute(HostedRouteResponse value, string destinationName) => new()
    {
        Waypoints = value.Geometry!.Select((item, index) => new NavigationWaypoint
        {
            Longitude = item.Longitude, Latitude = item.Latitude,
            Name = index == value.Geometry!.Count - 1 ? destinationName : string.Empty
        }).ToList(),
        Steps = value.Instructions!.Select(item => new NavigationStep
        {
            Instruction = item.Text, ManeuverType = item.Type, DistanceMeters = item.DistanceMetres,
            DurationSeconds = item.DurationSeconds, Longitude = value.Geometry![item.FromIndex].Longitude,
            Latitude = value.Geometry![item.FromIndex].Latitude
        }).ToList(),
        DestinationName = destinationName,
        TotalDistanceMeters = value.DistanceMetres!.Value,
        EstimatedDuration = TimeSpan.FromSeconds(value.DurationSeconds!.Value),
        IsDirectRoute = false,
        Attribution = value.Attribution!.ToList()
    };

    private static bool ValidCoordinate(HostedRouteCoordinate item) => double.IsFinite(item.Longitude)
        && double.IsFinite(item.Latitude) && item.Longitude is >= -180 and <= 180 && item.Latitude is >= -90 and <= 90;
    private static bool ValidIdentity(string? value) => value is { Length: >= 4 and <= 64 } && value.StartsWith("v1.", StringComparison.Ordinal);
    private static bool ValidAttribution(IReadOnlyList<HostedRouteAttribution>? value) => value is { Count: > 0 and <= 10 }
        && value.All(item => Bounded(item.Text, 200) && Bounded(item.Url, 500)
            && Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
    private static bool Bounded(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum;
}
