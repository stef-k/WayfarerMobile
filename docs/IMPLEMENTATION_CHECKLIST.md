# WayfarerMobile - Implementation Checklist

Step-by-step guide for implementing WayfarerMobile from scratch.

---

## ⚠️ MANDATORY PROCESS

**Before implementing ANY feature:**
1. Read the relevant section in `DESIGN_SPEC.md`
2. Read any referenced docs in `docs/reference/`
3. Follow the checklist items IN ORDER
4. Mark items complete ONLY when fully done (not stubbed)
5. Verify against the spec after completion

**DO NOT:**
- Skip phases or items
- Create stub implementations
- Deviate from the design without documenting why
- Create new planning documents (use this checklist)

---

## Current Status Summary

| Phase | Status | Completion |
|-------|--------|------------|
| 1. Foundation | Complete | 100% |
| 2. Location Service (Android) | Complete | 100% |
| 3. Onboarding Flow | Complete | 100% |
| 4. Settings | Partial | ~90% (lock screen UI pending) |
| 5. Main Map | Partial | ~80% |
| 6. Database & API | Partial | ~90% |
| 7. Timeline | Complete | 100% |
| 8. Trips | Complete | 100% |
| 9. Navigation | Partial | ~80% |
| 10. Groups | Complete | 100% |
| 11. Check-In | Complete | 100% |
| 12. Polish | Partial | ~50% (logging, error handling, lifecycle done) |

**Overall: ~94% Complete (Backend/MVVM)**

**Remaining UI Work:**
- Lock screen overlay (PIN entry)
- Offline banner
- Loading states (`SfBusyIndicator`)
- Trip sidebar sliding animation
- Segment visualization on map

---

## Phase 1: Foundation

### 1.1 Create Solution

- [x] Create new .NET 10 MAUI solution in `src/WayfarerMobile`
- [x] Configure for Android and iOS only (remove Windows/Mac targets)
- [x] Set up project structure (Core, Infrastructure, Features, Shared, Platforms)
- [x] Add NuGet packages:
  - [x] `Syncfusion.Maui.Toolkit`
  - [x] `CommunityToolkit.Mvvm`
  - [x] `Mapsui.Maui` (5.0+)
  - [x] `sqlite-net-pcl`
  - [x] `ZXing.Net.MAUI`
  - [x] `Xamarin.GooglePlayServices.Location` (Android only)

### 1.2 Configure App

- [x] Set up `MauiProgram.cs` with DI container
- [x] Configure Syncfusion: `builder.ConfigureSyncfusionToolkit()`
- [x] Create base styles in `Resources/Styles/`:
  - [x] `Colors.xaml` - App color palette (ported from old app)
  - [x] `Styles.xaml` - Typography + Control styles (Headline, SubHeadline, DarkButton, DangerButton, Frame, ListView, etc.)

**UI Components to use (from DESIGN_SPEC.md):**
- `SfSwitch` for toggles
- `SfExpander` for collapsible sections
- `SfListView` for lists with grouping
- `SfBusyIndicator` for loading states
- `SfCircularProgressBar` for downloads

### 1.3 Set Up Core Layer

- [x] Create `Core/Enums/TrackingState.cs`
- [x] Create `Core/Enums/PerformanceMode.cs`
- [x] Create `Core/Enums/SyncStatus.cs`
- [x] Create `Core/Enums/TransportationMode.cs`
- [x] Create `Core/Interfaces/ILocationBridge.cs`
- [x] Create `Core/Interfaces/ISettingsService.cs`
- [x] Create `Core/Interfaces/IApiClient.cs`
- [x] Create `Core/Interfaces/IGroupsService.cs`
- [x] Create `Core/Interfaces/IPermissionsService.cs`

