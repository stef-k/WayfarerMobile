using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for trip sheet coordination.
/// Manages trip sheet state, selection, and trip editing operations.
/// Extracted from MainViewModel to handle trip sheet-specific concerns.
/// </summary>
public partial class TripSheetViewModel : BaseViewModel
{
    #region Fields

    private readonly ITripSyncService _tripSyncService;
    private readonly DatabaseService _databaseService;
    private readonly IWikipediaService _wikipediaService;
    private readonly IToastService _toastService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TripSheetViewModel> _logger;

    // Callbacks to parent ViewModel
    private ITripSheetCallbacks? _callbacks;

    // Cached search results to avoid recomputation on every property access
    private List<TripPlace> _cachedSearchResults = new();

    #endregion

    #region Observable Properties - Core Sheet State

    /// <summary>
    /// Gets or sets whether the trip sheet is open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    private bool _isTripSheetOpen;

    /// <summary>
    /// Gets or sets the currently loaded trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedTrip))]
    [NotifyPropertyChangedFor(nameof(TripPlaceCount))]
    [NotifyPropertyChangedFor(nameof(HasTripSegments))]
    [NotifyPropertyChangedFor(nameof(TripNotesPreview))]
    [NotifyPropertyChangedFor(nameof(TripNotesHtml))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    private TripDetails? _loadedTrip;

    #endregion

    #region Observable Properties - Selection State

    /// <summary>
    /// Gets or sets the selected place in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingPlace))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(SelectedTripPlaceNotesHtml))]
    private TripPlace? _selectedTripPlace;

    /// <summary>
    /// Gets or sets the selected area in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingArea))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripAreaNotesHtml))]
    private TripArea? _selectedTripArea;

    /// <summary>
    /// Gets or sets the selected segment in the trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingSegment))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTripSegmentNotesHtml))]
    private TripSegment? _selectedTripSegment;

    /// <summary>
    /// Gets or sets the selected region for notes display.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTripRegionNotesHtml))]
    private TripRegion? _selectedTripRegion;

    /// <summary>
    /// Legacy property for compatibility.
    /// </summary>
    private TripPlace? _selectedPlace;

    /// <summary>
    /// Gets or sets the selected place (legacy compatibility).
    /// </summary>
    public TripPlace? SelectedPlace
    {
        get => _selectedPlace;
        set => SetProperty(ref _selectedPlace, value);
    }

    #endregion

    #region Observable Properties - Notes Display State

    /// <summary>
    /// Gets or sets whether trip notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingTripNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    [NotifyPropertyChangedFor(nameof(TripSheetSubtitle))]
    private bool _isShowingTripNotes;

    /// <summary>
    /// Gets or sets whether area notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingAreaNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingAreaNotes;

    /// <summary>
    /// Gets or sets whether segment notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingSegmentNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingSegmentNotes;

    /// <summary>
    /// Gets or sets whether region notes detail view is showing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingRegionNotes))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingOverview))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingDetails))]
    [NotifyPropertyChangedFor(nameof(IsTripSheetShowingScrollableContent))]
    [NotifyPropertyChangedFor(nameof(TripSheetTitle))]
    private bool _isShowingRegionNotes;

    #endregion

    #region Observable Properties - Coordinate Editing State

    /// <summary>
    /// Gets or sets whether place coordinate editing mode is active.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(IsAnyEditModeActive))]
    private bool _isPlaceCoordinateEditMode;

    /// <summary>
    /// Gets or sets the pending latitude during place coordinate editing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingPlaceCoordinatesText))]
    private double? _pendingPlaceLatitude;

    /// <summary>
    /// Gets or sets the pending longitude during place coordinate editing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingPlaceCoordinates))]
    [NotifyPropertyChangedFor(nameof(PendingPlaceCoordinatesText))]
    private double? _pendingPlaceLongitude;

    /// <summary>
    /// Gets or sets the place being edited for coordinates.
    /// </summary>
    [ObservableProperty]
    private TripPlace? _placeBeingEditedForCoordinates;

    #endregion

    #region Observable Properties - Place Search State

    /// <summary>
    /// Gets or sets whether the place search bar is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isPlaceSearchVisible;

    /// <summary>
    /// Gets or sets the place search query text.
    /// </summary>
    [ObservableProperty]
    private string _placeSearchQuery = string.Empty;

    #endregion

    #region Properties - Sub-Editor Navigation

    /// <summary>
    /// Tracks whether we're navigating to a sub-editor page (notes, marker, etc.).
    /// When true, OnDisappearingAsync should NOT unload the trip or close sheets.
    /// Also checked by MainPage to avoid clearing selection when navigating to sub-editors.
    /// </summary>
    public bool IsNavigatingToSubEditor { get; set; }

    /// <summary>
    /// Pending entity ID for selection restoration from sub-editor navigation.
    /// Set by ApplyQueryAttributes, consumed by OnAppearingAsync.
    /// </summary>
    private (string? EntityType, Guid EntityId)? _pendingSelectionRestore;

    #endregion

    #region Computed Properties - Trip Info

    /// <summary>
    /// Gets whether a trip is currently loaded.
    /// </summary>
    public bool HasLoadedTrip => LoadedTrip != null;

    /// <summary>
    /// Gets the number of places in the loaded trip.
    /// </summary>
    public int TripPlaceCount => LoadedTrip?.AllPlaces.Count ?? 0;

    /// <summary>
    /// Gets whether the loaded trip has segments.
    /// </summary>
    public bool HasTripSegments => LoadedTrip?.Segments.Count > 0;

    /// <summary>
    /// Gets a preview of the trip notes (first 200 characters).
    /// </summary>
    public string? TripNotesPreview
    {
        get
        {
            if (string.IsNullOrEmpty(LoadedTrip?.Notes))
                return null;

            var notes = LoadedTrip.Notes;
            return notes.Length > 200 ? notes[..200] + "..." : notes;
        }
    }

    /// <summary>
    /// Gets the HTML content for the trip notes, wrapped for WebView display.
    /// </summary>
    public HtmlWebViewSource? TripNotesHtml
    {
        get
        {
            if (LoadedTrip?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(LoadedTrip.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    #endregion

    #region Computed Properties - Sheet Display Mode

    /// <summary>
    /// Gets whether the trip sheet is showing the overview (no selection active).
    /// </summary>
    public bool IsTripSheetShowingOverview =>
        SelectedTripPlace == null &&
        SelectedTripArea == null &&
        SelectedTripSegment == null &&
        !IsShowingTripNotes &&
        !IsShowingRegionNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing trip notes.
    /// </summary>
    public bool IsTripSheetShowingTripNotes => IsShowingTripNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing area notes.
    /// </summary>
    public bool IsTripSheetShowingAreaNotes => IsShowingAreaNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing segment notes.
    /// </summary>
    public bool IsTripSheetShowingSegmentNotes => IsShowingSegmentNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing region notes.
    /// </summary>
    public bool IsTripSheetShowingRegionNotes => IsShowingRegionNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing scrollable content.
    /// True for overview, area details, or segment details. False for place details or notes views.
    /// </summary>
    public bool IsTripSheetShowingScrollableContent =>
        IsTripSheetShowingOverview ||
        IsTripSheetShowingArea ||
        IsTripSheetShowingSegment ||
        IsShowingTripNotes ||
        IsShowingAreaNotes ||
        IsShowingSegmentNotes ||
        IsShowingRegionNotes;

    /// <summary>
    /// Gets whether the trip sheet is showing a place.
    /// </summary>
    public bool IsTripSheetShowingPlace => SelectedTripPlace != null;

    /// <summary>
    /// Gets whether the trip sheet is showing an area.
    /// </summary>
    public bool IsTripSheetShowingArea => SelectedTripArea != null;

    /// <summary>
    /// Gets whether the trip sheet is showing a segment.
    /// </summary>
    public bool IsTripSheetShowingSegment => SelectedTripSegment != null;

    /// <summary>
    /// Gets whether the trip sheet is showing any detail view (not overview).
    /// </summary>
    public bool IsTripSheetShowingDetails => !IsTripSheetShowingOverview;

    #endregion

    #region Computed Properties - Title and Subtitle

    /// <summary>
    /// Gets the trip sheet title based on current state.
    /// </summary>
    public string TripSheetTitle
    {
        get
        {
            if (IsTripSheetShowingPlace)
                return SelectedTripPlace?.Name ?? "Place";

            if (IsTripSheetShowingArea)
                return SelectedTripArea?.Name ?? "Area";

            if (IsTripSheetShowingSegment)
                return "Segment";

            if (IsShowingTripNotes)
                return "Trip Notes";

            if (IsShowingAreaNotes)
                return $"{SelectedTripArea?.Name ?? "Area"} - Notes";

            if (IsShowingSegmentNotes)
                return "Segment Notes";

            if (IsShowingRegionNotes)
                return $"{SelectedTripRegion?.Name ?? "Region"} - Notes";

            return LoadedTrip?.Name ?? "Trip Overview";
        }
    }

    /// <summary>
    /// Gets the trip sheet subtitle based on current state.
    /// </summary>
    public string? TripSheetSubtitle
    {
        get
        {
            if (IsTripSheetShowingOverview && LoadedTrip != null)
            {
                var parts = new List<string>();
                if (LoadedTrip.AllPlaces.Count > 0)
                    parts.Add($"{LoadedTrip.AllPlaces.Count} places");
                if (LoadedTrip.AllAreas.Count > 0)
                    parts.Add($"{LoadedTrip.AllAreas.Count} areas");
                if (LoadedTrip.Segments.Count > 0)
                    parts.Add($"{LoadedTrip.Segments.Count} segments");
                return string.Join(" · ", parts);
            }

            if (IsTripSheetShowingPlace && SelectedTripPlace != null)
                return SelectedTripPlace.Address;

            if (IsTripSheetShowingArea && SelectedTripArea != null)
                return "Tap to view on map";

            if (IsTripSheetShowingSegment && SelectedTripSegment != null)
                return $"{SelectedTripSegment.OriginName} → {SelectedTripSegment.DestinationName}";

            return null;
        }
    }

    #endregion

    #region Computed Properties - Selected Item Details

    /// <summary>
    /// Gets the selected trip place coordinates as text.
    /// </summary>
    public string? SelectedTripPlaceCoordinates => SelectedTripPlace != null
        ? $"{SelectedTripPlace.Latitude:F5}, {SelectedTripPlace.Longitude:F5}"
        : null;

    /// <summary>
    /// Gets the HTML content for the selected trip place notes.
    /// </summary>
    public HtmlWebViewSource? SelectedTripPlaceNotesHtml
    {
        get
        {
            if (SelectedTripPlace?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripPlace.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets the HTML content for the selected trip area notes.
    /// </summary>
    public HtmlWebViewSource? SelectedTripAreaNotesHtml
    {
        get
        {
            if (SelectedTripArea?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripArea.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets the HTML content for the selected trip segment notes.
    /// </summary>
    public HtmlWebViewSource? SelectedTripSegmentNotesHtml
    {
        get
        {
            if (SelectedTripSegment?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripSegment.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    /// <summary>
    /// Gets the HTML content for the selected trip region notes.
    /// </summary>
    public HtmlWebViewSource? SelectedTripRegionNotesHtml
    {
        get
        {
            if (SelectedTripRegion?.Notes == null)
                return null;

            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            return NotesViewerHelper.PrepareNotesHtml(SelectedTripRegion.Notes, _settingsService.ServerUrl, isDark);
        }
    }

    #endregion

    #region Computed Properties - Coordinate Editing

    /// <summary>
    /// Gets whether place coordinate editing is active.
    /// </summary>
    public bool IsEditingPlaceCoordinates => IsPlaceCoordinateEditMode;

    /// <summary>
    /// Gets whether any edit mode is active.
    /// </summary>
    public bool IsAnyEditModeActive => IsPlaceCoordinateEditMode;

    /// <summary>
    /// Gets whether pending place coordinates are set.
    /// </summary>
    public bool HasPendingPlaceCoordinates =>
        PendingPlaceLatitude.HasValue && PendingPlaceLongitude.HasValue;

    /// <summary>
    /// Gets the pending place coordinates as text.
    /// </summary>
    public string PendingPlaceCoordinatesText => HasPendingPlaceCoordinates
        ? $"{PendingPlaceLatitude:F5}, {PendingPlaceLongitude:F5}"
        : "Tap on map to set location";

    #endregion

    #region Computed Properties - Place Search

    /// <summary>
    /// Gets the filtered list of places matching the search query.
    /// </summary>
    public List<TripPlace> PlaceSearchResults => _cachedSearchResults;

    /// <summary>
    /// Gets whether there are search results to display.
    /// </summary>
    public bool HasPlaceSearchResults => _cachedSearchResults.Count > 0;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripSheetViewModel.
    /// </summary>
    public TripSheetViewModel(
        ITripSyncService tripSyncService,
        DatabaseService databaseService,
        IWikipediaService wikipediaService,
        IToastService toastService,
        ISettingsService settingsService,
        ILogger<TripSheetViewModel> logger)
    {
        _tripSyncService = tripSyncService;
        _databaseService = databaseService;
        _wikipediaService = wikipediaService;
        _toastService = toastService;
        _settingsService = settingsService;
        _logger = logger;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Sets the callback interface to the parent ViewModel.
    /// Must be called before using methods that depend on parent state.
    /// </summary>
    public void SetCallbacks(ITripSheetCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    #endregion

    #region Partial Handlers

    /// <summary>
    /// Called when LoadedTrip changes - resets search state.
    /// Note: CurrentLoadedTripId is synced by MainViewModel's OnTripSheetPropertyChanged.
    /// </summary>
    partial void OnLoadedTripChanged(TripDetails? value)
    {
        _logger.LogDebug("OnLoadedTripChanged: LoadedTrip changed to {TripId}", value?.Id);

        // Reset search state when trip changes
        IsPlaceSearchVisible = false;
        PlaceSearchQuery = string.Empty;
    }

    /// <summary>
    /// Called when PlaceSearchQuery changes - updates cached results.
    /// </summary>
    partial void OnPlaceSearchQueryChanged(string value)
    {
        UpdateSearchResults();
    }

    /// <summary>
    /// Updates the cached search results.
    /// </summary>
    private void UpdateSearchResults()
    {
        if (LoadedTrip == null || string.IsNullOrWhiteSpace(PlaceSearchQuery))
        {
            _cachedSearchResults = new List<TripPlace>();
        }
        else
        {
            var query = PlaceSearchQuery.Trim();
            _cachedSearchResults = LoadedTrip.AllPlaces
                .Where(p =>
                    (p.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                    (p.Address?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
        }

        OnPropertyChanged(nameof(PlaceSearchResults));
        OnPropertyChanged(nameof(HasPlaceSearchResults));
    }

    #endregion

    #region Commands - Sheet Navigation

    /// <summary>
    /// Toggles the trip sheet open/closed.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTripSheetAsync()
    {
        if (IsTripSheetOpen)
        {
            // Close the sheet
            ClearTripSheetSelection();
            IsTripSheetOpen = false;
        }
        else if (HasLoadedTrip)
        {
            // Open the sheet with the loaded trip
            IsTripSheetOpen = true;
        }
        else
        {
            // No trip loaded, navigate to trips page
            await GoToMyTripsAsync();
        }
    }

    /// <summary>
    /// Closes the trip sheet.
    /// </summary>
    [RelayCommand]
    private void CloseTripSheet()
    {
        IsTripSheetOpen = false;
    }

    /// <summary>
    /// Navigates to the trips page.
    /// </summary>
    [RelayCommand]
    private async Task GoToMyTripsAsync()
    {
        IsTripSheetOpen = false;
        await (_callbacks?.NavigateToPageAsync("//trips") ?? Task.CompletedTask);
    }

    /// <summary>
    /// Handles back navigation within the trip sheet.
    /// </summary>
    [RelayCommand]
    private void TripSheetBack()
    {
        // Multi-level back navigation
        if (IsShowingAreaNotes)
        {
            IsShowingAreaNotes = false;
        }
        else if (IsShowingSegmentNotes)
        {
            IsShowingSegmentNotes = false;
        }
        else if (IsShowingRegionNotes)
        {
            IsShowingRegionNotes = false;
            SelectedTripRegion = null;
        }
        else
        {
            // Return to overview
            ClearTripSheetSelection();
        }
    }

    #endregion

    #region Commands - Selection

    /// <summary>
    /// Selects a trip place and shows its details.
    /// </summary>
    [RelayCommand]
    private void SelectTripPlace(TripPlace? place)
    {
        CloseSearchIfActive();

        // Clear other selections
        SelectedTripArea = null;
        SelectedTripSegment = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
        IsShowingRegionNotes = false;
        SelectedTripRegion = null;

        // Set the selection
        SelectedTripPlace = place;
        SelectedPlace = place; // Legacy compatibility

        if (place != null)
        {
            // Center map on place
            _callbacks?.CenterOnLocation(place.Latitude, place.Longitude, zoomLevel: 16);

            // Update map selection ring
            _callbacks?.UpdatePlaceSelection(place);

            // Disable location follow
            _callbacks?.SetFollowingLocation(false);
        }
    }

    /// <summary>
    /// Selects a trip area and shows its details.
    /// </summary>
    [RelayCommand]
    private void SelectTripArea(TripArea? area)
    {
        // Clear other selections
        SelectedTripPlace = null;
        SelectedPlace = null;
        SelectedTripSegment = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
        IsShowingRegionNotes = false;
        SelectedTripRegion = null;

        // Clear map selection
        _callbacks?.ClearPlaceSelection();

        // Set the selection
        SelectedTripArea = area;

        if (area != null && area.Center != null)
        {
            // Center map on area center
            _callbacks?.CenterOnLocation(area.Center.Latitude, area.Center.Longitude);

            // Disable location follow
            _callbacks?.SetFollowingLocation(false);
        }
    }

    /// <summary>
    /// Selects a trip segment and shows its details.
    /// </summary>
    [RelayCommand]
    private void SelectTripSegment(TripSegment? segment)
    {
        // Clear other selections
        SelectedTripPlace = null;
        SelectedPlace = null;
        SelectedTripArea = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
        IsShowingRegionNotes = false;
        SelectedTripRegion = null;

        // Clear map selection
        _callbacks?.ClearPlaceSelection();

        // Set the selection
        SelectedTripSegment = segment;
    }

    #endregion

    #region Commands - Notes Display

    /// <summary>
    /// Shows the trip notes view.
    /// </summary>
    [RelayCommand]
    private void ShowTripNotes()
    {
        IsShowingTripNotes = true;
    }

    /// <summary>
    /// Shows the area notes view.
    /// </summary>
    [RelayCommand]
    private void ShowAreaNotes()
    {
        IsShowingAreaNotes = true;
    }

    /// <summary>
    /// Shows the segment notes view.
    /// </summary>
    [RelayCommand]
    private void ShowSegmentNotes()
    {
        IsShowingSegmentNotes = true;
    }

    /// <summary>
    /// Shows the region notes view.
    /// </summary>
    [RelayCommand]
    private void ShowRegionNotes(TripRegion? region)
    {
        SelectedTripRegion = region;
        IsShowingRegionNotes = true;
    }

    #endregion

    #region Commands - Place Search

    /// <summary>
    /// Toggles the place search bar visibility.
    /// </summary>
    [RelayCommand]
    private void TogglePlaceSearch()
    {
        IsPlaceSearchVisible = !IsPlaceSearchVisible;
        if (!IsPlaceSearchVisible)
        {
            PlaceSearchQuery = string.Empty;
        }
    }

    /// <summary>
    /// Closes the place search and resets to normal view.
    /// </summary>
    private void CloseSearchIfActive()
    {
        if (IsPlaceSearchVisible)
        {
            IsPlaceSearchVisible = false;
            PlaceSearchQuery = string.Empty;
        }
    }

    #endregion

    #region Commands - Place Actions

    /// <summary>
    /// Navigates to the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        // Show transport mode picker
        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Navigation Mode",
            "Cancel",
            null,
            "Walk", "Drive", "Cycle") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        // Map display name to OSRM profile
        var profile = selected switch
        {
            "Walk" => "foot",
            "Drive" => "car",
            "Cycle" => "bike",
            _ => "foot"
        };

        // Save selection for next time
        _settingsService.LastTransportMode = profile;

        await (_callbacks?.StartNavigationToPlaceAsync(SelectedTripPlace.Id.ToString()) ?? Task.CompletedTask);
        IsTripSheetOpen = false;
    }

    /// <summary>
    /// Opens the selected trip place in external maps app.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceInMapsAsync()
    {
        if (SelectedTripPlace == null)
            return;

        try
        {
            var location = new Location(SelectedTripPlace.Latitude, SelectedTripPlace.Longitude);
            var options = new MapLaunchOptions { Name = SelectedTripPlace.Name };
            await Map.Default.OpenAsync(location, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open maps app");
            await _toastService.ShowErrorAsync("Could not open maps app");
        }
    }

    /// <summary>
    /// Copies the selected trip place coordinates to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyTripPlaceCoordsAsync()
    {
        if (SelectedTripPlace == null || SelectedTripPlaceCoordinates == null)
            return;

        await Clipboard.Default.SetTextAsync(SelectedTripPlaceCoordinates);
        await _toastService.ShowSuccessAsync("Coordinates copied");
    }

    /// <summary>
    /// Shares the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task ShareTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        var mapsUrl = $"https://www.google.com/maps/search/?api=1&query={SelectedTripPlace.Latitude},{SelectedTripPlace.Longitude}";
        var text = $"{SelectedTripPlace.Name}\n{mapsUrl}";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = SelectedTripPlace.Name
        });
    }

    /// <summary>
    /// Opens Wikipedia for the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceWikipediaAsync()
    {
        if (SelectedTripPlace == null)
            return;

        await _wikipediaService.OpenNearbyArticleAsync(
            SelectedTripPlace.Latitude,
            SelectedTripPlace.Longitude);
    }

    /// <summary>
    /// Shows the edit menu for the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task EditTripPlaceAsync()
    {
        if (SelectedTripPlace == null)
            return;

        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Edit Place",
            "Cancel",
            "Delete",
            "Edit Name", "Edit Notes", "Edit Coordinates", "Edit Marker") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        switch (selected)
        {
            case "Edit Name":
                await EditPlaceNameAsync(SelectedTripPlace);
                break;
            case "Edit Notes":
                await EditPlaceNotesAsync(SelectedTripPlace);
                break;
            case "Edit Coordinates":
                EnterPlaceCoordinateEditMode(SelectedTripPlace);
                break;
            case "Edit Marker":
                await EditPlaceMarkerAsync(SelectedTripPlace);
                break;
            case "Delete":
                await DeletePlaceAsync(SelectedTripPlace);
                break;
        }
    }

    #endregion

    #region Commands - Coordinate Editing

    /// <summary>
    /// Saves the edited place coordinates.
    /// </summary>
    [RelayCommand]
    private async Task SavePlaceCoordinatesAsync()
    {
        if (PlaceBeingEditedForCoordinates == null || !HasPendingPlaceCoordinates)
            return;

        var place = PlaceBeingEditedForCoordinates;
        var newLat = PendingPlaceLatitude!.Value;
        var newLon = PendingPlaceLongitude!.Value;

        // Update place coordinates in the loaded trip
        if (LoadedTrip != null)
        {
            foreach (var region in LoadedTrip.Regions)
            {
                var placeToUpdate = region.Places.FirstOrDefault(p => p.Id == place.Id);
                if (placeToUpdate != null)
                {
                    placeToUpdate.Latitude = newLat;
                    placeToUpdate.Longitude = newLon;
                    break;
                }
            }
        }

        // Exit edit mode
        ExitPlaceCoordinateEditMode();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(LoadedTrip) ?? Task.CompletedTask);

        // Sync to server
        if (LoadedTrip != null)
        {
            await _tripSyncService.UpdatePlaceAsync(
                place.Id,
                LoadedTrip.Id,
                latitude: newLat,
                longitude: newLon);
        }

        // Reopen trip sheet with updated place
        IsTripSheetOpen = true;
        SelectTripPlace(place);

        await _toastService.ShowSuccessAsync("Coordinates updated");
    }

    /// <summary>
    /// Cancels place coordinate editing.
    /// </summary>
    [RelayCommand]
    private void CancelPlaceCoordinateEditing()
    {
        var place = PlaceBeingEditedForCoordinates;
        ExitPlaceCoordinateEditMode();

        // Reopen trip sheet with original place
        IsTripSheetOpen = true;
        if (place != null)
        {
            SelectTripPlace(place);
        }
    }

    #endregion

    #region Commands - Region/Place Management

    /// <summary>
    /// Shows the edit menu for a region.
    /// </summary>
    [RelayCommand]
    private async Task EditRegionAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        // Prevent editing the "Unassigned Places" region
        if (region.Name == "Unassigned Places")
        {
            await _toastService.ShowWarningAsync("Cannot edit the Unassigned Places region");
            return;
        }

        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Edit Region",
            "Cancel",
            "Delete",
            "Edit Name", "Edit Notes") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        switch (selected)
        {
            case "Edit Name":
                await EditRegionNameAsync(region);
                break;
            case "Edit Notes":
                await EditRegionNotesAsync(region);
                break;
            case "Delete":
                await DeleteRegionAsync(region);
                break;
        }
    }

    /// <summary>
    /// Deletes a region.
    /// </summary>
    [RelayCommand]
    private async Task DeleteRegionAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        var confirmed = await (_callbacks?.DisplayAlertAsync(
            "Delete Region",
            $"Are you sure you want to delete '{region.Name}'? All places in this region will be moved to Unassigned Places.",
            "Delete",
            "Cancel") ?? Task.FromResult(false));

        if (!confirmed)
            return;

        // Remove from loaded trip
        LoadedTrip.Regions.Remove(region);

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(LoadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.DeleteRegionAsync(region.Id, LoadedTrip.Id);

        await _toastService.ShowSuccessAsync("Region deleted");
    }

    /// <summary>
    /// Moves a region up in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionUpAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        var regions = LoadedTrip.Regions.OrderBy(r => r.SortOrder).ToList();
        var index = regions.IndexOf(region);
        if (index <= 0)
            return;

        // Swap sort orders
        var prevRegion = regions[index - 1];
        (region.SortOrder, prevRegion.SortOrder) = (prevRegion.SortOrder, region.SortOrder);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.UpdateRegionAsync(region.Id, LoadedTrip.Id, displayOrder: region.SortOrder);
        await _tripSyncService.UpdateRegionAsync(prevRegion.Id, LoadedTrip.Id, displayOrder: prevRegion.SortOrder);
    }

    /// <summary>
    /// Moves a region down in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionDownAsync(TripRegion? region)
    {
        if (region == null || LoadedTrip == null)
            return;

        var regions = LoadedTrip.Regions.OrderBy(r => r.SortOrder).ToList();
        var index = regions.IndexOf(region);
        if (index < 0 || index >= regions.Count - 1)
            return;

        // Swap sort orders
        var nextRegion = regions[index + 1];
        (region.SortOrder, nextRegion.SortOrder) = (nextRegion.SortOrder, region.SortOrder);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.UpdateRegionAsync(region.Id, LoadedTrip.Id, displayOrder: region.SortOrder);
        await _tripSyncService.UpdateRegionAsync(nextRegion.Id, LoadedTrip.Id, displayOrder: nextRegion.SortOrder);
    }

    /// <summary>
    /// Moves a place up in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceUpAsync(TripPlace? place)
    {
        if (place == null || LoadedTrip == null)
            return;

        // Find the region containing this place
        var region = LoadedTrip.Regions.FirstOrDefault(r => r.Places.Contains(place));
        if (region == null)
            return;

        var places = region.Places.OrderBy(p => p.SortOrder).ToList();
        var index = places.IndexOf(place);
        if (index <= 0)
            return;

        // Swap sort orders
        var prevPlace = places[index - 1];
        (place.SortOrder, prevPlace.SortOrder) = (prevPlace.SortOrder, place.SortOrder);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.UpdatePlaceAsync(place.Id, LoadedTrip.Id, displayOrder: place.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(prevPlace.Id, LoadedTrip.Id, displayOrder: prevPlace.SortOrder);
    }

    /// <summary>
    /// Moves a place down in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceDownAsync(TripPlace? place)
    {
        if (place == null || LoadedTrip == null)
            return;

        // Find the region containing this place
        var region = LoadedTrip.Regions.FirstOrDefault(r => r.Places.Contains(place));
        if (region == null)
            return;

        var places = region.Places.OrderBy(p => p.SortOrder).ToList();
        var index = places.IndexOf(place);
        if (index < 0 || index >= places.Count - 1)
            return;

        // Swap sort orders
        var nextPlace = places[index + 1];
        (place.SortOrder, nextPlace.SortOrder) = (nextPlace.SortOrder, place.SortOrder);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.UpdatePlaceAsync(place.Id, LoadedTrip.Id, displayOrder: place.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(nextPlace.Id, LoadedTrip.Id, displayOrder: nextPlace.SortOrder);
    }

    #endregion

    #region Commands - Area/Segment Actions

    /// <summary>
    /// Edits the selected area's notes.
    /// </summary>
    [RelayCommand]
    private async Task EditAreaAsync()
    {
        if (SelectedTripArea == null || LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", SelectedTripArea.Id.ToString() },
            { "entityType", "area" }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Edits the selected segment's notes.
    /// </summary>
    [RelayCommand]
    private async Task EditSegmentAsync()
    {
        if (SelectedTripSegment == null || LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", SelectedTripSegment.Id.ToString() },
            { "entityType", "segment" }
        }) ?? Task.CompletedTask);
    }

    #endregion

    #region Commands - Trip Management

    /// <summary>
    /// Shows the add to trip menu.
    /// </summary>
    [RelayCommand]
    private async Task AddToTripAsync()
    {
        if (LoadedTrip == null)
            return;

        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Add to Trip",
            "Cancel",
            null,
            "Add Region", "Add Place at Current Location") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        switch (selected)
        {
            case "Add Region":
                await AddRegionAsync();
                break;
            case "Add Place at Current Location":
                await AddPlaceToCurrentLocationAsync();
                break;
        }
    }

    /// <summary>
    /// Shows the edit menu for the loaded trip.
    /// </summary>
    [RelayCommand]
    private async Task EditLoadedTripAsync()
    {
        if (LoadedTrip == null)
            return;

        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Edit Trip",
            "Cancel",
            null,
            "Edit Name", "Edit Notes") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        switch (selected)
        {
            case "Edit Name":
                await EditLoadedTripNameAsync();
                break;
            case "Edit Notes":
                await EditLoadedTripNotesAsync();
                break;
        }
    }

    /// <summary>
    /// Clears the loaded trip.
    /// </summary>
    [RelayCommand]
    private void ClearLoadedTrip()
    {
        UnloadTrip();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Clears all trip sheet selections and returns to overview.
    /// </summary>
    public void ClearTripSheetSelection()
    {
        SelectedTripPlace = null;
        SelectedPlace = null;
        SelectedTripArea = null;
        SelectedTripSegment = null;
        SelectedTripRegion = null;
        IsShowingTripNotes = false;
        IsShowingAreaNotes = false;
        IsShowingSegmentNotes = false;
        IsShowingRegionNotes = false;

        // Clear map selection
        _callbacks?.ClearPlaceSelection();
    }

    /// <summary>
    /// Unloads the current trip.
    /// </summary>
    public void UnloadTrip()
    {
        LoadedTrip = null;
        SelectedPlace = null;
        ClearTripSheetSelection();
        IsTripSheetOpen = false;

        // Clear trip layers from map
        _callbacks?.UnloadTripFromMap();

        // Resume following user location
        _callbacks?.SetFollowingLocation(true);
    }

    /// <summary>
    /// Sets pending place coordinates from map tap.
    /// Called by MainPage code-behind when map is tapped during coordinate edit mode.
    /// </summary>
    public void SetPendingPlaceCoordinates(double latitude, double longitude)
    {
        PendingPlaceLatitude = latitude;
        PendingPlaceLongitude = longitude;
    }

    /// <summary>
    /// Stores a pending selection to restore after returning from sub-editor.
    /// </summary>
    public void RestoreSelectionFromSubEditor(string? entityType, Guid entityId)
    {
        _pendingSelectionRestore = (entityType, entityId);
    }

    /// <summary>
    /// Processes any pending selection restore and clears the flag.
    /// Called by MainViewModel.OnAppearingAsync.
    /// </summary>
    public async Task ProcessPendingSelectionRestoreAsync()
    {
        if (_pendingSelectionRestore == null || LoadedTrip == null)
            return;

        var (entityType, entityId) = _pendingSelectionRestore.Value;
        _pendingSelectionRestore = null;
        IsNavigatingToSubEditor = false;

        // Refresh the entity from database to get updated notes
        switch (entityType)
        {
            case "place":
                var place = await _databaseService.GetOfflinePlaceByServerIdAsync(entityId);
                if (place != null)
                {
                    // Find and update the place in loaded trip
                    foreach (var region in LoadedTrip.Regions)
                    {
                        var tripPlace = region.Places.FirstOrDefault(p => p.Id == entityId);
                        if (tripPlace != null)
                        {
                            tripPlace.Notes = place.Notes;
                            SelectTripPlace(tripPlace);
                            break;
                        }
                    }
                }
                break;

            case "area":
                var area = await _databaseService.GetOfflineAreaByServerIdAsync(entityId);
                if (area != null)
                {
                    var tripArea = LoadedTrip.AllAreas.FirstOrDefault(a => a.Id == entityId);
                    if (tripArea != null)
                    {
                        tripArea.Notes = area.Notes;
                        SelectTripArea(tripArea);
                    }
                }
                break;

            case "segment":
                var segment = await _databaseService.GetOfflineSegmentByServerIdAsync(entityId);
                if (segment != null)
                {
                    var tripSegment = LoadedTrip.Segments.FirstOrDefault(s => s.Id == entityId);
                    if (tripSegment != null)
                    {
                        tripSegment.Notes = segment.Notes;
                        SelectTripSegment(tripSegment);
                    }
                }
                break;

            case "region":
                var tripRegion = LoadedTrip.Regions.FirstOrDefault(r => r.Id == entityId);
                if (tripRegion != null)
                {
                    ShowRegionNotes(tripRegion);
                }
                break;
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Enters place coordinate editing mode.
    /// </summary>
    private void EnterPlaceCoordinateEditMode(TripPlace place)
    {
        PlaceBeingEditedForCoordinates = place;
        PendingPlaceLatitude = place.Latitude;
        PendingPlaceLongitude = place.Longitude;
        IsPlaceCoordinateEditMode = true;

        // Close trip sheet to expose map
        IsTripSheetOpen = false;
    }

    /// <summary>
    /// Exits place coordinate editing mode.
    /// </summary>
    private void ExitPlaceCoordinateEditMode()
    {
        IsPlaceCoordinateEditMode = false;
        PendingPlaceLatitude = null;
        PendingPlaceLongitude = null;
        PlaceBeingEditedForCoordinates = null;
    }

    /// <summary>
    /// Edits a place's name.
    /// </summary>
    private async Task EditPlaceNameAsync(TripPlace place)
    {
        var newName = await (_callbacks?.DisplayPromptAsync(
            "Edit Place Name",
            "Enter the new name:",
            place.Name) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(newName) || newName == place.Name)
            return;

        // Update locally
        place.Name = newName;

        // Notify UI
        OnPropertyChanged(nameof(SelectedTripPlace));
        OnPropertyChanged(nameof(TripSheetTitle));
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        if (LoadedTrip != null)
        {
            await _tripSyncService.UpdatePlaceAsync(
                place.Id,
                LoadedTrip.Id,
                name: newName);
        }

        await _toastService.ShowSuccessAsync("Place renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for a place.
    /// </summary>
    private async Task EditPlaceNotesAsync(TripPlace place)
    {
        if (LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", place.Id.ToString() },
            { "entityType", "place" }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Navigates to the marker editor for a place.
    /// </summary>
    private async Task EditPlaceMarkerAsync(TripPlace place)
    {
        if (LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("markerEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "placeId", place.Id.ToString() }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Deletes a place.
    /// </summary>
    private async Task DeletePlaceAsync(TripPlace place)
    {
        if (LoadedTrip == null)
            return;

        var confirmed = await (_callbacks?.DisplayAlertAsync(
            "Delete Place",
            $"Are you sure you want to delete '{place.Name}'?",
            "Delete",
            "Cancel") ?? Task.FromResult(false));

        if (!confirmed)
            return;

        // Remove from loaded trip
        foreach (var region in LoadedTrip.Regions)
        {
            if (region.Places.Remove(place))
                break;
        }

        // Clear selection
        ClearTripSheetSelection();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(LoadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.DeletePlaceAsync(place.Id, LoadedTrip.Id);

        await _toastService.ShowSuccessAsync("Place deleted");
    }

    /// <summary>
    /// Edits a region's name.
    /// </summary>
    private async Task EditRegionNameAsync(TripRegion region)
    {
        var newName = await (_callbacks?.DisplayPromptAsync(
            "Edit Region Name",
            "Enter the new name:",
            region.Name) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(newName) || newName == region.Name)
            return;

        // Prevent reserved name
        if (newName == "Unassigned Places")
        {
            await _toastService.ShowWarningAsync("Cannot use reserved name");
            return;
        }

        // Update locally
        region.Name = newName;

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        if (LoadedTrip != null)
        {
            await _tripSyncService.UpdateRegionAsync(
                region.Id,
                LoadedTrip.Id,
                name: newName);
        }

        await _toastService.ShowSuccessAsync("Region renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for a region.
    /// </summary>
    private async Task EditRegionNotesAsync(TripRegion region)
    {
        if (LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", region.Id.ToString() },
            { "entityType", "region" }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Adds a new region to the trip.
    /// </summary>
    private async Task AddRegionAsync()
    {
        if (LoadedTrip == null)
            return;

        var name = await (_callbacks?.DisplayPromptAsync(
            "Add Region",
            "Enter the region name:") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(name))
            return;

        // Create new region with temp ID
        var newRegion = new TripRegion
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = LoadedTrip.Regions.Count
        };

        // Add to loaded trip
        LoadedTrip.Regions.Add(newRegion);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.CreateRegionAsync(
            LoadedTrip.Id,
            name,
            displayOrder: newRegion.SortOrder);

        await _toastService.ShowSuccessAsync("Region added");
    }

    /// <summary>
    /// Adds a new place at the current location.
    /// </summary>
    private async Task AddPlaceToCurrentLocationAsync()
    {
        if (LoadedTrip == null)
            return;

        var currentLocation = _callbacks?.CurrentLocation;
        if (currentLocation == null)
        {
            await _toastService.ShowWarningAsync("Waiting for location...");
            return;
        }

        var name = await (_callbacks?.DisplayPromptAsync(
            "Add Place",
            "Enter the place name:") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(name))
            return;

        // Find or create unassigned region
        var region = LoadedTrip.Regions.FirstOrDefault(r => r.Name == "Unassigned Places")
            ?? LoadedTrip.Regions.FirstOrDefault();

        if (region == null)
        {
            await _toastService.ShowErrorAsync("No region available");
            return;
        }

        // Create new place with temp ID
        var newPlace = new TripPlace
        {
            Id = Guid.NewGuid(),
            Name = name,
            Latitude = currentLocation.Latitude,
            Longitude = currentLocation.Longitude,
            SortOrder = region.Places.Count
        };

        // Add to region
        region.Places.Add(newPlace);

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(LoadedTrip) ?? Task.CompletedTask);

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));

        // Sync to server
        await _tripSyncService.CreatePlaceAsync(
            LoadedTrip.Id,
            region.Id,
            name,
            currentLocation.Latitude,
            currentLocation.Longitude,
            null,
            null,
            null,
            newPlace.SortOrder);

        await _toastService.ShowSuccessAsync("Place added");

        // Select the new place
        SelectTripPlace(newPlace);
    }

    /// <summary>
    /// Edits the loaded trip's name.
    /// </summary>
    private async Task EditLoadedTripNameAsync()
    {
        if (LoadedTrip == null)
            return;

        var newName = await (_callbacks?.DisplayPromptAsync(
            "Edit Trip Name",
            "Enter the new name:",
            LoadedTrip.Name) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(newName) || newName == LoadedTrip.Name)
            return;

        // Update locally
        LoadedTrip.Name = newName;

        // Notify UI
        OnPropertyChanged(nameof(LoadedTrip));
        OnPropertyChanged(nameof(TripSheetTitle));

        // Sync to server
        await _tripSyncService.UpdateTripAsync(
            LoadedTrip.Id,
            newName,
            LoadedTrip.Notes);

        await _toastService.ShowSuccessAsync("Trip renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for the loaded trip.
    /// </summary>
    private async Task EditLoadedTripNotesAsync()
    {
        if (LoadedTrip == null)
            return;

        IsNavigatingToSubEditor = true;
        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", LoadedTrip.Id.ToString() },
            { "entityId", LoadedTrip.Id.ToString() },
            { "entityType", "trip" }
        }) ?? Task.CompletedTask);
    }

    #endregion
}
