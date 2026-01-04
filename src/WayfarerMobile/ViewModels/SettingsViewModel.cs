using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.ViewModels.Settings;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Represents a language option for the navigation voice guidance settings picker.
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
/// Delegates domain-specific settings to focused child ViewModels.
/// </summary>
public partial class SettingsViewModel : BaseViewModel
{
    #region Fields

    private readonly ISettingsService _settingsService;
    private readonly IAppLockService _appLockService;
    private readonly DatabaseService _databaseService;
    private readonly ILocationBridge _locationBridge;

    #endregion

    #region Child ViewModels

    /// <summary>
    /// Gets the navigation settings view model.
    /// </summary>
    public NavigationSettingsViewModel NavigationSettings { get; }

    /// <summary>
    /// Gets the cache settings view model.
    /// </summary>
    public CacheSettingsViewModel CacheSettings { get; }

    /// <summary>
    /// Gets the visit notification settings view model.
    /// </summary>
    public VisitNotificationSettingsViewModel VisitNotificationSettings { get; }

    /// <summary>
    /// Gets the appearance settings view model.
    /// </summary>
    public AppearanceSettingsViewModel AppearanceSettings { get; }

    /// <summary>
    /// Gets the timeline data view model.
    /// </summary>
    public TimelineDataViewModel TimelineData { get; }

    /// <summary>
    /// Gets the PIN security view model for the security section.
    /// </summary>
    public PinSecurityViewModel PinSecurity { get; }

    #endregion

    #region Properties - Account & Core Settings (kept in parent)

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
    public SettingsViewModel(
        ISettingsService settingsService,
        IAppLockService appLockService,
        DatabaseService databaseService,
        ILocationBridge locationBridge,
        NavigationSettingsViewModel navigationSettings,
        CacheSettingsViewModel cacheSettings,
        VisitNotificationSettingsViewModel visitNotificationSettings,
        AppearanceSettingsViewModel appearanceSettings,
        TimelineDataViewModel timelineData)
    {
        _settingsService = settingsService;
        _appLockService = appLockService;
        _databaseService = databaseService;
        _locationBridge = locationBridge;

        // Child ViewModels
        NavigationSettings = navigationSettings;
        CacheSettings = cacheSettings;
        VisitNotificationSettings = visitNotificationSettings;
        AppearanceSettings = appearanceSettings;
        TimelineData = timelineData;
        PinSecurity = new PinSecurityViewModel(appLockService);

        // Wire up property change forwarding for XAML bindings
        NavigationSettings.PropertyChanged += OnNavigationSettingsPropertyChanged;
        CacheSettings.PropertyChanged += OnCacheSettingsPropertyChanged;
        VisitNotificationSettings.PropertyChanged += OnVisitNotificationSettingsPropertyChanged;
        AppearanceSettings.PropertyChanged += OnAppearanceSettingsPropertyChanged;
        TimelineData.PropertyChanged += OnTimelineDataPropertyChanged;

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
        // Core settings (kept in parent)
        TimelineTrackingEnabled = _settingsService.TimelineTrackingEnabled;
        ServerUrl = _settingsService.ServerUrl ?? string.Empty;
        LocationTimeThreshold = _settingsService.LocationTimeThresholdMinutes;
        LocationDistanceThreshold = _settingsService.LocationDistanceThresholdMeters;

        IsLoggedIn = _settingsService.IsConfigured;

        var lastSync = _settingsService.LastSyncTime;
        LastSyncText = lastSync.HasValue
            ? lastSync.Value.ToLocalTime().ToString("g")
            : "Never";

        // Delegate to child ViewModels
        NavigationSettings.LoadSettings();
        CacheSettings.LoadSettings();
        VisitNotificationSettings.LoadSettings();
        AppearanceSettings.LoadSettings();
    }

    /// <summary>
    /// Saves timeline tracking setting and toggles tracking service.
    /// </summary>
    partial void OnTimelineTrackingEnabledChanged(bool value)
    {
        _settingsService.TimelineTrackingEnabled = value;

        // Actually start/stop the tracking service
        _ = ToggleTrackingServiceAsync(value);
    }

    /// <summary>
    /// Starts or stops the tracking service based on the enabled state.
    /// </summary>
    private async Task ToggleTrackingServiceAsync(bool enabled)
    {
        try
        {
            if (enabled)
            {
                await _locationBridge.StartAsync();
            }
            else
            {
                await _locationBridge.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SettingsViewModel] Failed to toggle tracking: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Account & Navigation

    /// <summary>
    /// Opens the QR scanner to configure the server.
    /// </summary>
    [RelayCommand]
    private async Task ScanQrCodeAsync()
    {
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
    /// Opens the about page.
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
    /// Reruns the onboarding setup wizard.
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
            _settingsService.IsFirstRun = true;
            await Shell.Current.GoToAsync("//onboarding");
        }
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
        await TimelineData.RefreshQueueCountAsync();
        await TimelineData.RefreshTimelineCountAsync();
        await base.OnAppearingAsync();
    }

    /// <summary>
    /// Cleans up event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void Cleanup()
    {
        // Unsubscribe from child ViewModel property changes
        // Note: Child VMs inherit from ObservableObject, not BaseViewModel,
        // so they don't need Dispose() - only event unsubscription matters.
        NavigationSettings.PropertyChanged -= OnNavigationSettingsPropertyChanged;
        CacheSettings.PropertyChanged -= OnCacheSettingsPropertyChanged;
        VisitNotificationSettings.PropertyChanged -= OnVisitNotificationSettingsPropertyChanged;
        AppearanceSettings.PropertyChanged -= OnAppearanceSettingsPropertyChanged;
        TimelineData.PropertyChanged -= OnTimelineDataPropertyChanged;

        base.Cleanup();
    }

    #endregion

    #region Property Change Handlers

    private void OnNavigationSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged($"NavigationSettings.{e.PropertyName}");

    private void OnCacheSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged($"CacheSettings.{e.PropertyName}");

    private void OnVisitNotificationSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged($"VisitNotificationSettings.{e.PropertyName}");

    private void OnAppearanceSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged($"AppearanceSettings.{e.PropertyName}");

    private void OnTimelineDataPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => OnPropertyChanged($"TimelineData.{e.PropertyName}");

    #endregion
}
