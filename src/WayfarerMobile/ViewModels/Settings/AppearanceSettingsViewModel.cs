using CommunityToolkit.Mvvm.ComponentModel;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for appearance settings including theme, screen, and battery options.
/// </summary>
public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    #region Observable Properties

    /// <summary>
    /// Gets or sets the theme preference: "System", "Light", or "Dark".
    /// </summary>
    [ObservableProperty]
    private string _themePreference = "System";

    /// <summary>
    /// Gets or sets whether to keep the screen on while the app is in the foreground.
    /// </summary>
    [ObservableProperty]
    private bool _keepScreenOn;

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
    /// Gets or sets a description of the current tracking mode.
    /// </summary>
    [ObservableProperty]
    private string _trackingModeDescription = string.Empty;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the available theme options.
    /// </summary>
    public List<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of AppearanceSettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    public AppearanceSettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads appearance settings from the service.
    /// </summary>
    public void LoadSettings()
    {
        // Use the string instance from ThemeOptions list to ensure Picker binding works
        var savedTheme = _settingsService.ThemePreference;
        ThemePreference = ThemeOptions.Find(t => t == savedTheme) ?? ThemeOptions[0];

        KeepScreenOn = _settingsService.KeepScreenOn;
        ShowBatteryWarnings = _settingsService.ShowBatteryWarnings;
        AutoPauseTrackingOnCriticalBattery = _settingsService.AutoPauseTrackingOnCriticalBattery;

        // Tracking mode description based on user's onboarding choice
        TrackingModeDescription = _settingsService.BackgroundTrackingEnabled
            ? "24/7 Background Tracking - Your location is tracked even when the app is closed."
            : "Foreground Only - Location is only tracked while the app is open.";
    }

    #endregion

    #region Property Changed Handlers

    /// <summary>
    /// Saves theme preference setting and applies it immediately.
    /// </summary>
    partial void OnThemePreferenceChanged(string value)
    {
        _settingsService.ThemePreference = value;
        ApplyTheme(value);
    }

    /// <summary>
    /// Saves keep screen on setting and applies it immediately.
    /// </summary>
    partial void OnKeepScreenOnChanged(bool value)
    {
        _settingsService.KeepScreenOn = value;
        ApplyKeepScreenOn(value);
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

    #endregion

    #region Helper Methods

    /// <summary>
    /// Applies the theme change based on preference.
    /// </summary>
    /// <param name="themePreference">The theme preference: "System", "Light", or "Dark".</param>
    public static void ApplyTheme(string themePreference)
    {
        if (Application.Current == null)
            return;

        Application.Current.UserAppTheme = themePreference switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified // "System" - follows device theme
        };
    }

    /// <summary>
    /// Applies the keep screen on setting immediately using MAUI's DeviceDisplay API.
    /// </summary>
    private static void ApplyKeepScreenOn(bool keepScreenOn)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DeviceDisplay.Current.KeepScreenOn = keepScreenOn;
        });
    }

    #endregion
}
