using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class SegmentDecorationProjectorTests
{
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
}
