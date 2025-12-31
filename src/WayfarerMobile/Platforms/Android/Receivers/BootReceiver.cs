using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using WayfarerMobile.Platforms.Android.Services;

namespace WayfarerMobile.Platforms.Android.Receivers;

/// <summary>
/// Boot receiver to automatically start location tracking when device boots up.
/// Ensures 24/7 tracking continues even if user never opens the app after reboot.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] {
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced
}, Priority = 1000)]
public class BootReceiver : BroadcastReceiver
{
    private const string LogTag = "WayfarerBootReceiver";

    /// <summary>
    /// Handles boot completed and package replacement events.
    /// </summary>
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action == null)
            return;

        try
        {
            Log.Info(LogTag, $"Triggered by: {intent.Action}");

            // Check if user has completed onboarding (has permissions)
            var isFirstRun = Preferences.Get("is_first_run", true);
            if (isFirstRun)
            {
                Log.Debug(LogTag, "First run - not starting service (user needs to complete onboarding)");
                return;
            }

            // Start the location tracking service
            StartLocationService(context);
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the location tracking foreground service.
    /// </summary>
    private void StartLocationService(Context context)
    {
        try
        {
            Log.Info(LogTag, "Starting location tracking service");

            var serviceIntent = new Intent(context, typeof(LocationTrackingService));
            serviceIntent.SetAction(LocationTrackingService.ActionStart);

            // Use OperatingSystem.IsAndroidVersionAtLeast() - analyzer-recognized pattern
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(serviceIntent);
            }
            else
            {
                context.StartService(serviceIntent);
            }

            Log.Info(LogTag, "Location tracking service started successfully");
        }
        catch (Exception ex)
        {
            Log.Error(LogTag, $"Failed to start service: {ex.Message}");
        }
    }
}
