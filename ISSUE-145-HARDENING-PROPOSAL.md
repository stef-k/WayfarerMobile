# Issue #145 Hardening Proposal: TempId-to-ServerId Reconciliation

**Date:** 2026-01-13
**Status:** Pending Review
**Related Issues:** #145, #118, #119

---

## Executive Summary

After implementing D0-D6 fixes for offline-first architecture, manual testing revealed that editing a newly created Region still fails with `"NotFound"`. Root cause analysis shows that **EntityCreated reconciliation was not applied before the edit** - the temp ID was still present when the update was called. This document proposes a deterministic hardening approach with **Phases 1, 2, 3, plus the update-response fix all required** to eliminate temp-ID update paths and silent failures.

---

## Problem Statement

### Observed Behavior
1. User creates "Region 1" - succeeds on mobile and server
2. User edits region name to "Region 2" - visible on mobile, **not on server**
3. Server returns 404 Not Found

### Log Evidence
```
2026-01-13 17:27:24.802 [INF] AddRegion: Added region "06c68356-91bf-4b99-99b0-ded1404d597b" to memory
2026-01-13 17:27:25.829 [INF] AddRegion: Completed successfully for region "06c68356-91bf-4b99-99b0-ded1404d597b"
2026-01-13 17:27:44.938 [WRN] Failed to update region "06c68356-91bf-4b99-99b0-ded1404d597b": "NotFound"
```

The temp client ID is used in all three log entries. The server-assigned ID is never used.

---

## Root Cause Analysis

### Critical Insight: Reconciliation Not Applied Before Edit

The log proves that **reconciliation was not applied before the edit occurred**. Here's why:

**The Edit Flow (src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs:1093-1127):**
```csharp
private async Task EditRegionNameAsync(TripRegion region)
{
    // region = UI copy from SortedRegions with temp ID

    // Search loadedTrip.Regions for matching ID
    var targetRegion = loadedTrip.Regions.FirstOrDefault(r => r.Id == region.Id);
    if (targetRegion == null)
        return;  // Would exit here if ID was already updated!

    // ... update logic ...

    await _tripSyncService.UpdateRegionAsync(targetRegion.Id, ...);  // Uses targetRegion.Id
}
```

**The Proof:**
1. If EntityCreated had updated `loadedTrip.Regions[x].Id` from tempId to serverId...
2. Then `EditRegionNameAsync` would search for `r.Id == region.Id` (tempId from UI copy)
3. This search would **FAIL** (no region has tempId anymore)
4. The method would return early at line 1101-1102
5. **No update would be called at all**

But the log shows the update **WAS called** with the temp ID and got NotFound. This proves:
- `loadedTrip.Regions[x].Id` still contained the temp ID **at the time of the edit**
- EntityCreated either never fired, fired but was queued and hadn't executed yet, or failed silently
- The UI copy and the in-memory region both had the same temp ID when the user tapped Edit

### Root Cause Summary

| Component | Status | Evidence |
|-----------|--------|----------|
| EntityCreated event fired? | **Unknown** | LogDebug filtered in production |
| In-memory ID updated before edit? | **NO** | Update called with temp ID proves region still had temp ID |
| UI copies have temp ID? | **YES** | Expected (SortedRegions creates Region copies) |

**Conclusion:** EntityCreated reconciliation was not applied before the edit - either it never fired, fired too late (queued to UI thread), or failed silently. Fix 1 alone is necessary but NOT sufficient.

### Secondary Issue: Missing UI Notification for Regions

Even if EntityCreated reconciliation worked, the handler doesn't notify the UI to refresh Region copies:

```csharp
// TripItemEditorViewModel.cs:936-948
case "Region":
    var region = loadedTrip.Regions.FirstOrDefault(r => r.Id == e.TempClientId);
    if (region != null)
    {
        region.Id = e.ServerId;  // Updates Regions[x].Id
        // MISSING: _callbacks?.NotifyTripRegionsChanged();
    }
    break;
```

Without `NotifyTripRegionsChanged()`, `SortedRegions` Region copies retain stale temp IDs even after reconciliation.

**Note on Places:** Unlike Regions, `SortedRegions` does NOT clone TripPlace objects - it reuses the same references (see TripModels.cs:269). Therefore, updating `place.Id` in the original collection automatically updates it in `SortedRegions[x].Places[y]`. No explicit notification is needed for Place ID reconciliation.

