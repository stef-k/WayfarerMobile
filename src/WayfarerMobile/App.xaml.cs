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
            // Start location sync service (server sync)
            var syncService = _serviceProvider.GetService<LocationSyncService>();
            syncService?.Start();
            System.Diagnostics.Debug.WriteLine("[App] Background sync service started");

            // Note: Settings thresholds sync is handled by LocationTrackingService (foreground service)
            // which runs 24/7 and syncs every 6 hours reliably

            // Sync activity types if needed (UI data, fire-and-forget)
            var activitySyncService = _serviceProvider.GetService<IActivitySyncService>();
            _ = activitySyncService?.AutoSyncIfNeededAsync();

            // Start location tracking service (GPS) if permissions are already granted
            // This handles returning users who already completed onboarding
            _ = StartLocationTrackingServiceAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to start background services: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the location tracking service if permissions are granted.
    /// The service runs 24/7 with context switching between high/normal performance modes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>CRITICAL: Android Foreground Service Timing</strong>
    /// </para>
    /// <para>
    /// On Android 8.0+, after calling startForegroundService(), the service MUST call
    /// startForeground() within 5 seconds. However, during MAUI app startup, the main
    /// thread is heavily blocked loading assemblies and initializing UI components.
    /// </para>
    /// <para>
    /// If we call startForegroundService() during app construction, Android queues the
    /// service creation, but the main thread may remain blocked for 10+ seconds. When
    /// Android finally processes the service creation, the 5-second window has expired,
    /// causing a RemoteServiceException crash.
    /// </para>
    /// <para>
    /// <strong>Solution:</strong> Use MainThread.BeginInvokeOnMainThread() to queue the
    /// service start. This ensures startForegroundService() is called only after the
    /// current main thread work completes, so Android can process it immediately.
    /// </para>
    /// </remarks>
    private async Task StartLocationTrackingServiceAsync()
    {
        try
        {
            var settings = _serviceProvider.GetService<ISettingsService>();
            var permissions = _serviceProvider.GetService<IPermissionsService>();
            var locationBridge = _serviceProvider.GetService<ILocationBridge>();

            // Only start if user has completed onboarding and has permissions
            if (settings == null || permissions == null || locationBridge == null)
                return;

            if (settings.IsFirstRun)
            {
                System.Diagnostics.Debug.WriteLine("[App] First run - skipping location service start (will start after onboarding)");
                return;
            }

            var hasPermissions = await permissions.AreTrackingPermissionsGrantedAsync();
            if (!hasPermissions)
            {
                System.Diagnostics.Debug.WriteLine("[App] Location permissions not granted - skipping location service start");
                return;
            }

            // CRITICAL: Queue the service start to run after current main thread work completes.
            // This ensures startForegroundService() is called when the main thread is free,
            // allowing Android to immediately process OnCreate and call StartForeground()
            // within the required 5-second window.
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await locationBridge.StartAsync();
                    System.Diagnostics.Debug.WriteLine("[App] Location tracking service started (24/7 mode)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Failed to start location tracking service: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to start location tracking service: {ex.Message}");
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

            // Show lock screen if protection is enabled and session is locked
            if (appLockService != null && !appLockService.IsAccessAllowed() && !appLockService.IsPromptAwaiting)
            {
                await ShowLockScreenAsync();
            }

            // Check permissions health - user may have revoked in settings
            await CheckPermissionsHealthAsync();

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
    /// Checks if permissions and configuration are still valid.
    /// Redirects to onboarding if critical checks fail.
    /// </summary>
    private async Task CheckPermissionsHealthAsync()
    {
        try
        {
            var settings = _serviceProvider.GetService<ISettingsService>();
            var permissions = _serviceProvider.GetService<IPermissionsService>();
            var locationBridge = _serviceProvider.GetService<ILocationBridge>();

            // Skip check if first run (onboarding will handle)
            if (settings == null || permissions == null || settings.IsFirstRun)
                return;

            var hasLocationPermission = await permissions.IsLocationPermissionGrantedAsync();
            var isConfigured = settings.IsConfigured;

            // If permissions revoked or not configured, redirect to onboarding
            if (!hasLocationPermission || !isConfigured)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Health check failed - Permission: {hasLocationPermission}, Configured: {isConfigured}");

                // Stop the location service if running
                if (locationBridge != null && !hasLocationPermission)
                {
                    await locationBridge.StopAsync();
                }

                // Redirect to onboarding (only once per session)
                await RedirectToOnboardingAsync(!hasLocationPermission, !isConfigured);
            }
            else
            {
                // All OK - ensure service is running
                if (locationBridge != null && locationBridge.CurrentState != Core.Enums.TrackingState.Active)
                {
                    await locationBridge.StartAsync();
                    System.Diagnostics.Debug.WriteLine("[App] Health check: Restarted location service");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Permission health check error: {ex.Message}");
        }
    }

    private bool _healthCheckRedirectShown;

    /// <summary>
    /// Redirects to onboarding when critical checks fail.
    /// </summary>
    private async Task RedirectToOnboardingAsync(bool permissionRevoked, bool notConfigured)
    {
        if (_healthCheckRedirectShown)
            return;

        _healthCheckRedirectShown = true;

        try
        {
            var page = Windows.FirstOrDefault()?.Page;
            if (page == null)
                return;

            string message = permissionRevoked && notConfigured
                ? "Location permission was revoked and server configuration is missing. Please complete setup again."
                : permissionRevoked
                    ? "Location permission was revoked. Please grant permission to continue tracking."
                    : "Server configuration is missing. Please configure your server to continue.";

            var goToSetup = await page.DisplayAlertAsync(
                "Setup Required",
                message,
                "Go to Setup",
                "Later");

            if (goToSetup)
            {
                await Shell.Current.GoToAsync("//onboarding");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Failed to redirect to onboarding: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows the lock screen for PIN entry.
    /// </summary>
    private async Task ShowLockScreenAsync()
    {
        try
        {
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.SetPromptAwaiting(true);

            await Shell.Current.GoToAsync("lockscreen");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Error showing lock screen: {ex.Message}");

            // Reset prompt awaiting flag on error
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.SetPromptAwaiting(false);
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
