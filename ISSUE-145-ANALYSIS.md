# Issue #145 (Umbrella) Investigation Findings

**Date:** 2026-01-11
**Related Issues:** #118 (Add Region UI not updating), #119 (Add Place logic wrong)
**Evidence:** `wayfarer-app-20260111.log`

---

## Executive Summary

CREATE operations for Places and Regions successfully sync to the server but fail to:
1. Persist to local offline tables
2. Update in-memory collections with server-assigned IDs

This causes all subsequent UPDATE/DELETE operations to fail with "NotFound" errors because the client continues using temporary client-side IDs instead of server-assigned IDs.

---

## Log Evidence (Smoking Gun)

```
11:03:05.511 - AddRegion: Added region "1cc4d527-3af1-40b0-8010-bdac9d14c0f4" to memory
11:03:05.955 - AddRegion: Syncing to server
11:03:06.188 - AddRegion: Completed successfully
11:03:21.246 - Failed to update region "1cc4d527-3af1-40b0-8010-bdac9d14c0f4": "NotFound"
11:05:31.244 - Failed to create place: "BadRequest"
11:07:29.712 - Failed to delete region "1cc4d527-3af1-40b0-8010-bdac9d14c0f4": "NotFound"
```

**Timeline:**
- Region created with TempId (client-generated GUID)
- Server accepts creation and assigns ServerId
- 15 seconds later, update fails because client still uses TempId
- Place creation fails (RegionId is TempId, server doesn't recognize it)
- Delete fails for same TempId reason

---

## Findings

### F1: CREATE Operations Don't Persist to Offline Tables

**Location:** `TripSyncService.cs` - `CreateRegionAsync()` (lines 485-553) and `CreatePlaceAsync()` (lines 82-155)

**Current Flow:**
```
1. Generate tempClientId = Guid.NewGuid()
2. Call API -> Server returns response.Id (ServerId)
3. Fire EntityCreated event with (TempClientId, ServerId)
4. Return ServerId to caller
```

**Missing Step:** No `OfflineAreaEntity` or `OfflinePlaceEntity` is inserted into SQLite.

**Impact on Updates:**
```csharp
// In UpdateRegionAsync (line 576):
var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(regionId);
// Returns NULL because no offline entry exists
// Optimistic update infrastructure fails silently
```

**Files:** `src/WayfarerMobile/Services/TripSyncService.cs`

### F2: EntityCreated Event Has No Subscribers (Temp IDs Never Reconciled)

**Evidence:** `grep "EntityCreated +=" src/` returns no matches.

**Current Flow:**
```
1. MainViewModel creates TripPlace/TripRegion with temp ID
2. Adds to LoadedTrip.Regions collection
3. Calls CreatePlaceAsync/CreateRegionAsync
4. IGNORES the returned ServerId
5. Does NOT subscribe to EntityCreated event
6. In-memory collection retains TempId forever
7. All subsequent API calls use TempId -> Server returns 404
```

**Files:** `src/WayfarerMobile/ViewModels/MainViewModel.cs`, `src/WayfarerMobile/Services/TripSyncService.cs`

### F3: Queue Sync Does Not Reconcile Offline Data or Pending Mutations

**Evidence:** `ProcessPlaceCreateAsync` / `ProcessRegionCreateAsync` emit `EntityCreated` but do not:
- Update offline entries from temp ID to server ID
- Rewrite queued UPDATE/DELETE mutations targeting the temp ID
- Rewrite RegionId references in pending Place mutations when Region TempId resolves

**Impact:** Even when CREATE eventually syncs, pending updates still target temp IDs.

**Files:** `src/WayfarerMobile/Services/TripSyncService.cs`

### F4: CreatePlace Passes TempId as RegionId

**Location:** `MainViewModel.AddPlaceToCurrentLocationAsync()` (lines 2900-2909)

```csharp
await _tripSyncService.CreatePlaceAsync(
    LoadedTrip.Id,
    selectedRegion.Id,  // THIS IS THE TEMP ID if region was just created!
    placeName.Trim(),
    CurrentLocation.Latitude,
    CurrentLocation.Longitude,
    ...
);
```

If user creates a Region then immediately creates a Place in that Region, the RegionId sent to server is the TempId, which doesn't exist on the server -> BadRequest.

**Additional Scenario: Online But Pending Region**

Even when the device is online, if Region CREATE failed (network error) and is queued:
1. Region CREATE is pending in mutation queue (with TempId)
2. Device comes back online (or was online but had transient error)
3. User creates Place in that Region
4. `CreatePlaceAsync` sees device is online, calls API directly
5. Server receives RegionId = TempId -> 400 Bad Request
6. Current code treats 400 as `ServerRejection` -> Place marked as rejected
7. **BUG:** Place is valid, just waiting for Region to sync first

**Impact:** Valid Places are permanently rejected instead of being queued behind their parent Region.

### F5: Race Condition - Image Loads Before View Fully Attached

**Evidence:**
- `wayfarer-app-20260111.log` shows: `Unable to load image stream. You cannot start a load for a destroyed activity`
- Device testing confirms: My Trips -> Load (without delay) -> crash
- Same sequence with short delay before Load -> no crash

**Reproduction:**
1. Open app (Main page)
2. Menu -> Trips (My Trips tab)
3. Immediately tap "Load" on a trip (no wait)
4. Crash occurs when trip cover image attempts to load

**Root Cause:** Navigation to MainPage triggers trip load, which binds `CoverImageUrl` to an image control. The image loader (Glide/native) starts fetching before the MainPage's Activity is fully attached. Android image loaders require a valid, attached Activity context.

**Impact:** App crash during rapid navigation sequences.

**Files:** MainPage.xaml (image binding), MainViewModel.cs (trip loading), possibly custom image converters or behaviors

### F6: Syncfusion Effects View Reparent Warning (Non-Fatal)

**Evidence:** Repeated warnings that an `SfEffectsView` is already a child of a `Grid` when added to `SfIconButton`.

**Impact:** UI warning; not directly tied to #145/#118/#119.

### F7: TempId Mismatch Between ViewModel and Service

**Evidence:** Code inspection shows two separate GUID generations:
- `MainViewModel.cs:2814` - `var tempId = Guid.NewGuid();` (UI's temp ID)
- `TripSyncService.cs:496` - `var tempClientId = Guid.NewGuid();` (service's temp ID)

**Impact:** The `EntityCreated` event fires with the service's `tempClientId`, but the UI object has a different `tempId`. D2's handler looking for `r.Id == e.TempClientId` will NEVER find a match.

**Files:** `src/WayfarerMobile/ViewModels/MainViewModel.cs`, `src/WayfarerMobile/Services/TripSyncService.cs`

---

## Deterministic Solutions

### D0: Align TempId Between ViewModel and Service (PREREQUISITE)

**Files:** `TripSyncService.cs`, `MainViewModel.cs`

**Problem:** The ViewModel generates a tempId for the in-memory object, but the service generates its own tempClientId. These never match, so EntityCreated handlers cannot find the object.

**Solution:** Modify service methods to accept an optional `clientTempId` parameter. If provided, use it; otherwise generate one.

**For CreateRegionAsync signature:**
```csharp
public async Task<Guid> CreateRegionAsync(
    Guid tripId,
    string name,
    string? notes = null,
    string? coverImageUrl = null,
    double? centerLatitude = null,
    double? centerLongitude = null,
    int? displayOrder = null,
    Guid? clientTempId = null)  // NEW: Accept caller's temp ID
{
    await EnsureInitializedAsync();

    // Use caller's temp ID if provided, otherwise generate one
    var tempClientId = clientTempId ?? Guid.NewGuid();
    // ... rest of method unchanged
}
```

**For CreatePlaceAsync signature:**
```csharp
public async Task<Guid> CreatePlaceAsync(
    Guid tripId,
    Guid? regionId,
    string name,
    double latitude,
    double longitude,
    string? notes = null,
    string? iconName = null,
    string? markerColor = null,
    int? displayOrder = null,
    Guid? clientTempId = null)  // NEW: Accept caller's temp ID
{
    await EnsureInitializedAsync();

    // Use caller's temp ID if provided, otherwise generate one
    var tempClientId = clientTempId ?? Guid.NewGuid();
    // ... rest of method unchanged
}
```

**Update callers in MainViewModel:**
```csharp
// In AddRegionAsync:
var tempId = Guid.NewGuid();
var newRegion = new TripRegion { Id = tempId, ... };
LoadedTrip.Regions.Add(newRegion);
await _tripSyncService.CreateRegionAsync(
    LoadedTrip.Id, name.Trim(), null, null, null, null, newRegion.SortOrder,
    clientTempId: tempId);  // Pass the same temp ID

// In AddPlaceToCurrentLocationAsync:
var tempId = Guid.NewGuid();
var newPlace = new TripPlace { Id = tempId, ... };
selectedRegion.Places.Add(newPlace);
await _tripSyncService.CreatePlaceAsync(
    LoadedTrip.Id, selectedRegion.Id, placeName.Trim(), ...,
    clientTempId: tempId);  // Pass the same temp ID
```

### D1: Persist Offline Entry on CREATE (All Paths)

**File:** `TripSyncService.cs`

**Key Principle:** Use UPSERT pattern - if offline entry with TempId already exists (from offline CREATE path), UPDATE its ServerId; otherwise INSERT new entry.

**Prerequisite:** D0 must be implemented so that the service uses the caller's tempId.

**For CreateRegionAsync (after successful API response, ~line 523):**

```csharp
if (response?.Success == true && response.Id != Guid.Empty)
{
    var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
    if (downloadedTrip != null)
    {
        // Check if offline entry exists with TempId (from offline CREATE path)
        var existingArea = await _databaseService.GetOfflineAreaByServerIdAsync(tempClientId);
        if (existingArea != null)
        {
            // UPDATE: Offline entry was created during offline CREATE - update its ServerId
            existingArea.ServerId = response.Id;
            await _databaseService.UpdateOfflineAreaAsync(existingArea);
        }
        else
        {
            // INSERT: No offline entry exists - create new one with ServerId
            var offlineArea = new OfflineAreaEntity
            {
                TripId = downloadedTrip.Id,
                ServerId = response.Id,
                Name = name,
                Notes = notes,
                CenterLatitude = centerLatitude,
                CenterLongitude = centerLongitude,
                SortOrder = displayOrder ?? 0
            };
            await _databaseService.InsertOfflineAreaAsync(offlineArea);
        }
    }

    EntityCreated?.Invoke(this, new EntityCreatedEventArgs
    {
        TempClientId = tempClientId,
        ServerId = response.Id,
        EntityType = "Region"
    });
    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = response.Id });
    return response.Id;
}
```

**For CreatePlaceAsync (after successful API response, ~line 123):**

```csharp
if (response?.Success == true && response.Id != Guid.Empty)
{
    var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
    if (downloadedTrip != null)
    {
        // Check if offline entry exists with TempId (from offline CREATE path)
        var existingPlace = await _databaseService.GetOfflinePlaceByServerIdAsync(tempClientId);
        if (existingPlace != null)
        {
            // UPDATE: Offline entry was created during offline CREATE - update its ServerId
            existingPlace.ServerId = response.Id;
            await _databaseService.UpdateOfflinePlaceAsync(existingPlace);
        }
        else
        {
            // INSERT: No offline entry exists - create new one with ServerId
            var offlinePlace = new OfflinePlaceEntity
            {
                TripId = downloadedTrip.Id,
                ServerId = response.Id,
                RegionId = regionId,
                Name = name,
                Latitude = latitude,
                Longitude = longitude,
                Notes = notes,
                IconName = iconName,
                MarkerColor = markerColor,
                SortOrder = displayOrder ?? 0
            };
            await _databaseService.InsertOfflinePlaceAsync(offlinePlace);
        }
    }

    EntityCreated?.Invoke(this, new EntityCreatedEventArgs
    {
        TempClientId = tempClientId,
        ServerId = response.Id,
        EntityType = "Place"
    });
    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = response.Id });
    return response.Id;
}
```

**For Offline CREATE Path (when not connected):**

```csharp
// In offline CREATE path, UPSERT offline entry with TempId as placeholder ServerId
var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
if (downloadedTrip != null)
{
    // Check if entry already exists (retry/restart scenario)
    var existingArea = await _databaseService.GetOfflineAreaByServerIdAsync(tempClientId);
    if (existingArea == null)
    {
        var offlineArea = new OfflineAreaEntity
        {
            TripId = downloadedTrip.Id,
            ServerId = tempClientId,  // Use TempId as placeholder until sync
            Name = name,
            Notes = notes,
            CenterLatitude = centerLatitude,
            CenterLongitude = centerLongitude,
            SortOrder = displayOrder ?? 0
        };
        await _databaseService.InsertOfflineAreaAsync(offlineArea);
    }
    // If exists, no action needed - entry already present from previous attempt
}

await EnqueueRegionMutationAsync(...);
return tempClientId;
```

**For Places (offline CREATE path):**

```csharp
// In offline CREATE path for Places, UPSERT to avoid duplicates
var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
if (downloadedTrip != null)
{
    // Check if entry already exists (retry/restart scenario)
    var existingPlace = await _databaseService.GetOfflinePlaceByServerIdAsync(tempClientId);
    if (existingPlace == null)
    {
        var offlinePlace = new OfflinePlaceEntity
        {
            TripId = downloadedTrip.Id,
            ServerId = tempClientId,  // Use TempId as placeholder until sync
            RegionId = regionId,
            Name = name,
            Latitude = latitude,
            Longitude = longitude,
            Notes = notes,
            IconName = iconName,
            MarkerColor = markerColor,
            SortOrder = displayOrder ?? 0
        };
        await _databaseService.InsertOfflinePlaceAsync(offlinePlace);
    }
    // If exists, no action needed - entry already present from previous attempt
}

await EnqueuePlaceMutationAsync(...);
return tempClientId;
```

### D2: Subscribe to EntityCreated and Update In-Memory Collections

**File:** `MainViewModel.cs`

**Prerequisite:** D0 must be implemented so that the service's tempClientId matches the UI's tempId.

**In Constructor:**
```csharp
_tripSyncService.EntityCreated += OnEntityCreated;
```

**Add Dispose/Cleanup (in `Dispose()` method or `IDisposable` implementation):**
```csharp
// In MainViewModel.Dispose() or cleanup method called when ViewModel is torn down
public void Dispose()
{
    _tripSyncService.EntityCreated -= OnEntityCreated;
    // ... other cleanup
}
```

**Event Handler:**
```csharp
private void OnEntityCreated(object? sender, EntityCreatedEventArgs e)
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        if (LoadedTrip == null) return;

        switch (e.EntityType)
        {
            case "Region":
                var region = LoadedTrip.Regions.FirstOrDefault(r => r.Id == e.TempClientId);
                if (region != null)
                {
                    region.Id = e.ServerId;
                    _logger.LogDebug("Updated Region TempId {TempId} -> ServerId {ServerId}",
                        e.TempClientId, e.ServerId);
                }
                break;

            case "Place":
                foreach (var reg in LoadedTrip.Regions)
                {
                    var place = reg.Places.FirstOrDefault(p => p.Id == e.TempClientId);
                    if (place != null)
                    {
                        place.Id = e.ServerId;
                        _logger.LogDebug("Updated Place TempId {TempId} -> ServerId {ServerId}",
                            e.TempClientId, e.ServerId);
                        break;
                    }
                }
                break;
        }
    });
}
```

### D3: Reconcile Pending Mutations and Offline Data After CREATE Sync

**File:** `TripSyncService.cs` - In `ProcessPlaceCreateAsync` and `ProcessRegionCreateAsync`

After successful create, rewrite:
1. Pending mutations targeting the TempId (UPDATE/DELETE for this entity)
2. RegionId references in pending Place mutations (for Region CREATE)
3. RegionId in offline Place rows (for Region CREATE)

**For ProcessRegionCreateAsync:**

```csharp
// After EntityCreated is fired:
var tempId = mutation.TempClientId ?? mutation.EntityId;

// 1. Rewrite pending mutations for this Region from TempId to ServerId
var pendingMutations = await _database!.Table<PendingTripMutation>()
    .Where(m => m.EntityId == tempId && m.EntityType == "Region")
    .ToListAsync();

foreach (var pending in pendingMutations)
{
    pending.EntityId = response.Id;
    await _database.UpdateAsync(pending);
}

// 2. Rewrite RegionId in pending Place mutations that reference this Region's TempId
var placeMutationsWithRegion = await _database.Table<PendingTripMutation>()
    .Where(m => m.RegionId == tempId && m.EntityType == "Place")
    .ToListAsync();

foreach (var placeMutation in placeMutationsWithRegion)
{
    placeMutation.RegionId = response.Id;
    await _database.UpdateAsync(placeMutation);
}

// 3. Rewrite RegionId in offline Place rows that reference this Region's TempId
var offlinePlaces = await _databaseService.GetOfflinePlacesByRegionIdAsync(tempId);
foreach (var offlinePlace in offlinePlaces)
{
    offlinePlace.RegionId = response.Id;
    await _databaseService.UpdateOfflinePlaceAsync(offlinePlace);
}

// 4. Update the offline area entry from TempId to ServerId
var offlineArea = await _databaseService.GetOfflineAreaByServerIdAsync(tempId);
if (offlineArea != null)
{
    offlineArea.ServerId = response.Id;
    await _databaseService.UpdateOfflineAreaAsync(offlineArea);
}
```

**For ProcessPlaceCreateAsync:**

```csharp
// After EntityCreated is fired:
var tempId = mutation.TempClientId ?? mutation.EntityId;

// 1. Rewrite pending mutations for this Place from TempId to ServerId
var pendingMutations = await _database!.Table<PendingTripMutation>()
    .Where(m => m.EntityId == tempId && m.EntityType == "Place")
    .ToListAsync();

foreach (var pending in pendingMutations)
{
    pending.EntityId = response.Id;
    await _database.UpdateAsync(pending);
}

// 2. Update the offline place entry from TempId to ServerId
var offlinePlace = await _databaseService.GetOfflinePlaceByServerIdAsync(tempId);
if (offlinePlace != null)
{
    offlinePlace.ServerId = response.Id;
    await _databaseService.UpdateOfflinePlaceAsync(offlinePlace);
}
```

### D4: Offline Delete of Unsynced Items Should Cancel Queued CREATE

If DELETE is issued for a temp ID with a pending CREATE:
- Cancel both mutations (no server call needed)
- Remove the offline entry if it exists
- For Regions: also clean up any Place mutations/entries that reference the Region's TempId

**For Regions (in DeleteRegionAsync):**

```csharp
// Before making API call:
var pendingCreate = await _database!.Table<PendingTripMutation>()
    .Where(m => m.EntityId == regionId && m.EntityType == "Region" && m.OperationType == "Create")
    .FirstOrDefaultAsync();

if (pendingCreate != null)
{
    // Entity was never synced - just remove from queue and offline table
    await _database.DeleteAsync(pendingCreate);
    await _databaseService.DeleteOfflineAreaByServerIdAsync(regionId);

    // Also delete any pending mutations for this region
    var relatedMutations = await _database.Table<PendingTripMutation>()
        .Where(m => m.EntityId == regionId && m.EntityType == "Region")
        .ToListAsync();
    foreach (var m in relatedMutations)
        await _database.DeleteAsync(m);

    // Get orphaned offline Place rows that reference this Region's TempId
    var orphanedPlaces = await _databaseService.GetOfflinePlacesByRegionIdAsync(regionId);

    // Clean up Place CREATE mutations that reference this Region's TempId (via RegionId)
    var orphanedPlaceCreates = await _database.Table<PendingTripMutation>()
        .Where(m => m.RegionId == regionId && m.EntityType == "Place")
        .ToListAsync();
    foreach (var m in orphanedPlaceCreates)
        await _database.DeleteAsync(m);

    // Clean up Place UPDATE/DELETE mutations by EntityId (they don't carry RegionId)
    foreach (var place in orphanedPlaces)
    {
        var placeMutations = await _database.Table<PendingTripMutation>()
            .Where(m => m.EntityId == place.ServerId && m.EntityType == "Place")
            .ToListAsync();
        foreach (var m in placeMutations)
            await _database.DeleteAsync(m);
    }

    // Delete the orphaned offline Place rows
    foreach (var place in orphanedPlaces)
        await _databaseService.DeleteOfflinePlaceByServerIdAsync(place.ServerId);

    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = regionId });
    return; // No server call needed
}
```

**For Places (in DeletePlaceAsync):**

```csharp
// Before making API call:
var pendingCreate = await _database!.Table<PendingTripMutation>()
    .Where(m => m.EntityId == placeId && m.EntityType == "Place" && m.OperationType == "Create")
    .FirstOrDefaultAsync();

if (pendingCreate != null)
{
    // Entity was never synced - just remove from queue and offline table
    await _database.DeleteAsync(pendingCreate);
    await _databaseService.DeleteOfflinePlaceByServerIdAsync(placeId);

    // Also delete any pending mutations for this place
    var relatedMutations = await _database.Table<PendingTripMutation>()
        .Where(m => m.EntityId == placeId && m.EntityType == "Place")
        .ToListAsync();
    foreach (var m in relatedMutations)
        await _database.DeleteAsync(m);

    SyncCompleted?.Invoke(this, new SyncSuccessEventArgs { EntityId = placeId });
    return; // No server call needed
}
```

### D5: Prevent Image Load Before View Ready

**Files:** `MainPage.xaml.cs`, `TripOverviewContent.xaml`, `MainViewModel.cs`

**Problem:** Rapid navigation (My Trips -> Load) triggers trip loading in `OnAppearing`, which sets `LoadedTrip` and immediately triggers image binding before Android Activity is fully attached.

**Root Cause Flow (Hypothesis - "Before Attach" Path):**
1. `ApplyQueryAttributes` stores trip in `_pendingTrip`
2. `OnAppearing` calls `LoadTripForNavigationAsync(_pendingTrip)`
3. This sets `LoadedTrip = tripDetails` (MainViewModel.cs:3159)
4. XAML binding `{Binding LoadedTrip.CleanCoverImageUrl}` triggers image load
5. Android Activity not yet attached -> "destroyed activity" crash

**Note:** The "destroyed activity" error can occur in two scenarios:
- **Before attach:** Activity not yet ready (this fix targets this path)
- **After detach:** Activity disposed while image load in flight (requires cancellation on `OnDisappearing`)

This fix targets the "before attach" path based on reproduction steps (crash on rapid navigation). Validate with instrumentation logging to confirm root cause. Consider adding cancellation in `OnDisappearing` as a fallback for the "after detach" path.

**MAUI Version:** The `Loaded` event is supported in .NET MAUI 7.0+ and .NET 8/9/10. This project targets .NET 10 MAUI.

**Deterministic Solution: Use `Loaded` Event with Ordering Guard**

The `Loaded` event fires AFTER the visual tree is attached to the window. However, `Loaded` can fire before `OnAppearingAsync` completes (which initializes map state/permissions). Use an ordering guard to ensure both conditions are met.

**Implementation:**
```csharp
private bool _isLoaded;
private bool _isAppearingComplete;
private TripDetails? _pendingTrip;

public MainPage(MainViewModel viewModel)
{
    InitializeComponent();
    _viewModel = viewModel;
    BindingContext = viewModel;

    // Use Loaded event - fires when visual tree is attached to window
    Loaded += OnPageLoaded;

    // ... rest of constructor
}

private async void OnPageLoaded(object? sender, EventArgs e)
{
    _isLoaded = true;
    await LoadPendingTripIfReadyAsync();
}

protected override async void OnAppearing()
{
    base.OnAppearing();

    // Initialize map state, permissions, etc.
    await _viewModel.OnAppearingAsync();

    // Mark appearing complete AFTER initialization finishes
    _isAppearingComplete = true;

    // Try to load trip (will only proceed if Loaded has also fired)
    await LoadPendingTripIfReadyAsync();
}

protected override async void OnDisappearing()
{
    base.OnDisappearing();

    // Call existing ViewModel cleanup
    await _viewModel.OnDisappearingAsync();

    // Reset appearing flag for next appearance cycle
    // Note: _isLoaded stays true - Loaded fires once per page instance (visual tree attachment)
    _isAppearingComplete = false;

    // Optional: Cancel any in-flight image loads here as fallback for "after detach" path
    // _viewModel.CancelPendingImageLoads();
}

public void ApplyQueryAttributes(IDictionary<string, object> query)
{
    if (query.TryGetValue("LoadTrip", out var tripObj) && tripObj is TripDetails trip)
    {
        _pendingTrip = trip;

        // Edge case: If both flags already true (re-navigation), trigger load immediately
        // This handles the case where ApplyQueryAttributes is called after page is fully ready
        _ = LoadPendingTripIfReadyAsync();
    }

    // ... handle other query attributes (UnloadTrip, restoreEntityType, etc.)
}

private async Task LoadPendingTripIfReadyAsync()
{
    // ORDERING GUARD: Only load when BOTH conditions are met
    // 1. Visual tree is attached (Loaded fired)
    // 2. ViewModel initialization complete (OnAppearingAsync finished)
    if (!_isLoaded || !_isAppearingComplete)
        return;

    if (_pendingTrip != null)
    {
        var trip = _pendingTrip;
        _pendingTrip = null;
        await _viewModel.LoadTripForNavigationAsync(trip);
    }
}
```

**Why This Works:**
- `Loaded` ensures visual tree is attached to native window (Activity ready for image loads)
- `_isAppearingComplete` ensures ViewModel initialization (map, permissions) is done
- Trip only loads when BOTH conditions are satisfied
- Order of `Loaded` vs `OnAppearingAsync` completion doesn't matter - whichever finishes last triggers the load
- `_isLoaded` stays true across appear/disappear cycles (`Loaded` fires once per page instance)
- `_isAppearingComplete` resets in `OnDisappearing` so initialization re-runs on each appearance
- `ApplyQueryAttributes` also triggers load check for re-navigation edge case

**Validation Recommendation:**

Add instrumentation to confirm the hypothesis:
```csharp
private async void OnPageLoaded(object? sender, EventArgs e)
{
    _logger.LogDebug("Loaded fired at {Time}, _isAppearingComplete={Flag}", DateTime.Now, _isAppearingComplete);
    _isLoaded = true;
    await LoadPendingTripIfReadyAsync();
}

// In OnAppearingAsync completion:
_logger.LogDebug("OnAppearingAsync complete at {Time}, _isLoaded={Flag}", DateTime.Now, _isLoaded);
```

### D6: Force Place CREATE to Queue When Parent Region is Pending

**File:** `TripSyncService.cs` - `CreatePlaceAsync`

**Problem:** Even when online, if a Region CREATE is pending (network error / queued), calling the API for Place CREATE will fail with 400 because the server doesn't recognize the TempId as RegionId. Current code marks this as `ServerRejection`, permanently losing the valid Place.

**Solution:** At the start of `CreatePlaceAsync`, check if RegionId corresponds to a pending Region CREATE. If so, skip the API call and queue the Place CREATE instead, reusing the existing offline CREATE path.

**Implementation:**

Insert this check after `var tempClientId = ...` and before the `if (!IsConnected)` check:

```csharp
public async Task<Guid> CreatePlaceAsync(
    Guid tripId,
    Guid? regionId,
    string name,
    double latitude,
    double longitude,
    string? notes = null,
    string? iconName = null,
    string? markerColor = null,
    int? displayOrder = null,
    Guid? clientTempId = null)
{
    await EnsureInitializedAsync();
    var tempClientId = clientTempId ?? Guid.NewGuid();

    // NEW: If RegionId has a pending CREATE, queue this Place CREATE to maintain dependency ordering
    // This prevents 400 errors when online but parent Region hasn't synced yet
    if (regionId.HasValue)
    {
        var pendingRegionCreate = await _database!.Table<PendingTripMutation>()
            .Where(m => m.EntityId == regionId.Value
                     && m.EntityType == "Region"
                     && m.OperationType == "Create")
            .FirstOrDefaultAsync();

        if (pendingRegionCreate != null)
        {
            _logger.LogDebug(
                "Place CREATE queued - parent Region {RegionId} has pending CREATE",
                regionId.Value);

            // Reuse exact offline CREATE path (same as !IsConnected block)
            // Uses existing EnqueuePlaceMutationAsync method
            await EnqueuePlaceMutationAsync(
                "Create",
                tempClientId,
                tripId,
                regionId,
                name,
                latitude,
                longitude,
                notes,
                iconName,
                markerColor,
                displayOrder,
                true,  // includeNotes
                tempClientId);  // tempClientId for EntityCreated event

            // Persist offline entry with TempId (D1 pattern)
            var downloadedTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
            if (downloadedTrip != null)
            {
                var existingPlace = await _databaseService.GetOfflinePlaceByServerIdAsync(tempClientId);
                if (existingPlace == null)
                {
                    var offlinePlace = new OfflinePlaceEntity
                    {
                        TripId = downloadedTrip.Id,
                        ServerId = tempClientId,
                        RegionId = regionId.Value,
                        Name = name,
                        Latitude = latitude,
                        Longitude = longitude,
                        Notes = notes,
                        IconName = iconName,
                        MarkerColor = markerColor,
                        SortOrder = displayOrder ?? 0
                    };
                    await _databaseService.InsertOfflinePlaceAsync(offlinePlace);
                }
            }

            SyncQueued?.Invoke(this, new SyncQueuedEventArgs
            {
                EntityId = tempClientId,
                Message = "Queued - parent Region pending sync"
            });

            return tempClientId;
        }
    }

    // Existing code continues: if (!IsConnected) { ... } else { ... API call ... }
}
```

**Note:** This reuses the existing `EnqueuePlaceMutationAsync` method (TripSyncService.cs:345) with the same signature used by the offline CREATE path (line 112).

**Why This Works:**
- Dependency ordering: Place CREATE waits for Region CREATE to complete
- When Region CREATE syncs (D3), RegionId in pending Place mutations is rewritten to ServerId
- Queue processor then syncs the Place with correct RegionId
- No false rejections of valid Places
- Reuses existing enqueue infrastructure - no new methods needed

---

## Implementation Priority

| Priority | Fix | Effort | Impact |
|----------|-----|--------|--------|
| 0 | D0: Align TempId between ViewModel and Service | Low | PREREQUISITE - Required for D2 to work |
| 1 | D2: Subscribe to EntityCreated | Low | High - Fixes immediate TempId usage |
| 2 | D1: Create/update offline entries | Medium | High - Enables optimistic updates |
| 3 | D3: Reconcile mutations + RegionId refs | Medium | High - Fixes queue sync and F4 |
| 4 | D6: Queue Place CREATE when parent Region pending | Low | High - Prevents false Place rejections |
| 5 | D4: Cancel CREATE on DELETE (both entities) | Low | Medium - Prevents orphaned mutations |
| 6 | D5: Use Loaded event for trip loading | Low | Medium - Prevents image load crash |

---

## Testing Strategy

### Unit Tests
1. `CreateRegionAsync` inserts `OfflineAreaEntity` on success
2. `CreatePlaceAsync` inserts `OfflinePlaceEntity` on success
3. Offline CREATE path inserts entry with TempId as ServerId (for both Region and Place)
4. Online CREATE success updates existing TempId entry to ServerId (no duplicate)
5. `EntityCreated` event fires with correct TempClientId/ServerId mapping
6. `UpdateRegionAsync` finds offline entry after create
7. Pending mutations are rewritten after CREATE sync
8. RegionId in pending Place mutations is rewritten after Region CREATE sync
9. RegionId in offline Place rows is rewritten after Region CREATE sync
10. DELETE of unsynced Place cancels CREATE and removes offline entry
11. DELETE of unsynced Region removes orphaned Place CREATE mutations (by RegionId), UPDATE/DELETE mutations (by EntityId), and offline Place rows
12. CreatePlaceAsync queues (not calls API) when RegionId has pending Region CREATE

### Integration Tests
1. Create Region -> Update Region -> Verify single entity on server
2. Create Region -> Create Place in Region -> Verify Place has correct RegionId
3. Create Region (offline) -> Go online -> Verify sync completes with correct IDs
4. Create Region -> Delete Region (before sync) -> Verify no server calls
5. Create Place -> Delete Place (before sync) -> Verify no server calls
6. (Rapid sequence) Create Region -> Create Place in Region -> Go offline -> Queue both -> Sync -> Verify Place has correct RegionId
7. (Online but pending) Create Region (network error queues it) -> Create Place in Region -> Verify Place is queued (not rejected) -> Sync Region -> Sync Place -> Verify both on server

### Manual Test Case (Reproduces Original Bug)
1. Load a trip
2. Add a new region
3. Immediately edit the region name
4. Verify update succeeds (currently fails with NotFound)

---

## Files to Modify

| File | Changes |
|------|---------|
| `ITripSyncService.cs` | Add `clientTempId` parameter to `CreateRegionAsync` and `CreatePlaceAsync` signatures (D0) |
| `TripSyncService.cs` | Accept `clientTempId` parameter (D0), insert/update offline entries (D1), reconcile mutations and RegionId refs (D3), cancel CREATE on DELETE (D4), queue Place CREATE when parent Region pending (D6) |
| `MainViewModel.cs` | Pass tempId to service methods (D0), subscribe to EntityCreated (D2), update in-memory collections (D2) |
| `MainPage.xaml.cs` | Use `Loaded` event for trip loading instead of `OnAppearing` (D5) |
| `DatabaseService.cs` | Add `GetOfflinePlacesByRegionIdAsync` if not present (D3, D4) |

---

## Notes

- The `EntityCreated` event infrastructure already exists but is unused
- Issue #145 documents a comprehensive implementation plan that aligns with these findings
- The log file appears to be from an older version (references `TripItemEditorViewModel` which doesn't exist in current codebase), but the defects identified are still present in current code

---

## Implementation Progress

Track each fix: implement → build → test → verify finding resolved.

### Phase 0: Prerequisite
- [ ] **D0**: Align TempId between ViewModel and Service
- [ ] **D0 Verify**: builds, tests pass, F7 resolved

### Phase 1: Core Fixes
- [ ] **D2**: Subscribe to EntityCreated and update in-memory collections
- [ ] **D2 Verify**: builds, tests pass, F2 resolved

- [ ] **D1**: Persist offline entry on CREATE (all paths)
- [ ] **D1 Verify**: builds, tests pass, F1 resolved

- [ ] **D3**: Reconcile pending mutations and offline data after CREATE sync
- [ ] **D3 Verify**: builds, tests pass, F3 and F4 resolved

### Phase 2: Dependency Ordering
- [ ] **D6**: Queue Place CREATE when parent Region is pending
- [ ] **D6 Verify**: builds, tests pass, F4 fully resolved

### Phase 3: Edge Cases & Crash Fix
- [ ] **D4**: Cancel CREATE on DELETE for unsynced entities
- [ ] **D4 Verify**: builds, tests pass, orphan cleanup works

- [ ] **D5**: Use Loaded event for trip loading (prevent image crash)
- [ ] **D5 Verify**: builds, tests pass, F5 crash resolved

### Final Validation
- [ ] **Manual Test**: Add Region → Edit → Verify update succeeds
- [ ] **Close Issues**: Confirm #145, #118, #119 resolved

---

## Change Log

| Date | Action | Status |
|------|--------|--------|
| 2026-01-13 | Document merged to develop branch | ✅ |
| | D0 implementation | Pending |
| | D2 implementation | Pending |
| | D1 implementation | Pending |
| | D3 implementation | Pending |
| | D6 implementation | Pending |
| | D4 implementation | Pending |
| | D5 implementation | Pending |
