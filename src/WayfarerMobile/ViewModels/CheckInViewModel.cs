using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for manual check-in functionality.
/// </summary>
public partial class CheckInViewModel : BaseViewModel
{
    private readonly ILocationBridge _locationBridge;
    private readonly IApiClient _apiClient;
    private readonly MapService _mapService;

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
    private string _selectedActivity = "General";

    /// <summary>
    /// Gets or sets whether the check-in is being submitted.
    /// </summary>
    [ObservableProperty]
    private bool _isSubmitting;

    /// <summary>
    /// Gets or sets the result message.
    /// </summary>
    [ObservableProperty]
    private string? _resultMessage;

    /// <summary>
    /// Gets or sets whether the result is a success.
    /// </summary>
    [ObservableProperty]
    private bool _isSuccess;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether a location is available.
    /// </summary>
    public bool HasLocation => CurrentLocation != null;

    /// <summary>
    /// Gets the formatted location text.
    /// </summary>
    public string LocationText => CurrentLocation != null
        ? $"{CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6}"
        : "Acquiring location...";

    /// <summary>
    /// Gets the map instance for binding.
    /// </summary>
    public Mapsui.Map Map => _mapService.Map;

    /// <summary>
    /// Gets the available activity types.
    /// </summary>
    public List<string> ActivityTypes { get; } = new()
    {
        "General",
        "Work",
        "Home",
        "Restaurant",
        "Shopping",
        "Travel",
        "Exercise",
        "Social",
        "Other"
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of CheckInViewModel.
    /// </summary>
    public CheckInViewModel(
        ILocationBridge locationBridge,
        IApiClient apiClient,
        MapService mapService)
    {
        _locationBridge = locationBridge;
        _apiClient = apiClient;
        _mapService = mapService;
        Title = "Check In";
    }

    #endregion

    #region Commands

    /// <summary>
    /// Refreshes the current location.
    /// </summary>
    [RelayCommand]
    private async Task RefreshLocationAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ResultMessage = null;

            // Get current location from the bridge
            CurrentLocation = _locationBridge.LastLocation;

            if (CurrentLocation != null)
            {
                _mapService.UpdateLocation(CurrentLocation, centerMap: true);
                _mapService.SetDefaultZoom();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Submits the check-in to the server.
    /// </summary>
    [RelayCommand]
    private async Task SubmitCheckInAsync()
    {
        if (CurrentLocation == null)
        {
            ResultMessage = "No location available. Please wait for GPS.";
            IsSuccess = false;
            return;
        }

        if (IsSubmitting)
            return;

        try
        {
            IsSubmitting = true;
            ResultMessage = null;

            var request = new LocationLogRequest
            {
                Latitude = CurrentLocation.Latitude,
                Longitude = CurrentLocation.Longitude,
                Altitude = CurrentLocation.Altitude,
                Accuracy = CurrentLocation.Accuracy,
                Speed = CurrentLocation.Speed,
                Timestamp = DateTime.UtcNow,
                Provider = "manual-checkin"
            };

            var result = await _apiClient.CheckInAsync(request);

            if (result.Success)
            {
                ResultMessage = "Check-in successful!";
                IsSuccess = true;

                // Navigate back after a short delay
                await Task.Delay(1500);
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                ResultMessage = $"Check-in failed: {result.Message}";
                IsSuccess = false;
            }
        }
        catch (Exception ex)
        {
            ResultMessage = $"Error: {ex.Message}";
            IsSuccess = false;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    /// <summary>
    /// Cancels and goes back.
    /// </summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        await Shell.Current.GoToAsync("..");
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await RefreshLocationAsync();

        // Subscribe to location updates
        _locationBridge.LocationReceived += OnLocationReceived;
    }

    /// <summary>
    /// Called when the view disappears.
    /// </summary>
    public override Task OnDisappearingAsync()
    {
        _locationBridge.LocationReceived -= OnLocationReceived;
        return base.OnDisappearingAsync();
    }

    /// <summary>
    /// Handles location updates from the bridge.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentLocation = location;
            _mapService.UpdateLocation(location, centerMap: CurrentLocation == null);
        });
    }

    #endregion
}
