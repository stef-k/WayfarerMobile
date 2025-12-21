namespace WayfarerMobile.Tests.Unit.ViewModels;

public class MainViewModelTripSheetTests
{
    [Fact]
    public void IsTripSheetShowingOverview_WhenNoSelection_ReturnsTrue()
    {
        var state = new TripSheetState();
        state.IsTripSheetShowingOverview.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingOverview_WhenPlaceSelected_ReturnsFalse()
    {
        var state = new TripSheetState { SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "Test" } };
        state.IsTripSheetShowingOverview.Should().BeFalse();
    }

    [Fact]
    public void IsTripSheetShowingPlace_WhenPlaceSelected_ReturnsTrue()
    {
        var state = new TripSheetState { SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "Test" } };
        state.IsTripSheetShowingPlace.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingPlace_WhenNoPlaceSelected_ReturnsFalse()
    {
        var state = new TripSheetState();
        state.IsTripSheetShowingPlace.Should().BeFalse();
    }

    [Fact]
    public void IsTripSheetShowingArea_WhenAreaSelected_ReturnsTrue()
    {
        var state = new TripSheetState { SelectedArea = new TripArea { Id = Guid.NewGuid(), Name = "Area" } };
        state.IsTripSheetShowingArea.Should().BeTrue();
    }

    [Fact]
    public void SelectTripPlace_SetsSelectedPlace()
    {
        var state = new TripSheetState();
        var place = new TripPlace { Id = Guid.NewGuid(), Name = "Paris" };
        state.SelectPlace(place);
        state.SelectedPlace.Should().Be(place);
    }

    [Fact]
    public void SelectTripPlace_ClearsOtherSelections()
    {
        var state = new TripSheetState
        {
            SelectedArea = new TripArea { Id = Guid.NewGuid(), Name = "Area" },
            SelectedSegment = new TripSegment { Id = Guid.NewGuid() }
        };
        state.SelectPlace(new TripPlace { Id = Guid.NewGuid(), Name = "New" });
        state.SelectedArea.Should().BeNull();
        state.SelectedSegment.Should().BeNull();
    }

    [Fact]
    public void SelectTripArea_SetsSelectedArea()
    {
        var state = new TripSheetState();
        var area = new TripArea { Id = Guid.NewGuid(), Name = "District" };
        state.SelectArea(area);
        state.SelectedArea.Should().Be(area);
    }

    [Fact]
    public void ClearTripSheetSelection_ClearsAllSelections()
    {
        var state = new TripSheetState
        {
            SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "P" },
            SelectedArea = new TripArea { Id = Guid.NewGuid(), Name = "A" },
            SelectedSegment = new TripSegment { Id = Guid.NewGuid() }
        };
        state.ClearSelection();
        state.SelectedPlace.Should().BeNull();
        state.SelectedArea.Should().BeNull();
        state.SelectedSegment.Should().BeNull();
    }

    [Fact]
    public void ClearTripSheetSelection_ReturnsToOverview()
    {
        var state = new TripSheetState { SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "P" } };
        state.ClearSelection();
        state.IsTripSheetShowingOverview.Should().BeTrue();
    }

    [Fact]
    public void TripSheetTitle_WhenPlaceSelected_ReturnsPlaceName()
    {
        var state = new TripSheetState
        {
            TripName = "Trip",
            SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "Eiffel Tower" }
        };
        state.TripSheetTitle.Should().Be("Eiffel Tower");
    }

    [Fact]
    public void TripSheetTitle_WhenAreaSelected_ReturnsAreaName()
    {
        var state = new TripSheetState
        {
            TripName = "Trip",
            SelectedArea = new TripArea { Id = Guid.NewGuid(), Name = "Historic District" }
        };
        state.TripSheetTitle.Should().Be("Historic District");
    }

    [Fact]
    public void TripSheetTitle_WhenNoSelection_ReturnsTripName()
    {
        var state = new TripSheetState { TripName = "Europe Adventure" };
        state.TripSheetTitle.Should().Be("Europe Adventure");
    }

    [Fact]
    public void TripSheetSubtitle_WhenPlaceSelected_ReturnsTripName()
    {
        var state = new TripSheetState
        {
            TripName = "Europe Trip",
            SelectedPlace = new TripPlace { Id = Guid.NewGuid(), Name = "Place" }
        };
        state.TripSheetSubtitle.Should().Be("Europe Trip");
    }

    [Fact]
    public void TripSheetSubtitle_SinglePlace_UsesSingularForm()
    {
        var state = new TripSheetState { TripName = "Trip", PlaceCount = 1 };
        state.TripSheetSubtitle.Should().Be("1 place");
    }

    [Fact]
    public void TripSheetSubtitle_MultiplePlaces_UsesPluralForm()
    {
        var state = new TripSheetState { TripName = "Trip", PlaceCount = 5 };
        state.TripSheetSubtitle.Should().Be("5 places");
    }
}

internal class TripSheetState
{
    public TripPlace? SelectedPlace { get; set; }
    public TripArea? SelectedArea { get; set; }
    public TripSegment? SelectedSegment { get; set; }
    public string TripName { get; set; } = string.Empty;
    public int PlaceCount { get; set; }

    public bool IsTripSheetShowingOverview
    {
        get { return SelectedPlace == null && SelectedArea == null && SelectedSegment == null; }
    }
    public bool IsTripSheetShowingPlace
    {
        get { return SelectedPlace != null; }
    }
    public bool IsTripSheetShowingArea
    {
        get { return SelectedArea != null; }
    }
    public bool IsTripSheetShowingSegment
    {
        get { return SelectedSegment != null; }
    }

    public string TripSheetTitle
    {
        get
        {
            if (SelectedPlace != null) return SelectedPlace.Name;
            if (SelectedArea != null) return SelectedArea.Name;
            return TripName;
        }
    }

    public string TripSheetSubtitle
    {
        get
        {
            if (SelectedPlace != null || SelectedArea != null || SelectedSegment != null)
                return TripName;
            return PlaceCount == 1 ? "1 place" : PlaceCount + " places";
        }
    }

    public void SelectPlace(TripPlace place)
    {
        SelectedPlace = place; SelectedArea = null; SelectedSegment = null;
    }

    public void SelectArea(TripArea area)
    {
        SelectedArea = area; SelectedPlace = null; SelectedSegment = null;
    }

    public void SelectSegment(TripSegment segment)
    {
        SelectedSegment = segment; SelectedPlace = null; SelectedArea = null;
    }

    public void ClearSelection()
    {
        SelectedPlace = null; SelectedArea = null; SelectedSegment = null;
    }
}
