using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.ViewModels;

public sealed class TripGrouping(string name, IEnumerable<TripListItem> items) : ObservableCollection<TripListItem>(items)
{
    public string Name { get; } = name;
}

/// <summary>Presentation state for online and downloaded provider-independent Trips.</summary>
public partial class TripListItem : ObservableObject
{
    public Guid ServerId { get; }
    public string Name { get; }
    public DateTime UpdatedAt { get; }
    public TripSummary? ServerTrip { get; }
    public BoundingBox? BoundingBox { get; }
    private readonly string? _serverStats;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    [NotifyPropertyChangedFor(nameof(CanQuickDownload))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private UnifiedDownloadState _unifiedState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private DownloadedTripEntity? _downloadedEntity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private bool _isCurrentlyLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private bool _isDownloading;

    [ObservableProperty] private double _downloadProgress;

    public string UpdatedAtText => $"Updated {UpdatedAt.ToLocalTime():MMM d, yyyy}";
    public string GroupName => UnifiedState.GetGroupName();
    public string StatusText => UnifiedState.GetStatusText();
    public Color StatusColor => UnifiedState switch
    {
        UnifiedDownloadState.Downloaded => Colors.Green,
        UnifiedDownloadState.Downloading => Colors.Blue,
        UnifiedDownloadState.Failed => Colors.Red,
        _ => Colors.Gray
    };
    public bool CanDelete => UnifiedState.HasLocalData();
    public bool CanLoadToMap => !IsCurrentlyLoaded && UnifiedState.CanLoadToMap() && (DownloadedEntity?.IsMetadataComplete ?? false);
    public bool CanQuickDownload => UnifiedState is UnifiedDownloadState.ServerOnly or UnifiedDownloadState.Failed;
    public bool CanEdit => UnifiedState.HasMetadata();
    public string StatsText
    {
        get
        {
            if (UnifiedState == UnifiedDownloadState.Downloading) return "Downloading…";
            if (UnifiedState == UnifiedDownloadState.Failed) return "Failed";
            if (DownloadedEntity is null) return _serverStats ?? "Available online";
            var parts = new List<string>();
            if (DownloadedEntity.RegionCount > 0) parts.Add($"{DownloadedEntity.RegionCount} regions");
            if (DownloadedEntity.PlaceCount > 0) parts.Add($"{DownloadedEntity.PlaceCount} places");
            if (DownloadedEntity.SegmentCount > 0) parts.Add($"{DownloadedEntity.SegmentCount} routes");
            if (DownloadedEntity.AreaCount > 0) parts.Add($"{DownloadedEntity.AreaCount} areas");
            return parts.Count == 0 ? "Trip data downloaded" : string.Join(" • ", parts);
        }
    }

    public TripListItem(TripSummary trip, DownloadedTripEntity? downloaded)
    {
        ServerTrip = trip;
        ServerId = trip.Id;
        Name = trip.Name;
        UpdatedAt = trip.UpdatedAt;
        BoundingBox = trip.BoundingBox;
        _serverStats = trip.PlacesCount > 0 ? trip.StatsText : null;
        _downloadedEntity = downloaded;
        _unifiedState = downloaded?.UnifiedState ?? UnifiedDownloadState.ServerOnly;
        _isDownloading = _unifiedState.IsDownloading();
    }

    public TripListItem(DownloadedTripEntity downloaded)
    {
        ServerId = downloaded.ServerId;
        Name = downloaded.Name;
        UpdatedAt = downloaded.UpdatedAt;
        var box = new BoundingBox { North = downloaded.BoundingBoxNorth, South = downloaded.BoundingBoxSouth,
            East = downloaded.BoundingBoxEast, West = downloaded.BoundingBoxWest };
        BoundingBox = box.IsValid ? box : null;
        _downloadedEntity = downloaded;
        _unifiedState = downloaded.UnifiedState;
        _isDownloading = _unifiedState.IsDownloading();
    }

    public void UpdateState(UnifiedDownloadState state, bool _, bool __)
    {
        UnifiedState = state;
        IsDownloading = state.IsDownloading();
        if (DownloadedEntity is not null) DownloadedEntity.UnifiedState = state;
    }
}
