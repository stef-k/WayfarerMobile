using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the onboarding flow managing permission requests and setup.
/// </summary>
public partial class OnboardingViewModel : BaseViewModel
{
    #region Fields

    private readonly IPermissionsService _permissionsService;
    private readonly ISettingsService _settingsService;
    private readonly ILocationBridge _locationBridge;
    private readonly IApiClient _apiClient;
    private readonly IActivitySyncService _activitySyncService;

    #endregion

    #region Observable Properties

    /// <summary>
    /// Gets or sets the current step index (0-based).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStepTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentStepDescription))]
    [NotifyPropertyChangedFor(nameof(CurrentStepIcon))]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowSkipButton))]
    [NotifyPropertyChangedFor(nameof(ShowRequestButton))]
    [NotifyPropertyChangedFor(nameof(StepProgress))]
    [NotifyPropertyChangedFor(nameof(DisplayStep))]
    private int _currentStep;

    /// <summary>
    /// Gets or sets whether location permission is granted.
    /// </summary>
    [ObservableProperty]
    private bool _locationPermissionGranted;

    /// <summary>
    /// Gets or sets whether background location permission is granted.
    /// </summary>
    [ObservableProperty]
    private bool _backgroundLocationGranted;

    /// <summary>
    /// Gets or sets whether notification permission is granted.
    /// </summary>
    [ObservableProperty]
    private bool _notificationPermissionGranted;

    /// <summary>
    /// Gets or sets whether battery optimization is disabled.
    /// </summary>
    [ObservableProperty]
    private bool _batteryOptimizationDisabled;

    /// <summary>
    /// Gets or sets whether server is configured.
    /// </summary>
    [ObservableProperty]
    private bool _serverConfigured;

    /// <summary>
    /// Gets or sets the server URL for manual entry.
    /// </summary>
    [ObservableProperty]
    private string _serverUrl = string.Empty;

    /// <summary>
    /// Gets or sets the API token for manual entry.
    /// </summary>
    [ObservableProperty]
    private string _apiToken = string.Empty;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Total number of onboarding steps.
    /// </summary>
    public int TotalSteps => 6;

    /// <summary>
    /// Gets whether this is the first step.
    /// </summary>
    public bool IsFirstStep => CurrentStep == 0;

    /// <summary>
    /// Gets whether this is the last step.
    /// </summary>
    public bool IsLastStep => CurrentStep == TotalSteps - 1;

    /// <summary>
    /// Gets the progress value (0.0 to 1.0).
    /// </summary>
    public double StepProgress => (double)(CurrentStep + 1) / TotalSteps;

    /// <summary>
    /// Gets the current step as 1-based for display (1 to 6).
    /// </summary>
    public int DisplayStep => CurrentStep + 1;

    /// <summary>
    /// Gets the current step title.
    /// </summary>
    public string CurrentStepTitle => CurrentStep switch
    {
        0 => "Welcome to WayfarerMobile",
        1 => "Location Access",
        2 => "Background Location",
        3 => "Notifications",
        4 => "Battery Optimization",
        5 => "Connect to Server",
        _ => "Setup"
    };

    /// <summary>
    /// Gets the current step description.
    /// </summary>
    public string CurrentStepDescription => CurrentStep switch
    {
        0 => "Your personal location tracking companion. Track your journeys, view your timeline, and stay connected with your groups.",
        1 => "WayfarerMobile needs access to your location to track your movements and show your position on the map.",
        2 => "To track your journeys even when the app is in the background, we need \"Always\" location access. This ensures continuous tracking.",
        3 => "Receive notifications about tracking status and important updates. This is required for the background tracking indicator.",
        4 => "For reliable background tracking, please disable battery optimization for WayfarerMobile. This prevents Android from stopping tracking.",
        5 => "Connect to your Wayfarer server by scanning a QR code or entering the server URL manually.",
        _ => ""
    };

    /// <summary>
    /// Gets the current step icon.
    /// </summary>
    public string CurrentStepIcon => CurrentStep switch
    {
        0 => "🗺️",
        1 => "📍",
        2 => "🔄",
        3 => "🔔",
        4 => "🔋",
        5 => "🔗",
        _ => "⚙️"
    };

    /// <summary>
    /// Gets the next button text.
    /// </summary>
    public string NextButtonText => CurrentStep switch
    {
        0 => "Get Started",
        5 => "Finish Setup",
        _ => "Continue"
    };

    /// <summary>
    /// Gets whether to show skip button.
    /// </summary>
    public bool ShowSkipButton => CurrentStep > 0 && CurrentStep < 5;

    /// <summary>
    /// Gets whether to show the permission request button.
    /// </summary>
    public bool ShowRequestButton => CurrentStep >= 1 && CurrentStep <= 4;

    /// <summary>
    /// Gets the request button text.
    /// </summary>
    public string RequestButtonText => CurrentStep switch
    {
        1 => LocationPermissionGranted ? "Granted ✓" : "Grant Access",
        2 => BackgroundLocationGranted ? "Granted ✓" : "Grant Access",
        3 => NotificationPermissionGranted ? "Granted ✓" : "Grant Access",
        4 => BatteryOptimizationDisabled ? "Disabled ✓" : "Disable Optimization",
        _ => "Request"
    };

    /// <summary>
    /// Gets whether the current permission is granted.
    /// </summary>
    public bool CurrentPermissionGranted => CurrentStep switch
    {
        1 => LocationPermissionGranted,
        2 => BackgroundLocationGranted,
        3 => NotificationPermissionGranted,
        4 => BatteryOptimizationDisabled,
        _ => true
    };

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of OnboardingViewModel.
    /// </summary>
    public OnboardingViewModel(
        IPermissionsService permissionsService,
        ISettingsService settingsService,
        ILocationBridge locationBridge,
        IApiClient apiClient,
        IActivitySyncService activitySyncService)
    {
        _permissionsService = permissionsService;
        _settingsService = settingsService;
        _locationBridge = locationBridge;
        _apiClient = apiClient;
        _activitySyncService = activitySyncService;
        Title = "Setup";

        // Check current permission states
        _ = CheckPermissionStatesAsync();
    }

    #endregion

    #region Commands

    /// <summary>
    /// Moves to the next step or completes onboarding.
    /// </summary>
    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsLastStep)
        {
            await CompleteOnboardingAsync();
        }
        else
        {
            CurrentStep++;
            await CheckPermissionStatesAsync();
        }
    }

    /// <summary>
    /// Moves to the previous step.
    /// </summary>
    [RelayCommand]
    private void Previous()
    {
        if (!IsFirstStep)
        {
            CurrentStep--;
        }
    }

    /// <summary>
    /// Skips the current permission step.
    /// </summary>
    [RelayCommand]
    private async Task SkipAsync()
    {
        CurrentStep++;
        await CheckPermissionStatesAsync();
    }

    /// <summary>
    /// Requests the permission for the current step.
    /// </summary>
    [RelayCommand]
    private async Task RequestPermissionAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;

            switch (CurrentStep)
            {
                case 1:
                    LocationPermissionGranted = await _permissionsService.RequestLocationPermissionAsync();
                    break;
                case 2:
                    BackgroundLocationGranted = await _permissionsService.RequestBackgroundLocationPermissionAsync();
                    break;
                case 3:
                    NotificationPermissionGranted = await _permissionsService.RequestNotificationPermissionAsync();
                    break;
                case 4:
                    await RequestBatteryOptimizationExemptionAsync();
                    break;
            }

            // Refresh all permission states
            await CheckPermissionStatesAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Scans a QR code to configure the server.
    /// </summary>
    [RelayCommand]
    private async Task ScanQrCodeAsync()
    {
        try
        {
            // First check/request camera permission
            var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (cameraStatus != PermissionStatus.Granted)
            {
                cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                if (cameraStatus != PermissionStatus.Granted)
                {
                    var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                    if (page != null)
                    {
                        await page.DisplayAlertAsync("Camera Permission Required",
                            "Please grant camera permission to scan QR codes.", "OK");
                    }
                    return;
                }
            }

            // Navigate to QR scanner page (use absolute route since we're in onboarding shell)
            await Shell.Current.GoToAsync($"//onboarding/QrScanner");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Onboarding] Failed to navigate to QR scanner: {ex}");
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", $"Could not open QR scanner: {ex.Message}", "OK");
            }
        }
    }

    /// <summary>
    /// Saves the manually entered server URL and API token.
    /// </summary>
    [RelayCommand]
    private async Task SaveServerUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", "Please enter a server URL.", "OK");
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiToken))
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Error", "Please enter an API token.", "OK");
            }
            return;
        }

        // Validate URL format
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page != null)
            {
                await page.DisplayAlertAsync("Invalid URL", "Please enter a valid HTTP or HTTPS URL.", "OK");
            }
            return;
        }

        _settingsService.ServerUrl = ServerUrl;
        _settingsService.ApiToken = ApiToken;
        ServerConfigured = true;

        // Fetch settings and activities from server in background
        _ = FetchServerDataAsync();
    }

    /// <summary>
    /// Fetches settings and activities from server after configuration.
    /// </summary>
    private async Task FetchServerDataAsync()
    {
        try
        {
            // Fetch server settings (time/distance thresholds)
            var serverSettings = await _apiClient.GetSettingsAsync();
            if (serverSettings != null)
            {
                _settingsService.LocationTimeThresholdMinutes = serverSettings.LocationTimeThresholdMinutes;
                _settingsService.LocationDistanceThresholdMeters = serverSettings.LocationDistanceThresholdMeters;
                Console.WriteLine($"[Onboarding] Fetched settings: {serverSettings.LocationTimeThresholdMinutes}min, {serverSettings.LocationDistanceThresholdMeters}m");
            }

            // Sync activities from server
            var activitySuccess = await _activitySyncService.SyncWithServerAsync();
            Console.WriteLine($"[Onboarding] Activities sync: {(activitySuccess ? "success" : "failed")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Onboarding] Failed to fetch server data: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens app settings for manual permission configuration.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _permissionsService.OpenAppSettings();
    }

    /// <summary>
    /// Refreshes all permission and configuration states.
    /// Called when the page appears to update UI after returning from other screens.
    /// </summary>
    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        await CheckPermissionStatesAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Checks the current state of all permissions.
    /// </summary>
    private async Task CheckPermissionStatesAsync()
    {
        LocationPermissionGranted = await _permissionsService.IsLocationPermissionGrantedAsync();
        BackgroundLocationGranted = await _permissionsService.IsBackgroundLocationPermissionGrantedAsync();
        NotificationPermissionGranted = await _permissionsService.IsNotificationPermissionGrantedAsync();
        BatteryOptimizationDisabled = await IsBatteryOptimizationDisabledAsync();
        ServerConfigured = !string.IsNullOrEmpty(_settingsService.ServerUrl) && !string.IsNullOrEmpty(_settingsService.ApiToken);
        ServerUrl = _settingsService.ServerUrl ?? string.Empty;
        ApiToken = _settingsService.ApiToken ?? string.Empty;

        // Notify computed properties
        OnPropertyChanged(nameof(RequestButtonText));
        OnPropertyChanged(nameof(CurrentPermissionGranted));
    }

    /// <summary>
    /// Checks if battery optimization is disabled for this app.
    /// </summary>
    private async Task<bool> IsBatteryOptimizationDisabledAsync()
    {
#if ANDROID
        await Task.CompletedTask;
        var context = Android.App.Application.Context;
        var powerManager = (Android.OS.PowerManager?)context.GetSystemService(Android.Content.Context.PowerService);
        return powerManager?.IsIgnoringBatteryOptimizations(context.PackageName!) ?? false;
#else
        await Task.CompletedTask;
        return true; // iOS doesn't have this concept
#endif
    }

    /// <summary>
    /// Requests battery optimization exemption.
    /// </summary>
    private async Task RequestBatteryOptimizationExemptionAsync()
    {
#if ANDROID
        try
        {
            var packageName = Android.App.Application.Context.PackageName;
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));

            if (Platform.CurrentActivity != null)
            {
                // Use activity context for proper dialog display
                Platform.CurrentActivity.StartActivity(intent);
            }
            else
            {
                // Fallback to application context with NewTask flag
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }

            // Poll for the change since the system dialog doesn't trigger app lifecycle events
            // The dialog typically takes 1-3 seconds for user interaction
            await PollForBatteryOptimizationChangeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Onboarding] Failed to request battery optimization exemption: {ex.Message}");

            // Fallback: open general battery settings
            try
            {
                var fallbackIntent = new Android.Content.Intent(Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings);
                fallbackIntent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(fallbackIntent);
            }
            catch
            {
                // Last resort: open general settings
                AppInfo.ShowSettingsUI();
            }
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// Polls for battery optimization status change after the system dialog is shown.
    /// </summary>
    private async Task PollForBatteryOptimizationChangeAsync()
    {
#if ANDROID
        var initialState = BatteryOptimizationDisabled;

        // Poll every 500ms for up to 30 seconds (user might take time to read and decide)
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(500);

            var currentState = await IsBatteryOptimizationDisabledAsync();
            if (currentState != initialState)
            {
                // State changed - update UI on main thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    BatteryOptimizationDisabled = currentState;
                    OnPropertyChanged(nameof(RequestButtonText));
                    OnPropertyChanged(nameof(CurrentPermissionGranted));
                });
                return;
            }
        }
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>
    /// Completes the onboarding process.
    /// </summary>
    private async Task CompleteOnboardingAsync()
    {
        // Mark onboarding as complete
        _settingsService.IsFirstRun = false;

        // Store user's background tracking choice for health check comparison
        // This allows us to alert users if they had 24/7 tracking but revoked permission
        _settingsService.BackgroundTrackingEnabled = BackgroundLocationGranted;
        Console.WriteLine($"[Onboarding] BackgroundTrackingEnabled set to: {BackgroundLocationGranted}");

        // Start the location tracking service if basic location permission was granted
        // - With background permission: runs 24/7
        // - Without background permission: runs only while app is in foreground (casual use)
        //
        // CRITICAL: Use MainThread.BeginInvokeOnMainThread to ensure we're at the end of
        // any queued main thread work. This prevents Android's 5-second foreground service
        // timeout from expiring if the main thread has pending work.
        if (LocationPermissionGranted)
        {
            var backgroundGranted = BackgroundLocationGranted; // Capture for closure
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await _locationBridge.StartAsync();
                    var mode = backgroundGranted ? "24/7 background" : "foreground only";
                    Console.WriteLine($"[Onboarding] Location tracking service started ({mode} mode)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Onboarding] Failed to start location service: {ex.Message}");
                }
            });
        }

        // Navigate to main app
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Shell shell)
        {
            await shell.GoToAsync("//main");
        }
    }

    #endregion
}
