using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using Map = Mapsui.Map;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Collections;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the groups page showing user's groups and member locations.
/// Uses SSE (Server-Sent Events) for real-time location and membership updates.
/// </summary>
public partial class GroupsViewModel : BaseViewModel
{
    private readonly IGroupsService _groupsService;
    private readonly ISettingsService _settingsService;
    private readonly ISseClientFactory _sseClientFactory;
    private readonly IToastService _toastService;
    private readonly ILogger<GroupsViewModel> _logger;
    private readonly TripNavigationService _tripNavigationService;
    private readonly ILocationBridge _locationBridge;
    private readonly NavigationHudViewModel _navigationHudViewModel;
    private readonly IMapBuilder _mapBuilder;
    private readonly IGroupLayerService _groupLayerService;

    /// <summary>
    /// This ViewModel's private map instance.
    /// Each map-based page owns its own map to avoid layer conflicts.
    /// </summary>
    private Map? _map;

    /// <summary>
    /// Layer for displaying group member markers (live/latest locations).
    /// </summary>
    private WritableLayer? _groupMembersLayer;

    /// <summary>
    /// Layer for displaying historical location breadcrumbs.
    /// </summary>
    private WritableLayer? _historicalLocationsLayer;

    /// <summary>
    /// SSE client for consolidated group events (location + membership updates).
    /// Single client receives both location and membership events from the same stream.
    /// </summary>
    private ISseClient? _groupSseClient;

    /// <summary>
    /// Cancellation token source for SSE subscriptions.
    /// </summary>
    private CancellationTokenSource? _sseCts;

    /// <summary>
    /// Dictionary tracking last update time per user for throttling.
    /// Thread-safe for concurrent SSE event handling.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastUpdateTimes = new();

    /// <summary>
    /// Throttle interval in milliseconds for SSE updates.
    /// </summary>
    private const int ThrottleIntervalMs = 2000;

    /// <summary>
    /// Flag indicating a historical query is currently in progress.
    /// Prevents overlapping queries.
    /// </summary>
    private bool _isQueryInProgress;

    /// <summary>
    /// Cached viewport bounds. Updated only when map pans/zooms, not on date navigation.
    /// </summary>
    private (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? _cachedViewportBounds;

    /// <summary>
    /// Gets the collection of groups.
    /// </summary>
    public ObservableRangeCollection<GroupSummary> Groups { get; } = new();

    /// <summary>
    /// Gets the collection of members for the selected group.
    /// </summary>
    public ObservableRangeCollection<GroupMember> Members { get; } = new();

    /// <summary>
    /// Gets or sets the selected group.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(SelectedGroupName))]
    [NotifyPropertyChangedFor(nameof(IsFriendsGroup))]
    private GroupSummary? _selectedGroup;

    /// <summary>
    /// Gets or sets whether groups are loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingGroups;

    /// <summary>
    /// Gets or sets whether members are loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMembers;

    /// <summary>
    /// Gets or sets the error message if any.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Gets or sets whether map view is active (default: true - map is the primary view).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(ViewModeIndex))]
    [NotifyPropertyChangedFor(nameof(ShowListView))]
    [NotifyPropertyChangedFor(nameof(ShowMapView))]
    private bool _isMapView = true;

    /// <summary>
    /// Gets or sets whether the group picker popup is open.
    /// </summary>
    [ObservableProperty]
    private bool _isGroupPickerOpen;

    /// <summary>
    /// Gets or sets whether the current user's peer visibility is disabled.
    /// </summary>
    [ObservableProperty]
    private bool _myPeerVisibilityDisabled;

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

    /// <summary>
    /// Gets or sets whether the member details sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isMemberSheetOpen;

    /// <summary>
    /// Gets or sets the selected member for the details sheet.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedMemberCoordinates))]
    [NotifyPropertyChangedFor(nameof(SelectedMemberLocationTime))]
    private GroupMember? _selectedMember;

    /// <summary>
    /// Date before the picker was opened (for cancel restoration).
    /// </summary>
    private DateTime? _dateBeforePickerOpened;

    /// <summary>
    /// Gets whether list view is active.
    /// </summary>
    public bool IsListView => !IsMapView;

    /// <summary>
    /// Gets whether to show the list view.
    /// </summary>
    public bool ShowListView => IsListView;

    /// <summary>
    /// Gets whether to show the map view.
    /// </summary>
    public bool ShowMapView => IsMapView;

    /// <summary>
    /// Gets the count of members visible on the map.
    /// </summary>
    public int VisibleMemberCount => Members.Count(m => m.IsVisibleOnMap);

    /// <summary>
    /// Gets the formatted selected date text.
    /// </summary>
    public string SelectedDateText => SelectedDate.Date == DateTime.Today
        ? "Today"
        : SelectedDate.ToString("ddd, MMM d, yyyy");

    /// <summary>
    /// Gets whether the selected date is today.
    /// </summary>
    public bool IsToday => SelectedDate.Date == DateTime.Today;

    /// <summary>
    /// Gets whether to show the historical toggle (only visible when viewing today).
    /// </summary>
    public bool ShowHistoricalToggle => IsToday;

    /// <summary>
    /// Gets whether the selected group is a Friends group (shows peer visibility toggle).
    /// </summary>
    public bool IsFriendsGroup => string.Equals(SelectedGroup?.GroupType, "Friends", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the date button text (for consistency with TimelinePage).
    /// </summary>
    public string DateButtonText => SelectedDate.Date == DateTime.Today
        ? "Today"
        : SelectedDate.ToString("MMM d, yyyy");

    /// <summary>
    /// Gets the selected member's coordinates as text.
    /// </summary>
    public string SelectedMemberCoordinates => SelectedMember?.LastLocation != null
        ? $"{SelectedMember.LastLocation.Latitude:F6}, {SelectedMember.LastLocation.Longitude:F6}"
        : "N/A";

    /// <summary>
    /// Gets the selected member's location time as text.
    /// </summary>
    public string SelectedMemberLocationTime => SelectedMember?.LastLocation != null
        ? SelectedMember.LastLocation.Timestamp.ToLocalTime().ToString("ddd, MMM d yyyy HH:mm")
        : "N/A";

    /// <summary>
    /// Gets or sets the view mode index (0=List, 1=Map) for SfSegmentedControl binding.
    /// </summary>
    public int ViewModeIndex
    {
        get => IsMapView ? 1 : 0;
        set
        {
            var newIsMapView = value == 1;
            if (IsMapView != newIsMapView)
            {
                IsMapView = newIsMapView;
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }
            }
        }
    }

    /// <summary>
    /// Gets the map instance for binding.
    /// Groups owns its own map instance to avoid layer conflicts with other pages.
    /// </summary>
    public Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets whether API is configured.
    /// </summary>
    public bool IsConfigured => _settingsService.IsConfigured;

    /// <summary>
    /// Gets whether a group is selected.
    /// </summary>
    public bool HasSelectedGroup => SelectedGroup != null;

    /// <summary>
    /// Gets the selected group name.
    /// </summary>
    public string SelectedGroupName => SelectedGroup?.Name ?? "Select a group";

    /// <summary>
    /// Creates a new instance of GroupsViewModel.
    /// </summary>
    /// <param name="groupsService">Service for group operations.</param>
    /// <param name="settingsService">Service for application settings.</param>
    /// <param name="sseClientFactory">Factory for creating SSE clients.</param>
    /// <param name="toastService">Toast notification service.</param>
    /// <param name="tripNavigationService">Navigation service for routing.</param>
    /// <param name="locationBridge">Location bridge for current position.</param>
    /// <param name="navigationHudViewModel">Navigation HUD for display.</param>
    /// <param name="mapBuilder">Map builder for creating isolated map instances.</param>
    /// <param name="groupLayerService">Service for group layer rendering.</param>
    /// <param name="logger">Logger instance.</param>
    public GroupsViewModel(
        IGroupsService groupsService,
        ISettingsService settingsService,
        ISseClientFactory sseClientFactory,
        IToastService toastService,
        TripNavigationService tripNavigationService,
        ILocationBridge locationBridge,
        NavigationHudViewModel navigationHudViewModel,
        IMapBuilder mapBuilder,
        IGroupLayerService groupLayerService,
        ILogger<GroupsViewModel> logger)
    {
        _groupsService = groupsService;
        _settingsService = settingsService;
        _sseClientFactory = sseClientFactory;
        _toastService = toastService;
        _tripNavigationService = tripNavigationService;
        _locationBridge = locationBridge;
        _navigationHudViewModel = navigationHudViewModel;
        _mapBuilder = mapBuilder;
        _groupLayerService = groupLayerService;
        _logger = logger;
        Title = "Groups";
    }

    /// <summary>
    /// Creates and configures this ViewModel's private map instance.
    /// </summary>
    private Map CreateMap()
    {
        // Create layers for Groups-specific features using layer service names
        _groupMembersLayer = _mapBuilder.CreateLayer(_groupLayerService.GroupMembersLayerName);
        _historicalLocationsLayer = _mapBuilder.CreateLayer(_groupLayerService.HistoricalLocationsLayerName);

        // Create map with tile source and our layers
        var map = _mapBuilder.CreateMap(
            _historicalLocationsLayer,  // Historical first (below members)
            _groupMembersLayer);         // Members on top

        _logger.LogDebug("Created Groups map with layers: {Layers}",
            string.Join(", ", map.Layers.Select(l => l.Name)));

        return map;
    }

    /// <summary>
    /// Called when the selected group changes.
    /// </summary>
    partial void OnSelectedGroupChanged(GroupSummary? value)
    {
        if (value != null)
        {
            // Update page title to show group name
            Title = value.Name;

            // Persist the selection
            _settingsService.LastSelectedGroupId = value.Id.ToString();
            _settingsService.LastSelectedGroupName = value.Name;

            // Abandon old SSE (don't stop - it blocks for 10+ seconds)
            // Old connections will timeout naturally; new ones start fresh
            AbandonSseClients();

            SafeFireAndForget(LoadMembersAndStartSseAsync(), "LoadMembersAndStartSse");
        }
        else
        {
            Title = "Groups";
            AbandonSseClients();
            Members.Clear();
        }
    }

    /// <summary>
    /// Called when the selected date changes.
    /// Updates computed properties only - data loading is handled by commands.
    /// </summary>
    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(ShowHistoricalToggle));
        // Data loading is now handled directly by PreviousDayAsync/NextDayAsync/TodayAsync
        // to provide proper loading state and visual feedback

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
        if (!IsToday)
            return;

