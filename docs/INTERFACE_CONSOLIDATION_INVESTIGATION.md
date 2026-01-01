# Interface Consolidation Investigation

**Issue:** #79 - refactor: consolidate interface definitions
**Branch:** `refactor/consolidate-interfaces`
**Date:** 2026-01-01

---

## Peer Review Summary

**Reviewed by:** Architect Reviewer, Code Reviewer
**Review Date:** 2026-01-01

### Critical Issues Identified

| Issue | Severity | Resolution |
|-------|----------|------------|
| **Namespace strategy** - Using `WayfarerMobile.Core.Interfaces` for files in `WayfarerMobile/Interfaces/` is a "semantic lie" | HIGH | Use `WayfarerMobile.Interfaces` namespace for MAUI-dependent interfaces |
| **Test infrastructure collision** - `TripDownloadTypes.cs` uses same namespace as production types | HIGH | Change test namespace to `WayfarerMobile.Tests.Infrastructure` |
| **Event args location** - Pure event arg types from ITripDownloadService should be in Core | MEDIUM | Move 8 event args types to Core, keep interface in MAUI |
| **Phase 1 ordering** - Sync event args must be extracted before sync interfaces | MEDIUM | Add explicit sub-step ordering |

### Additional Findings

- **Missing navigation interfaces:** Verify if `OsrmRoutingService` and `RouteCacheService` need interfaces
- **DIP violation:** `ITripDownloadService` returns SQLite entities (track as separate refactor)
- **Missing test file:** Add `GroupsServiceTests.cs` to test impact table
- **Phase 1 consumers:** Document ViewModels consuming sync interfaces

### Decision: Namespace Strategy

**APPROVED CHANGE:** MAUI-dependent interfaces will use `WayfarerMobile.Interfaces` namespace (not `WayfarerMobile.Core.Interfaces`).

**Rationale:**
- Namespace should reflect actual file location
- Test project cannot access MAUI project types - namespace makes this explicit
- Consumers already need separate `using` statements for Mapsui types

**Impact:** Files moving to `WayfarerMobile/Interfaces/` will need namespace change from `WayfarerMobile.Core.Interfaces` to `WayfarerMobile.Interfaces`.

---

## Executive Summary

Interface definitions are scattered across three locations:
1. `src/WayfarerMobile/Core/Interfaces/` - **17 interfaces** (WRONG location)
2. `src/WayfarerMobile.Core/Interfaces/` - **11 interfaces** (TARGET for pure interfaces)
3. **Embedded in service files** - **4 interfaces + helper types**

Additionally, `TripNavigationService` has **no interface** at all.

### Consolidation Strategy

**Two-location model based on dependencies:**

| Location | Namespace | Criteria | Count |
|----------|-----------|----------|-------|
| `WayfarerMobile.Core/Interfaces/` | `WayfarerMobile.Core.Interfaces` | Pure interfaces with no MAUI/platform dependencies | **20** |
| `WayfarerMobile/Interfaces/` | `WayfarerMobile.Interfaces` | Interfaces depending on Mapsui, SQLite entities, or MAUI-specific types | **10** |

This maintains clean architectural separation:
- `WayfarerMobile.Core` remains a lightweight net10.0 library with no MAUI dependencies
- `WayfarerMobile/Interfaces/` houses platform-specific abstractions with honest namespace
- Pure event args types (e.g., `DownloadProgressEventArgs`) go to Core even if the interface stays in MAUI

---

## Detailed Findings

### Category A: Interfaces in `WayfarerMobile/Core/Interfaces/` (17 total)

Currently all use namespace `WayfarerMobile.Core.Interfaces`. After refactor:
- **9 clean interfaces** → keep `WayfarerMobile.Core.Interfaces` namespace
- **8 MAUI-dependent interfaces** → change to `WayfarerMobile.Interfaces` namespace

#### A1. MAUI-Dependent Interfaces (Stay in WayfarerMobile)

