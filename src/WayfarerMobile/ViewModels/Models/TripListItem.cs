using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Grouping of trips by status.
/// </summary>
public class TripGrouping : ObservableCollection<TripListItem>
{
    /// <summary>
    /// Gets the group name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a new trip grouping.
    /// </summary>
    public TripGrouping(string name, IEnumerable<TripListItem> items) : base(items)
    {
        Name = name;
    }
}

/// <summary>
/// Trip list item with download status.
/// </summary>
public partial class TripListItem : ObservableObject
{
    /// <summary>
    /// Gets the server ID.
    /// </summary>
    public Guid ServerId { get; }

    /// <summary>
    /// Gets the trip name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the stats text (dynamically calculated based on download state).
    /// Shows regions, places, segments, areas, and tiles.
    /// </summary>
    public string StatsText
    {
        get
        {
            if (DownloadState == TripDownloadState.Downloading)
                return "Downloading...";

            if (DownloadState == TripDownloadState.ServerOnly)
                return _serverStatsText ?? "Available online";

            // For downloaded trips (MetadataOnly or Complete), show detailed stats
            if (DownloadedEntity == null)
                return DownloadState == TripDownloadState.Complete ? "Downloaded" : "Metadata only";

            var parts = new List<string>();

            if (DownloadedEntity.RegionCount > 0)
                parts.Add($"{DownloadedEntity.RegionCount} region{(DownloadedEntity.RegionCount == 1 ? "" : "s")}");

            if (DownloadedEntity.PlaceCount > 0)
                parts.Add($"{DownloadedEntity.PlaceCount} place{(DownloadedEntity.PlaceCount == 1 ? "" : "s")}");

            if (DownloadedEntity.SegmentCount > 0)
                parts.Add($"{DownloadedEntity.SegmentCount} segment{(DownloadedEntity.SegmentCount == 1 ? "" : "s")}");

            if (DownloadedEntity.AreaCount > 0)
                parts.Add($"{DownloadedEntity.AreaCount} area{(DownloadedEntity.AreaCount == 1 ? "" : "s")}");

            if (DownloadState == TripDownloadState.Complete && DownloadedEntity.TileCount > 0)
                parts.Add($"{DownloadedEntity.TileCount} tiles");
            else if (DownloadState == TripDownloadState.MetadataOnly)
                parts.Add("No tiles");

            return parts.Count > 0 ? string.Join(" • ", parts) : "Empty trip";
        }
    }

    /// <summary>
    /// Server stats text (cached from initial load).
    /// </summary>
    private readonly string? _serverStatsText;

    /// <summary>
    /// Gets the bounding box (for download).
    /// </summary>
    public BoundingBox? BoundingBox { get; }

    /// <summary>
    /// Gets or sets the download status.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloaded))]
    [NotifyPropertyChangedFor(nameof(IsMetadataOnly))]
    [NotifyPropertyChangedFor(nameof(IsServerOnly))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanDeleteTilesOnly))]
    [NotifyPropertyChangedFor(nameof(GroupName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    [NotifyPropertyChangedFor(nameof(CanQuickDownload))]
    [NotifyPropertyChangedFor(nameof(CanFullDownload))]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private TripDownloadState _downloadState;

    /// <summary>
    /// Gets or sets the downloaded entity (for stats updates).
    /// </summary>
    public Data.Entities.DownloadedTripEntity? DownloadedEntity { get; set; }

    /// <summary>
    /// Gets whether the trip is downloaded.
    /// </summary>
    public bool IsDownloaded => DownloadState == TripDownloadState.Complete;

    /// <summary>
    /// Gets whether the trip has metadata only.
    /// </summary>
    public bool IsMetadataOnly => DownloadState == TripDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether the trip is on server only.
    /// </summary>
    public bool IsServerOnly => DownloadState == TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether the trip has any local data that can be deleted.
    /// Includes complete downloads, metadata only, and failed/stuck downloads.
    /// </summary>
    public bool CanDelete => DownloadState != TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets the group name for this trip.
    /// </summary>
    public string GroupName => DownloadState switch
    {
        TripDownloadState.Complete => "Downloaded",
        TripDownloadState.MetadataOnly => "Metadata Only",
        _ => "Available on Server"
    };

    /// <summary>
    /// Gets the status text.
    /// </summary>
    public string StatusText => DownloadState switch
    {
        TripDownloadState.Complete => "Offline",
        TripDownloadState.MetadataOnly => "Metadata",
        TripDownloadState.Downloading => "Downloading...",
        _ => "Online"
    };

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public Color StatusColor => DownloadState switch
    {
        TripDownloadState.Complete => Colors.Green,
        TripDownloadState.MetadataOnly => Colors.Orange,
        TripDownloadState.Downloading => Colors.Blue,
        _ => Colors.Gray
    };

    /// <summary>
    /// Gets whether Load to Map is available.
    /// Only available for downloaded trips (metadata or complete) that aren't already loaded.
    /// </summary>
    public bool CanLoadToMap => !IsCurrentlyLoaded &&
                                 (DownloadState == TripDownloadState.MetadataOnly ||
                                  DownloadState == TripDownloadState.Complete);

    /// <summary>
    /// Gets or sets whether this trip is currently loaded on the map.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private bool _isCurrentlyLoaded;

    /// <summary>
    /// Gets whether Quick Download is available.
    /// </summary>
    public bool CanQuickDownload => DownloadState == TripDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether Full Download is available.
    /// </summary>
    public bool CanFullDownload => DownloadState == TripDownloadState.ServerOnly || DownloadState == TripDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether Delete Tiles Only is available.
    /// Only available for trips with offline maps (Complete state with tiles).
    /// </summary>
    public bool CanDeleteTilesOnly => DownloadState == TripDownloadState.Complete &&
                                       (DownloadedEntity?.TileCount ?? 0) > 0;

    /// <summary>
    /// Gets whether editing is available.
    /// Only available for downloaded trips (have local data to edit).
    /// </summary>
    public bool CanEdit => DownloadState == TripDownloadState.MetadataOnly ||
                            DownloadState == TripDownloadState.Complete;

    /// <summary>
    /// Gets or sets whether downloading.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress (0.0-1.0).
    /// </summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>
    /// Creates a new trip list item.
    /// </summary>
    public TripListItem(TripSummary trip, Data.Entities.DownloadedTripEntity? downloaded)
    {
        ServerId = trip.Id;
        Name = trip.Name;
        BoundingBox = trip.BoundingBox;

        // Cache server stats for fallback
        _serverStatsText = trip.PlacesCount > 0 ? trip.StatsText : null;
        DownloadedEntity = downloaded;

        // Determine download state
        if (downloaded == null)
        {
            _downloadState = TripDownloadState.ServerOnly;
        }
        else if (downloaded.Status == Data.Entities.TripDownloadStatus.Complete)
        {
            _downloadState = TripDownloadState.Complete;
        }
        else if (downloaded.Status == Data.Entities.TripDownloadStatus.MetadataOnly)
        {
            _downloadState = TripDownloadState.MetadataOnly;
        }
        else
        {
            _downloadState = TripDownloadState.Downloading;
        }
    }
}

/// <summary>
/// Trip download state.
/// </summary>
public enum TripDownloadState
{
    /// <summary>Trip exists only on server.</summary>
    ServerOnly,

    /// <summary>Trip metadata is downloaded but no tiles.</summary>
    MetadataOnly,

    /// <summary>Trip is being downloaded.</summary>
    Downloading,

    /// <summary>Trip is fully downloaded with tiles.</summary>
    Complete
}
