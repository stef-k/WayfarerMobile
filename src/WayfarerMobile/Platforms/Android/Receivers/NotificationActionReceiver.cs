using Android.Content;
using Android.Util;
using Android.Widget;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
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

            // Get IApiClient from DI container using service locator pattern
            var apiClient = IPlatformApplication.Current?.Services.GetService<IApiClient>();
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

            // Perform check-in (bypasses thresholds)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await apiClient.CheckInAsync(request, cts.Token);

            if (result.Success)
            {
                ShowToast(context, "Check-in successful");
                LocationServiceCallbacks.NotifyCheckInPerformed(true, null);
                Log.Info(LogTag, "Check-in successful");
            }
            else
            {
                ShowToast(context, $"Check-in failed: {result.Message ?? "Unknown error"}");
                LocationServiceCallbacks.NotifyCheckInPerformed(false, result.Message);
                Log.Warn(LogTag, $"Check-in failed: {result.Message}");
            }
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
