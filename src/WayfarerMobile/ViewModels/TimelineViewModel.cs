using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Controls;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Map = Mapsui.Map;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// View type for timeline navigation.
/// </summary>
public enum TimelineViewType
{
    /// <summary>Daily view showing all locations.</summary>
    Day,
    /// <summary>Monthly view with sampled locations.</summary>
    Month,
    /// <summary>Yearly view with sampled locations.</summary>
    Year
}

/// <summary>
/// ViewModel for the timeline page showing location history on a map.
/// </summary>
public partial class TimelineViewModel : BaseViewModel
{
    #region Constants

    private const string TimelineLayerName = "TimelineLocations";

    #endregion

    #region Fields

    private readonly IApiClient _apiClient;
    private readonly DatabaseService _database;
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private Map? _map;
    private WritableLayer? _timelineLayer;
    private List<TimelineLocation> _allLocations = new();

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the collection of timeline items grouped by day.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TimelineGroup> _timelineGroups = new();

    /// <summary>
    /// Gets or sets the selected date for filtering.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DateButtonText))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// Gets or sets whether data is being refreshed.
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Gets or sets whether there are no items.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Gets or sets the total count of locations.
    /// </summary>
    [ObservableProperty]
    private int _totalCount;

    /// <summary>
    /// Gets or sets the stats text displayed on the map overlay.
    /// </summary>
    [ObservableProperty]
    private string _statsText = "No data";

    /// <summary>
    /// Gets or sets whether the date picker popup is open.
    /// </summary>
    [ObservableProperty]
    private bool _isDatePickerOpen;

    /// <summary>
    /// Gets or sets the selected location for the details sheet.
    /// </summary>
    [ObservableProperty]
    private TimelineLocationDisplay? _selectedLocation;

    /// <summary>
    /// Gets or sets whether the location details sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isLocationSheetOpen;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets the date button text based on current date.
    /// </summary>
    public string DateButtonText
    {
        get
        {
            if (SelectedDate.Date == DateTime.Today)
                return "Today";
            if (SelectedDate.Date == DateTime.Today.AddDays(-1))
                return "Yesterday";
            return SelectedDate.ToString("ddd, MMM d");
        }
    }

    /// <summary>
    /// Gets whether the user can navigate to the next day (cannot go past today).
    /// </summary>
    public bool CanGoNext => SelectedDate.Date < DateTime.Today;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TimelineViewModel.
    /// </summary>
    /// <param name="apiClient">The API client.</param>
    /// <param name="database">The database service.</param>
    /// <param name="timelineSyncService">The timeline sync service.</param>
    /// <param name="toastService">The toast service.</param>
    public TimelineViewModel(
        IApiClient apiClient,
        DatabaseService database,
        ITimelineSyncService timelineSyncService,
        IToastService toastService)
    {
        _apiClient = apiClient;
        _database = database;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        Title = "Timeline";

        // Subscribe to sync events
        _timelineSyncService.SyncCompleted += OnSyncCompleted;
        _timelineSyncService.SyncQueued += OnSyncQueued;
        _timelineSyncService.SyncRejected += OnSyncRejected;
    }

    #endregion

    #region Map Creation

    /// <summary>
    /// Creates and configures the map instance.
    /// </summary>
    private Map CreateMap()
    {
        var map = new Map
        {
            CRS = "EPSG:3857" // Web Mercator
        };

        // Add OSM tile layer
        map.Layers.Add(Mapsui.Tiling.OpenStreetMap.CreateTileLayer());

        // Add timeline locations layer
        _timelineLayer = new WritableLayer
        {
            Name = TimelineLayerName,
            Style = null // Style is set per feature
        };
        map.Layers.Add(_timelineLayer);

        return map;
    }

