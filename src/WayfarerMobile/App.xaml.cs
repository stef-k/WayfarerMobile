using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Services;

namespace WayfarerMobile;

/// <summary>
/// Main application class.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a new instance of the application.
    /// </summary>
    /// <param name="serviceProvider">The service provider for DI.</param>
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        // Initialize global exception handler first
        InitializeExceptionHandler();

        // Start background sync service
        StartBackgroundServices();

        // Initialize app lock service
        InitializeAppLockAsync();
    }

    /// <summary>
    /// Creates the main window.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = new AppShell(_serviceProvider);
        var window = new Window(shell);

        // Wire up lifecycle events for app lock
        window.Resumed += OnWindowResumed;
        window.Stopped += OnWindowStopped;

        return window;
    }

    /// <summary>
    /// Initializes the global exception handler.
    /// </summary>
    private void InitializeExceptionHandler()
    {
        try
        {
            var exceptionHandler = _serviceProvider.GetService<IExceptionHandlerService>();
            exceptionHandler?.Initialize();
            System.Diagnostics.Debug.WriteLine("[App] Global exception handler initialized");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to initialize exception handler: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts background services for the application.
    /// </summary>
    private void StartBackgroundServices()
    {
        try
        {
            var syncService = _serviceProvider.GetService<LocationSyncService>();
            syncService?.Start();
            System.Diagnostics.Debug.WriteLine("[App] Background sync service started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to start background services: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the app lock service on cold start.
    /// </summary>
    private async void InitializeAppLockAsync()
    {
        try
        {
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            if (appLockService != null)
            {
                await appLockService.InitializeAsync();
                System.Diagnostics.Debug.WriteLine("[App] App lock service initialized");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to initialize app lock service: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the app window is resumed from background.
    /// </summary>
    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        try
        {
            // Handle app lock
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.OnAppToForeground();

            // Handle app lifecycle (sync, state restoration)
            var lifecycleService = _serviceProvider.GetService<IAppLifecycleService>();
            if (lifecycleService != null)
            {
                await lifecycleService.OnResumingAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Error in OnWindowResumed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the app window goes to background.
    /// </summary>
    private async void OnWindowStopped(object? sender, EventArgs e)
    {
        try
        {
            // Handle app lock
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.OnAppToBackground();

            // Handle app lifecycle (state saving, sync flush)
            var lifecycleService = _serviceProvider.GetService<IAppLifecycleService>();
            if (lifecycleService != null)
            {
                await lifecycleService.OnSuspendingAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Error in OnWindowStopped: {ex.Message}");
        }
    }
}
