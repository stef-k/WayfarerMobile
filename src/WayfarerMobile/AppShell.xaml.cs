using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;
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
    private readonly ISettingsService? _settingsService;
    private readonly IAppLockService? _appLockService;
    private readonly bool _recoveryMode;

    /// <summary>
    /// Creates a new instance of AppShell.
    /// </summary>
    /// <param name="serviceProvider">The service provider for DI.</param>
    public AppShell(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        // Try to resolve critical services with recovery fallback
        try
        {
            _settingsService = serviceProvider.GetRequiredService<ISettingsService>();
            _appLockService = serviceProvider.GetRequiredService<IAppLockService>();
            _recoveryMode = false;
        }
        catch (Exception ex)
        {
            // DI resolution failed - likely corrupted state after data clear
            Console.WriteLine($"[AppShell] Critical service resolution failed: {ex.Message}");
            Console.WriteLine("[AppShell] Entering recovery mode");

            _recoveryMode = true;

            // Try to recover by resetting settings
            TryRecoverFromCorruptedState(serviceProvider);

            // Try to get services again after recovery
            try
            {
                _settingsService = serviceProvider.GetService<ISettingsService>();
                _appLockService = serviceProvider.GetService<IAppLockService>();
            }
            catch
            {
                // Still failing - services will remain null, we'll handle in OnShellLoaded
                Console.WriteLine("[AppShell] Services still unavailable after recovery attempt");
            }
        }

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

        AboutContent.ContentTemplate = new DataTemplate(() =>
            serviceProvider.GetRequiredService<AboutPage>());

        // Navigate to appropriate starting point
        Loaded += OnShellLoaded;
    }

    /// <summary>
    /// Attempts to recover from corrupted app state by resetting settings to defaults.
    /// </summary>
    private static void TryRecoverFromCorruptedState(IServiceProvider serviceProvider)
    {
        try
        {
            // Try to get SettingsService and reset it
            var settings = serviceProvider.GetService<ISettingsService>();
            if (settings != null)
            {
                settings.ResetToDefaults();
                Console.WriteLine("[AppShell] Settings reset via service");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppShell] Could not reset via service: {ex.Message}");
        }

        // If we can't get the service, try direct reset
        try
        {
            Console.WriteLine("[AppShell] Attempting direct preferences/storage reset");
            Preferences.Clear();
            SecureStorage.Default.RemoveAll();
            Preferences.Set("is_first_run", true);
            Console.WriteLine("[AppShell] Direct reset complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppShell] Direct reset failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles shell loaded event to set initial navigation.
    /// </summary>
    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;

        // If in recovery mode or settings unavailable, go to onboarding
        if (_recoveryMode || _settingsService == null)
        {
            Console.WriteLine("[AppShell] Recovery mode or missing settings - navigating to onboarding");
            await GoToAsync("//onboarding");
            return;
        }

        if (_settingsService.IsFirstRun)
        {
            // Show onboarding for first run
            await GoToAsync("//onboarding");
        }
        else
        {
            // Show lock screen on cold start if protection is enabled
            // (we're already on main since it's the default)
            if (_appLockService != null && !_appLockService.IsAccessAllowed())
            {
                await GoToAsync("lockscreen");
            }
        }
    }
}