### Why Multiple Fixes Are Required

| Fix | Purpose | Alone Sufficient? |
|-----|---------|-------------------|
| Fix 1 | Refresh Region UI copies after EntityCreated reconciles | NO - EntityCreated may be late or absent |
| Fix 2 | Immediate reconciliation after online create succeeds | NO - Won't help queued mutation replays or in-flight edits |
| Fix 3 | Merge updates into pending CREATE for offline scenario | NO - Does not cover in-flight edits |
| Update-response fix | Treat failed API updates as failures (not success) | NO - Does not handle temp IDs |
| All | Deterministic behavior across online/offline and in-flight edits | YES |

**Note:** Determinism requires the in-flight edit guard, Fix 3 for offline create -> edit, and the update-response fix to avoid silent successes.

---

## Proposed Fixes

### Fix 1: UI Notification in EntityCreated (P0 - Necessary)

**Priority:** P0 - Necessary but not sufficient
**Risk:** Low
**Effort:** Minimal (2 lines)

**File:** `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs`

**Change:** Add UI notification after updating Region IDs in `OnEntityCreated`.

```csharp
private void OnEntityCreated(object? sender, EntityCreatedEventArgs e)
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        var loadedTrip = _callbacks?.LoadedTrip;
        if (loadedTrip == null)
        {
            _logger.LogDebug("EntityCreated: No loaded trip, skipping ID reconciliation");
            return;
        }

        switch (e.EntityType)
        {
            case "Region":
                var region = loadedTrip.Regions.FirstOrDefault(r => r.Id == e.TempClientId);
                if (region != null)
                {
                    region.Id = e.ServerId;
                    _logger.LogDebug("EntityCreated: Updated Region TempId {TempId} -> ServerId {ServerId}",
                        e.TempClientId, e.ServerId);

+                   // Refresh SortedRegions so UI-bound Region copies have the updated server ID
+                   _callbacks?.NotifyTripRegionsChanged();
                }
                else
                {
                    _logger.LogDebug("EntityCreated: Region with TempId {TempId} not found in loaded trip",
                        e.TempClientId);
                }
                break;

            case "Place":
                foreach (var reg in loadedTrip.Regions)
                {
                    var place = reg.Places.FirstOrDefault(p => p.Id == e.TempClientId);
                    if (place != null)
                    {
                        place.Id = e.ServerId;
                        _logger.LogDebug("EntityCreated: Updated Place TempId {TempId} -> ServerId {ServerId}",
                            e.TempClientId, e.ServerId);
-                       // No notification needed: SortedRegions reuses TripPlace objects (doesn't clone)
-                       // so the ID update propagates automatically to UI-bound references
+                       // Note: No NotifyTripPlacesChanged() needed here because SortedRegions
+                       // reuses TripPlace objects (TripModels.cs:269) rather than cloning them.
+                       // The ID update propagates automatically to all references.
                        break;
                    }
                }
                break;

            default:
                _logger.LogDebug("EntityCreated: Unhandled entity type {EntityType}", e.EntityType);
                break;
        }
    });
}
```

**Why Regions need notification but Places don't:**
- `SortedRegions` creates **new TripRegion objects** (TripModels.cs:259: `new TripRegion { ... }`)
- But it **reuses TripPlace objects** (TripModels.cs:269: `Places = r.Places.OrderBy(...).ToList()`)
- Region ID updates need UI refresh; Place ID updates propagate automatically

**Rationale:**
- Ensures Region UI copies are refreshed after EntityCreated reconciles
- Required for queued mutation replays that fire EntityCreated

**Limitation:**
- Does NOT fix the observed bug alone (EntityCreated may not have been applied before the edit)

---

### Fix 2: Immediate Fallback Reconciliation (P1 - Required)

**Priority:** P1 - Required (this fixes the observed bug)
**Risk:** Low
**Effort:** Small (10 lines)

**File:** `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs`

#### Region: AddRegionAsync (lines 1156-1230)

**Change:** Capture tempId before API call, reconcile after, remove by tempId in finally.

