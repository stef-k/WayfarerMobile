namespace WayfarerMobile.Tests.Unit.Contracts;

public class TripSheetSegmentNotesReplacementContractTests
{
    [Fact]
    public void SameTripReplacement_ClosesSegmentNotesOnlyWhenSelectedSegmentWasRemoved()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "WayfarerMobile", "ViewModels", "TripSheetViewModel.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));
        var handlerStart = source.IndexOf("private void OnLoadedTripChanged", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private static IReadOnlyList<string> CreateSegmentTrail", handlerStart, StringComparison.Ordinal);
        var handler = source[handlerStart..handlerEnd];

        handler.Should().Contain("var previouslySelectedSegment = SelectedTripSegment;");
        handler.Should().Contain("SelectedTripSegment = replacementSegment;");
        handler.Should().Contain("if (previouslySelectedSegment != null && replacementSegment == null)");
        handler.Should().Contain("IsShowingSegmentNotes = false;");

        handler.IndexOf("SelectedTripSegment = replacementSegment;", StringComparison.Ordinal).Should().BeLessThan(
            handler.IndexOf("IsShowingSegmentNotes = false;", StringComparison.Ordinal),
            "the generated selection notifications must restore overview and title state before Segment Notes closes");
    }
}
