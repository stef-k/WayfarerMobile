using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.UI.Maui;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile;

/// <summary>
/// Main page showing current location and tracking controls.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    /// <summary>
    /// Creates a new instance of MainPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        // Subscribe to map info events for tap handling
        MapControl.Info += OnMapInfo;

        // Wire up context menu events to ViewModel commands
        ContextMenu.NavigateToRequested += OnContextMenuNavigateTo;
        ContextMenu.ShareLocationRequested += OnContextMenuShare;
        ContextMenu.WikiSearchRequested += OnContextMenuWikiSearch;
        ContextMenu.NavigateGoogleMapsRequested += OnContextMenuNavigateGoogleMaps;
        ContextMenu.CloseRequested += OnContextMenuClose;
        ContextMenu.DeletePinRequested += OnContextMenuDeletePin;

        // Subscribe to ViewModel property changes to reset sheet state
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Wire up bottom sheet StateChanged event (done in code-behind for XamlC compatibility)
        // SfBottomSheet uses StateChanged, not Closed - we detect closure via Hidden state
        CheckInBottomSheet.StateChanged += OnCheckInSheetStateChanged;
    }

    #region Context Menu Event Handlers

    /// <summary>
    /// Handles navigate to request from context menu.
    /// </summary>
    private void OnContextMenuNavigateTo(object? sender, EventArgs e)
    {
        _viewModel.NavigateToContextLocationCommand.Execute(null);
    }

    /// <summary>
    /// Handles share location request from context menu.
    /// </summary>
    private void OnContextMenuShare(object? sender, EventArgs e)
    {
        _viewModel.ShareContextLocationCommand.Execute(null);
    }

    /// <summary>
    /// Handles Wikipedia search request from context menu.
    /// </summary>
    private void OnContextMenuWikiSearch(object? sender, EventArgs e)
    {
        _viewModel.SearchWikipediaCommand.Execute(null);
    }

    /// <summary>
    /// Handles navigate via Google Maps request from context menu (fallback only).
    /// </summary>
    private void OnContextMenuNavigateGoogleMaps(object? sender, EventArgs e)
    {
        // This is only called if PlaceContextMenu's direct launch fails
        _viewModel.OpenInGoogleMapsCommand.Execute(null);
    }

    /// <summary>
    /// Handles close request from context menu.
    /// </summary>
    private void OnContextMenuClose(object? sender, EventArgs e)
    {
        _viewModel.HideContextMenuCommand.Execute(null);
    }

    /// <summary>
    /// Handles delete pin request from context menu.
    /// </summary>
    private void OnContextMenuDeletePin(object? sender, EventArgs e)
    {
        _viewModel.ClearDroppedPinCommand.Execute(null);
    }

    #endregion

    /// <summary>
    /// Handles map info events for drop pin mode and other interactions.
    /// </summary>
    private void OnMapInfo(object? sender, MapInfoEventArgs e)
    {
        // Get map info from all layers
        var map = MapControl.Map;
        if (map == null)
            return;

        var mapInfo = e.GetMapInfo(map.Layers);

        // Check if we have a valid world position
        if (mapInfo?.WorldPosition == null)
            return;

        try
        {
            // Convert world position (Web Mercator) to lat/lon
            var worldPos = mapInfo.WorldPosition;
            var lonLat = SphericalMercator.ToLonLat(worldPos.X, worldPos.Y);

            // Check if tapping on existing dropped pin (when not in drop pin mode)
            if (!_viewModel.IsDropPinModeActive && _viewModel.HasDroppedPin)
            {
                if (_viewModel.IsNearDroppedPin(lonLat.lat, lonLat.lon))
                {
                    // Reopen context menu for the existing pin
                    _viewModel.ReopenContextMenuFromPin();
                    return;
                }
            }

            // If drop pin mode is active, create new pin
            if (_viewModel.IsDropPinModeActive)
            {
                // Show context menu at tapped location
                _viewModel.ShowContextMenu(lonLat.lat, lonLat.lon);

                // Deactivate drop pin mode after successful tap
                _viewModel.IsDropPinModeActive = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnMapInfo: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the page disappears.
    /// </summary>
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _viewModel.OnDisappearingAsync();
    }

    /// <summary>
    /// Toggles drop pin mode - when active, tapping the map shows a context menu.
    /// </summary>
    private async void OnDropPinClicked(object? sender, EventArgs e)
    {
        _viewModel.IsDropPinModeActive = !_viewModel.IsDropPinModeActive;

        if (_viewModel.IsDropPinModeActive)
        {
            await this.DisplayAlertAsync("Drop Pin Mode",
                "Tap anywhere on the map to show options for that location.\n\n" +
                "Tap the button again to cancel.",
                "OK");
        }
    }

    /// <summary>
    /// Handles the check-in sheet state changes.
    /// Detects when sheet is closed (Hidden state) to run cleanup logic.
    /// </summary>
    private async void OnCheckInSheetStateChanged(object? sender, Syncfusion.Maui.Toolkit.BottomSheet.StateChangedEventArgs e)
    {
        // Only handle when sheet becomes hidden (closed)
        if (e.NewState == Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.Hidden)
        {
            _viewModel.IsCheckInSheetOpen = false;
            await _viewModel.OnCheckInSheetClosedAsync();
        }
    }

    /// <summary>
    /// Handles the check-in close button click.
    /// </summary>
    private async void OnCheckInCloseClicked(object? sender, EventArgs e)
    {
        _viewModel.IsCheckInSheetOpen = false;
        await _viewModel.OnCheckInSheetClosedAsync();
    }

    /// <summary>
    /// Shares the current check-in location via Google Maps link.
    /// </summary>
    private async void OnCheckInShareLocationClicked(object? sender, EventArgs e)
    {
        var location = _viewModel.CheckInViewModel.CurrentLocation;
        if (location == null)
            return;

        try
        {
            var googleMapsUrl = $"https://www.google.com/maps?q={location.Latitude:F6},{location.Longitude:F6}";
            await Share.RequestAsync(new ShareTextRequest
            {
                Title = "Share Location",
                Text = $"Check out this location:\n{googleMapsUrl}"
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] Failed to share location: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies the current check-in location coordinates to clipboard.
    /// </summary>
    private async void OnCheckInCopyCoordinatesClicked(object? sender, EventArgs e)
    {
        var location = _viewModel.CheckInViewModel.CurrentLocation;
        if (location == null)
            return;

        try
        {
            var coords = $"{location.Latitude:F6}, {location.Longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            await this.DisplayAlertAsync("Copied", $"Coordinates copied to clipboard:\n{coords}", "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] Failed to copy coordinates: {ex.Message}");
        }
    }

    /// <summary>
    /// Scrolls the Notes editor into view when focused to avoid keyboard overlap.
    /// </summary>
    private async void OnNotesEditorFocused(object? sender, FocusEventArgs e)
    {
        // Small delay to allow keyboard to appear
        await Task.Delay(300);

        // Find the parent ScrollView and scroll to make the notes section visible
        if (sender is Editor editor && NotesSection?.Parent?.Parent is ScrollView scrollView)
        {
            await scrollView.ScrollToAsync(NotesSection, ScrollToPosition.Center, true);
        }
    }

    /// <summary>
    /// Handles ViewModel property changes to manage sheet state.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsCheckInSheetOpen) && _viewModel.IsCheckInSheetOpen)
        {
            // Reset sheet to fully expanded state when opening
            // This ensures the sheet always opens fully, not at the last dragged position
            CheckInBottomSheet.State = Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.FullExpanded;
        }
    }
}
