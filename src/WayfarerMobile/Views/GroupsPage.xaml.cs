using System.ComponentModel;
using Mapsui;
using Mapsui.UI.Maui;
using Syncfusion.Maui.Toolkit.BottomSheet;
using WayfarerMobile.Core.Models;
using WayfarerMobile.ViewModels;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.Views;

/// <summary>
/// Page for viewing groups and member locations.
/// </summary>
public partial class GroupsPage : ContentPage
{
    private readonly GroupsViewModel _viewModel;
    private DateTime _lastViewportUpdate = DateTime.MinValue;
    private const int ViewportUpdateThrottleMs = 500;

    /// <summary>
    /// Creates a new instance of GroupsPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    public GroupsPage(GroupsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to page loaded event for map initialization
        Loaded += OnPageLoaded;
    }

    /// <summary>
    /// Called when the page is loaded and controls are initialized.
    /// </summary>
    private void OnPageLoaded(object? sender, EventArgs e)
    {
        // Ensure map is set (event subscriptions are handled in OnAppearing)
        if (MapControl.Map == null)
        {
            MapControl.Map = _viewModel.Map;
        }
    }

    /// <summary>
    /// Handles viewport changes to update cached bounds.
    /// Does NOT trigger data reload - just caches bounds for next date navigation.
    /// Skipped during loading operations to avoid blocking main thread.
    /// </summary>
    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        // Skip viewport updates during loading - prevents GetViewportBounds() blocking
        // when loading overlay causes map to re-render
        if (_viewModel.IsBusy)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastViewportUpdate).TotalMilliseconds < ViewportUpdateThrottleMs)
            return;

        _lastViewportUpdate = now;
        _viewModel.UpdateCachedViewportBounds();
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Re-subscribe to events (they get unsubscribed in OnDisappearing)
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        MapControl.Info += OnMapInfo;

        // Re-subscribe to viewport changes
        if (MapControl.Map?.Navigator != null)
        {
            MapControl.Map.Navigator.ViewportChanged += OnViewportChanged;
        }

        // Clear the live GPS indicator - Groups page shows server-reported locations only
        // This prevents confusion between live GPS dot and group member markers
        _viewModel.ClearLiveLocationIndicator();

        // Sync peer visibility switch with ViewModel state
        SyncPeerVisibilitySwitch();

        await _viewModel.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the page disappears.
    /// Non-blocking: runs cleanup in background to avoid navigation lag.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Unsubscribe from events (will be re-subscribed in OnAppearing)
        MapControl.Info -= OnMapInfo;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // Unsubscribe from viewport changes
        if (MapControl.Map?.Navigator != null)
        {
            MapControl.Map.Navigator.ViewportChanged -= OnViewportChanged;
        }

        // Fire and forget - don't await, don't block navigation
        _ = _viewModel.OnDisappearingAsync();
    }

    /// <summary>
    /// Handles map info events for marker tap detection.
    /// Mapsui 5.0: GetMapInfo returns MapInfo with Feature if a feature was tapped.
    /// </summary>
    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        var map = MapControl.Map;
        if (map == null) return;

        // Get layers that have features we care about (group members and historical locations)
        var targetLayers = map.Layers
            .Where(l => l.Name == "GroupMembers" || l.Name == "HistoricalLocations")
            .ToList();

        if (targetLayers.Count == 0) return;

        // Mapsui 5.0: Use GetMapInfo with specific layers to get tap info
        var mapInfo = e.GetMapInfo(targetLayers);

        Console.WriteLine($"[Groups] OnMapInfo: HasMapInfo={mapInfo != null}, Feature={mapInfo?.Feature != null}");

        if (mapInfo?.Feature == null)
        {
            Console.WriteLine("[Groups] OnMapInfo: No feature found at tap location");
            return;
        }

        var feature = mapInfo.Feature;
        Console.WriteLine($"[Groups] OnMapInfo: Feature found, checking for UserId...");

        // Check for historical location marker FIRST (both have UserId, but historical has IsHistorical=true)
        if (feature["IsHistorical"] is bool isHistorical && isHistorical)
        {
            // Historical location marker tapped - extract all data from feature
            var historicalUserId = feature["UserId"] as string;
            if (!string.IsNullOrEmpty(historicalUserId))
            {
                var latitude = feature["Latitude"] is double lat ? lat : 0;
                var longitude = feature["Longitude"] is double lon ? lon : 0;
                var timestamp = feature["TimestampUtc"] is DateTime ts ? ts : DateTime.UtcNow;

                Console.WriteLine($"[Groups] OnMapInfo: Tapped historical location for {historicalUserId} at {latitude},{longitude} on {timestamp}");
                _viewModel.ShowHistoricalMemberDetails(historicalUserId, latitude, longitude, timestamp);
                BottomSheet.State = BottomSheetState.FullExpanded;
            }
        }
        // Check for live/latest group member marker
        else if (feature["UserId"] is string userId && !string.IsNullOrEmpty(userId))
        {
            Console.WriteLine($"[Groups] OnMapInfo: Tapped member {userId}");
            _viewModel.ShowMemberDetailsByUserId(userId);
            BottomSheet.State = BottomSheetState.FullExpanded;
        }
    }

    /// <summary>
    /// Handles ViewModel property changes for bottom sheet state and peer visibility.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupsViewModel.IsMemberSheetOpen))
        {
            if (_viewModel.IsMemberSheetOpen && _viewModel.SelectedMember != null)
            {
                BottomSheet.State = BottomSheetState.FullExpanded;
            }
            else if (!_viewModel.IsMemberSheetOpen)
            {
                BottomSheet.State = BottomSheetState.Collapsed;
            }
        }
        else if (e.PropertyName == nameof(GroupsViewModel.MyPeerVisibilityDisabled))
        {
            // Sync switch state: ON when visible (not disabled), OFF when disabled
            SyncPeerVisibilitySwitch();
        }
    }

    /// <summary>
    /// Syncs the peer visibility switch with the ViewModel state.
    /// </summary>
    private void SyncPeerVisibilitySwitch()
    {
        // Switch ON = visible (not disabled), OFF = hidden (disabled)
        var shouldBeOn = !_viewModel.MyPeerVisibilityDisabled;
        if (PeerVisibilitySwitch.IsToggled != shouldBeOn)
        {
            PeerVisibilitySwitch.IsToggled = shouldBeOn;
        }
    }

    /// <summary>
    /// Handles group picker selection - closes the popup when a group is selected.
    /// </summary>
    private void OnGroupPickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        // Close the popup when a group is selected
        if (_viewModel.SelectedGroup != null)
        {
            _viewModel.CloseGroupPickerCommand.Execute(null);
        }
    }

    /// <summary>
    /// Handles date picker OK click.
    /// </summary>
    private void OnDatePickerOkClicked(object? sender, EventArgs e)
    {
        _viewModel.DateSelectedCommand.Execute(null);
    }

    /// <summary>
    /// Handles date picker Cancel click.
    /// </summary>
    private void OnDatePickerCancelClicked(object? sender, EventArgs e)
    {
        _viewModel.CancelDatePickerCommand.Execute(null);
    }

    /// <summary>
    /// Handles member visibility checkbox changes.
    /// </summary>
    private void OnMemberVisibilityChanged(object? sender, CheckedChangedEventArgs e)
    {
        // Trigger map update when any member's visibility changes
        _viewModel.RefreshMapMarkersCommand.Execute(null);
    }

    /// <summary>
    /// Handles peer visibility toggle changes.
    /// </summary>
    private void OnPeerVisibilityToggled(object? sender, ToggledEventArgs e)
    {
        // Toggle peer visibility via command
        // Note: Switch ON = visible (not disabled), OFF = hidden (disabled)
        // The command toggles the current state, so we only execute if the desired state differs
        var wantVisible = e.Value;
        var currentlyDisabled = _viewModel.MyPeerVisibilityDisabled;

        // Only toggle if the states don't match
        // wantVisible && currentlyDisabled = need to enable (toggle)
        // !wantVisible && !currentlyDisabled = need to disable (toggle)
        if ((wantVisible && currentlyDisabled) || (!wantVisible && !currentlyDisabled))
        {
            _viewModel.TogglePeerVisibilityCommand.Execute(null);
        }
    }

    /// <summary>
    /// Shows the navigation method picker and returns the selected method.
    /// </summary>
    /// <returns>The selected navigation method, or null if cancelled.</returns>
    public Task<NavigationMethod?> ShowNavigationPickerAsync()
    {
        return NavigationMethodPicker.ShowAsync();
    }
}
