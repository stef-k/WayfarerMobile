using Android.Content;
using Android.Widget;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Platforms.Android.Services;
using WayfarerMobile.Services;

namespace WayfarerMobile.Platforms.Android.Receivers;

/// <summary>
/// BroadcastReceiver that handles notification action button clicks.
/// Processes check-in, pause/resume, and stop actions from the tracking notification.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class NotificationActionReceiver : BroadcastReceiver
{
    /// <summary>Action intent for manual check-in from notification.</summary>
    public const string ActionCheckIn = "com.wayfarer.mobile.notification.CHECK_IN";

    /// <summary>Action intent for pause/resume toggle from notification.</summary>
    public const string ActionPauseResume = "com.wayfarer.mobile.notification.PAUSE_RESUME";

    /// <summary>Action intent for stop tracking from notification.</summary>
    public const string ActionStop = "com.wayfarer.mobile.notification.STOP";

    /// <summary>
    /// Called when the receiver receives a broadcast intent.
    /// </summary>
    /// <param name="context">The context in which the receiver is running.</param>
    /// <param name="intent">The intent being received.</param>
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action == null)
            return;

        System.Diagnostics.Debug.WriteLine($"[NotificationActionReceiver] Received action: {intent.Action}");

        switch (intent.Action)
        {
            case ActionCheckIn:
                _ = PerformCheckInAsync(context);
                break;

            case ActionPauseResume:
                // Toggle based on current state - the service will handle the actual state
                var isPaused = intent.GetBooleanExtra("is_paused", false);
                var serviceAction = isPaused
                    ? LocationTrackingService.ActionResume
                    : LocationTrackingService.ActionPause;
                SendServiceCommand(context, serviceAction);
                break;

            case ActionStop:
                SendServiceCommand(context, LocationTrackingService.ActionStop);
                break;
        }
    }

    /// <summary>
    /// Performs a manual check-in using the last received location.
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
                System.Diagnostics.Debug.WriteLine("[NotificationActionReceiver] Check-in failed: no location");
                return;
            }

            // Get IApiClient from DI container using service locator pattern
            var apiClient = IPlatformApplication.Current?.Services.GetService<IApiClient>();
            if (apiClient == null)
            {
                ShowToast(context, "Check-in service unavailable");
                System.Diagnostics.Debug.WriteLine("[NotificationActionReceiver] Check-in failed: IApiClient not available");
                return;
            }

            if (!apiClient.IsConfigured)
            {
                ShowToast(context, "Server not configured");
                System.Diagnostics.Debug.WriteLine("[NotificationActionReceiver] Check-in failed: API client not configured");
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
                Timestamp = lastLocation.Timestamp
            };

            // Perform check-in (bypasses thresholds)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await apiClient.CheckInAsync(request, cts.Token);

            if (result.Success)
            {
                ShowToast(context, "Check-in successful");
                LocationServiceCallbacks.NotifyCheckInPerformed(true, null);
                System.Diagnostics.Debug.WriteLine("[NotificationActionReceiver] Check-in successful");
            }
            else
            {
                ShowToast(context, $"Check-in failed: {result.Message ?? "Unknown error"}");
                LocationServiceCallbacks.NotifyCheckInPerformed(false, result.Message);
                System.Diagnostics.Debug.WriteLine($"[NotificationActionReceiver] Check-in failed: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            ShowToast(context, "Check-in timed out");
            LocationServiceCallbacks.NotifyCheckInPerformed(false, "Timeout");
            System.Diagnostics.Debug.WriteLine("[NotificationActionReceiver] Check-in timed out");
        }
        catch (Exception ex)
        {
            ShowToast(context, "Check-in error");
            LocationServiceCallbacks.NotifyCheckInPerformed(false, ex.Message);
            System.Diagnostics.Debug.WriteLine($"[NotificationActionReceiver] Check-in error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a command to the LocationTrackingService.
    /// </summary>
    /// <param name="context">The Android context.</param>
    /// <param name="action">The service action to invoke.</param>
    private static void SendServiceCommand(Context context, string action)
    {
        try
        {
            var serviceIntent = new Intent(context, typeof(LocationTrackingService));
            serviceIntent.SetAction(action);

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(serviceIntent);
            }
            else
            {
                context.StartService(serviceIntent);
            }

            System.Diagnostics.Debug.WriteLine($"[NotificationActionReceiver] Sent service command: {action}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationActionReceiver] Failed to send service command: {ex.Message}");
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
