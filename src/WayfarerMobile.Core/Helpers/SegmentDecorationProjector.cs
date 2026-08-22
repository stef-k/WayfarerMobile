namespace WayfarerMobile.Core.Helpers;

public sealed record SegmentBadgeProjection(Guid PlaceId, string Label, double Latitude, double Longitude, int SemanticPosition);
public readonly record struct ProjectedRoutePoint(double X, double Y);
public readonly record struct SegmentChevronProjection(double X, double Y, double AngleDegrees);

public static class SegmentDecorationProjector
{
    public static IReadOnlyList<SegmentBadgeProjection> CreateBadges(SegmentAnchorResolution resolution)
    {
        if (!resolution.IsValid) return Array.Empty<SegmentBadgeProjection>();
        return resolution.Anchors
            .GroupBy(anchor => anchor.PlaceId)
            .Select(group => new SegmentBadgeProjection(
                group.Key,
                string.Join('/', group.Select(anchor => anchor.Label)),
                group.First().Latitude,
                group.First().Longitude,
                group.Min(anchor => anchor.SemanticPosition)))
            .OrderBy(badge => badge.SemanticPosition)
            .ToList();
    }
}

/// <summary>Places bounded decorative direction cues along projected route distance.</summary>
public static class SegmentChevronPlacer
{
    public const double EndpointClearance = 24;
    public const double MinimumSpacing = 72;
    public const int MaximumCount = 8;

    public static IReadOnlyList<SegmentChevronProjection> Place(IReadOnlyList<ProjectedRoutePoint> points)
    {
        if (points.Count < 2 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            return Array.Empty<SegmentChevronProjection>();

        var cumulative = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
            cumulative[index] = cumulative[index - 1] + Distance(points[index - 1], points[index]);
        var total = cumulative[^1];
        var usable = total - (2 * EndpointClearance);
        if (usable < MinimumSpacing) return Array.Empty<SegmentChevronProjection>();

        var count = Math.Min(MaximumCount, Math.Max(1, (int)Math.Floor(usable / MinimumSpacing)));
        var spacing = usable / (count + 1);
        var results = new List<SegmentChevronProjection>(count);
        for (var placement = 1; placement <= count; placement++)
        {
            var target = EndpointClearance + placement * spacing;
            var segmentIndex = 1;
            while (segmentIndex < cumulative.Length && cumulative[segmentIndex] < target) segmentIndex++;
            if (segmentIndex >= cumulative.Length) break;
            var start = points[segmentIndex - 1];
            var end = points[segmentIndex];
            var length = cumulative[segmentIndex] - cumulative[segmentIndex - 1];
            if (length < 4) continue;
            var ratio = (target - cumulative[segmentIndex - 1]) / length;
            results.Add(new(
                start.X + ((end.X - start.X) * ratio),
                start.Y + ((end.Y - start.Y) * ratio),
                Math.Atan2(end.Y - start.Y, end.X - start.X) * 180 / Math.PI));
        }
        return results;
    }

    private static double Distance(ProjectedRoutePoint a, ProjectedRoutePoint b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}