### 1.4 Port Algorithms

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Geo\`

- [x] Port `GeoMath.cs` to `Core/Algorithms/`
- [x] Port `ThresholdFilter.cs` to `Core/Algorithms/`
- [ ] Port `BearingStabilityTracker.cs` (if needed for navigation)

---

## Phase 2: Location Service (Android)

### 2.1 Create LocationTrackingService

File: `Platforms/Android/Services/LocationTrackingService.cs`

- [x] Create Android foreground service with `ForegroundServiceType.Location`
- [x] Implement `OnStartCommand` with actions:
  - [x] `ACTION_START` - Start tracking
  - [x] `ACTION_PAUSE` - Pause GPS, keep service alive
  - [x] `ACTION_RESUME` - Resume GPS
  - [x] `ACTION_STOP` - Stop service
  - [x] `ACTION_SET_HIGH_PERFORMANCE` - 1s updates
  - [x] `ACTION_SET_NORMAL` - Server-configured interval
- [x] Implement `StartForeground()` with notification (within 5 seconds!)
- [x] Use Google Play Services `FusedLocationProviderClient` when available
- [x] Fallback to `LocationManager` for devices without Play Services
- [x] Add quality filtering (accuracy threshold)
- [x] Add threshold filtering (time/distance from server settings)
- [x] Implement SQLite writes (queue for server sync)
- [x] Use `LocationServiceCallbacks` for UI updates (replaced LocalBroadcastManager)
- [x] Handle `TimelineTrackingEnabled` setting (skip logging when OFF)

### 2.2 Create LocationBridge

File: `Platforms/Android/Services/LocationBridge.cs`

- [x] Register for `LocationServiceCallbacks` events
- [x] Expose `CurrentState` property
- [x] Expose `CurrentMode` property
- [x] Expose `LastLocation` property
- [x] Implement `StartAsync()` - sends ACTION_START intent
- [x] Implement `StopAsync()` - sends ACTION_STOP intent
- [x] Implement `PauseAsync()` - sends ACTION_PAUSE intent
- [x] Implement `ResumeAsync()` - sends ACTION_RESUME intent
- [x] Implement `SetPerformanceModeAsync(mode)`

### 2.3 Create Notification

- [x] Create notification channel for tracking
- [x] Create notification with:
  - [x] Title: "WayfarerMobile"
  - [x] Text: "Tracking active" / "Paused" / location count
  - [x] Actions: [Pause/Resume] [Stop]
- [x] Update notification on location updates

### 2.4 Android Manifest

- [x] Add permissions (in AndroidManifest.xml)
- [x] Declare service with `foregroundServiceType="location"`

### 2.5 iOS Location Service

- [x] Create `Platforms/iOS/Services/LocationTrackingService.cs`
  - [x] Use `CLLocationManager` with `ICLLocationManagerDelegate`
  - [x] Enable background updates
  - [x] Request "always" authorization
- [x] Create `Platforms/iOS/Services/LocationBridge.cs`
  - [x] Implement `ILocationBridge` interface
  - [x] Connect to `LocationServiceCallbacks`

---

## Phase 3: Onboarding Flow

**Status: COMPLETE**

### 3.1 Create Permission Service

File: `Services/PermissionsService.cs` (already exists, enhanced)

- [x] Check all required permissions
- [x] Request permissions in correct order:
  1. ACCESS_FINE_LOCATION (foreground)
  2. ACCESS_BACKGROUND_LOCATION
  3. POST_NOTIFICATIONS (Android 13+)
  4. Battery optimization exemption
- [x] Track permission state
- [x] Handle denial gracefully

### 3.2 Create Onboarding Pages

**Uses single OnboardingPage with step-based navigation (modern UX pattern)**

- [x] `OnboardingPage.xaml` - Step-based onboarding with:
  - [x] Welcome step - App introduction
  - [x] Location permission step - Explain why, request foreground
  - [x] Background location step - Explain 24/7, request background
  - [x] Notification permission step - Explain status, request (Android 13+)
  - [x] Battery optimization step - Explain reliability, request exemption
  - [x] Server setup step - QR scan or manual entry
- [x] Progress indicator with `SfLinearProgressBar`
- [x] Loading overlay with `SfBusyIndicator`

### 3.3 Create OnboardingViewModel

- [x] Track current step
- [x] Commands for next/skip/request
- [x] Handle completion (set IsFirstRun = false, navigate to main)

---

## Phase 4: Settings

### 4.1 Create Settings Service

File: `Services/SettingsService.cs`

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Storage\SettingsStore.cs`

- [x] `IsFirstRun` - First launch detection
- [x] `TimelineTrackingEnabled` - Enable/disable server logging
- [x] `ServerUrl` - Backend URL
- [x] `ApiToken` - Authentication token
- [x] `LocationTimeThresholdMinutes` - From server
- [x] `LocationDistanceThresholdMeters` - From server
- [x] `UserId` - Current user ID
- [x] `UserEmail` - Current user email
- [x] `DarkModeEnabled` - Theme preference (with immediate theme apply)
- [x] `MapOfflineCacheEnabled` - Offline tiles
- [x] `MaxLiveCacheSizeMB` - Live tile cache limit (100-2000, default 500)
- [x] `MaxTripCacheSizeMB` - Trip tile cache limit (500-5000, default 2000)
- [x] `MaxConcurrentTileDownloads` - Concurrent tile downloads (1-4, default 2)
- [x] `MinTileRequestDelayMs` - Delay between requests (50-5000, default 100)

