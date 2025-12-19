using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui;
using Mapsui.Layers;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;
using WayfarerMobile.Services.TileCache;
using WayfarerMobile.Shared.Collections;
using WayfarerMobile.Shared.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for managing trips - viewing, downloading, and navigating.
/// </summary>
public partial class TripsViewModel : BaseViewModel
{
    private readonly IApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly IMapBuilder _mapBuilder;
    private readonly ITripLayerService _tripLayerService;
    private readonly TripDownloadService _downloadService;
    private readonly NavigationService _navigationService;
    private readonly TripNavigationService _tripNavigationService;
    private readonly ILocationBridge _locationBridge;
    private readonly ITripSyncService _tripSyncService;
    private readonly IToastService _toastService;
    private readonly IDownloadNotificationService _downloadNotificationService;
    private readonly CacheStatusService _cacheStatusService;
    private IReadOnlyList<SegmentDisplayItem>? _cachedSegmentDisplayItems;

    // Map state - TripsViewModel owns its Map instance
    private Mapsui.Map? _map;
    private WritableLayer? _tripPlacesLayer;
    private WritableLayer? _tripSegmentsLayer;
    private WritableLayer? _navigationRouteLayer;
    private WritableLayer? _navigationRouteCompletedLayer;

    #region Observable Properties

    /// <summary>
    /// Gets or sets the collection of available trips from the server.
    /// </summary>
    [ObservableProperty]
    private ObservableRangeCollection<TripSummary> _availableTrips = new();

    /// <summary>
    /// Gets or sets the selected trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrip))]
    private TripSummary? _selectedTrip;

    /// <summary>
    /// Gets or sets the selected trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaces))]
    [NotifyPropertyChangedFor(nameof(HasSegments))]
    [NotifyPropertyChangedFor(nameof(SegmentDisplayItems))]
    [NotifyPropertyChangedFor(nameof(PlacesCount))]
    [NotifyPropertyChangedFor(nameof(SegmentsCount))]
    [NotifyPropertyChangedFor(nameof(RegionsCount))]
    [NotifyPropertyChangedFor(nameof(HasTripNotes))]
    [NotifyPropertyChangedFor(nameof(TripNotesPreview))]
    [NotifyPropertyChangedFor(nameof(TripNotesPlainText))]
    private TripDetails? _selectedTripDetails;

    /// <summary>
    /// Gets or sets whether trips are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingTrips;

    /// <summary>
    /// Gets or sets whether trip details are being loaded.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingDetails;

    /// <summary>
    /// Gets or sets whether a download is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// Gets or sets the download progress (0-100).
    /// </summary>
    [ObservableProperty]
    private int _downloadProgress;

    /// <summary>
    /// Gets or sets the download status message.
    /// </summary>
    [ObservableProperty]
    private string? _downloadStatusMessage;

    /// <summary>
    /// Gets or sets whether the list is empty.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Gets or sets whether showing trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowingList))]
    private bool _showingDetails;

    /// <summary>
    /// Gets or sets whether navigation is active.
    /// </summary>
    [ObservableProperty]
    private bool _isNavigating;

    /// <summary>
    /// Gets or sets the current navigation state.
    /// </summary>
    [ObservableProperty]
    private NavigationState? _navigationState;

    /// <summary>
    /// Gets or sets the destination place name for display.
    /// </summary>
    [ObservableProperty]
    private string? _navigationDestination;

    /// <summary>
    /// Gets or sets whether the sidebar drawer is open.
    /// </summary>
    [ObservableProperty]
    private bool _isSidebarOpen;

    /// <summary>
    /// Gets or sets the selected place in the sidebar.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPlace))]
    private TripPlace? _selectedPlace;

    /// <summary>
    /// Gets or sets the place being shown in the details sheet.
    /// </summary>
    [ObservableProperty]
    private TripPlace? _selectedPlaceForDetails;

    /// <summary>
    /// Gets or sets whether the place details sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isPlaceDetailsOpen;

    /// <summary>
    /// Gets or sets whether the selected trip is downloaded for offline use.
    /// </summary>
    [ObservableProperty]
    private bool _isSelectedTripDownloaded;

    /// <summary>
    /// Gets or sets whether the trip info panel is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isTripInfoExpanded = true;

