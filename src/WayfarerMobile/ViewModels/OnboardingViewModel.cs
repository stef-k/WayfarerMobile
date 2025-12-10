using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the onboarding flow managing permission requests and setup.
/// </summary>
public partial class OnboardingViewModel : BaseViewModel
{
    #region Fields

    private readonly IPermissionsService _permissionsService;
    private readonly ISettingsService _settingsService;

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
        ISettingsService settingsService)
    {
        _permissionsService = permissionsService;
        _settingsService = settingsService;
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
        // TODO: Implement QR scanning with ZXing
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page != null)
        {
            await page.DisplayAlertAsync("QR Scanner", "QR scanning will be implemented with ZXing.", "OK");
        }
    }

    /// <summary>
    /// Saves the manually entered server URL.
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
        ServerConfigured = true;
    }

    /// <summary>
    /// Opens app settings for manual permission configuration.
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        _permissionsService.OpenAppSettings();
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
        ServerConfigured = !string.IsNullOrEmpty(_settingsService.ServerUrl);
        ServerUrl = _settingsService.ServerUrl ?? string.Empty;

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
        await Task.CompletedTask;
        try
        {
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Onboarding] Failed to request battery optimization exemption: {ex.Message}");
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

        // Navigate to main app
        if (Application.Current?.Windows.FirstOrDefault()?.Page is Shell shell)
        {
            await shell.GoToAsync("//main");
        }
    }

    #endregion
}