```csharp
private async Task AddRegionAsync()
{
    try
    {
        // ... existing code to create newRegion and add to Regions ...

        _logger.LogInformation("AddRegion: Syncing to server");
+       var tempId = newRegion.Id;  // Capture before potential reconciliation
-       _inFlightRegionCreates.Add(newRegion.Id);
+       _inFlightRegionCreates.Add(tempId);
        try
        {
-           await _tripSyncService.CreateRegionAsync(
+           var serverId = await _tripSyncService.CreateRegionAsync(
                loadedTrip.Id,
                name,
                displayOrder: newRegion.SortOrder,
                 clientTempId: tempId);
+
+           // Immediate reconciliation - deterministic, doesn't rely on EntityCreated
+           if (serverId != Guid.Empty && serverId != newRegion.Id)
+           {
+               _logger.LogInformation("AddRegion: Reconciling tempId {TempId} -> serverId {ServerId}",
+                   tempId, serverId);
+               newRegion.Id = serverId;
+               _callbacks?.NotifyTripRegionsChanged();
+           }
        }
        finally
        {
-           _inFlightRegionCreates.Remove(newRegion.Id);
+           _inFlightRegionCreates.Remove(tempId);  // Always remove by original tempId
        }

        _logger.LogInformation("AddRegion: Completed successfully for region {RegionId}", newRegion.Id);
        await _toastService.ShowSuccessAsync("Region added");
    }
    // ... catch blocks ...
}
```

#### Place: SavePlaceCoordinatesAsync (lines 381-452)

**Note:** Place creation happens in `SavePlaceCoordinatesAsync`, NOT in `AddPlaceToCurrentLocationAsync`. The latter only sets up pending coordinates; the actual `CreatePlaceAsync` call is in `SavePlaceCoordinatesAsync`.

