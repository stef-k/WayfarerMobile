using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.UI.Maui;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using WayfarerMobile.Core.Models;
using WayfarerMobile.ViewModels;
using WayfarerMobile.Views.Controls;
using Color = Mapsui.Styles.Color;
using Brush = Mapsui.Styles.Brush;
using Pen = Mapsui.Styles.Pen;
using Point = NetTopologySuite.Geometries.Point;

namespace WayfarerMobile;

/// <summary>
/// Main page showing current location and tracking controls.
/// Shell caches this root page - cleanup happens in destructor, not on Unloaded.
/// </summary>
public partial class MainPage : ContentPage, IQueryAttributable
{
    private const string TempMarkerLayerName = "PlaceCoordinateEditTempMarker";
    private readonly MainViewModel _viewModel;
    private readonly ILogger<MainPage> _logger;
    private TripDetails? _pendingTrip;
    private WritableLayer? _tempMarkerLayer;

    // Issue #191: Track the last processed LoadTripToken to detect Shell re-applying cached params.
    // Each navigation from TripsPage includes a unique token. If we see the same token twice,
    // it means Shell is re-applying cached parameters, not a fresh user selection.
    private Guid? _lastProcessedLoadTripToken;

    // Issue #185: Deterministic readiness gate to prevent ObjectDisposedException
    // Trip load awaits this gate; it fires only when all platform handlers are ready.
    // _isLoaded: Visual tree is attached (Loaded event fired)
    // _isAppearingComplete: ViewModel initialization (OnAppearingAsync) is done
    // Handler checks: Critical controls have non-null handlers (platform is truly ready)
    private bool _isLoaded;
    private bool _isAppearingComplete;
    private TaskCompletionSource _pageReadyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource _pageReadyCts = new();

    /// <summary>
    /// Creates a new instance of MainPage.
    /// </summary>
    /// <param name="viewModel">The view model for this page.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public MainPage(MainViewModel viewModel, ILogger<MainPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = viewModel;

        // D5: Use Loaded event - fires when visual tree is attached to window
        // This ensures Android Activity is ready before image loads can occur
        Loaded += OnPageLoaded;

        // Issue #185: Subscribe to HandlerChanged events for deterministic readiness
        // These fire when the platform-specific handlers are attached/ready
        HandlerChanged += OnControlHandlerChanged;
        MapControl.HandlerChanged += OnControlHandlerChanged;
        MainBottomSheet.HandlerChanged += OnControlHandlerChanged;

        // Subscribe to map info events for tap handling (page-owned control)
        MapControl.Info += OnMapInfo;

        // Wire up context menu events to ViewModel commands (page-owned control)
        ContextMenu.NavigateToRequested += OnContextMenuNavigateTo;
        ContextMenu.ShareLocationRequested += OnContextMenuShare;
        ContextMenu.WikiSearchRequested += OnContextMenuWikiSearch;
        ContextMenu.NavigateGoogleMapsRequested += OnContextMenuNavigateGoogleMaps;
        ContextMenu.CloseRequested += OnContextMenuClose;
        ContextMenu.DeletePinRequested += OnContextMenuDeletePin;

        // Wire up bottom sheet StateChanged event (page-owned control)
        MainBottomSheet.StateChanged += OnMainSheetStateChanged;

        // Subscribe to ViewModel property changes to manage sheet state.
        // Both MainViewModel and MainPage are Transient with the same lifetime,
        // so these subscriptions are safe and will be disposed together.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TripSheet.Editor.PropertyChanged += OnEditorPropertyChanged;

