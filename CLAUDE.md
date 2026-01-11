# WayfarerMobile - Project Rules (Additive)

These instructions are **project-specific additions**. They are intentionally limited to avoid duplicating or contradicting the **global** user rules (for workflow, git safety, general engineering standards, etc.). If something here conflicts with global rules, treat it as a mistake and follow the global rules.

## What This Is

Fresh implementation of the Wayfarer Mobile app with a clean architecture, incorporating lessons learned from the previous implementation (`Wayfarer.Mobile`).

## Reference Codebase

The previous implementation is at: `~\source\repos\Wayfarer.Mobile` if not available locally use gh to access it on github.
Backend server code is at: `~\source\repos\Wayfarer`

**Do not modify the old codebase or the backend.** Use them for reference only.

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

## Navigation System Architecture

### Route Calculation Priority

1. **User Segments**
   Trip-defined routes with polyline geometry (always preferred)

2. **Cached OSRM**
   Previously fetched route if still valid:
   - same destination
   - within 50 meters
   - less than 5 minutes old

3. **OSRM Fetch**
   Online route from `router.project-osrm.org`:
   - no API key
   - throttle to 1 request/second

4. **Direct Route**
   Straight line with bearing + distance (offline fallback)

### Key Services

- `TripNavigationService`
  Main orchestrator, owns `CalculateRouteToPlaceAsync()`

- `OsrmRoutingService`
  OSRM API client

- `RouteCacheService`
  Single-route session cache (stored in Preferences, survives restart)

- `MapService`
  Route polyline display with progress tracking

### Design Decision

Do not fabricate “fallback connections” between trip places to simulate a multi-hop itinerary. If a real route cannot be computed, prefer honest fallbacks (direct line / distance + bearing) rather than misleading multi-hop polylines.

See `docs/reference/NAVIGATION_SYSTEM.md` for full documentation.

## Technology Stack

| Category | Technology | Notes |
|----------|------------|-------|
| Framework | .NET 10 MAUI | Android + iOS only (no Windows) |
| Maps | Mapsui 5.0 | Raster tiles, MBTiles for offline |
| UI Components | Syncfusion MAUI Toolkit | MIT licensed, 30+ controls |
| MVVM | CommunityToolkit.Mvvm | Source generators |
| Database | SQLite-net-pcl | Local storage |
| QR | ZXing.Net.MAUI | QR scanning |

## Code Conventions (Project-Specific)

- All code must be documented with XML comments (public surface area; add internal comments where clarity is needed).
- Strict MVVM: ViewModels for **all** pages.
- No business logic in code-behind (code-behind may contain only UI-specific behaviors such as map gestures and animations).
- Prefer CommunityToolkit.Mvvm attributes: `[ObservableProperty]`, `[RelayCommand]`.
- Use Syncfusion components where they provide clear value and reduce custom UI complexity.

## Active Refactoring: Issues #75, #76, #77

**IMPORTANT: Read this section before any work on these issues.**

A large-scale refactoring is in progress. Issue #93 is the **master orchestration document** - always check it first for current status, phase order, and rules.

### Quick Reference

| Issue | Scope | Status |
|-------|-------|--------|
| #93 | Master orchestration (READ FIRST) | Open |
| #77 | TripSyncService + TripDownloadService → 6 Services | Open |
| #76 | TripsViewModel → 3 ViewModels | Open |
| #75 | MainViewModel → 4 ViewModels | Open |

### Branch Strategy

```
main (stable) ──► DO NOT commit refactoring work here
  │
  └── develop (integration branch) ──► All refactoring merges here first
        │
        └── feature/refactor-* ──► Individual phase branches
```

### Mandatory Rules

1. **All refactoring branches base off `develop`**, not `main`
2. **All refactoring PRs target `develop`**, not `main`
3. **Phase order is STRICT**: Phase 0 → #77 → #76 → #75 (dependencies block progress)
4. **Hotfix forward-merge**: If ANY change lands on `main`, immediately merge `main` → `develop`
5. **Check #93** for current phase status before starting work

### Phase Dependencies

```
Phase 0 (infrastructure) ──► BLOCKING
    │
    ├── ITripStateManager (replaces static CurrentLoadedTripId)
    ├── CompositeDisposable pattern
    ├── Characterization tests
    └── Race condition fixes
    │
    ▼
Phase 1: #77 Services ──► Phase 2: #76 TripsViewModel ──► Phase 3: #75 MainViewModel
```

### Before Starting Work

1. `git fetch origin`
2. Check which phase is active in #93
3. Ensure you're on `develop` or a feature branch based on `develop`