### 4.2 Create Settings Page

**Use Syncfusion `SfExpander` for collapsible sections (DESIGN_SPEC.md section 7.2)**

- [x] Basic settings page exists
- [x] Use `SfExpander` for collapsible sections:
  - [x] Account (login status, QR scan)
  - [x] Timeline Tracking (enable/disable toggle with `SfSwitch`)
  - [x] Map (offline cache toggle with `SfSwitch`)
  - [x] Appearance (dark mode toggle with `SfSwitch`)
  - [x] Data (clear all data with DangerButton style)
  - [x] About (version, about button)

### 4.3 App Lock / PIN Security

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Security\AppLockService.cs`

- [x] Create `Services/Security/AppLockService.cs`
  - [x] State machine (S0: No PIN, S1: Has PIN Disabled, S2: Enabled Locked, S3: Enabled Unlocked)
  - [x] SHA256 hashed PIN storage in SecureStorage
  - [x] Session-based unlock (auto-lock on app background)
  - [x] Lock on resume from background
- [x] Create `ViewModels/PinSecurityViewModel.cs`
  - [x] PIN entry/setup/change commands
  - [x] Enable/disable PIN lock commands
  - [x] Navigation guards for protected areas
- [x] Create PIN entry UI in Settings
  - [x] PIN setup flow (create + confirm)
  - [x] PIN change flow (verify old + new + confirm)
  - [x] Enable/disable toggle with PIN verification
- [ ] Create lock screen overlay
  - [ ] PIN entry on app resume
  - [ ] Wrong PIN attempts handling

---

## Phase 5: Main Map

**Status: COMPLETE**

### 5.1 Create Map Page

File: `MainPage.xaml`

- [x] Integrate Mapsui `MapControl`
- [x] Add location indicator layer (basic)
- [x] Add accuracy circle around location
- [x] Add heading indicator (arrow/cone)
- [x] Add floating action buttons (Syncfusion circular buttons):
  - [x] Center on user (basic button)
  - [x] Trip sidebar toggle
  - [x] Check-in button

### 5.2 Create MainViewModel

- [x] Inject `ILocationBridge`
- [x] `CurrentLocation` property bound to bridge
- [x] `CenterOnLocationCommand`
- [x] Subscribe to location updates
- [x] Set HIGH_PERFORMANCE mode when visible
- [x] Unsubscribe and set NORMAL mode in `OnDisappearing`

### 5.3 Location Indicator

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Location\LocationIndicatorService.cs`

- [x] Create dedicated `LocationIndicatorService` (`Services/LocationIndicatorService.cs`)
  - [x] Bearing smoothing with circular averaging (fixes 0°/360° wrap)
  - [x] Jitter filtering (15° minimum change threshold)
  - [x] Bearing persistence (20 second timeout)
  - [x] Accuracy-weighted sample history
  - [x] Navigation state tracking (IsNavigating, IsOnRoute)
  - [x] Pulse animation support
  - [x] **Gray dot fallback** - Shows gray marker when GPS unavailable/stale (30s timeout)
  - [x] **Dynamic cone width** - Cone angle varies with compass calibration quality (30°-90°)
- [x] Create location dot with accuracy circle
- [x] Add heading indicator (arrow/cone)
- [x] Smooth animation for position updates
  - [x] Feature reuse pattern (no rebuild per update)
  - [x] 60 FPS pulsing animation timer
  - [x] Navigation state colors (blue/gray/orange)

---

## Phase 6: Database & API

### 6.1 Create Database

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Database\`

- [x] Create `QueuedLocation` table for pending sync
- [x] Create `AppSetting` table for cached settings
- [x] Create `DownloadedTrip` table (`Data/Entities/DownloadedTrip.cs`)
- [x] Create `OfflinePlace` table (`Data/Entities/OfflinePlace.cs`)
- [x] Create `OfflineSegment` table (`Data/Entities/OfflineSegment.cs`)
- [x] Create `OfflineArea` table (`Data/Entities/OfflineArea.cs`)
- [x] Create `TripTile` table (`Data/Entities/TripTile.cs`)
- [x] Create `LiveTile` table (`Data/Entities/LiveTile.cs`)

### 6.2 Create API Client

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Api\WayfarerHttpService.cs`

- [x] Create base HTTP client with bearer auth
- [x] Implement endpoints:
  - [x] `POST /api/location/log-location` - Log location
  - [x] `POST /api/location/check-in` - Manual check-in
  - [x] `GET /api/settings` - Get server settings
  - [x] `GET /api/location/chronological` - Get timeline locations
  - [x] `GET /api/trips` - Get trips
  - [x] `GET /api/trips/{id}` - Get trip details
  - [x] `GET /api/trips/{id}/boundary` - Get trip boundary
  - [x] `GET /api/mobile/groups` - Get groups
  - [x] `GET /api/mobile/groups/{id}/members` - Get members
  - [x] `GET /api/mobile/groups/{id}/locations/latest` - Get member locations

