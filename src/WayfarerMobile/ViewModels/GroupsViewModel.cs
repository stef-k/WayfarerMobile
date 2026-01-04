using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using Map = Mapsui.Map;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Shared.Collections;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the groups page showing user's groups and member locations.
/// Uses SSE (Server-Sent Events) for real-time location and membership updates.
/// Coordinates child ViewModels for SSE management, date navigation, and member details.
/// </summary>
public partial class GroupsViewModel : BaseViewModel,
    ISseManagementCallbacks,
    IDateNavigationCallbacks,
    IMemberDetailsCallbacks
{
    #region Fields

    private readonly IGroupsService _groupsService;
    private readonly IGroupMemberManager _memberManager;
    private readonly ISettingsService _settingsService;
    private readonly IToastService _toastService;
    private readonly ILogger<GroupsViewModel> _logger;
    private readonly ILocationBridge _locationBridge;
    private readonly NavigationHudViewModel _navigationHudViewModel;
    private readonly IMapBuilder _mapBuilder;
    private readonly IGroupLayerService _groupLayerService;
    private readonly MarkerPulseAnimator _pulseAnimator;

    /// <summary>
    /// Current pulse scale for live marker animation (1.0 to 1.35).
    /// </summary>
    private double _currentPulseScale = 1.0;

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
    /// Cached viewport bounds. Updated only when map pans/zooms, not on date navigation.
    /// </summary>
    private (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? _cachedViewportBounds;

    #endregion

    #region Child ViewModels

    /// <summary>
    /// Gets the SSE management ViewModel.
    /// </summary>
    public SseManagementViewModel SseManagement { get; }

    /// <summary>
    /// Gets the date navigation ViewModel.
    /// </summary>
    public DateNavigationViewModel DateNav { get; }

    /// <summary>
    /// Gets the member details ViewModel.
    /// </summary>
    public MemberDetailsViewModel MemberDetails { get; }

    #endregion

    #region Observable Properties

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
    [NotifyPropertyChangedFor(nameof(ShowPeerVisibilityToggle))]
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
    [NotifyPropertyChangedFor(nameof(PeerVisibilityLabel))]
    [NotifyPropertyChangedFor(nameof(PeerVisibilityDescription))]
    private bool _myPeerVisibilityDisabled;

    #endregion

    #region Forwarding Properties - DateNav

    /// <summary>
    /// Gets or sets the selected date for viewing locations.
    /// Forwards to DateNav.SelectedDate.
    /// </summary>
    public DateTime SelectedDate
    {
        get => DateNav.SelectedDate;
        set => DateNav.SelectedDate = value;
    }

    /// <summary>
    /// Gets or sets whether to show historical locations (when viewing today).
    /// Forwards to DateNav.ShowHistoricalLocations.
    /// </summary>
    public bool ShowHistoricalLocations
    {
        get => DateNav.ShowHistoricalLocations;
        set => DateNav.ShowHistoricalLocations = value;
    }

    /// <summary>
    /// Gets or sets whether the date picker is open.
    /// Forwards to DateNav.IsDatePickerOpen.
    /// </summary>
    public bool IsDatePickerOpen
    {
        get => DateNav.IsDatePickerOpen;
        set => DateNav.IsDatePickerOpen = value;
    }

    /// <summary>
    /// Gets whether the selected date is today.
    /// Forwards to DateNav.IsToday.
    /// </summary>
    public bool IsToday => DateNav.IsToday;

    /// <summary>
    /// Gets whether to show the historical toggle (only visible when viewing today).
    /// Forwards to DateNav.ShowHistoricalToggle.
    /// </summary>
    public bool ShowHistoricalToggle => DateNav.ShowHistoricalToggle;

    /// <summary>
    /// Gets the formatted selected date text.
    /// Forwards to DateNav.SelectedDateText.
    /// </summary>
    public string SelectedDateText => DateNav.SelectedDateText;

    /// <summary>
    /// Gets the date button text.
    /// Forwards to DateNav.DateButtonText.
    /// </summary>
    public string DateButtonText => DateNav.DateButtonText;

    #endregion

    #region Forwarding Properties - MemberDetails

    /// <summary>
    /// Gets or sets whether the member details sheet is open.
    /// Forwards to MemberDetails.IsMemberSheetOpen.
    /// </summary>
    public bool IsMemberSheetOpen
    {
        get => MemberDetails.IsMemberSheetOpen;
        set => MemberDetails.IsMemberSheetOpen = value;
    }

    /// <summary>
    /// Gets or sets the selected member for the details sheet.
    /// Forwards to MemberDetails.SelectedMember.
    /// </summary>
    public GroupMember? SelectedMember
    {
        get => MemberDetails.SelectedMember;
        set => MemberDetails.SelectedMember = value;
    }

    /// <summary>
    /// Gets the selected member's coordinates as text.
    /// Forwards to MemberDetails.SelectedMemberCoordinates.
    /// </summary>
    public string SelectedMemberCoordinates => MemberDetails.SelectedMemberCoordinates;

    /// <summary>
    /// Gets the selected member's location time as text.
    /// Forwards to MemberDetails.SelectedMemberLocationTime.
    /// </summary>
    public string SelectedMemberLocationTime => MemberDetails.SelectedMemberLocationTime;

    #endregion

    #region Computed Properties

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
    /// Gets whether the selected group is a Friends group.
    /// </summary>
    public bool IsFriendsGroup => string.Equals(SelectedGroup?.GroupType, "Friends", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets whether to show the peer visibility toggle.
    /// Only visible for organization groups with OrgPeerVisibilityEnabled.
    /// </summary>
    public bool ShowPeerVisibilityToggle => SelectedGroup?.OrgPeerVisibilityEnabled == true;

    /// <summary>
    /// Gets the label text for peer visibility based on current state.
    /// </summary>
    public string PeerVisibilityLabel => MyPeerVisibilityDisabled
        ? "I'm Hidden from Peers"
        : "I'm Visible to Peers";

    /// <summary>
    /// Gets the description text for peer visibility toggle.
    /// </summary>
    public string PeerVisibilityDescription => MyPeerVisibilityDisabled
        ? "Toggle to let group members see your location"
        : "Toggle to hide your location from group members";

    #endregion

    #region Callback Interface Properties

    // ISseManagementCallbacks
    Guid? ISseManagementCallbacks.SelectedGroupId => SelectedGroup?.Id;
    ObservableRangeCollection<GroupMember> ISseManagementCallbacks.Members => Members;

    // IDateNavigationCallbacks
    GroupSummary? IDateNavigationCallbacks.SelectedGroup => SelectedGroup;
    ObservableRangeCollection<GroupMember> IDateNavigationCallbacks.Members => Members;
    bool IDateNavigationCallbacks.IsMapView => IsMapView;
    WritableLayer? IDateNavigationCallbacks.HistoricalLocationsLayer => _historicalLocationsLayer;
    (double MinLon, double MinLat, double MaxLon, double MaxLat, double ZoomLevel)? IDateNavigationCallbacks.CachedViewportBounds => _cachedViewportBounds;

    // IMemberDetailsCallbacks
    ObservableRangeCollection<GroupMember> IMemberDetailsCallbacks.Members => Members;
    LocationData? IMemberDetailsCallbacks.CurrentLocation => _locationBridge.LastLocation;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of GroupsViewModel.
    /// </summary>
    public GroupsViewModel(
        IGroupsService groupsService,
        IGroupMemberManager memberManager,
        ISettingsService settingsService,
        IToastService toastService,
        ILocationBridge locationBridge,
        NavigationHudViewModel navigationHudViewModel,
        IMapBuilder mapBuilder,
        IGroupLayerService groupLayerService,
        MarkerPulseAnimator pulseAnimator,
        SseManagementViewModel sseManagement,
        DateNavigationViewModel dateNav,
        MemberDetailsViewModel memberDetails,
        ILogger<GroupsViewModel> logger)
    {
        _groupsService = groupsService;
        _memberManager = memberManager;
        _settingsService = settingsService;
        _toastService = toastService;
        _locationBridge = locationBridge;
        _navigationHudViewModel = navigationHudViewModel;
        _mapBuilder = mapBuilder;
        _groupLayerService = groupLayerService;
        _pulseAnimator = pulseAnimator;
        _logger = logger;
        Title = "Groups";

        // Initialize child ViewModels
        SseManagement = sseManagement;
        DateNav = dateNav;
        MemberDetails = memberDetails;

        // Set callbacks on child ViewModels
        SseManagement.SetCallbacks(this);
        DateNav.SetCallbacks(this);
        MemberDetails.SetCallbacks(this);

        // Subscribe to child property changes for forwarding
        DateNav.PropertyChanged += OnDateNavPropertyChanged;
        MemberDetails.PropertyChanged += OnMemberDetailsPropertyChanged;
    }

    #endregion

    #region Child Property Change Forwarding

    private void OnDateNavPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward relevant property changes to maintain XAML bindings
        switch (e.PropertyName)
        {
            case nameof(DateNavigationViewModel.SelectedDate):
                OnPropertyChanged(nameof(SelectedDate));
                break;
            case nameof(DateNavigationViewModel.ShowHistoricalLocations):
                OnPropertyChanged(nameof(ShowHistoricalLocations));
                break;
            case nameof(DateNavigationViewModel.IsDatePickerOpen):
                OnPropertyChanged(nameof(IsDatePickerOpen));
                break;
            case nameof(DateNavigationViewModel.IsToday):
                OnPropertyChanged(nameof(IsToday));
                break;
            case nameof(DateNavigationViewModel.ShowHistoricalToggle):
                OnPropertyChanged(nameof(ShowHistoricalToggle));
                break;
            case nameof(DateNavigationViewModel.SelectedDateText):
                OnPropertyChanged(nameof(SelectedDateText));
                break;
            case nameof(DateNavigationViewModel.DateButtonText):
                OnPropertyChanged(nameof(DateButtonText));
                break;
        }
    }

    private void OnMemberDetailsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Forward relevant property changes to maintain XAML bindings
        switch (e.PropertyName)
        {
            case nameof(MemberDetailsViewModel.IsMemberSheetOpen):
                OnPropertyChanged(nameof(IsMemberSheetOpen));
                break;
            case nameof(MemberDetailsViewModel.SelectedMember):
                OnPropertyChanged(nameof(SelectedMember));
                break;
            case nameof(MemberDetailsViewModel.SelectedMemberCoordinates):
                OnPropertyChanged(nameof(SelectedMemberCoordinates));
                break;
            case nameof(MemberDetailsViewModel.SelectedMemberLocationTime):
                OnPropertyChanged(nameof(SelectedMemberLocationTime));
                break;
        }
    }

    #endregion

    #region Forwarding Commands - DateNav

    public IRelayCommand SelectDateCommand => DateNav.SelectDateCommand;
    public IRelayCommand PreviousDayCommand => DateNav.PreviousDayCommand;
    public IRelayCommand NextDayCommand => DateNav.NextDayCommand;
    public IRelayCommand TodayCommand => DateNav.TodayCommand;
    public IRelayCommand OpenDatePickerCommand => DateNav.OpenDatePickerCommand;
    public IRelayCommand DateSelectedCommand => DateNav.DateSelectedCommand;
    public IRelayCommand CancelDatePickerCommand => DateNav.CancelDatePickerCommand;

    #endregion

    #region Forwarding Commands - MemberDetails

    public IRelayCommand<GroupMember?> ShowMemberDetailsCommand => MemberDetails.ShowMemberDetailsCommand;
    public IRelayCommand CloseMemberSheetCommand => MemberDetails.CloseMemberSheetCommand;
    public IAsyncRelayCommand OpenInMapsCommand => MemberDetails.OpenInMapsCommand;
    public IAsyncRelayCommand SearchWikipediaCommand => MemberDetails.SearchWikipediaCommand;
    public IAsyncRelayCommand CopyCoordinatesCommand => MemberDetails.CopyCoordinatesCommand;
    public IAsyncRelayCommand ShareLocationCommand => MemberDetails.ShareLocationCommand;
    public IAsyncRelayCommand NavigateToMemberCommand => MemberDetails.NavigateToMemberCommand;

    #endregion

    #region Map Creation

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

    #endregion

    #region Property Change Handlers

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
            SseManagement.AbandonSseClients();

            SafeFireAndForget(LoadMembersAndStartSseAsync(), "LoadMembersAndStartSse");
        }
        else
        {
            Title = "Groups";
            SseManagement.AbandonSseClients();
            Members.Clear();
        }
    }

    #endregion

    #region Commands

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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading groups");
            ErrorMessage = "Failed to load groups. Please check your connection and try again.";
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading groups");
            ErrorMessage = "Request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading groups");
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
    public async Task LoadMembersAsync()
    {
        if (SelectedGroup == null || IsLoadingMembers)
            return;

        try
        {
            IsLoadingMembers = true;
            ErrorMessage = null;

            var groupId = SelectedGroup.Id;

            // Load members with locations via manager
            var (members, myPeerVisibilityDisabled) = await _memberManager.LoadMembersWithLocationsAsync(groupId);

            // Update visibility state
            MyPeerVisibilityDisabled = myPeerVisibilityDisabled;

            // Update members collection
            Members.ReplaceRange(members);

            // Update map markers if in map view
            if (IsMapView)
            {
                UpdateMapMarkers();
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading members for group {GroupId}", SelectedGroup?.Id);
            ErrorMessage = "Failed to load members. Please check your connection and try again.";
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out loading members for group {GroupId}", SelectedGroup?.Id);
            ErrorMessage = "Request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading members for group {GroupId}", SelectedGroup?.Id);
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
    public async Task RefreshLocationsAsync()
    {
        if (SelectedGroup == null)
            return;

        // Get current members snapshot for thread safety
        var currentMembers = Members.ToList();

        // Refresh locations via manager (handles errors internally)
        var sortedMembers = await _memberManager.RefreshMemberLocationsAsync(SelectedGroup.Id, currentMembers);

        if (sortedMembers != null)
        {
            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Members.ReplaceRange(sortedMembers);

                // Update map markers if in map view
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }
            });
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
    /// Toggles the current user's peer visibility in the selected group.
    /// </summary>
    [RelayCommand]
    private async Task TogglePeerVisibilityAsync()
    {
        if (SelectedGroup == null)
            return;

        var newDisabledState = !MyPeerVisibilityDisabled;

        var success = await _memberManager.UpdatePeerVisibilityAsync(SelectedGroup.Id, newDisabledState);

        if (success)
        {
            MyPeerVisibilityDisabled = newDisabledState;

            // Update the current user's member record
            var currentUser = _memberManager.FindCurrentUser(Members);
            if (currentUser != null)
            {
                currentUser.OrgPeerVisibilityAccessDisabled = newDisabledState;
            }
        }
        else
        {
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
    /// </summary>
    private const int SelectAllConfirmationThreshold = 10;

    /// <summary>
    /// Selects all members to show on map.
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

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows member details by user ID (called from map tap handler).
    /// </summary>
    public void ShowMemberDetailsByUserId(string userId)
    {
        MemberDetails.ShowMemberDetailsByUserId(userId);
    }

    /// <summary>
    /// Shows member details for a historical location (called when tapping historical marker).
    /// </summary>
    public void ShowHistoricalMemberDetails(string userId, double latitude, double longitude, DateTime timestamp)
    {
        MemberDetails.ShowHistoricalMemberDetails(userId, latitude, longitude, timestamp);
    }

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
    /// </summary>
    public void ClearLiveLocationIndicator()
    {
        // Groups has its own map without a location layer - nothing to clear
        _logger.LogDebug("[Groups] ClearLiveLocationIndicator - no-op (own map)");
    }

    #endregion

    #region Map Operations

    /// <summary>
    /// Updates the map markers with current member locations.
    /// </summary>
    public void UpdateMapMarkers()
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
        var points = _groupLayerService.UpdateGroupMemberMarkers(_groupMembersLayer, memberLocations, _currentPulseScale);

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

    /// <summary>
    /// Clears historical location markers from the map.
    /// </summary>
    public void ClearHistoricalLocations()
    {
        _historicalLocationsLayer?.Clear();
        _historicalLocationsLayer?.DataHasChanged();
    }

    /// <summary>
    /// Updates historical location markers on the map.
    /// </summary>
    public void UpdateHistoricalLocationMarkers(List<GroupLocationResult> locations, Dictionary<string, string> memberColors)
    {
        if (_historicalLocationsLayer != null)
        {
            _groupLayerService.UpdateHistoricalLocationMarkers(_historicalLocationsLayer, locations, memberColors);
        }
    }

    #endregion

    #region Callback Interface Methods

    // ISseManagementCallbacks
    Task ISseManagementCallbacks.LoadMembersAsync() => LoadMembersAsync();

    // IDateNavigationCallbacks
    Task IDateNavigationCallbacks.RefreshLocationsAsync() => RefreshLocationsAsync();
    Task IDateNavigationCallbacks.EnsureSseConnectedAsync() => SseManagement.EnsureSseConnectedAsync();

    // IMemberDetailsCallbacks
    async Task<NavigationMethod?> IMemberDetailsCallbacks.ShowNavigationPickerAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return null;

        // Get the GroupsPage to access the navigation picker
        var groupsPage = page as Views.GroupsPage ??
            (Shell.Current?.CurrentPage as Views.GroupsPage);

        if (groupsPage != null)
        {
            return await groupsPage.ShowNavigationPickerAsync();
        }

        // Fallback to action sheet if page reference not available
        var result = await page.DisplayActionSheetAsync(
            "Navigate by", "Cancel", null,
            "🚶 Walk", "🚗 Drive", "🚴 Bike", "📍 External Maps");

        return result switch
        {
            "🚶 Walk" => NavigationMethod.Walk,
            "🚗 Drive" => NavigationMethod.Drive,
            "🚴 Bike" => NavigationMethod.Bike,
            "📍 External Maps" => NavigationMethod.ExternalMaps,
            _ => null
        };
    }

    void IMemberDetailsCallbacks.SetNavigationSourcePage(string route)
    {
        _navigationHudViewModel.SourcePageRoute = route;
    }

    async Task IMemberDetailsCallbacks.NavigateToMainMapWithRouteAsync(NavigationRoute route)
    {
        var navParams = new Dictionary<string, object>
        {
            ["NavigationRoute"] = route
        };
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//main", navParams);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads members and starts SSE subscriptions for the selected group.
    /// </summary>
    private async Task LoadMembersAndStartSseAsync()
    {
        await LoadMembersAsync();

        if (IsToday && SelectedGroup != null)
        {
            await SseManagement.StartSseSubscriptionsAsync();
        }
    }

    /// <summary>
    /// Executes an async task in fire-and-forget mode with error logging.
    /// </summary>
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
    /// Clears group member markers from the map.
    /// </summary>
    private void ClearGroupMembers()
    {
        _groupMembersLayer?.Clear();
        _groupMembersLayer?.DataHasChanged();
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();

        OnPropertyChanged(nameof(IsConfigured));

        // Start pulse animation for live markers
        _pulseAnimator.PulseScaleChanged += OnPulseScaleChanged;
        _pulseAnimator.Start();

        // Initialize cached viewport bounds on first appearance
        if (!_cachedViewportBounds.HasValue)
        {
            UpdateCachedViewportBounds();
        }

        if (IsConfigured && Groups.Count == 0)
        {
            await LoadGroupsAsync();
        }
        else if (SelectedGroup != null)
        {
            // Returning to page - restore markers that were cleared in OnDisappearingAsync
            if (IsMapView)
            {
                UpdateMapMarkers();
            }

            // Resume SSE subscriptions for live updates (only when viewing today)
            if (IsToday)
            {
                _ = SseManagement.StartSseSubscriptionsAsync();
            }
        }
    }

    /// <summary>
    /// Handles pulse scale changes to animate live marker rings.
    /// </summary>
    private void OnPulseScaleChanged(object? sender, double scale)
    {
        _currentPulseScale = scale;

        // Only update map if we're in map view and have live members
        if (IsMapView && _groupMembersLayer != null && Members.Any(m => m.LastLocation?.IsLive == true))
        {
            UpdateMapMarkers();
        }
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        // Stop pulse animation to save resources
        _pulseAnimator.PulseScaleChanged -= OnPulseScaleChanged;
        _pulseAnimator.Stop();
        _currentPulseScale = 1.0;

        // Groups owns its own map - no shared layer conflicts with other pages
        ClearGroupMembers();
        ClearHistoricalLocations();

        // DON'T stop SSE here - it causes navigation freeze
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up resources to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from child property changes
        DateNav.PropertyChanged -= OnDateNavPropertyChanged;
        MemberDetails.PropertyChanged -= OnMemberDetailsPropertyChanged;

        // Dispose SSE resources
        SseManagement.Dispose();

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

        base.Cleanup();
    }

    #endregion
}
