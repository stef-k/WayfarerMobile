using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Syncfusion.Maui.Toolkit.Hosting;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services;
using WayfarerMobile.Services.Security;
using WayfarerMobile.Services.TileCache;
using WayfarerMobile.ViewModels;
using WayfarerMobile.Views;
using WayfarerMobile.Views.Onboarding;
using ZXing.Net.Maui.Controls;

namespace WayfarerMobile;

/// <summary>
/// MAUI application entry point and dependency injection configuration.
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// Creates and configures the MAUI application.
    /// </summary>
    /// <returns>Configured MauiApp instance.</returns>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureSyncfusionToolkit()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Configure Serilog file-based logging
        ConfigureSerilog(builder.Logging);

        // Register Services
        ConfigureServices(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Configures Serilog for file-based logging with rotation.
    /// </summary>
    /// <param name="logging">The logging builder to configure.</param>
    private static void ConfigureSerilog(ILoggingBuilder logging)
    {
        // Get the app data directory for logs
        var logDirectory = Path.Combine(FileSystem.AppDataDirectory, "logs");

        // Ensure directory exists
        Directory.CreateDirectory(logDirectory);

        var logPath = Path.Combine(logDirectory, "wayfarer-.log");

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "WayfarerMobile")
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Add Serilog to the logging pipeline
        logging.AddSerilog(Log.Logger, dispose: true);
    }

    /// <summary>
    /// Configures dependency injection services.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure Services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<LocationIndicatorService>();
        services.AddSingleton<MapService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // API and Sync Services
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<LocationSyncService>();
        services.AddSingleton<IGroupsService, GroupsService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<TripDownloadService>();

        // Routing Services
        services.AddSingleton<HttpClient>();
        services.AddSingleton<OsrmRoutingService>();
        services.AddSingleton<RouteCacheService>();
        services.AddSingleton<TripNavigationService>();

        // Permissions Service
        services.AddSingleton<IPermissionsService, PermissionsService>();

        // Security Services
        services.AddSingleton<IAppLockService, AppLockService>();

        // Audio Services
        services.AddSingleton<ITextToSpeechService, TextToSpeechService>();
        services.AddSingleton<INavigationAudioService, NavigationAudioService>();

        // Lifecycle Services
        services.AddSingleton<IAppLifecycleService, AppLifecycleService>();

        // Exception Handling
        services.AddSingleton<IExceptionHandlerService, ExceptionHandlerService>();

        // Tile Cache Services
        services.AddSingleton<LiveTileCacheService>();
        services.AddSingleton<UnifiedTileCacheService>();

        // Platform Services (registered conditionally)
#if ANDROID
        services.AddSingleton<ILocationBridge, WayfarerMobile.Platforms.Android.Services.LocationBridge>();
        services.AddSingleton<IWakeLockService, WayfarerMobile.Platforms.Android.Services.WakeLockService>();
#elif IOS
        services.AddSingleton<ILocationBridge, WayfarerMobile.Platforms.iOS.Services.LocationBridge>();
        services.AddSingleton<IWakeLockService, WayfarerMobile.Platforms.iOS.Services.WakeLockService>();
#endif

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<GroupsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<TimelineViewModel>();
        services.AddTransient<CheckInViewModel>();
        services.AddTransient<TripsViewModel>();
        services.AddTransient<PinSecurityViewModel>();
        services.AddSingleton<NavigationHudViewModel>();
        services.AddTransient<QrScannerViewModel>();
        services.AddTransient<PublicTripsViewModel>();
        services.AddTransient<LockScreenViewModel>();
        services.AddTransient<AboutViewModel>();

        // Pages
        services.AddTransient<MainPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<GroupsPage>();
        services.AddTransient<OnboardingPage>();
        services.AddTransient<TimelinePage>();
        services.AddTransient<CheckInPage>();
        services.AddTransient<TripsPage>();
        services.AddTransient<QrScannerPage>();
        services.AddTransient<PublicTripsPage>();
        services.AddTransient<LockScreenPage>();
        services.AddTransient<AboutPage>();

        // Route Registration
        Routing.RegisterRoute("checkin", typeof(CheckInPage));
        Routing.RegisterRoute("trips", typeof(TripsPage));
        Routing.RegisterRoute("QrScanner", typeof(QrScannerPage));
        Routing.RegisterRoute("publictrips", typeof(PublicTripsPage));
        Routing.RegisterRoute("lockscreen", typeof(LockScreenPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
    }
}
