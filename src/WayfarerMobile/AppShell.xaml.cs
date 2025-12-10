using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Views;
using WayfarerMobile.Views.Onboarding;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile;

/// <summary>
/// Application shell providing navigation structure.
/// </summary>
public partial class AppShell : Shell
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;
    private readonly IAppLockService _appLockService;

    /// <summary>
    /// Creates a new instance of AppShell.
    /// </summary>
    /// <param name="serviceProvider">The service provider for DI.</param>
    public AppShell(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
        _appLockService = serviceProvider.GetRequiredService<IAppLockService>();

        // Set up content templates to use DI
        OnboardingContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<OnboardingPage>());

        MainContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<MainPage>());

        TimelineContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<TimelinePage>());

        TripsContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<TripsPage>());

        GroupsContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<GroupsPage>());

        SettingsContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<SettingsPage>());

        // Navigate to appropriate starting point
        Loaded += OnShellLoaded;
    }

    /// <summary>
    /// Handles shell loaded event to set initial navigation.
    /// </summary>
    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;

        if (_settingsService.IsFirstRun)
        {
            // Show onboarding for first run
            await GoToAsync("//onboarding");
        }
        else
        {
            // Go directly to main app
            await GoToAsync("//main");

            // Show lock screen on cold start if protection is enabled
            if (!_appLockService.IsAccessAllowed())
            {
                await GoToAsync("lockscreen");
            }
        }
    }
}
