using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>Validates transient authenticated hosted-route candidates before coordinator publication.</summary>
public sealed class HostedRoutingService
{
    private const int MaximumGeometry = 10000;
    private const int MaximumInstructions = 1000;
    private readonly IHostedRoutingApiClient api;
    private readonly ILogger<HostedRoutingService> logger;
    private readonly object stateLock = new();
    private long activeGeneration;
    private HostedRouteSelection? currentSelection;
    public bool IsLoading { get; private set; }

    public HostedRouteSelection? CurrentSelection
    {
        get { lock (stateLock) return currentSelection; }
    }

    public HostedRoutingService(IHostedRoutingApiClient api, ILogger<HostedRoutingService> logger)
    {
        this.api = api;
        this.logger = logger;
    }

    public async Task<HostedRoutingResult> RequestRouteAsync(HostedRouteRequestContext context,
        HostedProviderMode? explicitChoice = null, CancellationToken cancellationToken = default,
        bool allowCatalogRediscovery = true)
    {
        if (!Begin(context)) return new(HostedRoutingOutcome.Stale);
        try
        {
            HostedProviderMode selectedMode;
            string catalogIdentity;
            if (explicitChoice == null)
            {
                var catalog = await api.DiscoverAsync(cancellationToken);
                if (!AvailableCatalog(catalog)) return new(HostedRoutingOutcome.Unavailable);
                return new(HostedRoutingOutcome.RequiresChoice, Choices: catalog.Modes,
                    DiscoveryCatalogIdentity: catalog.DiscoveryCatalogIdentity, Provider: catalog.Provider);
            }
            else
            {
                if (!ValidMode(explicitChoice) || !Bounded(context.ExpectedProvider, 100)
                    || !HostedOpaqueIdentity.IsValid(context.ExpectedCatalogIdentity))
                    return new(HostedRoutingOutcome.Unavailable);
                selectedMode = explicitChoice;
                catalogIdentity = context.ExpectedCatalogIdentity!;
            }

            var profileId = context.SavedTransportProfileId ?? Guid.Empty;
            var capability = await api.GetCapabilityAsync(
                profileId, selectedMode.Key, catalogIdentity, cancellationToken);
            if (capability.Outcome == "catalog-changed")
                return allowCatalogRediscovery
                    ? await RefreshCatalogAsync(cancellationToken)
                    : new(HostedRoutingOutcome.Unavailable);
            if (!ValidCapability(capability, profileId, selectedMode.Key,
                    context.ExpectedProvider!, catalogIdentity))
                return new(HostedRoutingOutcome.Unavailable);
            var request = new HostedRouteRequest(profileId, context.Origin,
                context.Destination, context.Anchors, capability.SelectedProfileAuthorityIdentity!,
                selectedMode.Key);
            var response = await api.GetRouteAsync(request, cancellationToken);
            if (!ValidResponse(response, request, capability)) return new(HostedRoutingOutcome.InvalidResponse);
            if (!SelectCurrent(context.Generation, profileId, selectedMode.Key,
                capability.SelectedProfileAuthorityIdentity!))
                return new(HostedRoutingOutcome.Stale);
            var metadata = new HostedRouteCapabilityMetadata(capability.Provider!,
                capability.ProviderConfigurationId!.Value, capability.MappingIdentity!, capability.StorageMode!);
            var candidate = new HostedRouteCandidate(BuildRoute(response, context.DestinationName), context,
                profileId, capability.SelectedProfileAuthorityIdentity!, metadata,
                response.GeneratedAt!.Value, selectedMode.Key);
            return new(HostedRoutingOutcome.Success, Candidate: candidate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(HostedRoutingOutcome.Cancelled);
        }
        catch (Exception)
        {
            logger.LogWarning("Hosted routing failed locally: transport-or-contract-error");
            return new(HostedRoutingOutcome.Unavailable);
        }
        finally
        {
            lock (stateLock)
                if (activeGeneration == context.Generation) IsLoading = false;
        }
    }

    public void SelectDirect(long generation)
    {
        lock (stateLock)
        {
            activeGeneration = generation;
            currentSelection = null;
            IsLoading = false;
        }
    }

    public void SelectRetained(long generation, Guid profileId, string providerMode, string authorityIdentity)
    {
        lock (stateLock)
        {
            activeGeneration = generation;
            currentSelection = new(generation, profileId, providerMode, authorityIdentity);
            IsLoading = false;
        }
    }

    /// <summary>Restores the selection that preceded a dismissed chooser.</summary>
    public void RestoreSelection(HostedRouteSelection? selection)
    {
        lock (stateLock)
        {
            activeGeneration = selection?.Generation ?? 0;
            currentSelection = selection;
            IsLoading = false;
        }
    }

    private bool Begin(HostedRouteRequestContext context)
    {
        lock (stateLock)
        {
            if (context.Generation < activeGeneration) return false;
            activeGeneration = context.Generation;
            currentSelection = null;
            IsLoading = true;
            return true;
        }
    }

    private bool SelectCurrent(long generation, Guid profileId, string providerMode, string authorityIdentity)
    {
        lock (stateLock)
        {
            if (activeGeneration != generation) return false;
            currentSelection = new(generation, profileId, providerMode, authorityIdentity);
            return true;
        }
    }

    private async Task<HostedRoutingResult> RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await api.DiscoverAsync(cancellationToken);
        return AvailableCatalog(catalog)
            ? new(HostedRoutingOutcome.CatalogChanged, Choices: catalog.Modes,
                DiscoveryCatalogIdentity: catalog.DiscoveryCatalogIdentity, Provider: catalog.Provider)
            : new(HostedRoutingOutcome.Unavailable);
    }

