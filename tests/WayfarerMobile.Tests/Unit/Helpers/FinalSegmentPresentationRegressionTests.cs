using System.Reflection;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Helpers;

public class FinalSegmentPresentationRegressionTests
{
    private const double Tolerance = 0.000001;

    [Fact]
    public void FoldedRoute_RetainedChevronsArePairwiseSeparatedInScreenSpace()
    {
        var chevrons = SegmentChevronPlacer.Place(
            [new(-24, 0), new(108, 0), new(0, 0), new(24, 0)]);

        for (var first = 0; first < chevrons.Count; first++)
        {
            for (var second = first + 1; second < chevrons.Count; second++)
            {
                var distance = Math.Sqrt(
                    Math.Pow(chevrons[second].X - chevrons[first].X, 2) +
                    Math.Pow(chevrons[second].Y - chevrons[first].Y, 2));
                distance.Should().BeGreaterThanOrEqualTo(
                    SegmentChevronPlacer.MinimumSpacing - Tolerance,
                    $"retained chevrons {first} and {second} must not overlap");
            }
        }
    }

    [Fact]
    public void TripReplacement_RemapsSelectionAndRebuildsCurrentSegmentTrail()
    {
        var tripId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        var versionOne = CreateTrip(tripId, segmentId, "Old", waypointIndex: 1);
        var selected = versionOne.Segments.Single();
        selected = PrepareReplacement(versionOne, selected);
        selected!.AnchorTrail.Should().Equal(
            "A — Start — Old start", "B — Via 1 — Old via", "C — End — Old end");

        var validReplacement = CreateTrip(tripId, segmentId, "New", waypointIndex: 1);
        var remapped = PrepareReplacement(validReplacement, selected);

        remapped.Should().BeSameAs(validReplacement.Segments.Single());
        remapped.Should().NotBeSameAs(selected);
        remapped!.AnchorTrail.Should().Equal(
            "A — Start — New start", "B — Via 1 — New via", "C — End — New end");

        var invalidReplacement = CreateTrip(tripId, segmentId, "Invalid", waypointIndex: 2);
        remapped = PrepareReplacement(invalidReplacement, remapped);

        remapped.Should().BeSameAs(invalidReplacement.Segments.Single());
        remapped!.AnchorTrail.Should().Equal(SegmentPresentationProjector.UnavailableMessage);
    }

    [Fact]
    public void TripReplacement_ClearsSelectionWhenSegmentWasRemoved()
    {
        var tripId = Guid.NewGuid();
        var selected = CreateTrip(tripId, Guid.NewGuid(), "Old", waypointIndex: 1).Segments.Single();
        var replacement = new TripDetails { Id = tripId, Version = 2 };

        PrepareReplacement(replacement, selected).Should().BeNull();
    }

    private static TripSegment? PrepareReplacement(TripDetails replacement, TripSegment? selected)
    {
        var method = typeof(SegmentPresentationProjector).GetMethod(
            "PrepareTripReplacement",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(TripDetails), typeof(TripSegment)]);
        method.Should().NotBeNull(
            "Trip replacement must prepare the new object graph and remap selected Segment presentation by ID");
        return (TripSegment?)method!.Invoke(null, [replacement, selected]);
    }

    private static TripDetails CreateTrip(Guid tripId, Guid segmentId, string namePrefix, int waypointIndex)
    {
        var start = new TripPlace { Id = Guid.NewGuid(), Name = $"{namePrefix} start", Latitude = 37.90, Longitude = 23.70 };
        var via = new TripPlace { Id = Guid.NewGuid(), Name = $"{namePrefix} via", Latitude = 37.91, Longitude = 23.71 };
        var end = new TripPlace { Id = Guid.NewGuid(), Name = $"{namePrefix} end", Latitude = 37.92, Longitude = 23.72 };
        var region = new TripRegion { Places = [start, via, end] };
        var segment = new TripSegment
        {
            Id = segmentId,
            OriginId = start.Id,
            DestinationId = end.Id,
            Geometry = """{"type":"LineString","coordinates":[[23.70,37.90],[23.71,37.91],[23.72,37.92]]}""",
            HasCustomRoute = true,
            Waypoints = [new TripSegmentWaypoint { PlaceId = via.Id, Position = 0, RouteVertexIndex = waypointIndex }]
        };
        return new TripDetails { Id = tripId, Version = 2, Regions = [region], Segments = [segment] };
    }
}
