using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using AndroidX.Core.View;
using WayfarerMobile.Platforms.Android;

namespace WayfarerMobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Static flag to ensure we only register the exception handler once per process.
    /// </summary>
    private static bool s_exceptionHandlerRegistered;

    /// <summary>
    /// Static flag that persists for the lifetime of the process.
    /// Set to true after first successful initialization.
    /// If this is true but preferences are cleared, we know data was cleared externally.
    /// </summary>
    private static bool s_processInitialized;

    /// <summary>
    /// Static flag set when OnDestroy is called. Used to detect Activity recreation
    /// where Singleton services have stale IServiceProvider references.
    /// </summary>
    private static bool s_activityWasDestroyed;

    /// <summary>
    /// Key used to mark that we've written to preferences in this process.
    /// </summary>
    private const string ProcessMarkerKey = "process_marker";
    private const string LogTag = "WayfarerMainActivity";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Issue #185: Detect Activity recreation after OnDestroy.
        // When Activity is destroyed but process stays alive, Singleton services
        // hold stale IServiceProvider references. Restart to get clean state.
        if (s_activityWasDestroyed)
        {
            Log.Warn(LogTag, "Activity recreated after OnDestroy - restarting for clean state (issue #185)");
            Serilog.Log.Warning("Activity recreated after OnDestroy - restarting for clean state (issue #185)");
            s_activityWasDestroyed = false; // Reset to avoid infinite restart loop
            RestartApp();
            return;
        }

        // Issue #185: Install SafeSynchronizationContext FIRST, before any async operations.
        // This wraps SyncContext.Post to catch ObjectDisposedException from IImageHandler.FireAndForget.
        // The exception occurs when async image callbacks fire after IServiceProvider is disposed.
        SafeSynchronizationContext.Install();

        // Issue #185: Register Android-specific exception handler as a backup.
        // The SafeSynchronizationContext should catch exceptions in Post callbacks,
        // but we keep these handlers as a defense-in-depth measure.
        RegisterImageHandlerExceptionSuppressor();

        // Early check for corrupted state before MAUI initializes.
        // This handles the case where app data was cleared while the process was alive.
        if (DetectAndRecoverFromCorruptedState())
        {
            // State was corrupted - we've cleared it, now restart the app process
            Log.Warn(LogTag, "Corrupted state detected - restarting app");
            RestartApp();
            return;
        }

        base.OnCreate(savedInstanceState);

        ConfigureStatusBar();
    }

    protected override void OnDestroy()
    {
        // Mark that Activity was destroyed. If OnCreate is called again in this process,
        // we know we have an Activity recreation scenario with stale Singleton services.
        s_activityWasDestroyed = true;
        Log.Debug(LogTag, "Activity destroyed - marking for potential restart on recreation");

        base.OnDestroy();
    }

    /// <summary>
    /// Issue #185: Registers exception handlers that suppress ObjectDisposedException
    /// from IImageHandler.FireAndForget. This is a MAUI framework bug where async image
    /// load callbacks fire after IServiceProvider is disposed during Activity recreation.
    /// </summary>
    private static void RegisterImageHandlerExceptionSuppressor()
    {
        if (s_exceptionHandlerRegistered)
            return;

        s_exceptionHandlerRegistered = true;

        // Handler 1: AndroidEnvironment for managed exceptions
        AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
        {
            var ex = args.Exception;

            if (IsImageHandlerDisposedException(ex))
            {
                Log.Warn(LogTag, "Suppressed IImageHandler ObjectDisposedException via AndroidEnvironment (MAUI bug, issue #185)");
                Serilog.Log.Warning("Suppressed IImageHandler ObjectDisposedException via AndroidEnvironment (MAUI bug, issue #185)");
                args.Handled = true;
                return;
            }

            Log.Error(LogTag, $"Unhandled exception: {ex.GetType().Name}: {ex.Message}");
        };

        // Handler 2: Java Thread UncaughtExceptionHandler for exceptions from SyncContext.Post
        var defaultHandler = Java.Lang.Thread.DefaultUncaughtExceptionHandler;
        Java.Lang.Thread.DefaultUncaughtExceptionHandler = new ImageHandlerExceptionHandler(defaultHandler);

        Log.Debug(LogTag, "IImageHandler exception suppressor registered (both AndroidEnvironment and Java Thread handlers)");
    }

    /// <summary>
    /// Checks if the exception is the specific ObjectDisposedException from IImageHandler.FireAndForget.
    /// </summary>
    private static bool IsImageHandlerDisposedException(Exception? ex)
    {
        if (ex is not ObjectDisposedException disposedEx)
            return false;

        // Check the stack trace for IImageHandler.FireAndForget pattern
        var stackTrace = ex.StackTrace;
        if (stackTrace == null)
            return false;

        // The crash always has this pattern in the stack trace:
        // - CreateLogger[IImageHandler]
        // - FireAndForget
        return stackTrace.Contains("IImageHandler") &&
               stackTrace.Contains("FireAndForget");
    }

    /// <summary>
    /// Issue #185: Custom Java UncaughtExceptionHandler that suppresses IImageHandler
    /// ObjectDisposedException. This catches exceptions from Android.App.SyncContext.Post
    /// that bypass AndroidEnvironment.UnhandledExceptionRaiser.
    /// </summary>
    private class ImageHandlerExceptionHandler : Java.Lang.Object, Java.Lang.Thread.IUncaughtExceptionHandler
    {
        private readonly Java.Lang.Thread.IUncaughtExceptionHandler? _defaultHandler;

        public ImageHandlerExceptionHandler(Java.Lang.Thread.IUncaughtExceptionHandler? defaultHandler)
        {
            _defaultHandler = defaultHandler;
        }

        public void UncaughtException(Java.Lang.Thread t, Java.Lang.Throwable e)
        {
            // Log ALL exceptions that reach this handler for diagnostics
            var message = e.Message ?? "";
            var fullTrace = e.ToString() ?? "";

            Log.Warn(LogTag, $"Java UncaughtExceptionHandler invoked: {e.GetType().Name}");
            Log.Warn(LogTag, $"Message: {message}");
            Serilog.Log.Warning("Java UncaughtExceptionHandler invoked: {ExType}, Message: {Msg}", e.GetType().Name, message);

            // Check if this is a fire-and-forget ObjectDisposedException (MAUI bug)
            // ONLY suppress FireAndForget callbacks - NOT actual UI rendering exceptions
            var isFireAndForgetException =
                message.Contains("ObjectDisposed") &&
                message.Contains("IServiceProvider") &&
                fullTrace.Contains("FireAndForget");

            if (isFireAndForgetException)
            {
                Log.Warn(LogTag, "Suppressed ObjectDisposedException via Java UncaughtExceptionHandler (MAUI bug, issue #185)");
                Serilog.Log.Warning("Suppressed ObjectDisposedException via Java UncaughtExceptionHandler (MAUI bug, issue #185)");
                // Don't call default handler - suppress the crash
                return;
            }

            // For all other exceptions, delegate to the default handler
            Log.Warn(LogTag, "Delegating to default handler");
            _defaultHandler?.UncaughtException(t, e);
        }
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