**No changes needed for Places:** `SavePlaceCoordinatesAsync` already performs immediate reconciliation after `CreatePlaceAsync` returns. Additionally, since `SortedRegions` reuses TripPlace objects (doesn't clone them), Place ID updates propagate automatically without needing `NotifyTripPlacesChanged()`.

#### Mandatory Guard: Edit During In-Flight Create

To be deterministic, edits and destructive operations must be blocked while a Region CREATE is in-flight. Add `_inFlightRegionCreates.Contains(region.Id)` checks with a toast ("Region is still saving - try again in a moment") in:
- `EditRegionNameAsync`
- `EditRegionNotesAsync`
- `DeleteRegionAsync`
- `MoveRegionUpAsync`
- `MoveRegionDownAsync`

This closes the temp-ID update window entirely.

For `MoveRegionUpAsync` and `MoveRegionDownAsync`, check both the target region and the adjacent region involved in the swap; block if either is in-flight.

**Rationale:**
- Provides deterministic reconciliation that doesn't rely on EntityCreated event
- Synchronous reconciliation in the same execution context
- **This is the fix for the observed bug**

**Trade-off:**
- Duplicates reconciliation logic (now in both Add methods and OnEntityCreated)
- OnEntityCreated still needed for queued mutation replays

---

### Fix 3: Pending-Create Guard in Operation Handlers (P2 - Required)

**Priority:** P2 - Required (for offline create -> edit determinism)
**Risk:** Medium
**Effort:** Medium

**Files:**
- `src/WayfarerMobile/Services/RegionOperationsHandler.cs`
- `src/WayfarerMobile/Services/PlaceOperationsHandler.cs`

**Change:** In `UpdateRegionAsync`, detect pending CREATE and merge update into it instead of calling API.

**Implementation Note:** Reuse existing merge logic in `EnqueueRegionMutationAsync` (lines 353-403) and `EnqueueRegionMutationWithOriginalAsync` (lines 405-471). These methods already handle field merging when a pending mutation exists for the same entity. Do NOT reimplement merge logic - call the existing helpers to avoid drift if new fields are added later.

```csharp
// RegionOperationsHandler.cs - UpdateRegionAsync
public async Task<RegionOperationResult> UpdateRegionAsync(
    Guid regionId,
    Guid tripId,
    string? name = null,
    // ... other params ...
)
{
    await EnsureInitializedAsync();

+   // Check if this entity has a pending CREATE (offline-created, not yet synced)
+   var pendingCreate = await _database!.Table<PendingTripMutation>()
+       .Where(m => m.EntityId == regionId && m.EntityType == "Region" && m.OperationType == "Create" && !m.IsRejected)
+       .FirstOrDefaultAsync();
+
+   if (pendingCreate != null)
+   {
+       // Reuse existing merge logic - EnqueueRegionMutationAsync handles merging
+       // into existing non-Delete mutations (including CREATE)
+       await EnqueueRegionMutationAsync("Update", regionId, tripId, name, notes, coverImageUrl,
+           centerLatitude, centerLongitude, displayOrder, includeNotes, null);
+
+       // Also update offline entry for immediate UI consistency
+       var offlineArea = await _areaRepository.GetOfflineAreaByServerIdAsync(regionId);
+       if (offlineArea != null)
+       {
+           if (name != null) offlineArea.Name = name;
+           if (includeNotes) offlineArea.Notes = notes;
+           if (displayOrder.HasValue) offlineArea.SortOrder = displayOrder.Value;
+           if (centerLatitude.HasValue) offlineArea.CenterLatitude = centerLatitude;
+           if (centerLongitude.HasValue) offlineArea.CenterLongitude = centerLongitude;
+           await _areaRepository.UpdateOfflineAreaAsync(offlineArea);
+       }
+
+       return RegionOperationResult.Queued(regionId, "Merged into pending create - will sync when online");
+   }

    // ... existing UpdateRegionAsync logic ...
}
```

**Apply same pattern to:**
- `PlaceOperationsHandler.UpdatePlaceAsync`:
  - Check for pending CREATE with same `EntityId`
  - If found, call `EnqueuePlaceMutationAsync` to merge update into pending CREATE
  - **Important:** Also update offline place entry for UI consistency (same as Region pattern above)
  - Return `PlaceOperationResult.Queued()`
- Consider for `DeleteRegionAsync` / `DeletePlaceAsync` (cancel pending CREATE - already implemented in D4)

**Rationale:**
- Prevents API calls with temp IDs entirely for offline scenario
- Correct behavior for offline create-then-edit
- Reuses existing merge logic to avoid duplication and drift

**Trade-off:**
- More complex logic in handlers
- Requires thorough testing of merge scenarios
- D4 (delete) already handles pending CREATE cancellation, so pattern exists

---

### Fix 4: Handle Unsuccessful Update Responses (Required for determinism)

**Issue:** `UpdateRegionAsync` and `UpdatePlaceAsync` currently treat any non-null API response as success, even when `response.Success == false` (e.g., 404 NotFound wrapped in a response object). Queued mutation replay paths do the same.

**Code refs:**
- `src/WayfarerMobile/Services/RegionOperationsHandler.cs:203-209`: `if (response != null) return Completed`
- `src/WayfarerMobile/Services/PlaceOperationsHandler.cs:226-244`: Similar pattern
- `src/WayfarerMobile/Services/TripSyncService.cs:629-636`: `ProcessRegionUpdateAsync` returns `response != null`
- `src/WayfarerMobile/Services/TripSyncService.cs:524-529`: `ProcessPlaceUpdateAsync` returns `response != null`

**Impact:** Failed updates appear to succeed from the caller's perspective. The failure is only visible in logs. Offline tables can also remain in the optimistic state after a 4xx rejection.

**Required:** Check `response.Success` and treat unsuccessful responses as Queued or Rejected in both:
- Online update path (`RegionOperationsHandler.UpdateRegionAsync` / `PlaceOperationsHandler.UpdatePlaceAsync`)
- Queued mutation replay path (`TripSyncService.ProcessRegionUpdateAsync` / `ProcessPlaceUpdateAsync`)

**Deterministic rule:** If `response.Error` indicates `HTTP 4xx` (except 429), reject and restore originals. Otherwise queue for retry.

```csharp
if (response?.Success == true)
    return RegionOperationResult.Completed(regionId);

if (TryGetHttpStatus(response?.Error, out var statusCode) && IsClientError(statusCode))
{
    // 4xx rejection: restore offline values and reject
    await RestoreOriginalOfflineValuesAsync(...);
    return RegionOperationResult.Rejected($"Server rejected: {response?.Error}");
}

// Response was null or non-4xx failure - queue for retry
await EnqueueRegionMutationWithOriginalAsync(...);
return RegionOperationResult.Queued(regionId, "Update failed - will retry");
```

**Queued mutation replay path (deterministic rejection):**
If `response.Success == false` and `Error` is `HTTP 4xx`, throw a `HttpRequestException` with the parsed status code so `ProcessPendingMutationsAsync` can:
- restore original values (`_mutationQueue.RestoreOriginalValuesAsync`)
- mark mutation rejected (`_mutationQueue.MarkMutationRejectedAsync`)
- raise `SyncRejected`

This prevents endless retries on 4xx and keeps offline data consistent.

**Helper guidance:** parse `response.Error` using the ApiClient format (`"HTTP {statusCode}"`). Treat 429 as retryable (same as existing `IsClientError`).

---

## Implementation Plan

### Phase 1 + 2: Required (Single PR)

Both fixes are required to address the observed bug.

- [ ] Implement Fix 1 (UI notification in EntityCreated for Regions only)
- [ ] Implement Fix 2 (immediate fallback reconciliation)
  - [ ] Region: Update `AddRegionAsync` with tempId capture and reconciliation
  - [ ] Place: No changes needed (`SavePlaceCoordinatesAsync` already handles reconciliation)
- [ ] Add in-flight edit guard checks (mandatory)
- [ ] Manual test: Create Region -> Edit immediately -> Verify server updated
- [ ] Commit with message: `fix: add deterministic ID reconciliation and in-flight edit guard`

### Phase 3: Offline Determinism (Required)
- [ ] Implement Fix 3 (pending-create guard using existing merge logic)
- [ ] Manual test: Airplane mode -> Create Region -> Edit -> Go online -> Verify sync
- [ ] Commit with message: `fix: merge updates into pending CREATE mutations`

### Phase 4: Update-Response Determinism (Required)
- [ ] Implement Fix 4 (treat response.Success=false as failure; 4xx -> rejected, otherwise queued)
- [ ] Online update path: restore offline values when response.Error is HTTP 4xx
- [ ] Queue replay path: throw HttpRequestException with parsed 4xx StatusCode to trigger rejection handling
- [ ] Manual test: Force server rejection -> verify update is queued/rejected and surfaced
- [ ] Commit with message: `fix: handle unsuccessful update responses`

---

## Testing Checklist

### Phase 1 + 2 Test Cases
- [ ] Online: Create region -> Edit name immediately -> Verify server has new name
- [ ] Online: Start create, attempt edit before create completes -> toast shown, no update call
- [ ] Online: Create region -> Wait 30s -> Edit name -> Verify server has new name
- [ ] Online: Create place -> Edit name immediately -> Verify server has new name
- [ ] Online: Create place -> Wait 30s -> Edit name -> Verify server has new name

### Phase 3 Test Cases
- [ ] Offline: Create region -> Edit name -> Go online -> Verify server has final name
- [ ] Offline: Create region -> Delete -> Go online -> Verify no orphan on server
- [ ] Offline: Create place in new region -> Edit place -> Go online -> Verify both synced

### Phase 4 Test Cases
- [ ] Online: Server returns Success=false for update -> mutation is queued/rejected and user sees failure
- [ ] Queued update replay: Server returns HTTP 4xx -> mutation marked rejected (not retried) and originals restored

---

## Risk Assessment

| Fix | Risk Level | Regression Potential | Rollback Difficulty |
|-----|------------|---------------------|---------------------|
| Fix 1 | Low | Minimal - adds notification | Easy - remove 2 lines |
| Fix 2 | Low | Low - additive code | Easy - remove fallback block |
| Fix 3 | Medium | Medium - changes mutation flow | Moderate - revert handler logic |
| Fix 4 | Low | Low - stricter success handling | Easy - revert conditional |

---

## Decision Required

Please review and indicate approval for:

- [ ] **All phases (1-4)** (Deterministic behavior across online/offline and in-flight edits)

---

## Appendix: File Locations

| Component | File Path |
|-----------|-----------|
| EntityCreated handler | `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs:922-971` |
| EditRegionNameAsync | `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs:1093-1132` |
| AddRegionAsync | `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs:1156-1230` |
| SavePlaceCoordinatesAsync | `src/WayfarerMobile/ViewModels/TripItemEditorViewModel.cs:381-452` |
| SortedRegions (Region clone, Place reuse) | `src/WayfarerMobile.Core/Models/TripModels.cs:256-272` |
| EnqueueRegionMutationAsync | `src/WayfarerMobile/Services/RegionOperationsHandler.cs:353-403` |
| EnqueueRegionMutationWithOriginalAsync | `src/WayfarerMobile/Services/RegionOperationsHandler.cs:405-471` |
| RegionOperationsHandler | `src/WayfarerMobile/Services/RegionOperationsHandler.cs` |
| PlaceOperationsHandler | `src/WayfarerMobile/Services/PlaceOperationsHandler.cs` |

---

## Revision History

| Date | Change |
|------|--------|
| 2026-01-13 | Initial proposal |
| 2026-01-13 | Rev 1: Address peer review findings |
| | - Clarified diagnostic ambiguity; Phase 1 alone may be insufficient |
| | - Fix 2: Corrected Place location from `AddPlaceToCurrentLocationAsync` to `SavePlaceCoordinatesAsync` |
| | - Fix 2: Added tempId capture before reconciliation to fix in-flight marker leak |
| | - Fix 3: Changed to reuse existing `EnqueueRegionMutationAsync` merge logic |
| | - Updated recommendation: Phase 1+2 as required minimum |
| 2026-01-13 | Rev 2: Correct root cause analysis per peer review |
| | - **High**: Reframed root cause - proof that EntityCreated did NOT reconcile (update was called with temp ID) |
| | - Added "Critical Insight" section with logical proof from EditRegionNameAsync flow |
| | - Fix 1 now marked "Necessary but not sufficient" |
| | - Fix 2 now marked "Required (this fixes the observed bug)" |
| | - Added EditRegionNameAsync to file locations appendix |
| 2026-01-13 | Rev 3: Address Place notification and decision options |
| | - Removed NotifyTripPlacesChanged from Fix 1 - not needed because SortedRegions reuses TripPlace objects |
| | - Added explanation of why Regions need notification but Places don't (clone vs reuse) |
| | - Removed "Phase 1 only" option entirely (was misleading given log evidence) |
| | - Updated Fix 1 effort from 4 lines to 2 lines |
| | - Added SortedRegions to file locations with note about clone/reuse behavior |
| 2026-01-13 | Rev 4: Final peer review fixes |
| | - Fix 2: Replaced "Review required" with definitive "No changes needed for Places" |
| | - Fix 3: Added explicit steps for PlaceOperationsHandler including offline entry update |
| 2026-01-13 | Rev 5: Precision and scope clarifications |
| | - **High**: Changed "did not reconcile at all" to "not applied before edit" (more precise) |
| | - **Medium**: Added "Accepted Edge Case: Edit During In-Flight Create" section |
| | - **Medium**: Added "Follow-Up: Silent Failure on Unsuccessful Update Responses" section |
| | - Clarified that EntityCreated may have fired but was queued/too late |
| 2026-01-13 | Rev 6: Final consistency fixes |
| | - Updated "Why Two Fixes Are Required" table: "isn't reconciling" -> "may be late or absent" |
| | - Implementation plan: Changed Place verification step to "No changes needed" |
| 2026-01-13 | Rev 7: Honest coverage claims |
| | - **High**: Expanded coverage table to show what each phase covers and gaps |
| | - **High**: Removed "complete coverage = YES" claim for Phase 1+2; now "covers observed failure" |
| | - **Medium**: Fix 1 limitation now says "may not have been applied" not "isn't reconciling" |
| | - Clarified in-flight edit is "accepted gap" not "deterministic" |
| | - Decision options now explicitly list accepted gaps |
| 2026-01-13 | Rev 8: Deterministic requirements |
| | - Made in-flight edit guard mandatory |
| | - Marked Fix 3 and Fix 4 as required for determinism |
| | - Updated implementation plan, tests, risk table, and decision to require phases 1-4 |
| 2026-01-13 | Rev 9: Deterministic update-response rule |
| | - Added explicit 4xx -> rejected, otherwise queued guidance for UpdateResponse handling |
| 2026-01-13 | Rev 10: In-flight guard precision |
| | - Clarified MoveRegionUp/Down must block if either region in the swap is in-flight |
| 2026-01-13 | Rev 11: Deterministic update-response handling |
| | - Required offline rollback for HTTP 4xx responses without exceptions |
| | - Required queue replay path to reject 4xx via HttpRequestException |

