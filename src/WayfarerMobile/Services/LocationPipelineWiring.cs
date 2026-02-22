using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Helpers;

namespace WayfarerMobile.Services;

/// <summary>
/// Sole orchestrator of the location sync pipeline bootstrap.
/// Ensures delegates, drain services, and timeline storage are wired
/// exactly once, whether bootstrapped from App.xaml.cs (normal MAUI start)
/// or from LocationTrackingService (headless restart after process kill).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Design invariants:</strong>
/// </para>
/// <list type="bullet">
///   <item>Guarded by Interlocked.CompareExchange - concurrent callers are safe.</item>
///   <item>On failure the guard resets, allowing retry on the next location fix.
///         This is safe because all downstream services (R3) are individually idempotent.</item>
///   <item>LocationTrackingService does not know WHAT gets bootstrapped - it calls one method.</item>
/// </list>
/// </remarks>
public static class LocationPipelineWiring
{
    private static int _bootstrapGuard;

    /// <summary>
    /// Bootstraps the sync pipeline idempotently. Safe to call from multiple threads.
    /// </summary>
    /// <param name="serviceProvider">
    /// DI service provider. If null, logs a warning and returns (no retry loop -
    /// Android guarantees Application.onCreate() completes before Service.onCreate(),
    /// so DI is available).
    /// </param>
    public static async Task EnsureBootstrappedAsync(IServiceProvider? serviceProvider)
    {
        if (serviceProvider == null)
        {
#if ANDROID
            Android.Util.Log.Warn("WayfarerLocation", "LocationPipelineWiring: serviceProvider is null, skipping bootstrap");
#endif
            return;
        }

        // Atomic guard: only one caller enters bootstrap
        if (Interlocked.CompareExchange(ref _bootstrapGuard, 1, 0) != 0)
            return;

        // Resolve logger from DI (available once we have a serviceProvider)
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(LocationPipelineWiring).FullName!);

        var delegatesWired = false;
        var drainStarted = false;

        try
        {
            // 1. Resolve DI services
            var settingsService = serviceProvider.GetService<ISettingsService>() as SettingsService;
            var queueDrainService = serviceProvider.GetService<QueueDrainService>();
            var timelineSyncService = serviceProvider.GetService<ITimelineSyncService>();
            var apiClient = serviceProvider.GetService<IApiClient>();
            var locationQueue = serviceProvider.GetService<ILocationQueueRepository>();
            var timelineStorageService = serviceProvider.GetService<LocalTimelineStorageService>();

            // 2. Preload secure settings (prevents deadlocks on Android SecureStorage)
            if (settingsService != null)
            {
                await settingsService.PreloadSecureSettingsAsync();
            }

            // 3. Start QueueDrainService and TimelineSyncService (both idempotent via R3 guards)
            if (queueDrainService != null)
            {
                await queueDrainService.StartAsync();
            }

            if (timelineSyncService != null)
            {
                await timelineSyncService.StartAsync();
            }

            drainStarted = true;

            // 4. Wire drain-loop starter delegate
            if (queueDrainService != null)
            {
                Action drainLoopStarter = () =>
                {
                    queueDrainService.StartDrainLoop();
                    timelineSyncService?.StartDrainLoop(); // Piggyback on location wakeups
                };
                Func<bool> isRunningChecker = () => queueDrainService.IsDrainLoopRunning;

#if ANDROID
                Platforms.Android.Services.LocationTrackingService.SetDrainLoopStarter(drainLoopStarter, isRunningChecker);
#elif IOS
                Platforms.iOS.Services.LocationTrackingService.SetDrainLoopStarter(drainLoopStarter, isRunningChecker);
#endif
            }

            // 5. Initialize LocalTimelineStorageService (idempotent via R3 guard on SubscribeToEvents)
            if (timelineStorageService != null)
            {
                await timelineStorageService.InitializeAsync();
            }

            // 6. Build and set OnlineSubmitDelegate and OfflineQueueDelegate
            if (apiClient != null && locationQueue != null)
            {
                WireLocationDelegates(apiClient, locationQueue, timelineStorageService);
                delegatesWired = true;
            }

            logger?.LogInformation(
                "LocationPipelineWiring: bootstrap=success delegates={DelegatesWired} drain={DrainStarted}",
                delegatesWired, drainStarted);
        }
        catch (Exception ex)
        {
            // Reset guard to allow retry on next location fix.
            // Safe because all steps called above are individually idempotent (R3).
            Interlocked.Exchange(ref _bootstrapGuard, 0);

            logger?.LogError(ex,
                "LocationPipelineWiring: bootstrap=failed delegates={DelegatesWired} drain={DrainStarted} reason={Reason}",
                delegatesWired, drainStarted, ex.Message);
        }
    }

    /// <summary>
    /// Builds and sets the online/offline delegates on the platform LocationTrackingService.
    /// </summary>
    private static void WireLocationDelegates(
        IApiClient apiClient,
        ILocationQueueRepository locationQueue,
        LocalTimelineStorageService? timelineStorageService)
    {
        // Online submit delegate: Submit directly to server via log-location endpoint
        Func<LocationData, Task<int?>> onlineSubmit = async location =>
        {
            // Early connectivity check
            if (Connectivity.Current.NetworkAccess == NetworkAccess.None)
                throw new HttpRequestException("No network connectivity");

            var request = new LocationLogRequest
            {
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                Accuracy = location.Accuracy,
                Altitude = location.Altitude,
                Speed = location.Speed,
                Timestamp = location.Timestamp,
                Provider = location.Provider,
                Bearing = location.Bearing,
                Source = "mobile-log",
                IsUserInvoked = false,
                AppVersion = DeviceMetadataHelper.GetAppVersion(),
                AppBuild = DeviceMetadataHelper.GetAppBuild(),
                DeviceModel = DeviceMetadataHelper.GetDeviceModel(),
                OsVersion = DeviceMetadataHelper.GetOsVersion(),
                BatteryLevel = DeviceMetadataHelper.GetBatteryLevel(),
                IsCharging = DeviceMetadataHelper.GetIsCharging()
            };

            var result = await apiClient.LogLocationAsync(request, idempotencyKey: null);

            if (result.Success)
            {
                if (!result.Skipped && result.LocationId.HasValue && timelineStorageService != null)
                {
                    await timelineStorageService.AddAcceptedLocationAsync(location, result.LocationId.Value);
                }
                return result.LocationId;
            }

            var isTransientStatusCode = result.StatusCode.HasValue &&
                (result.StatusCode == 408 || result.StatusCode == 429 || result.StatusCode >= 500);

            if (result.IsTransient || isTransientStatusCode)
                throw new HttpRequestException($"Transient failure: {result.Message}");

            // Permanent API failure (4xx) - don't queue
            return null;
        };

        // Offline queue delegate: Queue for background sync
        Func<LocationData, Task<int>> offlineQueue = async location =>
        {
            var queuedId = await locationQueue.QueueLocationAsync(location, isUserInvoked: false);

            if (timelineStorageService != null)
            {
                await timelineStorageService.AddPendingLocationAsync(location, queuedId);
            }

            return queuedId;
        };

        // Wire to platform services
#if ANDROID
        Platforms.Android.Services.LocationTrackingService.OnlineSubmitDelegate = onlineSubmit;
        Platforms.Android.Services.LocationTrackingService.OfflineQueueDelegate = offlineQueue;
#elif IOS
        Platforms.iOS.Services.LocationTrackingService.OnlineSubmitDelegate = onlineSubmit;
        Platforms.iOS.Services.LocationTrackingService.OfflineQueueDelegate = offlineQueue;
#endif
    }
}
