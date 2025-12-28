using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using WayfarerMobile.Core.Interfaces;
using Application = Android.App.Application;

namespace WayfarerMobile.Platforms.Android.Services;

/// <summary>
/// Android implementation of local notifications.
/// </summary>
public class LocalNotificationService : ILocalNotificationService
{
    #region Constants

    /// <summary>
    /// Notification channel ID for visit notifications.
    /// </summary>
    private const string VisitChannelId = "wayfarer_visit_notifications";

    /// <summary>
    /// Notification channel name displayed in Android settings.
    /// </summary>
    private const string VisitChannelName = "Visit Notifications";

    /// <summary>
    /// Notification channel description.
    /// </summary>
    private const string VisitChannelDescription = "Notifications when you arrive at trip places";

    /// <summary>
    /// Base ID for visit notifications.
    /// </summary>
    private const int BaseNotificationId = 2000;

    #endregion

    #region Fields

    private readonly NotificationManager? _notificationManager;
    private int _notificationIdCounter = BaseNotificationId;
    private bool _channelCreated;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of LocalNotificationService.
    /// </summary>
    public LocalNotificationService()
    {
        _notificationManager = Application.Context.GetSystemService(Context.NotificationService) as NotificationManager;
    }

    #endregion

    #region ILocalNotificationService

    /// <inheritdoc />
    public Task<int> ShowAsync(string title, string message, bool silent = false, Dictionary<string, string>? data = null)
    {
        try
        {
            EnsureChannelCreated();

            var context = Application.Context;
            if (context == null)
            {
                System.Diagnostics.Debug.WriteLine("[Android LocalNotificationService] Context is null");
                return Task.FromResult(-1);
            }

            var notificationId = Interlocked.Increment(ref _notificationIdCounter);

            // Create intent for when notification is tapped
            var intent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty)
                ?? new Intent(context, typeof(MainActivity));

            intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

            // Add custom data to intent
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    intent.PutExtra(kvp.Key, kvp.Value);
                }
            }

            var pendingIntent = PendingIntent.GetActivity(
                context,
                notificationId,
                intent,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            // Build notification
            var builder = new NotificationCompat.Builder(context, VisitChannelId);
            builder.SetContentTitle(title);
            builder.SetContentText(message);
            builder.SetSmallIcon(Resource.Drawable.ic_notification);
            builder.SetAutoCancel(true);
            builder.SetContentIntent(pendingIntent);
            builder.SetPriority(silent ? NotificationCompat.PriorityLow : NotificationCompat.PriorityDefault);

            if (silent)
            {
                // Silent: no sound, no vibration, no heads-up
                builder.SetDefaults(0);
                builder.SetVibrate(null);
                builder.SetSound(null);
            }
            else
            {
                // Normal: use default sound and vibration
                builder.SetDefaults((int)NotificationDefaults.All);
            }

            _notificationManager?.Notify(notificationId, builder.Build());

            System.Diagnostics.Debug.WriteLine(
                $"[Android LocalNotificationService] Showed notification {notificationId}: {title} (silent: {silent})");

            return Task.FromResult(notificationId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Android LocalNotificationService] Failed to show notification: {ex.Message}");
            return Task.FromResult(-1);
        }
    }

    /// <inheritdoc />
    public void Cancel(int notificationId)
    {
        try
        {
            _notificationManager?.Cancel(notificationId);
            System.Diagnostics.Debug.WriteLine($"[Android LocalNotificationService] Cancelled notification {notificationId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Android LocalNotificationService] Failed to cancel notification: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void CancelAll()
    {
        try
        {
            _notificationManager?.CancelAll();
            System.Diagnostics.Debug.WriteLine("[Android LocalNotificationService] Cancelled all notifications");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Android LocalNotificationService] Failed to cancel all notifications: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> RequestPermissionAsync()
    {
        // Android 13+ (API 33+) requires POST_NOTIFICATIONS permission
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }

            return status == PermissionStatus.Granted;
        }

        // Below Android 13, notifications are allowed by default
        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Creates the notification channel if not already created.
    /// </summary>
    private void EnsureChannelCreated()
    {
        if (_channelCreated)
        {
            return;
        }

        // Notification channels are only supported on Android 8.0 (API 26) and later
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _channelCreated = true; // No channel needed
            return;
        }

        try
        {
            var channel = new NotificationChannel(
                VisitChannelId,
                VisitChannelName,
                NotificationImportance.Default)
            {
                Description = VisitChannelDescription
            };

            // Enable lights and vibration
            channel.EnableLights(true);
            channel.EnableVibration(true);

            _notificationManager?.CreateNotificationChannel(channel);
            _channelCreated = true;

            System.Diagnostics.Debug.WriteLine("[Android LocalNotificationService] Created visit notification channel");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Android LocalNotificationService] Failed to create notification channel: {ex.Message}");
        }
    }

    #endregion
}
