# WayfarerMobile - Development Instructions

## General Guidance

- Use the available agents when appropriate and provide value.

## Current Status

**Progress:** 100% Complete (All P0-P4 Features Done)

**Completed P3 Tasks (December 11, 2025):**

- ✅ P3: App-specific diagnostics (AppDiagnosticService with queue, cache, tracking diagnostics)
- ✅ P3: Cache debug overlay (CacheOverlayService - visual tile coverage on map)
- ✅ P3: DiagnosticsPage with expandable sections (Location Queue, Tile Cache, Tracking, Navigation)
- ✅ P3: Battery detection (BatteryMonitorService with warnings/auto-pause)
- ✅ P3: Performance profiling (PerformanceMonitorService with metrics)
- ❌ P3: Feature folder reorganization (skipped - low value refactoring)

**Completed P4 UI Enhancements (December 11, 2025):**

- ✅ PlaceDetailsSheet (SfBottomSheet) - trip place details with edit mode
- ✅ TimelineEntrySheet (SfBottomSheet) - timeline entry details with edit mode
- ✅ NotesEditorControl - Rich text editing with Quill.js
- ✅ ComboBox - Custom dropdown with lazy keyboard activation (replaces SearchableDropdown)
- ✅ SfSegmentedControl on GroupsPage - List/Map toggle
- ✅ SfShimmer loading placeholders on TripsPage, TimelinePage, GroupsPage
- ✅ SfDatePicker on TimelinePage - Date navigation

**Completed December 13, 2025:**

- ✅ ActivitySyncService - Server sync with local caching (6-hour interval)
- ✅ Settings sync in foreground service - Robust periodic sync (6-hour interval)
- ✅ Notification simplification - Check In action only (removed Pause/Resume/Stop)
- ✅ Notification text - Shows "Timeline: ON/OFF ±Xm" instead of fix count
- ✅ CheckInSheet improvements - Proper lifecycle management, bottom sheet state fixes
- ✅ ComboBox lazy keyboard - Keyboard only appears when user taps search field

**Completed Features:**

- ✅ Lock Screen Overlay (PIN entry on app resume)
- ✅ Route Polyline (navigation route visualization on map)
- ✅ Navigation System with OSRM integration
- ✅ Trip Sidebar (SfNavigationDrawer with places + segments list)
- ✅ Segment Visualization (transport mode styled polylines on map)
- ✅ Loading States (reusable LoadingOverlay control)
- ✅ Offline Banner (connectivity status on key pages)
- ✅ iOS Background Banner (blue status bar during tracking)
- ✅ Page Transition Animations (modal animations for overlay pages)
- ✅ App Icon and Splash Screen (Wayfarer themed)
- ✅ Navigation Settings (audio, vibration, auto-reroute, km/miles)
- ✅ About Page (version, open source libraries, OSM attribution)
- ✅ Toast Notifications (success, error, warning with animations)
- ✅ Dialog Service (error dialogs with retry option)
- ✅ Diagnostics Page (health checks, performance metrics, log viewer)
- ✅ Battery Monitor (low battery warnings, auto-pause on critical)
- ✅ Performance Monitor (memory tracking, operation timing, GC stats)

**Next Steps:**

- Device testing on Android and iOS
- Unit tests for core algorithms
- Integration tests for services

---

## Navigation System Architecture

**Route Calculation Priority:**

1. **User Segments** - Trip-defined routes with polyline geometry (always preferred)
2. **Cached OSRM** - Previously fetched route if still valid (same destination, within 50m, < 5 min old)
3. **OSRM Fetch** - Online route from `router.project-osrm.org` (no API key, 1 req/sec limit)
4. **Direct Route** - Straight line with bearing + distance (offline fallback)

**Key Services:**

- `TripNavigationService` - Main orchestrator with `CalculateRouteToPlaceAsync()`
- `OsrmRoutingService` - OSRM API client
- `RouteCacheService` - Single-route session cache (stored in Preferences, survives restart)
- `MapService` - Route polyline display with progress tracking

**Design Decision:** No fallback connections between trip places. Direct route is more honest than fake multi-hop routes through trip sequence.

See `docs/reference/NAVIGATION_SYSTEM.md` for full documentation.

---

## Settings & Activity Sync Architecture

**Settings Thresholds** (location_time_threshold, location_distance_threshold):

- Synced by **LocationTrackingService** (foreground service) - guaranteed every 6 hours
- Timer checks hourly, syncs if 6+ hours elapsed
- Completely isolated - sync failures never affect GPS tracking
- Timeout protection (15s max for network operations)

**Activity Types** (for check-in dropdown):

- Synced by **ActivitySyncService** on app startup (opportunistic)
- Local caching in SQLite with seeded defaults for offline
- Fire-and-forget - doesn't block app startup

**Key Design Decision:** Settings sync runs in foreground service because:
- App UI doesn't run 24/7, only the location service does
- Guarantees threshold updates even if user rarely opens app
- Isolated implementation ensures GPS tracking is never affected

---

## What This Is

Fresh implementation of Wayfarer Mobile app with clean architecture based on lessons learned from previous implementation (Wayfarer.Mobile).

## Key Documents

