using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for date navigation functionality.
/// Handles date selection, historical location loading, and live/history mode switching.
/// </summary>
public partial class DateNavigationViewModel : ObservableObject
{
    #region Fields

    private readonly IGroupsService _groupsService;
    private readonly IGroupLayerService _groupLayerService;
    private readonly ILogger<DateNavigationViewModel> _logger;
    private IDateNavigationCallbacks? _callbacks;

    /// <summary>
    /// Flag indicating a historical query is currently in progress.
    /// Prevents overlapping queries.
    /// </summary>
    private bool _isQueryInProgress;

    /// <summary>
    /// Date before the picker was opened (for cancel restoration).
    /// </summary>
    private DateTime? _dateBeforePickerOpened;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the selected date for viewing locations.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday))]
    [NotifyPropertyChangedFor(nameof(ShowHistoricalToggle))]
    [NotifyPropertyChangedFor(nameof(SelectedDateText))]
    [NotifyPropertyChangedFor(nameof(DateButtonText))]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// Gets or sets whether to show historical locations (when viewing today).
    /// </summary>
    [ObservableProperty]
    private bool _showHistoricalLocations;

    /// <summary>
    /// Gets or sets whether the date picker is open.
    /// </summary>
    [ObservableProperty]
    private bool _isDatePickerOpen;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether the selected date is today.
    /// </summary>
    public bool IsToday => SelectedDate.Date == DateTime.Today;

    /// <summary>
    /// Gets whether to show the historical toggle (only visible when viewing today).
    /// </summary>
    public bool ShowHistoricalToggle => IsToday;

    /// <summary>
    /// Gets the formatted selected date text.
    /// </summary>
    public string SelectedDateText => SelectedDate.Date == DateTime.Today
        ? "Today"
        : SelectedDate.ToString("ddd, MMM d, yyyy");

    /// <summary>
    /// Gets the date button text (for consistency with TimelinePage).
    /// </summary>
    public string DateButtonText => SelectedDate.Date == DateTime.Today
        ? "Today"
        : SelectedDate.ToString("MMM d, yyyy");

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of DateNavigationViewModel.
    /// </summary>
    /// <param name="groupsService">Service for group operations.</param>
    /// <param name="groupLayerService">Service for group layer rendering.</param>
    /// <param name="logger">Logger instance.</param>
    public DateNavigationViewModel(
        IGroupsService groupsService,
        IGroupLayerService groupLayerService,
        ILogger<DateNavigationViewModel> logger)
    {
        _groupsService = groupsService;
        _groupLayerService = groupLayerService;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the callbacks for accessing parent ViewModel state and operations.
    /// </summary>
    /// <param name="callbacks">The callback implementation.</param>
    public void SetCallbacks(IDateNavigationCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    #endregion

    #region Property Change Handlers

    /// <summary>
    /// Called when the selected date changes.
    /// Updates computed properties only - data loading is handled by commands.
    /// </summary>
    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(ShowHistoricalToggle));

        // Reset historical toggle when date changes (only relevant for today)
        if (!IsToday && ShowHistoricalLocations)
        {
            ShowHistoricalLocations = false;
        }
    }

    /// <summary>
    /// Called when the ShowHistoricalLocations toggle changes.
    /// Loads or clears historical locations for today's view.
    /// </summary>
    partial void OnShowHistoricalLocationsChanged(bool value)
    {
        if (!IsToday || _callbacks == null)
            return;

        if (value)
        {
            // Toggle ON: Load today's historical locations
            _logger.LogInformation("[Groups] Historical toggle ON - loading today's historical locations");
            _ = LoadHistoricalLocationsAsync();
        }
        else
        {
            // Toggle OFF: Clear historical locations and show only live markers
            _logger.LogInformation("[Groups] Historical toggle OFF - clearing historical locations");
            _callbacks.ClearHistoricalLocations();
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Selects a date for viewing locations.
    /// </summary>
    [RelayCommand]
    private void SelectDate(DateTime date)
    {
        SelectedDate = date;
    }

    /// <summary>
    /// Navigates to the previous day.
    /// Runs on background thread to prevent UI freeze.
    /// </summary>
    [RelayCommand]
    private void PreviousDay()
    {
        var newDate = SelectedDate.AddDays(-1);
        _logger.LogDebug("PreviousDay - changing to {Date}", newDate.ToString("yyyy-MM-dd"));

        // Capture data on main thread to avoid cross-thread collection access
        var navigationData = CaptureNavigationData(newDate);

        // Fire and forget on background thread - frees UI immediately
        _ = Task.Run(() => NavigateToDateAsync(navigationData));
    }

    /// <summary>
    /// Navigates to the next day (not beyond today).
    /// </summary>
    [RelayCommand]
    private void NextDay()
    {
        if (SelectedDate.Date < DateTime.Today)
        {
            var newDate = SelectedDate.AddDays(1);
            _logger.LogDebug("NextDay - changing to {Date}", newDate.ToString("yyyy-MM-dd"));
            var navigationData = CaptureNavigationData(newDate);
            _ = Task.Run(() => NavigateToDateAsync(navigationData));
        }
    }

    /// <summary>
    /// Navigates to today (live mode).
    /// </summary>
    [RelayCommand]
    private void Today()
    {
        _logger.LogDebug("Today - changing to {Date}", DateTime.Today.ToString("yyyy-MM-dd"));
        var navigationData = CaptureNavigationData(DateTime.Today);
        _ = Task.Run(() => NavigateToDateAsync(navigationData));
    }

    /// <summary>
    /// Opens the date picker dialog.
    /// </summary>
    [RelayCommand]
    private void OpenDatePicker()
    {
        _dateBeforePickerOpened = SelectedDate;
        IsDatePickerOpen = true;
    }

    /// <summary>
    /// Handles date selection from picker.
    /// </summary>
    [RelayCommand]
    private void DateSelected()
    {
        IsDatePickerOpen = false;

        // Compare against date before picker was opened
        if (_dateBeforePickerOpened.HasValue && SelectedDate.Date != _dateBeforePickerOpened.Value.Date)
        {
            // Limit to today at most
            var newDate = SelectedDate.Date > DateTime.Today ? DateTime.Today : SelectedDate;

            _logger.LogDebug("DateSelected - Date changed to {Date}", newDate.ToString("yyyy-MM-dd"));
            // Capture data on main thread, then run on background thread
            var navigationData = CaptureNavigationData(newDate);
            _ = Task.Run(() => NavigateToDateAsync(navigationData));
        }

        _dateBeforePickerOpened = null;
    }

    /// <summary>
    /// Cancels date picker and restores original date.
    /// </summary>
    [RelayCommand]
    private void CancelDatePicker()
    {
        if (_dateBeforePickerOpened.HasValue)
        {
            SelectedDate = _dateBeforePickerOpened.Value;
        }
        IsDatePickerOpen = false;
        _dateBeforePickerOpened = null;
    }

    #endregion

    #region Historical Locations

    /// <summary>
    /// Loads historical locations for the selected date using current viewport bounds.
    /// Uses query-in-progress guard to prevent overlapping queries.
    /// </summary>
    public async Task LoadHistoricalLocationsAsync()
    {
        _logger.LogDebug("LoadHistoricalLocationsAsync START");

        if (_callbacks?.SelectedGroup == null)
        {
            _logger.LogDebug("LoadHistoricalLocationsAsync - No group, returning");
            return;
        }

        // Prevent overlapping queries - critical for preventing UI freeze
        if (_isQueryInProgress)
        {
            _logger.LogDebug("LoadHistoricalLocationsAsync - Query in progress, skipping");
            return;
        }

        _isQueryInProgress = true;

        // Use cached viewport bounds - no Mapsui access during date navigation
        double minLng = -180, minLat = -90, maxLng = 180, maxLat = 90;
        double zoomLevel = 10;

        if (_callbacks.CachedViewportBounds.HasValue)
        {
            minLng = _callbacks.CachedViewportBounds.Value.MinLon;
            minLat = _callbacks.CachedViewportBounds.Value.MinLat;
            maxLng = _callbacks.CachedViewportBounds.Value.MaxLon;
            maxLat = _callbacks.CachedViewportBounds.Value.MaxLat;
            zoomLevel = _callbacks.CachedViewportBounds.Value.ZoomLevel;
            _logger.LogDebug("Using cached viewport bounds: ({MinLng},{MinLat}) to ({MaxLng},{MaxLat}) zoom {Zoom}",
                minLng, minLat, maxLng, maxLat, zoomLevel);
        }
        else
        {
            _logger.LogDebug("No cached bounds, using world extent");
        }

        // Capture all values needed for background work while still on main thread
        var visibleUserIds = _callbacks.Members
            .Where(m => m.IsVisibleOnMap)
            .Select(m => m.UserId)
            .ToList();

        var groupId = _callbacks.SelectedGroup.Id;
        var selectedDate = SelectedDate;
        var isMapView = _callbacks.IsMapView;
        var memberColors = _callbacks.Members.ToDictionary(m => m.UserId, m => m.ColorHex ?? "#4285F4");

        var request = new GroupLocationsQueryRequest
        {
            MinLng = minLng,
            MaxLng = maxLng,
            MinLat = minLat,
            MaxLat = maxLat,
            ZoomLevel = zoomLevel,
            UserIds = visibleUserIds.Count > 0 ? visibleUserIds : null,
            DateType = "day",
            Year = selectedDate.Year,
            Month = selectedDate.Month,
            Day = selectedDate.Day,
            PageSize = 500
        };

        _logger.LogInformation("Loading historical locations for {Date}", selectedDate);

        try
        {
            _logger.LogDebug("LoadHistoricalLocationsAsync - Calling API");
            var response = await _groupsService.QueryLocationsAsync(groupId, request).ConfigureAwait(false);
            _logger.LogDebug("LoadHistoricalLocationsAsync - API returned");

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count}/{Total} historical locations",
                    response.ReturnedItems, response.TotalItems);

                // Update map on main thread (fire-and-forget)
                if (isMapView && _callbacks.HistoricalLocationsLayer != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _callbacks.UpdateHistoricalLocationMarkers(response.Results, memberColors);
                    });
                }
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading historical locations");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LoadHistoricalLocationsAsync - Cancelled");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading historical locations");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading historical locations");
        }
        finally
        {
            _isQueryInProgress = false;
            _logger.LogDebug("LoadHistoricalLocationsAsync END");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Data captured on main thread for date navigation.
    /// Avoids cross-thread collection access issues.
    /// </summary>
    private sealed record NavigationData(
        DateTime NewDate,
        Guid GroupId,
        List<string> VisibleUserIds,
        Dictionary<string, string> MemberColors,
        (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? ViewportBounds);

    /// <summary>
    /// Captures all data needed for navigation on the main thread.
    /// This avoids cross-thread access to ObservableCollection which causes delays.
    /// </summary>
    private NavigationData? CaptureNavigationData(DateTime newDate)
    {
        if (_callbacks?.SelectedGroup == null) return null;

        var visibleUserIds = _callbacks.Members
            .Where(m => m.IsVisibleOnMap)
            .Select(m => m.UserId)
            .ToList();

        var memberColors = _callbacks.Members
            .ToDictionary(m => m.UserId, m => m.ColorHex ?? "#4285F4");

        return new NavigationData(
            newDate,
            _callbacks.SelectedGroup.Id,
            visibleUserIds,
            memberColors,
            _callbacks.CachedViewportBounds);
    }

    /// <summary>
    /// Core date navigation - runs on background thread via Task.Run.
    /// All UI updates via MainThread.BeginInvokeOnMainThread (fire-and-forget).
    /// Uses pre-captured data to avoid cross-thread collection access.
    /// SSE stays running but events are ignored when viewing historical dates.
    /// </summary>
    private async Task NavigateToDateAsync(NavigationData? data)
    {
        if (data == null || _callbacks == null) return;

        var newDate = data.NewDate;
        bool isToday = newDate.Date == DateTime.Today;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogDebug("[Timing] NavigateToDate START");

        // Update UI properties first - this sets IsToday which controls SSE event processing
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectedDate = newDate;
            OnPropertyChanged(nameof(IsToday));
            OnPropertyChanged(nameof(SelectedDateText));
            OnPropertyChanged(nameof(DateButtonText));
        });

        if (isToday)
        {
            // Clear historical markers on main thread (fire-and-forget)
            MainThread.BeginInvokeOnMainThread(() => _callbacks.ClearHistoricalLocations());

            // Ensure SSE is running - fire and forget, don't wait for connection
            _ = _callbacks.EnsureSseConnectedAsync();

            // Refresh locations immediately - don't wait for SSE
            await _callbacks.RefreshLocationsAsync().ConfigureAwait(false);

            _logger.LogDebug("[Timing] NavigateToDate completed in {Ms}ms (today mode)", sw.ElapsedMilliseconds);
        }
        else
        {
            // Historical mode: SSE stays running but events are ignored (IsToday check in handlers)
            _logger.LogDebug("[Timing] Starting LoadHistoricalLocationsWithDataAsync at {Ms}ms", sw.ElapsedMilliseconds);
            await LoadHistoricalLocationsWithDataAsync(data).ConfigureAwait(false);
            _logger.LogDebug("[Timing] NavigateToDate completed in {Ms}ms (historical mode)", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Loads historical locations using pre-captured navigation data.
    /// No cross-thread collection access - all data was captured on main thread.
    /// </summary>
    private async Task LoadHistoricalLocationsWithDataAsync(NavigationData data)
    {
        if (_isQueryInProgress || _callbacks == null) return;

        _isQueryInProgress = true;

        try
        {
            var date = data.NewDate;
            double minLng = -180, minLat = -90, maxLng = 180, maxLat = 90;
            double zoomLevel = 10;

            if (data.ViewportBounds.HasValue)
            {
                var bounds = data.ViewportBounds.Value;
                minLng = bounds.MinLon;
                minLat = bounds.MinLat;
                maxLng = bounds.MaxLon;
                maxLat = bounds.MaxLat;
                zoomLevel = bounds.ZoomLevel;
            }

            var request = new GroupLocationsQueryRequest
            {
                MinLng = minLng, MaxLng = maxLng,
                MinLat = minLat, MaxLat = maxLat,
                ZoomLevel = zoomLevel,
                UserIds = data.VisibleUserIds.Count > 0 ? data.VisibleUserIds : null,
                DateType = "day",
                Year = date.Year, Month = date.Month, Day = date.Day,
                PageSize = 500
            };

            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("[Timing] Starting API call for historical locations for {Date}", date);

            var response = await _groupsService.QueryLocationsAsync(data.GroupId, request).ConfigureAwait(false);
            _logger.LogInformation("[Timing] API call completed in {Ms}ms", apiSw.ElapsedMilliseconds);

            if (response != null && _callbacks.HistoricalLocationsLayer != null)
            {
                _logger.LogInformation("Loaded {Count}/{Total} historical locations", response.ReturnedItems, response.TotalItems);

                var memberColors = data.MemberColors;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _callbacks.UpdateHistoricalLocationMarkers(response.Results, memberColors);
                });
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading historical locations for {Date}", data.NewDate);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading historical locations for {Date}", data.NewDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading historical locations for {Date}", data.NewDate);
        }
        finally
        {
            _isQueryInProgress = false;
        }
    }

    #endregion
}