    /// <summary>
    /// Ensures the map is initialized.
    /// </summary>
    public void EnsureMapInitialized()
    {
        _ = Map;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Loads timeline data from the server API.
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            IsRefreshing = true;

            // Fetch from server API (always day view)
            var response = await _apiClient.GetTimelineLocationsAsync(
                dateType: "day",
                year: SelectedDate.Year,
                month: SelectedDate.Month,
                day: SelectedDate.Day);

            if (response?.Data == null || !response.Data.Any())
            {
                _allLocations.Clear();
                TimelineGroups = new ObservableCollection<TimelineGroup>();
                TotalCount = 0;
                IsEmpty = true;
                StatsText = "No locations";
                UpdateMapLocations();
                return;
            }

            _allLocations = response.Data;

            // Group by hour for better organization (use LocalTimestamp for grouping)
            var groups = response.Data
                .GroupBy(l => l.LocalTimestamp.Hour)
                .OrderByDescending(g => g.Key)
                .Select(g => new TimelineGroup(
                    $"{g.Key:00}:00 - {g.Key:00}:59",
                    g.OrderByDescending(l => l.LocalTimestamp).ToList()))
                .ToList();

            TimelineGroups = new ObservableCollection<TimelineGroup>(groups);
            TotalCount = response.TotalItems;
            IsEmpty = !groups.Any();

            // Update stats
            StatsText = $"{TotalCount} location{(TotalCount == 1 ? "" : "s")}";

            // Update map
            UpdateMapLocations();
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Failed to load timeline: {ex.Message}");
            IsEmpty = true;
            StatsText = "Error loading data";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Updates the map layer with current locations.
    /// </summary>
    private void UpdateMapLocations()
    {
        if (_timelineLayer == null || _map == null)
            return;

        _timelineLayer.Clear();

        if (!_allLocations.Any())
        {
            _timelineLayer.DataHasChanged();
            return;
        }

        var points = new List<MPoint>();

        foreach (var location in _allLocations)
        {
            if (location.Coordinates == null)
                continue;

            var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            var point = new MPoint(x, y);
            points.Add(point);

            // Create marker
            var markerPoint = new Point(point.X, point.Y);
            var feature = new GeometryFeature(markerPoint)
            {
                Styles = new[] { CreateLocationMarkerStyle() }
            };

            // Store location ID for tap handling
            feature["LocationId"] = location.Id;

            _timelineLayer.Add(feature);
        }

        _timelineLayer.DataHasChanged();

        // Zoom to fit all locations with delay to ensure map is ready
        if (points.Count >= 1)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                if (points.Count > 1)
                {
                    var minX = points.Min(p => p.X);
                    var maxX = points.Max(p => p.X);
                    var minY = points.Min(p => p.Y);
                    var maxY = points.Max(p => p.Y);
                    var padding = Math.Max(maxX - minX, maxY - minY) * 0.2;
                    var extent = new MRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
                    _map?.Navigator.ZoomToBox(extent, MBoxFit.Fit);
                }
                else
                {
                    _map?.Navigator.CenterOn(points[0]);
                    if (_map?.Navigator.Resolutions?.Count > 15)
                    {
                        _map.Navigator.ZoomTo(_map.Navigator.Resolutions[15]);
                    }
                }
            });
        }
    }

    /// <summary>
    /// Creates the style for location markers.
    /// </summary>
    private static IStyle CreateLocationMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolScale = 0.5,
            Fill = new Brush(Color.FromArgb(255, 33, 150, 243)), // Blue
            Outline = new Pen(Color.White, 2),
            SymbolType = SymbolType.Ellipse
        };
    }

    /// <summary>
    /// Goes to the previous day.
    /// </summary>
    [RelayCommand]
    private async Task PreviousAsync()
    {
        SelectedDate = SelectedDate.AddDays(-1);
        await LoadDataAsync();
    }

    /// <summary>
    /// Goes to the next day (limited to today).
    /// </summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        if (CanGoNext)
        {
            SelectedDate = SelectedDate.AddDays(1);
            await LoadDataAsync();
        }
    }

    /// <summary>
    /// Opens the date picker.
    /// </summary>
    [RelayCommand]
    private void OpenDatePicker()
    {
        IsDatePickerOpen = true;
    }

    /// <summary>
    /// Handles date selection from picker.
    /// </summary>
    [RelayCommand]
    private async Task DateSelectedAsync(DateTime? date)
    {
        if (date.HasValue && date.Value.Date != SelectedDate.Date)
        {
            // Limit to today at most
            SelectedDate = date.Value.Date > DateTime.Today ? DateTime.Today : date.Value.Date;
            await LoadDataAsync();
        }
        IsDatePickerOpen = false;
    }

    /// <summary>
    /// Shows location details in the bottom sheet.
    /// </summary>
    /// <param name="locationId">The location ID to show.</param>
    public void ShowLocationDetails(int locationId)
    {
        var location = _allLocations.FirstOrDefault(l => l.Id == locationId);
        if (location == null)
            return;

        SelectedLocation = new TimelineLocationDisplay(location);
        IsLocationSheetOpen = true;
    }

    /// <summary>
    /// Closes the location details sheet.
    /// </summary>
    [RelayCommand]
    private void CloseLocationSheet()
    {
        IsLocationSheetOpen = false;
        SelectedLocation = null;
    }

    /// <summary>
    /// Opens the selected location in Google Maps.
    /// </summary>
    [RelayCommand]
    private async Task OpenInMapsAsync()
    {
        if (SelectedLocation == null)
            return;

        try
        {
            var location = new Microsoft.Maui.Devices.Sensors.Location(SelectedLocation.Latitude, SelectedLocation.Longitude);
            var options = new MapLaunchOptions { Name = $"Location at {SelectedLocation.TimeText}" };
            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TimelineViewModel] Failed to open maps: {ex.Message}");
            await _toastService.ShowErrorAsync("Could not open maps");
        }
    }

    /// <summary>
    /// Searches Wikipedia for the selected location.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        if (SelectedLocation == null)
            return;

        try
        {
            var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{SelectedLocation.Latitude},{SelectedLocation.Longitude}";
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TimelineViewModel] Failed to open Wikipedia: {ex.Message}");
            await _toastService.ShowErrorAsync("Could not open Wikipedia");
        }
    }

    /// <summary>
    /// Copies the selected location coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (SelectedLocation == null)
            return;

        try
        {
            var coords = $"{SelectedLocation.Latitude:F6}, {SelectedLocation.Longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            await _toastService.ShowAsync("Coordinates copied");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TimelineViewModel] Failed to copy coordinates: {ex.Message}");
            await _toastService.ShowErrorAsync("Could not copy coordinates");
        }
    }

    /// <summary>
    /// Shares the selected location.
    /// </summary>
    [RelayCommand]
    private async Task ShareLocationAsync()
    {
        if (SelectedLocation == null)
            return;

        try
        {
            var googleMapsUrl = $"https://www.google.com/maps?q={SelectedLocation.Latitude:F6},{SelectedLocation.Longitude:F6}";
            var text = $"Location from {SelectedLocation.TimeText} on {SelectedLocation.DateText}:\n{googleMapsUrl}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Location",
                Text = text
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TimelineViewModel] Failed to share: {ex.Message}");
            await _toastService.ShowErrorAsync("Could not share location");
        }
    }

    /// <summary>
    /// Opens the notes editor for the selected location.
    /// </summary>
    [RelayCommand]
    private void EditLocation()
    {
        // This is handled by code-behind to show the NotesEditorControl
    }

    /// <summary>
    /// Saves entry changes with optimistic UI update.
    /// </summary>
    /// <param name="e">The timeline entry update event args.</param>
    public async Task SaveEntryChangesAsync(TimelineEntryUpdateEventArgs e)
    {
        // Apply optimistic UI update
        if (SelectedLocation != null)
        {
            // Reload will update the display
        }

        // Sync to server (handles offline queueing automatically)
        await _timelineSyncService.UpdateLocationAsync(
            e.LocationId,
            e.Latitude,
            e.Longitude,
            e.LocalTimestamp,
            e.Notes,
            includeNotes: true);

        // Reload data to reflect changes
        await LoadDataAsync();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles sync completed event.
    /// </summary>
    private async void OnSyncCompleted(object? sender, SyncSuccessEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowSuccessAsync("Changes saved");
        });
    }

    /// <summary>
    /// Handles sync queued event (offline).
    /// </summary>
    private async void OnSyncQueued(object? sender, SyncQueuedEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowWarningAsync(e.Message);
        });
    }

    /// <summary>
    /// Handles sync rejected event (server error).
    /// </summary>
    private async void OnSyncRejected(object? sender, SyncFailureEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowErrorAsync($"Save failed: {e.ErrorMessage}");
        });
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        EnsureMapInitialized();
        await LoadDataAsync();
        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from sync events
        _timelineSyncService.SyncCompleted -= OnSyncCompleted;
        _timelineSyncService.SyncQueued -= OnSyncQueued;
        _timelineSyncService.SyncRejected -= OnSyncRejected;

        base.Cleanup();
    }

    #endregion
}