    private static bool AvailableCatalog(HostedRoutingCatalog value) => value.Outcome == "available"
        && HostedOpaqueIdentity.IsValid(value.DiscoveryCatalogIdentity) && Bounded(value.Provider, 100)
        && value.Profiles.Count <= 100
        && value.Profiles.Select(item => item.TransportProfileId).Distinct().Count() == value.Profiles.Count
        && value.Profiles.All(ValidProfile) && value.Modes.Count is > 0 and <= 20
        && value.Modes.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() == value.Modes.Count
        && value.Modes.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() == value.Modes.Count
        && value.Modes.All(ValidMode);

    private static bool ValidProfile(HostedRoutingProfile item) => item.TransportProfileId != Guid.Empty
        && Bounded(item.DisplayName, 200) && Bounded(item.ModeKey, 100) && Bounded(item.Category, 100);

    private static bool ValidMode(HostedProviderMode item) => Bounded(item.Key, 100) && Bounded(item.Label, 200)
        && item.Key == item.Key.Trim() && item.Label == item.Label.Trim();

    private static bool ValidCapability(HostedRoutingCapability value, Guid profileId, string providerMode,
        string provider, string catalogIdentity) =>
        value.Outcome == "available" && value.TransportProfileId == profileId
        && value.ProviderMode == providerMode && value.Provider == provider
        && value.DiscoveryCatalogIdentity == catalogIdentity
        && HostedOpaqueIdentity.IsValid(value.DiscoveryCatalogIdentity)
        && HostedOpaqueIdentity.IsValid(value.SelectedProfileAuthorityIdentity)
        && Bounded(value.Provider, 100) && value.ProviderConfigurationId is { } id && id != Guid.Empty
        && Bounded(value.MappingIdentity, 200) && Bounded(value.StorageMode, 100)
        && ValidAttribution(value.Attribution);

    private static bool ValidResponse(HostedRouteResponse value, HostedRouteRequest request,
        HostedRoutingCapability capability)
    {
        if (!value.Succeeded || value.Outcome != "available" || value.TransportProfileId != request.TransportProfileId
            || value.ProviderMode != request.ProviderMode || value.ProviderMode != capability.ProviderMode
            || value.SelectedProfileAuthorityIdentity != request.SelectedProfileAuthorityIdentity
            || !HostedOpaqueIdentity.IsValid(value.SelectedProfileAuthorityIdentity)
            || value.Provider != capability.Provider
            || value.ProviderConfigurationId != capability.ProviderConfigurationId
            || value.MappingIdentity != capability.MappingIdentity || value.StorageMode != capability.StorageMode
            || value.GeneratedAt is not { } generatedAt || generatedAt.Offset != TimeSpan.Zero
            || value.Geometry is not { Count: >= 2 and <= MaximumGeometry }
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
            Latitude = value.Geometry![item.FromIndex].Latitude,
            GeometryFromIndex = item.FromIndex, GeometryToIndex = item.ToIndex
        }).ToList(),
        DestinationName = destinationName,
        TotalDistanceMeters = value.DistanceMetres!.Value,
        EstimatedDuration = TimeSpan.FromSeconds(value.DurationSeconds!.Value),
        IsDirectRoute = false,
        Attribution = value.Attribution!.ToList()
    };

    private static bool ValidCoordinate(HostedRouteCoordinate item) => double.IsFinite(item.Longitude)
        && double.IsFinite(item.Latitude) && item.Longitude is >= -180 and <= 180 && item.Latitude is >= -90 and <= 90;
    private static bool ValidAttribution(IReadOnlyList<HostedRouteAttribution>? value) => value is { Count: > 0 and <= 10 }
        && value.All(item => Bounded(item.Text, 200) && Bounded(item.Url, 500)
            && Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo));
    private static bool Bounded(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum;
}
