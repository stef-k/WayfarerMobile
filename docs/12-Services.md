# Services

This document provides detailed documentation for the key services in WayfarerMobile.

## Service Overview

### Core Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `LocationTrackingService` | GPS acquisition, foreground notification | Platform singleton |
| `LocationBridge` | Platform-to-MAUI location bridge | Singleton |
| `MapBuilder` | Mapsui map creation, layers, navigation routes | Singleton |
| `DatabaseService` | SQLite operations | Singleton |
| `SettingsService` | App configuration | Singleton |
| `ApiClient` | Backend HTTP communication | Singleton |

### Map Layer Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `LocationLayerService` | User location indicator (blue dot, accuracy, heading) | Singleton |
| `TripLayerService` | Trip place markers and segment polylines | Singleton |
| `GroupLayerService` | Group member markers and breadcrumbs | Singleton |
| `TimelineLayerService` | Timeline location markers | Singleton |
| `DroppedPinLayerService` | Dropped pin marker (map long-press) | Singleton |

### Tile/Download Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `TileDownloadOrchestrator` | Batch tile downloads with pause/resume | Singleton |
| `DownloadStateService` | Download state transitions and validation | Singleton |
| `DownloadStateManager` | Pause/resume state, progress tracking | Singleton |
| `DownloadProgressAggregator` | Centralized download progress events | Singleton |
| `CacheLimitEnforcer` | Cache size limit enforcement (80%/90%/100%) | Singleton |

### Trip Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `TripStateManager` | Currently loaded trip state (thread-safe) | Singleton |
| `TripSyncCoordinator` | Trip sync with server (version checks) | Singleton |
| `TripContentService` | Trip metadata fetch, sync, offline retrieval | Singleton |
| `TripMetadataBuilder` | Builds offline entities from trip details | Singleton |
| `PlaceOperationsHandler` | Place CRUD with optimistic UI | Singleton |
| `RegionOperationsHandler` | Region CRUD with optimistic UI | Singleton |
| `TripEntityOperationsHandler` | Trip/Segment/Area entity updates | Singleton |

### Timeline Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `TimelineDataService` | Timeline data access with offline fallback | Singleton |
| `LocalTimelineStorageService` | Local timeline filtering and persistence | Singleton |
| `MutationQueueService` | Offline mutation queue management | Singleton |

### Event/Communication Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `SyncEventBus` | Centralized sync-related events | Singleton |
| `LocationSyncEventBridge` | Bridges static callbacks to interface | Singleton |
| `SseClient` | Server-Sent Events client | Per-instance |
| `SseClientFactory` | Creates SSE client instances | Singleton |

### Group Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `GroupsService` | Group data and member locations | Singleton |
| `GroupMemberManager` | Member operations, visibility, utilities | Singleton |

### Other Services

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `AppLockService` | App lock/PIN security | Singleton |
| `VisitNotificationService` | Visit notifications via SSE/polling | Singleton |

### Sync Services

| Service | Purpose | Sync Interval |
|---------|---------|---------------|
| `QueueDrainService` | Queue → server sync with continuous drain loop | 12s per location |
| `TimelineSyncService` | Optimistic UI mutations with background processing | Background (60s timer) |
| `TimelineDataService` | Server → local cache with enrichment | On-demand |
| `ActivitySyncService` | Activity types from server | Every 6 hours |
| `SettingsSyncService` | Threshold/config sync from server | On login |

### Navigation Services

| Service | Purpose | Rate Limit |
|---------|---------|------------|
| `TripNavigationService` | Route calculation, turn-by-turn | N/A |
| `OsrmRoutingService` | OSRM API client | 1 req/second |
| `RouteCacheService` | Single-route session cache | N/A |
| `NavigationAudioService` | Voice announcements | N/A |

### Data Services

| Service | Purpose | Notes |
|---------|---------|-------|
| `TimelineExportService` | CSV/GeoJSON export | Date range filter |
| `TimelineImportService` | CSV/GeoJSON import | Duplicate detection |
| `GroupsService` | SSE live location sharing | Real-time updates |
| `TripDownloadService` | Trip download with pause/resume | Zoom 8-17, cache limits |

---

## LocationTrackingService

**Source**: `src/WayfarerMobile/Platforms/Android/Services/LocationTrackingService.cs`

The LocationTrackingService is a platform-native foreground service that owns GPS acquisition.

### Android Implementation

```csharp
[Service(
    Name = "com.wayfarer.mobile.LocationTrackingService",
    ForegroundServiceType = ForegroundService.TypeLocation,
    Exported = false)]
public class LocationTrackingService : Service, ILocationListener
```

### Responsibilities

- **GPS Acquisition**: Uses FusedLocationProviderClient (Google Play Services) or standard LocationManager as fallback
- **Sleep/Wake Optimization**: Three-phase approach for battery efficiency in Normal mode
- **Quality Filtering**: Rejects locations with accuracy > 50m (configurable)
- **Threshold Filtering**: Applies server-configured time/distance thresholds via ThresholdFilter
- **Queue Writing**: Writes filtered locations to SQLite queue (max 25,000)
- **Notification Management**: Shows foreground notification with pause/stop actions and current accuracy
- **Performance Modes**: Supports High (1s), Normal (sleep/wake), PowerSaver (300s) intervals
- **Best Location Tracking**: During wake phase, tracks best GPS fix for logging

### Actions

| Action | Constant | Description |
|--------|----------|-------------|
| Start | `ACTION_START` | Start tracking and foreground service |
| Stop | `ACTION_STOP` | Stop tracking and service |
| Pause | `ACTION_PAUSE` | Pause GPS, keep service alive |
| Resume | `ACTION_RESUME` | Resume GPS from paused state |
| High Performance | `ACTION_SET_HIGH_PERFORMANCE` | Set 1-second updates |
| Normal | `ACTION_SET_NORMAL` | Set 60-second updates |

