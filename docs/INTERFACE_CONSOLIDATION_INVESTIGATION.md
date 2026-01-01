# Interface Consolidation Investigation

**Issue:** #79 - refactor: consolidate interface definitions
**Branch:** `refactor/consolidate-interfaces`
**Date:** 2026-01-01

---

## Executive Summary

Interface definitions are scattered across three locations:
1. `src/WayfarerMobile/Core/Interfaces/` - **17 interfaces** (WRONG location)
2. `src/WayfarerMobile.Core/Interfaces/` - **11 interfaces** (TARGET location)
3. **Embedded in service files** - **4 interfaces + helper types**

Additionally, `TripNavigationService` has **no interface** at all.

### Key Finding: Show-Stopper Dependencies

Several interfaces in `WayfarerMobile/Core/Interfaces/` have **Mapsui dependencies** that prevent simple file moves to `WayfarerMobile.Core`:
- Mapsui is a MAUI-only NuGet package
- `WayfarerMobile.Core` is a net10.0 class library with no MAUI dependencies
- Adding Mapsui to Core would defeat its purpose as a lightweight shared library

**Resolution Options:**
1. **Keep Mapsui-dependent interfaces in WayfarerMobile** (but consolidate namespace)
2. **Abstract away Mapsui types** (significant refactor)
3. **Accept dual locations** with clear reasoning

---

## Detailed Findings

### Category A: Interfaces in `WayfarerMobile/Core/Interfaces/` (17 total)

All use namespace `WayfarerMobile.Core.Interfaces` - so **no namespace changes needed** if we just move files.

#### A1. Mapsui-Dependent Interfaces (SHOW-STOPPERS for Core project)

| Interface | File | Mapsui Dependencies | Consumers |
|-----------|------|---------------------|-----------|
| `IMapBuilder` | IMapBuilder.cs | `Map`, `WritableLayer`, `MPoint` | MapBuilder, tests |
| `ITripLayerService` | ITripLayerService.cs | `WritableLayer`, `MPoint` | TripLayerService, MainViewModel |
| `ILocationLayerService` | ILocationLayerService.cs | `WritableLayer`, `MPoint` | LocationLayerService, MainViewModel |
| `ITimelineLayerService` | ITimelineLayerService.cs | `WritableLayer`, `MPoint` | TimelineLayerService, TimelineViewModel |
| `IGroupLayerService` | IGroupLayerService.cs | `WritableLayer`, `MPoint` | GroupLayerService, GroupsViewModel |
| `IDroppedPinLayerService` | IDroppedPinLayerService.cs | `WritableLayer` | DroppedPinLayerService, MainViewModel |

**Impact:** Cannot move to `WayfarerMobile.Core/Interfaces/` without adding Mapsui dependency to Core project.

**Recommendation:** Keep in a dedicated folder within WayfarerMobile project, e.g., `src/WayfarerMobile/Interfaces/Map/`

#### A2. Data Entity-Dependent Interfaces

| Interface | File | Problematic Dependencies | Can Move to Core? |
|-----------|------|--------------------------|-------------------|
| `ITripDownloadService` | ITripDownloadService.cs | `WayfarerMobile.Data.Entities.DownloadedTripEntity`, `TripDownloadStateEntity` | **NO** - references SQLite entities |
| `IActivitySyncService` | IActivitySyncService.cs | `WayfarerMobile.Data.Entities.ActivityType` | **NO** - references SQLite entities |

**Impact:** These interfaces reference SQLite entity types that only exist in the MAUI project.

**Recommendation:** Keep in WayfarerMobile project or extract entity interfaces.

#### A3. Clean Interfaces (CAN move to Core)