/// <summary>
/// Represents a group of timeline items (e.g., by hour).
/// </summary>
public class TimelineGroup : List<TimelineItem>
{
    /// <summary>
    /// Gets the group header text.
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Creates a new timeline group from server locations.
    /// </summary>
    /// <param name="header">The group header text.</param>
    /// <param name="locations">The locations in this group.</param>
    public TimelineGroup(string header, IEnumerable<TimelineLocation> locations) : base()
    {
        Header = header;
        AddRange(locations.Select(l => new TimelineItem(l)));
    }
}

/// <summary>
/// Represents a single timeline item for display.
/// </summary>
public class TimelineItem
{
    /// <summary>
    /// Gets the underlying location data from server.
    /// </summary>
    public TimelineLocation Location { get; }

    /// <summary>
    /// Gets the location ID.
    /// </summary>
    public int LocationId => Location.Id;

    /// <summary>
    /// Gets the formatted time.
    /// </summary>
    public string TimeText => Location.LocalTimestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Gets the formatted coordinates.
    /// </summary>
    public string CoordinatesText => Location.Coordinates != null
        ? $"{Location.Coordinates.Y:F6}, {Location.Coordinates.X:F6}"
        : "Unknown";

    /// <summary>
    /// Gets the latitude.
    /// </summary>
    public double? Latitude => Location.Coordinates?.Y;

    /// <summary>
    /// Gets the longitude.
    /// </summary>
    public double? Longitude => Location.Coordinates?.X;

