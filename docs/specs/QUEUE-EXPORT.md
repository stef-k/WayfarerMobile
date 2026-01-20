# Queue Export Format Alignment

## Overview

Align the mobile queue export (GeoJSON and CSV) with the Wayfarer backend's import format to enable file-based bulk sync as an alternative to individual API calls.

## Motivation

- **Performance**: Bulk file import is faster than syncing thousands of locations via individual check-in API calls
- **Offline workflow**: Users on long trips can export queue to file, clear queue to free space, and import to server later
- **Backup**: Exported files serve as a local backup of location data

## Filename Format

```
wayfarer_app_queue_fromDate_{YYYY-MM-DD}_toDate_{YYYY-MM-DD}.geojson
wayfarer_app_queue_fromDate_{YYYY-MM-DD}_toDate_{YYYY-MM-DD}.csv
```

**Examples:**
- `wayfarer_app_queue_fromDate_2024-01-15_toDate_2024-03-20.geojson`
- `wayfarer_app_queue_fromDate_2024-01-15_toDate_2024-03-20.csv`

**Date derivation:**
- `fromDate`: Oldest `Timestamp` in exported data (UTC date)
- `toDate`: Newest `Timestamp` in exported data (UTC date)
- If queue is empty: Do not allow export (show message "No pending locations to export")

## Export Scope: Pending/Retrying Only

**Critical:** Export only locations that have **not yet been synced** to prevent duplicates on import.

### Included in Export

| Status | Included | Reason |
|--------|----------|--------|
| `Pending` | ✅ Yes | Not yet on server |
| `Retrying` (Pending + SyncAttempts > 0) | ✅ Yes | Failed sync, not on server |
| `Syncing` | ❌ No | In-flight, may complete |
| `Synced` | ❌ No | Already on server |
| `Rejected` | ❌ No | Invalid data, shouldn't be imported |

### Rationale

If the app syncs a location via API while the user also imports it via file, duplicates would occur. By exporting only pending/retrying locations:

1. Synced locations are excluded → no duplicates from already-synced data
2. Syncing locations are excluded → avoids race condition with in-flight syncs
3. Rejected locations are excluded → invalid data shouldn't be imported

**Note:** Backend deduplication is tracked as a separate enhancement (see Backend Deduplication section).

## Database Schema Change

### New Field: `TimeZoneId`

Add `TimeZoneId` to the `QueuedLocation` entity to capture the device's timezone at the moment the location is queued.

**Entity change (`QueuedLocation.cs`):**

```csharp
/// <summary>
/// The device's timezone ID when the location was captured.
/// Used for accurate LocalTimestamp calculation during export.
/// Example: "Europe/Athens", "America/New_York"
/// </summary>
public string? TimeZoneId { get; set; }
```

**Migration:**

```sql
ALTER TABLE QueuedLocations ADD COLUMN TimeZoneId TEXT;
```

**Population:**
- Capture `TimeZoneInfo.Local.Id` when queuing a location
- Existing rows will have `TimeZoneId = null`; export should fall back to device's current timezone

### Schema Version

Increment database schema version and add migration in `DatabaseService.cs`:

```csharp
// Version 4: Add TimeZoneId for export timezone accuracy
if (currentVersion < 4)
{
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN TimeZoneId TEXT");
    await SetSchemaVersionAsync(db, 4);
}
```

## GeoJSON Format

### Structure

```json
{
  "type": "FeatureCollection",
  "features": [
    {
      "type": "Feature",
      "geometry": {
        "type": "Point",
        "coordinates": [25.869276, 40.8497007, 250.0]
      },
      "properties": {
        // Backend-compatible fields (required for import)
        "TimestampUtc": "2024-01-15T10:30:00.0000000Z",
        "LocalTimestamp": "2024-01-15T12:30:00.0000000",
        "TimeZoneId": "Europe/Athens",
        "Accuracy": 15.5,
        "Altitude": 250.0,
        "Speed": 5.2,
        "Activity": "walking",
        "Notes": "Manual check-in at landmark",

        // Extra debug fields (ignored by backend, useful for diagnostics)
        "Id": 12345,
        "Bearing": 180.0,
        "Provider": "gps",
        "Status": "Pending",
        "SyncAttempts": 0,
        "LastSyncAttempt": null,
        "IsRejected": false,
        "RejectionReason": null,
        "LastError": null,
        "IsUserInvoked": true,
        "ActivityTypeId": 5
      }
    }
  ]
}
```

### Property Mapping

| Mobile Field | Export Property | Notes |
|--------------|-----------------|-------|
| `Timestamp` | `TimestampUtc` | ISO 8601 UTC |
| `TimeZoneId` | `TimeZoneId` | From entity, fallback to device current |
| (computed) | `LocalTimestamp` | `TimestampUtc` converted using `TimeZoneId` |
| `Accuracy` | `Accuracy` | PascalCase |
| `Altitude` | `Altitude` | From geometry or property |
| `Speed` | `Speed` | PascalCase |
| `ActivityTypeId` | `Activity` | Resolved to activity name string |
| `CheckInNotes` | `Notes` | Renamed |
| `Bearing` | `Bearing` | Extra (debug) |
| `Provider` | `Provider` | Extra (debug) |
| `SyncStatus` | `Status` | Extra (debug), human-readable |
| `SyncAttempts` | `SyncAttempts` | Extra (debug) |
| `LastSyncAttempt` | `LastSyncAttempt` | Extra (debug) |
| `IsRejected` | `IsRejected` | Extra (debug) |
| `RejectionReason` | `RejectionReason` | Extra (debug) |
| `LastError` | `LastError` | Extra (debug) |
| `IsUserInvoked` | `IsUserInvoked` | Extra (debug) |
| `Id` | `Id` | Extra (debug), local DB ID |
| `ActivityTypeId` | `ActivityTypeId` | Extra (debug), numeric ID |

