using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Helpers;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for context menu and dropped pin functionality.
/// Handles map location actions like navigate, share, Wikipedia search, etc.
/// </summary>
public partial class ContextMenuViewModel : BaseViewModel
{
    #region Fields

    private readonly ILogger<ContextMenuViewModel> _logger;
    private IContextMenuCallbacks? _callbacks;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether drop pin mode is active.
    /// When active, tapping the map shows a context menu at that location.
    /// </summary>
    [ObservableProperty]
    private bool _isDropPinModeActive;

    /// <summary>
    /// Gets or sets whether the context menu is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isContextMenuVisible;

    /// <summary>
    /// Gets or sets the context menu latitude.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLatitude;

    /// <summary>
    /// Gets or sets the context menu longitude.
    /// </summary>
    [ObservableProperty]
    private double _contextMenuLongitude;

    /// <summary>
    /// Gets or sets whether a dropped pin is visible on the map.
    /// Pin persists after context menu closes and can be tapped to reopen menu.
    /// </summary>
    [ObservableProperty]
    private bool _hasDroppedPin;

    /// <summary>
    /// Gets or sets the dropped pin latitude.
    /// </summary>
    [ObservableProperty]
    private double _droppedPinLatitude;

    /// <summary>
    /// Gets or sets the dropped pin longitude.
    /// </summary>
    [ObservableProperty]
    private double _droppedPinLongitude;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of ContextMenuViewModel.
    /// </summary>
    public ContextMenuViewModel(ILogger<ContextMenuViewModel> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the callbacks for accessing parent ViewModel state and operations.
    /// </summary>
    public void SetCallbacks(IContextMenuCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Shows the context menu at the specified coordinates with a visual pin marker.
    /// </summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    public void ShowContextMenu(double latitude, double longitude)
    {
        ContextMenuLatitude = latitude;
        ContextMenuLongitude = longitude;
        IsContextMenuVisible = true;
        IsDropPinModeActive = false;

        // Store pin location and show visual marker on map
        DroppedPinLatitude = latitude;
        DroppedPinLongitude = longitude;
        HasDroppedPin = true;

        _callbacks?.ShowDroppedPin(latitude, longitude);
    }

    /// <summary>
    /// Reopens the context menu at the dropped pin location.
    /// Called when user taps on an existing dropped pin.
    /// </summary>
    public void ReopenContextMenuFromPin()
    {
        if (!HasDroppedPin)
            return;

        ContextMenuLatitude = DroppedPinLatitude;
        ContextMenuLongitude = DroppedPinLongitude;
        IsContextMenuVisible = true;
    }

    /// <summary>
    /// Checks if the specified coordinates are near the dropped pin.
    /// </summary>
    /// <param name="latitude">The latitude to check.</param>
    /// <param name="longitude">The longitude to check.</param>
    /// <param name="toleranceMeters">Distance tolerance in meters (default 50m).</param>
    /// <returns>True if within tolerance of the dropped pin.</returns>
    public bool IsNearDroppedPin(double latitude, double longitude, double toleranceMeters = 50)
    {
        if (!HasDroppedPin)
            return false;

        // Simple distance calculation (Haversine for accuracy)
        const double earthRadiusMeters = 6371000;
        var dLat = (latitude - DroppedPinLatitude) * Math.PI / 180;
        var dLon = (longitude - DroppedPinLongitude) * Math.PI / 180;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DroppedPinLatitude * Math.PI / 180) * Math.Cos(latitude * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        var distance = earthRadiusMeters * c;

        return distance <= toleranceMeters;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Hides the context menu but keeps the dropped pin visible.
    /// The pin remains tappable to reopen the context menu.
    /// </summary>
    [RelayCommand]
    private void HideContextMenu()
    {
        IsContextMenuVisible = false;
        // Pin stays visible - user can tap it to reopen context menu
    }

    /// <summary>
    /// Clears the dropped pin from the map and hides the context menu.
    /// </summary>
    [RelayCommand]
    private void ClearDroppedPin()
    {
        IsContextMenuVisible = false;
        HasDroppedPin = false;
        DroppedPinLatitude = 0;
        DroppedPinLongitude = 0;

        _callbacks?.ClearDroppedPinFromMap();
    }

    /// <summary>
    /// Navigates to the context menu location with choice of internal or external navigation.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToContextLocationAsync()
    {
        if (_callbacks == null) return;

        // Show navigation method picker
        var navMethod = await _callbacks.ShowNavigationPickerAsync();

        if (navMethod == null)
            return;

        HideContextMenu();

        // Handle external maps
        if (navMethod == NavigationMethod.ExternalMaps)
        {
            await OpenExternalMapsAsync(ContextMenuLatitude, ContextMenuLongitude);
            return;
        }

        // Get current location for internal navigation
        var currentLocation = _callbacks.CurrentLocation ?? _callbacks.LocationBridge.LastLocation;
        if (currentLocation == null)
        {
            await _callbacks.ToastService.ShowWarningAsync("Waiting for your location...");
            return;
        }

        // Map selection to OSRM profile
        var osrmProfile = navMethod switch
        {
            NavigationMethod.Walk => "foot",
            NavigationMethod.Drive => "car",
            NavigationMethod.Bike => "bike",
            _ => "foot"
        };

        try
        {
            _callbacks.IsBusy = true;

            // Calculate route using OSRM with straight line fallback
            var route = await _callbacks.CalculateRouteToCoordinatesAsync(
                currentLocation.Latitude,
                currentLocation.Longitude,
                ContextMenuLatitude,
                ContextMenuLongitude,
                "Dropped Pin",
                osrmProfile);

            // Clear dropped pin and start navigation
            ClearDroppedPin();

            // Start navigation via coordinator
            await _callbacks.StartNavigationWithRouteAsync(route);

            _logger.LogInformation("Started navigation to dropped pin: {Distance:F1}km", route.TotalDistanceMeters / 1000);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error calculating route: {Message}", ex.Message);
            await _callbacks.ToastService.ShowErrorAsync("Network error. Please check your connection.");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Route calculation timed out");
            await _callbacks.ToastService.ShowErrorAsync("Request timed out. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start navigation");
            await _callbacks.ToastService.ShowErrorAsync("Failed to start navigation");
        }
        finally
        {
            _callbacks.IsBusy = false;
        }
    }

    /// <summary>
    /// Shares the context menu location.
    /// </summary>
    [RelayCommand]
    private async Task ShareContextLocationAsync()
    {
        var text = $"Location: {ContextMenuLatitude:F6}, {ContextMenuLongitude:F6}\n" +
                   $"https://maps.google.com/?q={ContextMenuLatitude},{ContextMenuLongitude}";

        await Share.RequestAsync(new ShareTextRequest
        {
            Title = "Share Location",
            Text = text
        });

        HideContextMenu();
    }

    /// <summary>
    /// Searches Wikipedia for articles near the context menu location.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{ContextMenuLatitude},{ContextMenuLongitude}";
        await Launcher.OpenAsync(new Uri(url));
        HideContextMenu();
    }

    /// <summary>
    /// Opens the context menu location in Google Maps.
    /// </summary>
    [RelayCommand]
    private async Task OpenInGoogleMapsAsync()
    {
        var url = $"https://www.google.com/maps/search/?api=1&query={ContextMenuLatitude},{ContextMenuLongitude}";
        await Launcher.OpenAsync(new Uri(url));
        HideContextMenu();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Opens external maps app for navigation.
    /// </summary>
    private async Task OpenExternalMapsAsync(double lat, double lon)
    {
        try
        {
            var location = new Location(lat, lon);
            var options = new MapLaunchOptions { NavigationMode = NavigationMode.Walking };

            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (FeatureNotSupportedException)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to open external maps via fallback URL");
                if (_callbacks != null)
                    await _callbacks.ToastService.ShowErrorAsync("Unable to open maps");
            }
        }
        catch (Exception ex)
        {
            // Fallback to Google Maps URL
            try
            {
                var url = $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=walking";
                await Launcher.OpenAsync(new Uri(url));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(ex, "Failed to open external maps");
                _logger.LogError(fallbackEx, "Fallback URL also failed");
                if (_callbacks != null)
                    await _callbacks.ToastService.ShowErrorAsync($"Unable to open maps: {ex.Message}");
            }
        }
    }

    #endregion
}