| Interface | File | Dependencies | Lines | Can Move? |
|-----------|------|--------------|-------|-----------|
| `IApiClient` | IApiClient.cs | `WayfarerMobile.Core.Models` only | 336 | **YES** |
| `ILocationBridge` | ILocationBridge.cs | `WayfarerMobile.Core.Enums`, `.Models` | 63 | **YES** |
| `IPermissionsService` | IPermissionsService.cs | None (self-contained) | 89 | **YES** |
| `INavigationAudioService` | INavigationAudioService.cs | None | 62 | **YES** |
| `IWakeLockService` | IWakeLockService.cs | None | 25 | **YES** |
| `IAppLifecycleService` | IAppLifecycleService.cs | None (self-contained) | 72 | **YES** |
| `IExceptionHandlerService` | IExceptionHandlerService.cs | None | 29 | **YES** |
| `IGroupsService` | IGroupsService.cs | `WayfarerMobile.Core.Models` only | 63 | **YES** |
| `IAppLockService` | IAppLockService.cs | None | 132 | **YES** |

**Total moveable:** 9 interfaces

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

| Interface | Embedded In | Helper Types Also Embedded | Lines |
|-----------|-------------|---------------------------|-------|
| `ITripSyncService` | TripSyncService.cs:1808 | `SyncFailureEventArgs`, `SyncQueuedEventArgs`, `SyncSuccessEventArgs`, `EntityCreatedEventArgs` | ~100 |
| `ITimelineSyncService` | TimelineSyncService.cs:579 | Shares sync event args with TripSyncService | ~50 |
| `IDownloadNotificationService` | DownloadNotificationService.cs:10 | `InterruptedDownloadInfo`, `DownloadInterruptionReason` | ~120 |
| `IWikipediaService` | WikipediaService.cs:11 | `WikipediaSearchResult` | ~30 |

**Impact:**
- Must extract interface + helper types together
- Sync event args are shared between TripSyncService and TimelineSyncService

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

---

## Reference Analysis

### Files Using `WayfarerMobile.Core.Interfaces` Namespace

**78 files** reference this namespace. Key categories:

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
- `DownloadProgressEventArgs`
- `CacheLimitEventArgs`
- `CacheLimitLevel`
- `CacheLimitCheckResult`
- `CacheQuotaCheckResult`
- `DownloadTerminalEventArgs`
- `DownloadPausedEventArgs`
- `DownloadPauseReasonType`

**Reason:** Test project targets net10.0 and cannot reference MAUI-specific types directly.

---

## Refactoring Plan

### Phase 1: Extract Embedded Interfaces (Low Risk)

| Task | Source | Target | Helper Types |
|------|--------|--------|--------------|
| 1.1 | TripSyncService.cs | `WayfarerMobile.Core/Interfaces/ITripSyncService.cs` | Extract sync event args to shared file |
| 1.2 | TimelineSyncService.cs | `WayfarerMobile.Core/Interfaces/ITimelineSyncService.cs` | Uses shared sync event args |
| 1.3 | DownloadNotificationService.cs | `WayfarerMobile/Interfaces/IDownloadNotificationService.cs` | `InterruptedDownloadInfo`, `DownloadInterruptionReason` |
| 1.4 | WikipediaService.cs | `WayfarerMobile/Interfaces/IWikipediaService.cs` | `WikipediaSearchResult` |

**Breaking changes:** None - namespace stays the same

### Phase 2: Move Clean Interfaces to Core (Low Risk)

Move these 9 interfaces from `WayfarerMobile/Core/Interfaces/` to `WayfarerMobile.Core/Interfaces/`:

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

### Phase 3: Consolidate Mapsui-Dependent Interfaces (Medium Risk)

Move from `WayfarerMobile/Core/Interfaces/` to `WayfarerMobile/Interfaces/Map/`:

| Interface | Reason |
|-----------|--------|
| `IMapBuilder` | Mapsui dependency |
| `ITripLayerService` | Mapsui dependency |
| `ILocationLayerService` | Mapsui dependency |
| `ITimelineLayerService` | Mapsui dependency |
| `IGroupLayerService` | Mapsui dependency |
| `IDroppedPinLayerService` | Mapsui dependency |

**Breaking changes:** None - namespace stays the same

### Phase 4: Handle Entity-Dependent Interfaces (Medium Risk)

Move from `WayfarerMobile/Core/Interfaces/` to `WayfarerMobile/Interfaces/`:

| Interface | Reason |
|-----------|--------|
| `ITripDownloadService` | References `DownloadedTripEntity`, `TripDownloadStateEntity` |
| `IActivitySyncService` | References `ActivityType` entity |

**Breaking changes:** None - namespace stays the same

### Phase 5: Create Missing Interface (Low Risk)

Create `ITripNavigationService` interface:
- Extract from `TripNavigationService` class
- Update DI registration to interface
- Update all consumers

**Breaking changes:**
- `MauiProgram.cs` DI registration change
- 4 ViewModel constructor parameter type changes

### Phase 6: Cleanup (Low Risk)

- Delete empty `WayfarerMobile/Core/Interfaces/` directory
- Update any stale imports
- Run full test suite

---

## File Operations Summary

### Files to CREATE

| File | Content |
|------|---------|
| `src/WayfarerMobile.Core/Interfaces/ITripSyncService.cs` | Extracted from TripSyncService.cs |
| `src/WayfarerMobile.Core/Interfaces/ITimelineSyncService.cs` | Extracted from TimelineSyncService.cs |
| `src/WayfarerMobile.Core/Interfaces/SyncEventArgs.cs` | Shared sync event args |
| `src/WayfarerMobile/Interfaces/IDownloadNotificationService.cs` | Extracted from DownloadNotificationService.cs |
| `src/WayfarerMobile/Interfaces/IWikipediaService.cs` | Extracted from WikipediaService.cs |
| `src/WayfarerMobile/Interfaces/Map/` | Directory for Mapsui-dependent interfaces |
| `src/WayfarerMobile.Core/Interfaces/ITripNavigationService.cs` | New interface for TripNavigationService |

### Files to MOVE

| From | To |
|------|-----|
| `WayfarerMobile/Core/Interfaces/IApiClient.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/ILocationBridge.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IPermissionsService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/INavigationAudioService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IWakeLockService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IAppLifecycleService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IExceptionHandlerService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IGroupsService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IAppLockService.cs` | `WayfarerMobile.Core/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IMapBuilder.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/ITripLayerService.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/ILocationLayerService.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/ITimelineLayerService.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/IGroupLayerService.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/IDroppedPinLayerService.cs` | `WayfarerMobile/Interfaces/Map/` |
| `WayfarerMobile/Core/Interfaces/ITripDownloadService.cs` | `WayfarerMobile/Interfaces/` |
| `WayfarerMobile/Core/Interfaces/IActivitySyncService.cs` | `WayfarerMobile/Interfaces/` |

### Files to MODIFY

| File | Changes |
|------|---------|
| `TripSyncService.cs` | Remove embedded interface + event args |
| `TimelineSyncService.cs` | Remove embedded interface |
| `DownloadNotificationService.cs` | Remove embedded interface + types |
| `WikipediaService.cs` | Remove embedded interface + result class |
| `TripNavigationService.cs` | Implement new interface |
| `MauiProgram.cs` | Update TripNavigationService DI registration |
| `MainViewModel.cs` | Change TripNavigationService to ITripNavigationService |
| `TripsViewModel.cs` | Change TripNavigationService to ITripNavigationService |
| `GroupsViewModel.cs` | Change TripNavigationService to ITripNavigationService |
| `NavigationHudViewModel.cs` | Change TripNavigationService to ITripNavigationService |

### Files to DELETE

| File | Reason |
|------|--------|
| `WayfarerMobile/Core/Interfaces/` (entire directory) | Emptied after moves |

---

## Test Impact

### Tests That May Need Updates

| Test File | Potential Impact |
|-----------|-----------------|
| `TripDownloadServiceTests.cs` | Uses mock types from Infrastructure |
| `MapBuilderTests.cs` | Uses Mapsui types directly |
| `TripLayerServiceTests.cs` | Uses Mapsui types |
| `LocationLayerServiceTests.cs` | Uses Mapsui types |
| `TimelineLayerServiceTests.cs` | Uses Mapsui types |
| `GroupLayerServiceTests.cs` | Uses Mapsui types |
| `DroppedPinLayerServiceTests.cs` | Uses Mapsui types |

