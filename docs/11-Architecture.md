# Architecture

This document describes the system architecture of WayfarerMobile, including project structure, design patterns, and platform-specific implementations.

## Solution Structure

WayfarerMobile follows a multi-project solution structure:

```
WayfarerMobile/
+-- src/
|   +-- WayfarerMobile/              # Main MAUI application
|   |   +-- Core/                    # (Moved to separate project)
|   |   +-- Data/                    # Database entities and services
|   |   +-- Platforms/               # Platform-specific code
|   |   |   +-- Android/
|   |   |   +-- iOS/
|   |   +-- Services/                # Application services
|   |   +-- ViewModels/              # MVVM ViewModels
|   |   +-- Views/                   # XAML pages
|   |   +-- Shared/                  # Shared controls and converters
|   |   +-- Resources/               # Images, fonts, raw assets
|   |   +-- MauiProgram.cs           # DI configuration
|   |   +-- App.xaml.cs              # Application lifecycle
|   |
|   +-- WayfarerMobile.Core/         # Platform-agnostic library
|       +-- Algorithms/              # Geo calculations, pathfinding
|       +-- Enums/                   # Shared enumerations
|       +-- Helpers/                 # Utility classes
|       +-- Interfaces/              # Service contracts
|       +-- Models/                  # Domain models, DTOs
|       +-- Navigation/              # Navigation graph and routing
|
+-- tests/
    +-- WayfarerMobile.Tests/        # Unit tests (xUnit)
```

## MVVM Architecture

WayfarerMobile uses the **Model-View-ViewModel (MVVM)** pattern with CommunityToolkit.Mvvm for source generation.

### Components

```
+----------------+     +------------------+     +----------------+
|     View       |<--->|    ViewModel     |<--->|     Model      |
|   (XAML Page)  |     | (ObservableObj)  |     |   (Services)   |
+----------------+     +------------------+     +----------------+
        |                      |                       |
   UI binding           Commands/Props          Business logic
   Events only          State management        Data access
```

### ViewModel Pattern

ViewModels use CommunityToolkit.Mvvm attributes for automatic property and command generation:

```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly ILocationBridge _locationBridge;
    private readonly MapService _mapService;

    [ObservableProperty]
    private LocationData? _currentLocation;

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackingStatus = "Not tracking";

    public MainViewModel(ILocationBridge locationBridge, MapService mapService)
    {
        _locationBridge = locationBridge;
        _mapService = mapService;

        _locationBridge.LocationReceived += OnLocationReceived;
        _locationBridge.StateChanged += OnStateChanged;
    }

    [RelayCommand]
    private async Task StartTrackingAsync()
    {
        await _locationBridge.StartAsync();
        IsTracking = true;
    }

    [RelayCommand]
    private async Task StopTrackingAsync()
    {
        await _locationBridge.StopAsync();
        IsTracking = false;
    }

    private void OnLocationReceived(object? sender, LocationData location)
    {
        CurrentLocation = location;
        _mapService.UpdateLocation(location, centerMap: true);
    }
}
```

### Key ViewModels

| ViewModel | Responsibility |
|-----------|---------------|
| `MainViewModel` | Map display, location tracking, trip sidebar |
| `SettingsViewModel` | App configuration, tracking toggle |
| `TimelineViewModel` | Location history display and management |
| `TripsViewModel` | Trip listing, download, offline management |
| `GroupsViewModel` | Group selection, member locations |
| `NavigationHudViewModel` | Turn-by-turn navigation overlay |
| `OnboardingViewModel` | First-run permission flow |

## Dependency Injection