        if (value)
        {
            // Toggle ON: Load today's historical locations
            _logger.LogInformation("[Groups] Historical toggle ON - loading today's historical locations");
            SafeFireAndForget(LoadHistoricalLocationsAsync(), "LoadTodayHistoricalLocations");
        }
        else
        {
            // Toggle OFF: Clear historical locations and show only live markers
            _logger.LogInformation("[Groups] Historical toggle OFF - clearing historical locations");
            ClearHistoricalLocations();
        }
    }

    /// <summary>
    /// Clears historical location markers from the map.
    /// </summary>
    private void ClearHistoricalLocations()
    {
        _historicalLocationsLayer?.Clear();
        _historicalLocationsLayer?.DataHasChanged();
    }

    /// <summary>
    /// Clears group member markers from the map.
    /// </summary>
    private void ClearGroupMembers()
    {
        _groupMembersLayer?.Clear();
        _groupMembersLayer?.DataHasChanged();
    }

    /// <summary>
    /// Executes an async task in fire-and-forget mode with error logging.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    private async void SafeFireAndForget(Task task, string operationName)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{Operation} was cancelled", operationName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {Operation}", operationName);
        }
    }

    /// <summary>
    /// Loads members and starts SSE subscriptions for the selected group.
    /// </summary>
    private async Task LoadMembersAndStartSseAsync()
    {
        await LoadMembersAsync();

        if (IsToday && SelectedGroup != null)
        {
            await StartSseSubscriptionsAsync();
        }
    }

    /// <summary>
    /// Loads the list of groups.
    /// </summary>
    [RelayCommand]
    private async Task LoadGroupsAsync()
    {
        if (IsLoadingGroups || !IsConfigured)
            return;

        try
        {
            IsLoadingGroups = true;
            ErrorMessage = null;

            var groups = await _groupsService.GetGroupsAsync();

            Groups.ReplaceRange(groups);

            // Restore last selected group or auto-select first
            if (SelectedGroup == null && Groups.Count > 0)
            {
                var lastGroupId = _settingsService.LastSelectedGroupId;
                GroupSummary? lastGroup = null;
                if (!string.IsNullOrEmpty(lastGroupId) && Guid.TryParse(lastGroupId, out var lastGuid))
                {
                    lastGroup = Groups.FirstOrDefault(g => g.Id == lastGuid);
                }
                SelectedGroup = lastGroup ?? Groups[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load groups");
            ErrorMessage = "Failed to load groups. Please check your connection and try again.";
        }
        finally
        {
            IsLoadingGroups = false;
        }
    }

    /// <summary>
    /// Loads members for the selected group.
    /// </summary>
    [RelayCommand]
    private async Task LoadMembersAsync()
    {
        if (SelectedGroup == null || IsLoadingMembers)
            return;

        try
        {
            IsLoadingMembers = true;
            ErrorMessage = null;

            var groupId = SelectedGroup.Id;

            // Load members
            var members = await _groupsService.GetGroupMembersAsync(groupId);
            _logger.LogInformation("[Groups] Loaded {Count} members for group {GroupId}", members.Count, groupId);
            foreach (var m in members)
            {
                _logger.LogInformation("[Groups] Member: {UserId} - {DisplayName}, IsSelf={IsSelf}, Color={Color}",
                    m.UserId, m.DisplayText, m.IsSelf, m.ColorHex);
            }

            // Load latest locations
            var locations = await _groupsService.GetLatestLocationsAsync(groupId);
            _logger.LogInformation("[Groups] Loaded {Count} locations for group {GroupId}", locations.Count, groupId);
            foreach (var kvp in locations)
            {
                _logger.LogInformation("[Groups] Location for {UserId}: Lat={Lat}, Lon={Lon}, IsLive={IsLive}",
                    kvp.Key, kvp.Value.Latitude, kvp.Value.Longitude, kvp.Value.IsLive);
            }

            // Find current user and update visibility state
            var currentUser = members.FirstOrDefault(m => m.IsSelf);
            if (currentUser != null)
            {
                MyPeerVisibilityDisabled = currentUser.OrgPeerVisibilityAccessDisabled;
            }

            // Merge locations into members
            var membersWithLocation = 0;
            foreach (var member in members)
            {
                if (locations.TryGetValue(member.UserId, out var location))
                {
                    member.LastLocation = location;
                    membersWithLocation++;
                    _logger.LogDebug("[Groups] Member {UserId} has location: {Lat},{Lon} IsLive={IsLive}",
                        member.UserId, location.Latitude, location.Longitude, location.IsLive);
                }
                else
                {
                    _logger.LogDebug("[Groups] Member {UserId} ({Name}) has NO location", member.UserId, member.DisplayText);
                }
            }
            _logger.LogDebug("[Groups] {Count}/{Total} members have locations", membersWithLocation, members.Count);

            Members.ReplaceRange(members.OrderByDescending(m => m.LastLocation?.IsLive ?? false)
                                        .ThenBy(m => m.DisplayText));

            // Update map markers if in map view
            if (IsMapView)
            {
                UpdateMapMarkers();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load members for group {GroupId}", SelectedGroup?.Id);
            ErrorMessage = "Failed to load members. Please check your connection and try again.";
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    /// <summary>
    /// Refreshes member locations without full reload.
    /// Thread-safe: can be called from background thread.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLocationsAsync()
    {
        if (SelectedGroup == null)
            return;

        try
        {
            // Do I/O on background (safe from any thread)
            var groupId = SelectedGroup.Id;
            var locations = await _groupsService.GetLatestLocationsAsync(groupId).ConfigureAwait(false);

            // Update UI on main thread to avoid cross-thread collection access
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Update existing members with new locations
                foreach (var member in Members)
                {
                    if (locations.TryGetValue(member.UserId, out var location))
                    {
                        member.LastLocation = location;
                    }
                }

                // Trigger UI refresh by re-sorting using batch operation
                var sorted = Members.OrderByDescending(m => m.LastLocation?.IsLive ?? false)
                                   .ThenBy(m => m.DisplayText)
                                   .ToList();

                Members.ReplaceRange(sorted);

                // Update map markers if in map view
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refresh locations error");
        }
    }

    /// <summary>
    /// Loads historical locations for the selected date using current viewport bounds.
    /// Uses query-in-progress guard to prevent overlapping queries.
    /// IMPORTANT: This method must be called from main thread to capture viewport bounds synchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [RelayCommand]
    private async Task LoadHistoricalLocationsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("LoadHistoricalLocationsAsync START");

        if (SelectedGroup == null)
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
        // NOTE: Intentionally NOT setting IsBusy here - loading overlay causes map re-render
        // which triggers ViewportChanged and blocks the UI for seconds.
        // The old app didn't use loading indicators for date navigation and was instant.

        // Use cached viewport bounds - no Mapsui access during date navigation
        // Bounds are updated by page when map pans/zooms
        double minLng = -180, minLat = -90, maxLng = 180, maxLat = 90;
        double zoomLevel = 10;

        if (_cachedViewportBounds.HasValue)
        {
            minLng = _cachedViewportBounds.Value.MinLon;
            minLat = _cachedViewportBounds.Value.MinLat;
            maxLng = _cachedViewportBounds.Value.MaxLon;
            maxLat = _cachedViewportBounds.Value.MaxLat;
            zoomLevel = _cachedViewportBounds.Value.ZoomLevel;
            _logger.LogDebug("Using cached viewport bounds: ({MinLng},{MinLat}) to ({MaxLng},{MaxLat}) zoom {Zoom}",
                minLng, minLat, maxLng, maxLat, zoomLevel);
        }
        else
        {
            _logger.LogDebug("No cached bounds, using world extent");
        }

        // Capture all values needed for background work while still on main thread
        var visibleUserIds = Members
            .Where(m => m.IsVisibleOnMap)
            .Select(m => m.UserId)
            .ToList();

        var groupId = SelectedGroup.Id;
        var selectedDate = SelectedDate;
        var isMapView = IsMapView;
        var memberColors = Members.ToDictionary(m => m.UserId, m => m.ColorHex ?? "#4285F4");

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
            // Now do the async I/O - this yields to let UI respond
            _logger.LogDebug("LoadHistoricalLocationsAsync - Calling API");
            var response = await _groupsService.QueryLocationsAsync(groupId, request, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("LoadHistoricalLocationsAsync - API returned");

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count}/{Total} historical locations",
                    response.ReturnedItems, response.TotalItems);

                // Update map on main thread (fire-and-forget)
                if (isMapView && _historicalLocationsLayer != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _groupLayerService.UpdateHistoricalLocationMarkers(
                            _historicalLocationsLayer, response.Results, memberColors);
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LoadHistoricalLocationsAsync - Cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load historical locations");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ErrorMessage = "Failed to load historical locations";
            });
        }
        finally
        {
            _isQueryInProgress = false;
            _logger.LogDebug("LoadHistoricalLocationsAsync END");
        }
    }

    /// <summary>
    /// Selects a group by ID.
    /// </summary>
    [RelayCommand]
    private void SelectGroup(GroupSummary? group)
    {
        SelectedGroup = group;
    }

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
        if (SelectedGroup == null) return null;

        var visibleUserIds = Members
            .Where(m => m.IsVisibleOnMap)
            .Select(m => m.UserId)
            .ToList();

        var memberColors = Members
            .ToDictionary(m => m.UserId, m => m.ColorHex ?? "#4285F4");

        return new NavigationData(
            newDate,
            SelectedGroup.Id,
            visibleUserIds,
            memberColors,
            _cachedViewportBounds);
    }

    /// <summary>
    /// Core date navigation - runs on background thread via Task.Run.
    /// All UI updates via MainThread.BeginInvokeOnMainThread (fire-and-forget).
    /// Uses pre-captured data to avoid cross-thread collection access.
    /// SSE stays running but events are ignored when viewing historical dates.
    /// </summary>
    private async Task NavigateToDateAsync(NavigationData? data)
    {
        if (data == null) return;

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
            MainThread.BeginInvokeOnMainThread(ClearHistoricalLocations);

            // Ensure SSE is running - fire and forget, don't wait for connection
            // SSE will reconnect automatically if needed
            _ = EnsureSseConnectedAsync();

            // Refresh locations immediately - don't wait for SSE
            await RefreshLocationsAsync().ConfigureAwait(false);

            _logger.LogDebug("[Timing] NavigateToDate completed in {Ms}ms (today mode)", sw.ElapsedMilliseconds);
        }
        else
        {
            // Historical mode: SSE stays running but events are ignored (IsToday check in handlers)
            // Load historical data
            _logger.LogDebug("[Timing] Starting LoadHistoricalLocationsWithDataAsync at {Ms}ms", sw.ElapsedMilliseconds);
            await LoadHistoricalLocationsWithDataAsync(data).ConfigureAwait(false);
            _logger.LogDebug("[Timing] NavigateToDate completed in {Ms}ms (historical mode)", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Loads historical locations for a specific date without triggering property changes.
    /// Thread-safe: designed to be called from background thread.
    /// </summary>
    private async Task LoadHistoricalLocationsForDateAsync(DateTime date)
    {
        if (SelectedGroup == null || _isQueryInProgress) return;

        _isQueryInProgress = true;

        try
        {
            // Capture all values needed - quick property/field reads
            var groupId = SelectedGroup.Id;
            double minLng = -180, minLat = -90, maxLng = 180, maxLat = 90;
            double zoomLevel = 10;

            if (_cachedViewportBounds.HasValue)
            {
                minLng = _cachedViewportBounds.Value.MinLon;
                minLat = _cachedViewportBounds.Value.MinLat;
                maxLng = _cachedViewportBounds.Value.MaxLon;
                maxLat = _cachedViewportBounds.Value.MaxLat;
                zoomLevel = _cachedViewportBounds.Value.ZoomLevel;
            }

            // Snapshot collection data (ToList/ToDictionary creates a copy)
            var visibleUserIds = Members.Where(m => m.IsVisibleOnMap).Select(m => m.UserId).ToList();
            var memberColors = Members.ToDictionary(m => m.UserId, m => m.ColorHex ?? "#4285F4");

            var request = new GroupLocationsQueryRequest
            {
                MinLng = minLng, MaxLng = maxLng,
                MinLat = minLat, MaxLat = maxLat,
                ZoomLevel = zoomLevel,
                UserIds = visibleUserIds.Count > 0 ? visibleUserIds : null,
                DateType = "day",
                Year = date.Year, Month = date.Month, Day = date.Day,
                PageSize = 500
            };

            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("[Timing] Starting API call for historical locations for {Date}", date);

            // Do I/O - this is the slow part
            var response = await _groupsService.QueryLocationsAsync(groupId, request).ConfigureAwait(false);
            _logger.LogInformation("[Timing] API call completed in {Ms}ms", apiSw.ElapsedMilliseconds);

            if (response != null && _historicalLocationsLayer != null)
            {
                _logger.LogInformation("Loaded {Count}/{Total} historical locations", response.ReturnedItems, response.TotalItems);

                // Update map on main thread (fire-and-forget, no waiting)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _groupLayerService.UpdateHistoricalLocationMarkers(
                        _historicalLocationsLayer, response.Results, memberColors);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load historical locations for {Date}", date);
        }
        finally
        {
            _isQueryInProgress = false;
        }
    }

    /// <summary>
    /// Loads historical locations using pre-captured navigation data.
    /// No cross-thread collection access - all data was captured on main thread.
    /// </summary>
    private async Task LoadHistoricalLocationsWithDataAsync(NavigationData data)
    {
        if (_isQueryInProgress) return;

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

            // Do I/O - now no longer blocked by collection access
            var response = await _groupsService.QueryLocationsAsync(data.GroupId, request).ConfigureAwait(false);
            _logger.LogInformation("[Timing] API call completed in {Ms}ms", apiSw.ElapsedMilliseconds);

            if (response != null && _historicalLocationsLayer != null)
            {
                _logger.LogInformation("Loaded {Count}/{Total} historical locations", response.ReturnedItems, response.TotalItems);

                // Capture colors for main thread use
                var memberColors = data.MemberColors;

                // Update map on main thread (fire-and-forget, no waiting)
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _groupLayerService.UpdateHistoricalLocationMarkers(
                        _historicalLocationsLayer, response.Results, memberColors);
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load historical locations for {Date}", data.NewDate);
        }
        finally
        {
            _isQueryInProgress = false;
        }
    }

    /// <summary>
    /// Handles date change by loading appropriate data with visual feedback.
    /// Follows the Timeline page pattern: load data once per date change, no viewport-based requerying.
    /// Runs heavy work on background thread to avoid UI freeze.
    /// SSE stays running but events are ignored when viewing historical dates.
    /// </summary>
    private async Task HandleDateChangeAsync()
    {
        _logger.LogDebug("HandleDateChangeAsync START - SelectedGroup: {Group}, IsToday: {IsToday}",
            SelectedGroup?.Name ?? "null", IsToday);

        if (SelectedGroup == null)
        {
            _logger.LogDebug("HandleDateChangeAsync - No group selected, returning");
            return;
        }

        if (IsToday)
        {
            _logger.LogDebug("HandleDateChangeAsync - Switching to live mode");
            // Switch to live mode - clear historical markers
            ClearHistoricalLocations();
            // Ensure SSE is running - fire and forget
            _ = EnsureSseConnectedAsync();
            // Refresh live locations immediately
            await RefreshLocationsAsync();
        }
        else
        {
            _logger.LogDebug("HandleDateChangeAsync - Switching to historical mode");
            // Historical mode: SSE stays running but events are ignored (IsToday check in handlers)
            // Load historical data
            await LoadHistoricalLocationsAsync().ConfigureAwait(false);
        }

        _logger.LogDebug("HandleDateChangeAsync END");
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
    /// Toggles the current user's peer visibility in the selected group.
    /// </summary>
    [RelayCommand]
    private async Task TogglePeerVisibilityAsync()
    {
        if (SelectedGroup == null)
            return;

        try
        {
            var newDisabledState = !MyPeerVisibilityDisabled;
            _logger.LogInformation("Toggling peer visibility: disabled={Disabled}", newDisabledState);

            var success = await _groupsService.UpdatePeerVisibilityAsync(SelectedGroup.Id, newDisabledState);

            if (success)
            {
                MyPeerVisibilityDisabled = newDisabledState;

                // Update the current user's member record
                var currentUser = Members.FirstOrDefault(m => m.IsSelf);
                if (currentUser != null)
                {
                    currentUser.OrgPeerVisibilityAccessDisabled = newDisabledState;
                }

                _logger.LogInformation("Peer visibility updated successfully: disabled={Disabled}", newDisabledState);
            }
            else
            {
                _logger.LogWarning("Failed to update peer visibility");
                ErrorMessage = "Failed to update peer visibility";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling peer visibility");
            ErrorMessage = "Failed to update peer visibility";
        }
    }

    /// <summary>
    /// Toggles between list and map view.
    /// </summary>
    [RelayCommand]
    private void ToggleView()
    {
        IsMapView = !IsMapView;
        if (IsMapView)
        {
            UpdateMapMarkers();
        }
    }

    /// <summary>
    /// Opens the group picker popup.
    /// </summary>
    [RelayCommand]
    private void OpenGroupPicker()
    {
        IsGroupPickerOpen = true;
    }

    /// <summary>
    /// Closes the group picker popup.
    /// </summary>
    [RelayCommand]
    private void CloseGroupPicker()
    {
        IsGroupPickerOpen = false;
    }

    /// <summary>
    /// Confirmation threshold for select all operation.
    /// If more members than this, user will be prompted to confirm.
    /// </summary>
    private const int SelectAllConfirmationThreshold = 10;

    /// <summary>
    /// Selects all members to show on map.
    /// Shows confirmation dialog if member count exceeds threshold.
    /// </summary>
    [RelayCommand]
    private async Task SelectAllMembersAsync()
    {
        // Check if confirmation is needed
        if (Members.Count > SelectAllConfirmationThreshold)
        {
            var currentPage = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (currentPage != null)
            {
                var confirmed = await currentPage.DisplayAlertAsync(
                    "Select All Members",
                    $"You're about to show {Members.Count} members on the map. This may affect performance. Continue?",
                    "Yes",
                    "Cancel");

                if (!confirmed)
                    return;
            }
        }

        foreach (var member in Members)
        {
            member.IsVisibleOnMap = true;
        }
        OnPropertyChanged(nameof(VisibleMemberCount));
        UpdateMapMarkers();
    }

    /// <summary>
    /// Deselects all members from map.
    /// </summary>
    [RelayCommand]
    private void DeselectAllMembers()
    {
        foreach (var member in Members)
        {
            member.IsVisibleOnMap = false;
        }
        OnPropertyChanged(nameof(VisibleMemberCount));
        UpdateMapMarkers();
    }

    /// <summary>
    /// Refreshes map markers (called when member visibility changes via checkbox).
    /// </summary>
    [RelayCommand]
    private void RefreshMapMarkers()
    {
        OnPropertyChanged(nameof(VisibleMemberCount));
        if (IsMapView)
        {
            UpdateMapMarkers();
        }
    }

    #region Member Details Sheet Commands

    /// <summary>
    /// Shows member details in the bottom sheet.
    /// </summary>
    /// <param name="member">The member to show details for.</param>
    [RelayCommand]
    private void ShowMemberDetails(GroupMember? member)
    {
        if (member == null) return;

        SelectedMember = member;
        IsMemberSheetOpen = true;
    }

    /// <summary>
    /// Shows member details by user ID (called from map tap handler).
    /// </summary>
    /// <param name="userId">The user ID to show details for.</param>
    public void ShowMemberDetailsByUserId(string userId)
    {
        var member = Members.FirstOrDefault(m => m.UserId == userId);
        if (member != null)
        {
            ShowMemberDetails(member);
        }
    }

    /// <summary>
    /// Closes the member details sheet.
    /// </summary>
    [RelayCommand]
    private void CloseMemberSheet()
    {
        IsMemberSheetOpen = false;
        SelectedMember = null;
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

    /// <summary>
    /// Opens the selected member's location in Google Maps.
    /// </summary>
    [RelayCommand]
    private async Task OpenInMapsAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var location = new Microsoft.Maui.Devices.Sensors.Location(
                SelectedMember.LastLocation.Latitude,
                SelectedMember.LastLocation.Longitude);
            var options = new MapLaunchOptions { Name = SelectedMember.DisplayText };
            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open maps");
        }
    }

    /// <summary>
    /// Searches Wikipedia for nearby places.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{SelectedMember.LastLocation.Latitude},{SelectedMember.LastLocation.Longitude}";
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Wikipedia");
        }
    }

    /// <summary>
    /// Copies coordinates to clipboard with feedback.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (SelectedMember?.LastLocation == null)
        {
            await _toastService.ShowWarningAsync("No location available");
            return;
        }

        try
        {
            var coords = $"{SelectedMember.LastLocation.Latitude:F6}, {SelectedMember.LastLocation.Longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            await _toastService.ShowAsync("Coordinates copied");
            _logger.LogInformation("Coordinates copied to clipboard: {Coords}", coords);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy coordinates");
            await _toastService.ShowErrorAsync("Failed to copy coordinates");
        }
    }

    /// <summary>
    /// Shares the member's location.
    /// </summary>
    [RelayCommand]
    private async Task ShareLocationAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var googleMapsUrl = $"https://www.google.com/maps?q={SelectedMember.LastLocation.Latitude:F6},{SelectedMember.LastLocation.Longitude:F6}";
            var text = $"{SelectedMember.DisplayText}'s location:\n{googleMapsUrl}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Location",
                Text = text
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share location");
        }
    }

    /// <summary>
    /// Navigates to the member's location using OSRM routing with straight line fallback.
    /// Calculates route from current location and displays it on the main map.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToMemberAsync()
    {
        if (SelectedMember?.LastLocation == null)
        {
            await _toastService.ShowWarningAsync("No location available");
            return;
        }

        // Get current location
        var currentLocation = _locationBridge.LastLocation;
        if (currentLocation == null)
        {
            await _toastService.ShowWarningAsync("Waiting for your location...");
            return;
        }

        // Ask user for navigation method using the styled picker
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        // Get the GroupsPage to access the navigation picker
        var groupsPage = page as Views.GroupsPage ??
            (Shell.Current?.CurrentPage as Views.GroupsPage);

        Views.Controls.NavigationMethod? navMethod = null;
        if (groupsPage != null)
        {
            navMethod = await groupsPage.ShowNavigationPickerAsync();
        }
        else
        {
            // Fallback to action sheet if page reference not available
            var result = await page.DisplayActionSheetAsync(
                "Navigate by", "Cancel", null,
                "🚶 Walk", "🚗 Drive", "🚴 Bike", "📍 External Maps");

            navMethod = result switch
            {
                "🚶 Walk" => Views.Controls.NavigationMethod.Walk,
                "🚗 Drive" => Views.Controls.NavigationMethod.Drive,
                "🚴 Bike" => Views.Controls.NavigationMethod.Bike,
                "📍 External Maps" => Views.Controls.NavigationMethod.ExternalMaps,
                _ => null
            };
        }

        if (navMethod == null)
            return;

        // Handle external maps
        if (navMethod == Views.Controls.NavigationMethod.ExternalMaps)
        {
            await OpenExternalMapsAsync(
                SelectedMember.LastLocation.Latitude,
                SelectedMember.LastLocation.Longitude);
            return;
        }

        // Map selection to OSRM profile
        var osrmProfile = navMethod switch
        {
            Views.Controls.NavigationMethod.Walk => "foot",
            Views.Controls.NavigationMethod.Drive => "car",
            Views.Controls.NavigationMethod.Bike => "bike",
            _ => "foot"
        };

        try
        {
            IsBusy = true;

            var destLat = SelectedMember.LastLocation.Latitude;
            var destLon = SelectedMember.LastLocation.Longitude;
            var destName = SelectedMember.DisplayText ?? "Member";

            _logger.LogInformation("Calculating {Mode} route to {Member} at {Lat},{Lon}", osrmProfile, destName, destLat, destLon);

            // Calculate route using OSRM with straight line fallback
            var route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
                currentLocation.Latitude,
                currentLocation.Longitude,
                destLat,
                destLon,
                destName,
                osrmProfile);

            // Close bottom sheet before navigating
            IsMemberSheetOpen = false;

            // Set source page for returning after navigation stops
            _navigationHudViewModel.SourcePageRoute = "//groups";

            // Navigate to main map with route parameter
            // MainViewModel will receive and display the route via IQueryAttributable
            var navParams = new Dictionary<string, object>
            {
                ["NavigationRoute"] = route
            };
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//main", navParams);
            }

            _logger.LogInformation("Started navigation to {Member}: {Distance:F1}km",
                destName, route.TotalDistanceMeters / 1000);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start navigation");
            await _toastService.ShowErrorAsync("Failed to start navigation");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens external maps app for navigation.
    /// </summary>
    private async Task OpenExternalMapsAsync(double lat, double lon)
    {
        try
        {
            IsMemberSheetOpen = false;

            var location = new Location(lat, lon);
            var options = new MapLaunchOptions { NavigationMode = NavigationMode.Walking };

            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (Exception ex)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch
            {
                _logger.LogError(ex, "Failed to open external maps");
                await _toastService.ShowErrorAsync("Unable to open maps");
            }
        }
    }

    #endregion

    /// <summary>
    /// Updates the cached viewport bounds. Called from page when map viewport changes.
    /// </summary>
    public void UpdateCachedViewportBounds()
    {
        if (_map == null) return;

        var bounds = _mapBuilder.GetViewportBounds(_map);
        if (bounds.HasValue)
        {
            _cachedViewportBounds = bounds;
            _logger.LogDebug("Viewport bounds cached: ({MinLon},{MinLat}) to ({MaxLon},{MaxLat}) zoom {Zoom}",
                bounds.Value.MinLon, bounds.Value.MinLat, bounds.Value.MaxLon, bounds.Value.MaxLat, bounds.Value.ZoomLevel);
        }
    }

    /// <summary>
    /// Clears the live GPS location indicator from the map.
    /// Groups page has its own map - no location indicator to clear.
    /// This method is kept for interface compatibility but is now a no-op.
    /// </summary>
    public void ClearLiveLocationIndicator()
    {
        // Groups has its own map without a location layer - nothing to clear
        _logger.LogDebug("[Groups] ClearLiveLocationIndicator - no-op (own map)");
    }

    /// <summary>
    /// Updates the map markers with current member locations.
    /// </summary>
    private void UpdateMapMarkers()
    {
        var total = Members.Count;
        var withLocation = Members.Count(m => m.LastLocation != null);
        var visible = Members.Count(m => m.IsVisibleOnMap);
        var filtered = Members.Count(m => m.LastLocation != null && m.IsVisibleOnMap);

        _logger.LogDebug("[Groups] UpdateMapMarkers: total={Total}, withLocation={WithLocation}, visible={Visible}, filtered={Filtered}",
            total, withLocation, visible, filtered);

        if (_groupMembersLayer == null) return;

        var memberLocations = Members
            .Where(m => m.LastLocation != null && m.IsVisibleOnMap)
            .Select(m => new GroupMemberLocation
            {
                UserId = m.UserId,
                DisplayName = m.DisplayText ?? m.UserId,
                Latitude = m.LastLocation!.Latitude,
                Longitude = m.LastLocation.Longitude,
                ColorHex = m.ColorHex,
                IsLive = m.LastLocation.IsLive
            })
            .ToList();

        _logger.LogDebug("[Groups] Passing {Count} member locations to map", memberLocations.Count);
        var points = _groupLayerService.UpdateGroupMemberMarkers(_groupMembersLayer, memberLocations);

        // Auto-zoom to fit all members if there are multiple
        if (points.Count > 1 && _map != null)
        {
            _mapBuilder.ZoomToPoints(_map, points);
        }
        else if (points.Count == 1 && _map != null)
        {
            _map.Navigator.CenterOn(points[0]);
        }
    }

    #region SSE Management

    /// <summary>
    /// Abandons SSE clients without waiting for cleanup.
    /// Just unsubscribes from events and forgets references - instant, non-blocking.
    /// Old connections will timeout naturally or be GC'd.
    /// </summary>
    private void AbandonSseClients()
    {
        _logger.LogDebug("Abandoning SSE clients (non-blocking)");

        // Unsubscribe from events immediately (prevents old events reaching handlers)
        if (_groupSseClient != null)
        {
            _groupSseClient.LocationReceived -= OnLocationReceived;
            _groupSseClient.LocationDeleted -= OnLocationDeleted;
            _groupSseClient.MembershipReceived -= OnMembershipReceived;
            _groupSseClient.InviteCreated -= OnInviteCreated;
            _groupSseClient.Connected -= OnSseConnected;
            _groupSseClient.Reconnecting -= OnSseReconnecting;
        }

        // Trigger cancellation but don't wait for it (fire and forget)
        var oldCts = _sseCts;
        if (oldCts != null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { oldCts.Cancel(); oldCts.Dispose(); }
                catch { /* ignore cleanup errors */ }
            });
        }

        // Clear references - old client becomes orphaned, will be GC'd eventually
        _sseCts = null;
        _groupSseClient = null;

        // Clear throttle tracking
        _lastUpdateTimes.Clear();
    }

    /// <summary>
    /// Ensures SSE client exists. Only starts new client if null.
    /// SSE client has auto-reconnect, so we don't restart based on IsConnected.
    /// Called when navigating back to today's date.
    /// </summary>
    private async Task EnsureSseConnectedAsync()
    {
        // Check if SSE client already exists (it has auto-reconnect built in)
        // Don't check IsConnected - it may be temporarily false during reconnection
        if (_groupSseClient != null)
        {
            _logger.LogDebug("SSE client exists, skipping start (auto-reconnect handles connection)");
            return;
        }

        _logger.LogDebug("SSE client is null, starting new subscription");
        // Start SSE only if client doesn't exist
        await StartSseSubscriptionsAsync();
    }

    /// <summary>
    /// Starts SSE subscription for the selected group.
    /// Uses consolidated endpoint that delivers both location and membership events.
    /// </summary>
    private async Task StartSseSubscriptionsAsync()
    {
        if (SelectedGroup == null || !IsToday)
            return;

        // Abandon any existing subscription (instant, non-blocking)
        AbandonSseClients();

        _sseCts = new CancellationTokenSource();
        var groupId = SelectedGroup.Id.ToString();

        // Create single SSE client for consolidated group stream
        _groupSseClient = _sseClientFactory.Create();
        _groupSseClient.LocationReceived += OnLocationReceived;
        _groupSseClient.LocationDeleted += OnLocationDeleted;
        _groupSseClient.MembershipReceived += OnMembershipReceived;
        _groupSseClient.InviteCreated += OnInviteCreated;
        _groupSseClient.Connected += OnSseConnected;
        _groupSseClient.Reconnecting += OnSseReconnecting;

        _logger.LogInformation("Starting SSE subscription for group {GroupId}", groupId);

        // Start subscription in background (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _groupSseClient.SubscribeToGroupAsync(groupId, _sseCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Group SSE subscription cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Group SSE subscription error");
            }
        });
    }

    /// <summary>
    /// Stops SSE subscription.
    /// Non-blocking: cancels immediately, cleanup runs in background.
    /// </summary>
    private void StopSseSubscriptions()
    {
        _logger.LogDebug("Stopping SSE subscription");

        // Cancel ongoing operations immediately (non-blocking)
        _sseCts?.Cancel();

        // Capture references for background cleanup
        var oldCts = _sseCts;
        var oldGroupClient = _groupSseClient;

        // Clear references immediately so new subscriptions can start
        _sseCts = null;
        _groupSseClient = null;

        // Unsubscribe from events on main thread to prevent race conditions
        if (oldGroupClient != null)
        {
            oldGroupClient.LocationReceived -= OnLocationReceived;
            oldGroupClient.LocationDeleted -= OnLocationDeleted;
            oldGroupClient.MembershipReceived -= OnMembershipReceived;
            oldGroupClient.InviteCreated -= OnInviteCreated;
            oldGroupClient.Connected -= OnSseConnected;
            oldGroupClient.Reconnecting -= OnSseReconnecting;
        }

        // Dispose in background to avoid blocking main thread
        // HttpClient cleanup on Android can take 2+ seconds per connection
        _ = Task.Run(() =>
        {
            try
            {
                oldGroupClient?.Stop();
                oldGroupClient?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE client cleanup error: {Message}", ex.Message);
            }

            try
            {
                oldCts?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("SSE CTS cleanup error: {Message}", ex.Message);
            }
        });

        // Clear throttle tracking
        _lastUpdateTimes.Clear();
    }

    /// <summary>
    /// Handles location received events from SSE with throttling.
    /// </summary>
    private async void OnLocationReceived(object? sender, SseLocationEventArgs e)
    {
        // Guard against events firing after disposal
        if (IsDisposed)
            return;

        try
        {
            // Skip live updates when viewing historical data
            if (!IsToday)
            {
                _logger.LogDebug("SSE update skipped - viewing historical date");
                return;
            }

            var userId = e.Location.UserId;
            var now = DateTime.UtcNow;

            // Throttle updates - only process if enough time has passed
            if (_lastUpdateTimes.TryGetValue(userId, out var lastUpdate))
            {
                var elapsed = (now - lastUpdate).TotalMilliseconds;
                if (elapsed < ThrottleIntervalMs)
                {
                    _logger.LogDebug("SSE update throttled for {UserId} ({Elapsed}ms since last)", userId, elapsed);
                    return;
                }
            }

            _lastUpdateTimes[userId] = now;
            _logger.LogDebug("SSE location received for {UserName}", e.Location.UserName);

            // Refresh the specific member's location
            await RefreshMemberLocationAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling SSE location event");
        }
    }

    /// <summary>
    /// Handles location deleted events from SSE.
    /// When a location is deleted, refresh the member's data to get their new latest location.
    /// </summary>
    private async void OnLocationDeleted(object? sender, SseLocationDeletedEventArgs e)
    {
        // Guard against events firing after disposal
        if (IsDisposed)
            return;

        try
        {
            // Skip updates when viewing historical data
            if (!IsToday)
            {
                _logger.LogDebug("SSE location deleted skipped - viewing historical date");
                return;
            }

            var userId = e.LocationDeleted.UserId;
            _logger.LogDebug("SSE location deleted: {LocationId} for user {UserId}",
                e.LocationDeleted.LocationId, userId);

            // Refresh the specific member's location to get their new latest
            await RefreshMemberLocationAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling SSE location deleted event");
        }
    }

    /// <summary>
    /// Handles membership events from SSE (peer visibility changes, member removal).
    /// </summary>
    private async void OnMembershipReceived(object? sender, SseMembershipEventArgs e)
    {
        // Guard against events firing after disposal
        if (IsDisposed)
            return;

        try
        {
            // Skip membership updates when viewing historical data
            if (!IsToday)
            {
                _logger.LogDebug("SSE membership update skipped - viewing historical date");
                return;
            }

            var membership = e.Membership;
            _logger.LogDebug("SSE membership event: {Action} for {UserId}", membership.Action, membership.UserId);

            switch (membership.Action)
            {
                case "visibility-changed":
                    await HandlePeerVisibilityChangedAsync(membership.UserId, membership.Disabled ?? false);
                    break;

                case "member-removed":
                case "member-left":
                    await HandleMemberRemovedAsync(membership.UserId);
                    break;

                case "member-joined":
                    // Reload members to show the new member
                    await LoadMembersAsync();
                    break;

                case "invite-declined":
                case "invite-revoked":
                    // These are informational - no UI action needed
                    _logger.LogInformation("Invite event: {Action}", membership.Action);
                    break;

                default:
                    _logger.LogDebug("Unhandled membership action: {Action}", membership.Action);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling SSE membership event");
        }
    }

    /// <summary>
    /// Handles invite created events from SSE.
    /// Currently logs the event; future implementation could refresh pending invitations UI.
    /// </summary>
    private void OnInviteCreated(object? sender, SseInviteCreatedEventArgs e)
    {
        // Guard against events firing after disposal
        if (IsDisposed)
            return;

        _logger.LogInformation("SSE invite created: {InvitationId}", e.InviteCreated.InvitationId);
        // Future: Could refresh pending invitations list if UI is added
    }

    /// <summary>
    /// Handles peer visibility change events.
    /// </summary>
    private async Task HandlePeerVisibilityChangedAsync(string? userId, bool isDisabled)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var member = Members.FirstOrDefault(m => m.UserId == userId);
            if (member != null)
            {
                member.OrgPeerVisibilityAccessDisabled = isDisabled;

                // Update current user state if this is us
                if (member.IsSelf)
                {
                    MyPeerVisibilityDisabled = isDisabled;
                }

                // If another member disabled visibility, remove their location
                if (!member.IsSelf && isDisabled)
                {
                    member.LastLocation = null;
                }

                // Update map markers
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }

                _logger.LogInformation("Updated peer visibility for {UserId}: disabled={Disabled}", userId, isDisabled);
            }
        });
    }

    /// <summary>
    /// Handles member removal events.
    /// </summary>
    private async Task HandleMemberRemovedAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var member = Members.FirstOrDefault(m => m.UserId == userId);
            if (member != null)
            {
                Members.Remove(member);

                // Update map markers
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }

                _logger.LogInformation("Removed member {UserId} from group", userId);
            }
        });
    }

    /// <summary>
    /// Refreshes a specific member's location.
    /// </summary>
    private async Task RefreshMemberLocationAsync(string userId)
    {
        if (SelectedGroup == null)
            return;

        try
        {
            var locations = await _groupsService.GetLatestLocationsAsync(
                SelectedGroup.Id,
                new List<string> { userId });

            if (locations.TryGetValue(userId, out var location))
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var member = Members.FirstOrDefault(m => m.UserId == userId);
                    if (member != null)
                    {
                        member.LastLocation = location;

                        // Update map markers
                        if (IsMapView)
                        {
                            UpdateMapMarkers();
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh location for {UserId}", userId);
        }
    }

    /// <summary>
    /// Handles SSE connected event.
    /// </summary>
    private void OnSseConnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("SSE connected");
    }

    /// <summary>
    /// Handles SSE reconnecting event.
    /// </summary>
    private void OnSseReconnecting(object? sender, SseReconnectEventArgs e)
    {
        _logger.LogInformation("SSE reconnecting (attempt {Attempt}, delay {DelayMs}ms)", e.Attempt, e.DelayMs);
    }

    #endregion

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();

        OnPropertyChanged(nameof(IsConfigured));

        // Initialize cached viewport bounds on first appearance
        if (!_cachedViewportBounds.HasValue)
        {
            UpdateCachedViewportBounds();
        }

        if (IsConfigured && Groups.Count == 0)
        {
            await LoadGroupsAsync();
        }
        else if (SelectedGroup != null && IsToday)
        {
            // Resume SSE subscriptions when returning to the page
            // Fire and forget - don't block page appearing
            _ = StartSseSubscriptionsAsync();
        }
    }

    /// <summary>
    /// Called when the view disappears.
    /// Groups owns its own map, so layer cleanup is optional (for memory).
    /// SSE intentionally NOT stopped here - stopping SSE blocks for 10+ seconds on Android.
    /// SSE continues running in background; events are ignored when page not visible.
    /// SSE only stops when switching groups or app closes.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        // Groups owns its own map - no shared layer conflicts with other pages
        // Optional: clear layers to free memory when page not visible
        ClearGroupMembers();
        ClearHistoricalLocations();

        // DON'T stop SSE here - it causes 25+ second navigation freeze
        // SSE events are already guarded by IsDisposed check
        // SSE will be stopped when switching groups (OnSelectedGroupChanged)
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up resources to prevent memory leaks.
    /// Called during ViewModel disposal (app closing, etc.)
    /// </summary>
    protected override void Cleanup()
    {
        // Only stop SSE during actual disposal (app closing)
        // Use ThreadPool to not block the disposal
        ThreadPool.QueueUserWorkItem(_ => StopSseSubscriptions());

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

        base.Cleanup();
    }
}