        // NOTE: Do NOT subscribe to Unloaded event for Shell root pages.
        // Shell caches root pages and reuses them on navigation.
        // Disposing on Unloaded breaks event subscriptions when Shell reuses the page.
    }

    #region Context Menu Event Handlers

    /// <summary>
    /// Handles navigate to request from context menu.
    /// </summary>
    private void OnContextMenuNavigateTo(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.NavigateToContextLocationCommand.Execute(null);
    }

    /// <summary>
    /// Handles share location request from context menu.
    /// </summary>
    private void OnContextMenuShare(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.ShareContextLocationCommand.Execute(null);
    }

    /// <summary>
    /// Handles Wikipedia search request from context menu.
    /// </summary>
    private void OnContextMenuWikiSearch(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.SearchWikipediaCommand.Execute(null);
    }

    /// <summary>
    /// Handles navigate via Google Maps request from context menu (fallback only).
    /// </summary>
    private void OnContextMenuNavigateGoogleMaps(object? sender, EventArgs e)
    {
        // This is only called if PlaceContextMenu's direct launch fails
        _viewModel.ContextMenu.OpenInGoogleMapsCommand.Execute(null);
    }

    /// <summary>
    /// Handles close request from context menu.
    /// </summary>
    private void OnContextMenuClose(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.HideContextMenuCommand.Execute(null);
    }

    /// <summary>
    /// Handles delete pin request from context menu.
    /// </summary>
    private void OnContextMenuDeletePin(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.ClearDroppedPinCommand.Execute(null);
    }

    #endregion

    /// <summary>
    /// Handles map info events for drop pin mode, trip feature taps, coordinate editing, and other interactions.
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

            // Handle place coordinate editing mode first (takes priority)
            if (_viewModel.TripSheet.Editor.IsPlaceCoordinateEditMode)
            {
                _viewModel.TripSheet.SetPendingPlaceCoordinates(lonLat.lat, lonLat.lon);
                UpdateTempMarker(lonLat.lat, lonLat.lon);
                return;
            }

            // Check if user tapped on a trip place or area feature
            if (HandleTripFeatureTap(mapInfo))
            {
                return;
            }

            // Check if tapping on existing dropped pin (when not in drop pin mode)
            if (!_viewModel.ContextMenu.IsDropPinModeActive && _viewModel.ContextMenu.HasDroppedPin)
            {
                if (_viewModel.ContextMenu.IsNearDroppedPin(lonLat.lat, lonLat.lon))
                {
                    // Reopen context menu for the existing pin
                    _viewModel.ContextMenu.ReopenContextMenuFromPin();
                    return;
                }
            }

            // If drop pin mode is active, create new pin
            if (_viewModel.ContextMenu.IsDropPinModeActive)
            {
                // Show context menu at tapped location
                _viewModel.ContextMenu.ShowContextMenu(lonLat.lat, lonLat.lon);

                // Deactivate drop pin mode after successful tap (done by ShowContextMenu)
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnMapInfo");
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
        if (!_viewModel.HasLoadedTrip || _viewModel.TripSheet.LoadedTrip == null)
            return false;

        // Get the feature that was tapped
        var feature = mapInfo.Feature;
        if (feature == null)
            return false;

        // Check for place tap
        if (feature["PlaceId"] is Guid placeId)
        {
            var place = _viewModel.TripSheet.LoadedTrip.AllPlaces.FirstOrDefault(p => p.Id == placeId);
            if (place != null)
            {
                _viewModel.TripSheet.SelectTripPlaceCommand.Execute(place);
                _viewModel.IsTripSheetOpen = true;
                return true;
            }
        }

        // Check for area tap
        if (feature["AreaId"] is Guid areaId)
        {
            var area = _viewModel.TripSheet.LoadedTrip.AllAreas.FirstOrDefault(a => a.Id == areaId);
            if (area != null)
            {
                _viewModel.TripSheet.SelectTripAreaCommand.Execute(area);
                _viewModel.IsTripSheetOpen = true;
                return true;
            }
        }

        // Check for segment tap
        if (feature["SegmentId"] is Guid segmentId)
        {
            var segment = _viewModel.TripSheet.LoadedTrip.Segments.FirstOrDefault(s => s.Id == segmentId);
            if (segment != null)
            {
                _viewModel.TripSheet.SelectTripSegmentCommand.Execute(segment);
                _viewModel.TripSheet.IsTripSheetOpen = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// D5: Called when the visual tree is attached to the window.
    /// This is the safe point to trigger image loads on Android.
    /// </summary>
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        _logger.LogDebug("OnPageLoaded: Visual tree attached, _isAppearingComplete={Flag}", _isAppearingComplete);
        _isLoaded = true;
        TrySetPageReady();
        await LoadPendingTripIfReadyAsync();
    }

    /// <summary>
    /// Issue #185: Handles HandlerChanged events from critical controls.
    /// Used to determine when platform handlers are ready.
    /// </summary>
    private void OnControlHandlerChanged(object? sender, EventArgs e)
    {
        // Determine control name and whether handler is attached or detached
        var (controlName, isAttached) = sender switch
        {
            MainPage page => ("MainPage", page.Handler != null),
            Mapsui.UI.Maui.MapControl map => ("MapControl", map.Handler != null),
            Syncfusion.Maui.Toolkit.BottomSheet.SfBottomSheet sheet => ("MainBottomSheet", sheet.Handler != null),
            _ => ("Unknown", false)
        };

        var action = isAttached ? "attached" : "detached";
        _logger.LogDebug("OnControlHandlerChanged: {ControlName} handler {Action}", controlName, action);

        // Only try to signal readiness on attach events
        if (isAttached)
        {
            TrySetPageReady();
        }
    }

    /// <summary>
    /// Issue #185: Resets the page readiness gate.
    /// Called on OnDisappearing and before each OnAppearing to ensure stale readiness never persists.
    /// </summary>
    private void ResetPageReadyGate()
    {
        _logger.LogDebug("ResetPageReadyGate: Resetting readiness gate");

        // Cancel any pending wait on the old gate
        _pageReadyCts.Cancel();
        _pageReadyCts.Dispose();

        // Create fresh CTS and TCS for the next appearance cycle
        _pageReadyCts = new CancellationTokenSource();
        _pageReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// Issue #185: Checks if all readiness conditions are met and signals the gate if so.
    /// Conditions: Loaded fired, OnAppearingAsync complete, all critical handlers non-null.
    /// </summary>
    private void TrySetPageReady()
    {
        // All conditions must be true
        if (!_isLoaded)
        {
            _logger.LogDebug("TrySetPageReady: Not ready - _isLoaded=false");
            return;
        }

        if (!_isAppearingComplete)
        {
            _logger.LogDebug("TrySetPageReady: Not ready - _isAppearingComplete=false");
            return;
        }

        if (Handler == null)
        {
            _logger.LogDebug("TrySetPageReady: Not ready - MainPage.Handler is null");
            return;
        }

        if (MapControl.Handler == null)
        {
            _logger.LogDebug("TrySetPageReady: Not ready - MapControl.Handler is null");
            return;
        }

        if (MainBottomSheet.Handler == null)
        {
            _logger.LogDebug("TrySetPageReady: Not ready - MainBottomSheet.Handler is null");
            return;
        }

        // All conditions met - signal readiness
        _logger.LogDebug("TrySetPageReady: All conditions met, signaling page ready");
        _pageReadyTcs.TrySetResult();
    }

    /// <summary>
    /// Called when the page appears.
    /// </summary>
    protected override async void OnAppearing()
    {
        // Issue #185: Reset readiness gate at start of each appearance cycle
        // This ensures stale readiness from previous suspend/resume never persists
        ResetPageReadyGate();

        base.OnAppearing();

        // Initialize map state, permissions, etc.
        await _viewModel.OnAppearingAsync();

        // D5: Mark appearing complete AFTER initialization finishes
        _isAppearingComplete = true;
        _logger.LogDebug("OnAppearing: OnAppearingAsync complete, _isLoaded={IsLoaded}", _isLoaded);

        // Issue #185: Check if all conditions now met (handlers may already be attached)
        TrySetPageReady();

        // D5: Try to load trip (will await the readiness gate)
        await LoadPendingTripIfReadyAsync();
    }

    /// <summary>
    /// Issue #185: Loads the pending trip only when the deterministic readiness gate is satisfied.
    /// The gate requires: Loaded fired, OnAppearingAsync complete, all critical handlers non-null.
    /// No magic delays - uses concrete platform readiness signals.
    /// </summary>
    private async Task LoadPendingTripIfReadyAsync()
    {
        if (_pendingTrip == null)
        {
            _logger.LogDebug("LoadPendingTripIfReadyAsync: No pending trip");
            return;
        }

        var trip = _pendingTrip;
        _logger.LogDebug("LoadPendingTripIfReadyAsync: Waiting for readiness gate, trip={TripName}", trip.Name);

        try
        {
            // Wait for the deterministic readiness gate with cancellation support
            // This will complete when TrySetPageReady() signals all conditions are met
            using var reg = _pageReadyCts.Token.Register(() =>
                _logger.LogDebug("LoadPendingTripIfReadyAsync: Readiness gate cancelled (page disappeared)"));

            await _pageReadyTcs.Task.WaitAsync(_pageReadyCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Page disappeared while waiting - keep pending trip for retry on next appear
            _logger.LogDebug("LoadPendingTripIfReadyAsync: Cancelled, keeping pending trip for retry");
            return;
        }

        // Re-check that pending trip is still set (another caller may have processed it)
        if (_pendingTrip == null)
        {
            _logger.LogDebug("LoadPendingTripIfReadyAsync: Pending trip was cleared by another caller");
            return;
        }

        // Clear pending trip now that we're committed to loading
        _pendingTrip = null;

        _logger.LogDebug("LoadPendingTripIfReadyAsync: Readiness gate passed, loading trip {TripName}", trip.Name);
        await _viewModel.LoadTripForNavigationAsync(trip);
        _logger.LogDebug("LoadPendingTripIfReadyAsync: After load, HasLoadedTrip={HasLoaded}", _viewModel.HasLoadedTrip);
    }

    /// <summary>
    /// Handles navigation query attributes.
    /// </summary>
    /// <param name="query">The query parameters from navigation.</param>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _logger.LogDebug("ApplyQueryAttributes: query keys={Keys}", string.Join(", ", query.Keys));

        if (query.TryGetValue("LoadTrip", out var tripObj) && tripObj is TripDetails trip)
        {
            // Issue #191: Detect Shell re-applying cached query parameters.
            // Each navigation from TripsPage includes a unique LoadTripToken.
            // If we see the same token twice, Shell is re-applying cached params → skip.
            // If it's a new token (or no token from old code paths), proceed to load.
            var token = query.TryGetValue("LoadTripToken", out var tokenObj) && tokenObj is Guid t ? t : (Guid?)null;

            if (token.HasValue && token == _lastProcessedLoadTripToken)
            {
                _logger.LogDebug(
                    "ApplyQueryAttributes: Skipping trip {TripId} - same token (Shell re-applying cached params)",
                    trip.Id);
                return;
            }

            // Store the token (if present) to detect future re-applications
            if (token.HasValue)
            {
                _lastProcessedLoadTripToken = token;
            }

            _logger.LogDebug("ApplyQueryAttributes: Setting pending trip {TripName} ({TripId})", trip.Name, trip.Id);
            _pendingTrip = trip;

            // D5: Edge case - If both flags already true (re-navigation), trigger load immediately
            // This handles the case where ApplyQueryAttributes is called after page is fully ready
            _ = LoadPendingTripIfReadyAsync();
        }

        // Handle UnloadTrip signal (when trip is deleted from TripsPage)
        if (query.TryGetValue("UnloadTrip", out var unloadObj) && unloadObj is bool unload && unload)
        {
            _viewModel.UnloadTrip();
        }

        // Handle selection restoration from sub-editors (notes, marker)
        if (query.TryGetValue("restoreEntityType", out var entityTypeObj) &&
            query.TryGetValue("restoreEntityId", out var entityIdObj))
        {
            var entityType = entityTypeObj?.ToString();
            if (Guid.TryParse(entityIdObj?.ToString(), out var entityId))
            {
                _viewModel.RestoreSelectionFromSubEditor(entityType, entityId);
            }
        }

        // Handle NavigationRoute from groups page (navigate to member)
        if (query.TryGetValue("NavigationRoute", out var routeObj) &&
            routeObj is NavigationRoute route)
        {
            // Start navigation with the provided route
            _ = _viewModel.Navigation.StartNavigationWithRouteAsync(route);
        }
    }

    /// <summary>
    /// Called when the page disappears.
    /// </summary>
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        // Issue #185: Reset readiness gate immediately to cancel any pending trip load
        // This ensures stale readiness never persists after suspend/resume
        _isAppearingComplete = false;
        ResetPageReadyGate();

        // Call existing ViewModel cleanup
        await _viewModel.OnDisappearingAsync();
    }

    /// <summary>
    /// Toggles drop pin mode - when active, tapping the map shows a context menu.
    /// </summary>
    private async void OnDropPinClicked(object? sender, EventArgs e)
    {
        _viewModel.ContextMenu.IsDropPinModeActive = !_viewModel.ContextMenu.IsDropPinModeActive;

        if (_viewModel.ContextMenu.IsDropPinModeActive)
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
        _logger.LogDebug("SheetStateChanged: {OldState} -> {NewState}, IsNavigatingToSubEditor={IsNavigatingToSubEditor}, IsTripSheetOpen={IsTripSheetOpen}, SelectedPlace={SelectedPlace}",
            e.OldState, e.NewState, _viewModel.TripSheet.IsNavigatingToSubEditor, _viewModel.IsTripSheetOpen, _viewModel.TripSheet.SelectedTripPlace?.Name ?? "null");

        // Only handle when sheet becomes hidden (closed)
        if (e.NewState == Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.Hidden)
        {
            // Don't run cleanup if navigating to sub-editor (notes, marker, etc.)
            // The sheet goes hidden during navigation but we want to preserve selection
            if (_viewModel.TripSheet.IsNavigatingToSubEditor)
            {
                _logger.LogDebug("Skipping cleanup - navigating to sub-editor");
                return;
            }

            // Handle check-in sheet cleanup if it was open
            if (_viewModel.IsCheckInSheetOpen)
            {
                _logger.LogDebug("Running check-in sheet cleanup");
                _viewModel.IsCheckInSheetOpen = false;
                await _viewModel.OnCheckInSheetClosedAsync();
            }

            // Handle trip sheet cleanup if it was open
            if (_viewModel.IsTripSheetOpen)
            {
                _logger.LogDebug("Running trip sheet cleanup - calling TripSheetBackCommand");
                _viewModel.IsTripSheetOpen = false;
                _viewModel.TripSheet.TripSheetBackCommand.Execute(null);
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
            _logger.LogError(ex, "Failed to share location");
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
            _logger.LogError(ex, "Failed to copy coordinates");
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
        // Ensure sheet state is set correctly when trip sheet opens
        if (e.PropertyName == nameof(MainViewModel.IsTripSheetOpen))
        {
            if (_viewModel.IsTripSheetOpen)
            {
                MainBottomSheet.State = Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.FullExpanded;
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsCheckInSheetOpen) && _viewModel.IsCheckInSheetOpen)
        {
            MainBottomSheet.State = Syncfusion.Maui.Toolkit.BottomSheet.BottomSheetState.FullExpanded;
        }
    }

    /// <summary>
    /// Handles Editor property changes for coordinate editing mode.
    /// </summary>
    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Handle place coordinate editing mode changes
        if (e.PropertyName == nameof(TripItemEditorViewModel.IsPlaceCoordinateEditMode))
        {
            if (_viewModel.TripSheet.Editor.IsPlaceCoordinateEditMode)
            {
                // Entering edit mode - show temp marker at current place location
                EnsureTempMarkerLayer();
                if (_viewModel.TripSheet.Editor.PendingPlaceLatitude.HasValue && _viewModel.TripSheet.Editor.PendingPlaceLongitude.HasValue)
                {
                    UpdateTempMarker(_viewModel.TripSheet.Editor.PendingPlaceLatitude.Value, _viewModel.TripSheet.Editor.PendingPlaceLongitude.Value);
                }
            }
            else
            {
                // Exiting edit mode - remove temp marker
                RemoveTempMarker();
            }
        }
    }

    #region Temp Marker Helpers

    /// <summary>
    /// Ensures the temp marker layer exists on the map.
    /// </summary>
    private void EnsureTempMarkerLayer()
    {
        var map = MapControl.Map;
        if (map == null)
            return;

        // Check if layer already exists
        _tempMarkerLayer = map.Layers.FirstOrDefault(l => l.Name == TempMarkerLayerName) as WritableLayer;
        if (_tempMarkerLayer != null)
            return;

        // Create and add the layer
        _tempMarkerLayer = new WritableLayer { Name = TempMarkerLayerName };
        map.Layers.Add(_tempMarkerLayer);
    }

    /// <summary>
    /// Updates or creates the temporary marker at the specified coordinates.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    private void UpdateTempMarker(double latitude, double longitude)
    {
        if (_tempMarkerLayer == null)
        {
            EnsureTempMarkerLayer();
            if (_tempMarkerLayer == null)
                return;
        }

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
            SymbolScale = 0.8,
            Fill = new Brush(Color.FromArgb(255, 255, 87, 34)), // Orange (Material Deep Orange)
            Outline = new Pen(Color.White, 3),
            SymbolType = SymbolType.Ellipse
        };
    }

    #endregion

    /// <summary>
    /// Shows the navigation method picker and returns the selected method.
    /// </summary>
    /// <returns>The selected navigation method, or null if cancelled.</returns>
    public Task<NavigationMethod?> ShowNavigationPickerAsync()
    {
        return NavigationMethodPicker.ShowAsync();
    }
}
