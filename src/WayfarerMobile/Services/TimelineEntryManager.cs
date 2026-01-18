using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Shared.Controls;

namespace WayfarerMobile.Services;

/// <summary>
/// Manages timeline entry operations (CRUD and external integrations).
/// </summary>
public class TimelineEntryManager : ITimelineEntryManager
{
    private readonly ITimelineSyncService _timelineSyncService;
    private readonly IToastService _toastService;
    private readonly ILogger<TimelineEntryManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineEntryManager"/> class.
    /// </summary>
    public TimelineEntryManager(
        ITimelineSyncService timelineSyncService,
        IToastService toastService,
        ILogger<TimelineEntryManager> logger)
    {
        _timelineSyncService = timelineSyncService;
        _toastService = toastService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> SaveNotesAsync(int locationId, string? notesHtml)
    {
        try
        {
            await _timelineSyncService.UpdateLocationAsync(
                locationId,
                latitude: null,
                longitude: null,
                localTimestamp: null,
                notes: notesHtml,
                includeNotes: true);

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error saving notes for location {LocationId}: {Message}", locationId, ex.Message);
            await _toastService.ShowErrorAsync("Network error. Changes will sync when online.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving notes for location {LocationId}", locationId);
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SaveEntryChangesAsync(TimelineEntryUpdateEventArgs args)
    {
        try
        {
            await _timelineSyncService.UpdateLocationAsync(
                args.LocationId,
                args.Latitude,
                args.Longitude,
                args.LocalTimestamp,
                args.Notes,
                includeNotes: true,
                activityTypeId: args.ActivityTypeId,
                clearActivity: args.ClearActivity);

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogNetworkWarningIfOnline("Network error saving entry changes for location {LocationId}: {Message}", args.LocationId, ex.Message);
            await _toastService.ShowErrorAsync("Network error. Changes will sync when online.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving entry changes for location {LocationId}", args.LocationId);
            await _toastService.ShowErrorAsync($"Failed to save: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task OpenInMapsAsync(double latitude, double longitude, string locationName)
    {
        try
        {
            var location = new Microsoft.Maui.Devices.Sensors.Location(latitude, longitude);
            var options = new MapLaunchOptions { Name = locationName };
            await Microsoft.Maui.ApplicationModel.Map.Default.OpenAsync(location, options);
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Maps feature not supported on this device");
            await _toastService.ShowErrorAsync("Maps not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open maps for coordinates ({Latitude}, {Longitude})", latitude, longitude);
            await _toastService.ShowErrorAsync("Could not open maps");
        }
    }

    /// <inheritdoc/>
    public async Task SearchWikipediaAsync(double latitude, double longitude)
    {
        try
        {
            var url = $"https://en.wikipedia.org/wiki/Special:Nearby#/coord/{latitude},{longitude}";
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (UriFormatException ex)
        {
            _logger.LogError(ex, "Invalid Wikipedia URL for coordinates ({Latitude}, {Longitude})", latitude, longitude);
            await _toastService.ShowErrorAsync("Could not open Wikipedia");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Wikipedia for coordinates ({Latitude}, {Longitude})", latitude, longitude);
            await _toastService.ShowErrorAsync("Could not open Wikipedia");
        }
    }

    /// <inheritdoc/>
    public async Task CopyCoordinatesAsync(double latitude, double longitude)
    {
        try
        {
            var coords = $"{latitude:F6}, {longitude:F6}";
            await Clipboard.SetTextAsync(coords);
            await _toastService.ShowAsync("Coordinates copied");
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Clipboard not supported on this device");
            await _toastService.ShowErrorAsync("Clipboard not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy coordinates ({Latitude}, {Longitude})", latitude, longitude);
            await _toastService.ShowErrorAsync("Could not copy coordinates");
        }
    }

    /// <inheritdoc/>
    public async Task ShareLocationAsync(double latitude, double longitude, string timeText, string dateText)
    {
        try
        {
            var googleMapsUrl = $"https://www.google.com/maps?q={latitude:F6},{longitude:F6}";
            var text = $"Location from {timeText} on {dateText}:\n{googleMapsUrl}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Share Location",
                Text = text
            });
        }
        catch (FeatureNotSupportedException ex)
        {
            _logger.LogWarning(ex, "Share feature not supported on this device");
            await _toastService.ShowErrorAsync("Share not available");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to share location ({Latitude}, {Longitude})", latitude, longitude);
            await _toastService.ShowErrorAsync("Could not share location");
        }
    }
}
