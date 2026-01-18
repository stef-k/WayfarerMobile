using Foundation;
using UserNotifications;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Helpers;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// Manages local notifications with quick actions for iOS location tracking.
/// Implements IUNUserNotificationCenterDelegate to handle notification action responses.
/// </summary>
public class TrackingNotificationService : NSObject, IUNUserNotificationCenterDelegate
{
    #region Constants

    /// <summary>
    /// Category identifier for tracking notifications.
    /// </summary>
    public const string CategoryIdentifier = "WAYFARER_TRACKING";

    /// <summary>
    /// Action identifier for check-in action.
    /// </summary>
    public const string ActionCheckIn = "CHECK_IN_ACTION";

    /// <summary>
    /// Action identifier for pause action.
    /// </summary>
    public const string ActionPause = "PAUSE_ACTION";

    /// <summary>
    /// Action identifier for resume action.
    /// </summary>
    public const string ActionResume = "RESUME_ACTION";

    /// <summary>
    /// Action identifier for stop action.
    /// </summary>
    public const string ActionStop = "STOP_ACTION";

    /// <summary>
    /// Unique identifier for the tracking notification.
    /// </summary>
    public const string NotificationIdentifier = "wayfarer_tracking_notification";

    #endregion

    #region Singleton

    private static TrackingNotificationService? _instance;

    /// <summary>
    /// Gets the singleton instance of the service.
    /// </summary>
    public static TrackingNotificationService Instance => _instance ??= new TrackingNotificationService();

    private TrackingNotificationService() { }

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers notification categories and actions with the system.
    /// Should be called during app startup.
    /// </summary>
    public void RegisterNotificationActions()
    {
        var checkInAction = UNNotificationAction.FromIdentifier(
            ActionCheckIn,
            "Check In",
            UNNotificationActionOptions.None);

        var pauseAction = UNNotificationAction.FromIdentifier(
            ActionPause,
            "Pause",
            UNNotificationActionOptions.None);

        var resumeAction = UNNotificationAction.FromIdentifier(
            ActionResume,
            "Resume",
            UNNotificationActionOptions.None);

        var stopAction = UNNotificationAction.FromIdentifier(
            ActionStop,
            "Stop",
            UNNotificationActionOptions.Destructive);

        // Create category with check-in, pause, and stop actions (default active state)
        var category = UNNotificationCategory.FromIdentifier(
            CategoryIdentifier,
            new[] { checkInAction, pauseAction, stopAction },
            Array.Empty<string>(),
            UNNotificationCategoryOptions.None);

        UNUserNotificationCenter.Current.SetNotificationCategories(
            new NSSet<UNNotificationCategory>(category));

        Console.WriteLine("[iOS TrackingNotificationService] Registered notification actions");
    }

