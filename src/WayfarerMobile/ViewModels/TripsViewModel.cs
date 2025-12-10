using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for managing trips - viewing, downloading, and navigating.
/// </summary>
public partial class TripsViewModel : BaseViewModel
{
    private readonly ApiClient _apiClient;
    private readonly ISettingsService _settingsService;
    private readonly MapService _mapService;
    private readonly TripDownloadService _downloadService;
    private readonly NavigationService _navigationService;
    private readonly TripNavigationService _tripNavigationService;
    private readonly ILocationBridge _locationBridge;
    private readonly HashSet<Guid> _downloadedTripIds = new();

    #region Observable Properties

    /// <summary>
    /// Gets or sets the collection of available trips from the server.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TripSummary> _availableTrips = new();

    /// <summary>
    /// Gets or sets the selected trip.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrip))]
    [NotifyPropertyChangedFor(nameof(IsSelectedTripDownloaded))]
    private TripSummary? _selectedTrip;

    /// <summary>
    /// Gets or sets the selected trip details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlaces))]
    [NotifyPropertyChangedFor(nameof(HasSegments))]
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
    /// Gets whether the selected trip is downloaded.
    /// </summary>
    public bool IsSelectedTripDownloaded =>
        SelectedTrip != null && _downloadedTripIds.Contains(SelectedTrip.Id);

    /// <summary>
    /// Gets whether API is configured.
    /// </summary>
    public bool IsConfigured => _settingsService.IsConfigured;

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Mapsui.Map Map => _mapService.Map;

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
        ApiClient apiClient,
        ISettingsService settingsService,
        MapService mapService,
        TripDownloadService downloadService,
        NavigationService navigationService,
        TripNavigationService tripNavigationService,
        ILocationBridge locationBridge)
    {
        _apiClient = apiClient;
        _settingsService = settingsService;
        _mapService = mapService;
        _downloadService = downloadService;
        _navigationService = navigationService;
        _tripNavigationService = tripNavigationService;
        _locationBridge = locationBridge;
        Title = "Trips";

        // Subscribe to download progress
        _downloadService.ProgressChanged += OnDownloadProgressChanged;

        // Subscribe to navigation state changes
        _navigationService.StateChanged += OnNavigationStateChanged;

        // Subscribe to trip navigation state changes for route updates
        _tripNavigationService.StateChanged += OnTripNavigationStateChanged;
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

            AvailableTrips.Clear();
            foreach (var trip in trips.OrderByDescending(t => t.UpdatedAt))
            {
                AvailableTrips.Add(trip);
            }

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
        await LoadTripDetailsAsync(trip.Id);
        ShowingDetails = true;
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
        _mapService.ClearTripPlaces();
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
    /// Centers map on a specific place.
    /// </summary>
    [RelayCommand]
    private void CenterOnPlace(TripPlace? place)
    {
        if (place == null)
            return;

        var location = new LocationData
        {
            Latitude = place.Latitude,
            Longitude = place.Longitude
        };

        _mapService.CenterOnLocation(location);
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
            _mapService.ClearNavigationRoute();

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
                    _mapService.ShowNavigationRoute(route);
                    _mapService.ZoomToNavigationRoute();

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
            _mapService.UpdateNavigationRouteProgress(location.Latitude, location.Longitude);

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
        _mapService.ClearNavigationRoute();

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

        try
        {
            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusMessage = "Starting download...";

            var result = await _downloadService.DownloadTripAsync(SelectedTrip);

            if (result != null)
            {
                _downloadedTripIds.Add(SelectedTrip.Id);
                OnPropertyChanged(nameof(IsSelectedTripDownloaded));
                DownloadStatusMessage = "Download complete!";
            }
            else
            {
                DownloadStatusMessage = "Download failed";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Download failed: {ex.Message}";
            DownloadStatusMessage = "Download failed";
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
            _downloadedTripIds.Remove(SelectedTrip.Id);
            OnPropertyChanged(nameof(IsSelectedTripDownloaded));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete: {ex.Message}";
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Displays trip places on the map with custom icons.
    /// </summary>
    private async Task DisplayTripOnMapAsync(TripDetails trip)
    {
        // Use the dedicated trip places layer with custom icons
        await _mapService.UpdateTripPlacesAsync(trip.AllPlaces);

        // Center on trip bounding box or first place
        if (trip.CenterLat.HasValue && trip.CenterLon.HasValue)
        {
            var centerLocation = new LocationData
            {
                Latitude = trip.CenterLat.Value,
                Longitude = trip.CenterLon.Value
            };
            _mapService.CenterOnLocation(centerLocation);
        }
        else if (trip.AllPlaces.Any())
        {
            var firstPlace = trip.AllPlaces.First();
            var location = new LocationData
            {
                Latitude = firstPlace.Latitude,
                Longitude = firstPlace.Longitude
            };
            _mapService.CenterOnLocation(location);
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
                _mapService.ClearNavigationRoute();
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
                _mapService.ClearNavigationRoute();
            }
            // Handle rerouting - redraw the route
            else if (state.Status == NavigationStatus.OnRoute)
            {
                var route = _tripNavigationService.ActiveRoute;
                if (route != null)
                {
                    _mapService.ShowNavigationRoute(route);
                }
            }
        });
    }

    /// <summary>
    /// Loads the list of downloaded trip IDs.
    /// </summary>
    private async Task LoadDownloadedTripIdsAsync()
    {
        var downloaded = await _downloadService.GetDownloadedTripsAsync();
        _downloadedTripIds.Clear();
        foreach (var trip in downloaded)
        {
            _downloadedTripIds.Add(trip.ServerId);
        }
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        // Load downloaded trip IDs first
        await LoadDownloadedTripIdsAsync();

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

        // Clear map markers when leaving
        if (!ShowingDetails)
        {
            _mapService.ClearTripPlaces();
        }
        await base.OnDisappearingAsync();
    }

    #endregion
}
