using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for trip item editing operations.
/// Handles CRUD operations for places, regions, areas, and segments.
/// Extracted from TripSheetViewModel to reduce complexity.
/// </summary>
public partial class TripItemEditorViewModel : BaseViewModel
{
    #region Constants

    /// <summary>
    /// Name of the default unassigned places region.
    /// </summary>
    private const string UnassignedRegionName = "Unassigned Places";

    #endregion

    #region Fields

    private readonly ITripSyncService _tripSyncService;
    private readonly DatabaseService _databaseService;
    private readonly IWikipediaService _wikipediaService;
    private readonly IToastService _toastService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TripItemEditorViewModel> _logger;

    // Callbacks to parent ViewModel
    private ITripItemEditorCallbacks? _callbacks;

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

    /// <summary>
    /// Gets or sets whether a new place is being created (vs editing existing).
    /// When true, cancellation will remove the place from the trip.
    /// </summary>
    [ObservableProperty]
    private bool _isCreatingNewPlace;

    /// <summary>
    /// Gets or sets the region for the new place being created.
    /// </summary>
    [ObservableProperty]
    private TripRegion? _newPlaceTargetRegion;

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

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripItemEditorViewModel.
    /// </summary>
    public TripItemEditorViewModel(
        ITripSyncService tripSyncService,
        DatabaseService databaseService,
        IWikipediaService wikipediaService,
        IToastService toastService,
        ISettingsService settingsService,
        ILogger<TripItemEditorViewModel> logger)
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
    public void SetCallbacks(ITripItemEditorCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    #endregion

    #region Public Methods

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
    /// Enters place coordinate editing mode for the specified place.
    /// </summary>
    public void EnterPlaceCoordinateEditMode(TripPlace place)
    {
        PlaceBeingEditedForCoordinates = place;
        PendingPlaceLatitude = place.Latitude;
        PendingPlaceLongitude = place.Longitude;
        IsPlaceCoordinateEditMode = true;

        // Close trip sheet to expose map
        _callbacks?.CloseTripSheet();
    }

    /// <summary>
    /// Exits place coordinate editing mode without saving.
    /// </summary>
    public void ExitPlaceCoordinateEditMode()
    {
        IsPlaceCoordinateEditMode = false;
        PendingPlaceLatitude = null;
        PendingPlaceLongitude = null;
        PlaceBeingEditedForCoordinates = null;
    }

    #endregion

    #region Commands - Place Actions

    /// <summary>
    /// Navigates to the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToTripPlaceAsync()
    {
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
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

        await (_callbacks?.StartNavigationToPlaceAsync(selectedPlace.Id.ToString()) ?? Task.CompletedTask);
        _callbacks?.CloseTripSheet();
    }

    /// <summary>
    /// Opens the selected trip place in external maps app.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceInMapsAsync()
    {
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
            return;

        try
        {
            var location = new Location(selectedPlace.Latitude, selectedPlace.Longitude);
            var options = new MapLaunchOptions { Name = selectedPlace.Name };
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
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
            return;

        var coords = $"{selectedPlace.Latitude:F5}, {selectedPlace.Longitude:F5}";
        await Clipboard.Default.SetTextAsync(coords);
        await _toastService.ShowSuccessAsync("Coordinates copied");
    }

    /// <summary>
    /// Shares the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task ShareTripPlaceAsync()
    {
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
            return;

        var mapsUrl = $"https://www.google.com/maps/search/?api=1&query={selectedPlace.Latitude},{selectedPlace.Longitude}";
        var text = $"{selectedPlace.Name}\n{mapsUrl}";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = selectedPlace.Name
        });
    }

    /// <summary>
    /// Opens Wikipedia for the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task OpenTripPlaceWikipediaAsync()
    {
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
            return;

        var found = await _wikipediaService.OpenNearbyArticleAsync(
            selectedPlace.Latitude,
            selectedPlace.Longitude);

        if (!found)
        {
            await _toastService.ShowWarningAsync("No Wikipedia article found nearby");
        }
    }

