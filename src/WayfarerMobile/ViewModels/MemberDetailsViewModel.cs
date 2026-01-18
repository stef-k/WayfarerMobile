using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Views.Controls;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for member details bottom sheet functionality.
/// Handles member selection, details display, and member-related actions.
/// </summary>
public partial class MemberDetailsViewModel : ObservableObject
{
    #region Fields

    private readonly IToastService _toastService;
    private readonly ITripNavigationService _tripNavigationService;
    private readonly ILogger<MemberDetailsViewModel> _logger;
    private IMemberDetailsCallbacks? _callbacks;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether the member details sheet is open.
    /// </summary>
    [ObservableProperty]
    private bool _isMemberSheetOpen;

    /// <summary>
    /// Gets or sets the selected member for the details sheet.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedMemberCoordinates))]
    [NotifyPropertyChangedFor(nameof(SelectedMemberLocationTime))]
    private GroupMember? _selectedMember;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the selected member's coordinates as text.
    /// </summary>
    public string SelectedMemberCoordinates => SelectedMember?.LastLocation != null
        ? $"{SelectedMember.LastLocation.Latitude:F6}, {SelectedMember.LastLocation.Longitude:F6}"
        : "N/A";

    /// <summary>
    /// Gets the selected member's location time as text.
    /// </summary>
    public string SelectedMemberLocationTime => SelectedMember?.LastLocation != null
        ? SelectedMember.LastLocation.Timestamp.ToLocalTime().ToString("ddd, MMM d yyyy HH:mm")
        : "N/A";

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of MemberDetailsViewModel.
    /// </summary>
    /// <param name="toastService">Toast notification service.</param>
    /// <param name="tripNavigationService">Navigation service for routing.</param>
    /// <param name="logger">Logger instance.</param>
    public MemberDetailsViewModel(
        IToastService toastService,
        ITripNavigationService tripNavigationService,
        ILogger<MemberDetailsViewModel> logger)
    {
        _toastService = toastService;
        _tripNavigationService = tripNavigationService;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the callbacks for accessing parent ViewModel state and operations.
    /// </summary>
    /// <param name="callbacks">The callback implementation.</param>
    public void SetCallbacks(IMemberDetailsCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Shows member details by user ID (called from map tap handler).
    /// </summary>
    /// <param name="userId">The user ID to show details for.</param>
    public void ShowMemberDetailsByUserId(string userId)
    {
        if (_callbacks == null)
            return;

        var member = _callbacks.Members.FirstOrDefault(m => m.UserId == userId);
        if (member != null)
        {
            ShowMemberDetails(member);
        }
    }

    /// <summary>
    /// Shows member details for a historical location (called when tapping historical marker).
    /// Updates the member's LastLocation to show the historical timestamp.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="latitude">The historical latitude.</param>
    /// <param name="longitude">The historical longitude.</param>
    /// <param name="timestamp">The historical timestamp.</param>
    public void ShowHistoricalMemberDetails(string userId, double latitude, double longitude, DateTime timestamp)
    {
        if (_callbacks == null)
            return;

        var member = _callbacks.Members.FirstOrDefault(m => m.UserId == userId);
        if (member != null)
        {
            // Update the member's LastLocation with the historical data
            member.LastLocation = new MemberLocation
            {
                Latitude = latitude,
                Longitude = longitude,
                Timestamp = timestamp,
                IsLive = false
            };
            ShowMemberDetails(member);
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Shows member details in the bottom sheet.
    /// </summary>
    /// <param name="member">The member to show details for.</param>
    [RelayCommand]
    private void ShowMemberDetails(GroupMember? member)
    {
        if (member == null) return;

        SelectedMember = member;
        IsMemberSheetOpen = true;
    }

    /// <summary>
    /// Closes the member details sheet.
    /// </summary>
    [RelayCommand]
    private void CloseMemberSheet()
    {
        IsMemberSheetOpen = false;
        SelectedMember = null;
    }

    /// <summary>
    /// Opens the selected member's location in Google Maps.
    /// </summary>
    [RelayCommand]
    private async Task OpenInMapsAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var location = new Microsoft.Maui.Devices.Sensors.Location(
                SelectedMember.LastLocation.Latitude,
                SelectedMember.LastLocation.Longitude);
            var options = new MapLaunchOptions { Name = SelectedMember.DisplayText };
            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Maps feature not supported on this device");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening maps");
        }
    }

    /// <summary>
    /// Searches Wikipedia for nearby places.
    /// </summary>
    [RelayCommand]
    private async Task SearchWikipediaAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{SelectedMember.LastLocation.Latitude},{SelectedMember.LastLocation.Longitude}";
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (UriFormatException ex)
        {
            _logger.LogError(ex, "Invalid Wikipedia URL");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening Wikipedia");
        }
    }