    /// <summary>
    /// Gets or sets whether the trip notes section is expanded.
    /// </summary>
    [ObservableProperty]
    private bool _isTripNotesExpanded;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether a trip is selected.
    /// </summary>
    public bool HasSelectedTrip => SelectedTrip != null;

    /// <summary>
    /// Gets whether showing trip list.
    /// </summary>
    public bool ShowingList => !ShowingDetails;

    /// <summary>
    /// Gets whether the selected trip has places.
    /// </summary>
    public bool HasPlaces => SelectedTripDetails?.AllPlaces.Any() ?? false;

    /// <summary>
    /// Gets whether the selected trip has segments.
    /// </summary>
    public bool HasSegments => SelectedTripDetails?.Segments.Any() ?? false;

    /// <summary>
    /// Gets the count of places in the selected trip.
    /// </summary>
    public int PlacesCount => SelectedTripDetails?.AllPlaces?.Count ?? 0;

    /// <summary>
    /// Gets the count of segments in the selected trip.
    /// </summary>
    public int SegmentsCount => SelectedTripDetails?.Segments?.Count ?? 0;

    /// <summary>
    /// Gets the count of regions in the selected trip.
    /// </summary>
    public int RegionsCount => SelectedTripDetails?.Regions?.Count ?? 0;

    /// <summary>
    /// Gets whether the selected trip has notes.
    /// </summary>
    public bool HasTripNotes => !string.IsNullOrWhiteSpace(SelectedTripDetails?.Notes);

    /// <summary>
    /// Gets a preview of the trip notes (first 100 characters).
    /// </summary>
    public string TripNotesPreview => GetNotesPreview();

    /// <summary>
    /// Gets the trip notes with HTML stripped.
    /// </summary>
    public string TripNotesPlainText => StripHtml(SelectedTripDetails?.Notes ?? string.Empty);

    /// <summary>
    /// Gets the segment display items for the sidebar, using cached value when available.
    /// </summary>
    public IEnumerable<SegmentDisplayItem> SegmentDisplayItems => _cachedSegmentDisplayItems ??= BuildSegmentDisplayItems();

    /// <summary>
    /// Gets whether API is configured.
    /// </summary>
    public bool IsConfigured => _settingsService.IsConfigured;

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Mapsui.Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets whether a place is selected.
    /// </summary>
    public bool HasSelectedPlace => SelectedPlace != null;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TripsViewModel.
    /// </summary>
    public TripsViewModel(
        IApiClient apiClient,
        ISettingsService settingsService,
        IMapBuilder mapBuilder,
        ITripLayerService tripLayerService,
        TripDownloadService downloadService,
        NavigationService navigationService,
        TripNavigationService tripNavigationService,
        ILocationBridge locationBridge,
        ITripSyncService tripSyncService,
        IToastService toastService,
        IDownloadNotificationService downloadNotificationService,
        CacheStatusService cacheStatusService)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _mapBuilder = mapBuilder;
        _tripLayerService = tripLayerService;
        _downloadService = downloadService;
        _navigationService = navigationService;
        _downloadNotificationService = downloadNotificationService;
        _tripNavigationService = tripNavigationService;
        _locationBridge = locationBridge;
        _tripSyncService = tripSyncService;
        _toastService = toastService;
        _cacheStatusService = cacheStatusService;
        Title = "Trips";

        // Subscribe to download progress
        _downloadService.ProgressChanged += OnDownloadProgressChanged;

        // Subscribe to navigation state changes
        _navigationService.StateChanged += OnNavigationStateChanged;

        // Subscribe to trip navigation state changes for route updates
        _tripNavigationService.StateChanged += OnTripNavigationStateChanged;

