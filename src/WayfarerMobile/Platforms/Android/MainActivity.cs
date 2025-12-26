using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace WayfarerMobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Static flag that persists for the lifetime of the process.
    /// Set to true after first successful initialization.
    /// If this is true but preferences are cleared, we know data was cleared externally.
    /// </summary>
    private static bool s_processInitialized;

    /// <summary>
    /// Key used to mark that we've written to preferences in this process.
    /// </summary>
    private const string ProcessMarkerKey = "process_marker";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Early check for corrupted state before MAUI initializes.
        // This handles the case where app data was cleared while the process was alive.
        if (DetectAndRecoverFromCorruptedState())
        {
            // State was corrupted - we've cleared it, now restart the app process
            System.Diagnostics.Debug.WriteLine("[MainActivity] Corrupted state detected - restarting app");
            RestartApp();
            return;
        }

        base.OnCreate(savedInstanceState);
    }

    /// <summary>
    /// Detects if app data was cleared while the process was running.
    /// Uses a static flag + preferences marker to detect the mismatch.
    /// </summary>
    private bool DetectAndRecoverFromCorruptedState()
    {
        try
        {
            // MAUI Preferences uses a specific SharedPreferences file
            var prefs = GetSharedPreferences($"{PackageName}.microsoft.maui.essentials.preferences", FileCreationMode.Private);
            if (prefs == null)
                return false;

            // Check if our process marker exists in preferences
            var hasProcessMarker = prefs.Contains(ProcessMarkerKey);

            // Case 1: Process was initialized before, but marker is gone → data was cleared
            if (s_processInitialized && !hasProcessMarker)
            {
                System.Diagnostics.Debug.WriteLine("[MainActivity] Corruption detected: process initialized but marker missing");
                return true; // Will trigger restart
            }

            // Case 2: First time in this process - set up the marker
            if (!s_processInitialized)
            {
                s_processInitialized = true;

                // Write marker to preferences
                var editor = prefs.Edit();
                editor?.PutBoolean(ProcessMarkerKey, true);
                editor?.Apply();

                System.Diagnostics.Debug.WriteLine("[MainActivity] Process marker set");
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Error checking state: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restarts the app by killing the current process and launching a new one.
    /// This ensures all singletons are cleared.
    /// </summary>
    private void RestartApp()
    {
        try
        {
            var packageManager = PackageManager;
            var intent = packageManager?.GetLaunchIntentForPackage(PackageName ?? "");
            if (intent != null)
            {
                intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask | ActivityFlags.ClearTask);
                StartActivity(intent);
            }

            // Kill the current process to clear all singletons
            Java.Lang.JavaSystem.Exit(0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Failed to restart app: {ex.Message}");
            // Fall through to normal startup
        }
    }
}
