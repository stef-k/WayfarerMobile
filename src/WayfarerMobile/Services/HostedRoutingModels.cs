using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

public sealed record HostedProviderMode(string Key, string Label);
public sealed record HostedRoutingCatalog(string? DiscoveryCatalogIdentity, string Outcome,
    string? Provider = null, IReadOnlyList<HostedProviderMode>? ProviderModes = null)
{
    public IReadOnlyList<HostedProviderMode> Modes => ProviderModes ?? [];
}
public sealed record HostedRouteCoordinate(double Longitude, double Latitude);
public sealed record HostedRouteInstruction(string Text, string Type, int FromIndex, int ToIndex,
    double DistanceMetres, double DurationSeconds);

public sealed record HostedRoutingCapability(string Outcome, Guid TransportProfileId,
    string? Provider, string? StorageMode,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? DiscoveryCatalogIdentity,
    string? SelectedProfileAuthorityIdentity, string? ProviderMode = null)
{
    public static HostedRoutingCapability Available(Guid profileId, string catalogIdentity,
        string selectedAuthorityIdentity, IReadOnlyList<HostedRouteAttribution> attribution,
        string providerMode = "walk", string provider = "geoapify", string storageMode = "persistent") =>
        new("available", profileId, provider, storageMode, attribution, catalogIdentity,
            selectedAuthorityIdentity, providerMode);
}

public sealed record HostedRouteRequest(Guid TransportProfileId, HostedRouteCoordinate Origin,
    HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string SelectedProfileAuthorityIdentity, string ProviderMode);

public sealed record HostedRouteResponse(bool Succeeded, string Outcome, IReadOnlyList<HostedRouteCoordinate>? Geometry,
    double? DistanceMetres, double? DurationSeconds, IReadOnlyList<HostedRouteInstruction>? Instructions,
    DateTimeOffset? GeneratedAt, string? Provider,
    Guid? TransportProfileId, IReadOnlyList<HostedRouteCoordinate>? MatchPoints,
    IReadOnlyList<HostedRouteAttribution>? Attribution, string? StorageMode,
    string? SelectedProfileAuthorityIdentity, string? ProviderMode = null)
{
    public static HostedRouteResponse ValidForTest(Guid profileId, string selectedAuthorityIdentity) => new(
        true, "available", [new(23, 37), new(23.01, 37.01)], 1500, 900,
        [new("Continue", "continue", 0, 1, 1500, 900)], DateTimeOffset.UtcNow, "geoapify",
        profileId,
        [new(23, 37), new(23.01, 37.01)], [new("Powered by Wayfarer test", "https://example.test")],
        "persistent", selectedAuthorityIdentity, "walk");
}

public enum HostedRoutingOutcome { Success, Unavailable, RequiresChoice, CatalogChanged, InvalidResponse, Stale, Cancelled }
public sealed record HostedRoutingResult(HostedRoutingOutcome Outcome, NavigationRoute? Route = null,
    IReadOnlyList<HostedProviderMode>? Choices = null, HostedRouteCandidate? Candidate = null,
    string? DiscoveryCatalogIdentity = null, string? Provider = null);

public sealed record HostedRouteCapabilityMetadata(string Provider, string StorageMode);

public sealed record HostedRouteCandidate(NavigationRoute Route, HostedRouteRequestContext Context,
    Guid SelectedProfileId, string SelectedProfileAuthorityIdentity,
    HostedRouteCapabilityMetadata Metadata, DateTimeOffset GeneratedAt, string SelectedProviderMode);

public sealed record HostedRouteRequestContext(Guid? SavedTransportProfileId,
    HostedRouteCoordinate Origin, HostedRouteCoordinate Destination, IReadOnlyList<HostedRouteCoordinate> Anchors,
    string DestinationName, long Generation, long AuthenticationSessionRevision, string NormalizedServer,
    string TargetAssociation, string NavigationChoice, Guid? SegmentId = null,
    string? ExpectedCatalogIdentity = null, string? ExpectedProvider = null)
{
    public static HostedRouteRequestContext ForTest(Guid profileId, string? expectedCatalogIdentity = null) => new(
        profileId, new(23, 37), new(23.01, 37.01), [], "Target", 1,
        1, "https://wayfarer.test", "place:test", "hosted", ExpectedCatalogIdentity: expectedCatalogIdentity);
}

public sealed record HostedRouteLiveAuthority(
    long Generation,
    long AuthenticationSessionRevision,
    string NormalizedServer,
    HostedRouteCoordinate Origin,
    HostedRouteCoordinate Destination,
    IReadOnlyList<HostedRouteCoordinate> Anchors,
    string TargetAssociation,
    Guid? SegmentId,
    Guid? SavedTransportProfileId,
    Guid? SelectedTransportProfileId,
    string? SelectedProfileAuthorityIdentity,
    string NavigationChoice,
    string? SelectedProviderMode = null);

public sealed record HostedRouteSelection(long Generation, Guid TransportProfileId,
    string ProviderMode, string SelectedProfileAuthorityIdentity);

