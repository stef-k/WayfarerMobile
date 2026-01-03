using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for trip sheet coordination.
/// Manages trip sheet state, selection, and display.
/// Editing operations are delegated to TripItemEditorViewModel.
/// Extracted from MainViewModel to handle trip sheet-specific concerns.
/// </summary>
public partial class TripSheetViewModel : BaseViewModel, ITripItemEditorCallbacks
{
    #region Constants

    /// <summary>
    /// Name of the default unassigned places region.
    /// </summary>
    private const string UnassignedRegionName = "Unassigned Places";

    #endregion

    #region Fields

    private readonly IPlaceRepository _placeRepository;
    private readonly ISegmentRepository _segmentRepository;
    private readonly IAreaRepository _areaRepository;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TripSheetViewModel> _logger;

    // Callbacks to parent ViewModel
    private ITripSheetCallbacks? _callbacks;

    // Cached search results to avoid recomputation on every property access
    private List<TripPlace> _cachedSearchResults = new();

    #endregion

    #region Child ViewModels

    /// <summary>
    /// Gets the trip item editor ViewModel for editing operations.
    /// </summary>
    public TripItemEditorViewModel Editor { get; }

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
        TripItemEditorViewModel editor,
        IPlaceRepository placeRepository,
        ISegmentRepository segmentRepository,
        IAreaRepository areaRepository,
        ISettingsService settingsService,
        ILogger<TripSheetViewModel> logger)
    {
        Editor = editor;
        _placeRepository = placeRepository;
        _segmentRepository = segmentRepository;
        _areaRepository = areaRepository;
        _settingsService = settingsService;
        _logger = logger;

        // Wire up child ViewModel callbacks
        Editor.SetCallbacks(this);
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
        Editor.SetPendingPlaceCoordinates(latitude, longitude);
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
                var place = await _placeRepository.GetOfflinePlaceByServerIdAsync(entityId);
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
                var area = await _areaRepository.GetOfflineAreaByServerIdAsync(entityId);
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
                var segment = await _segmentRepository.GetOfflineSegmentByServerIdAsync(entityId);
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

    #region ITripItemEditorCallbacks Implementation

    /// <inheritdoc/>
    TripDetails? ITripItemEditorCallbacks.LoadedTrip => LoadedTrip;

    /// <inheritdoc/>
    TripPlace? ITripItemEditorCallbacks.SelectedTripPlace => SelectedTripPlace;

    /// <inheritdoc/>
    TripArea? ITripItemEditorCallbacks.SelectedTripArea => SelectedTripArea;

    /// <inheritdoc/>
    TripSegment? ITripItemEditorCallbacks.SelectedTripSegment => SelectedTripSegment;

    /// <inheritdoc/>
    TripRegion? ITripItemEditorCallbacks.SelectedTripRegion => SelectedTripRegion;

    /// <inheritdoc/>
    LocationData? ITripItemEditorCallbacks.CurrentLocation => _callbacks?.CurrentLocation;

    /// <inheritdoc/>
    bool ITripItemEditorCallbacks.IsNavigating => _callbacks?.IsNavigating ?? false;

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.SelectPlace(TripPlace? place) => SelectTripPlace(place);

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.ClearSelection() => UnloadTrip();

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.OpenTripSheet() => IsTripSheetOpen = true;

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.CloseTripSheet() => IsTripSheetOpen = false;

    /// <inheritdoc/>
    Task ITripItemEditorCallbacks.RefreshTripLayersAsync(TripDetails? trip) =>
        _callbacks?.RefreshTripLayersAsync(trip) ?? Task.CompletedTask;

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.CenterOnLocation(double latitude, double longitude, int? zoomLevel) =>
        _callbacks?.CenterOnLocation(latitude, longitude, zoomLevel);

    /// <inheritdoc/>
    void ITripItemEditorCallbacks.UpdatePlaceSelection(TripPlace? place) =>
        _callbacks?.UpdatePlaceSelection(place);

    /// <inheritdoc/>
    Task ITripItemEditorCallbacks.StartNavigationToPlaceAsync(string placeId) =>
        _callbacks?.StartNavigationToPlaceAsync(placeId) ?? Task.CompletedTask;

    /// <inheritdoc/>
    Task<string?> ITripItemEditorCallbacks.DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons) =>
        _callbacks?.DisplayActionSheetAsync(title, cancel, destruction, buttons) ?? Task.FromResult<string?>(null);

    /// <inheritdoc/>
    Task<string?> ITripItemEditorCallbacks.DisplayPromptAsync(string title, string message, string? initialValue) =>
        _callbacks?.DisplayPromptAsync(title, message, initialValue) ?? Task.FromResult<string?>(null);

    /// <inheritdoc/>
    Task<bool> ITripItemEditorCallbacks.DisplayAlertAsync(string title, string message, string accept, string cancel) =>
        _callbacks?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false);

    /// <inheritdoc/>
    Task ITripItemEditorCallbacks.NavigateToPageAsync(string route, IDictionary<string, object>? parameters)
    {
        IsNavigatingToSubEditor = true;
        return _callbacks?.NavigateToPageAsync(route, parameters) ?? Task.CompletedTask;
    }

    #endregion
}
