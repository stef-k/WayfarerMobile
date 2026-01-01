# Cache Service Consolidation Plan

Issue #90 - Consolidate scattered cache status and overlay implementations

## Current State (Scattered)

| Component | Location | Purpose | Has Interface? |
|-----------|----------|---------|----------------|
| `CacheStatusService` | MAUI/Services/TileCache | Status indicator ("green/yellow/red"), detailed info | Yes (Core) |
| `CacheOverlayService` | MAUI/Services/TileCache | Draws circles on map | No |
| `ICacheStatusService` | Core/Interfaces | Status-only interface | N/A |

**Problems:**
1. MainViewModel injects TWO concrete services and orchestrates them manually
2. Related functionality is split across services with no unified interface
3. Test file `CacheStatusResultTests.cs` tests deleted types (`CacheStatusSummaryResult`, `DetailedCacheStatusResult`, `ZoomCoverageResult`)

## Target State (Consolidated)

| Component | Location | Purpose |
|-----------|----------|---------|
| `ICacheVisualizationService` | MAUI/Interfaces | Unified interface for status + overlay |
| `CacheVisualizationService` | MAUI/Services/TileCache | Facade combining both services |

## Implementation Checklist

### Phase 1: Delete Dead Test Code
- [ ] Delete `tests/WayfarerMobile.Tests/Unit/Models/CacheStatusResultTests.cs`
  - Tests deleted types: `CacheStatusSummaryResult`, `DetailedCacheStatusResult`, `ZoomCoverageResult`
  - These types were in the old `ICacheStatusService` that was rewritten

### Phase 2: Create Unified Interface
- [ ] Create `src/WayfarerMobile/Interfaces/ICacheVisualizationService.cs`
  ```csharp
  namespace WayfarerMobile.Interfaces;

  public interface ICacheVisualizationService
  {
      // === Status (from CacheStatusService) ===
      event EventHandler<string>? StatusChanged;
      string CurrentStatus { get; }
      DetailedCacheInfo? LastDetailedInfo { get; }
      Task ForceRefreshAsync();
      Task<DetailedCacheInfo> GetDetailedCacheInfoAsync();
      Task<DetailedCacheInfo> GetDetailedCacheInfoAsync(double latitude, double longitude);
      string FormatStatusMessage(DetailedCacheInfo info);

      // === Overlay (from CacheOverlayService) ===
      bool IsOverlayVisible { get; }
      Task<bool> ToggleOverlayAsync(Map map, double latitude, double longitude);
      Task ShowOverlayAsync(Map map, double latitude, double longitude);
      void HideOverlay(Map map);
      Task UpdateOverlayAsync(Map map, double latitude, double longitude);
  }
  ```

### Phase 3: Create Facade Implementation
- [ ] Create `src/WayfarerMobile/Services/TileCache/CacheVisualizationService.cs`
  - Inject both `CacheStatusService` and `CacheOverlayService`
  - Delegate all calls to the appropriate underlying service
  - Forward `StatusChanged` event

### Phase 4: Update DI Registration
- [ ] In `MauiProgram.cs`:
  - Keep `CacheStatusService` registration (internal dependency)
  - Keep `CacheOverlayService` registration (internal dependency)
  - Add `CacheVisualizationService` registration
  - Add `ICacheVisualizationService` interface registration
  - Remove `ICacheStatusService` registration (replaced by unified interface)

### Phase 5: Update MainViewModel
- [ ] Replace two service injections:
  ```csharp
  // Before:
  private readonly CacheStatusService _cacheStatusService;
  private readonly CacheOverlayService _cacheOverlayService;

  // After:
  private readonly ICacheVisualizationService _cacheService;
  ```
- [ ] Update constructor parameter
- [ ] Update `ShowCacheStatusAsync()` to use unified interface
- [ ] Update `OnCacheStatusChanged` subscription

### Phase 6: Cleanup Old Interface
- [ ] Delete or deprecate `ICacheStatusService` from Core
  - Option A: Delete entirely (breaking change for any external consumers)
  - Option B: Keep as internal implementation detail, not for DI

### Phase 7: Build and Test
- [ ] Run `dotnet build` - expect 0 errors
- [ ] Run `dotnet test` - expect all tests pass
- [ ] Verify cache indicator works in app
- [ ] Verify overlay toggle works in app

## Files to Modify

| File | Action |
|------|--------|
| `tests/.../CacheStatusResultTests.cs` | DELETE |
| `src/WayfarerMobile/Interfaces/ICacheVisualizationService.cs` | CREATE |
| `src/WayfarerMobile/Services/TileCache/CacheVisualizationService.cs` | CREATE |
| `src/WayfarerMobile/MauiProgram.cs` | MODIFY (DI) |
| `src/WayfarerMobile/ViewModels/MainViewModel.cs` | MODIFY |
| `src/WayfarerMobile.Core/Interfaces/ICacheStatusService.cs` | DELETE or KEEP |

## Model Classes Location

The model classes stay in Core (they have no MAUI dependencies):
- `DetailedCacheInfo` - in `ICacheStatusService.cs` (Core)
- `ZoomLevelCoverage` - in `ICacheStatusService.cs` (Core)

The unified interface will reference these Core types.

## Why Facade Pattern?

Using a facade instead of merging the implementations:
1. **Separation of concerns**: Status calculation and overlay rendering are distinct responsibilities
2. **Testability**: Can mock the facade without mocking Mapsui types
3. **Minimal code changes**: Existing services remain intact, only adding a thin wrapper
4. **Reversibility**: Easy to undo if needed

## Dependency Flow

```
MainViewModel
    ↓
ICacheVisualizationService (WayfarerMobile.Interfaces)
    ↓
CacheVisualizationService (facade)
    ├── CacheStatusService (status logic)
    └── CacheOverlayService (overlay rendering)
```
