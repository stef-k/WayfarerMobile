# WayfarerMobile - Development Instructions

## Current Status

**Progress:** ~92% Complete (Business Logic Done, UI Remaining)

**Next Tasks (Priority Order):**

1. **P0 - Lock Screen Overlay** - PIN entry on app resume (`Views/LockScreenPage.xaml`)
2. **P0 - Route Polyline** - Navigation route visualization on map
3. **P1 - Trip Sidebar** - Sliding panel with `SfNavigationDrawer`
4. **P1 - Segment Visualization** - Draw trip segments on map
5. **P2 - Loading States** - `SfBusyIndicator` on all pages

**Key Files for Next Session:**

- `docs/IMPLEMENTATION_PLAN.md` - Detailed UI task specs with Syncfusion components
- `docs/IMPLEMENTATION_CHECKLIST.md` - Phase-by-phase progress tracking

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

- `GeoMath.cs` - Distance/bearing calculations
- `GpsAccuracyFilter.cs` - Transportation mode detection, filtering
- `ThresholdFilter.cs` - Time/distance threshold logic
- `BearingCalculator.cs` - Heading calculations
- DTOs from `Models/Dto/` - Server communication models

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