| Document | Purpose |
|----------|---------|
| `docs/DESIGN_SPEC.md` | Complete architecture, design decisions, app flows |
| `docs/IMPLEMENTATION_CHECKLIST.md` | Step-by-step implementation guide |
| `docs/reference/GROUPS_FEATURE.md` | Groups/SSE feature design |
| `docs/reference/NAVIGATION_SYSTEM.md` | A* pathfinding, turn-by-turn navigation |
| `docs/reference/TILE_CACHING.md` | Hybrid tile caching architecture |
| `docs/reference/UI_ENHANCEMENTS.md` | Syncfusion adoption & UI enhancement roadmap |

## Reference Codebase

The previous implementation is at: `C:\Users\stef\source\repos\Wayfarer.Mobile`

**DO NOT MODIFY the old codebase.** Use it for reference only.

### What to Reference from Old Codebase

| Purpose | Files to Look At |
|---------|------------------|
| API endpoints | `Services/Api/*.cs` |
| Server DTOs | `Models/Dto/*.cs` |
| Database schema | `Services/Database/*.cs` |
| Core models | `Models/*.cs` |
| Geo algorithms | `Services/Geo/*.cs` |
| GPS filtering | `Services/Location/GpsAccuracyFilter.cs`, `LocationFusionEngine.cs` |
| Tile caching | `Services/TileCache/*.cs` |
| Navigation | `Services/Navigation/*.cs` |
| Trip display | `Services/Trip/*.cs` |
| QR scanner | `Services/QrScanner/*.cs` |

### Code to Port Directly (copy and adapt)

- `GeoMath.cs` - Distance/bearing calculations ✅ DONE
- `ThresholdFilter.cs` - Time/distance threshold logic ✅ DONE
- DTOs from `Models/Dto/` - Server communication models ✅ DONE

**NOT NEEDED (native providers are better):**

- `GpsAccuracyFilter.cs` - Native FusedLocationProvider handles filtering
- `BearingStabilityTracker.cs` - Speed-based check is more reliable than accelerometer

## Technology Stack

| Category | Technology | Notes |
|----------|------------|-------|
| Framework | .NET 10 MAUI | Android + iOS only (no Windows) |
| Maps | Mapsui 5.0 | Raster tiles, MBTiles for offline |
| UI Components | Syncfusion MAUI Toolkit | MIT licensed, 30+ controls |
| MVVM | CommunityToolkit.Mvvm | Source generators |
| Database | SQLite-net-pcl | Local storage |
| QR | ZXing.Net.MAUI | QR scanning |

## Architecture Summary

**Single-component LocationTrackingService** that owns:

- GPS acquisition (LocationManager)
- Timer for periodic updates
- SQLite storage (writes directly)
- Notification management
- State machine (Starting, Active, Paused, Stopped)

**UI subscribes via LocationBridge** when visible:

- Service broadcasts location updates
- LocationBridge translates to C# events
- ViewModels bind to LocationBridge properties
- No direct GPS interaction from UI

**Key Principle**: Service IS the tracker. No coordination between components.

See `docs/DESIGN_SPEC.md` Section 6 for full architecture details.

## Timeline Tracking vs Live Location

Two independent concerns:

1. **Timeline Tracking** (Settings toggle)
   - Logs locations to server for timeline history
   - Can be enabled/disabled by user
   - When OFF: no server logging, but GPS still works

2. **Live Location** (Always available)
   - Shows user on map
   - Navigation guidance
   - NOT affected by timeline tracking setting

## Code Style

- All code must be documented with XML comments
- Strict MVVM - ViewModels for ALL pages
- No business logic in code-behind (only UI-specific: map gestures, animations)
- Use CommunityToolkit.Mvvm attributes (`[ObservableProperty]`, `[RelayCommand]`)
- Use Syncfusion components where applicable
- Single Responsibility classes
- Do not create `nul` files or if cannot be avoided by the tools you use, delete them afterwards.

## Git Commit Messages

- Descriptive, explain what was done and why
- No author/co-author credits
- No generated messages

## Project Structure

```bash
WayfarerMobile/
├── src/
│   └── WayfarerMobile/              # MAUI project
│       ├── Core/                    # Pure C#, no MAUI dependencies
│       │   ├── Models/
│       │   ├── Interfaces/
│       │   ├── Algorithms/
│       │   └── Enums/
│       ├── Infrastructure/          # Platform-agnostic implementations
│       │   ├── Database/
│       │   ├── Api/
│       │   └── Services/
│       ├── Platforms/
│       │   ├── Android/
│       │   │   └── Services/
│       │   │       ├── LocationTrackingService.cs
│       │   │       └── LocationBridge.cs
│       │   └── iOS/
│       ├── Features/                # Feature-based organization
│       │   ├── Map/
│       │   ├── Timeline/
│       │   ├── Trips/
│       │   ├── Settings/
│       │   ├── Groups/
│       │   ├── CheckIn/
│       │   └── Onboarding/
│       └── Shared/
│           ├── Controls/
│           ├── Converters/
│           └── Styles/
└── docs/
```

## Implementation Order

1. **Foundation** - Create solution, set up DI, styles
2. **Location Service** - Android LocationTrackingService + LocationBridge
3. **Onboarding** - Permission wizard for first run
4. **Main Map** - Mapsui integration, location indicator
5. **Settings** - Basic settings with tracking toggle
6. **Timeline** - Server sync, location history
7. **Trips** - Trip display, sidebar, offline maps
8. **Navigation** - Turn-by-turn with audio
9. **Groups** - SSE live location sharing
10. **Polish** - Animations, error handling, testing

See `docs/IMPLEMENTATION_CHECKLIST.md` and the reset *.md documentation files in `docs/` directory for detailed steps.
