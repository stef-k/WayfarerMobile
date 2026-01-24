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

See `docs/12-Services.md` for full service documentation.

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

## Git Workflow (Strict)

**The `main` branch is UNTOUCHABLE. No exceptions.**

### Branch Strategy

```
main (stable, protected)
  │
  └── feature/xyz ──► PR ──► merge on GitHub ──► pull locally ──► delete branch
  └── fix/abc     ──► PR ──► merge on GitHub ──► pull locally ──► delete branch
```

### Mandatory Rules

1. **NEVER commit directly to `main`** - all work goes through feature branches
2. **NEVER push directly to `main`** - GitHub branch protection enforces this
3. **ALL changes require a PR** - no exceptions, even for small fixes
4. **Merge PRs on GitHub only** - never merge locally
5. **After PR merge**: pull `main` from remote, then delete the local feature branch
6. **Branch naming**:
   - `feature/<description>` for new features
   - `fix/<description>` for bug fixes
   - `chore/<description>` for maintenance tasks

### Workflow Steps (Every Time)

```bash
# 1. Start work - create branch from main
git checkout main
git pull origin main
git checkout -b feature/my-feature

# 2. Do work, commit often
git add -A && git commit -m "description"

# 3. Push and create PR
git push -u origin feature/my-feature
gh pr create --base main --title "..." --body "..."

# 4. After PR is merged on GitHub - cleanup
git checkout main
git pull origin main
git branch -D feature/my-feature
```

### Why No `develop` Branch?

- Simpler workflow for quick iterations
- PRs provide sufficient review gates
- No release staging needed
- Less merge conflict overhead
