using Android.Content;
using Android.Util;
using Android.Widget;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Helpers;
using WayfarerMobile.Platforms.Android.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.Android.Receivers;

/// <summary>
/// BroadcastReceiver that handles notification action button clicks.
/// Processes check-in action from the tracking notification.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class NotificationActionReceiver : BroadcastReceiver
{
    private const string LogTag = "WayfarerNotifyAction";

    /// <summary>Action intent for manual check-in from notification.</summary>
    public const string ActionCheckIn = "com.wayfarer.mobile.notification.CHECK_IN";

    /// <summary>
    /// Called when the receiver receives a broadcast intent.
    /// </summary>
    /// <param name="context">The context in which the receiver is running.</param>
    /// <param name="intent">The intent being received.</param>
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action == null)
            return;

        Log.Debug(LogTag, $"Received action: {intent.Action}");

        if (intent.Action == ActionCheckIn)
        {
            _ = PerformCheckInAsync(context);
        }
    }

    /// <summary>
    /// Performs a manual check-in using the last received location.
    /// Supports offline fallback - queues check-in if server is unavailable.
    /// </summary>
    /// <param name="context">The Android context.</param>
    private async Task PerformCheckInAsync(Context context)
    {
        try
        {
            var lastLocation = LocationServiceCallbacks.LastLocation;
            if (lastLocation == null)
            {
                ShowToast(context, "No location available for check-in");
                Log.Warn(LogTag, "Check-in failed: no location");
                return;
            }

            // Get services from DI container using service locator pattern
            var services = IPlatformApplication.Current?.Services;
            var apiClient = services?.GetService<IApiClient>();
            var locationQueue = services?.GetService<ILocationQueueRepository>();
            var timelineStorage = services?.GetService<LocalTimelineStorageService>();

            if (apiClient == null)
            {
                ShowToast(context, "Check-in service unavailable");
                Log.Warn(LogTag, "Check-in failed: IApiClient not available");
                return;
            }

            if (!apiClient.IsConfigured)
            {
                ShowToast(context, "Server not configured");
                Log.Warn(LogTag, "Check-in failed: API client not configured");
                return;
            }

            // Create location log request from last location
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

                    ShowToast(context, "Check-in successful");
                    LocationServiceCallbacks.NotifyCheckInPerformed(true, null);
                    Log.Info(LogTag, "Check-in successful");
                    return;
                }

                // Server rejected (non-network failure)
                ShowToast(context, $"Check-in failed: {result.Message ?? "Unknown error"}");
                LocationServiceCallbacks.NotifyCheckInPerformed(false, result.Message);
                Log.Warn(LogTag, $"Check-in failed: {result.Message}");
                return;
            }
            catch (HttpRequestException ex)
            {
                // Only log if we thought we were online (unexpected failure)
                if (NetworkLoggingExtensions.HasInternetConnectivity())
                {
                    Log.Warn(LogTag, $"Network error, falling back to offline: {ex.Message}");
                }
            }
            catch (OperationCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                Log.Warn(LogTag, "Timeout, falling back to offline");
            }

            // OFFLINE PATH: Queue for background sync
            if (locationQueue == null)
            {
                ShowToast(context, "Offline check-in unavailable");
                LocationServiceCallbacks.NotifyCheckInPerformed(false, "Queue service unavailable");
                Log.Warn(LogTag, "Check-in failed: ILocationQueueRepository not available");
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

            ShowToast(context, "Check-in queued (offline)");
            LocationServiceCallbacks.NotifyCheckInPerformed(true, "Queued for sync");
            Log.Info(LogTag, "Check-in queued for offline sync");
        }
        catch (OperationCanceledException)
        {
            ShowToast(context, "Check-in timed out");
            LocationServiceCallbacks.NotifyCheckInPerformed(false, "Timeout");
            Log.Warn(LogTag, "Check-in timed out");
        }
        catch (Exception ex)
        {
            ShowToast(context, "Check-in error");
            LocationServiceCallbacks.NotifyCheckInPerformed(false, ex.Message);
            Log.Error(LogTag, $"Check-in error: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows a toast message to the user.
    /// </summary>
    /// <param name="context">The Android context.</param>
    /// <param name="message">The message to display.</param>
    private static void ShowToast(Context context, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Toast.MakeText(context, message, ToastLength.Short)?.Show();
        });
    }
}