public sealed record HostedTripTargetAuthority(
    Guid DestinationPlaceId,
    Guid? SegmentId,
    HostedRouteCoordinate Destination,
    Guid? SavedTransportProfileId,
    IReadOnlyList<HostedRouteCoordinate> Anchors)
{
    public static HostedTripTargetAuthority? Resolve(TripDetails? trip, Guid destinationPlaceId,
        double originLatitude, double originLongitude)
    {
        var destination = trip?.AllPlaces.SingleOrDefault(place => place.Id == destinationPlaceId);
        if (destination == null) return null;

        var places = trip!.AllPlaces.ToDictionary(place => place.Id);
        var segment = trip.Segments
            .Where(item => item.DestinationId == destinationPlaceId && item.OriginId is { } originId
                && places.ContainsKey(originId))
            .OrderBy(item =>
            {
                var origin = places[item.OriginId!.Value];
                return GeoMath.CalculateDistance(originLatitude, originLongitude,
                    origin.Latitude, origin.Longitude);
            })
            .FirstOrDefault();
        var anchors = ResolveAnchors(segment, places);
        if (anchors == null) return null;
        return new(destinationPlaceId, segment?.Id,
            new(destination.Longitude, destination.Latitude),
            HostedSegmentProfileIdentity.Get(segment), anchors);
    }

    private static IReadOnlyList<HostedRouteCoordinate>? ResolveAnchors(TripSegment? segment,
        IReadOnlyDictionary<Guid, TripPlace> places)
    {
        if (segment == null || segment.Waypoints.Count == 0) return [];
        if (segment.Waypoints.Count > 3) return null;
        var anchors = new List<HostedRouteCoordinate>(segment.Waypoints.Count);
        foreach (var waypoint in segment.Waypoints.OrderBy(item => item.Position))
        {
            if (!places.TryGetValue(waypoint.PlaceId, out var place)) return null;
            anchors.Add(new(place.Longitude, place.Latitude));
        }
        return anchors;
    }
}

public static class HostedRouteIdentity
{
    public static IReadOnlyList<int> Canonicalize(IEnumerable<HostedRouteCoordinate> points) => points
        .SelectMany(point => new[] { Scale(point.Longitude, 180), Scale(point.Latitude, 90) }).ToArray();

    private static int Scale(double value, double bound)
    {
        if (!double.IsFinite(value) || value < -bound || value > bound) throw new ArgumentOutOfRangeException(nameof(value));
        if (value == 0) value = 0;
        return decimal.ToInt32(decimal.Round((decimal)value * 100000m, 0, MidpointRounding.AwayFromZero));
    }
}

public static class HostedRouteServerIdentity
{
    public static string Normalize(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)) return string.Empty;
        var authority = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return $"{authority}{uri.AbsolutePath}".TrimEnd('/');
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
    public static bool TryPublishRetained(NavigationRoute source, NavigationRoute target)
    {
        if (source.IsDirectRoute || source.HostedProvenance?.IsRetained != true) return false;
        CopyRoute(source, target);
        return true;
    }

    public static bool TryPublish(HostedRouteCandidate candidate, HostedRouteLiveAuthority live,
        NavigationRoute target)
    {
        if (!Current(candidate, live)) return false;
        Copy(candidate, target);
        return true;
    }

    public static bool Current(HostedRouteCandidate candidate, HostedRouteLiveAuthority live)
    {
        var expected = candidate.Context;
        return CurrentRequest(expected, live)
            && live.SelectedTransportProfileId == candidate.SelectedProfileId
            && live.SelectedProviderMode == candidate.SelectedProviderMode
            && live.SelectedProfileAuthorityIdentity == candidate.SelectedProfileAuthorityIdentity;
    }

    public static bool CurrentRequest(HostedRouteRequestContext expected, HostedRouteLiveAuthority live) =>
        live.Generation == expected.Generation
            && live.AuthenticationSessionRevision == expected.AuthenticationSessionRevision
            && live.NormalizedServer == expected.NormalizedServer
            && live.TargetAssociation == expected.TargetAssociation
            && live.SegmentId == expected.SegmentId
            && live.NavigationChoice == expected.NavigationChoice
            && live.NavigationChoice == "hosted"
            && live.SavedTransportProfileId == expected.SavedTransportProfileId
            && HostedRouteIdentity.Canonicalize(Points(live.Origin, live.Anchors, live.Destination))
                .SequenceEqual(HostedRouteIdentity.Canonicalize(
                    Points(expected.Origin, expected.Anchors, expected.Destination)));

    private static IEnumerable<HostedRouteCoordinate> Points(HostedRouteCoordinate origin,
        IReadOnlyList<HostedRouteCoordinate> anchors, HostedRouteCoordinate destination) =>
        new[] { origin }.Concat(anchors).Append(destination);

    private static void Copy(HostedRouteCandidate candidate, NavigationRoute target)
    {
        var source = candidate.Route;
        CopyRoute(source, target);
        var generated = candidate.GeneratedAt.ToUniversalTime();
        target.HostedProvenance = new(candidate.SelectedProfileId,
            candidate.SelectedProfileAuthorityIdentity,
            candidate.Metadata.Provider,
            candidate.Metadata.StorageMode, generated, candidate.SelectedProviderMode);
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
        target.HostedProvenance = source.HostedProvenance;
    }
}

public interface IHostedRoutingApiClient
{
    Task<HostedRoutingCatalog> DiscoverAsync(CancellationToken cancellationToken);
    Task<HostedRoutingCapability> GetCapabilityAsync(Guid profileId, string providerMode,
        string discoveryCatalogIdentity,
        CancellationToken cancellationToken);
    Task<HostedRouteResponse> GetRouteAsync(HostedRouteRequest request, CancellationToken cancellationToken);
}