    /// <summary>
    /// Shows the edit menu for the selected trip place.
    /// </summary>
    [RelayCommand]
    private async Task EditTripPlaceAsync()
    {
        var selectedPlace = _callbacks?.SelectedTripPlace;
        if (selectedPlace == null)
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
                await EditPlaceNameAsync(selectedPlace);
                break;
            case "Edit Notes":
                await EditPlaceNotesAsync(selectedPlace);
                break;
            case "Edit Coordinates":
                EnterPlaceCoordinateEditMode(selectedPlace);
                break;
            case "Edit Marker":
                await EditPlaceMarkerAsync(selectedPlace);
                break;
            case "Delete":
                await DeletePlaceAsync(selectedPlace);
                break;
        }
    }

    #endregion

    #region Commands - Coordinate Editing

    /// <summary>
    /// Saves the edited place coordinates.
    /// For new places, creates the place on the server.
    /// For existing places, updates the coordinates.
    /// </summary>
    [RelayCommand]
    private async Task SavePlaceCoordinatesAsync()
    {
        if (PlaceBeingEditedForCoordinates == null || !HasPendingPlaceCoordinates)
            return;

        var place = PlaceBeingEditedForCoordinates;
        var newLat = PendingPlaceLatitude!.Value;
        var newLon = PendingPlaceLongitude!.Value;
        var loadedTrip = _callbacks?.LoadedTrip;
        var isNewPlace = IsCreatingNewPlace;
        var targetRegion = NewPlaceTargetRegion;

        // Update place coordinates in the loaded trip
        if (loadedTrip != null)
        {
            foreach (var region in loadedTrip.Regions)
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

        // Reset new place flags before exiting edit mode
        IsCreatingNewPlace = false;
        NewPlaceTargetRegion = null;

        // Exit edit mode
        ExitPlaceCoordinateEditMode();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Sync to server
        if (loadedTrip != null)
        {
            if (isNewPlace && targetRegion != null)
            {
                // Create new place on server
                await _tripSyncService.CreatePlaceAsync(
                    loadedTrip.Id,
                    targetRegion.Id,
                    place.Name ?? "New Place",
                    newLat,
                    newLon,
                    displayOrder: place.SortOrder);

                await _toastService.ShowSuccessAsync("Place added");
            }
            else
            {
                // Update existing place coordinates
                await _tripSyncService.UpdatePlaceAsync(
                    place.Id,
                    loadedTrip.Id,
                    latitude: newLat,
                    longitude: newLon);

                await _toastService.ShowSuccessAsync("Coordinates updated");
            }
        }

        // Reopen trip sheet with the place
        _callbacks?.OpenTripSheet();
        _callbacks?.SelectPlace(place);
    }

    /// <summary>
    /// Cancels place coordinate editing.
    /// If creating a new place, removes it from the trip.
    /// </summary>
    [RelayCommand]
    private async Task CancelPlaceCoordinateEditingAsync()
    {
        var place = PlaceBeingEditedForCoordinates;
        var isNewPlace = IsCreatingNewPlace;
        var targetRegion = NewPlaceTargetRegion;
        var loadedTrip = _callbacks?.LoadedTrip;

        // Reset new place flags
        IsCreatingNewPlace = false;
        NewPlaceTargetRegion = null;

        ExitPlaceCoordinateEditMode();

        // If cancelling a new place creation, remove the place from the region
        if (isNewPlace && place != null && targetRegion != null && loadedTrip != null)
        {
            targetRegion.Places.Remove(place);

            // Refresh map layers to remove the marker
            await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

            await _toastService.ShowAsync("Place creation cancelled");

            // Reopen trip sheet without selection
            _callbacks?.OpenTripSheet();
        }
        else
        {
            // Reopen trip sheet with original place (for existing place editing)
            _callbacks?.OpenTripSheet();
            if (place != null)
            {
                _callbacks?.SelectPlace(place);
            }
        }
    }

    #endregion

    #region Commands - Region Management

    /// <summary>
    /// Shows the edit menu for a region.
    /// </summary>
    [RelayCommand]
    private async Task EditRegionAsync(TripRegion? region)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (region == null || loadedTrip == null)
            return;

        // Prevent editing the "Unassigned Places" region
        if (region.Name == UnassignedRegionName)
        {
            await _toastService.ShowWarningAsync($"Cannot edit the {UnassignedRegionName} region");
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
        var loadedTrip = _callbacks?.LoadedTrip;
        if (region == null || loadedTrip == null)
            return;

        var confirmed = await (_callbacks?.DisplayAlertAsync(
            "Delete Region",
            $"Are you sure you want to delete '{region.Name}'? All places in this region will be moved to {UnassignedRegionName}.",
            "Delete",
            "Cancel") ?? Task.FromResult(false));

        if (!confirmed)
            return;

        // Find the actual region instance by ID (parameter may be from SortedRegions which creates new objects)
        var targetRegion = loadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
        if (targetRegion == null)
            return;

        // Remove from loaded trip
        loadedTrip.Regions.Remove(targetRegion);

        // Notify UI to refresh regions list
        _callbacks?.NotifyTripRegionsChanged();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.DeleteRegionAsync(region.Id, loadedTrip.Id);

        await _toastService.ShowSuccessAsync("Region deleted");
    }

    /// <summary>
    /// Moves a region up in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionUpAsync(TripRegion? region)
    {
        try
        {
            _logger.LogInformation("MoveRegionUp: Starting for region {RegionId}", region?.Id);

            var loadedTrip = _callbacks?.LoadedTrip;
            if (region == null || loadedTrip == null)
            {
                _logger.LogWarning("MoveRegionUp: Early exit - region={RegionNull}, loadedTrip={TripNull}",
                    region == null, loadedTrip == null);
                return;
            }

            // Find the actual region instance by ID (parameter may be from SortedRegions which creates new objects)
            var targetRegion = loadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
            if (targetRegion == null)
            {
                _logger.LogWarning("MoveRegionUp: Target region {RegionId} not found in loadedTrip.Regions", region.Id);
                return;
            }

            var regions = loadedTrip.Regions.OrderBy(r => r.SortOrder).ToList();
            var index = regions.IndexOf(targetRegion);
            _logger.LogInformation("MoveRegionUp: Region index={Index}, total regions={Count}", index, regions.Count);

            if (index <= 0)
            {
                _logger.LogInformation("MoveRegionUp: Already at top, index={Index}", index);
                return;
            }

            // Swap sort orders
            var prevRegion = regions[index - 1];
            (targetRegion.SortOrder, prevRegion.SortOrder) = (prevRegion.SortOrder, targetRegion.SortOrder);
            _logger.LogInformation("MoveRegionUp: Swapped sort orders - target={TargetOrder}, prev={PrevOrder}",
                targetRegion.SortOrder, prevRegion.SortOrder);

            // Notify UI to refresh regions list
            _callbacks?.NotifyTripRegionsChanged();

            // Refresh map layers
            await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

            // Sync to server
            await _tripSyncService.UpdateRegionAsync(targetRegion.Id, loadedTrip.Id, displayOrder: targetRegion.SortOrder);
            await _tripSyncService.UpdateRegionAsync(prevRegion.Id, loadedTrip.Id, displayOrder: prevRegion.SortOrder);

            _logger.LogInformation("MoveRegionUp: Completed successfully for region {RegionId}", region.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveRegionUp: Failed for region {RegionId}", region?.Id);
            throw;
        }
    }

    /// <summary>
    /// Moves a region down in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MoveRegionDownAsync(TripRegion? region)
    {
        try
        {
            _logger.LogInformation("MoveRegionDown: Starting for region {RegionId}", region?.Id);

            var loadedTrip = _callbacks?.LoadedTrip;
            if (region == null || loadedTrip == null)
            {
                _logger.LogWarning("MoveRegionDown: Early exit - region={RegionNull}, loadedTrip={TripNull}",
                    region == null, loadedTrip == null);
                return;
            }

            // Find the actual region instance by ID (parameter may be from SortedRegions which creates new objects)
            var targetRegion = loadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
            if (targetRegion == null)
            {
                _logger.LogWarning("MoveRegionDown: Target region {RegionId} not found in loadedTrip.Regions", region.Id);
                return;
            }

            var regions = loadedTrip.Regions.OrderBy(r => r.SortOrder).ToList();
            var index = regions.IndexOf(targetRegion);
            _logger.LogInformation("MoveRegionDown: Region index={Index}, total regions={Count}", index, regions.Count);

            if (index < 0 || index >= regions.Count - 1)
            {
                _logger.LogInformation("MoveRegionDown: Already at bottom, index={Index}", index);
                return;
            }

            // Swap sort orders
            var nextRegion = regions[index + 1];
            (targetRegion.SortOrder, nextRegion.SortOrder) = (nextRegion.SortOrder, targetRegion.SortOrder);
            _logger.LogInformation("MoveRegionDown: Swapped sort orders - target={TargetOrder}, next={NextOrder}",
                targetRegion.SortOrder, nextRegion.SortOrder);

            // Notify UI to refresh regions list
            _callbacks?.NotifyTripRegionsChanged();

            // Refresh map layers
            await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

            // Sync to server
            await _tripSyncService.UpdateRegionAsync(targetRegion.Id, loadedTrip.Id, displayOrder: targetRegion.SortOrder);
            await _tripSyncService.UpdateRegionAsync(nextRegion.Id, loadedTrip.Id, displayOrder: nextRegion.SortOrder);

            _logger.LogInformation("MoveRegionDown: Completed successfully for region {RegionId}", region.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveRegionDown: Failed for region {RegionId}", region?.Id);
            throw;
        }
    }

    #endregion

    #region Commands - Place Movement

    /// <summary>
    /// Moves a place up in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceUpAsync(TripPlace? place)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (place == null || loadedTrip == null)
            return;

        // Find the region containing this place by ID (parameter may be from SortedRegions which creates new objects)
        var region = loadedTrip.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
        if (region == null)
            return;

        // Find the actual place instance
        var targetPlace = region.Places.FirstOrDefault(p => p.Id == place.Id);
        if (targetPlace == null)
            return;

        var places = region.Places.OrderBy(p => p.SortOrder).ToList();
        var index = places.IndexOf(targetPlace);
        if (index <= 0)
            return;

        // Swap sort orders
        var prevPlace = places[index - 1];
        (targetPlace.SortOrder, prevPlace.SortOrder) = (prevPlace.SortOrder, targetPlace.SortOrder);

        // Notify UI to refresh places list
        _callbacks?.NotifyTripPlacesChanged();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.UpdatePlaceAsync(targetPlace.Id, loadedTrip.Id, displayOrder: targetPlace.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(prevPlace.Id, loadedTrip.Id, displayOrder: prevPlace.SortOrder);
    }

    /// <summary>
    /// Moves a place down in the sort order.
    /// </summary>
    [RelayCommand]
    private async Task MovePlaceDownAsync(TripPlace? place)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (place == null || loadedTrip == null)
            return;

        // Find the region containing this place by ID (parameter may be from SortedRegions which creates new objects)
        var region = loadedTrip.Regions.FirstOrDefault(r => r.Places.Any(p => p.Id == place.Id));
        if (region == null)
            return;

        // Find the actual place instance
        var targetPlace = region.Places.FirstOrDefault(p => p.Id == place.Id);
        if (targetPlace == null)
            return;

        var places = region.Places.OrderBy(p => p.SortOrder).ToList();
        var index = places.IndexOf(targetPlace);
        if (index < 0 || index >= places.Count - 1)
            return;

        // Swap sort orders
        var nextPlace = places[index + 1];
        (targetPlace.SortOrder, nextPlace.SortOrder) = (nextPlace.SortOrder, targetPlace.SortOrder);

        // Notify UI to refresh places list
        _callbacks?.NotifyTripPlacesChanged();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.UpdatePlaceAsync(targetPlace.Id, loadedTrip.Id, displayOrder: targetPlace.SortOrder);
        await _tripSyncService.UpdatePlaceAsync(nextPlace.Id, loadedTrip.Id, displayOrder: nextPlace.SortOrder);
    }

    #endregion

    #region Commands - Area/Segment Actions

    /// <summary>
    /// Edits the selected area's notes.
    /// </summary>
    [RelayCommand]
    private async Task EditAreaAsync()
    {
        var selectedArea = _callbacks?.SelectedTripArea;
        var loadedTrip = _callbacks?.LoadedTrip;
        if (selectedArea == null || loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "entityId", selectedArea.Id.ToString() },
            { "entityType", "area" },
            { "entityName", selectedArea.Name ?? "Area" },
            { "notes", selectedArea.Notes ?? string.Empty }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Edits the selected segment's notes.
    /// </summary>
    [RelayCommand]
    private async Task EditSegmentAsync()
    {
        var selectedSegment = _callbacks?.SelectedTripSegment;
        var loadedTrip = _callbacks?.LoadedTrip;
        if (selectedSegment == null || loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "entityId", selectedSegment.Id.ToString() },
            { "entityType", "segment" },
            { "entityName", "Segment" },
            { "notes", selectedSegment.Notes ?? string.Empty }
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
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        var selected = await (_callbacks?.DisplayActionSheetAsync(
            "Add to Trip",
            "Cancel",
            null,
            "Add Region", "Add Place") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selected) || selected == "Cancel")
            return;

        switch (selected)
        {
            case "Add Region":
                await AddRegionAsync();
                break;
            case "Add Place":
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
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
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
    /// Unloads the current trip from the map and clears all trip state.
    /// </summary>
    [RelayCommand]
    private void ClearLoadedTrip()
    {
        _callbacks?.UnloadTrip();
    }

    #endregion

    #region Private Helper Methods - Place Editing

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

        // Notify UI to refresh places list
        _callbacks?.NotifyTripPlacesChanged();

        // Sync to server
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip != null)
        {
            await _tripSyncService.UpdatePlaceAsync(
                place.Id,
                loadedTrip.Id,
                name: newName);
        }

        await _toastService.ShowSuccessAsync("Place renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for a place.
    /// </summary>
    private async Task EditPlaceNotesAsync(TripPlace place)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "entityId", place.Id.ToString() },
            { "entityType", "place" },
            { "entityName", place.Name ?? "Place" },
            { "notes", place.Notes ?? string.Empty }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Navigates to the marker editor for a place.
    /// </summary>
    private async Task EditPlaceMarkerAsync(TripPlace place)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("markerEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "placeId", place.Id.ToString() },
            { "currentColor", place.MarkerColor ?? string.Empty },
            { "currentIcon", place.Icon ?? string.Empty }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Deletes a place.
    /// </summary>
    private async Task DeletePlaceAsync(TripPlace place)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        var confirmed = await (_callbacks?.DisplayAlertAsync(
            "Delete Place",
            $"Are you sure you want to delete '{place.Name}'?",
            "Delete",
            "Cancel") ?? Task.FromResult(false));

        if (!confirmed)
            return;

        // Remove from loaded trip
        foreach (var region in loadedTrip.Regions)
        {
            if (region.Places.Remove(place))
                break;
        }

        // Clear selection
        _callbacks?.ClearSelection();

        // Notify UI to refresh places list
        _callbacks?.NotifyTripPlacesChanged();

        // Refresh map layers
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Sync to server
        await _tripSyncService.DeletePlaceAsync(place.Id, loadedTrip.Id);

        await _toastService.ShowSuccessAsync("Place deleted");
    }

    #endregion

    #region Private Helper Methods - Region Editing

    /// <summary>
    /// Edits a region's name.
    /// </summary>
    private async Task EditRegionNameAsync(TripRegion region)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        // Find the actual region instance by ID (parameter may be from SortedRegions which creates new objects)
        var targetRegion = loadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
        if (targetRegion == null)
            return;

        var newName = await (_callbacks?.DisplayPromptAsync(
            "Edit Region Name",
            "Enter the new name:",
            targetRegion.Name) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(newName) || newName == targetRegion.Name)
            return;

        // Prevent reserved name
        if (newName == UnassignedRegionName)
        {
            await _toastService.ShowWarningAsync("Cannot use reserved name");
            return;
        }

        // Update locally
        targetRegion.Name = newName;

        // Notify UI to refresh regions list
        _callbacks?.NotifyTripRegionsChanged();

        // Sync to server
        await _tripSyncService.UpdateRegionAsync(
            targetRegion.Id,
            loadedTrip.Id,
            name: newName);

        await _toastService.ShowSuccessAsync("Region renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for a region.
    /// </summary>
    private async Task EditRegionNotesAsync(TripRegion region)
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "entityId", region.Id.ToString() },
            { "entityType", "region" },
            { "entityName", region.Name ?? "Region" },
            { "notes", region.Notes ?? string.Empty }
        }) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Adds a new region to the trip.
    /// </summary>
    private async Task AddRegionAsync()
    {
        try
        {
            _logger.LogInformation("AddRegion: Starting");

            var loadedTrip = _callbacks?.LoadedTrip;
            if (loadedTrip == null)
            {
                _logger.LogWarning("AddRegion: Early exit - loadedTrip is null");
                return;
            }

            _logger.LogInformation("AddRegion: Trip {TripId} has {RegionCount} existing regions",
                loadedTrip.Id, loadedTrip.Regions.Count);

            var name = await (_callbacks?.DisplayPromptAsync(
                "Add Region",
                "Enter the region name:") ?? Task.FromResult<string?>(null));

            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogInformation("AddRegion: User cancelled or entered empty name");
                return;
            }

            _logger.LogInformation("AddRegion: Creating region with name '{Name}'", name);

            // Create new region with temp ID
            var newRegion = new TripRegion
            {
                Id = Guid.NewGuid(),
                Name = name,
                SortOrder = loadedTrip.Regions.Count
            };

            // Add to loaded trip
            loadedTrip.Regions.Add(newRegion);
            _logger.LogInformation("AddRegion: Added region {RegionId} to memory, total regions now {Count}",
                newRegion.Id, loadedTrip.Regions.Count);

            // Notify UI to refresh regions list
            _callbacks?.NotifyTripRegionsChanged();

            // Sync to server
            _logger.LogInformation("AddRegion: Syncing to server");
            await _tripSyncService.CreateRegionAsync(
                loadedTrip.Id,
                name,
                displayOrder: newRegion.SortOrder);

            _logger.LogInformation("AddRegion: Completed successfully for region {RegionId}", newRegion.Id);
            await _toastService.ShowSuccessAsync("Region added");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddRegion: Failed");
            throw;
        }
    }

    /// <summary>
    /// Adds a new place at the current location.
    /// Shows region picker, prompts for name, then enters coordinate editing mode.
    /// </summary>
    private async Task AddPlaceToCurrentLocationAsync()
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        var currentLocation = _callbacks?.CurrentLocation;
        if (currentLocation == null)
        {
            await _toastService.ShowWarningAsync("Waiting for location...");
            return;
        }

        // Step 1: Show region picker
        var regions = loadedTrip.Regions.Where(r => !r.IsUnassignedRegion).ToList();
        if (regions.Count == 0)
        {
            await _toastService.ShowErrorAsync("No regions available. Create a region first.");
            return;
        }

        var regionNames = regions.Select(r => r.Name).ToArray();
        var selectedRegionName = await (_callbacks?.DisplayActionSheetAsync(
            "Select Region",
            "Cancel",
            null,
            regionNames) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(selectedRegionName) || selectedRegionName == "Cancel")
            return;

        var region = regions.FirstOrDefault(r => r.Name == selectedRegionName);
        if (region == null)
            return;

        // Step 2: Prompt for place name
        var name = await (_callbacks?.DisplayPromptAsync(
            "Add Place",
            "Enter the place name:") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(name))
            return;

        // Step 3: Create new place with current location as starting point
        var newPlace = new TripPlace
        {
            Id = Guid.NewGuid(),
            Name = name,
            Latitude = currentLocation.Latitude,
            Longitude = currentLocation.Longitude,
            SortOrder = region.Places.Count
        };

        // Add to region locally (not synced yet)
        region.Places.Add(newPlace);

        // Refresh map layers to show the new place marker
        await (_callbacks?.RefreshTripLayersAsync(loadedTrip) ?? Task.CompletedTask);

        // Step 4: Enter coordinate editing mode
        // Mark that we're creating a new place (affects save/cancel behavior)
        IsCreatingNewPlace = true;
        NewPlaceTargetRegion = region;
        EnterPlaceCoordinateEditMode(newPlace);

        await _toastService.ShowAsync("Tap on map to adjust location, then save");
    }

    #endregion

    #region Private Helper Methods - Trip Editing

    /// <summary>
    /// Edits the loaded trip's name.
    /// </summary>
    private async Task EditLoadedTripNameAsync()
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        var newName = await (_callbacks?.DisplayPromptAsync(
            "Edit Trip Name",
            "Enter the new name:",
            loadedTrip.Name) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrWhiteSpace(newName) || newName == loadedTrip.Name)
            return;

        // Update locally
        loadedTrip.Name = newName;

        // Notify UI to refresh header
        _callbacks?.NotifyTripHeaderChanged();

        // Sync to server
        await _tripSyncService.UpdateTripAsync(
            loadedTrip.Id,
            newName,
            loadedTrip.Notes);

        await _toastService.ShowSuccessAsync("Trip renamed");
    }

    /// <summary>
    /// Navigates to the notes editor for the loaded trip.
    /// </summary>
    private async Task EditLoadedTripNotesAsync()
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
            return;

        await (_callbacks?.NavigateToPageAsync("notesEditor", new Dictionary<string, object>
        {
            { "tripId", loadedTrip.Id.ToString() },
            { "entityId", loadedTrip.Id.ToString() },
            { "entityType", "trip" },
            { "entityName", loadedTrip.Name ?? "Trip" },
            { "notes", loadedTrip.Notes ?? string.Empty }
        }) ?? Task.CompletedTask);
    }

    #endregion
}
