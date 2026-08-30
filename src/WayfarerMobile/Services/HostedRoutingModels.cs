using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

public sealed record HostedRoutingProfile(Guid TransportProfileId, string DisplayName, string ModeKey, string Category);
public sealed record HostedRoutingCatalog(string? DiscoveryCatalogIdentity, string Outcome, IReadOnlyList<HostedRoutingProfile> Profiles);
public sealed record HostedRouteCoordinate(double Longitude, double Latitude);
public sealed record HostedRouteInstruction(string Text, string Type, int FromIndex, int ToIndex,
    double DistanceMetres, double DurationSeconds);

public sealed record HostedRoutingCapability(string Outcome, Guid TransportProfileId,
    string? Provider, Guid? ProviderConfigurationId, string? MappingIdentity, string? StorageMode,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? DiscoveryCatalogIdentity,
    string? SelectedProfileAuthorityIdentity)
{
    public static HostedRoutingCapability Available(Guid profileId, string catalogIdentity,
        string selectedAuthorityIdentity, IReadOnlyList<HostedRouteAttribution> attribution,
        string provider = "geoapify", Guid? providerConfigurationId = null,
        string mappingIdentity = "mapping", string storageMode = "persistent") =>
        new("available", profileId, provider, providerConfigurationId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"),
            mappingIdentity, storageMode, attribution, catalogIdentity, selectedAuthorityIdentity);
}

public sealed record HostedRouteRequest(Guid TransportProfileId, HostedRouteCoordinate Origin,
    HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string SelectedProfileAuthorityIdentity);

public sealed record HostedRouteResponse(bool Succeeded, string Outcome, IReadOnlyList<HostedRouteCoordinate>? Geometry,
    double? DistanceMetres, double? DurationSeconds, IReadOnlyList<HostedRouteInstruction>? Instructions,
    DateTimeOffset? GeneratedAt, string? Provider, Guid? ProviderConfigurationId, string? MappingIdentity,
    Guid? TransportProfileId, IReadOnlyList<HostedRouteCoordinate>? MatchPoints,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? StorageMode,
    string? SelectedProfileAuthorityIdentity)
{
    public static HostedRouteResponse ValidForTest(Guid profileId, string selectedAuthorityIdentity) => new(
        true, "available", [new(23, 37), new(23.01, 37.01)], 1500, 900,
        [new("Continue", "continue", 0, 1, 1500, 900)], DateTimeOffset.UtcNow, "geoapify",
        Guid.Parse("22222222-2222-2222-2222-222222222222"), "mapping", profileId,
        [new(23, 37), new(23.01, 37.01)], [new("Powered by Wayfarer test", "https://example.test")],
        "persistent", selectedAuthorityIdentity);
}

public enum HostedProfileSelectionKind { Selected, RequiresChoice }
public sealed record HostedProfileSelection(HostedProfileSelectionKind Kind, HostedRoutingProfile? Profile,
    IReadOnlyList<HostedRoutingProfile> Choices);

public static class HostedProfileSelector
{
    public static HostedProfileSelection Select(Guid? savedProfileId, string? modeKey, string? category,
        HostedRoutingCatalog catalog)
    {
        if (savedProfileId is { } id)
        {
            var exact = catalog.Profiles.SingleOrDefault(item => item.TransportProfileId == id);
            if (exact != null) return new(HostedProfileSelectionKind.Selected, exact, catalog.Profiles);
        }

        var matches = catalog.Profiles.Where(item => TextMatches(item, modeKey, category)).ToArray();
        return matches.Length == 1
            ? new(HostedProfileSelectionKind.Selected, matches[0], catalog.Profiles)
            : new(HostedProfileSelectionKind.RequiresChoice, null, catalog.Profiles);
    }

    public static HostedRoutingProfile? Confirm(HostedRoutingProfile? choice, HostedRoutingCatalog currentCatalog) =>
        choice != null && currentCatalog.DiscoveryCatalogIdentity != null
            ? currentCatalog.Profiles.SingleOrDefault(item => item.TransportProfileId == choice.TransportProfileId)
            : null;

