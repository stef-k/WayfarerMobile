using CommunityToolkit.Mvvm.ComponentModel;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>One drawer row's transient map choice; never part of saved Trip data.</summary>
public sealed class SegmentMapRow : ObservableObject
{
    private readonly Action<SegmentMapRow> _visibilityChanged;
    private bool _showOnMap;

    public TripSegment Segment { get; }

    public bool ShowOnMap
    {
        get => _showOnMap;
        set
        {
            if (SetProperty(ref _showOnMap, value))
                _visibilityChanged(this);
        }
    }

    public SegmentMapRow(TripSegment segment, bool showOnMap, Action<SegmentMapRow> visibilityChanged)
    {
        Segment = segment;
        _showOnMap = showOnMap;
        _visibilityChanged = visibilityChanged;
    }
}