### Usage

The service is controlled via intents from `LocationBridge`:

```csharp
// Start tracking
var intent = new Intent(context, typeof(LocationTrackingService));
intent.SetAction(LocationTrackingService.ActionStart);
context.StartForegroundService(intent);
```

### Location Processing Flow

```
GPS Update Received
        |
        v
+-------------------+
| Quality Filter    |  Reject if accuracy > 50m (configurable)
+-------------------+
        |
        v
+-------------------+
| Update Best       |  Track best accuracy during wake phase
| Wake Location     |  (only in Normal mode wake phase)
+-------------------+
        |
        v
+-------------------+
| Threshold Filter  |  Check seconds until next log
| (Single Source    |  GetSecondsUntilNextLog()
| of Truth)         |
+-------------------+
        |
        v (passes filter)
+-------------------+
| Queue to SQLite   |  Write to QueuedLocations (max 25,000)
+-------------------+
        |
        v
+-------------------+
| Broadcast to UI   |  Via LocationServiceCallbacks
+-------------------+
        |
        v
+-------------------+
| Adjust Priority   |  Sleep/wake phase transition
| (Normal Mode)     |  HighAccuracy ↔ Balanced
+-------------------+
        |
        v
+-------------------+
| Update Notification|  Show "Last: HH:mm:ss (Xm accuracy)"
+-------------------+
```

### Sleep/Wake Optimization (Normal Mode)

The service uses ThresholdFilter as the **single source of truth** for timing:

| Phase | Seconds Until Log | Priority | Interval |
|-------|-------------------|----------|----------|
| Deep Sleep | >200s | Balanced | threshold - 200s |
| Approach | 100-200s | Balanced | 1s |
| Wake | ≤100s | HighAccuracy | 1s |

**Two-Tier Accuracy Thresholds**:
- **Excellent (≤20m)**: Early GPS shutoff, stored sample used at threshold time
- **Moderate/Poor (>20m)**: Proceeds to log at threshold time with best available

**Early GPS Shutoff**: When excellent GPS (≤20m) is acquired, immediately switches to Balanced mode. The stored sample is logged at threshold time, saving ~80+ seconds of GPS usage per cycle.

**Always On-Time**: Logging always occurs at threshold time regardless of GPS accuracy. A coarse location is better than no location for timeline continuity.

---

## LocationBridge

**Source**: `src/WayfarerMobile/Platforms/Android/Services/LocationBridge.cs`

The LocationBridge provides a cross-platform interface between the native service and MAUI code.

### Interface

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

### Communication Pattern

Uses static callbacks (`LocationServiceCallbacks`) for service-to-bridge communication:

```csharp
// In LocationBridge constructor
LocationServiceCallbacks.LocationReceived += OnLocationReceived;
LocationServiceCallbacks.StateChanged += OnStateChanged;

// In LocationTrackingService
LocationServiceCallbacks.NotifyLocationReceived(locationData);
LocationServiceCallbacks.NotifyStateChanged(state);
```

### Usage in ViewModels

```csharp
public class MainViewModel : ObservableObject
{
    private readonly ILocationBridge _locationBridge;

    public MainViewModel(ILocationBridge locationBridge)
    {
        _locationBridge = locationBridge;
        _locationBridge.LocationReceived += OnLocationReceived;
    }

    private void OnLocationReceived(object? sender, LocationData location)
    {
        // Update UI with new location
        CurrentLocation = location;
    }

    public async Task StartTracking()
    {
        await _locationBridge.StartAsync();
    }
}
```

---

## QueueDrainService

**Source**: `src/WayfarerMobile/Services/QueueDrainService.cs`

Manages synchronization of queued locations to the server using a continuous drain loop.

### Responsibilities

- Processes the location queue continuously when online
- Rate-limits requests to respect server policies (12s minimum interval)
- Handles transient failures with retry and exponential backoff
- Marks locations as synced or server-rejected
- Coordinates with background location service for piggyback syncing

### Sync Algorithm

```
Drain Loop Trigger
        |
        v
+-------------------+
| Claim Pending     |  SELECT WHERE SyncStatus = Pending
| Location          |  ORDER BY Timestamp LIMIT 1
+-------------------+
        |
        v
+-------------------+
| Send to Server    |  POST /api/location/log-location
+-------------------+
        |
   +----+----+
   |         |
Success    Failure
   |         |
   v         v
Mark      Increment
Synced    SyncAttempts
   |         |
   v         v
+-------------------+
| Check More        |  Loop continues until:
| Pending?          |  - Queue empty
+-------------------+  - Device offline
        |              - Too many failures
        v              - Service disposed
  (12s delay, then
   next location)
```

### Rate Limiting

| Setting | Value | Notes |
|---------|-------|-------|
| Minimum interval | 12 seconds | Server allows 10s, 2s safety margin |
| Sync time for 100 locations | ~17 minutes | Improved from ~50 minutes (65s interval) |

### Sync Status

| Status | Description |
|--------|-------------|
| `Pending` | Awaiting sync |
| `Syncing` | Currently being sent |
| `Synced` | Successfully sent to server |
| `Rejected` | Server rejected (4xx response) |

### Rejection Handling

Locations can be rejected by client threshold checks or server validation:

```csharp
// Client rejection (threshold not met)
await _repository.MarkLocationRejectedAsync(id, "Client: Time 2.3min below 5min threshold");

// Server rejection (4xx response)
await _repository.MarkLocationRejectedAsync(id, "Server: HTTP 400 Bad Request");
```

Rejected locations are not retried and are removed during rolling buffer cleanup.

---

## MapBuilder

