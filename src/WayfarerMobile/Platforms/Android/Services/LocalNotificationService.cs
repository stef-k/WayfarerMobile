using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
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

    private const string LogTag = "WayfarerNotification";

    /// <summary>
    /// Notification channel ID for normal visit notifications (with sound/vibration).
    /// </summary>
    private const string VisitChannelId = "wayfarer_visit_notifications";

    /// <summary>
    /// Notification channel ID for silent visit notifications (no sound/vibration).
    /// On Android 8+, channel importance controls alert behavior, not per-notification settings.
    /// </summary>
    private const string SilentVisitChannelId = "wayfarer_visit_silent";

    /// <summary>
    /// Notification channel name displayed in Android settings.
    /// </summary>
    private const string VisitChannelName = "Visit Notifications";

    /// <summary>
    /// Silent notification channel name displayed in Android settings.
    /// </summary>
    private const string SilentVisitChannelName = "Visit Notifications (Silent)";

    /// <summary>
    /// Notification channel description.
    /// </summary>
    private const string VisitChannelDescription = "Notifications when you arrive at trip places";

    /// <summary>
    /// Silent notification channel description.
    /// </summary>
    private const string SilentVisitChannelDescription = "Silent notifications during navigation";

    /// <summary>
    /// Base ID for visit notifications.
    /// </summary>
    private const int BaseNotificationId = 2000;

    #endregion

    #region Fields

    private readonly NotificationManager? _notificationManager;
    private int _notificationIdCounter = BaseNotificationId;
    private bool _channelsCreated;

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
            EnsureChannelsCreated();

            var context = Application.Context;
            if (context == null)
            {
                Log.Warn(LogTag, "Context is null");
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

            // Use appropriate channel based on silent flag
            // On Android 8+, channel importance controls sound/vibration, not per-notification settings
            var channelId = silent ? SilentVisitChannelId : VisitChannelId;

            // Build notification
            var builder = new NotificationCompat.Builder(context, channelId);
            builder.SetContentTitle(title);
            builder.SetContentText(message);
            builder.SetSmallIcon(Resource.Drawable.ic_notification);
            builder.SetAutoCancel(true);
            builder.SetContentIntent(pendingIntent);
            builder.SetPriority(silent ? NotificationCompat.PriorityLow : NotificationCompat.PriorityDefault);

            // On Android 7.x (API 24-25), channels are ignored - configure per-notification
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                if (silent)
                {
                    builder.SetDefaults(0);
                    builder.SetVibrate(null);
                    builder.SetSound(null);
                }
                else
                {
                    builder.SetDefaults((int)NotificationDefaults.All);
                }
            }

            _notificationManager?.Notify(notificationId, builder.Build());

            Log.Debug(LogTag, $"Showed notification {notificationId}: {title} (silent: {silent}, channel: {channelId})");

            return Task.FromResult(notificationId);
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Failed to show notification: {ex.Message}");
            return Task.FromResult(-1);
        }
    }

    /// <inheritdoc />
    public void Cancel(int notificationId)
    {
        try
        {
            _notificationManager?.Cancel(notificationId);
            Log.Debug(LogTag, $"Cancelled notification {notificationId}");
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Failed to cancel notification: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void CancelAll()
    {
        try
        {
            _notificationManager?.CancelAll();
            Log.Debug(LogTag, "Cancelled all notifications");
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Failed to cancel all notifications: {ex.Message}");
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
    /// Creates both notification channels if not already created.
    /// On Android 8+, channel importance controls sound/vibration behavior.
    /// </summary>
    private void EnsureChannelsCreated()
    {
        if (_channelsCreated)
        {
            return;
        }

        // Notification channels are only supported on Android 8.0 (API 26) and later
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            _channelsCreated = true; // No channel needed
            return;
        }

        try
        {
            // Normal channel - default importance with sound and vibration
            var normalChannel = new NotificationChannel(
                VisitChannelId,
                VisitChannelName,
                NotificationImportance.Default)
            {
                Description = VisitChannelDescription
            };
            normalChannel.EnableLights(true);
            normalChannel.EnableVibration(true);
            _notificationManager?.CreateNotificationChannel(normalChannel);

            // Silent channel - low importance, no sound, no vibration
            var silentChannel = new NotificationChannel(
                SilentVisitChannelId,
                SilentVisitChannelName,
                NotificationImportance.Low)
            {
                Description = SilentVisitChannelDescription
            };
            silentChannel.EnableLights(false);
            silentChannel.EnableVibration(false);
            silentChannel.SetSound(null, null);
            _notificationManager?.CreateNotificationChannel(silentChannel);

            _channelsCreated = true;

            Log.Debug(LogTag, "Created visit notification channels (normal + silent)");
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Failed to create notification channels: {ex.Message}");
        }
    }

    #endregion
}