        // Subscribe to sync events
        _tripSyncService.SyncCompleted += OnSyncCompleted;
        _tripSyncService.SyncQueued += OnSyncQueued;
        _tripSyncService.SyncRejected += OnSyncRejected;
    }

    #endregion

    #region Property Change Handlers

    /// <summary>
    /// Called when SelectedTrip changes - updates download status.
    /// </summary>
    partial void OnSelectedTripChanged(TripSummary? value)
    {
        _ = UpdateDownloadStatusAsync(value);
    }

    /// <summary>
    /// Called when SelectedTripDetails changes - invalidates the segment display items cache.
    /// </summary>
    partial void OnSelectedTripDetailsChanged(TripDetails? value)
    {
        _cachedSegmentDisplayItems = null;
    }

    /// <summary>
    /// Updates the download status for a trip asynchronously.
    /// </summary>
    private async Task UpdateDownloadStatusAsync(TripSummary? trip)
    {
        if (trip == null)
        {
            IsSelectedTripDownloaded = false;
            return;
        }

        try
        {
            IsSelectedTripDownloaded = await _downloadService.IsTripDownloadedAsync(trip.Id);
        }
        catch
        {
            IsSelectedTripDownloaded = false;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Loads trips from the server.
    /// </summary>
    [RelayCommand]
    private async Task LoadTripsAsync()
    {
        if (IsLoadingTrips)
            return;

        try
        {
            IsLoadingTrips = true;
            ErrorMessage = null;

            var trips = await _apiClient.GetTripsAsync();

            AvailableTrips.ReplaceRange(trips.OrderByDescending(t => t.UpdatedAt));

            IsEmpty = !AvailableTrips.Any();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load trips: {ex.Message}";
            IsEmpty = true;
        }
        finally
        {
            IsLoadingTrips = false;
        }
    }

    /// <summary>
    /// Selects a trip to view details.
    /// </summary>
    [RelayCommand]
    private async Task SelectTripAsync(TripSummary? trip)
    {
        if (trip == null)
            return;

        SelectedTrip = trip;
        // Show details view immediately so loading overlay is visible
        ShowingDetails = true;
        await LoadTripDetailsAsync(trip.Id);
    }

    /// <summary>
    /// Loads trip details.
    /// </summary>
    private async Task LoadTripDetailsAsync(Guid tripId)
    {
        if (IsLoadingDetails)
            return;

        try
        {
            IsLoadingDetails = true;
            ErrorMessage = null;

            SelectedTripDetails = await _apiClient.GetTripDetailsAsync(tripId);

            if (SelectedTripDetails != null)
            {
                // Display trip places on map with custom icons
                await DisplayTripOnMapAsync(SelectedTripDetails);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load trip details: {ex.Message}";
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    /// <summary>
    /// Goes back to trip list.
    /// </summary>
    [RelayCommand]
    private void BackToList()
    {
        ShowingDetails = false;
        SelectedTrip = null;
        SelectedTripDetails = null;
        SelectedPlace = null;
        IsSidebarOpen = false;
        ClearTripLayers();
    }

    /// <summary>
    /// Toggles the sidebar drawer open/closed.
    /// </summary>
    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    /// <summary>
    /// Opens the sidebar drawer.
    /// </summary>
    [RelayCommand]
    private void OpenSidebar()
    {
        IsSidebarOpen = true;
    }

    /// <summary>
    /// Closes the sidebar drawer.
    /// </summary>
    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

    /// <summary>
    /// Toggles the trip info panel expansion state.
    /// </summary>
    [RelayCommand]
    private void ToggleTripInfo()
    {
        IsTripInfoExpanded = !IsTripInfoExpanded;
    }

    /// <summary>
    /// Toggles the trip notes section expansion state.
    /// </summary>
    [RelayCommand]
    private void ToggleTripNotes()
    {
        IsTripNotesExpanded = !IsTripNotesExpanded;
    }

    /// <summary>
    /// Selects a place from the sidebar and centers the map on it.
    /// </summary>
    [RelayCommand]
    private void SelectPlace(TripPlace? place)
    {
        if (place == null)
            return;

        SelectedPlace = place;
        CenterOnPlace(place);
    }

    /// <summary>
    /// Shows the place details bottom sheet for the specified place.
    /// </summary>
    [RelayCommand]
    private void ShowPlaceDetails(TripPlace? place)
    {
        if (place == null)
            return;

        SelectedPlaceForDetails = place;
        IsPlaceDetailsOpen = true;

        // Also center the map on the place
        CenterOnPlace(place);

        // Close sidebar to show the bottom sheet
        IsSidebarOpen = false;
    }

    /// <summary>
    /// Closes the place details bottom sheet.
    /// </summary>
    public void ClosePlaceDetails()
    {
        IsPlaceDetailsOpen = false;
        SelectedPlaceForDetails = null;
    }

    /// <summary>
    /// Centers map on a specific place.
    /// </summary>
    [RelayCommand]
    private void CenterOnPlace(TripPlace? place)
    {
        if (place == null || _map == null)
            return;

        _mapBuilder.CenterOnLocation(_map, place.Latitude, place.Longitude);
    }

    /// <summary>
    /// Starts navigation to a place.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToPlaceAsync(TripPlace? place)
    {
        if (place == null)
            return;

        try
        {
            // Stop any existing navigation
            if (_navigationService.IsNavigating)
            {
                await _navigationService.StopNavigationAsync();
            }
            ClearNavigationRoute();

            // Load trip for routing if we have trip details
            if (SelectedTripDetails != null && !_tripNavigationService.IsTripLoaded)
            {
                _tripNavigationService.LoadTrip(SelectedTripDetails);
            }

            // Get current location for route calculation
            var currentLocation = _locationBridge.LastLocation;
            if (currentLocation != null && _tripNavigationService.IsTripLoaded)
            {
                // Calculate route using trip navigation graph with OSRM fallback
                // Priority: 1. User segments, 2. OSRM fetch, 3. Direct route
                var route = await _tripNavigationService.CalculateRouteToPlaceAsync(
                    currentLocation.Latitude,
                    currentLocation.Longitude,
                    place.Id.ToString(),
                    fetchFromOsrm: true);

                if (route != null)
                {
                    // Show route on map
                    ShowNavigationRoute(route);
                    ZoomToNavigationRoute();

                    // Subscribe to location updates for route progress
                    _locationBridge.LocationReceived += OnLocationReceivedForNavigation;
                }
            }

            // Start basic navigation for distance/bearing updates
            NavigationDestination = place.Name;
            await _navigationService.StartNavigationAsync(place);
            IsNavigating = true;

            // Center map on destination
            CenterOnPlace(place);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start navigation: {ex.Message}";
            IsNavigating = false;
        }
    }

    /// <summary>
    /// Handles location updates during navigation to update route progress.
    /// </summary>
    private void OnLocationReceivedForNavigation(object? sender, LocationData location)
    {
        if (!IsNavigating)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update route progress display on map
            UpdateNavigationRouteProgress(location.Latitude, location.Longitude);

            // Update trip navigation state
            if (_tripNavigationService.IsTripLoaded)
            {
                _tripNavigationService.UpdateLocation(location.Latitude, location.Longitude);
            }
        });
    }

    /// <summary>
    /// Stops the current navigation.
    /// </summary>
    [RelayCommand]
    private async Task StopNavigationAsync()
    {
        // Unsubscribe from location updates for route progress
        _locationBridge.LocationReceived -= OnLocationReceivedForNavigation;

        if (_navigationService.IsNavigating)
        {
            await _navigationService.StopNavigationAsync();
        }

        // Clear route from map
        ClearNavigationRoute();

        // Unload trip navigation
        _tripNavigationService.UnloadTrip();

        IsNavigating = false;
        NavigationDestination = null;
        NavigationState = null;
    }

    /// <summary>
    /// Downloads the selected trip for offline access.
    /// </summary>
    [RelayCommand]
    private async Task DownloadTripAsync()
    {
        if (SelectedTrip == null || IsDownloading)
            return;

        // Show download guidance and get confirmation
        // Estimate based on bounding box area if available, otherwise use conservative default
        var estimatedSizeMb = EstimateDownloadSize(SelectedTrip);
        var confirmed = await _downloadNotificationService.ShowDownloadGuidanceAsync(SelectedTrip.Name, estimatedSizeMb);
        if (!confirmed)
            return;

        // Check storage space
        var hasStorage = await _downloadNotificationService.CheckStorageBeforeDownloadAsync(estimatedSizeMb);
        if (!hasStorage)
            return;

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusMessage = "Starting download...";

            var result = await _downloadService.DownloadTripAsync(SelectedTrip);

            if (result != null)
            {
                IsSelectedTripDownloaded = true;
                DownloadStatusMessage = "Download complete!";

                // Show completion notification
                await _downloadNotificationService.NotifyDownloadCompletedAsync(
                    SelectedTrip.Name,
                    result.TotalSizeBytes / (1024.0 * 1024.0));

                // Refresh cache status to reflect newly downloaded tiles
                await _cacheStatusService.ForceRefreshAsync();
            }
            else
            {
                DownloadStatusMessage = "Download failed";
                await _downloadNotificationService.HandleUnexpectedInterruptionAsync(
                    SelectedTrip.Name,
                    DownloadInterruptionReason.DownloadFailed);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Download failed: {ex.Message}";
            DownloadStatusMessage = "Download failed";

            var reason = ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                ? DownloadInterruptionReason.NetworkLost
                : ex.Message.Contains("storage", StringComparison.OrdinalIgnoreCase)
                    ? DownloadInterruptionReason.StorageLow
                    : DownloadInterruptionReason.Unknown;

            await _downloadNotificationService.HandleUnexpectedInterruptionAsync(SelectedTrip.Name, reason);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    /// <summary>
    /// Navigates to the public trips browser.
    /// </summary>
    [RelayCommand]
    private async Task BrowsePublicTripsAsync()
    {
        await Shell.Current.GoToAsync("publictrips");
    }

    /// <summary>
    /// Deletes the selected trip from offline storage.
    /// </summary>
    [RelayCommand]
    private async Task DeleteDownloadedTripAsync()
    {
        if (SelectedTrip == null)
            return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null)
            return;

        var confirm = await page.DisplayAlertAsync(
            "Delete Offline Trip",
            $"Remove '{SelectedTrip.Name}' from offline storage?",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        try
        {
            await _downloadService.DeleteTripAsync(SelectedTrip.Id);
            IsSelectedTripDownloaded = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Estimates the download size for a trip based on its bounding box.
    /// </summary>
    /// <param name="trip">The trip to estimate.</param>
    /// <returns>Estimated size in megabytes.</returns>
    private static double EstimateDownloadSize(TripSummary trip)
    {
        // If we have a bounding box, estimate based on area
        if (trip.BoundingBox != null)
        {
            var bbox = trip.BoundingBox;
            var latSpan = Math.Abs(bbox.North - bbox.South);
            var lonSpan = Math.Abs(bbox.East - bbox.West);

            // Rough estimate: degrees at equator is ~111km
            // Area in square degrees * ~2MB per square degree for tiles at typical zoom levels
            var areaSqDegrees = latSpan * lonSpan;
            var estimatedMb = areaSqDegrees * 50; // ~50MB per square degree covers multiple zoom levels

            // Clamp to reasonable range (10MB - 500MB)
            return Math.Clamp(estimatedMb, 10, 500);
        }

        // Default estimate based on typical trip size
        return 50; // Conservative 50MB default
    }

    /// <summary>
    /// Displays trip places and segments on the map.
    /// </summary>
    private async Task DisplayTripOnMapAsync(TripDetails trip)
    {
        EnsureMapInitialized();

        // Display segments first (so they appear below place markers)
        if (trip.Segments.Any() && _tripSegmentsLayer != null)
        {
            _tripLayerService.UpdateTripSegments(_tripSegmentsLayer, trip.Segments);
        }

        // Use the dedicated trip places layer with custom icons
        if (_tripPlacesLayer != null)
        {
            await _tripLayerService.UpdateTripPlacesAsync(_tripPlacesLayer, trip.AllPlaces);
        }

        // Center on trip bounding box or first place
        if (trip.CenterLat.HasValue && trip.CenterLon.HasValue)
        {
            _mapBuilder.CenterOnLocation(_map!, trip.CenterLat.Value, trip.CenterLon.Value);
        }
        else if (trip.AllPlaces.Any())
        {
            var firstPlace = trip.AllPlaces.First();
            _mapBuilder.CenterOnLocation(_map!, firstPlace.Latitude, firstPlace.Longitude);
        }
    }

    /// <summary>
    /// Handles download progress updates.
    /// </summary>
    private void OnDownloadProgressChanged(object? sender, DownloadProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DownloadProgress = e.ProgressPercent;
            DownloadStatusMessage = e.Message;
        });
    }

    /// <summary>
    /// Handles navigation state changes.
    /// </summary>
    private void OnNavigationStateChanged(object? sender, NavigationState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NavigationState = state;
            IsNavigating = state.IsActive;

            // If arrived at destination, clean up
            if (state.HasArrived)
            {
                _locationBridge.LocationReceived -= OnLocationReceivedForNavigation;
                ClearNavigationRoute();
                IsNavigating = false;
                NavigationDestination = null;
            }
        });
    }

    /// <summary>
    /// Handles trip navigation state changes (rerouting, arrival, etc.).
    /// </summary>
    private void OnTripNavigationStateChanged(object? sender, TripNavigationState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Handle arrival
            if (state.Status == NavigationStatus.Arrived)
            {
                _locationBridge.LocationReceived -= OnLocationReceivedForNavigation;
                ClearNavigationRoute();
            }
            // Handle rerouting - redraw the route
            else if (state.Status == NavigationStatus.OnRoute)
            {
                var route = _tripNavigationService.ActiveRoute;
                if (route != null)
                {
                    ShowNavigationRoute(route);
                }
            }
        });
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Refresh download status for selected trip
        if (SelectedTrip != null)
        {
            await UpdateDownloadStatusAsync(SelectedTrip);
        }

        if (!AvailableTrips.Any())
        {
            await LoadTripsAsync();
        }
        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override async Task OnDisappearingAsync()
    {
        // Stop navigation when leaving
        if (_navigationService.IsNavigating)
        {
            await StopNavigationAsync();
        }

        // Clear map markers and segments when leaving
        if (!ShowingDetails)
        {
            ClearTripLayers();
        }
        await base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from download progress events
        _downloadService.ProgressChanged -= OnDownloadProgressChanged;

        // Unsubscribe from navigation state change events
        _navigationService.StateChanged -= OnNavigationStateChanged;

        // Unsubscribe from trip navigation state change events
        _tripNavigationService.StateChanged -= OnTripNavigationStateChanged;

        // Unsubscribe from sync events
        _tripSyncService.SyncCompleted -= OnSyncCompleted;
        _tripSyncService.SyncQueued -= OnSyncQueued;
        _tripSyncService.SyncRejected -= OnSyncRejected;

        // Unsubscribe from location updates if still subscribed
        _locationBridge.LocationReceived -= OnLocationReceivedForNavigation;

        // Dispose map to release native resources
        _map?.Dispose();
        _map = null;

        base.Cleanup();
    }

    #endregion

    #region Map Management

    /// <summary>
    /// Creates the Map instance with trip layers.
    /// Layer order (bottom to top): TileLayer, TripSegments, NavigationRouteCompleted, NavigationRoute, TripPlaces.
    /// </summary>
    private Mapsui.Map CreateMap()
    {
        // Create layers
        _tripSegmentsLayer = _mapBuilder.CreateLayer(_tripLayerService.TripSegmentsLayerName);
        _navigationRouteCompletedLayer = _mapBuilder.CreateLayer("NavigationRouteCompleted");
        _navigationRouteLayer = _mapBuilder.CreateLayer("NavigationRoute");
        _tripPlacesLayer = _mapBuilder.CreateLayer(_tripLayerService.TripPlacesLayerName);

        // Create map with layers in correct z-order
        var map = _mapBuilder.CreateMap(
            _tripSegmentsLayer,
            _navigationRouteCompletedLayer,
            _navigationRouteLayer,
            _tripPlacesLayer);

        // Set default zoom
        SetDefaultZoom(map);

        return map;
    }

    /// <summary>
    /// Sets a sensible default zoom level.
    /// </summary>
    private static void SetDefaultZoom(Mapsui.Map map)
    {
        map.Navigator.ZoomTo(2);
    }

    /// <summary>
    /// Ensures the map is initialized before use.
    /// </summary>
    private void EnsureMapInitialized()
    {
        _ = Map; // Access property to trigger lazy initialization
    }

    /// <summary>
    /// Clears all trip layers (places and segments).
    /// </summary>
    private void ClearTripLayers()
    {
        if (_tripPlacesLayer != null)
            _tripLayerService.ClearTripPlaces(_tripPlacesLayer);
        if (_tripSegmentsLayer != null)
            _tripLayerService.ClearTripSegments(_tripSegmentsLayer);
    }

    /// <summary>
    /// Clears all navigation route layers.
    /// </summary>
    private void ClearNavigationRoute()
    {
        _navigationRouteLayer?.Clear();
        _navigationRouteLayer?.DataHasChanged();
        _navigationRouteCompletedLayer?.Clear();
        _navigationRouteCompletedLayer?.DataHasChanged();
    }

    /// <summary>
    /// Shows a navigation route on the map.
    /// </summary>
    private void ShowNavigationRoute(NavigationRoute route)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null) return;
        _mapBuilder.UpdateNavigationRoute(_navigationRouteLayer, _navigationRouteCompletedLayer, route);
    }

    /// <summary>
    /// Zooms the map to show the entire navigation route.
    /// </summary>
    private void ZoomToNavigationRoute()
    {
        if (_navigationRouteLayer == null || _map == null) return;

        var features = _navigationRouteLayer.GetFeatures().ToList();
        if (!features.Any()) return;

        var extent = features.Select(f => f.Extent).Where(e => e != null).Aggregate((a, b) => a!.Join(b!));
        if (extent != null)
        {
            _map.Navigator.ZoomToBox(extent.Grow(extent.Width * 0.2, extent.Height * 0.2));
        }
    }

    /// <summary>
    /// Updates the navigation route progress display.
    /// </summary>
    private void UpdateNavigationRouteProgress(double latitude, double longitude)
    {
        if (_navigationRouteLayer == null || _navigationRouteCompletedLayer == null) return;
        var route = _tripNavigationService.ActiveRoute;
        if (route == null) return;
        _mapBuilder.UpdateNavigationRouteProgress(_navigationRouteLayer, _navigationRouteCompletedLayer, route, latitude, longitude);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gets a preview of the trip notes (first 100 characters).
    /// </summary>
    private string GetNotesPreview()
    {
        var plainText = StripHtml(SelectedTripDetails?.Notes ?? string.Empty);
        return plainText.Length > 100 ? plainText[..100] + "..." : plainText;
    }

    /// <summary>
    /// Strips HTML tags from a string.
    /// </summary>
    /// <param name="html">The HTML string to process.</param>
    /// <returns>Plain text with HTML removed.</returns>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        return Regex.Replace(html, "<[^>]*>", "").Trim();
    }

    /// <summary>
    /// Builds the segment display items from the current trip details.
    /// </summary>
    private IReadOnlyList<SegmentDisplayItem> BuildSegmentDisplayItems()
    {
        if (SelectedTripDetails?.Segments == null || !SelectedTripDetails.Segments.Any())
        {
            return Array.Empty<SegmentDisplayItem>();
        }

        var places = SelectedTripDetails.AllPlaces.ToDictionary(p => p.Id);

        return SelectedTripDetails.Segments.Select(segment =>
        {
            var originName = places.TryGetValue(segment.OriginId, out var origin) ? origin.Name : "Unknown";
            var destName = places.TryGetValue(segment.DestinationId, out var dest) ? dest.Name : "Unknown";

            return new SegmentDisplayItem
            {
                Id = segment.Id,
                OriginName = originName,
                DestinationName = destName,
                TransportMode = segment.TransportMode ?? "walk",
                DistanceKm = segment.DistanceKm,
                DurationMinutes = segment.DurationMinutes
            };
        }).ToList();
    }

    #endregion

    #region Place Editing

    /// <summary>
    /// Saves place changes with optimistic UI update.
    /// </summary>
    /// <param name="e">The place update event args.</param>
    public async Task SavePlaceChangesAsync(PlaceUpdateEventArgs e)
    {
        if (SelectedTrip == null)
            return;

        // Apply optimistic UI update
        var place = SelectedTripDetails?.AllPlaces.FirstOrDefault(p => p.Id == e.PlaceId);
        if (place != null)
        {
            place.Name = e.Name;
            place.Latitude = e.Latitude;
            place.Longitude = e.Longitude;
            place.Notes = e.Notes;
        }

        // Sync to server (handles offline queueing automatically)
        await _tripSyncService.UpdatePlaceAsync(
            e.PlaceId,
            SelectedTrip.Id,
            e.Name,
            e.Latitude,
            e.Longitude,
            e.Notes,
            includeNotes: true);
    }

    /// <summary>
    /// Handles sync completed event.
    /// </summary>
    private async void OnSyncCompleted(object? sender, SyncSuccessEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowSuccessAsync("Changes saved");
        });
    }

    /// <summary>
    /// Handles sync queued event (offline).
    /// </summary>
    private async void OnSyncQueued(object? sender, SyncQueuedEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowWarningAsync(e.Message);
        });
    }

    /// <summary>
    /// Handles sync rejected event (server error).
    /// </summary>
    private async void OnSyncRejected(object? sender, SyncFailureEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _toastService.ShowErrorAsync($"Save failed: {e.ErrorMessage}");

            // TODO: Could revert optimistic UI update here if needed
        });
    }

    #endregion
}
