using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// ViewModel for the settings page.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    #region Fields

    private readonly SettingsService _settingsService;
    private readonly IAppLockService _appLockService;

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

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of SettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="appLockService">The app lock service.</param>
    public SettingsViewModel(SettingsService settingsService, IAppLockService appLockService)
    {
        _settingsService = settingsService;
        _appLockService = appLockService;
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
        UserEmail = _settingsService.UserEmail ?? string.Empty;
        IsLoggedIn = _settingsService.IsConfigured;

        var lastSync = _settingsService.LastSyncTime;
        LastSyncText = lastSync.HasValue
            ? lastSync.Value.ToLocalTime().ToString("g")
            : "Never";
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
    /// Opens the about page or shows app info.
    /// </summary>
    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        await Shell.Current.DisplayAlertAsync(
            "WayfarerMobile",
            $"{AppVersion}\n\nA location tracking app for timeline recording.",
            "OK");
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
        await base.OnAppearingAsync();
    }

    #endregion
}
