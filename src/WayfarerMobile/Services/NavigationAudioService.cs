using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for generating and playing contextual navigation audio announcements.
/// Handles announcement timing, deduplication, and formatting.
/// </summary>
public class NavigationAudioService : INavigationAudioService
{
    private readonly ITextToSpeechService _ttsService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<NavigationAudioService> _logger;

    // Announcement deduplication
    private string? _lastAnnouncedWaypoint;
    private DateTime _lastAnnouncementTime = DateTime.MinValue;
    private const int MinAnnouncementIntervalSeconds = 10;

    /// <summary>
    /// Gets or sets whether audio announcements are enabled.
    /// Reads from and writes to settings service.
    /// </summary>
    public bool IsEnabled
    {
        get => _settingsService.NavigationAudioEnabled;
        set => _settingsService.NavigationAudioEnabled = value;
    }

    /// <summary>
    /// Creates a new instance of NavigationAudioService.
    /// </summary>
    /// <param name="ttsService">The text-to-speech service.</param>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="logger">The logger instance.</param>
    public NavigationAudioService(
        ITextToSpeechService ttsService,
        ISettingsService settingsService,
        ILogger<NavigationAudioService> logger)
    {
        _ttsService = ttsService;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Announces the start of navigation.
    /// </summary>
    public async Task AnnounceNavigationStartAsync(string destinationName, double totalDistanceMeters)
    {
        if (!IsEnabled) return;

        var distanceText = FormatDistance(totalDistanceMeters);
        var announcement = $"Starting navigation to {destinationName}. Total distance: {distanceText}.";

        _logger.LogInformation("Navigation start announcement: {Announcement}", announcement);
        await _ttsService.SpeakAsync(announcement);

        ResetDeduplication();
    }

    /// <summary>
    /// Announces approaching a waypoint.
    /// </summary>
    public async Task AnnounceApproachingWaypointAsync(string waypointName, double distanceMeters, string? transportMode)
    {
        if (!IsEnabled) return;

        // Check deduplication
        if (!ShouldAnnounce(waypointName))
        {
            _logger.LogDebug("Skipping duplicate announcement for {Waypoint}", waypointName);
            return;
        }

        var distanceText = FormatDistance(distanceMeters);
        var actionVerb = GetActionVerb(transportMode);

        string announcement;
        if (distanceMeters <= 50)
        {
            announcement = $"{actionVerb} {waypointName} ahead.";
        }
        else
        {
            announcement = $"In {distanceText}, {actionVerb.ToLowerInvariant()} {waypointName}.";
        }

        _logger.LogDebug("Approaching waypoint announcement: {Announcement}", announcement);
        await _ttsService.SpeakAsync(announcement);

        RecordAnnouncement(waypointName);
    }

    /// <summary>
    /// Announces arrival at a waypoint.
    /// </summary>
    public async Task AnnounceArrivalAsync(string waypointName)
    {
        if (!IsEnabled) return;

        var announcement = $"You have arrived at {waypointName}.";

        _logger.LogInformation("Arrival announcement: {Announcement}", announcement);
        await _ttsService.SpeakAsync(announcement);

        ResetDeduplication();
    }

    /// <summary>
    /// Announces that the user is off route.
    /// </summary>
    public async Task AnnounceOffRouteAsync()
    {
        if (!IsEnabled) return;

        // Throttle off-route announcements
        if (!CanAnnounce())
        {
            return;
        }

        var announcement = "You are off route.";

        _logger.LogDebug("Off-route announcement");
        await _ttsService.SpeakAsync(announcement);

        RecordAnnouncement("off_route");
    }

    /// <summary>
    /// Announces that the route has been recalculated.
    /// </summary>
    public async Task AnnounceReroutingAsync()
    {
        if (!IsEnabled) return;

        var announcement = "Route recalculated.";

        _logger.LogDebug("Rerouting announcement");
        await _ttsService.SpeakAsync(announcement);

        RecordAnnouncement("rerouting");
    }

    /// <summary>
    /// Announces route completion.
    /// </summary>
    public async Task AnnounceRouteCompleteAsync(string destinationName)
    {
        if (!IsEnabled) return;

        var announcement = $"You have arrived at your destination, {destinationName}. Navigation complete.";

        _logger.LogInformation("Route complete announcement: {Announcement}", announcement);
        await _ttsService.SpeakAsync(announcement);

        ResetDeduplication();
    }

    /// <summary>
    /// Announces a turn-by-turn step instruction.
    /// </summary>
    public async Task AnnounceStepInstructionAsync(string instruction, double distanceMeters)
    {
        if (!IsEnabled) return;

        // Check deduplication - use instruction as identifier
        var instructionKey = $"step:{instruction}";
        if (!ShouldAnnounce(instructionKey))
        {
            _logger.LogDebug("Skipping duplicate step announcement: {Instruction}", instruction);
            return;
        }

        // Build announcement with distance context if significant
        string announcement;
        if (distanceMeters >= 50)
        {
            var distanceText = FormatDistance(distanceMeters);
            announcement = $"In {distanceText}, {instruction}";
        }
        else
        {
            // Immediate instruction (within 50m)
            announcement = instruction;
        }

        _logger.LogDebug("Step announcement: {Announcement}", announcement);
        await _ttsService.SpeakAsync(announcement);

        RecordAnnouncement(instructionKey);
    }

    /// <summary>
    /// Stops any current announcement.
    /// </summary>
    public async Task StopAsync()
    {
        await _ttsService.StopAsync();
        ResetDeduplication();
    }

    #region Private Methods

    /// <summary>
    /// Formats distance for speech output based on user's distance unit preference.
    /// </summary>
    private string FormatDistance(double meters)
    {
        var useMiles = _settingsService.DistanceUnits == "miles";

        if (useMiles)
        {
            // Convert to miles (1 mile = 1609.344 meters)
            var miles = meters / 1609.344;
            if (miles >= 0.5)
            {
                return miles >= 10
                    ? $"{miles:F0} miles"
                    : $"{miles:F1} miles";
            }

            // Use feet for short distances (1 mile = 5280 feet)
            var feet = meters * 3.28084;
            var roundedFeet = Math.Round(feet / 50) * 50; // Round to nearest 50 feet
            return $"{roundedFeet:F0} feet";
        }

        // Metric (default)
        if (meters >= 1000)
        {
            var km = meters / 1000;
            return km >= 10
                ? $"{km:F0} kilometers"
                : $"{km:F1} kilometers";
        }

        // Round to nearest 10 meters for cleaner speech
        var roundedMeters = Math.Round(meters / 10) * 10;
        return $"{roundedMeters:F0} meters";
    }

    /// <summary>
    /// Gets the action verb based on transport mode.
    /// </summary>
    private static string GetActionVerb(string? transportMode)
    {
        return transportMode?.ToLowerInvariant() switch
        {
            "walk" or "walking" => "Walk to",
            "drive" or "driving" or "car" => "Drive to",
            "transit" or "bus" or "train" or "subway" => "Take transit to",
            "bike" or "bicycle" or "cycling" => "Cycle to",
            "ferry" or "boat" => "Take the ferry to",
            "taxi" or "rideshare" => "Take a ride to",
            "flight" or "plane" => "Fly to",
            _ => "Head to"
        };
    }

    /// <summary>
    /// Checks if enough time has passed since the last announcement.
    /// </summary>
    private bool CanAnnounce()
    {
        var timeSinceLastAnnouncement = DateTime.UtcNow - _lastAnnouncementTime;
        return timeSinceLastAnnouncement.TotalSeconds >= MinAnnouncementIntervalSeconds;
    }

    /// <summary>
    /// Checks if we should announce for this waypoint (deduplication).
    /// </summary>
    private bool ShouldAnnounce(string waypointName)
    {
        // Different waypoint - always announce
        if (_lastAnnouncedWaypoint != waypointName)
        {
            return true;
        }

        // Same waypoint - check time threshold
        return CanAnnounce();
    }

    /// <summary>
    /// Records that an announcement was made.
    /// </summary>
    private void RecordAnnouncement(string identifier)
    {
        _lastAnnouncedWaypoint = identifier;
        _lastAnnouncementTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Resets deduplication state.
    /// </summary>
    private void ResetDeduplication()
    {
        _lastAnnouncedWaypoint = null;
        _lastAnnouncementTime = DateTime.MinValue;
    }

    #endregion
}
