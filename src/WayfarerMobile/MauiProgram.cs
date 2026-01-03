using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Syncfusion.Maui.Toolkit.Hosting;
using WayfarerMobile.Core.Algorithms;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Services;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Handlers;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Services;
using WayfarerMobile.Services.Security;
using WayfarerMobile.Services.TileCache;
using WayfarerMobile.ViewModels;
using WayfarerMobile.ViewModels.Settings;
using WayfarerMobile.Views;
using WayfarerMobile.Views.Onboarding;
using SkiaSharp.Views.Maui.Controls.Hosting;
using ZXing.Net.Maui.Controls;
using SQLite;

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
            .UseSkiaSharp()
            .ConfigureSyncfusionToolkit()
            .UseBarcodeReader()
            .ConfigureMauiHandlers(handlers =>
            {
                // Register custom WebView handler to enable external content loading
                handlers.AddHandler<WebView, CustomWebViewHandler>();
            })
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

        // Clean up old-format log files (wayfarer-YYYYMMDD.log without "app-" prefix)
        // This is a one-time migration from the old naming convention
        CleanupOldLogFiles(logDirectory);

        var logPath = Path.Combine(logDirectory, "wayfarer-app-.log");

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
    /// Cleans up old-format log files that use the wayfarer-YYYYMMDD.log naming convention.
    /// These are migrated to wayfarer-app-YYYYMMDD.log to avoid collision with backend logs.
    /// </summary>
    /// <param name="logDirectory">The log directory to clean.</param>
    private static void CleanupOldLogFiles(string logDirectory)
    {
        try
        {
            // Match old format: wayfarer-YYYYMMDD.log (but not wayfarer-app-*)
            var oldLogFiles = Directory.GetFiles(logDirectory, "wayfarer-*.log")
                .Where(f => !Path.GetFileName(f).StartsWith("wayfarer-app-", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in oldLogFiles)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore individual file deletion errors (file may be in use)
                }
            }

            // Silently clean up old log files during startup
        }
        catch
        {
            // Ignore cleanup errors - not critical
        }
    }

    /// <summary>
    /// Configures dependency injection services.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Configure named HttpClient instances via IHttpClientFactory
        ConfigureHttpClients(services);

        // Platform Services (must be registered first - many services depend on these)
#if ANDROID
        services.AddSingleton<ILocationBridge, WayfarerMobile.Platforms.Android.Services.LocationBridge>();
        services.AddSingleton<IWakeLockService, WayfarerMobile.Platforms.Android.Services.WakeLockService>();
        services.AddSingleton<ILocalNotificationService, WayfarerMobile.Platforms.Android.Services.LocalNotificationService>();
#elif IOS
        services.AddSingleton<ILocationBridge, WayfarerMobile.Platforms.iOS.Services.LocationBridge>();
        services.AddSingleton<IWakeLockService, WayfarerMobile.Platforms.iOS.Services.WakeLockService>();
        services.AddSingleton<ILocalNotificationService, WayfarerMobile.Platforms.iOS.Services.LocalNotificationService>();
