using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for location tracking lifecycle management.
/// Handles start/stop/pause/resume tracking, permissions, and performance modes.
/// </summary>
public partial class TrackingCoordinatorViewModel : BaseViewModel
{
    #region Fields

    private readonly ILocationBridge _locationBridge;
    private readonly IPermissionsService _permissionsService;
    private readonly ILogger<TrackingCoordinatorViewModel> _logger;
    private ITrackingCallbacks? _callbacks;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the current tracking state.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTracking))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonText))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonIcon))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonImage))]
    [NotifyPropertyChangedFor(nameof(TrackingButtonColor))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TrackingState _trackingState = TrackingState.NotInitialized;

    /// <summary>
    /// Gets or sets the current performance mode.
    /// </summary>
    [ObservableProperty]
    private PerformanceMode _performanceMode = PerformanceMode.Normal;

    /// <summary>
    /// Gets or sets the location update count.
    /// </summary>
    [ObservableProperty]
    private int _locationCount;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether tracking is currently active.
    /// </summary>
    public bool IsTracking => TrackingState == TrackingState.Active;

    /// <summary>
    /// Gets the tracking button text based on current state.
    /// </summary>
    public string TrackingButtonText => TrackingState switch
    {
        TrackingState.Active => "Stop Tracking",
        TrackingState.Paused => "Resume Tracking",
        _ => "Start Tracking"
    };

    /// <summary>
    /// Gets the tracking button icon based on current state.
    /// </summary>
    public string TrackingButtonIcon => TrackingState switch
    {
        TrackingState.Active => "⏹",
        TrackingState.Paused => "▶",
        _ => "▶"
    };

    /// <summary>
    /// Gets the tracking button image source based on current state.
    /// </summary>
    public string TrackingButtonImage => TrackingState switch
    {
        TrackingState.Active => "stop.png",
        TrackingState.Paused => "play_start.png",
        _ => "play_start.png"
    };

    /// <summary>
    /// Gets the tracking button color based on current state.
    /// </summary>
    public Color TrackingButtonColor => TrackingState switch
    {
        TrackingState.Active => Colors.Red,
        TrackingState.Paused => Colors.Orange,
        _ => Application.Current?.Resources["Primary"] as Color ?? Colors.Blue
    };

    /// <summary>
    /// Gets the status text based on current state.
    /// </summary>
    public string StatusText => TrackingState switch
    {
        TrackingState.NotInitialized => "Ready",
        TrackingState.PermissionsNeeded => "Permissions required",
        TrackingState.PermissionsDenied => "Permissions denied",
        TrackingState.Ready => "Ready",
        TrackingState.Starting => "Starting...",
        TrackingState.Active => "Tracking",
        TrackingState.Paused => "Paused",
        TrackingState.Stopping => "Stopping...",
        TrackingState.Error => "Error",
        _ => "Ready"
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of TrackingCoordinatorViewModel.
    /// </summary>
    public TrackingCoordinatorViewModel(
        ILocationBridge locationBridge,
        IPermissionsService permissionsService,
        ILogger<TrackingCoordinatorViewModel> logger)
    {
        _locationBridge = locationBridge;
        _permissionsService = permissionsService;
        _logger = logger;

        // Subscribe to state changes
        _locationBridge.StateChanged += OnStateChanged;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the callbacks for accessing parent ViewModel state and operations.
    /// </summary>
    public void SetCallbacks(ITrackingCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Increments the location count.
    /// Called when a new location is received.
    /// </summary>
    public void IncrementLocationCount()
    {
        LocationCount++;
    }

    /// <summary>
    /// Checks and updates the permissions state.
    /// Only updates if not actively tracking.
    /// </summary>
    public async Task CheckPermissionsStateAsync()
    {
        // Only update state if not actively tracking
        if (TrackingState == TrackingState.Active || TrackingState == TrackingState.Paused)
            return;

        var hasPermissions = await _permissionsService.AreTrackingPermissionsGrantedAsync();

        if (!hasPermissions)
        {
            TrackingState = TrackingState.PermissionsNeeded;
        }
        else if (TrackingState == TrackingState.PermissionsNeeded || TrackingState == TrackingState.PermissionsDenied)
        {
            // Permissions were granted, update to ready state
            TrackingState = TrackingState.Ready;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Toggles tracking on/off.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            if (TrackingState == TrackingState.Active)
            {
                await _locationBridge.StopAsync();
            }
            else if (TrackingState == TrackingState.Paused)
            {
                await _locationBridge.ResumeAsync();
            }
            else
            {
                // Check and request permissions before starting
                var permissionsGranted = await CheckAndRequestPermissionsAsync();
                if (!permissionsGranted)
                {
                    TrackingState = TrackingState.PermissionsDenied;
                    return;
                }

                // CRITICAL: Use MainThread.BeginInvokeOnMainThread to ensure we're at the end of
                // any queued main thread work. This prevents Android's 5-second foreground service
                // timeout from expiring if the main thread has pending work.
                var tcs = new TaskCompletionSource();
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await _locationBridge.StartAsync();
                        tcs.SetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Pauses tracking.
    /// </summary>
    [RelayCommand]
    private async Task PauseTrackingAsync()
    {
        if (TrackingState == TrackingState.Active)
        {
            await _locationBridge.PauseAsync();
        }
    }

    /// <summary>
    /// Opens app settings for permission management.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _permissionsService.OpenAppSettings();
    }

    /// <summary>
    /// Sets the performance mode.
    /// </summary>
    /// <param name="mode">The mode to set.</param>
    [RelayCommand]
    private async Task SetPerformanceModeAsync(PerformanceMode mode)
    {
        PerformanceMode = mode;
        await _locationBridge.SetPerformanceModeAsync(mode);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles tracking state changes from the location bridge.
    /// </summary>
    private void OnStateChanged(object? sender, TrackingState state)
    {
        TrackingState = state;

        // Clear location indicator when stopping
        if (state == TrackingState.Ready || state == TrackingState.NotInitialized)
        {
            _callbacks?.ClearLocationIndicator();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Checks and requests required permissions.
    /// </summary>
    /// <returns>True if all required permissions are granted.</returns>
    private async Task<bool> CheckAndRequestPermissionsAsync()
    {
        // Check if already granted
        if (await _permissionsService.AreTrackingPermissionsGrantedAsync())
        {
            return true;
        }

        // Request permissions
        var result = await _permissionsService.RequestTrackingPermissionsAsync();

        if (!result.LocationGranted)
        {
            // Show alert about required permissions
            if (_callbacks != null)
            {
                var openSettings = await _callbacks.DisplayAlertAsync(
                    "Location Permission Required",
                    "WayfarerMobile needs location permission to track your movements. Please grant permission in Settings.",
                    "Open Settings",
                    "Cancel");

                if (openSettings)
                {
                    _callbacks.OpenAppSettings();
                }
            }
            return false;
        }

        return result.AllGranted || result.LocationGranted;
    }

    #endregion

    #region Cleanup

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _locationBridge.StateChanged -= OnStateChanged;
        base.Cleanup();
    }

    #endregion
}
