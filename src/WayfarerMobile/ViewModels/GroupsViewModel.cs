using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the groups page showing user's groups and member locations.
/// </summary>
public partial class GroupsViewModel : BaseViewModel
{
    private readonly IGroupsService _groupsService;
    private readonly ISettingsService _settingsService;
    private readonly MapService _mapService;
    private Timer? _refreshTimer;
    private const int RefreshIntervalSeconds = 30;

    /// <summary>
    /// Gets the collection of groups.
    /// </summary>
    public ObservableCollection<GroupSummary> Groups { get; } = new();

    /// <summary>
    /// Gets the collection of members for the selected group.
    /// </summary>
    public ObservableCollection<GroupMember> Members { get; } = new();

    /// <summary>
    /// Gets or sets the selected group.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(SelectedGroupName))]
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
    private bool _isMapView;

    /// <summary>
    /// Gets whether list view is active.
    /// </summary>
    public bool IsListView => !IsMapView;

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
    public GroupsViewModel(IGroupsService groupsService, ISettingsService settingsService, MapService mapService)
    {
        _groupsService = groupsService;
        _settingsService = settingsService;
        _mapService = mapService;
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
            _ = LoadMembersAsync();
        }
        else
        {
            Members.Clear();
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

            Groups.Clear();
            foreach (var group in groups)
            {
                Groups.Add(group);
            }

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

            // Merge locations into members
            foreach (var member in members)
            {
                if (locations.TryGetValue(member.UserId, out var location))
                {
                    member.LastLocation = location;
                }
            }

            Members.Clear();
            foreach (var member in members.OrderByDescending(m => m.LastLocation?.IsLive ?? false)
                                          .ThenBy(m => m.DisplayText))
            {
                Members.Add(member);
            }

            // Update map markers if in map view
            if (IsMapView)
            {
                UpdateMapMarkers();
            }
        }
        catch (Exception ex)
        {
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

            // Trigger UI refresh by re-sorting
            var sorted = Members.OrderByDescending(m => m.LastLocation?.IsLive ?? false)
                               .ThenBy(m => m.DisplayText)
                               .ToList();

            Members.Clear();
            foreach (var member in sorted)
            {
                Members.Add(member);
            }

            // Update map markers if in map view
            if (IsMapView)
            {
                UpdateMapMarkers();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GroupsViewModel] Refresh error: {ex.Message}");
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
    /// Updates the map markers with current member locations.
    /// </summary>
    private void UpdateMapMarkers()
    {
        var memberLocations = Members
            .Where(m => m.LastLocation != null)
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

        // Start auto-refresh timer
        StartRefreshTimer();
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        StopRefreshTimer();
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Starts the auto-refresh timer.
    /// </summary>
    private void StartRefreshTimer()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = new Timer(
            async _ => await RefreshLocationsAsync(),
            null,
            TimeSpan.FromSeconds(RefreshIntervalSeconds),
            TimeSpan.FromSeconds(RefreshIntervalSeconds));
    }

    /// <summary>
    /// Stops the auto-refresh timer.
    /// </summary>
    private void StopRefreshTimer()
    {
        _refreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }
}