### 6.3 Create Sync Service

- [x] Queue locations to SQLite when offline
- [x] Sync queue periodically (timer-based)
- [x] Batch sync (50 locations per request)
- [x] Respect server thresholds (time/distance) - ThresholdFilter loads from settings
- [x] Add rate limiting (65s min, 55/hour max)
- [x] Add exponential backoff retry (use Polly)
- [x] Distinguish server rejection vs technical failure

---

## Phase 7: Timeline

**Status: COMPLETE**

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Pages\TimelinePage.xaml`

### 7.1 Create Timeline Page

**Using MAUI CollectionView with grouping**

- [x] Use `CollectionView` with hour grouping
- [x] Pull-to-refresh with `RefreshView`
- [x] Location items with:
  - [x] Time
  - [x] Coordinates display
  - [x] Accuracy indicator (color-coded)
  - [x] Sync status icon
  - [x] Provider info
  - [x] Speed display (when available)

### 7.2 Create TimelineViewModel

- [x] Load locations from local database
- [x] Date filtering (previous/next day, today)
- [x] Group by hour
- [x] Empty state handling

---

## Phase 8: Trips

**Status: COMPLETE (100%)**

Reference: `docs/reference/TILE_CACHING.md`

### 8.1 Create Trip Manager

- [x] Trip list page (`TripsPage.xaml`, `TripsViewModel.cs`)
- [x] Trip models (`Core/Models/TripModels.cs`)
- [x] Trip API endpoints in `ApiClient.cs`
- [x] Download trip metadata + places (`TripDownloadService.cs`)
- [x] Show download progress with ProgressBar
- [x] Delete downloaded trip
- [x] Download tiles for offline map

### 8.2 Create Trip Sidebar

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Controls\TripSidebar\`

- [x] Trip details view with places list
- [x] Place markers on map via `MapService`
- [ ] Sliding panel animation
- [ ] Segment visualization
- [x] Navigate to place command (via NavigationService)

### 8.4 Custom Icon System

- [x] Icon assets copied (`Resources/Raw/wayfarer-map-icons/`) - 63 icons × 5 colors
- [x] `IconCatalog` helper class (`Helpers/IconCatalog.cs`)
  - [x] Icon name validation and coercion
  - [x] Color validation and coercion
  - [x] Resource path generation
- [x] `MapService` custom place markers
  - [x] Dedicated `_tripPlacesLayer` for trip places
  - [x] `UpdateTripPlacesAsync()` with custom icons
  - [x] Icon caching for performance
  - [x] Mapsui 5.0 `ImageStyle` with base64-content scheme
  - [x] Fallback to colored ellipse if icon fails

### 8.5 Offline Database

- [x] `DownloadedTripEntity` (`Data/Entities/DownloadedTrip.cs`)
- [x] `TripTileEntity` (`Data/Entities/TripTile.cs`)
- [x] `OfflinePlaceEntity` (`Data/Entities/OfflinePlace.cs`)
- [x] DatabaseService trip management methods
- [x] `TripDownloadService` for download orchestration

### 8.3 Create Tile Cache

Reference: `docs/reference/TILE_CACHING.md` (501 lines of detailed design)

- [x] Tile coordinate calculation from bounding box
- [x] Tile download with concurrent downloads (2 max, respects OSM usage policy)
- [x] Tile caching to filesystem
- [x] Database tracking of downloaded tiles (`TripTileEntity`)
- [x] Progress reporting during download
- [x] Atomic file writes with race condition fix (File.Move overwrite)
- [x] Intelligent zoom level selection based on area size
- [x] Rate limiting with configurable delay (`MinTileRequestDelayMs`, default 100ms)
- [x] Configurable concurrent downloads (`MaxConcurrentTileDownloads`, default 2, max 4)
- [x] Network connectivity monitoring (wait for restore during download)
- [x] Storage space check before download
- [x] `UnifiedTileCacheService` - Priority: Live → Trip → Download
- [x] `WayfarerTileSource` - Custom Mapsui tile source
- [x] `LiveTileCacheService` - Live tile caching with LRU eviction
- [x] Complete offline trip storage (places, segments, areas, regions)
- [x] `GetOfflineTripDetailsAsync` - Load complete trip for navigation
- [x] Database cascade delete for all trip entities

---

## Phase 9: Navigation

**Status: COMPLETE**

Reference: `docs/reference/NAVIGATION_SYSTEM.md` (1382 lines of detailed design)

### 9.1 Create Navigation Service

File: `Services/NavigationService.cs` (basic), `Services/TripNavigationService.cs` (trip-based)

- [x] Basic navigation to destination place
- [x] Distance calculation using GeoMath
- [x] Bearing calculation for direction
- [x] Navigation state with events
- [x] Trip navigation graph from Places and Segments (`Core/Navigation/TripNavigationGraph.cs`)
- [x] A* pathfinding for route calculation
- [x] Fallback connections when no segments exist
- [x] Off-route detection (100m threshold)
- [x] Rerouting capability
- [x] 50-meter rule for segment routing activation
- [x] Polyline decoding for detailed route geometry

### 9.2 Create Navigation Overlay

- [x] Distance remaining (`NavigationState.DistanceText`)
- [x] Time remaining (`NavigationState.TimeText`)
- [x] Cardinal direction (`NavigationState.DirectionText`)
- [x] Next waypoint instruction (`TripNavigationState.CurrentInstruction`)
- [x] Progress along route (`TripNavigationState.ProgressPercent`)
- [x] Navigation state integrated with TripsViewModel

### 9.3 Create Audio Service

Reference: `C:\Users\stef\source\repos\Wayfarer.Mobile\Services\Navigation\NavigationAudioService.cs`

- [x] Text-to-speech integration (MAUI Essentials)
- [x] Distance announcements at intervals
- [x] Arrival announcements
- [x] Turn announcements with segment transport mode support
  - [x] Announces waypoints within 100m (min 15s between announcements)
  - [x] Transport mode context (Walk to, Drive to, Take transit to, etc.)
  - [x] InstructionAnnounced event for UI integration

---

## Phase 10: Groups

**Status: COMPLETE**

### 10.1 Create Groups Page

Reference: `docs/reference/GROUPS_FEATURE.md`

- [x] Group list (basic)
- [x] Member locations on map with toggle view
- [x] Member markers with color coding
- [x] Member legend overlay on map

### 10.2 Auto-Refresh

- [x] Timer-based location refresh (30s interval)
- [x] Updates both list and map views

### 10.3 Create Legend

- [x] Member list with colors
- [x] Live status indicator

---

## Phase 11: Check-In

**Status: COMPLETE**

### 11.1 Create Check-In Page

- [x] Location preview (mini map)
- [x] Activity type picker (predefined list)
- [x] Notes field
- [x] Submit command with success/error feedback

### 11.2 Create CheckInViewModel

- [x] Get current location from location bridge
- [x] Activity type selection
- [x] Submit to server via API
- [x] Success/error handling with navigation

---

## Phase 12: Polish

**Status: PARTIAL (~50%)**

### 12.1 Error Handling

- [x] Global exception handler (`IExceptionHandlerService`, `ExceptionHandlerService`)
- [ ] User-friendly error messages (UI alerts)
- [x] Retry logic for network failures (Polly in `ApiClient`)

### 12.2 Offline Support

- [ ] Detect connectivity changes
- [ ] Show offline banner
- [x] Queue operations for sync (LocationSyncService queues to SQLite)

### 12.3 Logging

- [x] Add Serilog for file-based logging
- [x] Configure log rotation (7 days, 10MB per file)
- [x] Category-based filtering (SourceContext in template)

### 12.4 Platform Services

- [x] Wake lock for long operations (Android + iOS `IWakeLockService`)
- [ ] Battery optimization detection
- [x] App lifecycle handling (`IAppLifecycleService`)

### 12.5 Testing

- [ ] Unit tests for algorithms
- [ ] Unit tests for ViewModels
- [ ] Integration tests for services

### 12.6 Final Polish

- [ ] Loading states (SfBusyIndicator)
- [ ] Animations/transitions
- [ ] App icon and splash screen
- [ ] Performance optimization

---

## Completion Criteria

- [ ] All features implemented and tested
- [ ] Works on Android (primary) and iOS
- [ ] Offline mode works correctly
- [ ] Background tracking survives app kill
- [ ] Battery usage is reasonable
- [ ] UI uses Syncfusion components as designed
- [ ] All features match or exceed old app functionality

---

## Verification Checklist

After completing each phase, verify:

1. [ ] All checklist items are marked complete (not stubbed)
2. [ ] Code compiles without errors
3. [ ] Feature works on device (not just emulator)
4. [ ] Feature matches DESIGN_SPEC.md requirements
5. [ ] UI uses correct Syncfusion components
6. [ ] Code is documented (XML comments)
