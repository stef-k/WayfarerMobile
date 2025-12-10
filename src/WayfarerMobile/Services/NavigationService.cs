using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for navigation guidance to trip places.
/// </summary>
public class NavigationService
{
    private readonly ILocationBridge _locationBridge;
    private readonly ILogger<NavigationService> _logger;

    private TripPlace? _destination;
    private bool _isNavigating;
    private double _arrivalThresholdMeters = 50;
    private DateTime _lastAnnouncementTime = DateTime.MinValue;

    /// <summary>
    /// Event raised when navigation state changes.
    /// </summary>
    public event EventHandler<NavigationState>? StateChanged;

    /// <summary>
    /// Event raised when a navigation instruction should be announced.
    /// </summary>
    public event EventHandler<string>? InstructionAnnounced;

    /// <summary>
    /// Gets the current navigation state.
    /// </summary>
    public NavigationState CurrentState { get; private set; } = new();

    /// <summary>
    /// Creates a new instance of NavigationService.
    /// </summary>
    public NavigationService(ILocationBridge locationBridge, ILogger<NavigationService> logger)
    {
        _locationBridge = locationBridge;
        _logger = logger;
    }

    /// <summary>
    /// Starts navigation to a destination place.
    /// </summary>
    /// <param name="destination">The destination place.</param>
    public async Task StartNavigationAsync(TripPlace destination)
    {
        if (_isNavigating)
        {
            await StopNavigationAsync();
        }

        _destination = destination;
        _isNavigating = true;

        _logger.LogInformation("Starting navigation to {Place}", destination.Name);

        // Subscribe to location updates
        _locationBridge.LocationReceived += OnLocationReceived;

        // Initial state update
        var currentLocation = _locationBridge.LastLocation;
        if (currentLocation != null)
        {
            UpdateNavigationState(currentLocation);
        }

        // Announce start
        AnnounceInstruction($"Starting navigation to {destination.Name}");
    }

    /// <summary>
    /// Stops the current navigation.
    /// </summary>
    public Task StopNavigationAsync()
    {
        if (!_isNavigating)
            return Task.CompletedTask;

        _locationBridge.LocationReceived -= OnLocationReceived;
        _isNavigating = false;
        _destination = null;

        CurrentState = new NavigationState { IsActive = false };
        StateChanged?.Invoke(this, CurrentState);

        _logger.LogInformation("Navigation stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets whether navigation is active.
    /// </summary>
    public bool IsNavigating => _isNavigating;

    /// <summary>
    /// Handles location updates during navigation.
    /// </summary>
    private void OnLocationReceived(object? sender, LocationData location)
    {
        if (!_isNavigating || _destination == null)
            return;

        UpdateNavigationState(location);
    }

    /// <summary>
    /// Updates the navigation state based on current location.
    /// </summary>
    private void UpdateNavigationState(LocationData currentLocation)
    {
        if (_destination == null)
            return;

        // Calculate distance to destination
        var distance = GeoMath.CalculateDistance(
            currentLocation.Latitude, currentLocation.Longitude,
            _destination.Latitude, _destination.Longitude);

        // Calculate bearing to destination
        var bearing = GeoMath.CalculateBearing(
            currentLocation.Latitude, currentLocation.Longitude,
            _destination.Latitude, _destination.Longitude);

        // Estimate time remaining (assuming walking speed of 5 km/h)
        var walkingSpeedMps = 1.4; // ~5 km/h
        var estimatedSeconds = distance / walkingSpeedMps;
        var estimatedTime = TimeSpan.FromSeconds(estimatedSeconds);

        // Update state
        CurrentState = new NavigationState
        {
            IsActive = true,
            DestinationName = _destination.Name,
            DistanceMeters = distance,
            BearingDegrees = bearing,
            EstimatedTimeRemaining = estimatedTime,
            CurrentLatitude = currentLocation.Latitude,
            CurrentLongitude = currentLocation.Longitude,
            DestinationLatitude = _destination.Latitude,
            DestinationLongitude = _destination.Longitude
        };

        StateChanged?.Invoke(this, CurrentState);

        // Check for arrival
        if (distance <= _arrivalThresholdMeters)
        {
            HandleArrival();
            return;
        }

        // Periodic distance announcements
        AnnounceDistanceIfNeeded(distance);
    }

    /// <summary>
    /// Handles arrival at destination.
    /// </summary>
    private async void HandleArrival()
    {
        try
        {
            // Notify of arrival before stopping
            CurrentState = new NavigationState
            {
                IsActive = false,
                HasArrived = true,
                DestinationName = _destination?.Name
            };
            StateChanged?.Invoke(this, CurrentState);

            AnnounceInstruction($"You have arrived at {_destination?.Name}");
            await StopNavigationAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling arrival at destination");
        }
    }

    /// <summary>
    /// Announces distance at appropriate intervals.
    /// </summary>
    private void AnnounceDistanceIfNeeded(double distanceMeters)
    {
        var now = DateTime.Now;
        var timeSinceLastAnnouncement = now - _lastAnnouncementTime;

        // Announce at specific distance thresholds
        string? announcement = distanceMeters switch
        {
            <= 100 when timeSinceLastAnnouncement.TotalSeconds >= 30 => $"{distanceMeters:F0} meters to destination",
            <= 500 when timeSinceLastAnnouncement.TotalSeconds >= 60 => $"{distanceMeters:F0} meters remaining",
            > 500 when timeSinceLastAnnouncement.TotalMinutes >= 2 => FormatDistanceAnnouncement(distanceMeters),
            _ => null
        };

        if (announcement != null)
        {
            AnnounceInstruction(announcement);
        }
    }

    /// <summary>
    /// Formats a distance announcement.
    /// </summary>
    private static string FormatDistanceAnnouncement(double meters)
    {
        if (meters >= 1000)
        {
            var km = meters / 1000;
            return $"{km:F1} kilometers to destination";
        }
        return $"{meters:F0} meters to destination";
    }

    /// <summary>
    /// Announces a navigation instruction.
    /// </summary>
    private void AnnounceInstruction(string instruction)
    {
        _lastAnnouncementTime = DateTime.Now;
        _logger.LogDebug("Navigation instruction: {Instruction}", instruction);
        InstructionAnnounced?.Invoke(this, instruction);

        // Use text-to-speech
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await TextToSpeech.Default.SpeakAsync(instruction);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to speak navigation instruction");
            }
        });
    }
}