| Interface | File | MAUI Dependencies | Target |
|-----------|------|-------------------|--------|
| `IMapBuilder` | IMapBuilder.cs | Mapsui: `Map`, `WritableLayer`, `MPoint` | `WayfarerMobile/Interfaces/` |
| `ITripLayerService` | ITripLayerService.cs | Mapsui: `WritableLayer`, `MPoint` | `WayfarerMobile/Interfaces/` |
| `ILocationLayerService` | ILocationLayerService.cs | Mapsui: `WritableLayer`, `MPoint` | `WayfarerMobile/Interfaces/` |
| `ITimelineLayerService` | ITimelineLayerService.cs | Mapsui: `WritableLayer`, `MPoint` | `WayfarerMobile/Interfaces/` |
| `IGroupLayerService` | IGroupLayerService.cs | Mapsui: `WritableLayer`, `MPoint` | `WayfarerMobile/Interfaces/` |
| `IDroppedPinLayerService` | IDroppedPinLayerService.cs | Mapsui: `WritableLayer` | `WayfarerMobile/Interfaces/` |
| `ITripDownloadService` | ITripDownloadService.cs | SQLite: `DownloadedTripEntity`, `TripDownloadStateEntity` | `WayfarerMobile/Interfaces/` |
| `IActivitySyncService` | IActivitySyncService.cs | SQLite: `ActivityType` entity | `WayfarerMobile/Interfaces/` |

**Total:** 8 interfaces → `WayfarerMobile/Interfaces/`

#### A2. Clean Interfaces (Move to Core)

| Interface | File | Dependencies | Target |
|-----------|------|--------------|--------|
| `IApiClient` | IApiClient.cs | `WayfarerMobile.Core.Models` only | `WayfarerMobile.Core/Interfaces/` |
| `ILocationBridge` | ILocationBridge.cs | `WayfarerMobile.Core.Enums`, `.Models` | `WayfarerMobile.Core/Interfaces/` |
| `IPermissionsService` | IPermissionsService.cs | None (self-contained) | `WayfarerMobile.Core/Interfaces/` |
| `INavigationAudioService` | INavigationAudioService.cs | None | `WayfarerMobile.Core/Interfaces/` |
| `IWakeLockService` | IWakeLockService.cs | None | `WayfarerMobile.Core/Interfaces/` |
| `IAppLifecycleService` | IAppLifecycleService.cs | None (self-contained) | `WayfarerMobile.Core/Interfaces/` |
| `IExceptionHandlerService` | IExceptionHandlerService.cs | None | `WayfarerMobile.Core/Interfaces/` |
| `IGroupsService` | IGroupsService.cs | `WayfarerMobile.Core.Models` only | `WayfarerMobile.Core/Interfaces/` |
| `IAppLockService` | IAppLockService.cs | None | `WayfarerMobile.Core/Interfaces/` |

**Total:** 9 interfaces → `WayfarerMobile.Core/Interfaces/`

---

### Category B: Interfaces Already in `WayfarerMobile.Core/Interfaces/` (11 total)

These are correctly placed. No action needed.

| Interface | File | Dependencies |
|-----------|------|--------------|
| `ISettingsService` | ISettingsService.cs | None |
| `IToastService` | IToastService.cs | None |
| `IDialogService` | IDialogService.cs | None |
| `ICacheStatusService` | ICacheStatusService.cs | `WayfarerMobile.Core.Enums` |
| `ILocalNotificationService` | ILocalNotificationService.cs | None |
| `ILocationSyncEventBridge` | ILocationSyncEventBridge.cs | None |
| `ISseClientFactory` | ISseClientFactory.cs | None |
| `ISseClient` | ISseClient.cs | None |
| `ITextToSpeechService` | ITextToSpeechService.cs | None |
| `IVisitApiClient` | IVisitApiClient.cs | `WayfarerMobile.Core.Models` |
| `IVisitNotificationService` | IVisitNotificationService.cs | `WayfarerMobile.Core.Models` |

---

### Category C: Interfaces Embedded in Service Files (4 total)

| Interface | Embedded In | Helper Types Also Embedded | Target |
|-----------|-------------|---------------------------|--------|
| `ITripSyncService` | TripSyncService.cs:1808 | `SyncFailureEventArgs`, `SyncQueuedEventArgs`, `SyncSuccessEventArgs`, `EntityCreatedEventArgs` | `WayfarerMobile.Core/Interfaces/` |
| `ITimelineSyncService` | TimelineSyncService.cs:579 | Shares sync event args with TripSyncService | `WayfarerMobile.Core/Interfaces/` |
| `IDownloadNotificationService` | DownloadNotificationService.cs:10 | `InterruptedDownloadInfo`, `DownloadInterruptionReason` | `WayfarerMobile/Interfaces/` |
| `IWikipediaService` | WikipediaService.cs:11 | `WikipediaSearchResult` | `WayfarerMobile/Interfaces/` |

