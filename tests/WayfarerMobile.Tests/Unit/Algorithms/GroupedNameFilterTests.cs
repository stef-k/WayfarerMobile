using WayfarerMobile.Core.Algorithms;

namespace WayfarerMobile.Tests.Unit.Algorithms;

public class GroupedNameFilterTests
{
    private static readonly IReadOnlyList<GroupedItems<TestTrip>> Groups =
    [
        new("Downloaded",
        [
            new("Athens Weekend"),
            new("Paris Museums")
        ]),
        new("Available on Server",
        [
            new("Athens Food Tour")
        ])
    ];

    [Fact]
    public void Filter_EmptyQuery_ReturnsAllGroupsAndItems()
    {
        var result = GroupedNameFilter.Filter(Groups, string.Empty, trip => trip.Name);

        result.Should().HaveCount(2);
        result.SelectMany(group => group.Items).Should().HaveCount(3);
    }

    [Fact]
    public void Filter_WhitespaceQuery_ReturnsAllGroupsAndItems()
    {
        var result = GroupedNameFilter.Filter(Groups, "   ", trip => trip.Name);

        result.Should().HaveCount(2);
        result.SelectMany(group => group.Items).Should().HaveCount(3);
    }

    [Fact]
    public void Filter_PartialName_MatchesCaseInsensitivelyAndPreservesGroups()
    {
        var result = GroupedNameFilter.Filter(Groups, "  ATHENS ", trip => trip.Name);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Downloaded");
        result[0].Items.Select(trip => trip.Name).Should().Equal("Athens Weekend");
        result[1].Name.Should().Be("Available on Server");
        result[1].Items.Select(trip => trip.Name).Should().Equal("Athens Food Tour");
    }

    [Fact]
    public void Filter_NoMatches_ReturnsNoGroups()
    {
        var result = GroupedNameFilter.Filter(Groups, "Tokyo", trip => trip.Name);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Filter_MatchingOneGroup_OmitsEmptyGroups()
    {
        var result = GroupedNameFilter.Filter(Groups, "Paris", trip => trip.Name);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Downloaded");
        result[0].Items.Select(trip => trip.Name).Should().Equal("Paris Museums");
        result[0].Items[0].Should().BeSameAs(Groups[0].Items[1]);
    }

    private sealed record TestTrip(string Name);
}
