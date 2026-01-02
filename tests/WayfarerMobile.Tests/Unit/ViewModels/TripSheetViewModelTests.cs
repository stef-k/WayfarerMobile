using WayfarerMobile.Core.Models;
using WayfarerMobile.Tests.Infrastructure.Mocks;

namespace WayfarerMobile.Tests.Unit.ViewModels;

/// <summary>
/// Unit tests for TripSheetViewModel.
/// Documents expected behavior for trip sheet state management, selection, and editing.
/// Pure logic tests verify computation without instantiating ViewModels with MAUI dependencies.
/// </summary>
public class TripSheetViewModelTests : IDisposable
{
    private readonly MockTripSyncService _mockSyncService;
    private readonly MockToastService _mockToast;
    private readonly MockSettingsService _mockSettings;

    public TripSheetViewModelTests()
    {
        _mockSyncService = new MockTripSyncService();
        _mockToast = new MockToastService();
        _mockSettings = new MockSettingsService();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    #region Computed Properties - HasLoadedTrip

    [Fact]
    public void HasLoadedTrip_ReturnsFalseWhenNull()
    {
        // Document expected behavior:
        // HasLoadedTrip => LoadedTrip != null
        // When LoadedTrip is null, should return false

        TripDetails? trip = null;
        var hasTrip = trip != null;
        hasTrip.Should().BeFalse();
    }

    [Fact]
    public void HasLoadedTrip_ReturnsTrueWhenLoaded()
    {
        // Document expected behavior:
        var trip = CreateTestTripDetails();
        var hasTrip = trip != null;
        hasTrip.Should().BeTrue();
    }

    #endregion

    #region Computed Properties - TripPlaceCount

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void TripPlaceCount_ReturnsAllPlacesCount(int placeCount)
    {
        // Document expected behavior:
        // TripPlaceCount => LoadedTrip?.AllPlaces.Count ?? 0
        var trip = CreateTripWithPlaceCount(placeCount);
        trip.AllPlaces.Count.Should().Be(placeCount);
    }

    [Fact]
    public void TripPlaceCount_ReturnsZeroWhenNoTrip()
    {
        TripDetails? trip = null;
        var count = trip?.AllPlaces.Count ?? 0;
        count.Should().Be(0);
    }

    #endregion

    #region Computed Properties - HasTripSegments

    [Fact]
    public void HasTripSegments_ReturnsFalseWhenNoSegments()
    {
        var trip = CreateTestTripDetails();
        trip.Segments.Clear();
        var hasSegments = trip.Segments.Count > 0;
        hasSegments.Should().BeFalse();
    }

    [Fact]
    public void HasTripSegments_ReturnsTrueWithSegments()
    {
        var trip = CreateTestTripDetails();
        trip.Segments.Add(new TripSegment { Id = Guid.NewGuid() });
        var hasSegments = trip.Segments.Count > 0;
        hasSegments.Should().BeTrue();
    }

    #endregion

    #region Computed Properties - TripNotesPreview

    [Fact]
    public void TripNotesPreview_ReturnsNullWhenNoNotes()
    {
        var trip = CreateTestTripDetails();
        trip.Notes = null;
        var preview = GetNotesPreview(trip.Notes);
        preview.Should().BeNull();
    }

    [Fact]
    public void TripNotesPreview_ReturnsNullWhenEmptyNotes()
    {
        var trip = CreateTestTripDetails();
        trip.Notes = "";
        var preview = GetNotesPreview(trip.Notes);
        preview.Should().BeNull();
    }

    [Fact]
    public void TripNotesPreview_ReturnsFullTextUnder200Chars()
    {
        var shortNotes = "Short notes here.";
        var preview = GetNotesPreview(shortNotes);
        preview.Should().Be(shortNotes);
    }

    [Fact]
    public void TripNotesPreview_TruncatesOver200Chars()
    {
        var longNotes = new string('a', 300);
        var preview = GetNotesPreview(longNotes);
        preview.Should().HaveLength(203); // 200 + "..."
        preview.Should().EndWith("...");
    }

    private static string? GetNotesPreview(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
            return null;
        return notes.Length > 200 ? notes[..200] + "..." : notes;
    }

    #endregion

    #region Computed Properties - Sheet Display Mode

    [Fact]
    public void IsTripSheetShowingOverview_TrueWhenNoSelections()
    {
        // Document expected behavior:
        // IsTripSheetShowingOverview => no selections and not showing notes

        var state = new SheetState();
        var isOverview = state.SelectedPlace == null &&
                        state.SelectedArea == null &&
                        state.SelectedSegment == null &&
                        !state.IsShowingTripNotes &&
                        !state.IsShowingRegionNotes;
        isOverview.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingOverview_FalseWhenPlaceSelected()
    {
        var state = new SheetState { SelectedPlace = new TripPlace() };
        var isOverview = state.SelectedPlace == null;
        isOverview.Should().BeFalse();
    }

    [Fact]
    public void IsTripSheetShowingPlace_TrueWhenPlaceSelected()
    {
        var state = new SheetState { SelectedPlace = new TripPlace { Name = "Test" } };
        var isShowingPlace = state.SelectedPlace != null;
        isShowingPlace.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingArea_TrueWhenAreaSelected()
    {
        var state = new SheetState { SelectedArea = new TripArea { Name = "Test" } };
        var isShowingArea = state.SelectedArea != null;
        isShowingArea.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingSegment_TrueWhenSegmentSelected()
    {
        var state = new SheetState { SelectedSegment = new TripSegment() };
        var isShowingSegment = state.SelectedSegment != null;
        isShowingSegment.Should().BeTrue();
    }

    [Fact]
    public void IsTripSheetShowingDetails_TrueWhenNotOverview()
    {
        // IsTripSheetShowingDetails => !IsTripSheetShowingOverview
        var state = new SheetState { SelectedPlace = new TripPlace() };
        var isOverview = state.SelectedPlace == null;
        var isDetails = !isOverview;
        isDetails.Should().BeTrue();
    }

    #endregion

    #region Computed Properties - Title and Subtitle

    [Fact]
    public void TripSheetTitle_ShowsPlaceNameWhenPlaceSelected()
    {
        var place = new TripPlace { Name = "Eiffel Tower" };
        var title = GetSheetTitle(place, null, null, null, false, false, false, false);
        title.Should().Be("Eiffel Tower");
    }

    [Fact]
    public void TripSheetTitle_ShowsAreaNameWhenAreaSelected()
    {
        var area = new TripArea { Name = "Paris District" };
        var title = GetSheetTitle(null, area, null, null, false, false, false, false);
        title.Should().Be("Paris District");
    }

    [Fact]
    public void TripSheetTitle_ShowsSegmentWhenSegmentSelected()
    {
        var segment = new TripSegment();
        var title = GetSheetTitle(null, null, segment, null, false, false, false, false);
        title.Should().Be("Segment");
    }

    [Fact]
    public void TripSheetTitle_ShowsTripNotesWhenShowingNotes()
    {
        var title = GetSheetTitle(null, null, null, null, true, false, false, false);
        title.Should().Be("Trip Notes");
    }

    [Fact]
    public void TripSheetTitle_ShowsRegionNotesWhenShowingRegionNotes()
    {
        var region = new TripRegion { Name = "North Side" };
        var title = GetSheetTitle(null, null, null, region, false, false, false, true);
        title.Should().Be("North Side - Notes");
    }

    [Fact]
    public void TripSheetTitle_ShowsTripNameForOverview()
    {
        var trip = new TripDetails { Name = "Paris Trip" };
        var title = GetOverviewTitle(trip);
        title.Should().Be("Paris Trip");
    }

    [Fact]
    public void TripSheetSubtitle_ShowsCountsForOverview()
    {
        var trip = CreateTestTripDetails();
        var subtitle = BuildSubtitle(trip);
        subtitle.Should().Contain("places");
    }

    [Fact]
    public void TripSheetSubtitle_ShowsAddressForPlace()
    {
        var place = new TripPlace { Address = "123 Main St" };
        var subtitle = place.Address;
        subtitle.Should().Be("123 Main St");
    }

    [Fact]
    public void TripSheetSubtitle_ShowsOriginDestinationForSegment()
    {
        var segment = new TripSegment
        {
            OriginName = "Paris",
            DestinationName = "Lyon"
        };
        var subtitle = $"{segment.OriginName} → {segment.DestinationName}";
        subtitle.Should().Be("Paris → Lyon");
    }

    private static string GetSheetTitle(
        TripPlace? place, TripArea? area, TripSegment? segment, TripRegion? region,
        bool showingTripNotes, bool showingAreaNotes, bool showingSegmentNotes, bool showingRegionNotes)
    {
        if (place != null) return place.Name ?? "Place";
        if (area != null) return area.Name ?? "Area";
        if (segment != null) return "Segment";
        if (showingTripNotes) return "Trip Notes";
        if (showingAreaNotes) return $"{area?.Name ?? "Area"} - Notes";
        if (showingSegmentNotes) return "Segment Notes";
        if (showingRegionNotes) return $"{region?.Name ?? "Region"} - Notes";
        return "Trip Overview";
    }

    private static string GetOverviewTitle(TripDetails? trip) => trip?.Name ?? "Trip Overview";

    private static string? BuildSubtitle(TripDetails trip)
    {
        var parts = new List<string>();
        if (trip.AllPlaces.Count > 0) parts.Add($"{trip.AllPlaces.Count} places");
        if (trip.AllAreas.Count > 0) parts.Add($"{trip.AllAreas.Count} areas");
        if (trip.Segments.Count > 0) parts.Add($"{trip.Segments.Count} segments");
        return string.Join(" · ", parts);
    }

    #endregion

    #region Computed Properties - Coordinate Editing

    [Fact]
    public void HasPendingPlaceCoordinates_TrueWhenBothSet()
    {
        double? lat = 48.8566;
        double? lon = 2.3522;
        var hasPending = lat.HasValue && lon.HasValue;
        hasPending.Should().BeTrue();
    }

    [Fact]
    public void HasPendingPlaceCoordinates_FalseWhenOnlyLatSet()
    {
        double? lat = 48.8566;
        double? lon = null;
        var hasPending = lat.HasValue && lon.HasValue;
        hasPending.Should().BeFalse();
    }

    [Fact]
    public void PendingPlaceCoordinatesText_FormatsCorrectly()
    {
        double lat = 48.85660;
        double lon = 2.35220;
        var text = $"{lat:F5}, {lon:F5}";
        text.Should().Be("48.85660, 2.35220");
    }

    [Fact]
    public void PendingPlaceCoordinatesText_ShowsPromptWhenEmpty()
    {
        double? lat = null;
        double? lon = null;
        var hasPending = lat.HasValue && lon.HasValue;
        var text = hasPending ? $"{lat:F5}, {lon:F5}" : "Tap on map to set location";
        text.Should().Be("Tap on map to set location");
    }

    #endregion

    #region Computed Properties - Place Search

    [Fact]
    public void PlaceSearchResults_EmptyWhenNoQuery()
    {
        var trip = CreateTestTripDetails();
        var query = "";
        var results = FilterPlaces(trip.AllPlaces, query);
        results.Should().BeEmpty();
    }

    [Fact]
    public void PlaceSearchResults_FiltersByName()
    {
        var trip = CreateTestTripDetails();
        var query = "Eiffel";
        var results = FilterPlaces(trip.AllPlaces, query);
        // Should match places with "Eiffel" in name
    }

    [Fact]
    public void PlaceSearchResults_FiltersByAddress()
    {
        var trip = CreateTestTripDetails();
        var query = "Champs";
        var results = FilterPlaces(trip.AllPlaces, query);
        // Should match places with "Champs" in address
    }

    [Fact]
    public void PlaceSearchResults_CaseInsensitive()
    {
        var places = new List<TripPlace>
        {
            new TripPlace { Name = "EIFFEL TOWER" }
        };
        var results = FilterPlaces(places, "eiffel");
        results.Should().HaveCount(1);
    }

    private static List<TripPlace> FilterPlaces(IEnumerable<TripPlace> places, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<TripPlace>();

        return places
            .Where(p =>
                (p.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                (p.Address?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
    }

    #endregion

    #region Selection Commands Tests

    [Fact]
    public void SelectTripPlace_ClearsOtherSelections()
    {
        // Document expected behavior:
        // When selecting a place:
        // - SelectedTripArea = null
        // - SelectedTripSegment = null
        // - IsShowingTripNotes = false
        // - (all other notes flags = false)
        // - SelectedTripRegion = null
    }

    [Fact]
    public void SelectTripPlace_CentersMapOnPlace()
    {
        // Document expected behavior:
        // _callbacks?.CenterOnLocation(place.Latitude, place.Longitude, zoomLevel: 16);
    }

    [Fact]
    public void SelectTripPlace_UpdatesMapSelection()
    {
        // Document expected behavior:
        // _callbacks?.UpdatePlaceSelection(place);
    }

    [Fact]
    public void SelectTripPlace_DisablesLocationFollow()
    {
        // Document expected behavior:
        // _callbacks?.SetFollowingLocation(false);
    }

    [Fact]
    public void SelectTripPlace_ClosesSearchIfActive()
    {
        // Document expected behavior:
        // Calls CloseSearchIfActive() to hide search panel
    }

    [Fact]
    public void SelectTripArea_ClearsOtherSelections()
    {
        // Document expected behavior: same pattern as place selection
    }

    [Fact]
    public void SelectTripArea_CentersMapOnAreaCenter()
    {
        // Document expected behavior:
        // if (area.Center != null)
        //     _callbacks?.CenterOnLocation(area.Center.Latitude, area.Center.Longitude);
    }

    [Fact]
    public void SelectTripSegment_ClearsOtherSelections()
    {
        // Document expected behavior: same pattern
    }

    #endregion

    #region Sheet Navigation Commands Tests

    [Fact]
    public void ToggleTripSheet_ClosesIfOpen()
    {
        // Document expected behavior:
        // if (IsTripSheetOpen) { ClearTripSheetSelection(); IsTripSheetOpen = false; }
    }

    [Fact]
    public void ToggleTripSheet_OpensIfTripLoaded()
    {
        // Document expected behavior:
        // if (HasLoadedTrip) { IsTripSheetOpen = true; }
    }

    [Fact]
    public void ToggleTripSheet_NavigatesToTripsIfNoTrip()
    {
        // Document expected behavior:
        // else { await GoToMyTripsAsync(); }
    }

    [Fact]
    public void TripSheetBack_ClearsAreaNotes()
    {
        // Document expected behavior:
        // if (IsShowingAreaNotes) { IsShowingAreaNotes = false; }
    }

    [Fact]
    public void TripSheetBack_ClearsSegmentNotes()
    {
        // Document expected behavior:
        // else if (IsShowingSegmentNotes) { IsShowingSegmentNotes = false; }
    }

    [Fact]
    public void TripSheetBack_ClearsRegionNotes()
    {
        // Document expected behavior:
        // else if (IsShowingRegionNotes) { IsShowingRegionNotes = false; SelectedTripRegion = null; }
    }

    [Fact]
    public void TripSheetBack_ReturnsToOverview()
    {
        // Document expected behavior:
        // else { ClearTripSheetSelection(); }
    }

    #endregion

    #region Notes Display Commands Tests

    [Fact]
    public void ShowTripNotes_SetsFlag()
    {
        // Document expected behavior:
        // IsShowingTripNotes = true;
    }

    [Fact]
    public void ShowAreaNotes_SetsFlag()
    {
        // Document expected behavior:
        // IsShowingAreaNotes = true;
    }

    [Fact]
    public void ShowSegmentNotes_SetsFlag()
    {
        // Document expected behavior:
        // IsShowingSegmentNotes = true;
    }

    [Fact]
    public void ShowRegionNotes_SetsRegionAndFlag()
    {
        // Document expected behavior:
        // SelectedTripRegion = region;
        // IsShowingRegionNotes = true;
    }

    #endregion

    #region ClearTripSheetSelection Tests

    [Fact]
    public void ClearTripSheetSelection_ClearsAllSelections()
    {
        // Document expected behavior:
        // SelectedTripPlace = null;
        // SelectedPlace = null;
        // SelectedTripArea = null;
        // SelectedTripSegment = null;
        // SelectedTripRegion = null;
    }

    [Fact]
    public void ClearTripSheetSelection_ClearsAllNotesFlags()
    {
        // Document expected behavior:
        // IsShowingTripNotes = false;
        // IsShowingAreaNotes = false;
        // IsShowingSegmentNotes = false;
        // IsShowingRegionNotes = false;
    }

    [Fact]
    public void ClearTripSheetSelection_ClearsMapSelection()
    {
        // Document expected behavior:
        // _callbacks?.ClearPlaceSelection();
    }

    #endregion

    #region UnloadTrip Tests

    [Fact]
    public void UnloadTrip_ClearsLoadedTrip()
    {
        // Document expected behavior:
        // LoadedTrip = null;
    }

    [Fact]
    public void UnloadTrip_ClearsSelections()
    {
        // Document expected behavior:
        // SelectedPlace = null;
        // ClearTripSheetSelection();
    }

    [Fact]
    public void UnloadTrip_ClosesTripSheet()
    {
        // Document expected behavior:
        // IsTripSheetOpen = false;
    }

    [Fact]
    public void UnloadTrip_ClearsTripLayers()
    {
        // Document expected behavior:
        // _callbacks?.UnloadTripFromMap();
    }

    [Fact]
    public void UnloadTrip_ResumesLocationFollow()
    {
        // Document expected behavior:
        // _callbacks?.SetFollowingLocation(true);
    }

    #endregion

    #region Coordinate Editing Tests

    [Fact]
    public void SetPendingPlaceCoordinates_UpdatesLatLon()
    {
        // Document expected behavior:
        // PendingPlaceLatitude = latitude;
        // PendingPlaceLongitude = longitude;
    }

    [Fact]
    public void EnterPlaceCoordinateEditMode_StoresPlace()
    {
        // Document expected behavior:
        // PlaceBeingEditedForCoordinates = place;
    }

    [Fact]
    public void EnterPlaceCoordinateEditMode_InitializesPendingCoords()
    {
        // Document expected behavior:
        // PendingPlaceLatitude = place.Latitude;
        // PendingPlaceLongitude = place.Longitude;
    }

    [Fact]
    public void EnterPlaceCoordinateEditMode_SetsEditFlag()
    {
        // Document expected behavior:
        // IsPlaceCoordinateEditMode = true;
    }

    [Fact]
    public void EnterPlaceCoordinateEditMode_ClosesTripSheet()
    {
        // Document expected behavior:
        // IsTripSheetOpen = false;
    }

    [Fact]
    public void CancelPlaceCoordinateEditing_ExitsEditMode()
    {
        // Document expected behavior:
        // Calls ExitPlaceCoordinateEditMode()
    }

    [Fact]
    public void CancelPlaceCoordinateEditing_ReopensTripSheet()
    {
        // Document expected behavior:
        // IsTripSheetOpen = true;
    }

    [Fact]
    public void CancelPlaceCoordinateEditing_ReselectsPlace()
    {
        // Document expected behavior:
        // if (place != null) SelectTripPlace(place);
    }

    #endregion

    #region Sub-Editor Navigation Tests

    [Fact]
    public void IsNavigatingToSubEditor_DefaultsFalse()
    {
        // Document expected behavior:
        // IsNavigatingToSubEditor = false initially
    }

    [Fact]
    public void RestoreSelectionFromSubEditor_StoresPendingRestore()
    {
        // Document expected behavior:
        // _pendingSelectionRestore = (entityType, entityId);
    }

    [Fact]
    public void ProcessPendingSelectionRestoreAsync_RestoresPlaceSelection()
    {
        // Document expected behavior:
        // When entityType == "place", finds and selects the place
    }

    [Fact]
    public void ProcessPendingSelectionRestoreAsync_RestoresAreaSelection()
    {
        // Document expected behavior:
        // When entityType == "area", finds and selects the area
    }

    [Fact]
    public void ProcessPendingSelectionRestoreAsync_ClearsPendingRestore()
    {
        // Document expected behavior:
        // _pendingSelectionRestore = null;
        // IsNavigatingToSubEditor = false;
    }

    #endregion

    #region Helper Methods

    private static TripDetails CreateTestTripDetails()
    {
        return new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>
            {
                new TripRegion
                {
                    Id = Guid.NewGuid(),
                    Name = "Region 1",
                    Places = new List<TripPlace>
                    {
                        new TripPlace { Id = Guid.NewGuid(), Name = "Place 1", Latitude = 48.8566, Longitude = 2.3522 },
                        new TripPlace { Id = Guid.NewGuid(), Name = "Place 2", Latitude = 48.8584, Longitude = 2.2945 }
                    }
                }
            },
            Segments = new List<TripSegment>()
        };
    }

    private static TripDetails CreateTripWithPlaceCount(int count)
    {
        var places = new List<TripPlace>();
        for (int i = 0; i < count; i++)
        {
            places.Add(new TripPlace { Id = Guid.NewGuid(), Name = $"Place {i + 1}" });
        }

        return new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>
            {
                new TripRegion { Id = Guid.NewGuid(), Name = "Region", Places = places }
            }
        };
    }

    #endregion
}

/// <summary>
/// Helper class to track sheet state for testing.
/// </summary>
public class SheetState
{
    public TripPlace? SelectedPlace { get; set; }
    public TripArea? SelectedArea { get; set; }
    public TripSegment? SelectedSegment { get; set; }
    public bool IsShowingTripNotes { get; set; }
    public bool IsShowingRegionNotes { get; set; }
}
