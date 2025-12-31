using Foundation;
using UserNotifications;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Platforms.iOS.Services;

/// <summary>
/// iOS implementation of local notifications.
/// </summary>
public class LocalNotificationService : ILocalNotificationService
{
    #region Constants

    /// <summary>
    /// Category identifier for visit notifications.
    /// </summary>
    private const string VisitCategoryId = "WAYFARER_VISIT";

    /// <summary>
    /// Base ID for visit notifications.
    /// </summary>
    private const int BaseNotificationId = 2000;

    #endregion

    #region Fields

    private int _notificationIdCounter = BaseNotificationId;
    private bool _categoryRegistered;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new instance of LocalNotificationService.
    /// </summary>
    public LocalNotificationService()
    {
        RegisterCategory();
    }

    #endregion

    #region ILocalNotificationService

    /// <inheritdoc />
    public async Task<int> ShowAsync(string title, string message, bool silent = false, Dictionary<string, string>? data = null)
    {
        try
        {
            var notificationId = Interlocked.Increment(ref _notificationIdCounter);
            var identifier = $"visit_{notificationId}";

            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body = message,
                CategoryIdentifier = VisitCategoryId
            };

            // Add custom data as userInfo
            if (data != null && data.Count > 0)
            {
                var userInfo = new NSMutableDictionary();
                foreach (var kvp in data)
                {
                    userInfo.Add(new NSString(kvp.Key), new NSString(kvp.Value));
                }
                content.UserInfo = userInfo;
            }

            if (!silent)
            {
                // Normal notification with default sound
                content.Sound = UNNotificationSound.Default;
            }
            // Silent: no sound property set = silent notification

            // Create immediate trigger
            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(0.1, false);

            var request = UNNotificationRequest.FromIdentifier(
                identifier,
                content,
                trigger);

            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);

            Console.WriteLine($"[iOS LocalNotificationService] Showed notification {identifier}: {title} (silent: {silent})");

            return notificationId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocalNotificationService] Failed to show notification: {ex.Message}");
            return -1;
        }
    }

    /// <inheritdoc />
    public void Cancel(int notificationId)
    {
        try
        {
            var identifier = $"visit_{notificationId}";
            UNUserNotificationCenter.Current.RemoveDeliveredNotifications(new[] { identifier });
            UNUserNotificationCenter.Current.RemovePendingNotificationRequests(new[] { identifier });

            Console.WriteLine($"[iOS LocalNotificationService] Cancelled notification {identifier}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocalNotificationService] Failed to cancel notification: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void CancelAll()
    {
        try
        {
            UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
            UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();

            Console.WriteLine("[iOS LocalNotificationService] Cancelled all notifications");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocalNotificationService] Failed to cancel all notifications: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);

            if (error != null)
            {
                Console.WriteLine($"[iOS LocalNotificationService] Permission request error: {error.LocalizedDescription}");
            }

            Console.WriteLine($"[iOS LocalNotificationService] Notification permission: {(granted ? "granted" : "denied")}");

            return granted;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocalNotificationService] Failed to request permission: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Registers the notification category for visit notifications.
    /// </summary>
    private void RegisterCategory()
    {
        if (_categoryRegistered)
        {
            return;
        }

        try
        {
            // Simple category without actions for now
            var category = UNNotificationCategory.FromIdentifier(
                VisitCategoryId,
                Array.Empty<UNNotificationAction>(),
                Array.Empty<string>(),
                UNNotificationCategoryOptions.None);

            UNUserNotificationCenter.Current.SetNotificationCategories(
                new NSSet<UNNotificationCategory>(category));

            _categoryRegistered = true;

            Console.WriteLine("[iOS LocalNotificationService] Registered visit notification category");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[iOS LocalNotificationService] Failed to register category: {ex.Message}");
        }
    }

    #endregion
}
