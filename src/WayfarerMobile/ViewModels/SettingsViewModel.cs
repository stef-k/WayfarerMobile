using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    #region Fields

    private readonly ISettingsService _settingsService;
    private readonly IAppLockService _appLockService;
    private readonly DatabaseService _databaseService;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the PIN security view model for the security section.
    /// </summary>
    public PinSecurityViewModel PinSecurity { get; }

    /// <summary>
    /// Gets or sets whether timeline tracking is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _timelineTrackingEnabled;

    /// <summary>
    /// Gets or sets the server URL.
    /// </summary>
    [ObservableProperty]
    private string _serverUrl = string.Empty;

    /// <summary>
    /// Gets or sets the location time threshold.
    /// </summary>
    [ObservableProperty]
    private int _locationTimeThreshold;

    /// <summary>
    /// Gets or sets the location distance threshold.
    /// </summary>
    [ObservableProperty]
    private int _locationDistanceThreshold;

    /// <summary>
    /// Gets or sets whether dark mode is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _darkModeEnabled;

    /// <summary>
    /// Gets or sets whether offline map cache is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _mapOfflineCacheEnabled;

    /// <summary>
    /// Gets or sets whether navigation audio is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _navigationAudioEnabled;

    /// <summary>
    /// Gets or sets whether navigation vibration is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _navigationVibrationEnabled;

    /// <summary>
    /// Gets or sets whether auto-reroute is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _autoRerouteEnabled;

    /// <summary>
    /// Gets or sets the distance units (kilometers or miles).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKilometers))]
    [NotifyPropertyChangedFor(nameof(IsMiles))]
    private string _distanceUnits = "kilometers";

    /// <summary>
    /// Gets whether kilometers is selected.
    /// </summary>
    public bool IsKilometers => DistanceUnits == "kilometers";

    /// <summary>
    /// Gets whether miles is selected.
    /// </summary>
    public bool IsMiles => DistanceUnits == "miles";

    /// <summary>
    /// Gets or sets whether to show battery warnings during tracking.
    /// </summary>
    [ObservableProperty]
    private bool _showBatteryWarnings;

    /// <summary>
    /// Gets or sets whether to auto-pause tracking on critical battery.
    /// </summary>
    [ObservableProperty]
    private bool _autoPauseTrackingOnCriticalBattery;

    /// <summary>
    /// Gets or sets the user email.
    /// </summary>
    [ObservableProperty]
    private string _userEmail = string.Empty;

    /// <summary>
    /// Gets whether the user is logged in.
    /// </summary>
    [ObservableProperty]
    private bool _isLoggedIn;

    /// <summary>
    /// Gets the last sync time display text.
    /// </summary>
    [ObservableProperty]
    private string _lastSyncText = "Never";

    /// <summary>
    /// Gets the app version.
    /// </summary>
    public string AppVersion => $"Version {AppInfo.VersionString} ({AppInfo.BuildString})";

    /// <summary>
    /// Gets or sets a description of the current tracking mode based on BackgroundTrackingEnabled setting.
    /// </summary>
    [ObservableProperty]
    private string _trackingModeDescription = string.Empty;

    #endregion

    #region Cache Settings Properties

    /// <summary>
    /// Gets or sets the prefetch radius (1-10).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrefetchRadiusGridSize))]
    private int _liveCachePrefetchRadius;

    /// <summary>
    /// Gets the grid size description for the current prefetch radius.
    /// </summary>
    public string PrefetchRadiusGridSize => $"{2 * LiveCachePrefetchRadius + 1}×{2 * LiveCachePrefetchRadius + 1} tiles";

    /// <summary>
    /// Gets or sets the maximum live cache size in MB.
    /// </summary>
    [ObservableProperty]
    private int _maxLiveCacheSizeMB;

    /// <summary>
    /// Gets or sets the maximum trip cache size in MB.
    /// </summary>
    [ObservableProperty]
    private int _maxTripCacheSizeMB;

    /// <summary>
    /// Gets or sets the pending queue count.
    /// </summary>
    [ObservableProperty]
    private int _pendingQueueCount;

    /// <summary>
    /// Gets or sets whether the queue is being cleared.
    /// </summary>
    [ObservableProperty]
    private bool _isClearingQueue;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of SettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="appLockService">The app lock service.</param>
    /// <param name="databaseService">The database service.</param>
    public SettingsViewModel(ISettingsService settingsService, IAppLockService appLockService, DatabaseService databaseService)
    {
        _settingsService = settingsService;
        _appLockService = appLockService;
        _databaseService = databaseService;
        PinSecurity = new PinSecurityViewModel(appLockService);
        Title = "Settings";
        LoadSettings();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Loads settings from the service.
    /// </summary>
    private void LoadSettings()
    {
        TimelineTrackingEnabled = _settingsService.TimelineTrackingEnabled;
        ServerUrl = _settingsService.ServerUrl ?? string.Empty;
        LocationTimeThreshold = _settingsService.LocationTimeThresholdMinutes;
        LocationDistanceThreshold = _settingsService.LocationDistanceThresholdMeters;
        DarkModeEnabled = _settingsService.DarkModeEnabled;
        MapOfflineCacheEnabled = _settingsService.MapOfflineCacheEnabled;

        // Navigation settings
        NavigationAudioEnabled = _settingsService.NavigationAudioEnabled;
        NavigationVibrationEnabled = _settingsService.NavigationVibrationEnabled;
        AutoRerouteEnabled = _settingsService.AutoRerouteEnabled;
        DistanceUnits = _settingsService.DistanceUnits;

        // Battery settings
        ShowBatteryWarnings = _settingsService.ShowBatteryWarnings;
        AutoPauseTrackingOnCriticalBattery = _settingsService.AutoPauseTrackingOnCriticalBattery;

        // Cache settings
        LiveCachePrefetchRadius = _settingsService.LiveCachePrefetchRadius;
        MaxLiveCacheSizeMB = _settingsService.MaxLiveCacheSizeMB;
        MaxTripCacheSizeMB = _settingsService.MaxTripCacheSizeMB;

        UserEmail = _settingsService.UserEmail ?? string.Empty;
        IsLoggedIn = _settingsService.IsConfigured;

        var lastSync = _settingsService.LastSyncTime;
        LastSyncText = lastSync.HasValue
            ? lastSync.Value.ToLocalTime().ToString("g")
            : "Never";

        // Tracking mode description based on user's onboarding choice
        TrackingModeDescription = _settingsService.BackgroundTrackingEnabled
            ? "24/7 Background Tracking - Your location is tracked even when the app is closed."
            : "Foreground Only - Location is only tracked while the app is open.";
    }

    /// <summary>
    /// Saves timeline tracking setting.
    /// </summary>
    partial void OnTimelineTrackingEnabledChanged(bool value)
    {
        _settingsService.TimelineTrackingEnabled = value;
    }

    /// <summary>
    /// Saves dark mode setting.
    /// </summary>
    partial void OnDarkModeEnabledChanged(bool value)
    {
        _settingsService.DarkModeEnabled = value;
        ApplyTheme(value);
    }

    /// <summary>
    /// Saves offline cache setting.
    /// </summary>
    partial void OnMapOfflineCacheEnabledChanged(bool value)
    {
        _settingsService.MapOfflineCacheEnabled = value;
    }

    /// <summary>
    /// Saves navigation audio setting.
    /// </summary>
    partial void OnNavigationAudioEnabledChanged(bool value)
    {
        _settingsService.NavigationAudioEnabled = value;
    }

    /// <summary>
    /// Saves navigation vibration setting.
    /// </summary>
    partial void OnNavigationVibrationEnabledChanged(bool value)
    {
        _settingsService.NavigationVibrationEnabled = value;
    }

    /// <summary>
    /// Saves auto-reroute setting.
    /// </summary>
    partial void OnAutoRerouteEnabledChanged(bool value)
    {
        _settingsService.AutoRerouteEnabled = value;
    }

    /// <summary>
    /// Saves distance units setting.
    /// </summary>
    partial void OnDistanceUnitsChanged(string value)
    {
        _settingsService.DistanceUnits = value;
    }

    /// <summary>
    /// Saves show battery warnings setting.
    /// </summary>
    partial void OnShowBatteryWarningsChanged(bool value)
    {
        _settingsService.ShowBatteryWarnings = value;
    }

    /// <summary>
    /// Saves auto-pause on critical battery setting.
    /// </summary>
    partial void OnAutoPauseTrackingOnCriticalBatteryChanged(bool value)
    {
        _settingsService.AutoPauseTrackingOnCriticalBattery = value;
    }

    /// <summary>
    /// Saves prefetch radius setting.
    /// </summary>
    partial void OnLiveCachePrefetchRadiusChanged(int value)
    {
        _settingsService.LiveCachePrefetchRadius = value;
    }

    /// <summary>
    /// Saves max live cache size setting.
    /// </summary>
    partial void OnMaxLiveCacheSizeMBChanged(int value)
    {
        _settingsService.MaxLiveCacheSizeMB = value;
    }

    /// <summary>
    /// Saves max trip cache size setting.
    /// </summary>
    partial void OnMaxTripCacheSizeMBChanged(int value)
    {
        _settingsService.MaxTripCacheSizeMB = value;
    }

    /// <summary>
    /// Applies the theme change.
    /// </summary>
    private static void ApplyTheme(bool darkMode)
    {
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = darkMode ? AppTheme.Dark : AppTheme.Light;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Opens the QR scanner to configure the server.
    /// </summary>
    [RelayCommand]
    private async Task ScanQrCodeAsync()
    {
        // Navigate to QR scanner page
        await Shell.Current.GoToAsync("QrScanner");
    }

    /// <summary>
    /// Logs out the user.
    /// </summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Logout",
            "Are you sure you want to logout? Your pending locations will still be synced when you log back in.",
            "Logout",
            "Cancel");

        if (confirm)
        {
            _settingsService.ClearAuth();
            IsLoggedIn = false;
            UserEmail = string.Empty;
            await Shell.Current.DisplayAlertAsync("Logged Out", "You have been logged out.", "OK");
        }
    }

    /// <summary>
    /// Clears all app data.
    /// </summary>
    [RelayCommand]
    private async Task ClearDataAsync()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Clear All Data",
            "This will delete all local data including pending locations and settings. This cannot be undone.",
            "Clear",
            "Cancel");

        if (confirm)
        {
            _settingsService.Clear();
            LoadSettings();
            await Shell.Current.DisplayAlertAsync("Data Cleared", "All local data has been cleared.", "OK");
        }
    }

    /// <summary>
    /// Sets the distance units.
    /// </summary>
    [RelayCommand]
    private void SetDistanceUnits(string units)
    {
        DistanceUnits = units;
    }

    /// <summary>
    /// Opens the about page or shows app info.
    /// </summary>
    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        await Shell.Current.GoToAsync("about");
    }

    /// <summary>
    /// Opens the diagnostics page.
    /// </summary>
    [RelayCommand]
    private async Task ShowDiagnosticsAsync()
    {
        await Shell.Current.GoToAsync("diagnostics");
    }

    /// <summary>
    /// Reruns the onboarding setup wizard to change permissions or tracking mode.
    /// </summary>
    [RelayCommand]
    private async Task RerunSetupAsync()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Rerun Setup",
            "This will take you through the setup wizard again where you can change your permissions and tracking mode. Continue?",
            "Continue",
            "Cancel");

        if (confirm)
        {
            // Mark as first run so onboarding shows all steps
            _settingsService.IsFirstRun = true;

            // Navigate to onboarding
            await Shell.Current.GoToAsync("//onboarding");
        }
    }

    /// <summary>
    /// Clears the pending location queue.
    /// </summary>
    [RelayCommand]
    private async Task ClearQueueAsync()
    {
        if (PendingQueueCount == 0)
        {
            await Shell.Current.DisplayAlertAsync("Queue Empty", "There are no pending locations to clear.", "OK");
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "Clear Queue",
            $"This will delete {PendingQueueCount} pending locations that haven't been synced to the server. This cannot be undone.",
            "Clear",
            "Cancel");

        if (confirm)
        {
            IsClearingQueue = true;
            try
            {
                var deleted = await _databaseService.ClearPendingQueueAsync();
                PendingQueueCount = 0;
                await Shell.Current.DisplayAlertAsync("Queue Cleared", $"{deleted} pending locations have been deleted.", "OK");
            }
            finally
            {
                IsClearingQueue = false;
            }
        }
    }

    /// <summary>
    /// Refreshes the pending queue count.
    /// </summary>
    [RelayCommand]
    private async Task RefreshQueueCountAsync()
    {
        PendingQueueCount = await _databaseService.GetPendingCountAsync();
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Called when the view appears.
    /// </summary>
    public override async Task OnAppearingAsync()
    {
        LoadSettings();
        await PinSecurity.LoadSettingsAsync();
        await RefreshQueueCountAsync();
        await base.OnAppearingAsync();
    }

    #endregion
}
