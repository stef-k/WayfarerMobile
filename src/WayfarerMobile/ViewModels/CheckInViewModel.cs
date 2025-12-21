using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapsui.Layers;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for manual check-in functionality.
/// </summary>
public partial class CheckInViewModel : BaseViewModel
{
    private readonly ILocationBridge _locationBridge;
    private readonly IApiClient _apiClient;
    private readonly IMapBuilder _mapBuilder;
    private readonly ILocationLayerService _locationLayerService;
    private readonly IActivitySyncService _activitySyncService;
    private readonly IToastService _toastService;

    // Map state - CheckInViewModel owns its Map instance
    private Mapsui.Map? _map;
    private WritableLayer? _locationLayer;

    #region Observable Properties

    /// <summary>
    /// Gets or sets the current location.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocation))]
    [NotifyPropertyChangedFor(nameof(LocationText))]
    private LocationData? _currentLocation;

    /// <summary>
    /// Gets or sets the check-in notes.
    /// </summary>
    [ObservableProperty]
    private string _notes = string.Empty;

    /// <summary>
    /// Gets or sets the selected activity type.
    /// </summary>
    [ObservableProperty]
    private ActivityType? _selectedActivity;

    /// <summary>
    /// Gets or sets whether the check-in is being submitted.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private bool _isSubmitting;

    /// <summary>
    /// Gets or sets whether the result is a success.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverlay))]
    private bool _isSuccess;

    /// <summary>
    /// Gets or sets the overlay message text.
    /// </summary>
    [ObservableProperty]
    private string _overlayMessage = "Submitting check-in...";

    /// <summary>
    /// Gets or sets whether activities are loading.
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingActivities;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether a location is available.
    /// </summary>
    public bool HasLocation => CurrentLocation != null;

    /// <summary>
    /// Gets whether the overlay should be shown (submitting or success).
    /// </summary>
    public bool ShowOverlay => IsSubmitting || IsSuccess;

    /// <summary>
    /// Gets the formatted location text with age indicator.
    /// </summary>
    public string LocationText
    {
        get
        {
            if (CurrentLocation == null)
                return "No location available";

            var age = DateTime.UtcNow - CurrentLocation.Timestamp;
            var coords = $"{CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6}";

            if (age.TotalSeconds < 30)
                return coords;
            if (age.TotalMinutes < 5)
                return $"{coords} ({age.TotalSeconds:F0}s ago)";
            if (age.TotalHours < 1)
                return $"{coords} ({age.TotalMinutes:F0}m ago)";

            return $"{coords} (stale)";
        }
    }

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Mapsui.Map Map => _map ??= CreateMap();

    /// <summary>
    /// Gets the available activity types.
    /// </summary>
    public ObservableCollection<ActivityType> ActivityTypes { get; } = [];

    /// <summary>
    /// Event raised when check-in is successfully completed.
    /// </summary>
    public event EventHandler? CheckInCompleted;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CheckInViewModel.
    /// </summary>
    public CheckInViewModel(
        ILocationBridge locationBridge,
        IApiClient apiClient,
        IMapBuilder mapBuilder,
        ILocationLayerService locationLayerService,
        IActivitySyncService activitySyncService,
        IToastService toastService)
    {
        _locationBridge = locationBridge;
        _apiClient = apiClient;
        _mapBuilder = mapBuilder;
        _locationLayerService = locationLayerService;
        _activitySyncService = activitySyncService;
        _toastService = toastService;
        Title = "Check In";
    }

    #endregion

    #region Commands

    /// <summary>
    /// Refreshes the current location from the bridge.
    /// </summary>
    [RelayCommand]
    private void RefreshLocation()
    {
        // Get current location from the bridge (instant)
        CurrentLocation = _locationBridge.LastLocation;

        if (CurrentLocation != null)
        {
            UpdateLocation(CurrentLocation, centerMap: true);
        }
    }

    /// <summary>
    /// Refreshes activities from server.
    /// </summary>
    [RelayCommand]
    private async Task RefreshActivitiesAsync()
    {
        if (IsLoadingActivities)
            return;

        try
        {
            IsLoadingActivities = true;
            var success = await _activitySyncService.SyncWithServerAsync();
            if (success)
            {
                await LoadActivitiesAsync();
                await _toastService.ShowSuccessAsync("Activities refreshed");
            }
            else
            {
                await _toastService.ShowWarningAsync("Could not refresh activities");
            }
        }
        finally
        {
            IsLoadingActivities = false;
        }
    }