Services are registered in `MauiProgram.cs` using Microsoft.Extensions.DependencyInjection:

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureSyncfusionToolkit()
            .UseBarcodeReader();

        ConfigureServices(builder.Services);
        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure Services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<MapService>();

        // API Services
        services.AddSingleton<IApiClient, ApiClient>();
        services.AddSingleton<QueueDrainService>();
        services.AddSingleton<IGroupsService, GroupsService>();

        // Platform Services (conditional)
        #if ANDROID
        services.AddSingleton<ILocationBridge,
            WayfarerMobile.Platforms.Android.Services.LocationBridge>();
        #elif IOS
        services.AddSingleton<ILocationBridge,
            WayfarerMobile.Platforms.iOS.Services.LocationBridge>();
        #endif

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        // ... other ViewModels

        // Pages
        services.AddTransient<MainPage>();
        services.AddTransient<SettingsPage>();
        // ... other Pages
    }
}
```

## Location Tracking Architecture

The location tracking system uses a **service-owns-GPS** pattern where the platform-specific foreground service directly owns all GPS operations.

### Architecture Diagram

```
+------------------------------------------------------------------+
|                   Platform Foreground Service                      |
|   +------------------------------------------------------------+  |
|   | LocationTrackingService                                     |  |
|   |   - Owns GPS/FusedLocationProvider                          |  |
|   |   - Applies quality/threshold filters                       |  |
|   |   - Writes to SQLite queue                                  |  |
|   |   - Shows foreground notification                           |  |
|   |   - State machine: Starting -> Active -> Paused -> Stopped  |  |
|   +------------------------------------------------------------+  |
+------------------------------------------------------------------+
                              |
                    Static callbacks
                              |
+------------------------------------------------------------------+
|                   LocationBridge (ILocationBridge)                 |
|   - Receives location updates via static events                   |
|   - Exposes C# events for UI consumption                          |
|   - Sends commands (Start/Stop/Pause) to service                  |
+------------------------------------------------------------------+
                              |
                    C# Events/Properties
                              |
+------------------------------------------------------------------+
|                       ViewModel Layer                              |
|   - Subscribes to LocationBridge.LocationReceived                 |
|   - Updates CurrentLocation property                              |
|   - No direct GPS interaction                                     |
+------------------------------------------------------------------+
```

### Android Implementation

The Android `LocationTrackingService` is a foreground service that uses Google Play Services FusedLocationProviderClient when available:

**Key characteristics:**
- Declared with `ForegroundServiceType = TypeLocation`
- Uses `StartForeground()` within 5 seconds of start (Android requirement)
- Returns `StartCommandResult.Sticky` for automatic restart
- Supports performance modes: High (1s), Normal (sleep/wake optimization), PowerSaver (300s)
- Sleep/wake optimization uses ThresholdFilter as single source of truth for timing
- Best location tracking during wake phase with 120s timeout for bad GPS

**Source**: `src/WayfarerMobile/Platforms/Android/Services/LocationTrackingService.cs`

```csharp
[Service(
    Name = "com.wayfarer.mobile.LocationTrackingService",
    ForegroundServiceType = ForegroundService.TypeLocation,
    Exported = false)]
public class LocationTrackingService : Service, ILocationListener
{
    // Service owns GPS directly
    private IFusedLocationProviderClient? _fusedClient;
    private LocationManager? _locationManager;

    public override StartCommandResult OnStartCommand(Intent? intent, ...)
    {
        // Handle actions: START, STOP, PAUSE, RESUME
        // ...
        return StartCommandResult.Sticky;
    }

    private void StartTracking()
    {
        // CRITICAL: StartForeground within 5 seconds!
        var notification = CreateNotification("Starting...");
        StartForeground(NotificationId, notification);

        // Then start GPS
        StartLocationUpdates();
    }
}
```

### iOS Implementation

The iOS `LocationTrackingService` uses `CLLocationManager` with "Always" authorization:

**Key characteristics:**
- Uses `CLLocationManager` for location updates
- Enables `AllowsBackgroundLocationUpdates`
- Supports significant location changes for battery efficiency
- Uses `PausesLocationUpdatesAutomatically = false` for continuous tracking

**Source**: `src/WayfarerMobile/Platforms/iOS/Services/LocationTrackingService.cs`

### LocationBridge Pattern

The `ILocationBridge` interface provides cross-platform location communication:

```csharp
public interface ILocationBridge
{
    event EventHandler<LocationData>? LocationReceived;
    event EventHandler<TrackingState>? StateChanged;

    TrackingState CurrentState { get; }
    PerformanceMode CurrentMode { get; }
    LocationData? LastLocation { get; }