**Notes:**
- Sync event args are shared between TripSyncService and TimelineSyncService → extract to `SyncEventArgs.cs`
- ITripSyncService and ITimelineSyncService have no MAUI dependencies → go to Core
- IDownloadNotificationService and IWikipediaService are MAUI-specific → stay in WayfarerMobile

---

### Category D: Missing Interfaces (1 identified)

| Class | File | Registered As | Should Have Interface? |
|-------|------|---------------|------------------------|
| `TripNavigationService` | TripNavigationService.cs | Concrete type | **YES** - for testability |

**Consumers:**
- `MainViewModel` (field + constructor injection)
- `TripsViewModel` (field + constructor injection)
- `GroupsViewModel` (field + constructor injection)
- `NavigationHudViewModel` (field + constructor injection)
- `MauiProgram.cs` (DI registration)

**Target:** `WayfarerMobile.Core/Interfaces/ITripNavigationService.cs`

---

## Reference Analysis

### Files Using `WayfarerMobile.Core.Interfaces` Namespace

**78 files** reference this namespace:

| Location | Count | Examples |
|----------|-------|----------|
| Services | 32 | TripSyncService, MapBuilder, LocationLayerService |
| ViewModels | 16 | MainViewModel, TripsViewModel, TimelineViewModel |
| Platform/Android | 5 | LocationBridge, WakeLockService, LocalNotificationService |
| Platform/iOS | 4 | LocationBridge, WakeLockService, TrackingNotificationService |
| Tests | 12 | MapBuilderTests, SettingsViewModelTests, etc. |
| Other | 9 | App.xaml.cs, MauiProgram.cs, etc. |

### Test Infrastructure

Test project has its own copies of types in `tests/WayfarerMobile.Tests/Infrastructure/TripDownloadTypes.cs`:
- `DownloadProgressEventArgs`, `CacheLimitEventArgs`, `CacheLimitLevel`
- `CacheLimitCheckResult`, `CacheQuotaCheckResult`
- `DownloadTerminalEventArgs`, `DownloadPausedEventArgs`, `DownloadPauseReasonType`

**Reason:** Test project targets net10.0 and cannot reference MAUI-specific types directly.

**Recommendation:** Keep duplicates but add comment explaining why.

---

## Refactoring Plan

### Phase 1: Extract Embedded Interfaces (Low Risk)

**Execution order matters:**
1. First extract `SyncEventArgs.cs` (shared event args) - required before sync interfaces
2. Then extract sync interfaces in parallel
3. Then extract MAUI-dependent interfaces

| Task | Source | Target | Namespace | Helper Types |
|------|--------|--------|-----------|--------------|
| 1.0 | TripSyncService.cs:1996-2060 | `WayfarerMobile.Core/Interfaces/SyncEventArgs.cs` | `WayfarerMobile.Core.Interfaces` | 4 event args classes |
| 1.1 | TripSyncService.cs:1808-1995 | `WayfarerMobile.Core/Interfaces/ITripSyncService.cs` | `WayfarerMobile.Core.Interfaces` | Uses SyncEventArgs.cs |
| 1.2 | TimelineSyncService.cs:579-626 | `WayfarerMobile.Core/Interfaces/ITimelineSyncService.cs` | `WayfarerMobile.Core.Interfaces` | Uses SyncEventArgs.cs |
| 1.3 | DownloadNotificationService.cs:10-118 | `WayfarerMobile/Interfaces/IDownloadNotificationService.cs` | `WayfarerMobile.Interfaces` | `InterruptedDownloadInfo`, `DownloadInterruptionReason` |
| 1.4 | WikipediaService.cs:11-54 | `WayfarerMobile/Interfaces/IWikipediaService.cs` | `WayfarerMobile.Interfaces` | `WikipediaSearchResult` |

**Sync interface consumers (need `using` updates after extraction):**
- `MainViewModel.cs`, `MarkerEditorViewModel.cs`, `NotesEditorViewModel.cs`
- `TimelineViewModel.cs`, `TripsViewModel.cs`, `TripDownloadService.cs`

**Breaking changes:**
- MAUI-dependent interfaces (1.3, 1.4) get new namespace → consumer files need `using WayfarerMobile.Interfaces;`