    /// <summary>
    /// Shows the tracking notification with actions.
    /// </summary>
    /// <param name="isPaused">Whether tracking is currently paused.</param>
    /// <returns>A task representing the async operation.</returns>
    public async Task ShowTrackingNotificationAsync(bool isPaused)
    {
        var content = new UNMutableNotificationContent
        {
            Title = "Wayfarer",
            Body = isPaused ? "Tracking paused" : "Tracking your location",
            CategoryIdentifier = CategoryIdentifier
        };

        var request = UNNotificationRequest.FromIdentifier(
            NotificationIdentifier,
            content,
            null);

        try
        {
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
            Console.WriteLine($"[iOS TrackingNotificationService] Notification shown (paused: {isPaused})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS TrackingNotificationService] Failed to show notification: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the notification content based on tracking state.
    /// </summary>
    /// <param name="isPaused">Whether tracking is currently paused.</param>
    /// <returns>A task representing the async operation.</returns>
    public async Task UpdateNotificationAsync(bool isPaused)
    {
        await HideTrackingNotificationAsync();
        await ShowTrackingNotificationAsync(isPaused);
    }

    /// <summary>
    /// Removes the tracking notification.
    /// </summary>
    /// <returns>A task representing the async operation.</returns>
    public Task HideTrackingNotificationAsync()
    {
        UNUserNotificationCenter.Current.RemoveDeliveredNotifications(
            new[] { NotificationIdentifier });

        Console.WriteLine("[iOS TrackingNotificationService] Notification hidden");
        return Task.CompletedTask;
    }

    #endregion

    #region IUNUserNotificationCenterDelegate

    /// <summary>
    /// Handles notification action responses from the user.
    /// </summary>
    /// <param name="center">The notification center.</param>
    /// <param name="response">The user's response to the notification.</param>
    /// <param name="completionHandler">Completion handler to call when done.</param>
    [Export("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:")]
    public async void DidReceiveNotificationResponse(
        UNUserNotificationCenter center,
        UNNotificationResponse response,
        Action completionHandler)
    {
        try
        {
            Console.WriteLine($"[iOS TrackingNotificationService] Received action: {response.ActionIdentifier}");

            switch (response.ActionIdentifier)
            {
                case ActionCheckIn:
                    await PerformCheckInAsync();
                    break;

                case ActionPause:
                    LocationServiceCallbacks.RequestPause();
                    break;

                case ActionResume:
                    LocationServiceCallbacks.RequestResume();
                    break;

                case ActionStop:
                    LocationServiceCallbacks.RequestStop();
                    break;

                default:
                    // User tapped the notification itself (not an action)
                    Console.WriteLine($"[iOS TrackingNotificationService] Notification tapped: {response.ActionIdentifier}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS TrackingNotificationService] Action handler error: {ex.Message}");
        }
        finally
        {
            completionHandler();
        }
    }

    /// <summary>
    /// Called when a notification is about to be presented while the app is in foreground.
    /// </summary>
    /// <param name="center">The notification center.</param>
    /// <param name="notification">The notification to present.</param>
    /// <param name="completionHandler">Completion handler to call with presentation options.</param>
    [Export("userNotificationCenter:willPresentNotification:withCompletionHandler:")]
    public void WillPresentNotification(
        UNUserNotificationCenter center,
        UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
        // Show notification even when app is in foreground
        completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Performs a manual check-in using the last received location.
    /// Supports offline fallback - queues check-in if server is unavailable.
    /// </summary>
    private async Task PerformCheckInAsync()
    {
        var lastLocation = LocationServiceCallbacks.LastLocation;
        if (lastLocation == null)
        {
            LocationServiceCallbacks.NotifyCheckInPerformed(false, "No location available");
            Console.WriteLine("[iOS TrackingNotificationService] Check-in failed: no location");
            return;
        }

        try
        {
            // Get services from DI container
            var services = IPlatformApplication.Current?.Services;
            var apiClient = services?.GetService<IApiClient>();
            var locationQueue = services?.GetService<ILocationQueueRepository>();
            var timelineStorage = services?.GetService<LocalTimelineStorageService>();

            if (apiClient == null)
            {
                LocationServiceCallbacks.NotifyCheckInPerformed(false, "Service unavailable");
                Console.WriteLine("[iOS TrackingNotificationService] Check-in failed: IApiClient not available");
                return;
            }

            if (!apiClient.IsConfigured)
            {
                LocationServiceCallbacks.NotifyCheckInPerformed(false, "Server not configured");
                Console.WriteLine("[iOS TrackingNotificationService] Check-in failed: API client not configured");
                return;
            }

            var request = new LocationLogRequest
            {
                Latitude = lastLocation.Latitude,
                Longitude = lastLocation.Longitude,
                Altitude = lastLocation.Altitude,
                Accuracy = lastLocation.Accuracy,
                Speed = lastLocation.Speed,
                Timestamp = lastLocation.Timestamp,
                Provider = "notification-checkin"
            };

            // ONLINE PATH: Try direct server submission
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await apiClient.CheckInAsync(request, Guid.NewGuid().ToString("N"), cts.Token);

                if (result.Success)
                {
                    // Add to local timeline with server-assigned ID
                    if (result.LocationId.HasValue && timelineStorage != null)
                    {
                        await timelineStorage.AddAcceptedLocationAsync(lastLocation, result.LocationId.Value);
                    }

                    LocationServiceCallbacks.NotifyCheckInPerformed(true, "Checked in successfully");
                    Console.WriteLine("[iOS TrackingNotificationService] Check-in successful");
                    return;
                }

                // Server rejected (non-network failure)
                LocationServiceCallbacks.NotifyCheckInPerformed(false, result.Message);
                Console.WriteLine($"[iOS TrackingNotificationService] Check-in failed: {result.Message}");
                return;
            }
            catch (HttpRequestException ex)
            {
                // Only log if we thought we were online (unexpected failure)
                if (NetworkLoggingExtensions.HasInternetConnectivity())
                {
                    Console.WriteLine($"[iOS TrackingNotificationService] Network error, falling back to offline: {ex.Message}");
                }
            }
            catch (OperationCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("[iOS TrackingNotificationService] Timeout, falling back to offline");
            }

            // OFFLINE PATH: Queue for background sync
            if (locationQueue == null)
            {
                LocationServiceCallbacks.NotifyCheckInPerformed(false, "Offline check-in unavailable");
                Console.WriteLine("[iOS TrackingNotificationService] Check-in failed: ILocationQueueRepository not available");
                return;
            }

            // Queue with isUserInvoked=true (manual check-in)
            var queuedId = await locationQueue.QueueLocationAsync(
                lastLocation,
                isUserInvoked: true);

            // Add pending entry to local timeline
            if (timelineStorage != null)
            {
                await timelineStorage.AddPendingLocationAsync(lastLocation, queuedId);
            }

            LocationServiceCallbacks.NotifyCheckInPerformed(true, "Queued for sync");
            Console.WriteLine("[iOS TrackingNotificationService] Check-in queued for offline sync");
        }
        catch (OperationCanceledException)
        {
            LocationServiceCallbacks.NotifyCheckInPerformed(false, "Timeout");
            Console.WriteLine("[iOS TrackingNotificationService] Check-in timed out");
        }
        catch (Exception ex)
        {
            LocationServiceCallbacks.NotifyCheckInPerformed(false, ex.Message);
            Console.WriteLine($"[iOS TrackingNotificationService] Check-in error: {ex.Message}");
        }
    }

    #endregion
}
