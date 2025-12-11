# Services

This document provides detailed documentation for the key services in WayfarerMobile.

## Service Overview

| Service | Purpose | Lifetime |
|---------|---------|----------|
| `LocationTrackingService` | GPS acquisition, foreground notification | Platform singleton |
| `LocationBridge` | Platform-to-MAUI location bridge | Singleton |
| `LocationSyncService` | Server synchronization with retry | Singleton |
| `MapService` | Mapsui integration, layers, markers | Singleton |
| `TripNavigationService` | Route calculation, OSRM integration | Singleton |
| `GroupsService` | SSE live location sharing | Singleton |
| `DatabaseService` | SQLite operations | Singleton |
| `SettingsService` | App configuration | Singleton |
| `ApiClient` | Backend HTTP communication | Singleton |

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
- **Quality Filtering**: Rejects locations with accuracy > 100m
- **Threshold Filtering**: Applies server-configured time/distance thresholds
- **Queue Writing**: Writes filtered locations to SQLite queue
- **Notification Management**: Shows foreground notification with pause/stop actions
- **Performance Modes**: Supports High (1s), Normal (60s), PowerSaver (300s) intervals

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
| Quality Filter    |  Reject if accuracy > 100m
+-------------------+
        |
        v
+-------------------+
| Threshold Filter  |  Check time/distance from last
+-------------------+
        |
        v (passes filter)
+-------------------+
| Queue to SQLite   |  Write to QueuedLocations
+-------------------+
        |
        v
+-------------------+
| Broadcast to UI   |  Via LocationServiceCallbacks
+-------------------+
        |
        v
+-------------------+
| Update Notification|  Show "Last: HH:mm:ss (N pts)"
+-------------------+
```

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

## LocationSyncService

**Source**: `src/WayfarerMobile/Services/LocationSyncService.cs`

Manages synchronization of queued locations to the server.

### Responsibilities

- Monitors the location queue in SQLite
- Batches locations for efficient sync
- Handles transient failures with retry
- Marks locations as synced or server-rejected
- Triggers automatic sync when queue exceeds threshold

### Sync Algorithm

```
Queue Check Timer (every 30s)
        |
        v
+-------------------+
| Get Pending       |  SELECT WHERE SyncStatus = Pending
| Locations         |  ORDER BY Timestamp LIMIT 100
+-------------------+
        |
        v
+-------------------+
| Send Batch        |  POST /api/location/log-location
+-------------------+
        |
   +----+----+
   |         |
Success    Failure
   |         |
   v         v
Mark      Retry or
Synced    Mark Failed
```

### Sync Status

| Status | Description |
|--------|-------------|
| `Pending` | Awaiting sync |
| `Synced` | Successfully sent to server |
| `Failed` | Max retries exceeded |

### Server Rejection Handling

Server may reject locations due to threshold validation. These are marked with `IsServerRejected = true` and not retried:

```csharp
await _databaseService.MarkLocationServerRejectedAsync(id, "threshold skipped");
```

---

## MapService

**Source**: `src/WayfarerMobile/Services/MapService.cs`

Manages the Mapsui map display including layers, markers, and navigation routes.

### Map Layers

| Layer | Purpose |
|-------|---------|
| `OpenStreetMap` | Base tile layer |
| `Track` | User movement track line |
| `CurrentLocation` | Location indicator with accuracy circle |
| `GroupMembers` | Group member location markers |
| `TripPlaces` | Trip place markers with icons |
| `TripSegments` | Trip segment polylines |
| `NavigationRoute` | Active navigation route (blue) |
| `NavigationRouteCompleted` | Completed route portion (gray) |

### Location Indicator

The location indicator includes:
- **Accuracy Circle**: Pulsing semi-transparent circle
- **Heading Cone**: Direction indicator (30-90 degrees based on calibration)
- **Center Marker**: Blue dot with white border

```csharp
public void UpdateLocation(LocationData location, bool centerMap = false)
{
    var (x, y) = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
    var point = new MPoint(x, y);

    // Update accuracy circle
    if (accuracy > 0)
    {
        _accuracyFeature.Geometry = CreateAccuracyCircle(point, accuracy * pulseScale);
    }

    // Update heading cone
    if (heading >= 0 && heading < 360)
    {
        _headingFeature.Geometry = CreateHeadingCone(point, heading, coneAngle);
    }

    // Update center marker
    _markerFeature.Geometry = new Point(point.X, point.Y);
}
```

### Navigation Route Display

```csharp
public void ShowNavigationRoute(NavigationRoute route)
{
    var coordinates = route.Waypoints
        .Select(w => {
            var (x, y) = SphericalMercator.FromLonLat(w.Longitude, w.Latitude);
            return new Coordinate(x, y);
        })
        .ToArray();

    var lineString = new LineString(coordinates);
    _navigationRouteLayer.Add(new GeometryFeature(lineString));
}

public void UpdateNavigationRouteProgress(double currentLat, double currentLon)
{
    // Split route into completed (gray) and remaining (blue) portions
}
```

### Trip Segment Styling

Segments are styled based on transport mode:

| Mode | Color | Style |
|------|-------|-------|
| Driving | Blue | Solid |
| Walking | Green | Dashed |
| Cycling | Orange | Solid |
| Transit | Purple | Solid |
| Ferry | Teal | Dashed |
| Flight | Light Blue | Dotted |

---

## TripNavigationService

**Source**: `src/WayfarerMobile/Services/TripNavigationService.cs`

Provides trip-based navigation with route calculation and progress tracking.

### Route Priority

1. **User Segments**: Trip-defined routes (always preferred)
2. **Cached OSRM**: Valid cached route (same destination, <5 min old, within 50m)
3. **OSRM Fetch**: Online route fetch
4. **Direct Route**: Straight line fallback

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

// Get pending locations for sync
var pending = await _databaseService.GetPendingLocationsAsync(limit: 100);

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

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `IsFirstRun` | bool | true | First launch flag |
| `TimelineTrackingEnabled` | bool | true | Server logging enabled |
| `LocationTimeThresholdMinutes` | int | 5 | Min time between logs (server-configurable) |
| `LocationDistanceThresholdMeters` | int | 15 | Min distance between logs (server-configurable) |
| `NavigationAudioEnabled` | bool | true | TTS announcements |
| `NavigationVibrationEnabled` | bool | true | Haptic feedback |
| `AutoRerouteEnabled` | bool | true | Automatic rerouting |
| `DistanceUnits` | string | "kilometers" | km or miles |
| `DarkModeEnabled` | bool | false | Dark theme |
| `MaxLiveCacheSizeMB` | int | 500 | Live tile cache limit |
| `MaxTripCacheSizeMB` | int | 2000 | Trip tile cache limit |
| `TileServerUrl` | string | OSM tiles | User-configurable tile server URL |
| `LiveCachePrefetchRadius` | int | 5 | Prefetch radius (1-9 tiles from center) |
| `PrefetchDistanceThresholdMeters` | int | 500 | Min distance before prefetching tiles |

**Note**: Location thresholds are server-configurable and use AND logic - both the time AND distance thresholds must be exceeded for a location to be logged.

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

## Next Steps

- [API Integration](13-API.md) - Backend endpoints and authentication
- [Testing](14-Testing.md) - Unit test structure and strategies
- [Security](15-Security.md) - Security implementation details
