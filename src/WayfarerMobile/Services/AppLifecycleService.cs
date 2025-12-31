using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for handling application lifecycle events.
/// Manages state persistence and sync queue resumption.
/// </summary>
public class AppLifecycleService : IAppLifecycleService
{
    private readonly LocationSyncService _syncService;
    private readonly SettingsSyncService _settingsSyncService;
    private readonly IWakeLockService _wakeLockService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AppLifecycleService> _logger;

    private const string NavigationStateKey = "navigation_state_snapshot";
    private NavigationStateSnapshot? _cachedState;

    /// <summary>
    /// Event raised when the app is about to go to the background.
    /// </summary>
    public event EventHandler? AppSuspending;

    /// <summary>
    /// Event raised when the app is resuming from the background.
    /// </summary>
    public event EventHandler? AppResuming;

    /// <summary>
    /// Creates a new instance of AppLifecycleService.
    /// </summary>
    /// <param name="syncService">The location sync service.</param>
    /// <param name="settingsSyncService">The settings sync service.</param>
    /// <param name="wakeLockService">The wake lock service.</param>
    /// <param name="settingsService">The settings service.</param>
    /// <param name="logger">The logger instance.</param>
    public AppLifecycleService(
        LocationSyncService syncService,
        SettingsSyncService settingsSyncService,
        IWakeLockService wakeLockService,
        ISettingsService settingsService,
        ILogger<AppLifecycleService> logger)
    {
        _syncService = syncService;
        _settingsSyncService = settingsSyncService;
        _wakeLockService = wakeLockService;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Called when the app is going to the background.
    /// Saves state and prepares for suspension.
    /// </summary>
    public async Task OnSuspendingAsync()
    {
        _logger.LogInformation("App suspending - saving state");

        try
        {
            // Notify subscribers
            AppSuspending?.Invoke(this, EventArgs.Empty);

            // Trigger a sync to push any pending locations before suspend
            var pendingCount = await _syncService.GetPendingCountAsync();
            if (pendingCount > 0)
            {
                _logger.LogDebug("Syncing {Count} pending locations before suspend", pendingCount);
                await _syncService.SyncAsync();
            }

            // Release any wake locks (screen can turn off in background)
            if (_wakeLockService.IsWakeLockHeld)
            {
                _wakeLockService.ReleaseWakeLock();
                _logger.LogDebug("Wake lock released on suspend");
            }

            _logger.LogInformation("App state saved successfully");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "App suspension cancelled");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation during app suspension");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during app suspension");
        }
    }

    /// <summary>
    /// Called when the app is resuming from the background.
    /// Restores state and resumes operations.
    /// </summary>
    public async Task OnResumingAsync()
    {
        _logger.LogInformation("App resuming - restoring state");

        try
        {
            // Notify subscribers
            AppResuming?.Invoke(this, EventArgs.Empty);

            // Restart sync service if it was stopped
            _syncService.Start();

            // Trigger a sync to push any locations accumulated in background
            var pendingCount = await _syncService.GetPendingCountAsync();
            if (pendingCount > 0)
            {
                _logger.LogDebug("Syncing {Count} pending locations on resume", pendingCount);
                _ = _syncService.SyncAsync(); // Fire and forget
            }

            // Sync settings from server if due (6-hour interval)
            _ = _settingsSyncService.SyncIfDueAsync(); // Fire and forget

            // Acquire wake lock if user setting is enabled (keeps screen on while app is in foreground)
            if (_settingsService.KeepScreenOn)
            {
                _wakeLockService.AcquireWakeLock(keepScreenOn: true);
                _logger.LogDebug("Wake lock acquired (KeepScreenOn setting enabled)");
            }

            _logger.LogInformation("App state restored successfully");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "App resume cancelled");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation during app resume");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during app resume");
        }
    }

    /// <summary>
    /// Saves navigation state for persistence.
    /// </summary>
    /// <param name="state">The navigation state to save.</param>
    public void SaveNavigationState(NavigationStateSnapshot state)
    {
        try
        {
            state.SavedAt = DateTime.UtcNow;
            _cachedState = state;

            var json = JsonSerializer.Serialize(state);
            Preferences.Set(NavigationStateKey, json);

            _logger.LogDebug("Navigation state saved: {State}", json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON error saving navigation state");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save navigation state");
        }
    }

    /// <summary>
    /// Gets the last saved navigation state, if any.
    /// </summary>
    public NavigationStateSnapshot? GetSavedNavigationState()
    {
        // Return cached state if available
        if (_cachedState != null)
            return _cachedState;

        try
        {
            var json = Preferences.Get(NavigationStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;

            _cachedState = JsonSerializer.Deserialize<NavigationStateSnapshot>(json);

            // Discard state older than 4 hours
            if (_cachedState?.SavedAt < DateTime.UtcNow.AddHours(-4))
            {
                ClearNavigationState();
                return null;
            }

            return _cachedState;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON error loading navigation state");
            ClearNavigationState();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load navigation state");
            return null;
        }
    }

    /// <summary>
    /// Clears any saved navigation state.
    /// </summary>
    public void ClearNavigationState()
    {
        _cachedState = null;
        Preferences.Remove(NavigationStateKey);
        _logger.LogDebug("Navigation state cleared");
    }
}