#endif

        // Infrastructure Services
        services.AddSingleton<DatabaseService>();

        // Database connection factory for repositories
        services.AddSingleton<Func<Task<SQLiteAsyncConnection>>>(sp =>
            () => sp.GetRequiredService<DatabaseService>().GetConnectionAsync());

        // Repositories (all singletons - shared connection)
        services.AddSingleton<ILocationQueueRepository, LocationQueueRepository>();
        services.AddSingleton<ITimelineRepository, TimelineRepository>();
        services.AddSingleton<ILiveTileCacheRepository, LiveTileCacheRepository>();
        services.AddSingleton<ITripTileRepository, TripTileRepository>();
        services.AddSingleton<IDownloadStateRepository, DownloadStateRepository>();
        services.AddSingleton<IPlaceRepository, PlaceRepository>();
        services.AddSingleton<ISegmentRepository, SegmentRepository>();
        services.AddSingleton<IAreaRepository, AreaRepository>();
        services.AddSingleton<ITripRepository, TripRepository>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITripStateManager, TripStateManager>();
        services.AddSingleton<IDownloadProgressAggregator, DownloadProgressAggregator>();
        services.AddSingleton<ISyncEventBus, SyncEventBus>();
        services.AddSingleton<IMutationQueueService, MutationQueueService>();

        // MAUI Essentials Services (for DI injection)
        services.AddSingleton<IConnectivity>(Connectivity.Current);

        // API and Sync Services (some needed by tile cache)
        services.AddSingleton<ApiClient>();
        services.AddSingleton<IApiClient>(sp => sp.GetRequiredService<ApiClient>());
        services.AddSingleton<IVisitApiClient>(sp => sp.GetRequiredService<ApiClient>());
        services.AddSingleton<LocationSyncService>();
        services.AddSingleton<QueueDrainService>(); // Drains offline queue via check-in endpoint
        services.AddSingleton<IPlaceOperationsHandler, PlaceOperationsHandler>();
        services.AddSingleton<IRegionOperationsHandler, RegionOperationsHandler>();
        services.AddSingleton<ITripEntityOperationsHandler, TripEntityOperationsHandler>();
        services.AddSingleton<ITripSyncService, TripSyncService>();
        services.AddSingleton<ITimelineSyncService, TimelineSyncService>();
        services.AddSingleton<IActivitySyncService, ActivitySyncService>();
        services.AddSingleton<SettingsSyncService>(); // Syncs settings + activities every 6 hours
        services.AddSingleton<IGroupsService, GroupsService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ITileDownloadService, TileDownloadService>();
        services.AddSingleton<IDownloadStateManager, DownloadStateManager>();
        services.AddSingleton<ICacheLimitEnforcer, CacheLimitEnforcer>();
        services.AddSingleton<ITripMetadataBuilder, TripMetadataBuilder>();
        services.AddSingleton<ITripContentService, TripContentService>();
        services.AddSingleton<ITileDownloadOrchestrator, TileDownloadOrchestrator>();
        services.AddSingleton<TripDownloadService>();
        // Also register as interface for consumers that prefer interface injection
        services.AddSingleton<ITripDownloadService>(sp => sp.GetRequiredService<TripDownloadService>());

        // Trip editing and sync coordination (extracted from TripDownloadService)
        services.AddSingleton<ITripEditingService, TripEditingService>();
        services.AddSingleton<ITripSyncCoordinator, TripSyncCoordinator>();

        // Local Timeline Services (offline-first timeline storage)
        services.AddSingleton<LocalTimelineFilter>();
        services.AddSingleton<LocalTimelineStorageService>();
        services.AddSingleton<TimelineDataService>();
        services.AddTransient<TimelineExportService>();
        services.AddTransient<TimelineImportService>();

        // Tile Cache Services (depends on TripDownloadService, ILocationBridge)
        services.AddSingleton<LiveTileCacheService>();
        services.AddSingleton<UnifiedTileCacheService>();
        services.AddSingleton<WayfarerTileSource>();
        services.AddSingleton<Services.TileCache.CacheOverlayService>();
        services.AddSingleton<CacheStatusService>(); // Subscribes to LocationBridge for cache health updates
        services.AddSingleton<Services.TileCache.CacheVisualizationService>();
        services.AddSingleton<ICacheVisualizationService>(sp =>
            sp.GetRequiredService<Services.TileCache.CacheVisualizationService>());

        // Map Services (depends on tile cache)
        services.AddSingleton<LocationIndicatorService>();
        services.AddTransient<IMapBuilder, MapBuilder>(); // Each ViewModel gets its own Map instance

        // Feature-specific Layer Services
        services.AddSingleton<IGroupLayerService, GroupLayerService>(); // Stateless rendering
        services.AddSingleton<ILocationLayerService, LocationLayerService>(); // Stateful (animation)
        services.AddSingleton<IDroppedPinLayerService, DroppedPinLayerService>(); // Stateless rendering
        services.AddSingleton<ITripLayerService, TripLayerService>(); // Has icon cache
        services.AddSingleton<ITimelineLayerService, TimelineLayerService>(); // Stateless rendering

        // Routing Services
        services.AddSingleton<OsrmRoutingService>();
        services.AddSingleton<RouteCacheService>();
        services.AddSingleton<TripNavigationService>();
        services.AddSingleton<ITripNavigationService>(sp => sp.GetRequiredService<TripNavigationService>());

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

        // Diagnostic Services
        services.AddSingleton<DiagnosticService>();
        services.AddSingleton<AppDiagnosticService>();

        // Battery Monitor
        services.AddSingleton<BatteryMonitorService>();

        // Performance Monitor
        services.AddSingleton<PerformanceMonitorService>();

        // UI Services
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Wikipedia Service
        services.AddSingleton<IWikipediaService, WikipediaService>();

        // Download Notification Service
        services.AddSingleton<IDownloadNotificationService, DownloadNotificationService>();

        // SSE Client Factory (for real-time updates)
        services.AddSingleton<ISseClientFactory, SseClientFactory>();

        // Location Sync Event Bridge (bridges static callbacks to Core interface)
        services.AddSingleton<ILocationSyncEventBridge, LocationSyncEventBridge>();

        // Visit Notification Service (depends on SSE, TTS, LocalNotification, LocationSyncEventBridge)
        services.AddSingleton<IVisitNotificationService, VisitNotificationService>();

        // ViewModels
        services.AddSingleton<MapDisplayViewModel>();  // Map display and layer management
        services.AddSingleton<NavigationCoordinatorViewModel>();  // Navigation coordination
        services.AddSingleton<TripItemEditorViewModel>();  // Trip item editing operations
        services.AddSingleton<TripSheetViewModel>();  // Trip sheet display and selection
        services.AddSingleton<ContextMenuViewModel>();  // Context menu and dropped pin operations
        services.AddSingleton<TrackingCoordinatorViewModel>();  // Tracking lifecycle management
        services.AddSingleton<MainViewModel>();

        // Settings child ViewModels
        services.AddTransient<NavigationSettingsViewModel>();
        services.AddTransient<CacheSettingsViewModel>();
        services.AddTransient<VisitNotificationSettingsViewModel>();
        services.AddTransient<AppearanceSettingsViewModel>();
        services.AddTransient<TimelineDataViewModel>();
        services.AddTransient<SettingsViewModel>();
        // Groups child ViewModels (singletons for state sharing)
        services.AddSingleton<SseManagementViewModel>();
        services.AddSingleton<DateNavigationViewModel>();
        services.AddSingleton<MemberDetailsViewModel>();
        services.AddTransient<GroupsViewModel>();
        services.AddTransient<OnboardingViewModel>();

        // Timeline ViewModels with factory pattern for child VMs
        services.AddTransient<TimelineViewModel>(sp => new TimelineViewModel(
            sp.GetRequiredService<IApiClient>(),
            sp.GetRequiredService<DatabaseService>(),
            sp.GetRequiredService<ITimelineSyncService>(),
            sp.GetRequiredService<IToastService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IMapBuilder>(),
            sp.GetRequiredService<ITimelineLayerService>(),
            sp.GetRequiredService<TimelineDataService>(),
            callbacks => new CoordinateEditorViewModel(
                callbacks,
                sp.GetRequiredService<ITimelineSyncService>(),
                sp.GetRequiredService<IToastService>(),
                sp.GetRequiredService<ILogger<CoordinateEditorViewModel>>()),
            callbacks => new DateTimeEditorViewModel(
                callbacks,
                sp.GetRequiredService<ITimelineSyncService>(),
                sp.GetRequiredService<IToastService>(),
                sp.GetRequiredService<ILogger<DateTimeEditorViewModel>>()),
            sp.GetRequiredService<ILogger<TimelineViewModel>>()));

        services.AddTransient<CheckInViewModel>();

        // Trips ViewModels (coordinator pattern)
        services.AddSingleton<TripDownloadViewModel>();    // Download operations
        services.AddSingleton<MyTripsViewModel>();         // My Trips tab
        services.AddSingleton<PublicTripsViewModel>();     // Public Trips tab
        services.AddSingleton<TripsPageViewModel>();       // Coordinator

        services.AddTransient<PinSecurityViewModel>();
        services.AddSingleton<NavigationHudViewModel>();
        services.AddTransient<QrScannerViewModel>();
        services.AddTransient<LockScreenViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<NotesEditorViewModel>();
        services.AddTransient<MarkerEditorViewModel>();

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
        services.AddTransient<DiagnosticsPage>();
        services.AddTransient<NotesEditorPage>();
        services.AddTransient<MarkerEditorPage>();

        // Route Registration
        Routing.RegisterRoute("checkin", typeof(CheckInPage));
        Routing.RegisterRoute("trips", typeof(TripsPage));
        Routing.RegisterRoute("QrScanner", typeof(QrScannerPage));
        Routing.RegisterRoute("publictrips", typeof(PublicTripsPage));
        Routing.RegisterRoute("lockscreen", typeof(LockScreenPage));
        Routing.RegisterRoute("about", typeof(AboutPage));
        Routing.RegisterRoute("diagnostics", typeof(DiagnosticsPage));
        Routing.RegisterRoute("notesEditor", typeof(NotesEditorPage));
        Routing.RegisterRoute("markerEditor", typeof(MarkerEditorPage));
    }

    /// <summary>
    /// Configures named HttpClient instances using IHttpClientFactory.
    /// </summary>
    /// <param name="services">Service collection to configure.</param>
    private static void ConfigureHttpClients(IServiceCollection services)
    {
        // Register IHttpClientFactory
        services.AddHttpClient();

        // WayfarerApi - main API client with 30s timeout and isolated connection pool
        // Uses HTTP/2 for better performance and to ensure separation from SSE (HTTP/1.1)
        services.AddHttpClient("WayfarerApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestVersion = new Version(2, 0); // Force HTTP/2
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Isolated connection pool - won't compete with SSE connections
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
            MaxConnectionsPerServer = 10, // Allow parallel API requests
            ConnectTimeout = TimeSpan.FromSeconds(10)
        });

        // Wikipedia - geosearch API with 10s timeout
        services.AddHttpClient("Wikipedia", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0 (Location tracking app)");
        });

        // Tiles - map tile downloads with 60s timeout
        services.AddHttpClient("Tiles", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");
        });

        // Osrm - routing service with 30s timeout
        services.AddHttpClient("Osrm", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0");
        });

        // SSE - Server-Sent Events with isolated connection pool and long timeout
        // Uses HTTP/1.1 to ensure completely separate TCP connections from API calls
        // HTTP/2 multiplexing can cause SSE to block API requests on the same host
        services.AddHttpClient("SSE", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30); // Long timeout for SSE streams
            client.DefaultRequestVersion = new Version(1, 1); // Force HTTP/1.1
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            // Isolated connection pool - won't compete with API connections
            PooledConnectionLifetime = TimeSpan.FromMinutes(30),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 4, // Allow multiple SSE connections per group
            ConnectTimeout = TimeSpan.FromSeconds(10)
        });
    }
}