    /// <summary>
    /// Gets the local timestamp.
    /// </summary>
    public DateTime LocalTimestamp => Location.LocalTimestamp;

    /// <summary>
    /// Gets the notes.
    /// </summary>
    public string? Notes => Location.Notes;

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => Location.Accuracy.HasValue
        ? $"~{Location.Accuracy.Value:F0}m"
        : "Unknown";

    /// <summary>
    /// Gets the accuracy indicator color.
    /// </summary>
    public Microsoft.Maui.Graphics.Color AccuracyColor => Location.Accuracy switch
    {
        null => Colors.Gray,
        <= 10 => Colors.Green,
        <= 30 => Colors.Orange,
        _ => Colors.Red
    };

    /// <summary>
    /// Gets the sync status icon (always synced for server data).
    /// </summary>
    public string SyncStatusIcon => "check";

    /// <summary>
    /// Gets the provider text.
    /// </summary>
    public string ProviderText => Location.LocationType ?? "Unknown";

    /// <summary>
    /// Gets the speed text if available.
    /// </summary>
    public string? SpeedText => Location.Speed.HasValue
        ? $"{Location.Speed.Value * 3.6:F1} km/h"
        : null;

    /// <summary>
    /// Creates a new timeline item from server location.
    /// </summary>
    /// <param name="location">The server location data.</param>
    public TimelineItem(TimelineLocation location)
    {
        Location = location;
    }
}

/// <summary>
/// Display model for timeline location details in the bottom sheet.
/// </summary>
public class TimelineLocationDisplay
{
    private readonly TimelineLocation _location;

    /// <summary>
    /// Creates a new display model from a timeline location.
    /// </summary>
    /// <param name="location">The timeline location.</param>
    public TimelineLocationDisplay(TimelineLocation location)
    {
        _location = location;
    }

    /// <summary>
    /// Gets the location ID.
    /// </summary>
    public int LocationId => _location.Id;

    /// <summary>
    /// Gets the formatted time text.
    /// </summary>
    public string TimeText => _location.LocalTimestamp.ToString("HH:mm:ss");

    /// <summary>
    /// Gets the formatted date text.
    /// </summary>
    public string DateText => _location.LocalTimestamp.ToString("dddd, MMMM d, yyyy");

    /// <summary>
    /// Gets the coordinates text.
    /// </summary>
    public string CoordinatesText => $"{Latitude:F6}, {Longitude:F6}";

    /// <summary>
    /// Gets the latitude.
    /// </summary>
    public double Latitude => _location.Latitude;

    /// <summary>
    /// Gets the longitude.
    /// </summary>
    public double Longitude => _location.Longitude;

    /// <summary>
    /// Gets the activity name.
    /// </summary>
    public string? ActivityName => _location.ActivityType;

    /// <summary>
    /// Gets whether an activity is set.
    /// </summary>
    public bool HasActivity => !string.IsNullOrEmpty(_location.ActivityType);

    /// <summary>
    /// Gets the address.
    /// </summary>
    public string? Address => _location.FullAddress ?? _location.Address;

    /// <summary>
    /// Gets whether an address is available.
    /// </summary>
    public bool HasAddress => !string.IsNullOrEmpty(Address);

    /// <summary>
    /// Gets the accuracy text.
    /// </summary>
    public string AccuracyText => _location.Accuracy.HasValue
        ? $"~{_location.Accuracy.Value:F0}m"
        : "Unknown";

    /// <summary>
    /// Gets whether speed is available.
    /// </summary>
    public bool HasSpeed => _location.Speed.HasValue;

    /// <summary>
    /// Gets the speed text.
    /// </summary>
    public string SpeedText => _location.Speed.HasValue
        ? $"{_location.Speed.Value * 3.6:F1} km/h"
        : "N/A";

    /// <summary>
    /// Gets whether altitude is available.
    /// </summary>
    public bool HasAltitude => _location.Altitude.HasValue;

    /// <summary>
    /// Gets the altitude text.
    /// </summary>
    public string AltitudeText => _location.Altitude.HasValue
        ? $"{_location.Altitude.Value:F0}m"
        : "N/A";

    /// <summary>
    /// Gets whether notes are available.
    /// </summary>
    public bool HasNotes => !string.IsNullOrEmpty(_location.Notes);

    /// <summary>
    /// Gets the notes HTML source for WebView.
    /// </summary>
    public HtmlWebViewSource? NotesHtmlSource
    {
        get
        {
            if (string.IsNullOrEmpty(_location.Notes))
                return null;

            // Wrap notes in basic HTML structure
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            font-size: 14px;
            line-height: 1.5;
            padding: 8px;
            margin: 0;
            color: #333;
        }}
        img {{ max-width: 100%; height: auto; }}
    </style>
</head>
<body>
    {_location.Notes}
</body>
</html>";
            return new HtmlWebViewSource { Html = html };
        }
    }
}