    /// <summary>
    /// Cancels the check-in and navigates back.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoBackAsync();
    }

    /// <summary>
    /// Submits the check-in to the server.
    /// </summary>
    [RelayCommand]
    private async Task SubmitCheckInAsync()
    {
        if (CurrentLocation == null)
        {
            await _toastService.ShowErrorAsync("No location available. Please wait for GPS.");
            return;
        }

        if (IsSubmitting)
            return;

        try
        {
            IsSubmitting = true;
            IsSuccess = false;
            OverlayMessage = "Submitting check-in...";

            var request = new LocationLogRequest
            {
                Latitude = CurrentLocation.Latitude,
                Longitude = CurrentLocation.Longitude,
                Altitude = CurrentLocation.Altitude,
                Accuracy = CurrentLocation.Accuracy,
                Speed = CurrentLocation.Speed,
                Timestamp = DateTime.UtcNow,
                Provider = "manual-checkin",
                ActivityTypeId = SelectedActivity?.Id,
                Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            var result = await _apiClient.CheckInAsync(request);

            IsSubmitting = false;

            if (result.Success)
            {
                // Show success state
                IsSuccess = true;
                OverlayMessage = "Check-in successful!";

                // Keep success visible for 1.5s so users can see it, then close
                await Task.Delay(1500);
                CheckInCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                await _toastService.ShowErrorAsync($"Check-in failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            IsSubmitting = false;
            await _toastService.ShowErrorAsync($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Resets the form state for a new check-in.
    /// </summary>
    public void ResetForm()
    {
        Notes = string.Empty;
        SelectedActivity = null;
        IsSuccess = false;
        IsSubmitting = false;
        OverlayMessage = "Submitting check-in...";
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();

        // INSTANT: Use last known location immediately for responsiveness
        CurrentLocation = _locationBridge.LastLocation;
        if (CurrentLocation != null)
        {
            UpdateLocation(CurrentLocation, centerMap: true);
        }
        else
        {
            // Fallback: Try MAUI Geolocation API for quick location
            _ = TryGetQuickLocationAsync();
        }

        // Subscribe to location updates for fresh data
        _locationBridge.LocationReceived += OnLocationReceived;

        // Load activities in background (don't block UI)
        _ = LoadActivitiesAsync();

        // Background sync of activities if needed
        _ = _activitySyncService.AutoSyncIfNeededAsync();
    }

    /// <summary>
    /// Tries to get a quick location using MAUI Geolocation API as fallback.
    /// </summary>
    private async Task TryGetQuickLocationAsync()
    {
        try
        {
            // First try cached location (instant)
            var cachedLocation = await Geolocation.GetLastKnownLocationAsync();
            if (cachedLocation != null)
            {
                CurrentLocation = new LocationData
                {
                    Latitude = cachedLocation.Latitude,
                    Longitude = cachedLocation.Longitude,
                    Altitude = cachedLocation.Altitude,
                    Accuracy = cachedLocation.Accuracy,
                    Speed = cachedLocation.Speed,
                    Timestamp = cachedLocation.Timestamp.UtcDateTime,
                    Provider = "maui-cached"
                };
                UpdateLocation(CurrentLocation, centerMap: true);
                return;
            }

            // If no cached location, try quick GPS fix (5 second timeout)
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5));
            var location = await Geolocation.GetLocationAsync(request);
            if (location != null)
            {
                CurrentLocation = new LocationData
                {
                    Latitude = location.Latitude,
                    Longitude = location.Longitude,
                    Altitude = location.Altitude,
                    Accuracy = location.Accuracy,
                    Speed = location.Speed,
                    Timestamp = location.Timestamp.UtcDateTime,
                    Provider = "maui-gps"
                };
                UpdateLocation(CurrentLocation, centerMap: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CheckInViewModel] Quick location failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        _locationBridge.LocationReceived -= OnLocationReceived;

        // Clear location layer to release memory
        if (_locationLayer != null)
        {
            _locationLayerService.ClearLocation(_locationLayer);
        }

        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        _locationBridge.LocationReceived -= OnLocationReceived;
        base.Cleanup();
    }

    /// <summary>
    /// Handles location updates from the bridge.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var isFirst = CurrentLocation == null;
            CurrentLocation = location;
            UpdateLocation(location, centerMap: isFirst);
        });
    }

    #endregion

    #region Map Management

    /// <summary>
    /// Creates the Map instance with location layer.
    /// </summary>
    private Mapsui.Map CreateMap()
    {
        _locationLayer = _mapBuilder.CreateLayer(_locationLayerService.LocationLayerName);
        var map = _mapBuilder.CreateMap(_locationLayer);

        // Set default zoom
        map.Navigator.ZoomTo(2);

        return map;
    }

    /// <summary>
    /// Updates the location on the map.
    /// </summary>
    /// <param name="location">The location data.</param>
    /// <param name="centerMap">Whether to center the map on the location.</param>
    private void UpdateLocation(LocationData location, bool centerMap = false)
    {
        // Ensure map is initialized
        _ = Map;

        if (_locationLayer != null)
        {
            _locationLayerService.UpdateLocation(_locationLayer, location);
        }

        if (centerMap && _map != null)
        {
            _mapBuilder.CenterOnLocation(_map, location.Latitude, location.Longitude, zoomLevel: 16);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads activity types from the sync service.
    /// </summary>
    private async Task LoadActivitiesAsync()
    {
        try
        {
            IsLoadingActivities = true;

            var activities = await _activitySyncService.GetActivityTypesAsync();

            ActivityTypes.Clear();
            foreach (var activity in activities)
            {
                ActivityTypes.Add(activity);
            }

            // Don't auto-select any activity - user must choose or leave empty
            // SelectedActivity remains null unless user selects one
        }
        finally
        {
            IsLoadingActivities = false;
        }
    }

    #endregion
}
