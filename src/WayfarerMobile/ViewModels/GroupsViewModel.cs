using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly MapService _mapService;
    private readonly ISseClientFactory _sseClientFactory;
    private readonly ILogger<GroupsViewModel> _logger;

    /// <summary>
    /// SSE client for location updates.
    /// </summary>
    private ISseClient? _locationSseClient;

    /// <summary>
    /// SSE client for membership updates (visibility, removals).
    /// </summary>
    private ISseClient? _membershipSseClient;

    /// <summary>
    /// Cancellation token source for SSE subscriptions.
    /// </summary>
    private CancellationTokenSource? _sseCts;

    /// <summary>
    /// Dictionary tracking last update time per user for throttling.
    /// </summary>
    private readonly Dictionary<string, DateTime> _lastUpdateTimes = new();

    /// <summary>
    /// Throttle interval in milliseconds for SSE updates.
    /// </summary>
    private const int ThrottleIntervalMs = 2000;

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
    /// Gets or sets whether map view is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListView))]
    [NotifyPropertyChangedFor(nameof(ViewModeIndex))]
    [NotifyPropertyChangedFor(nameof(ShowListView))]
    [NotifyPropertyChangedFor(nameof(ShowMapView))]
    private bool _isMapView;

    /// <summary>
    /// Gets or sets whether the header is expanded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowListView))]
    [NotifyPropertyChangedFor(nameof(ShowMapView))]
    private bool _isHeaderExpanded;

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
    /// Gets whether to show the list view (list mode and header not expanded).
    /// </summary>
    public bool ShowListView => IsListView && !IsHeaderExpanded;

    /// <summary>
    /// Gets whether to show the map view (map mode and header not expanded).
    /// </summary>
    public bool ShowMapView => IsMapView && !IsHeaderExpanded;

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
    /// </summary>
    public Mapsui.Map Map => _mapService.Map;

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
    /// <param name="mapService">Service for map operations.</param>
    /// <param name="sseClientFactory">Factory for creating SSE clients.</param>
    /// <param name="logger">Logger instance.</param>
    public GroupsViewModel(
        IGroupsService groupsService,
        ISettingsService settingsService,
        MapService mapService,
        ISseClientFactory sseClientFactory,
        ILogger<GroupsViewModel> logger)
    {
        _groupsService = groupsService;
        _settingsService = settingsService;
        _mapService = mapService;
        _sseClientFactory = sseClientFactory;
        _logger = logger;
        Title = "Groups";
    }

    /// <summary>
    /// Called when the selected group changes.
    /// </summary>
    partial void OnSelectedGroupChanged(GroupSummary? value)
    {
        if (value != null)
        {
            // Persist the selection
            _settingsService.LastSelectedGroupId = value.Id.ToString();
            _settingsService.LastSelectedGroupName = value.Name;

            // Stop existing SSE subscriptions before loading new group
            StopSseSubscriptions();

            _ = LoadMembersAndStartSseAsync();
        }
        else
        {
            StopSseSubscriptions();
            Members.Clear();
        }
    }

    /// <summary>
    /// Called when the selected date changes.
    /// </summary>
    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(ShowHistoricalToggle));

        if (SelectedGroup != null)
        {
            if (IsToday)
            {
                // Switch to live mode - start SSE
                _ = StartSseSubscriptionsAsync();
            }
            else
            {
                // Switch to historical mode - stop SSE and load historical data
                StopSseSubscriptions();
                _ = LoadHistoricalLocationsAsync();
            }
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
            ErrorMessage = $"Failed to load groups: {ex.Message}";
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

            // Load latest locations
            var locations = await _groupsService.GetLatestLocationsAsync(groupId);

            // Find current user and update visibility state
            var currentUser = members.FirstOrDefault(m => m.IsSelf);
            if (currentUser != null)
            {
                MyPeerVisibilityDisabled = currentUser.OrgPeerVisibilityAccessDisabled;
            }

            // Merge locations into members
            foreach (var member in members)
            {
                if (locations.TryGetValue(member.UserId, out var location))
                {
                    member.LastLocation = location;
                }
            }

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
            ErrorMessage = $"Failed to load members: {ex.Message}";
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    /// <summary>
    /// Refreshes member locations without full reload.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLocationsAsync()
    {
        if (SelectedGroup == null)
            return;

        try
        {
            var locations = await _groupsService.GetLatestLocationsAsync(SelectedGroup.Id);

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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refresh locations error");
        }
    }

    /// <summary>
    /// Loads historical locations for the selected date.
    /// </summary>
    [RelayCommand]
    private async Task LoadHistoricalLocationsAsync()
    {
        if (SelectedGroup == null)
            return;

        try
        {
            _logger.LogInformation("Loading historical locations for {Date}", SelectedDate);

            var request = new GroupLocationsQueryRequest
            {
                MinLng = -180,
                MaxLng = 180,
                MinLat = -90,
                MaxLat = 90,
                ZoomLevel = 10,
                DateType = "day",
                Year = SelectedDate.Year,
                Month = SelectedDate.Month,
                Day = SelectedDate.Day
            };

            var response = await _groupsService.QueryLocationsAsync(SelectedGroup.Id, request);

            if (response != null)
            {
                _logger.LogInformation("Loaded {Count} historical locations", response.TotalItems);
                // Update map markers with historical data
                if (IsMapView)
                {
                    UpdateMapMarkers();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load historical locations");
            ErrorMessage = "Failed to load historical locations";
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
    /// </summary>
    [RelayCommand]
    private void PreviousDay()
    {
        SelectedDate = SelectedDate.AddDays(-1);
    }

    /// <summary>
    /// Navigates to the next day (not beyond today).
    /// </summary>
    [RelayCommand]
    private void NextDay()
    {
        if (SelectedDate.Date < DateTime.Today)
        {
            SelectedDate = SelectedDate.AddDays(1);
        }
    }

    /// <summary>
    /// Navigates to today (live mode).
    /// </summary>
    [RelayCommand]
    private void Today()
    {
        SelectedDate = DateTime.Today;
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
    /// Toggles the header expansion state.
    /// </summary>
    [RelayCommand]
    private void ToggleHeaderExpanded()
    {
        IsHeaderExpanded = !IsHeaderExpanded;
    }

    /// <summary>
    /// Selects all members to show on map.
    /// </summary>
    [RelayCommand]
    private void SelectAllMembers()
    {
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
    private async Task DateSelectedAsync()
    {
        IsDatePickerOpen = false;

        // Compare against date before picker was opened
        if (_dateBeforePickerOpened.HasValue && SelectedDate.Date != _dateBeforePickerOpened.Value.Date)
        {
            // Limit to today at most
            if (SelectedDate.Date > DateTime.Today)
            {
                SelectedDate = DateTime.Today;
            }

            // Reload data for new date (this happens automatically via OnSelectedDateChanged)
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
    /// Copies coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var coords = $"{SelectedMember.LastLocation.Latitude:F6}, {SelectedMember.LastLocation.Longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            _logger.LogInformation("Coordinates copied to clipboard: {Coords}", coords);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy coordinates");
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
    /// Navigates to the member's location using internal navigation.
    /// </summary>
    [RelayCommand]
    private void NavigateToMember()
    {
        if (SelectedMember?.LastLocation == null) return;

        // TODO: Integrate with TripNavigationService when available
        _logger.LogInformation("Navigate to {Member} at {Lat},{Lon}",
            SelectedMember.DisplayText,
            SelectedMember.LastLocation.Latitude,
            SelectedMember.LastLocation.Longitude);
    }

    #endregion

    /// <summary>
    /// Updates the map markers with current member locations.
    /// </summary>
    private void UpdateMapMarkers()
    {
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
            });

        _mapService.UpdateGroupMembers(memberLocations);
    }

    #region SSE Management

    /// <summary>
    /// Starts SSE subscriptions for the selected group.
    /// </summary>
    private async Task StartSseSubscriptionsAsync()
    {
        if (SelectedGroup == null || !IsToday)
            return;

        // Stop any existing subscriptions
        StopSseSubscriptions();

        _sseCts = new CancellationTokenSource();
        var groupId = SelectedGroup.Id.ToString();

        // Create and start location SSE client
        _locationSseClient = _sseClientFactory.Create();
        _locationSseClient.LocationReceived += OnLocationReceived;
        _locationSseClient.Connected += OnSseConnected;
        _locationSseClient.Reconnecting += OnSseReconnecting;

        // Create and start membership SSE client
        _membershipSseClient = _sseClientFactory.Create();
        _membershipSseClient.MembershipReceived += OnMembershipReceived;

        _logger.LogInformation("Starting SSE subscriptions for group {GroupId}", groupId);

        // Start subscriptions in background (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _locationSseClient.SubscribeToGroupAsync(groupId, _sseCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Location SSE subscription cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Location SSE subscription error");
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await _membershipSseClient.SubscribeToGroupMembershipAsync(groupId, _sseCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Membership SSE subscription cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Membership SSE subscription error");
            }
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops all SSE subscriptions.
    /// </summary>
    private void StopSseSubscriptions()
    {
        _logger.LogDebug("Stopping SSE subscriptions");

        // Cancel ongoing operations
        _sseCts?.Cancel();
        _sseCts?.Dispose();
        _sseCts = null;

        // Stop and dispose location SSE client
        if (_locationSseClient != null)
        {
            _locationSseClient.LocationReceived -= OnLocationReceived;
            _locationSseClient.Connected -= OnSseConnected;
            _locationSseClient.Reconnecting -= OnSseReconnecting;
            _locationSseClient.Stop();
            _locationSseClient.Dispose();
            _locationSseClient = null;
        }

        // Stop and dispose membership SSE client
        if (_membershipSseClient != null)
        {
            _membershipSseClient.MembershipReceived -= OnMembershipReceived;
            _membershipSseClient.Stop();
            _membershipSseClient.Dispose();
            _membershipSseClient = null;
        }

        // Clear throttle tracking
        _lastUpdateTimes.Clear();
    }

    /// <summary>
    /// Handles location received events from SSE with throttling.
    /// </summary>
    private async void OnLocationReceived(object? sender, SseLocationEventArgs e)
    {
        try
        {
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
    /// Handles membership events from SSE (peer visibility changes, member removal).
    /// </summary>
    private async void OnMembershipReceived(object? sender, SseMembershipEventArgs e)
    {
        try
        {
            var membership = e.Membership;
            _logger.LogDebug("SSE membership event: {Action} for {UserId}", membership.Action, membership.UserId);

            switch (membership.Action)
            {
                case "peer-visibility-changed":
                    await HandlePeerVisibilityChangedAsync(membership.UserId, membership.Disabled ?? false);
                    break;

                case "member-removed":
                case "member-left":
                    await HandleMemberRemovedAsync(membership.UserId);
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

        if (IsConfigured && Groups.Count == 0)
        {
            await LoadGroupsAsync();
        }
        else if (SelectedGroup != null && IsToday)
        {
            // Resume SSE subscriptions when returning to the page
            await StartSseSubscriptionsAsync();
        }
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        StopSseSubscriptions();
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up resources to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        StopSseSubscriptions();
        base.Cleanup();
    }
}