### Phase 2: Move Clean Interfaces to Core (Low Risk)

Move 9 interfaces from `WayfarerMobile/Core/Interfaces/` to `WayfarerMobile.Core/Interfaces/`:

| Interface | Helper Types to Move |
|-----------|---------------------|
| `IApiClient` | `ApiResult`, `ServerSettings` |
| `ILocationBridge` | None |
| `IPermissionsService` | `PermissionRequestResult` |
| `INavigationAudioService` | None |
| `IWakeLockService` | None |
| `IAppLifecycleService` | `NavigationStateSnapshot` |
| `IExceptionHandlerService` | None |
| `IGroupsService` | None |
| `IAppLockService` | None |

**Breaking changes:** None - namespace stays the same

### Phase 3: Move MAUI-Dependent Interfaces (Medium Risk)

Move 8 interfaces from `WayfarerMobile/Core/Interfaces/` to `WayfarerMobile/Interfaces/`:

| Interface | Dependency Type | Namespace Change |
|-----------|-----------------|------------------|
| `IMapBuilder` | Mapsui | → `WayfarerMobile.Interfaces` |
| `ITripLayerService` | Mapsui | → `WayfarerMobile.Interfaces` |
| `ILocationLayerService` | Mapsui | → `WayfarerMobile.Interfaces` |
| `ITimelineLayerService` | Mapsui | → `WayfarerMobile.Interfaces` |
| `IGroupLayerService` | Mapsui | → `WayfarerMobile.Interfaces` |
| `IDroppedPinLayerService` | Mapsui | → `WayfarerMobile.Interfaces` |
| `ITripDownloadService` | SQLite entities | → `WayfarerMobile.Interfaces` |
| `IActivitySyncService` | SQLite entities | → `WayfarerMobile.Interfaces` |