**Source**: `src/WayfarerMobile/Services/MapBuilder.cs`

Injectable service for creating Mapsui map instances and managing map configuration.

### Interface

```csharp
public interface IMapBuilder
{
    Map CreateMap();
    WritableLayer CreateLayer(string name);
    void UpdateNavigationRoute(Map map, NavigationRoute route);
    void UpdateNavigationRouteProgress(Map map, double lat, double lon);
    void CenterOnLocation(Map map, double lat, double lon, double? resolution = null);
    void ZoomToPoints(Map map, IEnumerable<(double lat, double lon)> points, double padding);
    ViewportBounds? GetViewportBounds(Map map);
}
```

### Responsibilities

- Creates Map instances with tile layer and additional feature layers
- Manages navigation route rendering with progress tracking
- Provides viewport manipulation (center, zoom, bounds)
- Decoupled from per-feature layer management (handled by layer services)

### Usage

```csharp
public class MainViewModel : ObservableObject
{
    private readonly IMapBuilder _mapBuilder;

    public MainViewModel(IMapBuilder mapBuilder)
    {
        _mapBuilder = mapBuilder;
        Map = _mapBuilder.CreateMap();
    }

    public Map Map { get; }
}
```

---

## Layer Services

The map uses separate layer services for each feature domain, following the single-responsibility principle.

### LocationLayerService

**Source**: `src/WayfarerMobile/Services/LocationLayerService.cs`

Manages the current user location indicator on the map.

**Components**:
- **Accuracy Circle**: Semi-transparent circle showing GPS accuracy
- **Heading Cone**: Direction indicator (30-90 degrees based on calibration)
- **Center Marker**: Blue dot with white border

```csharp
public interface ILocationLayerService
{
    void UpdateLocation(WritableLayer layer, LocationData location);
    void Clear(WritableLayer layer);
}
```

### TripLayerService

**Source**: `src/WayfarerMobile/Services/TripLayerService.cs`

Manages trip place markers and segment polylines.

**Features**:
- Dynamic icon caching with colorization
- Place selection highlighting
- Transport mode styling for segments

| Mode | Color | Style |
|------|-------|-------|
| Driving | Blue | Solid |
| Walking | Green | Dashed |
| Cycling | Orange | Solid |
| Transit | Purple | Solid |
| Ferry | Teal | Dashed |
| Flight | Light Blue | Dotted |

### GroupLayerService

**Source**: `src/WayfarerMobile/Services/GroupLayerService.cs`

Manages group member markers and historical location breadcrumbs.

**Features**:
- Live vs latest location indicators
- Pulse animation for live locations
- Member color coding

### TimelineLayerService

**Source**: `src/WayfarerMobile/Services/TimelineLayerService.cs`

Manages timeline location markers for the timeline page. Pure rendering service (stateless).

### DroppedPinLayerService

**Source**: `src/WayfarerMobile/Services/DroppedPinLayerService.cs`

Manages the dropped pin marker for map long-press interactions. Stateless rendering service.

---

## TripNavigationService

**Source**: `src/WayfarerMobile/Services/TripNavigationService.cs`

Provides navigation with route calculation and progress tracking. Supports two modes:

### Navigation Modes

**Trip Navigation** (`CalculateRouteToPlaceAsync`):
- Used when navigating to a trip place
- Has access to user-defined segments and trip context
- Route priority:
  1. User Segments (trip-defined routes)
  2. Cached OSRM (valid cache)
  3. OSRM Fetch (online)
  4. Direct Route (offline fallback)

**Ad-Hoc Navigation** (`CalculateRouteToCoordinatesAsync`):
- Used for groups, map locations, any coordinates
- No trip context available
- Route priority:
  1. OSRM Fetch (online)
  2. Direct Route (offline fallback)

```csharp
// Trip navigation - uses full route priority chain
var route = await _tripNavigationService.CalculateRouteToPlaceAsync(
    currentLat, currentLon, destinationPlaceId);

// Ad-hoc navigation - OSRM or direct only
var route = await _tripNavigationService.CalculateRouteToCoordinatesAsync(
    currentLat, currentLon, destLat, destLon, destName, profile: "foot");
```

### Navigation Graph

```csharp
public class TripNavigationGraph
{
    public Dictionary<string, NavigationNode> Nodes { get; }
    public List<NavigationEdge> Edges { get; }

    public List<string> FindPath(string fromId, string toId);
    public NavigationNode? FindNearestNode(double lat, double lon);
}
```

### Route Calculation

```csharp
public async Task<NavigationRoute?> CalculateRouteToPlaceAsync(
    double currentLat, double currentLon,
    string destinationPlaceId,
    bool fetchFromOsrm = true)
{
    // Priority 1: User-defined segment
    if (_currentGraph.IsWithinSegmentRoutingRange(currentLat, currentLon))
    {
        var path = _currentGraph.FindPath(nearestNodeId, destinationPlaceId);
        if (path.Count > 0)
            return BuildRouteFromPath(path, currentLat, currentLon);
    }

    // Priority 2: Cached OSRM route
    var cachedRoute = _routeCacheService.GetValidRoute(currentLat, currentLon, destinationPlaceId);
    if (cachedRoute != null)
        return BuildRouteFromCache(cachedRoute, ...);

    // Priority 3: OSRM fetch
    if (fetchFromOsrm)
    {
        var osrmRoute = await _osrmService.GetRouteAsync(...);
        if (osrmRoute != null)
        {
            _routeCacheService.SaveRoute(...);
            return BuildRouteFromOsrm(osrmRoute, ...);
        }
    }

    // Priority 4: Direct route
    return BuildDirectRoute(currentLat, currentLon, destination);
}
```

### Navigation State

