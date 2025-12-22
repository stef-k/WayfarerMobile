using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.UI.Maui;
using WayfarerMobile.Core.Models;
using WayfarerMobile.ViewModels;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile;

/// <summary>
/// Main page showing current location and tracking controls.
/// </summary>
public partial class MainPage : ContentPage, IQueryAttributable
{
    private readonly MainViewModel _viewModel;
    private TripDetails? _pendingTrip;

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
        MainBottomSheet.StateChanged += OnMainSheetStateChanged;
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
    /// Handles map info events for drop pin mode, trip feature taps, and other interactions.
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
            // Check if user tapped on a trip place or area feature
            if (HandleTripFeatureTap(mapInfo))
            {
                return;
            }

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
    /// Handles taps on trip place or area features.
    /// </summary>
    /// <param name="mapInfo">The map info with feature data.</param>
    /// <returns>True if a trip feature was tapped and handled.</returns>
    private bool HandleTripFeatureTap(Mapsui.MapInfo mapInfo)
    {
        // Check if a trip is loaded
        if (!_viewModel.HasLoadedTrip || _viewModel.LoadedTrip == null)
            return false;

        // Get the feature that was tapped
        var feature = mapInfo.Feature;
        if (feature == null)
            return false;

        // Check for place tap
        if (feature["PlaceId"] is Guid placeId)
        {
            var place = _viewModel.LoadedTrip.AllPlaces.FirstOrDefault(p => p.Id == placeId);
            if (place != null)
            {
                _viewModel.SelectTripPlaceCommand.Execute(place);
                _viewModel.IsTripSheetOpen = true;
                return true;
            }
        }

        // Check for area tap
        if (feature["AreaId"] is Guid areaId)
        {
            var area = _viewModel.LoadedTrip.AllAreas.FirstOrDefault(a => a.Id == areaId);
            if (area != null)
            {
                _viewModel.SelectTripAreaCommand.Execute(area);
                _viewModel.IsTripSheetOpen = true;
                return true;
            }
        }

        // Check for segment tap
        if (feature["SegmentId"] is Guid segmentId)
        {
            var segment = _viewModel.LoadedTrip.Segments.FirstOrDefault(s => s.Id == segmentId);
            if (segment != null)
            {
                _viewModel.SelectTripSegmentCommand.Execute(segment);
                _viewModel.IsTripSheetOpen = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();

        // Load pending trip if navigated with LoadTrip parameter
        if (_pendingTrip != null)
        {
            await _viewModel.LoadTripForNavigationAsync(_pendingTrip);
            _pendingTrip = null;
        }
    }

    /// <summary>
    /// Handles navigation query attributes.
    /// </summary>
    /// <param name="query">The query parameters from navigation.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("LoadTrip", out var tripObj) && tripObj is TripDetails trip)
        {
            _pendingTrip = trip;
        }

        // Handle UnloadTrip signal (when trip is deleted from TripsPage)
        if (query.TryGetValue("UnloadTrip", out var unloadObj) && unloadObj is bool unload && unload)
        {
            _viewModel.UnloadTrip();
        }
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
    /// Handles the main bottom sheet state changes.
    /// Detects when sheet is closed (Hidden state) to run cleanup logic for whichever content was showing.
    /// </summary>
    private async void OnMainSheetStateChanged(object? sender, Syncfusion.Maui.Toolkit.BottomSheet.StateChangedEventArgs e)
    {
        // Only handle when sheet becomes hidden (closed)
        if (e.NewState == Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.Hidden)
        {
            // Handle check-in sheet cleanup if it was open
            if (_viewModel.IsCheckInSheetOpen)
            {
                _viewModel.IsCheckInSheetOpen = false;
                await _viewModel.OnCheckInSheetClosedAsync();
            }

            // Handle trip sheet cleanup if it was open
            if (_viewModel.IsTripSheetOpen)
            {
                _viewModel.IsTripSheetOpen = false;
                _viewModel.TripSheetBackCommand.Execute(null);
            }
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
        // Reset sheet to fully expanded state when either sheet opens
        // This ensures the sheet always opens fully, not at the last dragged position
        if (e.PropertyName == nameof(MainViewModel.IsCheckInSheetOpen) && _viewModel.IsCheckInSheetOpen)
        {
            MainBottomSheet.State = Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.FullExpanded;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsTripSheetOpen) && _viewModel.IsTripSheetOpen)
        {
            MainBottomSheet.State = Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.FullExpanded;
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
