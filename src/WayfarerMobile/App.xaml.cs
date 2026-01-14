using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Helpers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.ViewModels;
using WayfarerMobile.ViewModels.Settings;

namespace WayfarerMobile;

/// <summary>
/// Main application class.
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<App> _logger;

    /// <summary>
    /// Tracks if this is the initial cold start. Set to false after first OnWindowResumed.
    /// Used to skip health check during cold start since StartBackgroundServices handles it.
    /// </summary>
    private bool _isColdStart = true;

    /// <summary>
    /// Guards against showing multiple lock screens due to rapid resume events.
    /// </summary>
    private bool _isShowingLockScreen;

    /// <summary>
    /// Creates a new instance of the application.
    /// </summary>
    /// <param name="serviceProvider">The service provider for DI.</param>
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<App>>();

        // Apply saved theme and language settings on startup
        ApplySavedSettings();

        // Pre-load notes viewer template for WebView display
        _ = NotesViewerHelper.PreloadTemplateAsync();

        // Initialize global exception handler first
        InitializeExceptionHandler();

        // Start background sync service
        StartBackgroundServices();

        // Initialize app lock service
        InitializeAppLockAsync();
    }

    /// <summary>
    /// Tracks whether the location service start has been triggered from Window.Activated.
    /// </summary>
    private bool _locationServiceStartTriggered;

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

        // CRITICAL: Start location service after UI is fully ready.
        // Window.Activated fires after first render when main thread is settling.
        // This is deterministic - no arbitrary delays needed.
        window.Activated += OnWindowActivatedForServiceStart;

        return window;
    }

    /// <summary>
    /// Handles Window.Activated to start the location service deterministically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This one-shot handler starts the location tracking service after the UI is fully
    /// initialized. By waiting for Window.Activated, we ensure:
    /// </para>
    /// <list type="bullet">
    ///   <item>MAUI framework initialization is complete</item>
    ///   <item>First render has occurred</item>
    ///   <item>Main thread is no longer blocked by startup work</item>
    /// </list>
    /// <para>
    /// This eliminates the Android 5-second foreground service timeout issue that can
    /// occur when startForegroundService() is called while the main thread is busy.
    /// </para>
    /// </remarks>
    private void OnWindowActivatedForServiceStart(object? sender, EventArgs e)
    {
        // One-shot: unsubscribe immediately
        if (sender is Window window)
        {
            window.Activated -= OnWindowActivatedForServiceStart;
        }

        // Prevent duplicate triggers
        if (_locationServiceStartTriggered)
            return;

        _locationServiceStartTriggered = true;

        // Queue to end of main thread work queue for maximum safety
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = StartLocationTrackingServiceAsync();
        });
    }

    /// <summary>
    /// Applies saved theme settings on app startup.
    /// Note: Language preference is only used for navigation voice guidance, not app UI.
    /// </summary>
    private void ApplySavedSettings()
    {
        try
        {
            var settings = _serviceProvider.GetService<ISettingsService>();
            if (settings == null)
                return;

            // Apply theme preference
            AppearanceSettingsViewModel.ApplyTheme(settings.ThemePreference);
            _logger.LogDebug("Applied theme: {Theme}", settings.ThemePreference);

            // Apply keep screen on setting
            ApplyKeepScreenOn(settings.KeepScreenOn);

            // Note: Language preference (LanguagePreference) is only for navigation voice guidance,
            // not for changing the app's display language. The navigation service will read this
            // setting when generating voice instructions.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply saved settings");
        }
    }

    /// <summary>
    /// Applies the keep screen on setting using the cross-platform MAUI API.
    /// </summary>
    /// <param name="keepScreenOn">Whether to keep the screen on.</param>
    private static void ApplyKeepScreenOn(bool keepScreenOn)
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = keepScreenOn;
            // Note: No logging here - static method, called frequently on resume
        }
        catch
        {
            // Silently ignore - not critical functionality
        }
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
            _logger.LogDebug("Global exception handler initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize exception handler");
        }
    }

    /// <summary>
    /// Starts background services for the application.
    /// </summary>
    private void StartBackgroundServices()
    {
        try
        {
            // Pre-load secure settings to avoid blocking SecureStorage calls later
            // This must happen early to prevent deadlocks on Android when API calls access ServerUrl/ApiToken
            var settings = _serviceProvider.GetService<ISettingsService>() as SettingsService;
            if (settings != null)
            {
                _ = settings.PreloadSecureSettingsAsync();
                _logger.LogDebug("Secure settings preload started");
            }

            // Start queue drain service (offline queue sync via check-in endpoint)
            var queueDrainService = _serviceProvider.GetService<QueueDrainService>();
            SafeFireAndForget(queueDrainService?.StartAsync(), "QueueDrainService");
            _logger.LogDebug("Queue drain service started");

            // Initialize local timeline storage service (subscribes to location events)
            var timelineStorageService = _serviceProvider.GetService<LocalTimelineStorageService>();
            SafeFireAndForget(timelineStorageService?.InitializeAsync(), "LocalTimelineStorageService");
            _logger.LogDebug("Local timeline storage service initialization started");

            // Note: Settings sync is handled by SettingsSyncService, triggered opportunistically
            // in AppLifecycleService.OnResumingAsync() with a 6-hour minimum interval

            // Sync activity types if needed (UI data, fire-and-forget)
            var activitySyncService = _serviceProvider.GetService<IActivitySyncService>();
            SafeFireAndForget(activitySyncService?.AutoSyncIfNeededAsync(), "ActivitySyncService");

            // Start visit notification service if enabled (subscribes to SSE visit events)
            var visitNotificationService = _serviceProvider.GetService<IVisitNotificationService>();
            SafeFireAndForget(visitNotificationService?.StartAsync(), "VisitNotificationService");
            _logger.LogDebug("Visit notification service initialization started");

            // Note: Location tracking service start is handled by OnWindowActivatedForServiceStart
            // to ensure deterministic timing after UI is fully initialized.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start background services");
        }
    }

    /// <summary>
    /// Starts the location tracking service if permissions are granted.
    /// The service runs 24/7 with context switching between high/normal performance modes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is called from <see cref="OnWindowActivatedForServiceStart"/> after
    /// the UI is fully initialized and the main thread is free. This ensures the Android
    /// 5-second foreground service timeout is never violated.
    /// </para>
    /// <para>
    /// The service acts as a defensive restart for edge cases where:
    /// </para>
    /// <list type="bullet">
    ///   <item>User force-stopped the app</item>
    ///   <item>Android killed the service (battery optimization, low memory)</item>
    ///   <item>Service crashed</item>
    /// </list>
    /// <para>
    /// If the service is already running, calling StartAsync() is a no-op - the service
    /// receives the intent and ignores it since it's already active.
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
                _logger.LogDebug("First run - skipping location service start (will start after onboarding)");
                return;
            }

            var hasPermissions = await permissions.AreTrackingPermissionsGrantedAsync();
            if (!hasPermissions)
            {
                _logger.LogDebug("Location permissions not granted - skipping location service start");
                return;
            }

            // No delay needed - this method is called from Window.Activated via
            // MainThread.BeginInvokeOnMainThread(), ensuring the main thread is free.
            await locationBridge.StartAsync();
            _logger.LogInformation("Location tracking service started (24/7 mode)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start location tracking service");
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
                _logger.LogDebug("App lock service initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize app lock service");
        }
    }

    /// <summary>
    /// Called when the app window is resumed from background.
    /// </summary>
    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        try
        {
            // Check for corrupted state (e.g., user cleared data while app was backgrounded)
            if (await CheckAndRecoverFromCorruptedStateAsync())
            {
                // State was corrupted and recovered - navigate to onboarding
                return;
            }

            // Handle app lock
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.OnAppToForeground();

            // Show lock screen if protection is enabled and session is locked
            // Guard against rapid resume events that could show multiple lock screens
            if (appLockService != null &&
                !appLockService.IsAccessAllowed() &&
                !appLockService.IsPromptAwaiting &&
                !_isShowingLockScreen)
            {
                await ShowLockScreenAsync();
            }

            // Skip health check on cold start - StartBackgroundServices already handles service startup.
            // This prevents race conditions and duplicate startForegroundService() calls.
            if (_isColdStart)
            {
                _isColdStart = false;
                _logger.LogDebug("Skipping health check on cold start");
            }
            else
            {
                // Check permissions health - user may have revoked in settings while app was in background
                await CheckPermissionsHealthAsync();
            }

            // Handle app lifecycle (sync, state restoration)
            var lifecycleService = _serviceProvider.GetService<IAppLifecycleService>();
            if (lifecycleService != null)
            {
                await lifecycleService.OnResumingAsync();
            }

            // Re-apply keep screen on setting when resuming
            // (Android may reset this when app goes to background)
            var settings = _serviceProvider.GetService<ISettingsService>();
            if (settings != null)
            {
                ApplyKeepScreenOn(settings.KeepScreenOn);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnWindowResumed");
        }
    }

    /// <summary>
    /// Checks for corrupted app state and attempts recovery.
    /// This handles the case where the user clears app data while the app is backgrounded.
    /// </summary>
    /// <returns>True if state was corrupted and recovery was triggered, false otherwise.</returns>
    private async Task<bool> CheckAndRecoverFromCorruptedStateAsync()
    {
        try
        {
            // Try to access settings - this validates Preferences/SecureStorage are accessible
            var settings = _serviceProvider.GetService<ISettingsService>();
            if (settings == null)
            {
                _logger.LogWarning("Settings service unavailable - triggering recovery");
                await TriggerRecoveryAsync();
                return true;
            }

            // Use a canary value to detect if Preferences were cleared while we were backgrounded.
            // We set this canary after successful app start. If it's missing but IsFirstRun is false
            // (meaning we thought we were configured), then data was cleared externally.
            const string canaryKey = "app_state_canary";
            var canaryValue = Preferences.Get(canaryKey, (string?)null);
            var isFirstRun = settings.IsFirstRun;

            // If canary is missing but we're not on first run, we need to determine:
            // 1. Corruption: data was cleared while app was backgrounded
            // 2. Normal: first resume after completing onboarding (canary not set yet)
            //
            // To distinguish, check if configuration is actually valid
            if (canaryValue == null && !isFirstRun)
            {
                // Check if we actually have valid configuration
                // If IsConfigured is false but IsFirstRun is also false, state is corrupted
                if (!settings.IsConfigured)
                {
                    _logger.LogWarning("State corruption detected (not configured, not first run, no canary) - triggering recovery");
                    settings.ResetToDefaults();
                    await TriggerRecoveryAsync();
                    return true;
                }

                // State is valid - set canary for future detection
                _logger.LogDebug("Setting state canary after successful state validation");
                Preferences.Set(canaryKey, "active");
            }

            // If not first run and canary exists, state is valid
            // If first run, wait for onboarding to complete before setting canary

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "State check failed with exception - triggering recovery");

            try
            {
                var settings = _serviceProvider.GetService<ISettingsService>();
                settings?.ResetToDefaults();
            }
            catch
            {
                // Direct reset if service unavailable
                try
                {
                    Preferences.Clear();
                    SecureStorage.Default.RemoveAll();
                    Preferences.Set("is_first_run", true);
                }
                catch
                {
                    // Last resort - just navigate to onboarding
                }
            }

            await TriggerRecoveryAsync();
            return true;
        }
    }

    /// <summary>
    /// Triggers recovery by navigating to onboarding.
    /// </summary>
    private async Task TriggerRecoveryAsync()
    {
        try
        {
            _logger.LogInformation("Triggering recovery - navigating to onboarding");

            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("//onboarding");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to onboarding during recovery");
        }
    }

    /// <summary>
    /// Checks if permissions and configuration are still valid.
    /// Redirects to onboarding if critical checks fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method only DETECTS permission/configuration issues and redirects to onboarding.
    /// It does NOT start the location service - onboarding is the single authority for that.
    /// This design eliminates race conditions during cold start.
    /// </para>
    /// <para>
    /// The check compares the user's stored choice (BackgroundTrackingEnabled) against
    /// actual runtime permissions to detect if a 24/7 tracking user revoked background permission.
    /// </para>
    /// </remarks>
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
            var hasBackgroundPermission = await permissions.IsBackgroundLocationPermissionGrantedAsync();
            var isConfigured = settings.IsConfigured;

            // Check 1: Basic location permission or configuration missing
            if (!hasLocationPermission || !isConfigured)
            {
                _logger.LogWarning("Health check failed - Permission: {HasPermission}, Configured: {IsConfigured}", hasLocationPermission, isConfigured);

                // Stop the location service if running (permission revoked)
                if (locationBridge != null && !hasLocationPermission)
                {
                    await locationBridge.StopAsync();
                }

                // Redirect to onboarding (only once per session)
                await RedirectToOnboardingAsync(!hasLocationPermission, !isConfigured);
                return;
            }

            // Check 2: User had 24/7 tracking but background permission was revoked
            if (settings.BackgroundTrackingEnabled && !hasBackgroundPermission)
            {
                _logger.LogWarning("Health check: Background permission revoked by 24/7 tracking user");

                // Redirect to onboarding to re-grant or downgrade to casual use
                await RedirectToOnboardingAsync(backgroundPermissionRevoked: true, notConfigured: false);
                return;
            }

            // All OK - do NOT restart service here to avoid race conditions
            // Service is started by:
            // 1. Onboarding (when user completes setup)
            // 2. StartBackgroundServices() on cold start (if already configured)
            _logger.LogDebug("Health check passed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Permission health check error");
        }
    }

    private bool _healthCheckRedirectShown;

    /// <summary>
    /// Redirects to onboarding when critical checks fail.
    /// </summary>
    /// <param name="permissionRevoked">True if basic location permission was revoked.</param>
    /// <param name="notConfigured">True if server configuration is missing.</param>
    /// <param name="backgroundPermissionRevoked">True if background permission was revoked by a 24/7 tracking user.</param>
    private async Task RedirectToOnboardingAsync(bool permissionRevoked = false, bool notConfigured = false, bool backgroundPermissionRevoked = false)
    {
        if (_healthCheckRedirectShown)
            return;

        _healthCheckRedirectShown = true;

        try
        {
            var page = Windows.FirstOrDefault()?.Page;
            if (page == null)
                return;

            string message;
            if (permissionRevoked && notConfigured)
            {
                message = "Location permission was revoked and server configuration is missing. Please complete setup again.";
            }
            else if (permissionRevoked)
            {
                message = "Location permission was revoked. Please grant permission to continue using location features.";
            }
            else if (backgroundPermissionRevoked)
            {
                message = "Background location permission was revoked. Your timeline won't track while the app is closed. Would you like to re-enable 24/7 tracking or continue with foreground-only mode?";
            }
            else if (notConfigured)
            {
                message = "Server configuration is missing. Please configure your server to continue.";
            }
            else
            {
                return; // No issue detected
            }

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
            _logger.LogError(ex, "Failed to redirect to onboarding");
        }
    }

    /// <summary>
    /// Shows the lock screen for PIN entry.
    /// Uses _isShowingLockScreen flag to prevent duplicate navigations.
    /// </summary>
    private async Task ShowLockScreenAsync()
    {
        // Double-check guard to prevent race conditions
        if (_isShowingLockScreen)
            return;

        _isShowingLockScreen = true;

        try
        {
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.SetPromptAwaiting(true);

            // Check if we're already on the lock screen to prevent stacking
            var currentRoute = Shell.Current?.CurrentState?.Location?.OriginalString ?? "";
            if (currentRoute.Contains("lockscreen", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Already on lock screen, skipping navigation");
                return;
            }

            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("lockscreen");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing lock screen");

            // Reset prompt awaiting flag on error
            var appLockService = _serviceProvider.GetService<IAppLockService>();
            appLockService?.SetPromptAwaiting(false);
        }
        finally
        {
            _isShowingLockScreen = false;
        }
    }

    /// <summary>
    /// Executes an async task in fire-and-forget mode with proper exception logging.
    /// Ensures exceptions are observed and logged rather than becoming unobserved task exceptions.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="serviceName">Name of the service for logging context.</param>
    private async void SafeFireAndForget(Task? task, string serviceName)
    {
        if (task == null) return;

        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{ServiceName} initialization failed", serviceName);
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
            _logger.LogError(ex, "Error in OnWindowStopped");
        }
    }
}