```csharp
public TripNavigationState UpdateLocation(double currentLat, double currentLon)
{
    // Calculate distances
    state.DistanceToDestinationMeters = GeoMath.CalculateDistance(...);
    state.BearingToDestination = GeoMath.CalculateBearing(...);

    // Check for arrival
    if (state.DistanceToDestinationMeters <= 50)
    {
        state.Status = NavigationStatus.Arrived;
        return state;
    }

    // Check off-route
    if (_currentGraph.IsOffRoute(currentLat, currentLon, currentEdge))
    {
        state.Status = NavigationStatus.OffRoute;
        // Trigger reroute
    }

    // On route - calculate progress
    state.Status = NavigationStatus.OnRoute;
    state.ProgressPercent = CalculateRouteProgress(currentLat, currentLon);

    return state;
}
```

### Turn Announcements

Announcements are triggered when within 100m of a named waypoint:

```csharp
private void CheckForTurnAnnouncement(double lat, double lon, TripNavigationState state)
{
    if (timeSinceLastAnnouncement.TotalSeconds < 15)
        return;

    var nextWaypoint = _activeRoute.Waypoints
        .Where(w => !string.IsNullOrEmpty(w.Name))
        .FirstOrDefault(w => distance > 20 && distance <= 100);

    if (nextWaypoint != null && nextWaypoint.Name != _lastAnnouncedWaypoint)
    {
        AnnounceInstruction($"In {distance}m, head to {nextWaypoint.Name}");
    }
}
```

---

## GroupsService

**Source**: `src/WayfarerMobile/Services/GroupsService.cs`

Manages group data and member locations.

### Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/mobile/groups?scope=all` | List user's groups |
| GET | `/api/mobile/groups/{id}/members` | Get group members |
| POST | `/api/mobile/groups/{id}/locations/latest` | Get member locations |

### Usage

```csharp
public class GroupsViewModel : ObservableObject
{
    private readonly IGroupsService _groupsService;

    [RelayCommand]
    private async Task LoadGroupsAsync()
    {
        Groups = await _groupsService.GetGroupsAsync();
    }

    [RelayCommand]
    private async Task SelectGroupAsync(GroupSummary group)
    {
        var members = await _groupsService.GetGroupMembersAsync(group.Id);
        var locations = await _groupsService.GetLatestLocationsAsync(group.Id);

        // Update map with member locations
        foreach (var member in members)
        {
            if (locations.TryGetValue(member.UserId, out var loc))
            {
                _mapService.AddGroupMemberMarker(member, loc);
            }
        }
    }
}
```

---

## TripDownloadService

**Source**: `src/WayfarerMobile/Services/TripDownloadService.cs`

Manages trip downloads with pause/resume support and cache limit enforcement.

### Features

- **Two download modes**: Metadata-only or full offline maps
- **Pause/Resume**: Downloads can be paused and resumed, even after app restart
- **Cache limits**: Automatic pause when trip cache limit is reached
- **Progress tracking**: Real-time progress with tile counts and bytes
- **Concurrent downloads**: Parallel tile fetching with configurable concurrency

### Download States

| Status | Description |
|--------|-------------|
| `pending` | Queued for download |
| `downloading` | Actively downloading |
| `complete` | All tiles downloaded |
| `metadata_only` | Trip data without tiles |
| `failed` | Download failed |
| `cancelled` | User cancelled |

### Events

| Event | When Raised |
|-------|-------------|
| `ProgressChanged` | Tile download progress update |
| `CacheWarning` | Cache usage at 80% |
| `CacheCritical` | Cache usage at 90% |
| `CacheLimitReached` | Cache full, download paused |
| `DownloadCompleted` | Download finished successfully |
| `DownloadFailed` | Download failed with error |
| `DownloadPaused` | Download paused (user or limit) |

### Pause Reasons

| Reason | Description | Can Resume |
|--------|-------------|------------|
| `UserRequest` | User tapped pause | Yes |
| `UserCancel` | User cancelled | No |
| `NetworkLost` | Connection lost | Yes |
| `StorageLow` | Device storage low | Yes |
| `CacheLimitReached` | Trip cache full | Yes (after freeing space) |

### Key Methods

```csharp
// Download trip (metadata + optionally tiles)
Task<DownloadedTripEntity?> DownloadTripAsync(TripSummary trip, CancellationToken ct);

// Pause/Resume/Cancel
Task<bool> PauseDownloadAsync(int tripId);
Task<bool> ResumeDownloadAsync(int tripId, CancellationToken ct);
Task<bool> CancelDownloadAsync(int tripId, bool cleanup);

// Delete operations
Task DeleteTripAsync(Guid tripServerId);           // Remove everything
Task<int> DeleteTripTilesAsync(Guid tripServerId); // Remove tiles only

// Cache management
Task<CacheLimitCheckResult> CheckTripCacheLimitAsync();
Task<CacheQuotaCheckResult> CheckCacheQuotaForTripAsync(BoundingBox? bbox);
```

### Tile Download Configuration

| Setting | Value |
|---------|-------|
| Zoom levels | 8-17 |
| Concurrent downloads | 4 tiles |
| Checkpoint interval | Every 50 tiles |
| Estimated tile size | 40 KB (urban areas) |

---

## DatabaseService

**Source**: `src/WayfarerMobile/Data/Services/DatabaseService.cs`

Manages all SQLite database operations.

### Initialization

```csharp
private async Task EnsureInitializedAsync()
{
    _database = new SQLiteAsyncConnection(DatabasePath, DbFlags);

    await _database.CreateTableAsync<QueuedLocation>();
    await _database.CreateTableAsync<AppSetting>();
    await _database.CreateTableAsync<DownloadedTripEntity>();
    await _database.CreateTableAsync<TripTileEntity>();
    await _database.CreateTableAsync<OfflinePlaceEntity>();
    await _database.CreateTableAsync<OfflineSegmentEntity>();
    await _database.CreateTableAsync<OfflineAreaEntity>();
    await _database.CreateTableAsync<LiveTileEntity>();
}
```