/// <summary>
/// Represents the current navigation state.
/// </summary>
public class NavigationState
{
    /// <summary>
    /// Gets or sets whether navigation is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets whether the user has arrived at the destination.
    /// </summary>
    public bool HasArrived { get; set; }

    /// <summary>
    /// Gets or sets the destination name.
    /// </summary>
    public string? DestinationName { get; set; }

    /// <summary>
    /// Gets or sets the distance to destination in meters.
    /// </summary>
    public double DistanceMeters { get; set; }

    /// <summary>
    /// Gets or sets the bearing to destination in degrees.
    /// </summary>
    public double BearingDegrees { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    public TimeSpan EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the current latitude.
    /// </summary>
    public double CurrentLatitude { get; set; }

    /// <summary>
    /// Gets or sets the current longitude.
    /// </summary>
    public double CurrentLongitude { get; set; }

    /// <summary>
    /// Gets or sets the destination latitude.
    /// </summary>
    public double DestinationLatitude { get; set; }

    /// <summary>
    /// Gets or sets the destination longitude.
    /// </summary>
    public double DestinationLongitude { get; set; }

    /// <summary>
    /// Gets the formatted distance string.
    /// </summary>
    public string DistanceText => DistanceMeters >= 1000
        ? $"{DistanceMeters / 1000:F1} km"
        : $"{DistanceMeters:F0} m";

    /// <summary>
    /// Gets the formatted time remaining string.
    /// </summary>
    public string TimeText => EstimatedTimeRemaining.TotalMinutes >= 60
        ? $"{EstimatedTimeRemaining.TotalHours:F0}h {EstimatedTimeRemaining.Minutes}min"
        : $"{EstimatedTimeRemaining.TotalMinutes:F0} min";

    /// <summary>
    /// Gets the cardinal direction.
    /// </summary>
    public string DirectionText => BearingDegrees switch
    {
        >= 337.5 or < 22.5 => "N",
        >= 22.5 and < 67.5 => "NE",
        >= 67.5 and < 112.5 => "E",
        >= 112.5 and < 157.5 => "SE",
        >= 157.5 and < 202.5 => "S",
        >= 202.5 and < 247.5 => "SW",
        >= 247.5 and < 292.5 => "W",
        >= 292.5 and < 337.5 => "NW",
        _ => ""
    };
}
