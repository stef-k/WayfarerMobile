using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WayfarerMobile.Core.Enums;
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
/// Uses <see cref="UnifiedDownloadState"/> as the single source of truth for download state.
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
    /// Gets the last updated date.
    /// </summary>
    public DateTime UpdatedAt { get; }

    /// <summary>
    /// Gets the formatted updated date text.
    /// </summary>
    public string UpdatedAtText
    {
        get
        {
            var local = UpdatedAt.ToLocalTime();
            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);

            if (local.Date == today)
                return $"Updated today at {local:HH:mm}";
            if (local.Date == yesterday)
                return $"Updated yesterday at {local:HH:mm}";
            if (local.Date > today.AddDays(-7))
                return $"Updated {local:dddd} at {local:HH:mm}";

            return $"Updated {local:MMM d, yyyy}";
        }
    }

    /// <summary>
    /// Gets the stats text (dynamically calculated based on download state).
    /// Shows regions, places, segments, areas, and tiles.
    /// </summary>
    public string StatsText
    {
        get
        {
            if (UnifiedState.IsDownloading())
                return IsDownloading ? "Downloading..." : "Paused";

            if (UnifiedState.IsPaused())
                return "Paused";

            if (UnifiedState == UnifiedDownloadState.Failed)
                return "Failed";

            if (UnifiedState == UnifiedDownloadState.ServerOnly)
                return _serverStatsText ?? "Available online";

            // For downloaded trips (MetadataOnly or Complete), show detailed stats
            if (DownloadedEntity == null)
                return UnifiedState == UnifiedDownloadState.Complete ? "Downloaded" : "Metadata only";

            var parts = new List<string>();

            if (DownloadedEntity.RegionCount > 0)
                parts.Add($"{DownloadedEntity.RegionCount} region{(DownloadedEntity.RegionCount == 1 ? "" : "s")}");

            if (DownloadedEntity.PlaceCount > 0)
                parts.Add($"{DownloadedEntity.PlaceCount} place{(DownloadedEntity.PlaceCount == 1 ? "" : "s")}");

            if (DownloadedEntity.SegmentCount > 0)
                parts.Add($"{DownloadedEntity.SegmentCount} segment{(DownloadedEntity.SegmentCount == 1 ? "" : "s")}");

            if (DownloadedEntity.AreaCount > 0)
                parts.Add($"{DownloadedEntity.AreaCount} area{(DownloadedEntity.AreaCount == 1 ? "" : "s")}");

            if (UnifiedState == UnifiedDownloadState.Complete && DownloadedEntity.TileCount > 0)
                parts.Add($"{DownloadedEntity.TileCount} tiles");
            else if (UnifiedState == UnifiedDownloadState.MetadataOnly)
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
    /// Gets or sets the unified download state.
    /// This is the single source of truth for download state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloaded))]
    [NotifyPropertyChangedFor(nameof(IsMetadataOnly))]
    [NotifyPropertyChangedFor(nameof(IsServerOnly))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(CanDeleteTilesOnly))]
    [NotifyPropertyChangedFor(nameof(GroupName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    [NotifyPropertyChangedFor(nameof(CanQuickDownload))]
    [NotifyPropertyChangedFor(nameof(CanFullDownload))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private UnifiedDownloadState _unifiedState;

    /// <summary>
    /// Gets or sets the downloaded entity (for stats updates).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    [NotifyPropertyChangedFor(nameof(CanDeleteTilesOnly))]
    [NotifyPropertyChangedFor(nameof(IsMetadataComplete))]
    [NotifyPropertyChangedFor(nameof(HasTiles))]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private Data.Entities.DownloadedTripEntity? _downloadedEntity;

    /// <summary>
    /// Gets whether the trip is fully downloaded (Complete state).
    /// </summary>
    public bool IsDownloaded => UnifiedState == UnifiedDownloadState.Complete;

    /// <summary>
    /// Gets whether the trip has metadata only.
    /// </summary>
    public bool IsMetadataOnly => UnifiedState == UnifiedDownloadState.MetadataOnly;

    /// <summary>
    /// Gets whether the trip is on server only.
    /// </summary>
    public bool IsServerOnly => UnifiedState == UnifiedDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether the download is paused.
    /// </summary>
    public bool IsPaused => UnifiedState.IsPaused();

    /// <summary>
    /// Gets whether the download has failed.
    /// </summary>
    public bool IsFailed => UnifiedState == UnifiedDownloadState.Failed;

    /// <summary>
    /// Gets whether metadata is complete for this trip.
    /// </summary>
    public bool IsMetadataComplete => DownloadedEntity?.IsMetadataComplete ?? false;

    /// <summary>
    /// Gets whether this trip has downloaded tiles.
    /// </summary>
    public bool HasTiles => DownloadedEntity?.HasTiles ?? false;

    /// <summary>
    /// Gets whether the trip has any local data that can be deleted.
    /// Includes complete downloads, metadata only, paused, and failed downloads.
    /// </summary>
    public bool CanDelete => UnifiedState.HasLocalData();

    /// <summary>
    /// Gets the group name for this trip.
    /// Uses the unified state extension method.
    /// </summary>
    public string GroupName => UnifiedState.GetGroupName();

    /// <summary>
    /// Gets the status text.
    /// Uses the unified state extension method.
    /// </summary>
    public string StatusText => UnifiedState.GetStatusText();

    /// <summary>
    /// Gets the status color.
    /// </summary>
    public Color StatusColor => UnifiedState switch
    {
        UnifiedDownloadState.Complete => Colors.Green,
        UnifiedDownloadState.MetadataOnly => Colors.Orange,
        UnifiedDownloadState.DownloadingMetadata => Colors.Blue,
        UnifiedDownloadState.DownloadingTiles => Colors.Blue,
        UnifiedDownloadState.PausedByUser => Colors.DarkOrange,
        UnifiedDownloadState.PausedNetworkLost => Colors.OrangeRed,
        UnifiedDownloadState.PausedStorageLow => Colors.OrangeRed,
        UnifiedDownloadState.PausedCacheLimit => Colors.OrangeRed,
        UnifiedDownloadState.Failed => Colors.Red,
        UnifiedDownloadState.Cancelled => Colors.Gray,
        _ => Colors.Gray
    };

    /// <summary>
    /// Gets whether Load to Map is available.
    /// Requires: not currently loaded, state allows loading, AND metadata exists.
    /// </summary>
    public bool CanLoadToMap => !IsCurrentlyLoaded &&
                                UnifiedState.CanLoadToMap() &&
                                IsMetadataComplete;

    /// <summary>
    /// Gets or sets whether this trip is currently loaded on the map.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadToMap))]
    private bool _isCurrentlyLoaded;

    /// <summary>
    /// Gets whether Quick Download is available (metadata only).
    /// </summary>
    public bool CanQuickDownload => UnifiedState == UnifiedDownloadState.ServerOnly;

    /// <summary>
    /// Gets whether Full Download (with tiles) is available.
    /// </summary>
    public bool CanFullDownload => UnifiedState.CanDownloadTiles();

    /// <summary>
    /// Gets whether the download can be paused.
    /// </summary>
    public bool CanPause => UnifiedState.CanPause();

    /// <summary>
    /// Gets whether the download can be resumed.
    /// </summary>
    public bool CanResume => UnifiedState.CanResume();

    /// <summary>
    /// Gets whether Delete Tiles Only is available.
    /// Only available for trips with offline maps (Complete state with tiles).
    /// </summary>
    public bool CanDeleteTilesOnly => UnifiedState.CanDeleteTilesOnly() &&
                                       (DownloadedEntity?.TileCount ?? 0) > 0;

    /// <summary>
    /// Gets whether editing is available.
    /// Only available for downloaded trips that have metadata.
    /// </summary>
    public bool CanEdit => UnifiedState.HasMetadata();

    /// <summary>
    /// Gets or sets whether actively downloading.
    /// True only when download is in progress (not paused).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress (0.0-1.0).
    /// </summary>
    [ObservableProperty]
    private double _downloadProgress;

    /// <summary>
    /// Creates a new trip list item from a trip summary and optional downloaded entity.
    /// </summary>
    public TripListItem(TripSummary trip, Data.Entities.DownloadedTripEntity? downloaded)
    {
        ServerId = trip.Id;
        Name = trip.Name;
        UpdatedAt = trip.UpdatedAt;
        BoundingBox = trip.BoundingBox;

        // Cache server stats for fallback
        _serverStatsText = trip.PlacesCount > 0 ? trip.StatsText : null;
        _downloadedEntity = downloaded;

        // Use unified state from entity, or ServerOnly if no entity
        _unifiedState = downloaded?.UnifiedState ?? UnifiedDownloadState.ServerOnly;

        // Set IsDownloading based on active download states
        _isDownloading = _unifiedState.IsDownloading();
    }

    /// <summary>
    /// Updates the unified state and related properties.
    /// Call this when receiving state change events.
    /// </summary>
    /// <remarks>
    /// The isMetadataComplete and hasTiles parameters are provided for context but the
    /// actual values come from the entity's computed properties which are based on counts.
    /// </remarks>
    public void UpdateState(UnifiedDownloadState newState, bool isMetadataComplete, bool hasTiles)
    {
        UnifiedState = newState;
        IsDownloading = newState.IsDownloading();

        // Update entity state if present
        // Note: IsMetadataComplete and HasTiles are computed from entity counts, not directly set
        if (DownloadedEntity != null)
        {
            DownloadedEntity.UnifiedState = newState;
        }

        // Force property change notifications for computed properties that depend on entity state
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(IsMetadataComplete));
        OnPropertyChanged(nameof(HasTiles));
        OnPropertyChanged(nameof(CanLoadToMap));
    }
}