### Location Queue Operations

```csharp
// Queue a location
await _databaseService.QueueLocationAsync(locationData);

// Claim pending locations for sync (repository)
var claimed = await _locationQueueRepository.ClaimPendingLocationsAsync(limit: 100);

// Mark as synced
await _databaseService.MarkLocationSyncedAsync(id);

// Batch mark as synced
await _databaseService.MarkLocationsSyncedAsync(ids);

// Mark as server rejected
await _databaseService.MarkLocationServerRejectedAsync(id, "threshold");

// Purge old synced locations
int deleted = await _databaseService.PurgeSyncedLocationsAsync(daysOld: 7);
```

### Trip Cache Operations

```csharp
// Get downloaded trips
var trips = await _databaseService.GetDownloadedTripsAsync();

// Get trip by server ID
var trip = await _databaseService.GetDownloadedTripByServerIdAsync(serverId);

// Save trip
await _databaseService.SaveDownloadedTripAsync(tripEntity);

// Delete trip and associated data
await _databaseService.DeleteDownloadedTripAsync(tripId);
```

---

## SettingsService

**Source**: `src/WayfarerMobile/Services/SettingsService.cs`

Manages application settings using MAUI Preferences and SecureStorage.

### Secure Settings (SecureStorage)

| Setting | Type | Description |
|---------|------|-------------|
| `ServerUrl` | string | Backend API URL |
| `ApiToken` | string | Authentication token |

### Regular Settings (Preferences)

#### Core Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `IsFirstRun` | bool | true | First launch flag |
| `TimelineTrackingEnabled` | bool | false | Enable background location logging (privacy-first) |
| `BackgroundTrackingEnabled` | bool | false | Background GPS acquisition (set during onboarding) |
| `KeepScreenOn` | bool | false | Prevent screen dimming |

#### Location Thresholds

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `LocationTimeThresholdMinutes` | int | 5 | Min time between logs (server-configurable) |
| `LocationDistanceThresholdMeters` | int | 15 | Min distance between logs (server-configurable) |
| `LocationAccuracyThresholdMeters` | int | 50 | Maximum GPS accuracy to accept |

**Note**: Location thresholds are server-configurable and use AND logic - both the time AND distance thresholds must be exceeded for a location to be logged.

#### Map Cache Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `MapOfflineCacheEnabled` | bool | true | Enable tile caching |
| `MaxLiveCacheSizeMB` | int | 500 | Live tile cache limit |
| `MaxTripCacheSizeMB` | int | 2000 | Trip tile cache limit |
| `TileServerUrl` | string | OSM tiles | User-configurable tile server URL |
| `MaxConcurrentTileDownloads` | int | 2 | Parallel download limit (1-4) |
| `MinTileRequestDelayMs` | int | 100 | Rate limiting delay (50-5000) |
| `LiveCachePrefetchRadius` | int | 5 | Prefetch radius (1-10 tiles from center) |
| `PrefetchDistanceThresholdMeters` | int | 500 | Min distance before prefetching tiles |

#### Navigation Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `NavigationAudioEnabled` | bool | true | TTS announcements |
| `NavigationVolume` | float | 0.7 | Voice announcement volume (0.0-1.0) |
| `NavigationLanguage` | string | "" | TTS language code (empty = device default) |
| `NavigationVibrationEnabled` | bool | true | Haptic feedback |
| `AutoRerouteEnabled` | bool | true | Automatic rerouting |
| `DistanceUnits` | string | "kilometers" | km or miles |

#### Visit Notification Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `VisitNotificationsEnabled` | bool | false | Notify on place visits |
| `VisitNotificationStyle` | string | "notification" | Notification type (notification/voice/both) |
| `VisitVoiceAnnouncementEnabled` | bool | false | Voice announcements for visits |

#### Battery Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `AutoPauseTrackingOnCriticalBattery` | bool | false | Auto-pause at critical battery (<10%) |
| `ShowBatteryWarnings` | bool | true | Show battery warnings |

