using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class SegmentDecorationProjectorTests
{
    [Fact]
    public void BadgeProjection_CoalescesIdentityThenSuppressesOverlapsInSemanticOrder()
    {
        var first = Guid.NewGuid();
        var overlapping = Guid.NewGuid();
        var clear = Guid.NewGuid();
        var badges = new[]
        {
            new SegmentBadgeProjection(first, "A/C", 0, 0, 0),
            new SegmentBadgeProjection(overlapping, "B", 0, 0, 1),
            new SegmentBadgeProjection(clear, "D", 0, 200, 3)
        };
        var method = typeof(SegmentDecorationProjector).GetMethod("RetainVisibleBadges");

        method.Should().NotBeNull("badge collisions must be resolved by the production projector");
        var retained = (IReadOnlyList<SegmentBadgeProjection>)method!.Invoke(null,
            [badges, (Func<SegmentBadgeProjection, (double X, double Y, double Width, double Height)>)(badge =>
                (badge.Longitude, badge.Latitude, 40, 28))])!;

        retained.Select(badge => (badge.PlaceId, badge.Label)).Should().Equal((first, "A/C"), (clear, "D"));
        var reversed = new[] { badges[1] with { SemanticPosition = 0 }, badges[0] with { SemanticPosition = 1 }, badges[2] };
        var reversedRetained = (IReadOnlyList<SegmentBadgeProjection>)method.Invoke(null,
            [reversed, (Func<SegmentBadgeProjection, (double X, double Y, double Width, double Height)>)(badge =>
                (badge.Longitude, badge.Latitude, 40, 28))])!;
        reversedRetained.Select(badge => badge.PlaceId).Should().Equal(overlapping, clear);
    }

    [Fact]
    public void ChevronPlacement_FollowsGeometryWithClearanceEvenSpacingAndCap()
    {
        var points = Enumerable.Range(0, 20)
            .Select(index => new ProjectedRoutePoint(index * 100, 0))
            .ToList();

        var chevrons = SegmentChevronPlacer.Place(points);

        chevrons.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(8);
        chevrons.Should().OnlyContain(c => c.X > 0 && c.X < 1900 && Math.Abs(c.AngleDegrees) < 0.001);
        chevrons.Zip(chevrons.Skip(1), (a, b) => b.X - a.X)
            .Should().OnlyContain(spacing => spacing > 0);
    }

    [Fact]
    public void ChevronPlacement_SuppressesShortOrInvalidGeometry()
    {
        SegmentChevronPlacer.Place([new(0, 0), new(5, 0)]).Should().BeEmpty();
        SegmentChevronPlacer.Place([new(double.NaN, 0), new(100, 0)]).Should().BeEmpty();
    }

    [Theory]
    [InlineData(119.999, 0)]
    [InlineData(120, 1)]
    [InlineData(192, 2)]
    [InlineData(2000, 8)]
    public void ChevronPlacement_RespectsMinimumSpacingAtBoundaries(double routeLength, int expectedCount)
    {
        var chevrons = SegmentChevronPlacer.Place([new(0, 0), new(routeLength, 0)]);
        chevrons.Should().HaveCount(expectedCount);
        chevrons.Zip(chevrons.Skip(1), (left, right) =>
                Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2)))
            .Should().OnlyContain(distance => distance + 0.000001 >= SegmentChevronPlacer.MinimumSpacing);
    }

    [Fact]
    public void ChevronPlacement_FoldedRouteSuppressesScreenSpaceCollision()
    {
        var chevrons = SegmentChevronPlacer.Place(
            [new(0, 0), new(100, 0), new(100, 1), new(0, 1)]);

        chevrons.Zip(chevrons.Skip(1), (left, right) =>
                Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2)))
            .Should().OnlyContain(distance => distance + 0.000001 >= SegmentChevronPlacer.MinimumSpacing);
    }
}
