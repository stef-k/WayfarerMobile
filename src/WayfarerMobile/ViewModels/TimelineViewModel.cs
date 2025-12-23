using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Controls;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;
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
    private const string TempMarkerLayerName = "TempMarker";

    #endregion

    #region Fields

    private readonly IApiClient _apiClient;
    private readonly DatabaseService _database;
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private readonly ISettingsService _settingsService;
    private readonly IMapBuilder _mapBuilder;
    private readonly ITimelineLayerService _timelineLayerService;
    private Map? _map;
    private WritableLayer? _timelineLayer;
    private WritableLayer? _tempMarkerLayer;
    private List<TimelineLocation> _allLocations = new();
    private int? _pendingLocationIdToReopen;
    private DateTime? _dateBeforePickerOpened;

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

    /// <summary>
    /// Gets or sets whether coordinate picking mode is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(CanNavigateDate))]
    private bool _isCoordinatePickingMode;

    /// <summary>
    /// Gets or sets whether the edit datetime picker is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(CanNavigateDate))]
    private bool _isEditDateTimePickerOpen;

    /// <summary>
    /// Gets or sets the datetime being edited.
    /// </summary>
    [ObservableProperty]
    private DateTime _editDateTime = DateTime.Now;

    /// <summary>
    /// Gets or sets the pending latitude during coordinate picking.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingCoordinatesText))]
    private double? _pendingLatitude;

    /// <summary>
    /// Gets or sets the pending longitude during coordinate picking.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingCoordinatesText))]
    private double? _pendingLongitude;

    /// <summary>
    /// Gets or sets whether the app is currently online.
    /// </summary>
    [ObservableProperty]
    private bool _isOnline = true;

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
    public bool CanGoNext => SelectedDate.Date < DateTime.Today && CanNavigateDate;

    /// <summary>
    /// Gets whether any edit mode is currently active.
    /// </summary>
    public bool IsEditing => IsCoordinatePickingMode || IsEditDateTimePickerOpen;

    /// <summary>
    /// Gets whether date navigation is allowed (not during editing).
    /// </summary>
    public bool CanNavigateDate => !IsEditing;

    /// <summary>
    /// Gets whether there are pending coordinates to save.
    /// </summary>
    public bool HasPendingCoordinates => PendingLatitude.HasValue && PendingLongitude.HasValue;

    /// <summary>
    /// Gets the pending coordinates text for display.
    /// </summary>
    public string PendingCoordinatesText => HasPendingCoordinates
        ? $"{PendingLatitude:F6}, {PendingLongitude:F6}"
        : "Tap on map to set location";

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TimelineViewModel.
    /// </summary>
    /// <param name="apiClient">The API client.</param>
    /// <param name="database">The database service.</param>
    /// <param name="timelineSyncService">The timeline sync service.</param>
    /// <param name="toastService">The toast service.</param>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="mapBuilder">The map builder for creating isolated map instances.</param>
    /// <param name="timelineLayerService">The timeline layer service for rendering markers.</param>
    public TimelineViewModel(
        IApiClient apiClient,
        DatabaseService database,
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ISettingsService settingsService,
        IMapBuilder mapBuilder,
        ITimelineLayerService timelineLayerService)
    {
        _apiClient = apiClient;
        _database = database;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        _settingsService = settingsService;
        _mapBuilder = mapBuilder;
        _timelineLayerService = timelineLayerService;
        Title = "Timeline";

        // Subscribe to sync events
        _timelineSyncService.SyncCompleted += OnSyncCompleted;
        _timelineSyncService.SyncQueued += OnSyncQueued;
        _timelineSyncService.SyncRejected += OnSyncRejected;

        // Initialize connectivity state
        UpdateConnectivityState();
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    #endregion

    #region Map Creation

    /// <summary>
    /// Creates and configures the map instance using IMapBuilder for proper tile caching.
    /// </summary>
    private Map CreateMap()
    {
        // Create layers using layer service for consistent naming
        _timelineLayer = _mapBuilder.CreateLayer(_timelineLayerService.TimelineLayerName);
        _tempMarkerLayer = _mapBuilder.CreateLayer(TempMarkerLayerName);

        // Create map with tile source (includes offline caching) and our layers
        return _mapBuilder.CreateMap(_timelineLayer, _tempMarkerLayer);
    }

    /// <summary>
    /// Updates or creates the temporary marker at the specified coordinates.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    private void UpdateTempMarker(double latitude, double longitude)
    {
        if (_tempMarkerLayer == null || _map == null)
            return;

        _tempMarkerLayer.Clear();

        var (x, y) = SphericalMercator.FromLonLat(longitude, latitude);
        var point = new Point(x, y);
        var feature = new GeometryFeature(point)
        {
            Styles = new[] { CreateTempMarkerStyle() }
        };

        _tempMarkerLayer.Add(feature);
        _tempMarkerLayer.DataHasChanged();
    }

    /// <summary>
    /// Removes the temporary marker from the map.
    /// </summary>
    private void RemoveTempMarker()
    {
        if (_tempMarkerLayer == null)
            return;

        _tempMarkerLayer.Clear();
        _tempMarkerLayer.DataHasChanged();
    }

    /// <summary>
    /// Creates the style for the temporary marker (distinct from regular markers).
    /// </summary>
    private static IStyle CreateTempMarkerStyle()
    {
        return new SymbolStyle
        {
            SymbolScale = 0.7,
            Fill = new Brush(Color.FromArgb(255, 255, 87, 34)), // Orange (Material Deep Orange)
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    /// <summary>
    /// Ensures the map is initialized.
    /// </summary>
    private void EnsureMapInitialized()
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
    /// Updates the map layer with current locations using the timeline layer service.
    /// </summary>
    private void UpdateMapLocations()
    {
        if (_timelineLayer == null || _map == null)
            return;

        if (!_allLocations.Any())
        {
            _timelineLayerService.ClearTimelineMarkers(_timelineLayer);
            return;
        }

        // Use timeline layer service to render markers
        var points = _timelineLayerService.UpdateTimelineMarkers(_timelineLayer, _allLocations);

        // Zoom to fit all locations with delay to ensure map is ready
        if (points.Count >= 1)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                if (points.Count > 1)
                {
                    _mapBuilder.ZoomToPoints(_map, points);
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
    /// Navigates to today's date.
    /// </summary>
    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        if (SelectedDate.Date != DateTime.Today)
        {
            SelectedDate = DateTime.Today;
            await LoadDataAsync();
        }
    }

    /// <summary>
    /// Opens the date picker.
    /// </summary>
    [RelayCommand]
    private void OpenDatePicker()
    {
        // Store current date to detect if user changed it
        _dateBeforePickerOpened = SelectedDate;
        IsDatePickerOpen = true;
    }

    /// <summary>
    /// Handles date selection from picker (called when OK is clicked).
    /// </summary>
    [RelayCommand]
    private async Task DateSelectedAsync(DateTime? date)
    {
        IsDatePickerOpen = false;

        // SelectedDate is already updated via two-way binding from the picker
        // Compare against the date before picker was opened
        if (_dateBeforePickerOpened.HasValue && SelectedDate.Date != _dateBeforePickerOpened.Value.Date)
        {
            // Limit to today at most
            if (SelectedDate.Date > DateTime.Today)
            {
                SelectedDate = DateTime.Today;
            }
            await LoadDataAsync();
        }

        _dateBeforePickerOpened = null;
    }

    /// <summary>
    /// Cancels the date picker and restores the original date.
    /// </summary>
    [RelayCommand]
    private void CancelDatePicker()
    {
        // Restore the original date since user cancelled
        if (_dateBeforePickerOpened.HasValue)
        {
            SelectedDate = _dateBeforePickerOpened.Value;
        }
        IsDatePickerOpen = false;
        _dateBeforePickerOpened = null;
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

        SelectedLocation = new TimelineLocationDisplay(location, _settingsService.ServerUrl);
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
        // This is handled by code-behind to show the action sheet
    }

    /// <summary>
    /// Enters coordinate picking mode.
    /// </summary>
    [RelayCommand]
    private void EnterCoordinatePickingMode()
    {
        if (SelectedLocation == null) return;

        // Set initial pending coordinates to current location
        PendingLatitude = SelectedLocation.Latitude;
        PendingLongitude = SelectedLocation.Longitude;
        IsCoordinatePickingMode = true;

        // Show temp marker at current location
        UpdateTempMarker(PendingLatitude.Value, PendingLongitude.Value);
    }

    /// <summary>
    /// Sets the pending coordinates from a map tap.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void SetPendingCoordinates(double latitude, double longitude)
    {
        if (!IsCoordinatePickingMode) return;

        PendingLatitude = latitude;
        PendingLongitude = longitude;
        UpdateTempMarker(latitude, longitude);
    }

    /// <summary>
    /// Saves the pending coordinates.
    /// </summary>
    [RelayCommand]
    private async Task SaveCoordinatesAsync()
    {
        if (SelectedLocation == null || !HasPendingCoordinates) return;

        // Store locationId before any changes (reference becomes stale after reload)
        var locationId = SelectedLocation.LocationId;

        // Check online status
        if (!IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Changes will sync when online.");
        }

        try
        {
            IsBusy = true;

            await _timelineSyncService.UpdateLocationAsync(
                locationId,
                PendingLatitude,
                PendingLongitude,
                localTimestamp: null,
                notes: null,
                includeNotes: false);

            // Exit picking mode
            ExitCoordinatePickingMode();

            // Reload data to reflect changes on map
            await LoadDataAsync();

            // Re-select the location to show updated details in bottom sheet
            ShowLocationDetails(locationId);
            IsLocationSheetOpen = true;
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels coordinate picking mode.
    /// </summary>
    [RelayCommand]
    private void CancelCoordinatePicking()
    {
        ExitCoordinatePickingMode();
    }

    /// <summary>
    /// Exits coordinate picking mode and cleans up.
    /// </summary>
    private void ExitCoordinatePickingMode()
    {
        IsCoordinatePickingMode = false;
        PendingLatitude = null;
        PendingLongitude = null;
        RemoveTempMarker();
    }

    /// <summary>
    /// Opens the datetime picker for editing the selected location.
    /// </summary>
    [RelayCommand]
    private void OpenEditDateTimePicker()
    {
        if (SelectedLocation == null) return;

        // Set the picker to the current location's datetime
        EditDateTime = SelectedLocation.LocalTimestamp;
        IsEditDateTimePickerOpen = true;
    }

    /// <summary>
    /// Saves the edited datetime from the picker.
    /// </summary>
    [RelayCommand]
    private async Task SaveEditDateTimeAsync()
    {
        if (SelectedLocation == null) return;

        // Store locationId before any changes (reference becomes stale after reload)
        var locationId = SelectedLocation.LocationId;

        // Check online status
        if (!IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Changes will sync when online.");
        }

        try
        {
            IsBusy = true;

            // User entered local time - convert to UTC for the server
            // The SfDateTimePicker returns DateTimeKind.Unspecified, but it represents local time
            var utcDateTime = DateTime.SpecifyKind(EditDateTime, DateTimeKind.Local).ToUniversalTime();

            await _timelineSyncService.UpdateLocationAsync(
                locationId,
                latitude: null,
                longitude: null,
                localTimestamp: utcDateTime,
                notes: null,
                includeNotes: false);

            // Close picker
            IsEditDateTimePickerOpen = false;

            // Reload data to reflect changes on map and groupings
            await LoadDataAsync();

            // Re-select the location to show updated details in bottom sheet
            ShowLocationDetails(locationId);
            IsLocationSheetOpen = true;
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels datetime editing.
    /// </summary>
    [RelayCommand]
    private void CancelEditDateTime()
    {
        IsEditDateTimePickerOpen = false;
    }

    /// <summary>
    /// Saves notes for the selected location.
    /// </summary>
    /// <param name="notesHtml">The notes HTML content.</param>
    public async Task SaveNotesAsync(string? notesHtml)
    {
        if (SelectedLocation == null) return;

        // Check online status
        if (!IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Changes will sync when online.");
        }

        try
        {
            IsBusy = true;

            await _timelineSyncService.UpdateLocationAsync(
                SelectedLocation.LocationId,
                latitude: null,
                longitude: null,
                localTimestamp: null,
                notes: notesHtml,
                includeNotes: true);

            // Reload data to reflect changes
            await LoadDataAsync();

            // Re-select the location to show updated details
            ShowLocationDetails(SelectedLocation.LocationId);
        }
        catch (Exception ex)
        {
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
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

    /// <summary>
    /// Handles connectivity change events.
    /// </summary>
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateConnectivityState);
    }

    /// <summary>
    /// Updates the IsOnline property based on current connectivity.
    /// </summary>
    private void UpdateConnectivityState()
    {
        var access = Connectivity.Current.NetworkAccess;
        IsOnline = access == NetworkAccess.Internet || access == NetworkAccess.ConstrainedInternet;
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

        // Check if we need to reopen a location sheet (returning from notes editor)
        if (_pendingLocationIdToReopen.HasValue)
        {
            var locationId = _pendingLocationIdToReopen.Value;
            _pendingLocationIdToReopen = null;

            // Reopen the location details sheet with fresh data
            ShowLocationDetails(locationId);
            IsLocationSheetOpen = true;
        }

        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        // Clear timeline markers to release memory
        if (_timelineLayer != null)
        {
            _timelineLayerService.ClearTimelineMarkers(_timelineLayer);
        }

        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Sets a location ID to reopen when returning to this page.
    /// Used when navigating to notes editor and back.
    /// </summary>
    /// <param name="locationId">The location ID to reopen.</param>
    public void SetPendingLocationToReopen(int locationId)
    {
        _pendingLocationIdToReopen = locationId;
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

        // Unsubscribe from connectivity events
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

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
    private readonly string? _serverUrl;

    /// <summary>
    /// Creates a new display model from a timeline location.
    /// </summary>
    /// <param name="location">The timeline location.</param>
    /// <param name="serverUrl">The server URL for image proxy conversion.</param>
    public TimelineLocationDisplay(TimelineLocation location, string? serverUrl = null)
    {
        _location = location;
        _serverUrl = serverUrl;
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
    /// Gets the local timestamp.
    /// </summary>
    public DateTime LocalTimestamp => _location.LocalTimestamp;

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
    /// Gets whether notes contain actual visible content.
    /// Returns false for empty notes or Quill's empty markup (e.g., &lt;p&gt;&lt;br&gt;&lt;/p&gt;).
    /// </summary>
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrEmpty(_location.Notes))
                return false;

            // Strip HTML tags and check for actual text content
            var plainText = Regex.Replace(_location.Notes, "<[^>]+>", " ");
            var hasText = !string.IsNullOrWhiteSpace(plainText);

            // Also check for images (content even without text)
            var hasImages = Regex.IsMatch(_location.Notes, @"<img\s", RegexOptions.IgnoreCase);

            return hasText || hasImages;
        }
    }

    /// <summary>
    /// Gets the raw notes HTML.
    /// </summary>
    public string? Notes => _location.Notes;

    /// <summary>
    /// Gets the notes HTML source for WebView.
    /// </summary>
    public HtmlWebViewSource? NotesHtmlSource
    {
        get
        {
            if (!HasNotes)
                return null;

            // Convert images to proxy URLs for WebView display
            var notesContent = ImageProxyHelper.ConvertImagesToProxyUrls(
                _location.Notes,
                _serverUrl);

            // Wrap notes in basic HTML structure
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            font-size: 17px;
            line-height: 1.5;
            padding: 8px;
            margin: 0;
            color: #333;
        }}
        img {{ max-width: 100%; height: auto; }}
    </style>
</head>
<body>
    {notesContent}
</body>
</html>";
            return new HtmlWebViewSource { Html = html };
        }
    }
}