#### Appearance

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ThemePreference` | string | "System" | Theme (System/Light/Dark) |

### Usage

```csharp
public class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private bool _trackingEnabled;

    partial void OnTrackingEnabledChanged(bool value)
    {
        _settings.TimelineTrackingEnabled = value;
    }

    private void LoadSettings()
    {
        TrackingEnabled = _settings.TimelineTrackingEnabled;
        ServerUrl = _settings.ServerUrl ?? "";
    }
}
```

---

## ApiClient

**Source**: `src/WayfarerMobile/Services/ApiClient.cs`

HTTP client for backend API communication with Polly retry support.

### Configuration

```csharp
// In MauiProgram.cs
services.AddHttpClient("WayfarerApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});
```

### Retry Policy

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
            .HandleResult(r => TransientStatusCodes.Contains(r.StatusCode))
    })
    .Build();
```

### Transient Status Codes

- 408 Request Timeout
- 429 Too Many Requests
- 500 Internal Server Error
- 502 Bad Gateway
- 503 Service Unavailable
- 504 Gateway Timeout

### API Methods

See [API Integration](13-API.md) for complete endpoint documentation.

---

## TimelineSyncService

**Source**: `src/WayfarerMobile/Services/TimelineSyncService.cs`

Manages optimistic UI updates for timeline mutations with offline queue, rollback support, and background processing.

### Background Processing

Timeline mutations now sync automatically without requiring the Timeline page to be open:

- **Timer-based processing**: 60-second interval checks for pending mutations
- **App lifecycle integration**: Triggers sync on suspend/resume via AppLifecycleService
- **Background location piggyback**: Syncs during background location service wakeups
- **Self-contained connectivity**: Maintains own network subscription for online/offline awareness
- **Exception isolation**: Fully isolated to protect background location services from crashes

### Sync Strategy

1. Apply optimistic UI update immediately
2. Save to local database (both `PendingTimelineMutation` and `LocalTimelineEntry`)
3. Attempt server sync in background (immediately if online, or via timer)
4. On 4xx error: Server rejected → revert changes, notify caller
5. On 5xx/network error: Queue for retry (local keeps optimistic values)

### Rollback Data

Rollback data is persisted in `PendingTimelineMutation` to survive app restarts:

```csharp
public class PendingTimelineMutation
{
    public int LocationId { get; set; }
    public string OperationType { get; set; }  // "Update" or "Delete"

    // New values
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? LocalTimestamp { get; set; }
    public string? Notes { get; set; }

    // Original values for rollback
    public double? OriginalLatitude { get; set; }
    public double? OriginalLongitude { get; set; }
    public DateTime? OriginalTimestamp { get; set; }
    public string? OriginalNotes { get; set; }

    // For delete rollback
    public string? DeletedEntryJson { get; set; }
}
```

### Events

| Event | When Raised |
|-------|-------------|
| `SyncCompleted` | Server accepted the mutation |
| `SyncQueued` | Mutation queued for offline retry |
| `SyncRejected` | Server rejected (4xx) - UI should revert |

---

## TimelineExportService

**Source**: `src/WayfarerMobile/Services/TimelineExportService.cs`

Exports local timeline data to CSV and GeoJSON formats.

### Export Formats

**CSV**: Spreadsheet-compatible with all fields
```csv
id,server_id,timestamp,latitude,longitude,accuracy,altitude,speed,bearing,
provider,address,full_address,place,region,country,postcode,activity_type,timezone,notes
```

**GeoJSON**: FeatureCollection with Point geometries
```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": { "type": "Point", "coordinates": [lon, lat] },
      "properties": { "timestamp": "...", "accuracy": ..., ... }
    }
  ]
}
```

### Usage

```csharp
// Export to CSV
var csv = await _exportService.ExportToCsvAsync(fromDate, toDate);

// Export to GeoJSON
var geojson = await _exportService.ExportToGeoJsonAsync(fromDate, toDate);

// Export and share via system dialog
await _exportService.ShareExportAsync("csv", fromDate, toDate);
```

---

## TimelineImportService

**Source**: `src/WayfarerMobile/Services/TimelineImportService.cs`

Imports CSV and GeoJSON files into local timeline with duplicate detection.

### Duplicate Detection

Entries are considered duplicates if:
- Timestamp within **2 seconds** of an existing entry
- Location within **10 meters** of the existing entry

### Import Behavior

| Condition | Action |
|-----------|--------|
| New entry | Insert to local database |
| Duplicate with less data | Skip |
| Duplicate with more data | Update existing (merge enrichment) |
| Malformed row | Log error, continue |

### ImportResult

```csharp
public record ImportResult(
    int Imported,   // New entries added
    int Updated,    // Existing entries enriched
    int Skipped,    // Duplicates skipped
    List<string> Errors  // Parse errors
);
```

---

## ActivitySyncService

**Source**: `src/WayfarerMobile/Services/ActivitySyncService.cs`

Manages activity types with server sync and local caching.

### Default Activities

20 built-in activities with negative IDs (never conflict with server):

| ID | Name | Icon | ID | Name | Icon |
|----|------|------|----|------|------|
| -1 | Walking | walk | -11 | ATM | atm |
| -2 | Running | run | -12 | Fitness | fitness |
| -3 | Cycling | bike | -13 | Doctor | hospital |
| -4 | Travel | car | -14 | Hotel | hotel |
| -5 | Eating | eat | -15 | Airport | flight |
| -6 | Drinking | drink | -16 | Gas Station | gas |
| -7 | At Work | marker | -17 | Park | park |
| -8 | Meeting | flag | -18 | Museum | museum |
| -9 | Shopping | shopping | -19 | Photography | camera |
| -10 | Pharmacy | pharmacy | -20 | General | marker |

### Sync Behavior

- **Sync interval**: Every 6 hours
- **Priority**: Server activities (positive IDs) > default activities (negative IDs)
- **Icon mapping**: Automatically suggests icons based on activity name

---

## OsrmRoutingService

**Source**: `src/WayfarerMobile/Services/OsrmRoutingService.cs`

OSRM (Open Source Routing Machine) API client for route calculation.

### Configuration

| Setting | Value |
|---------|-------|
| Base URL | `https://router.project-osrm.org` |
| Rate limit | 1 request/second |
| Timeout | 10 seconds |
| Profiles | foot, car, bike |

### Rate Limiting

```csharp
private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1.1);

private static async Task EnforceRateLimitAsync()
{
    var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
    if (timeSinceLastRequest < MinRequestInterval)
    {
        await Task.Delay(MinRequestInterval - timeSinceLastRequest);
    }
    _lastRequestTime = DateTime.UtcNow;
}
```

### Response

```csharp
public class OsrmRouteResult
{
    public string Geometry { get; set; }        // Encoded polyline
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public List<OsrmStepResult> Steps { get; set; }  // Turn instructions
}
```

---

## RouteCacheService

**Source**: `src/WayfarerMobile/Services/RouteCacheService.cs`

Single-route session cache stored in Preferences. Survives app restart.

### Cache Validity

A cached route is valid if:
- Same destination place ID
- Origin within **50 meters** of cached origin
- Less than **5 minutes** old

### Storage

```csharp
public class CachedRoute
{
    public string DestinationPlaceId { get; set; }
    public string DestinationName { get; set; }
    public double OriginLatitude { get; set; }
    public double OriginLongitude { get; set; }
    public string Geometry { get; set; }  // Encoded polyline
    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }
    public DateTime FetchedAtUtc { get; set; }
}
```

---

## TileDownloadOrchestrator

**Source**: `src/WayfarerMobile/Services/TileDownloadOrchestrator.cs`

Orchestrates batch tile downloads with parallel execution, pause/resume support, and cache limit enforcement.

### Interface

```csharp
public interface ITileDownloadOrchestrator
{
    Task<TileDownloadResult> DownloadTilesAsync(
        BoundingBox bbox, int tripId, IProgress<TileProgress>? progress, CancellationToken ct);
    IEnumerable<TileIndex> CalculateTilesForBoundingBox(BoundingBox bbox, int maxZoom);
    int GetRecommendedMaxZoom(BoundingBox bbox);
    string? GetCachedTilePath(int x, int y, int z, int tripId);
}
```

### Features

- **Parallel downloads**: Configurable concurrent tile fetching
- **Pause/Resume**: Downloads can be paused and resumed via stop requests
- **Cache limits**: Enforces 80%/90%/100% thresholds with warnings
- **Rate limiting**: Handles 429 responses with retry-after header
- **Atomic writes**: Uses temp file pattern for integrity
- **PNG validation**: Validates downloaded tile signatures

### Tile Download Configuration

| Setting | Value |
|---------|-------|
| Zoom levels | 8-17 |
| Concurrent downloads | Configurable (default 4) |
| Checkpoint interval | Every 50 tiles |
| Estimated tile size | 40 KB (urban areas) |

---

## DownloadStateService

**Source**: `src/WayfarerMobile/Services/DownloadStateService.cs`

Manages download state transitions with validation and events.

### Download States

| Status | Description |
|--------|-------------|
| `ServerOnly` | Trip exists on server, not downloaded |
| `DownloadingMetadata` | Fetching trip metadata |
| `DownloadingTiles` | Downloading map tiles |
| `Complete` | All tiles downloaded |
| `MetadataOnly` | Trip data without tiles |
| `PausedUser` | User paused download |
| `PausedNetwork` | Network lost |
| `PausedStorage` | Device storage low |
| `PausedCacheLimit` | Trip cache full |
| `Failed` | Download failed |
| `Cancelled` | User cancelled |

### Key Methods

```csharp
Task<bool> TransitionAsync(int tripId, DownloadStatus newStatus);
bool IsValidTransition(DownloadStatus from, DownloadStatus to);
Task<List<DownloadedTripEntity>> GetResumableTripsAsync();
Task RecoverStuckDownloadsAsync();
```

---

## DownloadStateManager

**Source**: `src/WayfarerMobile/Services/DownloadStateManager.cs`

Manages download pause/resume state, progress tracking, and stop requests.

### Features

- **Thread-safe stop management**: Uses ConcurrentDictionary for stop requests
- **State persistence**: Saves/restores download checkpoints for resumability
- **Progress tracking**: Tracks tile completion per trip

### Key Methods

```csharp
void RequestStop(int tripId, StopReason reason);
bool TryGetStopReason(int tripId, out StopReason reason);
void ClearStopRequest(int tripId);
Task SaveStateAsync(int tripId, DownloadCheckpoint checkpoint);
Task<DownloadCheckpoint?> GetStateAsync(int tripId);
```

---

## DownloadProgressAggregator

**Source**: `src/WayfarerMobile/Services/DownloadProgressAggregator.cs`

Centralizes download progress events from multiple services.

### Events

| Event | When Raised |
|-------|-------------|
| `ProgressChanged` | Tile download progress update |
| `DownloadCompleted` | Download finished successfully |
| `DownloadFailed` | Download failed with error |
| `DownloadPaused` | Download paused (user or limit) |

---

## CacheLimitEnforcer

**Source**: `src/WayfarerMobile/Services/CacheLimitEnforcer.cs`

Enforces cache size limits for trip tile downloads.

### Thresholds

| Level | Threshold | Action |
|-------|-----------|--------|
| Warning | 80% | Notify user |
| Critical | 90% | Urgent warning |
| Limit | 100% | Pause download |

### Key Methods

```csharp
Task<CacheLimitResult> CheckLimitAsync();
Task<CacheLimitResult> GetCachedLimitCheckAsync(); // 2-second cache
```

---

## TripStateManager

**Source**: `src/WayfarerMobile/Services/TripStateManager.cs`

Thread-safe management of currently loaded trip state across the application.

### Events

| Event | When Raised |
|-------|-------------|
| `CurrentTripChanged` | Active trip changed |
| `LoadedTripChanged` | Trip data loaded/unloaded |

### Features

- Singleton registration
- Lock-based thread safety for concurrent access
- Central source of truth for trip state

---

## TripSyncCoordinator

**Source**: `src/WayfarerMobile/Services/TripSyncCoordinator.cs`

Coordinates synchronization of downloaded trips with the server.

### Key Methods

```csharp
Task<bool> CheckTripUpdateNeededAsync(Guid serverId);
Task<SyncResult> SyncTripAsync(Guid serverId, CancellationToken ct);
```

### Features

- Prevents concurrent syncs of same trip
- Compares server vs local versions
- Re-downloads tiles if bounding box changed

---

## TripContentService

**Source**: `src/WayfarerMobile/Services/TripContentService.cs`

Service for trip content operations: metadata fetch, sync, and offline retrieval.

### Key Methods

```csharp
Task<bool> CheckTripUpdateNeededAsync(Guid serverId);
Task<List<Guid>> GetTripsNeedingUpdateAsync();
Task<bool> SyncTripMetadataAsync(Guid serverId, CancellationToken ct);
```

### Features

- Handles Places, Segments, Areas, and Polygons
- Offline-first approach with online enrichment

---

## TripMetadataBuilder

**Source**: `src/WayfarerMobile/Services/TripMetadataBuilder.cs`

Builds offline entity collections from trip details.

```csharp
public interface ITripMetadataBuilder
{
    List<OfflineAreaEntity> BuildAreas(TripDetails trip, int tripId);
    List<OfflinePlaceEntity> BuildPlaces(TripDetails trip, int tripId);
    List<OfflineSegmentEntity> BuildSegments(TripDetails trip, int tripId);
    List<OfflinePolygonEntity> BuildPolygons(TripDetails trip, int tripId);
}
```

Pure transformation service with no side effects.

---

## Operations Handlers

### PlaceOperationsHandler

**Source**: `src/WayfarerMobile/Services/PlaceOperationsHandler.cs`

Handles place CRUD operations with optimistic UI pattern.

### RegionOperationsHandler

**Source**: `src/WayfarerMobile/Services/RegionOperationsHandler.cs`

Handles region CRUD operations with optimistic UI pattern.

### TripEntityOperationsHandler

**Source**: `src/WayfarerMobile/Services/TripEntityOperationsHandler.cs`

Handles trip entity updates (Trip, Segment, Area).

### Common Features

- Optimistic UI updates (apply locally, sync to server)
- Returns operation results instead of raising events
- Enforces dependency ordering (e.g., Region must exist before Place)

---

## TimelineDataService

**Source**: `src/WayfarerMobile/Services/TimelineDataService.cs`

Abstraction layer for timeline data access with offline fallback.

### Key Methods

```csharp
Task<List<LocalTimelineEntry>> GetEntriesForDateAsync(DateOnly date);
Task<List<LocalTimelineEntry>> GetEntriesInRangeAsync(DateOnly from, DateOnly to);
Task EnrichFromServerAsync(DateOnly date, CancellationToken ct);
```

### Features

- Per-date locks for enrichment concurrency control
- Offline-first with server enrichment
- Merges server data into local entries

---

## LocalTimelineStorageService

**Source**: `src/WayfarerMobile/Services/LocalTimelineStorageService.cs`

Manages local timeline storage by filtering and persisting location data.

### Features

- Subscribes to location events and sync callbacks
- Applies AND filter logic (matching server behavior)
- Both time AND distance thresholds must be exceeded

---

## MutationQueueService

**Source**: `src/WayfarerMobile/Services/MutationQueueService.cs`

Manages the offline mutation queue for pending sync operations.

### Features

- Queues Place, Region, and entity mutations
- Lifecycle tracking: pending → syncing → synced
- Shares database connection for transactional consistency

---

## SyncEventBus

**Source**: `src/WayfarerMobile/Services/SyncEventBus.cs`

Centralizes sync-related events from multiple services.

### Events

| Event | When Raised |
|-------|-------------|
| `SyncSucceeded` | Server accepted sync |
| `SyncFailed` | Sync failed |
| `SyncQueued` | Queued for offline retry |
| `EntityCreated` | New entity created |
| `TripsUpdated` | Trips list changed |
| `TripDataChanged` | Trip content changed |
| `ConnectivityChanged` | Network status changed |

---

## LocationSyncEventBridge

**Source**: `src/WayfarerMobile/Services/LocationSyncEventBridge.cs`

Bridges static LocationSyncCallbacks to ILocationSyncEventBridge interface.

Allows Core services to observe sync events without platform-specific coupling.

---

## SseClient

**Source**: `src/WayfarerMobile/Services/SseClient.cs`

Client for subscribing to Server-Sent Events (SSE) for real-time location updates.

### Events

| Event | Description |
|-------|-------------|
| `LocationReceived` | Member location update |
| `LocationDeleted` | Location removed |
| `MembershipReceived` | Membership change |
| `InviteCreated` | New group invite |
| `VisitStarted` | Place visit detected |
| `HeartbeatReceived` | Connection keepalive |
| `Connected` | SSE connection established |
| `Reconnecting` | Attempting reconnection |
| `PermanentError` | Non-recoverable error |

### Reconnection

- Automatic reconnection with exponential backoff (1s, 2s, 5s)
- Non-retryable status codes: 401, 403, 404

---

## GroupMemberManager

**Source**: `src/WayfarerMobile/Services/GroupMemberManager.cs`

Manages group member operations (load, refresh, visibility, utilities).

### Key Methods

```csharp
Task<List<GroupMemberWithLocation>> LoadMembersWithLocationsAsync(Guid groupId);
Task RefreshMemberLocationsAsync(Guid groupId);
Task UpdatePeerVisibilityAsync(Guid groupId, bool visible);
```

---

## AppLockService

**Source**: `src/WayfarerMobile/Services/Security/AppLockService.cs`

App lock/PIN security service.

### Security States

| State | Description |
|-------|-------------|
| S0 | No PIN configured |
| S1 | PIN configured but disabled |
| S2 | Locked (requires PIN) |
| S3 | Unlocked |

### Features

- Secure PIN hashing with PBKDF2
- Salt-based storage
- Legacy preference migration

---

## VisitNotificationService

**Source**: `src/WayfarerMobile.Core/Services/VisitNotificationService.cs`

Handles visit notifications via SSE and background polling.

### Features

- SSE subscription when in foreground
- Background polling after location sync
- Deduplication with circular buffer (max 10 recent visits)
- Per-place daily notification limit
- Navigation conflict handling (10-second cooldown)

---

## Next Steps

- [API Integration](13-API.md) - Backend endpoints and authentication
- [Testing](14-Testing.md) - Unit test structure and strategies
- [Security](15-Security.md) - Security implementation details
