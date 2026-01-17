using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for navigation settings including voice guidance, audio, and distance units.
/// </summary>
public partial class NavigationSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    #region Observable Properties

    /// <summary>
    /// Gets or sets the navigation voice guidance language preference.
    /// </summary>
    [ObservableProperty]
    private string _languagePreference = "System";

    /// <summary>
    /// Gets or sets the selected language option for navigation voice guidance.
    /// </summary>
    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    /// <summary>
    /// Gets or sets whether navigation audio is enabled.
    /// </summary>
    [ObservableProperty]
    private bool _navigationAudioEnabled;

    /// <summary>
    /// Gets or sets the navigation voice volume (0.0-1.0).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationVolumePercent))]
    private float _navigationVolume = 1.0f;

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

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the available language options for navigation voice guidance.
    /// </summary>
    public List<LanguageOption> LanguageOptions { get; } = BuildLanguageOptions();

    /// <summary>
    /// Gets the navigation volume as a percentage for display.
    /// </summary>
    public int NavigationVolumePercent => (int)(NavigationVolume * 100);

    /// <summary>
    /// Gets whether kilometers is selected.
    /// </summary>
    public bool IsKilometers => DistanceUnits == "kilometers";

    /// <summary>
    /// Gets whether miles is selected.
    /// </summary>
    public bool IsMiles => DistanceUnits == "miles";

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of NavigationSettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    public NavigationSettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads navigation settings from the service.
    /// </summary>
    public void LoadSettings()
    {
        LanguagePreference = _settingsService.LanguagePreference;
        SelectedLanguageOption = LanguageOptions.Find(l => l.Code == LanguagePreference)
            ?? LanguageOptions[0]; // Default to "System"

        NavigationAudioEnabled = _settingsService.NavigationAudioEnabled;
        NavigationVibrationEnabled = _settingsService.NavigationVibrationEnabled;
        NavigationVolume = _settingsService.NavigationVolume;
        AutoRerouteEnabled = _settingsService.AutoRerouteEnabled;
        DistanceUnits = _settingsService.DistanceUnits;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Sets the distance units.
    /// </summary>
    [RelayCommand]
    private void SetDistanceUnits(string units)
    {
        DistanceUnits = units;
    }

    #endregion

    #region Property Changed Handlers

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
    /// Saves navigation volume setting.
    /// </summary>
    partial void OnNavigationVolumeChanged(float value)
    {
        _settingsService.NavigationVolume = value;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds the list of available language options from device-supported cultures.
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
            // Use native name for display (e.g., "Deutsch" for German)
            var displayName = culture.NativeName == culture.EnglishName
                ? culture.NativeName
                : $"{culture.NativeName} ({culture.EnglishName})";

            options.Add(new LanguageOption(culture.Name, displayName));
        }

        return options;
    }

    /// <summary>
    /// Logs the navigation language preference change.
    /// </summary>
    private static void ApplyLanguage(string languageCode)
    {
        // Note: This preference is stored and will be used by the navigation voice service
        // when generating turn-by-turn instructions.
        Console.WriteLine($"[NavigationSettingsViewModel] Navigation voice language set to: {languageCode}");
    }

    #endregion
}
