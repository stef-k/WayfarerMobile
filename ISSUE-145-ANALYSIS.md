# Issue #145 (Umbrella) Investigation Findings

## Scope
- Issues: #145 (umbrella), #118, #119
- Evidence: `wayfarer-app-20260111.log`

## Summary
The CREATE flows for Places and Regions do not persist offline entries and do not reconcile temp IDs to server IDs in local data or the in-memory model. As a result, subsequent UPDATE/DELETE operations cannot find the offline entry and are sent to the server with temporary IDs. This matches the observed behavior in #118 and #119. The log also shows deterministic crashes tied to image loads after the Android activity is destroyed.

## Findings

### F1: Offline CREATE does not persist to offline tables
- **Evidence**: `TripSyncService.CreatePlaceAsync` / `CreateRegionAsync` only enqueue mutations when offline; no `InsertOfflinePlaceAsync` / `InsertOfflineAreaAsync` is called in any CREATE path.
- **Impact**: UPDATE operations read the offline entry to apply optimistic updates and to store originals for recovery. When the entry is missing, updates are effectively no-ops locally and do not reconcile to the server later.
- **Files**: `src/WayfarerMobile/Services/TripSyncService.cs`

### F2: Temp IDs are never reconciled in memory
- **Evidence**: `MainViewModel` creates `TripPlace` / `TripRegion` with a temp ID, adds it to collections, then calls `CreatePlaceAsync` / `CreateRegionAsync` but ignores the returned server ID and does not subscribe to `TripSyncService.EntityCreated`.
- **Impact**: Subsequent updates reference the temp ID; server rejects, UI appears to update but server does not reflect changes.
- **Files**: `src/WayfarerMobile/ViewModels/MainViewModel.cs`, `src/WayfarerMobile/Services/TripSyncService.cs`

### F3: Queue sync does not reconcile offline data or pending mutations
- **Evidence**: `ProcessPlaceCreateAsync` / `ProcessRegionCreateAsync` emit `EntityCreated` but do not:
  - Update offline entries from temp ID to server ID
  - Rewrite queued UPDATE/DELETE mutations targeting the temp ID
- **Impact**: Even when CREATE eventually syncs, pending updates still target temp IDs.
- **Files**: `src/WayfarerMobile/Services/TripSyncService.cs`

### F4: Deterministic crash on Android due to image loads after activity disposal
- **Evidence**: `wayfarer-app-20260111.log` shows repeated warnings:
  - `Unable to load image stream. You cannot start a load for a destroyed activity`
  - Followed by a fatal `ObjectDisposedException` from `Microsoft.Maui.TaskExtensions.FireAndForget` when resolving `IImageHandler`
- **Impact**: App terminates on activity teardown while image loads are in flight.
- **Likely culprit**: Stream-based image loading via `MauiAssetImageConverter` during view disposal.
- **Files**: `src/WayfarerMobile/Converters/MauiAssetImageConverter.cs`

### F5: Syncfusion effects view reparent warning (non-fatal)
- **Evidence**: Repeated warnings that an `SfEffectsView` is already a child of a `Grid` when added to `SfIconButton`.
- **Impact**: UI warning; not directly tied to #145/#118/#119.
- **Files**: UI controls using Syncfusion components.

## Deterministic Fixes

### D1: Persist offline entry on CREATE for all paths
- On CREATE (offline path), immediately insert `OfflinePlaceEntity` / `OfflineAreaEntity` using the temp ID.
- On CREATE (online success), insert offline entry if missing.
- On CREATE (queue sync success), ensure offline entry exists and update it.

### D2: Reconcile temp ID to server ID everywhere
- After CREATE success, update the offline entry from temp ID to server ID.
- Rewrite any pending mutations (UPDATE/DELETE) that still target the temp ID.
- Update in-memory `TripPlace.Id` / `TripRegion.Id` via either:
  - Using the return value from `CreatePlaceAsync` / `CreateRegionAsync`, or
  - Subscribing to `TripSyncService.EntityCreated` in `MainViewModel`.

### D3: Offline delete of unsynced items should cancel queued CREATE
- If DELETE is issued for a temp ID with a pending CREATE, cancel both (no server call), and remove the offline entry.

### D4: Prevent image load after activity disposal
- Respect cancellation tokens inside `MauiAssetImageConverter`:
  - If `cancellationToken.IsCancellationRequested`, return `null` and avoid starting the load.
- Consider switching to `ImageSource.FromFile` for static assets to avoid async stream loads during teardown.
- Ensure any image-binding cleanup occurs on view/page disposal for pages that host many images.

## Next Steps (if approved)
1. Implement D1-D3 in `TripSyncService` and `MainViewModel`.
2. Patch `MauiAssetImageConverter` to honor cancellation tokens (D4).
3. Re-test the offline create -> update -> sync scenarios for Places/Regions.

