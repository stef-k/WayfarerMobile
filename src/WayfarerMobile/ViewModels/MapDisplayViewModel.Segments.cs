using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

public partial class MapDisplayViewModel
{
    /// <summary>Current Trip's Segment choices, shared by the drawer and ordinary map layers.</summary>
    public IReadOnlyList<SegmentMapRow> SegmentRows { get; private set; } = [];

    private void ReplaceSegmentRows(TripDetails? trip)
    {
        var previous = trip != null && trip.Id == _displayedTrip?.Id
            ? SegmentRows.ToDictionary(row => row.Segment.Id, row => row.ShowOnMap)
            : new Dictionary<Guid, bool>();
        SegmentRows = trip?.Segments.Select(segment => new SegmentMapRow(segment,
            previous.GetValueOrDefault(segment.Id, true), OnSegmentVisibilityChanged)).ToList() ?? [];
        OnPropertyChanged(nameof(SegmentRows));
    }

    private void OnSegmentVisibilityChanged(SegmentMapRow row)
    {
        // A detached drawer row cannot change a replacement Trip's presentation.
        if (!SegmentRows.Contains(row)) return;
        RefreshOrdinarySegments();
        RefreshSelectedSegmentDecorations();
    }

    private void RefreshOrdinarySegments()
    {
        if (_tripSegmentsLayer != null && _displayedTrip != null)
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer,
                SegmentRows.Where(row => row.ShowOnMap).Select(row => row.Segment), _displayedTrip.AllPlaces);
    }

    private TripSegment? VisibleSelectedSegment() => SegmentRows
        .FirstOrDefault(row => row.ShowOnMap && row.Segment.Id == _selectedSegment?.Id)?.Segment;
}