    Task StartAsync();
    Task StopAsync();
    Task PauseAsync();
    Task ResumeAsync();
    Task SetPerformanceModeAsync(PerformanceMode mode);
}
```

## Data Layer

### SQLite Database

The `DatabaseService` manages all SQLite operations using sqlite-net-pcl:

**Tables:**
| Table | Purpose |
|-------|---------|
| `QueuedLocations` | Location queue for server sync (max 25,000) |
| `LocalTimelineEntries` | Cached timeline for offline viewing |
| `AppSettings` | Key-value app settings |
| `DownloadedTrips` | Trip metadata and status |
| `TripTiles` | Cached map tiles for trips |
| `LiveTiles` | LRU cache for live map browsing |
| `OfflinePlaces` | Trip places for offline access |
| `OfflineSegments` | Trip segments for offline navigation |
| `OfflineAreas` | Trip regions for offline display |

**Source**: `src/WayfarerMobile/Data/Services/DatabaseService.cs`

### Local Timeline Lifecycle

The timeline uses an **offline-first** pattern with server enrichment:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Location Lifecycle                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  GPS Fix → ThresholdFilter → QueuedLocations → QueueDrainService         │
│                                    │                    │                │
│                                    │              (Server Sync)          │
│                                    ▼                    ▼                │
│                           LocalTimelineEntry ← ─ ─ Enrichment            │
│                                    │           (Address, Place,          │
│                                    │            Activity, etc.)          │
│                                    ▼                                     │
│                            TimelineDataService → UI Display              │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**Data Flow**:

1. **GPS Acquisition**: LocationTrackingService acquires GPS fix
2. **Queue Entry**: Location is queued to `QueuedLocations` table
3. **Server Sync**: `QueueDrainService` sends to server when online (via `/api/location/check-in`)
4. **Local Cache**: Entry is copied to `LocalTimelineEntries` for offline viewing
5. **Enrichment**: `TimelineDataService.EnrichFromServerAsync()` fetches:
   - Reverse-geocoded address
   - Place/region/country
   - Activity type
   - Timezone
6. **Merge Strategy**: Server enrichment merges into local entry, preserving local edits

**Enrichment Fields**:
| Field | Source | Notes |
|-------|--------|-------|
| `Address` | Server | Short address |
| `FullAddress` | Server | Complete address |
| `Place` | Server | Business/landmark name |
| `Region` | Server | State/province |
| `Country` | Server | Country name |
| `PostCode` | Server | Postal code |
| `ActivityType` | Server/User | Can be edited locally |
| `Timezone` | Server | IANA timezone ID |
| `Notes` | User | Local notes preserved over server |

### Settings Service

The `SettingsService` manages app configuration using MAUI Preferences and SecureStorage:

```csharp
public class SettingsService : ISettingsService
{
    // Stored in SecureStorage (encrypted)
    public string? ServerUrl { get; set; }
    public string? ApiToken { get; set; }

