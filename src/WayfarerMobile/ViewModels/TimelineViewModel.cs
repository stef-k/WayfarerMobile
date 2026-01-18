using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Microsoft.Extensions.Logging;
using SQLite;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Helpers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
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
public partial class TimelineViewModel : BaseViewModel, ICoordinateEditorCallbacks, IDateTimeEditorCallbacks
{
    #region Child ViewModels

    /// <summary>
    /// Gets the coordinate editor child ViewModel.
    /// </summary>
    public CoordinateEditorViewModel CoordinateEditor { get; }

    /// <summary>
    /// Gets the datetime editor child ViewModel.
    /// </summary>
    public DateTimeEditorViewModel DateTimeEditor { get; }

    #endregion

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
    private readonly TimelineDataService _timelineDataService;
    private readonly ITimelineEntryManager _entryManager;
    private readonly IActivitySyncService _activitySyncService;
    private readonly ILogger<TimelineViewModel> _logger;
    private Map? _map;
    private WritableLayer? _timelineLayer;
    private WritableLayer? _tempMarkerLayer;
    private List<TimelineLocation> _allLocations = new();
    private int? _pendingLocationIdToReopen;
    private DateTime? _dateBeforePickerOpened;
    private int _loadDataGuard; // Atomic guard to prevent concurrent LoadDataAsync calls

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
    /// Gets or sets whether the app is currently online.
    /// </summary>
    [ObservableProperty]
    private bool _isOnline = true;

    /// <summary>
    /// Gets or sets whether activities are loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingActivities;

    #endregion

    #region Activity Properties

    /// <summary>
    /// Gets the available activity types for editing.
    /// </summary>
    public ObservableCollection<ActivityType> ActivityTypes { get; } = [];

    /// <summary>
    /// Gets or sets whether the activity picker popup is open.
    /// </summary>
    [ObservableProperty]
    private bool _isActivityPickerOpen;

    /// <summary>
    /// Gets or sets the selected activity in the picker (for two-way binding).
    /// </summary>
    [ObservableProperty]
    private ActivityType? _selectedActivityForEdit;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets or sets the date button text based on current date.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private string _dateButtonText = "Today";

    /// <summary>
    /// Gets or sets whether the user can navigate to the next day (cannot go past today).
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    private bool _canGoNext;

    /// <summary>
    /// Gets or sets whether any edit mode is currently active.
    /// Observable backing field to ensure compiled bindings update correctly.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateDate))]
    private bool _isEditing;