    /// <summary>
    /// Copies coordinates to clipboard with feedback.
    /// </summary>
    [RelayCommand]
    private async Task CopyCoordinatesAsync()
    {
        if (SelectedMember?.LastLocation == null)
        {
            await _toastService.ShowWarningAsync("No location available");
            return;
        }

        try
        {
            var coords = $"{SelectedMember.LastLocation.Latitude:F6}, {SelectedMember.LastLocation.Longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            await _toastService.ShowAsync("Coordinates copied");
            _logger.LogInformation("Coordinates copied to clipboard: {Coords}", coords);
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Clipboard not supported on this device");
            await _toastService.ShowErrorAsync("Clipboard not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error copying coordinates");
            await _toastService.ShowErrorAsync("Failed to copy coordinates");
        }
    }

    /// <summary>
    /// Shares the member's location.
    /// </summary>
    [RelayCommand]
    private async Task ShareLocationAsync()
    {
        if (SelectedMember?.LastLocation == null) return;

        try
        {
            var googleMapsUrl = $"https://www.google.com/maps?q={SelectedMember.LastLocation.Latitude:F6},{SelectedMember.LastLocation.Longitude:F6}";
            var text = $"{SelectedMember.DisplayText}'s location:\n{googleMapsUrl}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Location",
                Text = text
            });
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported on this device");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sharing location");
        }
    }

    /// <summary>
    /// Navigates to the member's location using OSRM routing with straight line fallback.
    /// Calculates route from current location and displays it on the main map.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToMemberAsync()
    {
        if (_callbacks == null)
            return;

        if (SelectedMember?.LastLocation == null)
        {
            await _toastService.ShowWarningAsync("No location available");
            return;
        }

        // Get current location
        var currentLocation = _callbacks.CurrentLocation;
        if (currentLocation == null)
        {
            await _toastService.ShowWarningAsync("Waiting for your location...");
            return;
        }

        // Ask user for navigation method using the styled picker
        var navMethod = await _callbacks.ShowNavigationPickerAsync();
        if (navMethod == null)
            return;

        // Handle external maps
        if (navMethod == NavigationMethod.ExternalMaps)
        {
            await OpenExternalMapsAsync(
                SelectedMember.LastLocation.Latitude,
                SelectedMember.LastLocation.Longitude);
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
            var destLat = SelectedMember.LastLocation.Latitude;
            var destLon = SelectedMember.LastLocation.Longitude;
            var destName = SelectedMember.DisplayText ?? "Member";

            _logger.LogInformation("Calculating {Mode} route to {Member} at {Lat},{Lon}", osrmProfile, destName, destLat, destLon);

            // Calculate route using OSRM with straight line fallback
            var route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
                currentLocation.Latitude,
                currentLocation.Longitude,
                destLat,
                destLon,
                destName,
                osrmProfile);

            // Close bottom sheet before navigating
            IsMemberSheetOpen = false;

            // Set source page for returning after navigation stops
            _callbacks.SetNavigationSourcePage("//groups");

            // Navigate to main map with route parameter
            await _callbacks.NavigateToMainMapWithRouteAsync(route);

            _logger.LogInformation("Started navigation to {Member}: {Distance:F1}km",
                destName, route.TotalDistanceMeters / 1000);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error calculating route: {Message}", ex.Message);
            await _toastService.ShowErrorAsync("Failed to calculate route");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Route calculation timed out");
            await _toastService.ShowErrorAsync("Route calculation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting navigation");
            await _toastService.ShowErrorAsync("Failed to start navigation");
        }
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
            IsMemberSheetOpen = false;

            var location = new Microsoft.Maui.Devices.Sensors.Location(lat, lon);
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
                await _toastService.ShowErrorAsync("Unable to open maps");
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
                await _toastService.ShowErrorAsync("Unable to open maps");
            }
        }
    }

    #endregion
}
