using Foundation;
using UserNotifications;
using WayfarerMobile.Platforms.iOS.Services;

namespace WayfarerMobile;

/// <summary>
/// iOS application delegate that handles app lifecycle and notification setup.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <inheritdoc/>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <inheritdoc/>
    public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary? launchOptions)
    {
        // Set up notification delegate for handling quick actions
        UNUserNotificationCenter.Current.Delegate = TrackingNotificationService.Instance;
        TrackingNotificationService.Instance.RegisterNotificationActions();

        return base.FinishedLaunching(application, launchOptions);
    }
}
