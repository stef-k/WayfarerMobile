using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the navigation heads-up display (HUD).
/// Displays real-time navigation information including distance, direction, and ETA.
/// </summary>
public partial class NavigationHudViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    private readonly TripNavigationService _navigationService;
    private readonly INavigationAudioService _audioService;
    private readonly IWakeLockService _wakeLockService;
    private readonly ILogger<NavigationHudViewModel> _logger;

    // Audio announcement tracking
    private NavigationStatus _lastAnnouncedStatus = NavigationStatus.NoRoute;
    private string? _lastAnnouncedWaypointName;
    private DateTime _lastWaypointAnnouncementTime = DateTime.MinValue;
    private const double ApproachingWaypointThresholdMeters = 150;
    private const int MinWaypointAnnouncementIntervalSeconds = 30;

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether navigation is active.
    /// </summary>
    [ObservableProperty]
    private bool _isNavigating;

    /// <summary>
    /// Gets or sets whether the HUD is expanded (vs minimized).
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Gets or sets the destination name.
    /// </summary>
    [ObservableProperty]
    private string _destinationName = string.Empty;

    /// <summary>
    /// Gets or sets the next waypoint name.
    /// </summary>
    [ObservableProperty]
    private string _nextWaypointName = string.Empty;

    /// <summary>
    /// Gets or sets the distance to destination formatted string.
    /// </summary>
    [ObservableProperty]
    private string _distanceText = string.Empty;

    /// <summary>
    /// Gets or sets the distance to next waypoint formatted string.
    /// </summary>
    [ObservableProperty]
    private string _nextWaypointDistanceText = string.Empty;

    /// <summary>
    /// Gets or sets the ETA formatted string.
    /// </summary>
    [ObservableProperty]
    private string _etaText = string.Empty;

    /// <summary>
    /// Gets or sets the current instruction text.
    /// </summary>
    [ObservableProperty]
    private string _instructionText = string.Empty;

    /// <summary>
    /// Gets or sets the bearing to destination in degrees.
    /// </summary>
    [ObservableProperty]
    private double _bearingDegrees;

    /// <summary>
    /// Gets or sets the route progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>
    /// Gets or sets the navigation status text.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>
    /// Gets or sets the status color (hex).
    /// </summary>
    [ObservableProperty]
    private string _statusColor = "#4285F4"; // Blue

    /// <summary>
    /// Gets or sets whether the user is off-route.
    /// </summary>
    [ObservableProperty]
    private bool _isOffRoute;

    /// <summary>
    /// Gets or sets whether audio announcements are muted.
    /// </summary>
    [ObservableProperty]
    private bool _isMuted;

    /// <summary>
    /// Gets or sets the source page route for returning after navigation ends.
    /// Example: "//groups" to return to the Groups tab.
    /// </summary>
    [ObservableProperty]
    private string? _sourcePageRoute;

    #endregion

    /// <summary>
    /// Event raised when navigation should be stopped.
    /// The string parameter contains the source page route to return to (or null).
    /// </summary>
    public event EventHandler<string?>? StopNavigationRequested;

    /// <summary>
    /// Creates a new instance of NavigationHudViewModel.
    /// </summary>
    /// <param name="navigationService">The trip navigation service.</param>
    /// <param name="audioService">The navigation audio service.</param>
    /// <param name="wakeLockService">The wake lock service.</param>
    /// <param name="logger">The logger instance.</param>
    public NavigationHudViewModel(
        TripNavigationService navigationService,
        INavigationAudioService audioService,
        IWakeLockService wakeLockService,
        ILogger<NavigationHudViewModel> logger)
    {
        _navigationService = navigationService;
        _audioService = audioService;
        _wakeLockService = wakeLockService;
        _logger = logger;

        // Subscribe to navigation events
        _navigationService.StateChanged += OnNavigationStateChanged;
        _navigationService.Rerouted += OnRerouted;
    }

    /// <summary>
    /// Called when IsMuted changes.
    /// </summary>
    partial void OnIsMutedChanged(bool value)
    {
        _audioService.IsEnabled = !value;
        _logger.LogDebug("Audio announcements {State}", value ? "muted" : "enabled");
    }

    #region Commands

    /// <summary>
    /// Toggles the HUD between expanded and minimized states.
    /// </summary>
    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        _logger.LogDebug("Navigation HUD {State}", IsExpanded ? "expanded" : "minimized");
    }

    /// <summary>
    /// Requests to stop navigation.
    /// </summary>
    [RelayCommand]
    private async Task StopNavigationAsync()
    {
        _logger.LogInformation("Stop navigation requested from HUD, returning to: {SourcePage}", SourcePageRoute ?? "(main)");
        await _audioService.StopAsync();
        var returnRoute = SourcePageRoute;
        SourcePageRoute = null; // Clear for next navigation
        StopNavigationRequested?.Invoke(this, returnRoute);
    }

    /// <summary>
    /// Toggles audio mute state.
    /// </summary>
    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Starts displaying navigation for a route.
    /// </summary>
    /// <param name="route">The navigation route.</param>
    public async Task StartNavigationAsync(NavigationRoute route)
    {
        IsNavigating = true;
        IsExpanded = true;
        DestinationName = route.DestinationName;
        UpdateDistanceText(route.TotalDistanceMeters);
        UpdateEtaText(route.EstimatedDuration);
        StatusText = "Navigating";
        StatusColor = "#4285F4"; // Blue
        IsOffRoute = false;
        ProgressPercent = 0;

        // Reset audio tracking state for fresh navigation
        _lastAnnouncedStatus = NavigationStatus.NoRoute;
        _lastAnnouncedWaypointName = null;
        _lastWaypointAnnouncementTime = DateTime.MinValue;

        // Acquire wake lock to keep screen on during navigation
        _wakeLockService.AcquireWakeLock(keepScreenOn: true);

        _logger.LogInformation("Navigation HUD started for destination: {Destination}", route.DestinationName);

        // Announce navigation start
        await _audioService.AnnounceNavigationStartAsync(route.DestinationName, route.TotalDistanceMeters);
    }

    /// <summary>
    /// Stops navigation display.
    /// </summary>
    public void StopNavigationDisplay()
    {
        IsNavigating = false;
        DestinationName = string.Empty;
        NextWaypointName = string.Empty;
        DistanceText = string.Empty;
        EtaText = string.Empty;
        InstructionText = string.Empty;
        StatusText = "Ready";
        StatusColor = "#4285F4";
        IsOffRoute = false;
        ProgressPercent = 0;

        // Reset audio tracking state
        _lastAnnouncedStatus = NavigationStatus.NoRoute;
        _lastAnnouncedWaypointName = null;
        _lastWaypointAnnouncementTime = DateTime.MinValue;

        // Release wake lock when navigation ends
        _wakeLockService.ReleaseWakeLock();

        _logger.LogInformation("Navigation HUD stopped");
    }

    /// <summary>
    /// Updates the HUD with new navigation state.
    /// </summary>
    /// <param name="state">The current navigation state.</param>
    public void UpdateState(TripNavigationState state)
    {
        if (!IsNavigating)
            return;

        // Update distance
        UpdateDistanceText(state.DistanceToDestinationMeters);
        UpdateNextWaypointDistanceText(state.DistanceToNextWaypointMeters);

        // Update ETA
        UpdateEtaText(state.EstimatedTimeRemaining);

        // Update next waypoint
        NextWaypointName = state.NextWaypointName ?? string.Empty;

        // Update instruction
        InstructionText = state.CurrentInstruction ?? "Continue to destination";

        // Update bearing
        BearingDegrees = state.BearingToDestination;

        // Update progress
        ProgressPercent = state.ProgressPercent;

        // Update status based on navigation status
        switch (state.Status)
        {
            case NavigationStatus.OnRoute:
                StatusText = "On Route";
                StatusColor = "#4CAF50"; // Green
                IsOffRoute = false;
                break;

            case NavigationStatus.OffRoute:
                StatusText = "Off Route";
                StatusColor = "#FF9800"; // Orange
                IsOffRoute = true;
                break;

            case NavigationStatus.Arrived:
                StatusText = "Arrived!";
                StatusColor = "#4CAF50"; // Green
                IsOffRoute = false;
                break;

            case NavigationStatus.NoRoute:
                StatusText = "No Route";
                StatusColor = "#9E9E9E"; // Gray
                IsOffRoute = false;
                break;
        }

        // Trigger audio announcements based on state changes
        _ = HandleAudioAnnouncementsAsync(state);
    }

    /// <summary>
    /// Handles audio announcements based on navigation state changes.
    /// </summary>
    private async Task HandleAudioAnnouncementsAsync(TripNavigationState state)
    {
        try
        {
            // Handle status-based announcements
            if (state.Status != _lastAnnouncedStatus)
            {
                switch (state.Status)
                {
                    case NavigationStatus.OffRoute:
                        await _audioService.AnnounceOffRouteAsync();
                        break;

                    case NavigationStatus.Arrived:
                        await _audioService.AnnounceRouteCompleteAsync(DestinationName);
                        break;
                }

                _lastAnnouncedStatus = state.Status;
            }

            // Handle approaching waypoint announcements
            if (state.Status == NavigationStatus.OnRoute &&
                !string.IsNullOrEmpty(state.NextWaypointName) &&
                state.DistanceToNextWaypointMeters <= ApproachingWaypointThresholdMeters &&
                state.DistanceToNextWaypointMeters > 20) // Not too close
            {
                var canAnnounce = state.NextWaypointName != _lastAnnouncedWaypointName ||
                    (DateTime.UtcNow - _lastWaypointAnnouncementTime).TotalSeconds >= MinWaypointAnnouncementIntervalSeconds;

                if (canAnnounce)
                {
                    await _audioService.AnnounceApproachingWaypointAsync(
                        state.NextWaypointName,
                        state.DistanceToNextWaypointMeters,
                        null); // Transport mode not available here

                    _lastAnnouncedWaypointName = state.NextWaypointName;
                    _lastWaypointAnnouncementTime = DateTime.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trigger audio announcement");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Handles navigation state changes.
    /// </summary>
    private void OnNavigationStateChanged(object? sender, TripNavigationState state)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateState(state));
    }

    /// <summary>
    /// Handles rerouting events.
    /// </summary>
    private void OnRerouted(object? sender, string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            StatusText = "Rerouting...";
            StatusColor = "#2196F3"; // Blue

            // Announce rerouting
            try
            {
                await _audioService.AnnounceReroutingAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce rerouting");
            }
        });
    }

    /// <summary>
    /// Updates the distance text with formatted value.
    /// </summary>
    private void UpdateDistanceText(double meters)
    {
        DistanceText = meters >= 1000
            ? $"{meters / 1000:F1} km"
            : $"{meters:F0} m";
    }

    /// <summary>
    /// Updates the next waypoint distance text.
    /// </summary>
    private void UpdateNextWaypointDistanceText(double meters)
    {
        NextWaypointDistanceText = meters >= 1000
            ? $"{meters / 1000:F1} km"
            : $"{meters:F0} m";
    }

    /// <summary>
    /// Updates the ETA text with formatted value.
    /// </summary>
    private void UpdateEtaText(TimeSpan eta)
    {
        if (eta.TotalHours >= 1)
        {
            EtaText = $"{(int)eta.TotalHours}h {eta.Minutes}m";
        }
        else if (eta.TotalMinutes >= 1)
        {
            EtaText = $"{(int)eta.TotalMinutes} min";
        }
        else
        {
            EtaText = "< 1 min";
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _navigationService.StateChanged -= OnNavigationStateChanged;
        _navigationService.Rerouted -= OnRerouted;

        // Ensure wake lock is released
        _wakeLockService.ReleaseWakeLock();
    }

    #endregion
}