    /// <summary>
    /// Gets whether date navigation is allowed (not during editing).
    /// </summary>
    public bool CanNavigateDate => !IsEditing;

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
    /// <param name="timelineDataService">The timeline data service for local storage access.</param>
    /// <param name="entryManager">The timeline entry manager for CRUD and external actions.</param>
    /// <param name="activitySyncService">The activity sync service for loading activity types.</param>
    /// <param name="coordinateEditorFactory">Factory to create the coordinate editor ViewModel.</param>
    /// <param name="dateTimeEditorFactory">Factory to create the datetime editor ViewModel.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public TimelineViewModel(
        IApiClient apiClient,
        DatabaseService database,
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ISettingsService settingsService,
        IMapBuilder mapBuilder,
        ITimelineLayerService timelineLayerService,
        TimelineDataService timelineDataService,
        ITimelineEntryManager entryManager,
        IActivitySyncService activitySyncService,
        Func<ICoordinateEditorCallbacks, CoordinateEditorViewModel> coordinateEditorFactory,
        Func<IDateTimeEditorCallbacks, DateTimeEditorViewModel> dateTimeEditorFactory,
        ILogger<TimelineViewModel> logger)
    {
        _apiClient = apiClient;
        _database = database;
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        _settingsService = settingsService;
        _mapBuilder = mapBuilder;
        _timelineLayerService = timelineLayerService;
        _timelineDataService = timelineDataService;
        _entryManager = entryManager;
        _activitySyncService = activitySyncService;
        _logger = logger;
        Title = "Timeline";

        // Create child ViewModels with this as callbacks
        CoordinateEditor = coordinateEditorFactory(this);
        DateTimeEditor = dateTimeEditorFactory(this);

        // Wire up property change forwarding for XAML bindings
        // (Child VMs are created fresh for each TimelineViewModel instance, so this is safe)
        CoordinateEditor.PropertyChanged += OnCoordinateEditorPropertyChanged;
        DateTimeEditor.PropertyChanged += OnDateTimeEditorPropertyChanged;

        // Note: Singleton service subscriptions (sync events, connectivity) are done in
        // OnAppearingAsync and unsubscribed in OnDisappearingAsync to prevent event handler
        // accumulation when this Transient ViewModel is recreated on each navigation.

        // Initialize connectivity state (but don't subscribe yet)
        UpdateConnectivityState();

        // Initialize observable properties derived from SelectedDate
        UpdateDateButtonText();
        UpdateCanGoNext();
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
    /// Ensures the map is initialized.
    /// </summary>
    private void EnsureMapInitialized()
    {
        _ = Map;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Loads timeline data from server (when online) or local storage (when offline).
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        // Atomic guard to prevent concurrent calls (IsBusy check alone has race window)
        if (Interlocked.CompareExchange(ref _loadDataGuard, 1, 0) != 0)
            return;

        try
        {
            IsBusy = true;
            IsRefreshing = true;

            // Offline fallback: load from local storage
            if (!IsOnline)
            {
                await LoadFromLocalAsync();
                return;
            }

            // Online: fetch from server and also enrich local storage
            var response = await _apiClient.GetTimelineLocationsAsync(
                dateType: "day",
                year: SelectedDate.Year,
                month: SelectedDate.Month,
                day: SelectedDate.Day);

            // Enrich local storage in background (don't await - fire and forget)
            _ = _timelineDataService.EnrichFromServerAsync(SelectedDate);

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
        catch (HttpRequestException ex)
        {
            // On network error, try loading from local storage as fallback
            _logger.LogNetworkWarningIfOnline("Network error loading timeline data: {Message}", ex.Message);
            await LoadFromLocalAsync();
            await _toastService.ShowWarningAsync("Server unavailable. Showing local data.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // On timeout, try loading from local storage as fallback
            await LoadFromLocalAsync();
            await _toastService.ShowWarningAsync("Request timed out. Showing local data.");
        }
        catch (Exception)
        {
            // On any other error, try loading from local storage as fallback
            await LoadFromLocalAsync();
            await _toastService.ShowWarningAsync("Server unavailable. Showing local data.");
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
            Interlocked.Exchange(ref _loadDataGuard, 0); // Release atomic guard
        }
    }

    /// <summary>
    /// Loads timeline data from local storage (offline mode).
    /// </summary>
    private async Task LoadFromLocalAsync()
    {
        try
        {
            var localEntries = await _timelineDataService.GetEntriesForDateAsync(SelectedDate);

            if (!localEntries.Any())
            {
                _allLocations.Clear();
                TimelineGroups = new ObservableCollection<TimelineGroup>();
                TotalCount = 0;
                IsEmpty = true;
                StatsText = "No local data";
                UpdateMapLocations();
                return;
            }

            // Convert local entries to TimelineLocation for display
            _allLocations = TimelineDataService.ToTimelineLocations(localEntries);

            // Group by hour for better organization
            var groups = _allLocations
                .GroupBy(l => l.LocalTimestamp.Hour)
                .OrderByDescending(g => g.Key)
                .Select(g => new TimelineGroup(
                    $"{g.Key:00}:00 - {g.Key:00}:59",
                    g.OrderByDescending(l => l.LocalTimestamp).ToList()))
                .ToList();

            TimelineGroups = new ObservableCollection<TimelineGroup>(groups);
            TotalCount = _allLocations.Count;
            IsEmpty = !groups.Any();

            // Update stats (indicate local data)
            StatsText = $"{TotalCount} location{(TotalCount == 1 ? "" : "s")} (offline)";

            // Update map
            UpdateMapLocations();
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error loading local timeline data");
            await _toastService.ShowErrorAsync("Failed to load local data");
            IsEmpty = true;
            StatsText = "Error loading data";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading local timeline data");
            await _toastService.ShowErrorAsync($"Failed to load local data: {ex.Message}");
            IsEmpty = true;
            StatsText = "Error loading data";
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

        await _entryManager.OpenInMapsAsync(
            SelectedLocation.Latitude,
            SelectedLocation.Longitude,
            $"Location at {SelectedLocation.TimeText}");
    }

    /// <summary>
    /// Searches Wikipedia for the selected location.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        if (SelectedLocation == null)
            return;

        await _entryManager.SearchWikipediaAsync(
            SelectedLocation.Latitude,
            SelectedLocation.Longitude);
    }

    /// <summary>
    /// Copies the selected location coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (SelectedLocation == null)
            return;

        await _entryManager.CopyCoordinatesAsync(
            SelectedLocation.Latitude,
            SelectedLocation.Longitude);
    }

    /// <summary>
    /// Shares the selected location.
    /// </summary>
    [RelayCommand]
    private async Task ShareLocationAsync()
    {
        if (SelectedLocation == null)
            return;

        await _entryManager.ShareLocationAsync(
            SelectedLocation.Latitude,
            SelectedLocation.Longitude,
            SelectedLocation.TimeText,
            SelectedLocation.DateText);
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
    /// Refreshes the activity types from server.
    /// </summary>
    [RelayCommand]
    private async Task RefreshActivitiesAsync()
    {
        if (IsLoadingActivities)
            return;

        try
        {
            IsLoadingActivities = true;
            var success = await _activitySyncService.SyncWithServerAsync();
            if (success)
            {
                await LoadActivitiesAsync();
                await _toastService.ShowSuccessAsync("Activities refreshed");
            }
            else
            {
                await _toastService.ShowWarningAsync("Could not refresh activities");
            }
        }
        finally
        {
            IsLoadingActivities = false;
        }
    }

    /// <summary>
    /// Loads activity types from local cache.
    /// </summary>
    private async Task LoadActivitiesAsync()
    {
        try
        {
            IsLoadingActivities = true;

            var activities = await _activitySyncService.GetActivityTypesAsync();

            ActivityTypes.Clear();
            foreach (var activity in activities)
            {
                ActivityTypes.Add(activity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load activity types");
        }
        finally
        {
            IsLoadingActivities = false;
        }
    }

    /// <summary>
    /// Opens the activity picker popup.
    /// </summary>
    [RelayCommand]
    private void OpenActivityPicker()
    {
        if (SelectedLocation == null) return;

        // Pre-select current activity if any
        SelectedActivityForEdit = ActivityTypes.FirstOrDefault(a => a.Name == SelectedLocation.ActivityType);
        IsActivityPickerOpen = true;
    }

    /// <summary>
    /// Closes the activity picker popup without saving.
    /// </summary>
    [RelayCommand]
    private void CloseActivityPicker()
    {
        IsActivityPickerOpen = false;
        SelectedActivityForEdit = null;
    }

    /// <summary>
    /// Saves the selected activity and closes the picker.
    /// </summary>
    [RelayCommand]
    private async Task SaveActivityAsync()
    {
        if (SelectedLocation == null) return;

        var activityChanged = SelectedActivityForEdit?.Name != SelectedLocation.ActivityType;
        if (activityChanged)
        {
            await UpdateActivityAsync(SelectedActivityForEdit?.Id, clearActivity: false);
        }

        IsActivityPickerOpen = false;
        SelectedActivityForEdit = null;
    }

    /// <summary>
    /// Clears the activity for the current location.
    /// </summary>
    [RelayCommand]
    private async Task ClearActivityAsync()
    {
        if (SelectedLocation == null) return;

        await UpdateActivityAsync(null, clearActivity: true);
        IsActivityPickerOpen = false;
        SelectedActivityForEdit = null;
    }

    /// <summary>
    /// Updates the activity type for the currently selected location.
    /// </summary>
    /// <param name="activityTypeId">The new activity type ID, or null if clearing.</param>
    /// <param name="clearActivity">True to clear the activity.</param>
    private async Task UpdateActivityAsync(int? activityTypeId, bool clearActivity)
    {
        // Capture reference to avoid race condition during async call
        var locationToUpdate = SelectedLocation;
        if (locationToUpdate == null) return;

        // Look up activity name for optimistic local update
        var activityName = activityTypeId.HasValue
            ? ActivityTypes.FirstOrDefault(a => a.Id == activityTypeId.Value)?.Name
            : null;

        try
        {
            IsBusy = true;

            await _timelineSyncService.UpdateLocationAsync(
                locationToUpdate.LocationId,
                activityTypeId: activityTypeId,
                clearActivity: clearActivity,
                activityTypeName: activityName);

            // Update local display
            if (clearActivity)
            {
                locationToUpdate.ActivityType = null;
            }
            else if (activityTypeId.HasValue)
            {
                locationToUpdate.ActivityType = activityName;
            }

            await _toastService.ShowSuccessAsync("Activity updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update activity for location {LocationId}", locationToUpdate.LocationId);
            await _toastService.ShowErrorAsync("Failed to update activity");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes a timeline location.
    /// </summary>
    /// <param name="locationId">The location ID to delete.</param>
    public async Task DeleteLocationAsync(int locationId)
    {
        // Check online status
        if (!IsOnline)
        {
            await _toastService.ShowWarningAsync("You're offline. Deletion will sync when online.");
        }

        try
        {
            IsBusy = true;

            // Close the location sheet first
            IsLocationSheetOpen = false;
            SelectedLocation = null;

            // Update UI immediately (optimistic delete)
            var locationToRemove = _allLocations.FirstOrDefault(l => l.Id == locationId);
            if (locationToRemove != null)
            {
                _allLocations.Remove(locationToRemove);

                // Re-group the remaining locations
                if (_allLocations.Any())
                {
                    var groups = _allLocations
                        .GroupBy(l => l.LocalTimestamp.Hour)
                        .OrderByDescending(g => g.Key)
                        .Select(g => new TimelineGroup(
                            $"{g.Key:00}:00 - {g.Key:00}:59",
                            g.OrderByDescending(l => l.LocalTimestamp).ToList()))
                        .ToList();
                    TimelineGroups = new ObservableCollection<TimelineGroup>(groups);
                    TotalCount = _allLocations.Count;
                    IsEmpty = false;
                    StatsText = $"{TotalCount} location{(TotalCount == 1 ? "" : "s")}";
                }
                else
                {
                    TimelineGroups = new ObservableCollection<TimelineGroup>();
                    TotalCount = 0;
                    IsEmpty = true;
                    StatsText = "No locations";
                }

                // Update map
                UpdateMapLocations();
            }

            // Delete via sync service (handles offline queueing)
            await _timelineSyncService.DeleteLocationAsync(locationId);

            await _toastService.ShowSuccessAsync("Location deleted");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error deleting location: {Message}", ex.Message);
            await _toastService.ShowWarningAsync("Network error. Deletion will sync when online.");
            // Reload to restore UI if local delete failed
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location {LocationId}", locationId);
            await _toastService.ShowErrorAsync($"Failed to delete: {ex.Message}");
            // Reload to restore UI state
            await LoadDataAsync();
        }
        finally
        {
            IsBusy = false;
        }
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

            var locationId = SelectedLocation.LocationId;
            await _entryManager.SaveNotesAsync(locationId, notesHtml);

            // Reload data to reflect changes
            await LoadDataAsync();

            // Re-select the location to show updated details
            ShowLocationDetails(locationId);
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
        // Sync to server (handles offline queueing automatically)
        await _entryManager.SaveEntryChangesAsync(e);

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
    /// Reloads timeline to show reverted values after LocalTimelineEntry rollback.
    /// </summary>
    private async void OnSyncRejected(object? sender, SyncFailureEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowErrorAsync($"Save failed: {e.ErrorMessage}");
            // Reload timeline to reflect reverted LocalTimelineEntry values
            await LoadDataAsync();
        });
    }

    /// <summary>
    /// Handles connectivity change events.
    /// Updates UI state only - sync is now handled autonomously by TimelineSyncService.
    /// </summary>
    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var access = e.NetworkAccess;
        var isNowOnline = access == NetworkAccess.Internet || access == NetworkAccess.ConstrainedInternet;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsOnline = isNowOnline;
        });

        // Note: Sync is now handled autonomously by TimelineSyncService
        // via its own connectivity subscription and timer-based processing
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
        // Subscribe to Singleton service events (unsubscribed in OnDisappearingAsync)
        _timelineSyncService.SyncCompleted += OnSyncCompleted;
        _timelineSyncService.SyncQueued += OnSyncQueued;
        _timelineSyncService.SyncRejected += OnSyncRejected;
        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        EnsureMapInitialized();
        await LoadDataAsync();

        // Load activities in background (don't block UI)
        _ = LoadActivitiesAsync();

        // Background sync of activities if needed
        _ = _activitySyncService.AutoSyncIfNeededAsync();

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
        // Unsubscribe from Singleton service events (subscribed in OnAppearingAsync)
        // This prevents event handler accumulation when this Transient ViewModel is recreated
        _timelineSyncService.SyncCompleted -= OnSyncCompleted;
        _timelineSyncService.SyncQueued -= OnSyncQueued;
        _timelineSyncService.SyncRejected -= OnSyncRejected;
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;

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
        // Note: Singleton service subscriptions (sync events, connectivity) are now
        // unsubscribed in OnDisappearingAsync to prevent accumulation during navigation.

        // Unsubscribe from child ViewModel property changes
        // (These are subscribed once in constructor to VMs created with this instance)
        CoordinateEditor.PropertyChanged -= OnCoordinateEditorPropertyChanged;
        DateTimeEditor.PropertyChanged -= OnDateTimeEditorPropertyChanged;

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

        base.Cleanup();
    }

    /// <summary>
    /// Forwards CoordinateEditor property changes for XAML binding.
    /// Also updates parent observable properties for IsEditing state.
    /// </summary>
    private void OnCoordinateEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward child property changes for path-based bindings (e.g., CoordinateEditor.IsCoordinatePickingMode)
        OnPropertyChanged($"CoordinateEditor.{e.PropertyName}");

        // Update parent observable properties when editing state changes
        if (e.PropertyName == nameof(CoordinateEditorViewModel.IsEditing))
        {
            UpdateEditingState();
        }
    }

    /// <summary>
    /// Forwards DateTimeEditor property changes for XAML binding.
    /// Also updates parent observable properties for IsEditing state.
    /// </summary>
    private void OnDateTimeEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward child property changes for path-based bindings (e.g., DateTimeEditor.IsEditDateTimePickerOpen)
        OnPropertyChanged($"DateTimeEditor.{e.PropertyName}");

        // Update parent observable properties when editing state changes
        if (e.PropertyName == nameof(DateTimeEditorViewModel.IsEditing))
        {
            UpdateEditingState();
        }
    }

    /// <summary>
    /// Updates the IsEditing observable property based on child editor states.
    /// Also updates CanGoNext since it depends on editing state.
    /// </summary>
    private void UpdateEditingState()
    {
        IsEditing = CoordinateEditor.IsEditing || DateTimeEditor.IsEditing;
        UpdateCanGoNext();
    }

    /// <summary>
    /// Updates the CanGoNext observable property based on current state.
    /// </summary>
    private void UpdateCanGoNext()
    {
        CanGoNext = SelectedDate.Date < DateTime.Today && CanNavigateDate;
    }

    /// <summary>
    /// Updates the DateButtonText observable property based on SelectedDate.
    /// </summary>
    private void UpdateDateButtonText()
    {
        if (SelectedDate.Date == DateTime.Today)
            DateButtonText = "Today";
        else if (SelectedDate.Date == DateTime.Today.AddDays(-1))
            DateButtonText = "Yesterday";
        else
            DateButtonText = SelectedDate.ToString("ddd, MMM d");
    }

    /// <summary>
    /// Called when SelectedDate changes. Updates derived observable properties.
    /// </summary>
    partial void OnSelectedDateChanged(DateTime value)
    {
        UpdateDateButtonText();
        UpdateCanGoNext();
    }

    #endregion

    #region Callback Interface Implementations

    // ICoordinateEditorCallbacks implementation
    TimelineLocationDisplay? ICoordinateEditorCallbacks.SelectedLocation => SelectedLocation;
    WritableLayer? ICoordinateEditorCallbacks.TempMarkerLayer => _tempMarkerLayer;
    Mapsui.Map? ICoordinateEditorCallbacks.MapInstance => _map;
    bool ICoordinateEditorCallbacks.IsOnline => IsOnline;
    bool ICoordinateEditorCallbacks.IsBusy { get => IsBusy; set => IsBusy = value; }
    Task ICoordinateEditorCallbacks.ReloadTimelineAsync() => LoadDataAsync();
    void ICoordinateEditorCallbacks.ShowLocationDetails(int locationId) => ShowLocationDetails(locationId);
    void ICoordinateEditorCallbacks.OpenLocationSheet() => IsLocationSheetOpen = true;

    // IDateTimeEditorCallbacks implementation
    TimelineLocationDisplay? IDateTimeEditorCallbacks.SelectedLocation => SelectedLocation;
    bool IDateTimeEditorCallbacks.IsOnline => IsOnline;
    bool IDateTimeEditorCallbacks.IsBusy { get => IsBusy; set => IsBusy = value; }
    Task IDateTimeEditorCallbacks.ReloadTimelineAsync() => LoadDataAsync();
    void IDateTimeEditorCallbacks.ShowLocationDetails(int locationId) => ShowLocationDetails(locationId);
    void IDateTimeEditorCallbacks.OpenLocationSheet() => IsLocationSheetOpen = true;

    /// <summary>
    /// Delegates coordinate setting to the child ViewModel.
    /// Called by code-behind when the map is tapped during coordinate picking mode.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void SetPendingCoordinates(double latitude, double longitude)
    {
        CoordinateEditor.SetPendingCoordinates(latitude, longitude);
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
    /// Gets or sets the activity type (used for updates).
    /// </summary>
    public string? ActivityType
    {
        get => _location.ActivityType;
        set => _location.ActivityType = value;
    }

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