    // Stored in Preferences
    public bool TimelineTrackingEnabled { get; set; }
    public int LocationTimeThresholdMinutes { get; set; }
    public int LocationDistanceThresholdMeters { get; set; }
    public bool NavigationAudioEnabled { get; set; }
    // ...
}
```

## Service Layer

### Service Categories

| Category | Services |
|----------|----------|
| **API** | `ApiClient`, `GroupsService`, `GroupMemberManager` |
| **Sync** | `QueueDrainService`, `TripSyncCoordinator`, `TimelineSyncService`, `SyncEventBus` |
| **Maps** | `MapBuilder`, `LocationLayerService`, `TripLayerService`, `GroupLayerService`, `TimelineLayerService`, `DroppedPinLayerService` |
| **Navigation** | `TripNavigationService`, `OsrmRoutingService`, `RouteCacheService` |
| **Tiles** | `TileDownloadOrchestrator`, `DownloadStateService`, `DownloadStateManager`, `CacheLimitEnforcer` |
| **Trip** | `TripStateManager`, `TripContentService`, `TripMetadataBuilder`, `PlaceOperationsHandler`, `RegionOperationsHandler` |
| **Timeline** | `TimelineDataService`, `LocalTimelineStorageService`, `MutationQueueService` |
| **Security** | `AppLockService` |
| **Audio** | `NavigationAudioService`, `TextToSpeechService` |
| **Real-time** | `SseClient`, `SseClientFactory`, `VisitNotificationService` |

### HttpClient Configuration

HTTP clients are configured via `IHttpClientFactory` with named clients:

```csharp
// In MauiProgram.cs
services.AddHttpClient("WayfarerApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

services.AddHttpClient("Osrm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0");
});
```

## Navigation System

### Route Calculation Priority

The `TripNavigationService` calculates routes with the following priority:

1. **User Segments**: Trip-defined routes with polyline geometry (always preferred)
2. **Cached OSRM**: Previously fetched route if still valid
3. **OSRM Fetch**: Online route from `router.project-osrm.org`
4. **Direct Route**: Straight line with bearing + distance (offline fallback)

### Navigation Graph

Trip places and segments form a navigation graph for A* pathfinding:

```csharp
public class TripNavigationGraph
{
    public Dictionary<string, NavigationNode> Nodes { get; }
    public List<NavigationEdge> Edges { get; }

    public List<string> FindPath(string fromId, string toId);
    public NavigationNode? FindNearestNode(double lat, double lon);
    public bool IsWithinSegmentRoutingRange(double lat, double lon);
}
```

**Source**: `src/WayfarerMobile/Services/TripNavigationService.cs`

## UI Components

### Syncfusion Toolkit

The app uses Syncfusion MAUI Toolkit (MIT licensed) for enhanced UI:

| Component | Usage |
|-----------|-------|
| `SfNavigationDrawer` | Trip sidebar |
| `SfExpander` | Settings sections |
| `SfSwitch` | Toggle switches |
| `SfBusyIndicator` | Loading states |
| `SfListView` | Timeline, trip lists |

### Custom Controls

| Control | Purpose |
|---------|---------|
| `LoadingOverlay` | Full-page loading indicator |
| `OfflineBanner` | Connectivity status banner |
| `NavigationHud` | Turn-by-turn navigation overlay |
| `TripSidebar` | Trip places and segments drawer |

## Logging

Serilog is configured for file-based logging with rotation:

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        fileSizeLimitBytes: 10 * 1024 * 1024)
    .CreateLogger();
```

Log files are stored in `FileSystem.AppDataDirectory/logs/wayfarer-app-{date}.log`

## State Management

### Tracking State Machine

```
    +------------------+
    | NotInitialized   |  App just installed
    +--------+---------+
             |
      App starts
             |
    +--------v---------+
    | PermissionsNeeded|  Need to request permissions
    +--------+---------+
             |
      Permissions granted
             |
    +--------v---------+
    |      Ready       |  Has permissions, not tracking
    +--------+---------+
             |
      ACTION_START
             |
    +--------v---------+
    |     Starting     |  Transitioning to active
    +--------+---------+
             |
      GPS acquired
             |
    +--------v---------+
    |      Active      |<------- ACTION_RESUME
    +--------+---------+
             |
      ACTION_PAUSE
             |
    +--------v---------+
    |      Paused      |  Service alive, GPS stopped
    +--------+---------+
             |
      ACTION_STOP
             |
    +--------v---------+
    |     Stopped      |  Service stopped
    +------------------+
```

## Error Handling

### Exception Handler Service

Global exception handling via `IExceptionHandlerService`:

```csharp
public interface IExceptionHandlerService
{
    void HandleException(Exception ex, string? context = null);
    Task HandleExceptionAsync(Exception ex, string? context = null);
}
```

### API Retry with Polly

The `ApiClient` uses Polly for transient failure handling:

```csharp
_retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            .HandleResult(response => TransientStatusCodes.Contains(response.StatusCode))
    })
    .Build();
```

## Next Steps

- [Services Documentation](12-Services.md) - Detailed service descriptions
- [API Integration](13-API.md) - Backend communication details
- [Testing](14-Testing.md) - Testing strategies and structure
