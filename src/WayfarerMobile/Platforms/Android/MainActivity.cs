using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using AndroidX.Core.View;

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
    private const string LogTag = "WayfarerMainActivity";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Early check for corrupted state before MAUI initializes.
        // This handles the case where app data was cleared while the process was alive.
        if (DetectAndRecoverFromCorruptedState())
        {
            // State was corrupted - we've cleared it, now restart the app process
            Log.Warn(LogTag, "Corrupted state detected - restarting app");
            RestartApp();
            return;
        }

        // WORKAROUND: Clear saved instance state to prevent MAUI Shell crash.
        // When Android restores the app from saved state after process death,
        // Shell tries to recreate pages using a disposed IServiceProvider scope,
        // causing ObjectDisposedException in ContentPage.UpdateHideSoftInputOnTapped().
        // Passing null forces MAUI to start fresh instead of attempting restoration.
        base.OnCreate(null);

        ConfigureStatusBar();
    }

    /// <summary>
    /// Configures the status bar appearance - black with white icons.
    /// </summary>
    private void ConfigureStatusBar()
    {
        if (Window == null) return;

        // Set status bar to black
#pragma warning disable CA1422 // SetStatusBarColor is obsolete on Android 35+ but works on older versions
        Window.SetStatusBarColor(Android.Graphics.Color.Black);
#pragma warning restore CA1422

        // Use WindowInsetsControllerCompat for icon appearance
        var insetsController = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (insetsController != null)
        {
            // false = light/white icons (for dark backgrounds)
            insetsController.AppearanceLightStatusBars = false;
        }
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
                Log.Warn(LogTag, "Corruption detected: process initialized but marker missing");
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

                Log.Debug(LogTag, "Process marker set");
            }

            return false;
        }
        catch (Exception ex)
        {
            Log.Warn(LogTag, $"Error checking state: {ex.Message}");
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
            Log.Warn(LogTag, $"Failed to restart app: {ex.Message}");
            // Fall through to normal startup
        }
    }
}
