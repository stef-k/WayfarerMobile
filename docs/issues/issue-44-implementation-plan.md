### Goal

Store user's timeline data locally in app so users can have their location history stored locally too, with full offline support.

Local storage use is expected to expand given that each Location Place may contain also HTML notes but given that we do not allow images in notes but only image URLs it should not be that much.

Additionally there should be an export feature of the entire table in CSV and [GeoJSON](https://geojson.org/) formats.

Last there should be a roundtrip availability so exported files can also be imported into the app and replace or update the respective local database table.

---

## Implementation Plan

### Design Decisions

1. **Local-first with server enrichment**: Store locations locally immediately using AND filter (matching server logic), then enrich with server data (addresses, activities) when online.

2. **New code over modifications**: Minimize changes to existing codebase. Use event-based decoupling where possible. Majority of implementation is new services/components.

3. **Offline-first**: Locations are stored locally even when offline. When connectivity returns, sync with server and update `ServerId` linkage.

4. **Storage estimation**: ~70-140 MB for 25 years of moderate use (trivial for modern devices).

---

### Backend Dependency

**Requires server feature request**: The `/api/location/log-location` endpoint must return the created location ID when storing a location.

```
Current response:  { "success": true, "skipped": false }
Required response: { "success": true, "skipped": false, "locationId": 12345 }
```

This is needed to link local records to server records via `ServerId`.

---

## Settings / Thresholds

**No new settings required.** The AND filter thresholds are already available:

- **Source**: `ISettingsService` (synced from server via `SettingsSyncService`)
- **Properties**: `LocationTimeThresholdMinutes`, `LocationDistanceThresholdMeters`
- **Server is single source of truth** - thresholds are periodically synced

The `LocalTimelineFilter` will read from `ISettingsService` instead of hardcoding defaults.

---

## New Components (Majority of Work)

### 1. LocalTimelineEntry Entity
**File**: `src/WayfarerMobile/Data/Entities/LocalTimelineEntry.cs`

```csharp
[Table("LocalTimelineEntries")]
public class LocalTimelineEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Server linkage (null = local-only, not yet synced)
    [Indexed]
    public int? ServerId { get; set; }

    // Core location data
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    [Indexed]
    public DateTime Timestamp { get; set; }
    public double? Accuracy { get; set; }
    public double? Altitude { get; set; }
    public double? Speed { get; set; }
    public double? Bearing { get; set; }
    public string? Provider { get; set; }

    // Enriched from server (populated on reconciliation)
    public string? Address { get; set; }
    public string? FullAddress { get; set; }
    public string? Place { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? PostCode { get; set; }
    public string? ActivityType { get; set; }
    public string? Notes { get; set; }
    public string? Timezone { get; set; }

    // Sync metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastEnrichedAt { get; set; }
}
```

### 2. LocalTimelineFilter
**File**: `src/WayfarerMobile.Core/Algorithms/LocalTimelineFilter.cs`

Applies AND filter logic (matching server's filtering) for local storage decisions.

```csharp
public class LocalTimelineFilter
{
    private LocationData? _lastStoredLocation;

    public int TimeThresholdMinutes { get; set; } = 5;
    public int DistanceThresholdMeters { get; set; } = 15;

    public bool ShouldStore(LocationData location)
    {
        if (_lastStoredLocation == null)
            return true;

        var timePassed = (location.Timestamp - _lastStoredLocation.Timestamp)
            .TotalMinutes >= TimeThresholdMinutes;
        var distanceMoved = GeoMath.CalculateDistance(...) >= DistanceThresholdMeters;

        return timePassed && distanceMoved;  // AND logic (matches server)
    }

    public void MarkAsStored(LocationData location) => _lastStoredLocation = location;
}
```

### 3. LocalTimelineStorageService
**File**: `src/WayfarerMobile/Services/LocalTimelineStorageService.cs`

Core service for local timeline operations. Subscribes to location events.

**Responsibilities**:
- Subscribe to `LocationServiceCallbacks.LocationReceived`
- Apply `LocalTimelineFilter` (AND logic)
- Store to `LocalTimelineEntry` table
- Handle `ServerId` updates when sync completes
- Provide CRUD operations for timeline data

### 4. LocationSyncCallbacks (New)
**File**: `src/WayfarerMobile/Services/LocationSyncCallbacks.cs`

Event-based callback system for sync operations (mirrors `LocationServiceCallbacks` pattern).

```csharp
public static class LocationSyncCallbacks
{
    public static event EventHandler<LocationSyncedEventArgs>? LocationSynced;
    public static event EventHandler<LocationSkippedEventArgs>? LocationSkipped;

    public static void NotifyLocationSynced(int queuedLocationId, int serverId, DateTime timestamp);
    public static void NotifyLocationSkipped(int queuedLocationId, DateTime timestamp, string reason);
}

public class LocationSyncedEventArgs : EventArgs
{
    public int QueuedLocationId { get; init; }
    public int ServerId { get; init; }
    public DateTime Timestamp { get; init; }
}

public class LocationSkippedEventArgs : EventArgs
{
    public int QueuedLocationId { get; init; }
    public DateTime Timestamp { get; init; }
    public string Reason { get; init; } = string.Empty;
}
```

### 5. TimelineExportService
**File**: `src/WayfarerMobile/Services/TimelineExportService.cs`

Exports local timeline data to CSV and GeoJSON formats.

```csharp
public class TimelineExportService
{
    Task<string> ExportToCsvAsync(DateTime? fromDate, DateTime? toDate);
    Task<string> ExportToGeoJsonAsync(DateTime? fromDate, DateTime? toDate);
    Task<FileResult?> ShareExportAsync(string format, DateTime? fromDate, DateTime? toDate);
}
```

**CSV Format**:
```csv
id,timestamp,latitude,longitude,accuracy,altitude,speed,address,place,country,notes
1,2025-01-15T14:30:00Z,40.7128,-74.0060,10.5,25.0,1.2,"123 Main St","New York","USA",""
```

**GeoJSON Format**:
```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": { "type": "Point", "coordinates": [-74.0060, 40.7128] },
      "properties": {
        "id": 1,
        "timestamp": "2025-01-15T14:30:00Z",
        "accuracy": 10.5,
        "address": "123 Main St",
        "place": "New York"
      }
    }
  ]
}
```

### 6. TimelineImportService
**File**: `src/WayfarerMobile/Services/TimelineImportService.cs`

Imports CSV and GeoJSON files into local timeline.

```csharp
public class TimelineImportService
{
    Task<ImportResult> ImportFromCsvAsync(Stream fileStream);
    Task<ImportResult> ImportFromGeoJsonAsync(Stream fileStream);
}

public record ImportResult(int Imported, int Updated, int Skipped, List<string> Errors);
```

**Import behavior**:
- Match by timestamp (within tolerance) for updates
- Insert new records with `ServerId = null`
- Skip duplicates
- Report errors for malformed rows

### 7. TimelineDataService
**File**: `src/WayfarerMobile/Services/TimelineDataService.cs`

Abstraction layer for timeline data access with offline fallback.

```csharp
public class TimelineDataService
{
    Task<List<TimelineLocationDisplay>> GetLocationsForDateAsync(DateTime date);
    Task EnrichFromServerAsync(DateTime date);  // Fetch and merge server data
}
```

Logic:
1. Load from `LocalTimelineEntry`
2. If online, fetch from server and merge enrichment data
3. Return merged result

---

## Integration Points (Minimal Changes)

### 1. DatabaseService (Additive Only)
**File**: `src/WayfarerMobile/Data/Services/DatabaseService.cs`

**Change**: Add table creation in `EnsureInitializedAsync()`:
```csharp
await _database.CreateTableAsync<LocalTimelineEntry>();
```

**Risk**: Very low - additive only, no modification to existing logic.

### 2. LocationSyncService (Small Addition)
**File**: `src/WayfarerMobile/Services/LocationSyncService.cs`

**Change**: After successful sync, raise event via `LocationSyncCallbacks`:
```csharp
// In SyncLocationWithRetryAsync, after success:
if (success)
{
    LocationSyncCallbacks.NotifyLocationSynced(
        location.Id,
        result.LocationId,  // New field from server
        location.Timestamp);
}
else if (result.Skipped)
{
    LocationSyncCallbacks.NotifyLocationSkipped(
        location.Id,
        location.Timestamp,
        "Threshold not met");
}
```

**Risk**: Low - small addition, existing logic unchanged.

### 3. ApiClient / IApiClient (Additive Only)
**Files**:
- `src/WayfarerMobile/Core/Interfaces/IApiClient.cs`
- `src/WayfarerMobile/Services/ApiClient.cs`

**Changes**:

1. Add `LocationId` property to `ApiResult`:
```csharp
public class ApiResult
{
    // ... existing properties ...
    public int? LocationId { get; set; }  // New: returned by server on store
}
```

2. Update `ParseSuccessResponse` to extract the new field:
```csharp
private static ApiResult ParseSuccessResponse(string responseBody)
{
    using var doc = JsonDocument.Parse(responseBody);
    var root = doc.RootElement;

    var message = root.TryGetProperty("message", out var msgProp)
        ? msgProp.GetString()
        : "Success";

    // NEW: Extract locationId if present
    int? locationId = root.TryGetProperty("locationId", out var idProp)
        && idProp.ValueKind == JsonValueKind.Number
            ? idProp.GetInt32()
            : null;

    if (message?.Contains("skipped", StringComparison.OrdinalIgnoreCase) == true)
        return ApiResult.SkippedResult(message);

    var result = ApiResult.Ok(message);
    result.LocationId = locationId;
    return result;
}
```

**Backward Compatibility**:
| Mobile | Server | Result |
|--------|--------|--------|
| Current | Current | Works (no locationId) |
| Current | New (returns locationId) | Works - unknown field ignored |
| New (reads locationId) | Current | Works - locationId = null |
| New | New | Full functionality |

**Risk**: Very low - additive property, defensive null handling, existing behavior unchanged.

### 4. TimelineViewModel (Small Addition)
**File**: `src/WayfarerMobile/ViewModels/TimelineViewModel.cs`

**Change**: In `LoadDataAsync()`, add offline fallback at the start:
```csharp
private async Task LoadDataAsync()
{
    // NEW: Offline fallback
    if (!IsOnline)
    {
        await LoadFromLocalAsync();
        return;
    }

    // ... existing server fetch logic unchanged ...
}

// NEW: Private method for offline loading
private async Task LoadFromLocalAsync()
{
    var locations = await _timelineDataService.GetLocationsForDateAsync(SelectedDate);
    // ... populate UI from local data ...
}
```

**Risk**: Low - adds early return for offline case, existing online logic unchanged.

### 5. Settings Page (UI Addition)
**Files**:
- `src/WayfarerMobile/Views/SettingsPage.xaml`
- `src/WayfarerMobile/ViewModels/SettingsViewModel.cs`

**UI Changes**:
Add "Timeline Data" section with:

```xml
<!-- Timeline Data Section -->
<VerticalStackLayout>
    <Label Text="Timeline Data" Style="{StaticResource SectionHeader}" />

    <!-- Export -->
    <Button Text="Export Timeline (CSV)" Command="{Binding ExportCsvCommand}" />
    <Button Text="Export Timeline (GeoJSON)" Command="{Binding ExportGeoJsonCommand}" />

    <!-- Import -->
    <Button Text="Import Timeline" Command="{Binding ImportTimelineCommand}" />

    <!-- Info -->
    <Label Text="{Binding LocalTimelineCount, StringFormat='Local entries: {0}'}" />
</VerticalStackLayout>
```

**ViewModel additions**:
```csharp
[RelayCommand]
private async Task ExportCsvAsync();

[RelayCommand]
private async Task ExportGeoJsonAsync();

[RelayCommand]
private async Task ImportTimelineAsync();

[ObservableProperty]
private int localTimelineCount;
```

**Risk**: Very low - UI addition only.

### 6. DI Registration
**File**: `src/WayfarerMobile/MauiProgram.cs`

**Change**: Register new services:
```csharp
// Local Timeline Services
services.AddSingleton<LocalTimelineStorageService>();
services.AddSingleton<TimelineDataService>();
services.AddTransient<TimelineExportService>();
services.AddTransient<TimelineImportService>();
```

**Risk**: Very low - additive only.

---

## Data Flow Diagrams

### Location Capture (Online or Offline)

```
GPS Location Received
        |
        v
LocationServiceCallbacks.LocationReceived (existing event)
        |
        v
+-------------------------------------------------------+
| LocalTimelineStorageService (NEW - subscribes)        |
|   |                                                   |
|   v                                                   |
| LocalTimelineFilter.ShouldStore() [AND logic]         |
|   |                                                   |
|   v                                                   |
| If passes: Insert LocalTimelineEntry {ServerId=null}  |
+-------------------------------------------------------+
        | (parallel, existing flow unchanged)
        v
+-------------------------------------------------------+
| Existing: QueuedLocation -> LocationSyncService       |
+-------------------------------------------------------+
```

### Sync Confirmation (Online)

```
LocationSyncService syncs QueuedLocation to server
        |
        v
Server responds: { success: true, locationId: 123 }
        |
        v
LocationSyncCallbacks.NotifyLocationSynced() (NEW event)
        |
        v
+-------------------------------------------------------+
| LocalTimelineStorageService (subscribes)              |
|   |                                                   |
|   v                                                   |
| Match by timestamp -> Update ServerId = 123           |
+-------------------------------------------------------+
```

### Sync Skipped (Server Rejected)

```
Server responds: { success: true, skipped: true }
        |
        v
LocationSyncCallbacks.NotifyLocationSkipped() (NEW event)
        |
        v
+-------------------------------------------------------+
| LocalTimelineStorageService (subscribes)              |
|   |                                                   |
|   v                                                   |
| Match by timestamp -> Delete from LocalTimelineEntry  |
| (Server's AND filter is stricter - trust server)      |
+-------------------------------------------------------+
```

### Timeline Viewing

```
User opens TimelinePage for specific date
        |
        v
+-------------------------------------------------------------+
| TimelineViewModel.LoadDataAsync()                           |
|   |                                                         |
|   v                                                         |
| If offline: Load from LocalTimelineEntry (basic data)       |
| If online:  Fetch from server (enriched) + update local     |
+-------------------------------------------------------------+
```

---

## Reconciliation Logic (Timeline Viewing - Online)

When user views a timeline date while online, `TimelineDataService.EnrichFromServerAsync()` performs reconciliation:

```csharp
public async Task EnrichFromServerAsync(DateTime date)
{
    // 1. Fetch from server
    var serverData = await _apiClient.GetTimelineLocationsAsync("day", date.Year, date.Month, date.Day);
    if (serverData?.Data == null) return;

    // 2. Load existing local entries for this date
    var localEntries = await _database.GetLocalTimelineEntriesForDateAsync(date);
    var localByServerId = localEntries
        .Where(e => e.ServerId.HasValue)
        .ToDictionary(e => e.ServerId!.Value);

    foreach (var serverLocation in serverData.Data)
    {
        if (localByServerId.TryGetValue(serverLocation.Id, out var existing))
        {
            // 3a. EXISTS locally - update enrichment fields
            existing.Address = serverLocation.Address;
            existing.FullAddress = serverLocation.FullAddress;
            existing.Place = serverLocation.Place;
            existing.Region = serverLocation.Region;
            existing.Country = serverLocation.Country;
            existing.PostCode = serverLocation.PostCode;
            existing.ActivityType = serverLocation.ActivityType;
            existing.Timezone = serverLocation.Timezone;
            // Note: Don't overwrite local Notes if user edited offline
            if (string.IsNullOrEmpty(existing.Notes))
                existing.Notes = serverLocation.Notes;
            existing.LastEnrichedAt = DateTime.UtcNow;

            await _database.UpdateLocalTimelineEntryAsync(existing);
        }
        else
        {
            // 3b. NOT found locally - insert (manual web entry, other device, historical)
            var newEntry = new LocalTimelineEntry
            {
                ServerId = serverLocation.Id,
                Latitude = serverLocation.Latitude,
                Longitude = serverLocation.Longitude,
                Timestamp = serverLocation.Timestamp,
                Accuracy = serverLocation.Accuracy,
                Altitude = serverLocation.Altitude,
                Speed = serverLocation.Speed,
                Address = serverLocation.Address,
                FullAddress = serverLocation.FullAddress,
                Place = serverLocation.Place,
                Region = serverLocation.Region,
                Country = serverLocation.Country,
                PostCode = serverLocation.PostCode,
                ActivityType = serverLocation.ActivityType,
                Notes = serverLocation.Notes,
                Timezone = serverLocation.Timezone,
                CreatedAt = DateTime.UtcNow,
                LastEnrichedAt = DateTime.UtcNow
            };

            await _database.InsertLocalTimelineEntryAsync(newEntry);
        }
    }
}
```

**Key behaviors**:
- Server entries not in local → INSERT (handles manual adds, other devices, historical)
- Server entries in local → UPDATE enrichment (addresses, activity, etc.)
- Local Notes preserved if edited offline (don't overwrite with server)
- `LastEnrichedAt` tracked for cache freshness

---

## Testing Requirements

### Unit Tests (New)

| Test Class | Coverage |
|------------|----------|
| `LocalTimelineFilterTests` | AND logic, threshold edge cases, first location |
| `LocalTimelineStorageServiceTests` | Store, update ServerId, delete on skip |
| `TimelineExportServiceTests` | CSV format, GeoJSON format, date filtering |
| `TimelineImportServiceTests` | CSV parsing, GeoJSON parsing, duplicate handling, error cases |
| `TimelineDataServiceTests` | Offline fallback, enrichment merge |
| `LocationSyncCallbacksTests` | Event raising and subscription |

### Integration Tests

| Test | Coverage |
|------|----------|
| End-to-end location capture | GPS -> LocalTimelineEntry storage |
| Sync confirmation flow | QueuedLocation -> Server -> ServerId update |
| Export/Import roundtrip | Export -> Import -> Data integrity |
| Offline fallback | Timeline viewing when offline |

---

## File Summary

### New Files (14)

| File | Type |
|------|------|
| `Data/Entities/LocalTimelineEntry.cs` | Entity |
| `Core/Algorithms/LocalTimelineFilter.cs` | Algorithm |
| `Services/LocalTimelineStorageService.cs` | Service |
| `Services/LocationSyncCallbacks.cs` | Events + EventArgs |
| `Services/TimelineExportService.cs` | Service |
| `Services/TimelineImportService.cs` | Service |
| `Services/TimelineDataService.cs` | Service |
| `Tests/.../LocalTimelineFilterTests.cs` | Tests |
| `Tests/.../LocalTimelineStorageServiceTests.cs` | Tests |
| `Tests/.../TimelineExportServiceTests.cs` | Tests |
| `Tests/.../TimelineImportServiceTests.cs` | Tests |
| `Tests/.../TimelineDataServiceTests.cs` | Tests |
| `Tests/.../LocationSyncCallbacksTests.cs` | Tests |
| `Tests/.../TimelineDataServiceReconciliationTests.cs` | Tests |

### Modified Files (7) - Minimal Changes

| File | Change Type | Risk |
|------|-------------|------|
| `Data/Services/DatabaseService.cs` | Add table creation + CRUD methods | Very Low |
| `Core/Interfaces/IApiClient.cs` | Add LocationId property | Very Low |
| `Services/ApiClient.cs` | Parse LocationId in ParseSuccessResponse | Very Low |
| `Services/LocationSyncService.cs` | Raise sync events via LocationSyncCallbacks | Low |
| `ViewModels/TimelineViewModel.cs` | Add offline fallback logic | Low |
| `ViewModels/SettingsViewModel.cs` | Add Export/Import commands | Low |
| `Views/SettingsPage.xaml` | Add Timeline Data section | Very Low |
| `MauiProgram.cs` | Register new services | Very Low |

---

## Acceptance Criteria (Updated)

- [ ] Location history stored locally in SQLite database
- [ ] Locations stored immediately using AND filter (matches server logic)
- [ ] Works fully offline - locations stored even without connectivity
- [ ] ServerId linkage updated when sync confirms storage
- [ ] Locations deleted from local if server rejects (skipped)
- [ ] Timeline page shows local data when offline
- [ ] Timeline page shows enriched server data when online
- [ ] Export to CSV format available
- [ ] Export to GeoJSON format available
- [ ] Import from CSV restores/updates local database
- [ ] Import from GeoJSON restores/updates local database
- [ ] Test coverage for all new components

---

## Implementation Order

1. ~~**Backend feature request** - Create issue for `locationId` in response~~ ✅ DONE (Wayfarer#38 deployed)
2. **LocalTimelineEntry entity** - Foundation table
3. **DatabaseService** - Add table creation + CRUD methods
4. **LocalTimelineFilter** - AND logic using ISettingsService thresholds
5. **LocationSyncCallbacks** - Event system with EventArgs
6. **LocalTimelineStorageService** - Core storage logic, subscribe to events
7. **ApiClient changes** - Add LocationId to ApiResult, update ParseSuccessResponse
8. **LocationSyncService changes** - Raise sync events
9. **TimelineDataService** - Data access + reconciliation logic
10. **TimelineViewModel changes** - Offline fallback
11. **TimelineExportService** - CSV and GeoJSON export
12. **TimelineImportService** - CSV and GeoJSON import
13. **SettingsViewModel + SettingsPage** - UI for Export/Import
14. **MauiProgram** - DI registration
15. **Tests** - Throughout implementation (TDD where practical)

---

### Impacts

- Backend: ✅ Feature deployed (Wayfarer#38 - locationId in response)
- Mobile: New components + minimal integration to existing code

### Related Links

- Backend feature request: https://github.com/stef-k/Wayfarer/issues/38 ✅ CLOSED

### Historical Data Policy

- **Going forward**: Locations stored locally immediately via AND filter
- **Historical (on-demand)**: When user views a past date, data is fetched from server and cached locally for future offline access
- **No proactive backfill**: No background job to sync all historical records
- **Export scope**: Includes whatever is in local storage (captured + previously viewed dates)

### DateTime/Timestamp Handling (CRITICAL)

**All timestamps must follow existing patterns to avoid data drift:**

| Field | Format | Reference |
|-------|--------|-----------|
| `LocalTimelineEntry.Timestamp` | UTC DateTime | Match `QueuedLocation.Timestamp` |
| `LocalTimelineEntry.CreatedAt` | UTC DateTime | Match `QueuedLocation.CreatedAt` |
| CSV Export | ISO 8601 UTC (`2025-01-15T14:30:00Z`) | - |
| GeoJSON Export | ISO 8601 UTC | - |
| Import parsing | Parse as UTC, store as UTC | - |

**Reference models for consistency:**
- `QueuedLocation` - existing entity with timestamp handling
- `TimelineLocation` - API model with `Timestamp` and `LocalTimestamp`
- `LocationData` - core model used in GPS capture

**Rules:**
1. Store all timestamps as UTC in database
2. Convert to local time only for display (use `Timezone` field if available)
3. Export always in ISO 8601 UTC format
4. Import must parse and convert to UTC before storage
5. Matching by timestamp (for reconciliation) uses UTC comparison with tolerance

### Notes

- Thresholds read from existing `ISettingsService` (synced from server - single source of truth)
- Local-first approach ensures data sovereignty even if user leaves server
- Event-based decoupling minimizes risk to existing stable code
- All new services are additive - existing functionality remains unchanged