    private static bool TextMatches(HostedRoutingProfile item, string? modeKey, string? category) =>
        (!string.IsNullOrWhiteSpace(modeKey) && string.Equals(item.ModeKey, modeKey, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(category) && string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
}

public enum HostedRoutingOutcome { Success, Unavailable, RequiresChoice, InvalidResponse, Stale, Cancelled }
public sealed record HostedRoutingResult(HostedRoutingOutcome Outcome, NavigationRoute? Route = null,
    IReadOnlyList<HostedRoutingProfile>? Choices = null, HostedRouteCandidate? Candidate = null);

public sealed record HostedRouteCapabilityMetadata(string Provider, Guid ProviderConfigurationId,
    string MappingIdentity, string StorageMode);

public sealed record HostedRouteCandidate(NavigationRoute Route, HostedRouteRequestContext Context,
    Guid SelectedProfileId, string SelectedProfileAuthorityIdentity, HostedRouteCapabilityMetadata Metadata,
    DateTimeOffset GeneratedAt);

public sealed record HostedRouteRequestContext(Guid? SavedTransportProfileId, string? ModeKey, string? Category,
    HostedRouteCoordinate Origin, HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string DestinationName, long Generation, string SessionAuthority, string NormalizedServer,
    string TargetAssociation, string NavigationChoice, string? ExpectedCatalogIdentity = null,
    Guid? SelectedTransportProfileId = null, string? SelectedProfileAuthorityIdentity = null)
{
    public static HostedRouteRequestContext ForTest(Guid profileId, string? expectedCatalogIdentity = null) => new(
        profileId, "walk", "active", new(23, 37), new(23.01, 37.01), [], "Target", 1,
        "session", "https://wayfarer.test", "place:test", "hosted", expectedCatalogIdentity);
}

public static class HostedRouteIdentity
{
    public static IReadOnlyList<long> Canonicalize(IEnumerable<HostedRouteCoordinate> points) => points
        .SelectMany(point => new[] { Scale(point.Longitude, 180), Scale(point.Latitude, 90) }).ToArray();

    private static long Scale(double value, double bound)
    {
        if (!double.IsFinite(value) || value < -bound || value > bound) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) value = 0;
        return decimal.ToInt64(decimal.Round((decimal)value * 100000m, 0, MidpointRounding.AwayFromZero));
    }
}

public static class HostedOpaqueIdentity
{
    public static bool IsValid(string? value)
    {
        if (value is not { Length: 46 } || !value.StartsWith("v1.", StringComparison.Ordinal)
            || value.Any(character => character > 127)) return false;
        var payload = value[3..];
        if (payload.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;
        Span<byte> decoded = stackalloc byte[32];
        if (!Convert.TryFromBase64String(payload.Replace('-', '+').Replace('_', '/') + "=", decoded, out var written)
            || written != 32) return false;
        var canonical = Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return string.Equals(payload, canonical, StringComparison.Ordinal);
    }
}

public static class HostedRoutePublication
{
    public static bool TryPublish(HostedRouteCandidate candidate, HostedRouteRequestContext live,
        NavigationRoute target)
    {
        if (!Current(candidate, live)) return false;
        Copy(candidate.Route, target);
        return true;
    }

    public static bool Current(HostedRouteCandidate candidate, HostedRouteRequestContext live)
    {
        var expected = candidate.Context;
        return live.Generation == expected.Generation
            && live.SessionAuthority == expected.SessionAuthority
            && live.NormalizedServer == expected.NormalizedServer
            && live.TargetAssociation == expected.TargetAssociation
            && live.NavigationChoice == expected.NavigationChoice
            && live.NavigationChoice == "hosted"
            && live.SavedTransportProfileId == expected.SavedTransportProfileId
            && live.SelectedTransportProfileId == candidate.SelectedProfileId
            && live.SelectedProfileAuthorityIdentity == candidate.SelectedProfileAuthorityIdentity
            && HostedRouteIdentity.Canonicalize(Points(live)).SequenceEqual(HostedRouteIdentity.Canonicalize(Points(expected)));
    }

    private static IEnumerable<HostedRouteCoordinate> Points(HostedRouteRequestContext context) =>
        new[] { context.Origin }.Concat(context.Anchors).Append(context.Destination);

    private static void Copy(NavigationRoute source, NavigationRoute target)
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
}

public interface IHostedRoutingApiClient
{
    Task<HostedRoutingCatalog> DiscoverAsync(CancellationToken cancellationToken);
    Task<HostedRoutingCapability> GetCapabilityAsync(Guid profileId, string discoveryCatalogIdentity,
        CancellationToken cancellationToken);
    Task<HostedRouteResponse> GetRouteAsync(HostedRouteRequest request, CancellationToken cancellationToken);
}