**Special handling for ITripDownloadService:**
- **Interface** stays in `WayfarerMobile/Interfaces/` (has SQLite entity dependencies)
- **Event args types** (pure C# records) move to `WayfarerMobile.Core/Interfaces/DownloadEventArgs.cs`:
  - `DownloadProgressEventArgs`, `CacheLimitEventArgs`, `CacheLimitLevel`
  - `CacheLimitCheckResult`, `CacheQuotaCheckResult`
  - `DownloadTerminalEventArgs`, `DownloadPausedEventArgs`, `DownloadPauseReasonType`

**Breaking changes:**
- All 8 interfaces change namespace → consumers need `using WayfarerMobile.Interfaces;`
- Update `tests/WayfarerMobile.Tests/Infrastructure/TripDownloadTypes.cs` namespace to `WayfarerMobile.Tests.Infrastructure` to avoid collision

### Phase 4: Create Missing Interface (Low Risk)

Create `ITripNavigationService` interface:
- Extract public API from `TripNavigationService` class
- Place in `WayfarerMobile.Core/Interfaces/`
- Update DI registration to interface
- Update all 4 ViewModel consumers

**Breaking changes:**
- `MauiProgram.cs` DI registration change
- 4 ViewModel constructor parameter type changes

### Phase 5: Cleanup (Low Risk)

- Delete empty `WayfarerMobile/Core/Interfaces/` directory
- Verify all imports resolve correctly
- Run full test suite
- Build Android + iOS

---

## File Operations Summary

### Files to CREATE

| File | Content |
|------|---------|
| `src/WayfarerMobile.Core/Interfaces/ITripSyncService.cs` | Extracted from TripSyncService.cs |
| `src/WayfarerMobile.Core/Interfaces/ITimelineSyncService.cs` | Extracted from TimelineSyncService.cs |
| `src/WayfarerMobile.Core/Interfaces/SyncEventArgs.cs` | Shared sync event args |
| `src/WayfarerMobile.Core/Interfaces/ITripNavigationService.cs` | New interface for TripNavigationService |
| `src/WayfarerMobile/Interfaces/IDownloadNotificationService.cs` | Extracted from DownloadNotificationService.cs |
| `src/WayfarerMobile/Interfaces/IWikipediaService.cs` | Extracted from WikipediaService.cs |

### Files to MOVE

**To `WayfarerMobile.Core/Interfaces/`:**
- `IApiClient.cs` (with `ApiResult`, `ServerSettings`)
- `ILocationBridge.cs`
- `IPermissionsService.cs` (with `PermissionRequestResult`)
- `INavigationAudioService.cs`
- `IWakeLockService.cs`
- `IAppLifecycleService.cs` (with `NavigationStateSnapshot`)
- `IExceptionHandlerService.cs`
- `IGroupsService.cs`
- `IAppLockService.cs`

**To `WayfarerMobile/Interfaces/`:**
- `IMapBuilder.cs`
- `ITripLayerService.cs`
- `ILocationLayerService.cs`
- `ITimelineLayerService.cs`
- `IGroupLayerService.cs`
- `IDroppedPinLayerService.cs`
- `ITripDownloadService.cs` (with event args, enums, result types)
- `IActivitySyncService.cs`

### Files to MODIFY

| File | Changes |
|------|---------|
| `TripSyncService.cs` | Remove embedded interface + event args |
| `TimelineSyncService.cs` | Remove embedded interface |
| `DownloadNotificationService.cs` | Remove embedded interface + types |
| `WikipediaService.cs` | Remove embedded interface + result class |
| `TripNavigationService.cs` | Implement `ITripNavigationService` |
| `MauiProgram.cs` | Update DI: `ITripNavigationService` registration |
| `MainViewModel.cs` | Change field/param: `ITripNavigationService` |
| `TripsViewModel.cs` | Change field/param: `ITripNavigationService` |
| `GroupsViewModel.cs` | Change field/param: `ITripNavigationService` |
| `NavigationHudViewModel.cs` | Change field/param: `ITripNavigationService` |

### Files/Directories to DELETE

| Path | Reason |
|------|--------|
| `WayfarerMobile/Core/Interfaces/` | Empty after all moves |

---

## Final Directory Structure

```
src/
├── WayfarerMobile.Core/                        # namespace: WayfarerMobile.Core.Interfaces
│   └── Interfaces/
│       ├── ISettingsService.cs                 (existing)
│       ├── IToastService.cs                    (existing)
│       ├── IDialogService.cs                   (existing)
│       ├── ICacheStatusService.cs              (existing)
│       ├── ILocalNotificationService.cs        (existing)
│       ├── ILocationSyncEventBridge.cs         (existing)
│       ├── ISseClientFactory.cs                (existing)
│       ├── ISseClient.cs                       (existing)
│       ├── ITextToSpeechService.cs             (existing)
│       ├── IVisitApiClient.cs                  (existing)
│       ├── IVisitNotificationService.cs        (existing)
│       ├── IApiClient.cs                       (MOVED from WayfarerMobile)
│       ├── ILocationBridge.cs                  (MOVED)
│       ├── IPermissionsService.cs              (MOVED)
│       ├── INavigationAudioService.cs          (MOVED)
│       ├── IWakeLockService.cs                 (MOVED)
│       ├── IAppLifecycleService.cs             (MOVED)
│       ├── IExceptionHandlerService.cs         (MOVED)
│       ├── IGroupsService.cs                   (MOVED)
│       ├── IAppLockService.cs                  (MOVED)
│       ├── ITripSyncService.cs                 (EXTRACTED)
│       ├── ITimelineSyncService.cs             (EXTRACTED)
│       ├── ITripNavigationService.cs           (NEW)
│       ├── SyncEventArgs.cs                    (NEW - 4 sync event args)
│       └── DownloadEventArgs.cs                (NEW - 8 download event types)
│
└── WayfarerMobile/                             # namespace: WayfarerMobile.Interfaces
    ├── Interfaces/
    │   ├── IMapBuilder.cs                      (MOVED, namespace changed)
    │   ├── ITripLayerService.cs                (MOVED, namespace changed)
    │   ├── ILocationLayerService.cs            (MOVED, namespace changed)
    │   ├── ITimelineLayerService.cs            (MOVED, namespace changed)
    │   ├── IGroupLayerService.cs               (MOVED, namespace changed)
    │   ├── IDroppedPinLayerService.cs          (MOVED, namespace changed)
    │   ├── ITripDownloadService.cs             (MOVED, namespace changed, event args extracted)
    │   ├── IActivitySyncService.cs             (MOVED, namespace changed)
    │   ├── IDownloadNotificationService.cs     (EXTRACTED, new namespace)
    │   └── IWikipediaService.cs                (EXTRACTED, new namespace)
    │
    └── Core/
        └── Interfaces/                         (DELETED - empty after refactor)

tests/
└── WayfarerMobile.Tests/
    └── Infrastructure/
        └── TripDownloadTypes.cs                (DELETED - no longer needed)
```

---

## Test Impact

### Tests That May Need Updates

| Test File | Reason | Action |
|-----------|--------|--------|
| `MapBuilderTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `TripLayerServiceTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `LocationLayerServiceTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `TimelineLayerServiceTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `GroupLayerServiceTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `DroppedPinLayerServiceTests.cs` | Uses Mapsui-dependent interface | Add `using WayfarerMobile.Interfaces;` |
| `TripDownloadServiceTests.cs` | Uses mock types from Infrastructure | Update namespace reference |
| `GroupsServiceTests.cs` | Uses `IGroupsService` | Verify import after move |

### Test Infrastructure Duplication

The file `tests/WayfarerMobile.Tests/Infrastructure/TripDownloadTypes.cs` currently duplicates event args types from `ITripDownloadService.cs`.

**After this refactor:**
- Event args types (`DownloadProgressEventArgs`, etc.) will be in `WayfarerMobile.Core/Interfaces/DownloadEventArgs.cs`
- Test project can reference these directly from Core
- `TripDownloadTypes.cs` can be **deleted** (no longer needed)

**Action:**
1. Before deleting, change namespace to `WayfarerMobile.Tests.Infrastructure` to avoid collision during migration
2. After Phase 3 completes, delete the file and reference Core types directly

---

## Risk Assessment

| Phase | Risk | Mitigation |
|-------|------|------------|
| Phase 1: Extract embedded | Low | Core interfaces keep namespace; MAUI interfaces get new namespace |
| Phase 2: Move clean to Core | Low | File moves only, namespace unchanged |
| Phase 3: Move MAUI-dependent | **Medium** | Namespace changes require consumer updates |
| Phase 4: Create ITripNavigationService | Low | New interface, straightforward DI update |
| Phase 5: Cleanup | Low | Delete empty directory, delete test duplicates |

**Overall Risk: MEDIUM** - Phase 3 requires namespace changes for 10 interfaces, affecting ~20 consumer files. All changes are mechanical (`using` statement updates) and will cause compile errors if missed (safe failure mode).

---

## Implementation Checklist

- [x] **Phase 1: Extract Embedded Interfaces** ✓ COMPLETE
  - [x] 1.0: Extract `SyncEventArgs.cs` (4 event args classes) to Core
  - [x] 1.1: Extract `ITripSyncService.cs` to Core
  - [x] 1.2: Extract `ITimelineSyncService.cs` to Core
  - [x] 1.3: Extract `IDownloadNotificationService.cs` to WayfarerMobile/Interfaces (namespace: `WayfarerMobile.Interfaces`)
  - [x] 1.4: Extract `IWikipediaService.cs` to WayfarerMobile/Interfaces (namespace: `WayfarerMobile.Interfaces`)
  - [x] Update consumer `using` statements for 1.3 and 1.4

- [x] **Phase 2: Move Clean Interfaces to Core** ✓ COMPLETE
  - [x] Move 9 interfaces with their helper types (namespace unchanged)

- [x] **Phase 3: Move MAUI-Dependent Interfaces** ✓ COMPLETE
  - [x] Extract `DownloadEventArgs.cs` (8 types) to Core first
  - [x] Move 8 interfaces to `WayfarerMobile/Interfaces/`
  - [x] Update namespace in each file to `WayfarerMobile.Interfaces`
  - [x] Update all consumer `using` statements (~14 files)
  - [x] ~~Change `TripDownloadTypes.cs` namespace~~ File deleted (types now in Core)

- [x] **Phase 4: Create ITripNavigationService** ✓ COMPLETE
  - [x] Create `ITripNavigationService` interface in Core
  - [x] Update `TripNavigationService` to implement interface
  - [x] Update DI registration in `MauiProgram.cs`
  - [x] Update 4 ViewModel consumers (MainViewModel, TripsViewModel, GroupsViewModel, NavigationHudViewModel)

- [x] **Phase 5: Cleanup** ✓ COMPLETE
  - [x] Delete empty `WayfarerMobile/Core/Interfaces/` directory
  - [x] Delete `tests/WayfarerMobile.Tests/Infrastructure/TripDownloadTypes.cs` (redundant - Core now has these types)
  - [x] Run all tests (1692 passed)
  - [x] Build Android + iOS
