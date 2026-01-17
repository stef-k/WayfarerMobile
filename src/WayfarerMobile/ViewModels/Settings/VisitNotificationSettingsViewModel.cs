using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.ViewModels.Settings;

/// <summary>
/// ViewModel for visit notification settings including style and voice options.
/// </summary>
public partial class VisitNotificationSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IVisitNotificationService _visitNotificationService;

    #region Observable Properties

    /// <summary>
    /// Gets or sets whether visit notifications are enabled.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVisitVoiceOption))]
    private bool _visitNotificationsEnabled;

    /// <summary>
    /// Gets or sets the visit notification style (notification, voice, both).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisitStyleNotification))]
    [NotifyPropertyChangedFor(nameof(IsVisitStyleVoice))]
    [NotifyPropertyChangedFor(nameof(IsVisitStyleBoth))]
    [NotifyPropertyChangedFor(nameof(ShowVisitVoiceOption))]
    private string _visitNotificationStyle = "notification";

    /// <summary>
    /// Gets or sets whether voice announcements are enabled for visit notifications.
    /// </summary>
    [ObservableProperty]
    private bool _visitVoiceAnnouncementEnabled;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets whether notification style is selected.
    /// </summary>
    public bool IsVisitStyleNotification => VisitNotificationStyle == "notification";

    /// <summary>
    /// Gets whether voice style is selected.
    /// </summary>
    public bool IsVisitStyleVoice => VisitNotificationStyle == "voice";

    /// <summary>
    /// Gets whether both style is selected.
    /// </summary>
    public bool IsVisitStyleBoth => VisitNotificationStyle == "both";

    /// <summary>
    /// Gets whether to show voice announcement option (when style includes voice).
    /// </summary>
    public bool ShowVisitVoiceOption => VisitNotificationsEnabled &&
        (VisitNotificationStyle == "voice" || VisitNotificationStyle == "both");

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of VisitNotificationSettingsViewModel.
    /// </summary>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="visitNotificationService">The visit notification service.</param>
    public VisitNotificationSettingsViewModel(
        ISettingsService settingsService,
        IVisitNotificationService visitNotificationService)
    {
        _settingsService = settingsService;
        _visitNotificationService = visitNotificationService;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads visit notification settings from the service.
    /// </summary>
    public void LoadSettings()
    {
        VisitNotificationsEnabled = _settingsService.VisitNotificationsEnabled;
        VisitNotificationStyle = _settingsService.VisitNotificationStyle;
        VisitVoiceAnnouncementEnabled = _settingsService.VisitVoiceAnnouncementEnabled;
    }

    #endregion

    #region Commands

    /// <summary>
    /// Sets the visit notification style.
    /// </summary>
    [RelayCommand]
    private void SetVisitNotificationStyle(string style)
    {
        VisitNotificationStyle = style;
    }

    #endregion

    #region Property Changed Handlers

    /// <summary>
    /// Saves visit notifications enabled setting and starts/stops the service.
    /// </summary>
    partial void OnVisitNotificationsEnabledChanged(bool value)
    {
        _settingsService.VisitNotificationsEnabled = value;

        // Start or stop the visit notification service immediately
        if (value)
        {
            _ = _visitNotificationService.StartAsync();
        }
        else
        {
            _visitNotificationService.Stop();
        }
    }

    /// <summary>
    /// Saves visit notification style setting.
    /// </summary>
    partial void OnVisitNotificationStyleChanged(string value)
    {
        _settingsService.VisitNotificationStyle = value;
    }

    /// <summary>
    /// Saves visit voice announcement setting.
    /// </summary>
    partial void OnVisitVoiceAnnouncementEnabledChanged(bool value)
    {
        _settingsService.VisitVoiceAnnouncementEnabled = value;
    }

    #endregion
}