### Test Infrastructure Duplication

The file `tests/WayfarerMobile.Tests/Infrastructure/TripDownloadTypes.cs` duplicates types from `ITripDownloadService.cs`. This is intentional because:
- Test project is net10.0 (not MAUI)
- Cannot reference MAUI project directly
- Types needed for test mocking

**Recommendation:** Keep duplicates but add comment explaining why.

---

## Risk Assessment

| Phase | Risk | Mitigation |
|-------|------|------------|
| Phase 1: Extract embedded | Low | Namespace unchanged, pure extraction |
| Phase 2: Move clean interfaces | Low | File moves only, no code changes |
| Phase 3: Mapsui interfaces | Medium | New folder structure, test path updates |
| Phase 4: Entity interfaces | Medium | New folder structure |
| Phase 5: Create ITripNavigationService | Low | New interface, straightforward DI update |
| Phase 6: Cleanup | Low | Just delete empty directory |

---

## Final Directory Structure

```
src/
├── WayfarerMobile.Core/
│   └── Interfaces/
│       ├── ISettingsService.cs          (existing)
│       ├── IToastService.cs             (existing)
│       ├── IDialogService.cs            (existing)
│       ├── ICacheStatusService.cs       (existing)
│       ├── ILocalNotificationService.cs (existing)
│       ├── ILocationSyncEventBridge.cs  (existing)
│       ├── ISseClientFactory.cs         (existing)
│       ├── ISseClient.cs                (existing)
│       ├── ITextToSpeechService.cs      (existing)
│       ├── IVisitApiClient.cs           (existing)
│       ├── IVisitNotificationService.cs (existing)
│       ├── IApiClient.cs                (MOVED)
│       ├── ILocationBridge.cs           (MOVED)
│       ├── IPermissionsService.cs       (MOVED)
│       ├── INavigationAudioService.cs   (MOVED)
│       ├── IWakeLockService.cs          (MOVED)
│       ├── IAppLifecycleService.cs      (MOVED)
│       ├── IExceptionHandlerService.cs  (MOVED)
│       ├── IGroupsService.cs            (MOVED)
│       ├── IAppLockService.cs           (MOVED)
│       ├── ITripSyncService.cs          (EXTRACTED)
│       ├── ITimelineSyncService.cs      (EXTRACTED)
│       ├── ITripNavigationService.cs    (NEW)
│       └── SyncEventArgs.cs             (NEW - shared)
│
└── WayfarerMobile/
    ├── Interfaces/
    │   ├── IDownloadNotificationService.cs (EXTRACTED)
    │   ├── IWikipediaService.cs            (EXTRACTED)
    │   ├── ITripDownloadService.cs         (MOVED)
    │   ├── IActivitySyncService.cs         (MOVED)
    │   └── Map/
    │       ├── IMapBuilder.cs              (MOVED)
    │       ├── ITripLayerService.cs        (MOVED)
    │       ├── ILocationLayerService.cs    (MOVED)
    │       ├── ITimelineLayerService.cs    (MOVED)
    │       ├── IGroupLayerService.cs       (MOVED)
    │       └── IDroppedPinLayerService.cs  (MOVED)
    │
    └── Core/
        └── Interfaces/ (DELETED - empty after refactor)
```

---

## Checklist for Implementation

- [ ] Phase 1.1: Extract ITripSyncService + sync event args
- [ ] Phase 1.2: Extract ITimelineSyncService (use shared event args)
- [ ] Phase 1.3: Extract IDownloadNotificationService + helper types
- [ ] Phase 1.4: Extract IWikipediaService + WikipediaSearchResult
- [ ] Phase 2: Move 9 clean interfaces to Core
- [ ] Phase 3: Create Map/ subfolder, move 6 Mapsui interfaces
- [ ] Phase 4: Move 2 entity-dependent interfaces
- [ ] Phase 5: Create ITripNavigationService, update DI + consumers
- [ ] Phase 6: Delete WayfarerMobile/Core/Interfaces/
- [ ] Run all tests
- [ ] Build Android + iOS
