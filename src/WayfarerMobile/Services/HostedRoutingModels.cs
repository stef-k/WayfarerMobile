using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

public sealed record HostedRoutingProfile(Guid TransportProfileId, string DisplayName, string ModeKey, string Category);
public sealed record HostedRoutingCatalog(string? DiscoveryCatalogIdentity, string Outcome, IReadOnlyList<HostedRoutingProfile> Profiles);
public sealed record HostedRouteCoordinate(double Longitude, double Latitude);
public sealed record HostedRouteInstruction(string Text, string Type, int FromIndex, int ToIndex,
    double DistanceMetres, double DurationSeconds);

public sealed record HostedRoutingCapability(string Outcome, Guid TransportProfileId,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? DiscoveryCatalogIdentity,
    string? SelectedProfileAuthorityIdentity)
{
    public static HostedRoutingCapability Available(Guid profileId, string catalogIdentity,
        string selectedAuthorityIdentity, IReadOnlyList<HostedRouteAttribution> attribution) =>
        new("available", profileId, attribution, catalogIdentity, selectedAuthorityIdentity);
}

public sealed record HostedRouteRequest(Guid TransportProfileId, HostedRouteCoordinate Origin,
    HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string SelectedProfileAuthorityIdentity);

public sealed record HostedRouteResponse(bool Succeeded, string Outcome, IReadOnlyList<HostedRouteCoordinate>? Geometry,
    double? DistanceMetres, double? DurationSeconds, IReadOnlyList<HostedRouteInstruction>? Instructions,
    DateTimeOffset? GeneratedAt, Guid? TransportProfileId, IReadOnlyList<HostedRouteCoordinate>? MatchPoints,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? StorageMode,
    string? SelectedProfileAuthorityIdentity)
{
    public static HostedRouteResponse ValidForTest(Guid profileId, string selectedAuthorityIdentity) => new(
        true, "available", [new(23, 37), new(23.01, 37.01)], 1500, 900,
        [new("Continue", "continue", 0, 1, 1500, 900)], DateTimeOffset.UtcNow, profileId,
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
            && currentCatalog.Profiles.Any(item => item == choice) ? choice : null;

    private static bool TextMatches(HostedRoutingProfile item, string? modeKey, string? category) =>
        (!string.IsNullOrWhiteSpace(modeKey) && string.Equals(item.ModeKey, modeKey, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(category) && string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
}

public enum HostedRoutingOutcome { Success, Unavailable, RequiresChoice, InvalidResponse, Stale, Cancelled }
public sealed record HostedRoutingResult(HostedRoutingOutcome Outcome, NavigationRoute? Route = null,
    IReadOnlyList<HostedRoutingProfile>? Choices = null);

public sealed record HostedRouteRequestContext(Guid? SavedTransportProfileId, string? ModeKey, string? Category,
    HostedRouteCoordinate Origin, HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string DestinationName, long Generation, string SessionAuthority, string NormalizedServer,
    string TargetAssociation, string NavigationChoice, string? ExpectedCatalogIdentity = null)
{
    public static HostedRouteRequestContext ForTest(Guid profileId, string? expectedCatalogIdentity = null) => new(
        profileId, "walk", "active", new(23, 37), new(23.01, 37.01), [], "Target", 1,
        "session", "https://wayfarer.test", "place:test", "hosted", expectedCatalogIdentity);
}

public sealed record HostedRoutingState(long Generation, string SessionAuthority, string NormalizedServer,
    Guid? SelectedProfileId, string? SelectedAuthorityIdentity, string TargetAssociation,
    string NavigationChoice, IReadOnlyList<long> CanonicalCoordinates)
{
    public static HostedRoutingState ForTest(string? catalogIdentity = null,
        string? selectedAuthorityIdentity = null) => new(1, "session", "https://wayfarer.test",
            WalkingTestProfile, selectedAuthorityIdentity, "place:test", "hosted",
            HostedRouteIdentity.Canonicalize([new(23, 37), new(23.01, 37.01)]));

    private static readonly Guid WalkingTestProfile = Guid.Parse("11111111-1111-1111-1111-111111111111");
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

public interface IHostedRoutingApiClient
{
    Task<HostedRoutingCatalog> DiscoverAsync(CancellationToken cancellationToken);
    Task<HostedRoutingCapability> GetCapabilityAsync(Guid profileId, string discoveryCatalogIdentity,
        CancellationToken cancellationToken);
    Task<HostedRouteResponse> GetRouteAsync(HostedRouteRequest request, CancellationToken cancellationToken);
}
