using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Represents a language option for the navigation voice guidance settings picker.
/// This is used for turn-by-turn voice navigation, not for changing the app display language.
/// </summary>
/// <param name="Code">The culture code (e.g., "en", "fr") or "System" for device default.</param>
/// <param name="DisplayName">The display name shown in the picker.</param>
public record LanguageOption(string Code, string DisplayName)
{
    /// <summary>
    /// Returns the display name for use in UI bindings.
    /// </summary>
    public override string ToString() => DisplayName;
}

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
    /// Gets or sets the theme preference: "System", "Light", or "Dark".
    /// </summary>
    [ObservableProperty]
    private string _themePreference = "System";

    /// <summary>
    /// Gets or sets whether to keep the screen on while the app is in the foreground.
    /// </summary>
    [ObservableProperty]
    private bool _keepScreenOn = true;

    /// <summary>
    /// Gets the available theme options.
    /// </summary>
    public List<string> ThemeOptions { get; } = ["System", "Light", "Dark"];

    /// <summary>
    /// Gets or sets the navigation voice guidance language preference.
    /// This is used for turn-by-turn voice guidance, not the app display language.
    /// </summary>
    [ObservableProperty]
    private string _languagePreference = "System";

    /// <summary>
    /// Gets the available language options for navigation voice guidance,
    /// dynamically retrieved from device-supported cultures.
    /// </summary>
    public List<LanguageOption> LanguageOptions { get; } = BuildLanguageOptions();

    /// <summary>
    /// Builds the list of available language options from device-supported cultures.
    /// These are used for navigation voice guidance language selection.
    /// </summary>
    private static List<LanguageOption> BuildLanguageOptions()
    {
        var options = new List<LanguageOption>
        {
            new("System", "System Default")
        };

        // Get all neutral cultures (languages without region specifics)
        var cultures = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !string.IsNullOrEmpty(c.Name) && c.Name != "iv") // Exclude invariant culture
            .OrderBy(c => c.NativeName)
            .ToList();

        foreach (var culture in cultures)
        {
            // Use native name for display (e.g., "Deutsch" for German, "Japanese" for Japanese)
            // Include English name in parentheses for clarity
            var displayName = culture.NativeName == culture.EnglishName
                ? culture.NativeName
                : $"{culture.NativeName} ({culture.EnglishName})";

            options.Add(new LanguageOption(culture.Name, displayName));
        }

        return options;
    }

    /// <summary>
    /// Gets or sets the selected language option for navigation voice guidance.
    /// </summary>
    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

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
        MapOfflineCacheEnabled = _settingsService.MapOfflineCacheEnabled;

        // Theme and language settings
        // Use the string instance from ThemeOptions list to ensure Picker binding works correctly
        var savedTheme = _settingsService.ThemePreference;
        ThemePreference = ThemeOptions.Find(t => t == savedTheme) ?? ThemeOptions[0]; // Default to "System"

        KeepScreenOn = _settingsService.KeepScreenOn;
        LanguagePreference = _settingsService.LanguagePreference;
        SelectedLanguageOption = LanguageOptions.Find(l => l.Code == LanguagePreference)
            ?? LanguageOptions[0]; // Default to "System"

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
    /// Saves theme preference setting and applies it immediately.
    /// </summary>
    partial void OnThemePreferenceChanged(string value)
    {
        _settingsService.ThemePreference = value;
        ApplyTheme(value);
    }

    /// <summary>
    /// Saves keep screen on setting and applies it immediately via wake lock service.
    /// </summary>
    partial void OnKeepScreenOnChanged(bool value)
    {
        _settingsService.KeepScreenOn = value;
        // The wake lock will be applied/released by AppLifecycleService on next resume
        // or immediately by the WakeLockService if we inject it here
        ApplyKeepScreenOn(value);
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

    /// <summary>
    /// Saves language preference setting when the selected option changes.
    /// </summary>
    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value != null)
        {
            LanguagePreference = value.Code;
            _settingsService.LanguagePreference = value.Code;
            ApplyLanguage(value.Code);
        }
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
    /// Stores the navigation language preference. This setting is used for turn-by-turn
    /// voice guidance only, not for changing the app's display language.
    /// The actual voice synthesis uses this preference when generating navigation instructions.
    /// </summary>
    /// <param name="languageCode">The language code (e.g., "en", "fr") or "System" for device default.</param>
    private static void ApplyLanguage(string languageCode)
    {
        // Note: This preference is stored and will be used by the navigation voice service
        // when generating turn-by-turn instructions. We do NOT change CultureInfo here
        // as this setting is only for navigation voice guidance, not the app UI.
        System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] Navigation voice language set to: {languageCode}");
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
    /// Exports the location queue to a CSV file and opens the share dialog.
    /// </summary>
    [RelayCommand]
    private async Task ExportQueueAsync()
    {
        try
        {
            var locations = await _databaseService.GetAllQueuedLocationsAsync();

            if (locations.Count == 0)
            {
                await Shell.Current.DisplayAlertAsync("No Data", "There are no locations to export.", "OK");
                return;
            }

            // Build CSV content
            var csv = new StringBuilder();

            // Header row
            csv.AppendLine("Id,Timestamp,Latitude,Longitude,Altitude,Accuracy,Speed,Bearing,Provider,SyncStatus,SyncAttempts,LastSyncAttempt,IsServerRejected,LastError,Notes");

            // Data rows
            foreach (var loc in locations)
            {
                var status = loc.SyncStatus switch
                {
                    SyncStatus.Pending => loc.IsServerRejected ? "ServerRejected" :
                                         loc.SyncAttempts >= 5 ? "Failed" :
                                         loc.SyncAttempts > 0 ? $"Retrying({loc.SyncAttempts})" : "Pending",
                    SyncStatus.Synced => "Synced",
                    SyncStatus.Failed => "Failed",
                    _ => "Unknown"
                };

                csv.AppendLine(
                    $"{loc.Id}," +
                    $"{loc.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                    $"{loc.Latitude:F6}," +
                    $"{loc.Longitude:F6}," +
                    $"{loc.Altitude?.ToString("F1") ?? ""}," +
                    $"{loc.Accuracy?.ToString("F1") ?? ""}," +
                    $"{loc.Speed?.ToString("F1") ?? ""}," +
                    $"{loc.Bearing?.ToString("F1") ?? ""}," +
                    $"\"{loc.Provider ?? ""}\"," +
                    $"{status}," +
                    $"{loc.SyncAttempts}," +
                    $"{(loc.LastSyncAttempt.HasValue ? loc.LastSyncAttempt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "")}," +
                    $"{loc.IsServerRejected}," +
                    $"\"{loc.LastError?.Replace("\"", "\"\"") ?? ""}\"," +
                    $"\"{loc.Notes?.Replace("\"", "\"\"") ?? ""}\"");
            }

            // Save to temp file
            var fileName = $"wayfarer_locations_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(tempPath, csv.ToString());

            // Share the file
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export Location Queue",
                File = new ShareFile(tempPath)
            });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Export Failed", $"Failed to export locations: {ex.Message}", "OK");
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