### Geometry

- Coordinate order: `[longitude, latitude]` or `[longitude, latitude, altitude]`
- Altitude included in coordinates when available
- SRID: WGS84 (4326)

## CSV Format

### Header Row

```
Latitude,Longitude,TimestampUtc,LocalTimestamp,TimeZoneId,Accuracy,Altitude,Speed,Activity,Notes,Bearing,Provider,Status,SyncAttempts,LastSyncAttempt,IsRejected,RejectionReason,LastError,IsUserInvoked,Id,ActivityTypeId
```

### Example Row

```
40.8497007,25.869276,2024-01-15T10:30:00.0000000Z,2024-01-15T12:30:00.0000000,Europe/Athens,15.5,250.0,5.2,walking,Manual check-in,180.0,gps,Pending,0,,false,,,true,12345,5
```

### Notes

- First 10 columns are backend-compatible (required for import)
- Remaining columns are extra debug fields (ignored by backend)
- Empty values represented as empty string (not "null")
- Formula injection protection: Values starting with `=`, `+`, `-`, `@`, `\t`, `\r` are prefixed with single quote

## Activity Resolution

The `Activity` field requires resolving `ActivityTypeId` to its name:

| ActivityTypeId | Activity Name |
|----------------|---------------|
| 1 | walking |
| 2 | running |
| 3 | cycling |
| 4 | driving |
| ... | ... |

**Resolution logic:**
1. If `ActivityTypeId` is null → `Activity` is null
2. If `ActivityTypeId` maps to known type → Use activity name (lowercase)
3. If `ActivityTypeId` is unknown → Use "unknown" or null

**Note:** Activity types should be fetched from a local cache or hardcoded mapping. The exact mapping depends on the backend's `ActivityType` table.

## LocalTimestamp Calculation

**At export time:**

```csharp
DateTime ComputeLocalTimestamp(DateTime timestampUtc, string? timeZoneId)
{
    if (string.IsNullOrEmpty(timeZoneId))
    {
        // Fallback: use device's current timezone
        return timestampUtc.ToLocalTime();
    }

    try
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTimeFromUtc(timestampUtc, tz);
    }
    catch (TimeZoneNotFoundException)
    {
        // Invalid timezone stored, fallback to device current
        return timestampUtc.ToLocalTime();
    }
}
```

## Backend Compatibility

The export format is compatible with `WayfarerGeoJsonParser` and `CsvLocationParser`:

- Required fields are present with expected names and casing
- Extra debug fields are ignored (JSON/CSV parsers skip unknown properties)
- Timestamps are ISO 8601 format
- Coordinates are in correct order (lon, lat for GeoJSON; lat, lon columns for CSV)

## Backend Deduplication (Future Enhancement)

**Issue:** If a location syncs via API between export and import, duplicates may occur.

**Mobile mitigation (this spec):** Export only pending/retrying locations.

**Backend enhancement (separate issue):** Add deduplication to `LocationImportService`:
- Before inserting, check for existing location with same `UserId` + `TimestampUtc` (within 1 second) + `Coordinates` (within ~10m)
- Skip duplicate instead of inserting

**Tracking:** See Wayfarer backend issue for implementation.

## Import Workflow

1. User exports queue to GeoJSON/CSV file (only pending/retrying locations)
2. User transfers file to computer (or uses directly on device)
3. User clears pending queue on mobile (optional, frees space)
4. User uploads file via Wayfarer web UI (Location Import)
5. Backend parses file using `WayfarerGeoJsonParser` or `CsvLocationParser`
6. Locations are inserted with their original timestamps
7. Timeline shows locations in correct chronological order

## Implementation Checklist

### Database Schema
- [ ] Add `TimeZoneId` field to `QueuedLocation` entity
- [ ] Add migration in `DatabaseService.cs` (version 4)
- [ ] Capture `TimeZoneInfo.Local.Id` when queuing location in `LocationQueueRepository`

### Export Service
- [ ] Add `GetPendingLocationsForExportAsync()` method to repository (excludes Synced, Syncing, Rejected)
- [ ] Update `QueueExportService.ExportToGeoJsonAsync()`:
  - [ ] Use new pending-only query
  - [ ] Use PascalCase for backend-compatible properties
  - [ ] Add `TimestampUtc`, `LocalTimestamp`, `TimeZoneId`
  - [ ] Resolve `ActivityTypeId` to `Activity` name
  - [ ] Rename `CheckInNotes` to `Notes`
  - [ ] Keep extra debug fields with PascalCase
- [ ] Update `QueueExportService.ExportToCsvAsync()`:
  - [ ] Use new pending-only query
  - [ ] Update headers to match backend format
  - [ ] Add `LocalTimestamp`, `TimeZoneId` columns
  - [ ] Resolve `ActivityTypeId` to `Activity` name
  - [ ] Rename `CheckInNotes` to `Notes`

### Filename Generation
- [ ] Update filename format: `wayfarer_app_queue_fromDate_{date}_toDate_{date}.{ext}`
- [ ] Extract date range from exported data
- [ ] Handle empty export (no pending locations) with user message

### UI Updates
- [ ] Update export button to show count of exportable (pending) locations
- [ ] Show message if no pending locations to export
