# Location Metadata Fields & Export Format Alignment

## Overview

This spec covers two related changes:

1. **Metadata Fields**: Add device/app context fields to locations for both real-time API sync and file export
2. **Export Format Alignment**: Align mobile queue export (GeoJSON/CSV) with backend import format for bulk sync

The backend is adding metadata fields to the `Location` model. Mobile needs to capture and submit these fields via:
- Real-time API sync (`log-location` and `check-in` endpoints)
- Queue export (GeoJSON/CSV file import)

## Motivation

- **Diagnostics**: Metadata enables debugging issues by app version, device model, OS version
- **Statistics**: Track battery/charging patterns, device distribution, app adoption
- **Performance**: Bulk file import is faster than syncing thousands of locations via individual API calls
- **Offline workflow**: Users on long trips can export queue to file, clear queue to free space, and import to server later
- **Backup**: Exported files serve as a local backup of location data

## Field Summary

| Field | Type | Capture From | log-location | check-in | Export |
|-------|------|--------------|--------------|----------|--------|
| `isUserInvoked` | bool | Context | false | true | ✅ |
| `provider` | string? | Location source | ✅ | ✅ | ✅ |
| `bearing` | double? | Location.Course | ✅ | ✅ | ✅ |
| `appVersion` | string? | AppInfo.VersionString | ✅ | ✅ | ✅ |
| `appBuild` | string? | AppInfo.BuildString | ✅ | ✅ | ✅ |
| `deviceModel` | string? | DeviceInfo.Model | ✅ | ✅ | ✅ |
| `osVersion` | string? | DeviceInfo.Platform + VersionString | ✅ | ✅ | ✅ |
| `batteryLevel` | int? | Battery.ChargeLevel * 100 | ✅ | ✅ | ✅ |
| `isCharging` | bool? | Battery.State | ✅ | ✅ | ✅ |

**Summary:** 9 metadata fields to capture at queue time, submit via API, and include in export.

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

### New Fields

Add the following fields to the `QueuedLocation` entity to capture device context at the moment the location is queued.

**Entity changes (`QueuedLocation.cs`):**

```csharp
/// <summary>
/// The device's timezone ID when the location was captured.
/// Used for accurate LocalTimestamp calculation during export.
/// Example: "Europe/Athens", "America/New_York"
/// </summary>
public string? TimeZoneId { get; set; }

/// <summary>
/// App version string (e.g., "1.2.3").
/// </summary>
public string? AppVersion { get; set; }

/// <summary>
/// App build number (e.g., "42").
/// </summary>
public string? AppBuild { get; set; }

/// <summary>
/// Device model identifier (e.g., "Pixel 7", "iPhone 14 Pro").
/// </summary>
public string? DeviceModel { get; set; }

/// <summary>
/// Operating system version (e.g., "Android 14", "iOS 17.2").
/// </summary>
public string? OsVersion { get; set; }

/// <summary>
/// Battery level at capture time (0-100), or null if unavailable.
/// </summary>
public int? BatteryLevel { get; set; }

/// <summary>
/// Whether the device was charging when location was captured.
/// </summary>
public bool? IsCharging { get; set; }
```

**Migration:**

```sql
ALTER TABLE QueuedLocations ADD COLUMN TimeZoneId TEXT;
ALTER TABLE QueuedLocations ADD COLUMN AppVersion TEXT;
ALTER TABLE QueuedLocations ADD COLUMN AppBuild TEXT;
ALTER TABLE QueuedLocations ADD COLUMN DeviceModel TEXT;
ALTER TABLE QueuedLocations ADD COLUMN OsVersion TEXT;
ALTER TABLE QueuedLocations ADD COLUMN BatteryLevel INTEGER;
ALTER TABLE QueuedLocations ADD COLUMN IsCharging INTEGER;
```

**Population (at queue time):**
- `TimeZoneId`: `TimeZoneInfo.Local.Id`
- `AppVersion`: `AppInfo.VersionString`
- `AppBuild`: `AppInfo.BuildString`
- `DeviceModel`: `DeviceInfo.Model`
- `OsVersion`: `DeviceInfo.VersionString`
- `BatteryLevel`: `(int?)(Battery.ChargeLevel * 100)` (0-100)
- `IsCharging`: `Battery.State == BatteryState.Charging || Battery.State == BatteryState.Full`

**Fallback for existing rows:**
- All new fields will be `null` for rows created before migration
- Export should output `null` for missing values (backend handles nulls gracefully)

### Schema Version

Increment database schema version and add migration in `DatabaseService.cs`:

```csharp
// Version 4: Add metadata fields for export and diagnostics
if (currentVersion < 4)
{
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN TimeZoneId TEXT");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN AppVersion TEXT");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN AppBuild TEXT");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN DeviceModel TEXT");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN OsVersion TEXT");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN BatteryLevel INTEGER");
    await db.ExecuteAsync("ALTER TABLE QueuedLocations ADD COLUMN IsCharging INTEGER");
    await SetSchemaVersionAsync(db, 4);
}
```

## Metadata Capture

### Verify Existing Fields

These fields should already exist in `QueuedLocation.cs` - verify they are present:

```csharp
public double? Bearing { get; set; }
public string? Provider { get; set; }
public bool IsUserInvoked { get; set; }
```

### Capture at Queue Time

When creating a `QueuedLocation` (in `LocationQueueRepository` or location queuing code):

```csharp
var queuedLocation = new QueuedLocation
{
    // ... existing fields (Latitude, Longitude, Timestamp, Accuracy, Altitude, Speed, etc.) ...

    // Existing fields - ensure populated:
    Bearing = location.Course,  // MAUI Location.Course = bearing/heading
    Provider = GetProviderString(location),
    IsUserInvoked = isManualCheckIn,

    // NEW fields - capture at queue time:
    TimeZoneId = TimeZoneInfo.Local.Id,
    AppVersion = AppInfo.VersionString,
    AppBuild = AppInfo.BuildString,
    DeviceModel = DeviceInfo.Model,
    OsVersion = $"{DeviceInfo.Platform} {DeviceInfo.VersionString}",
    BatteryLevel = GetBatteryLevel(),
    IsCharging = GetIsCharging(),
};
```

### Helper Methods

```csharp
private static int? GetBatteryLevel()
{
    try
    {
        var level = Battery.ChargeLevel;
        return level >= 0 ? (int)(level * 100) : null;
    }
    catch { return null; }
}

private static bool? GetIsCharging()
{
    try
    {
        var state = Battery.State;
        return state == BatteryState.Charging || state == BatteryState.Full;
    }
    catch { return null; }
}

private static string? GetProviderString(Location location)
{
    // Return provider based on how location was obtained
    // Could be "gps", "network", "fused", etc.
    return "fused";  // or determine from location source
}
```

## API Request Format

### POST /api/location/log-location

Used for background/automatic location logging.

**Request Body:**
```json
{
    "latitude": 40.8497007,
    "longitude": 25.869276,
    "timestamp": "2024-01-15T12:30:00",
    "accuracy": 15.5,
    "altitude": 250.0,
    "speed": 5.2,
    "locationType": null,
    "notes": null,
    "activityTypeId": null,

    "isUserInvoked": false,
    "provider": "fused",
    "bearing": 180.0,
    "appVersion": "1.2.3",
    "appBuild": "45",
    "deviceModel": "Pixel 7 Pro",
    "osVersion": "Android 14",
    "batteryLevel": 85,
    "isCharging": false
}
```

**Response (unchanged):**
```json
{
    "success": true,
    "skipped": false,
    "locationId": 12345
}
```

Or if skipped due to threshold:
```json
{
    "success": true,
    "skipped": true,
    "locationId": null
}
```

### POST /api/location/check-in

Used for manual user check-ins.

**Request Body:**
```json
{
    "latitude": 40.8497007,
    "longitude": 25.869276,
    "timestamp": "2024-01-15T12:30:00",
    "accuracy": 15.5,
    "altitude": 250.0,
    "speed": 5.2,
    "locationType": "Manual",
    "notes": "Coffee shop visit",
    "activityTypeId": 5,

    "isUserInvoked": true,
    "provider": "fused",
    "bearing": 180.0,
    "appVersion": "1.2.3",
    "appBuild": "45",
    "deviceModel": "Pixel 7 Pro",
    "osVersion": "Android 14",
    "batteryLevel": 85,
    "isCharging": false
}
```

**Headers:**
```
Authorization: Bearer <api-token>
Idempotency-Key: <guid>  (optional but recommended)
Content-Type: application/json
```

**Response (unchanged):**
```json
{
    "message": "Check-in logged successfully",
    "location": {
        "id": 12345,
        "userId": "user-guid",
        "timestamp": "2024-01-15T10:30:00Z",
        "localTimestamp": "2024-01-15T12:30:00Z",
        "timeZoneId": "Europe/Athens",
        "coordinates": { ... },
        "accuracy": 15.5,
        "altitude": 250.0,
        "speed": 5.2
    }
}
```

## Sync Service Updates

Update the service that submits queued locations to the server (likely `LocationSyncService` or similar):

```csharp
private async Task<SyncResult> SubmitLocationAsync(QueuedLocation queued, string apiToken)
{
    var request = new
    {
        latitude = queued.Latitude,
        longitude = queued.Longitude,
        timestamp = queued.Timestamp,
        accuracy = queued.Accuracy,
        altitude = queued.Altitude,
        speed = queued.Speed,
        locationType = queued.IsUserInvoked ? "Manual" : null,
        notes = queued.CheckInNotes,
        activityTypeId = queued.ActivityTypeId,

        // Metadata fields
        isUserInvoked = queued.IsUserInvoked,
        provider = queued.Provider,
        bearing = queued.Bearing,
        appVersion = queued.AppVersion,
        appBuild = queued.AppBuild,
        deviceModel = queued.DeviceModel,
        osVersion = queued.OsVersion,
        batteryLevel = queued.BatteryLevel,
        isCharging = queued.IsCharging
    };

    // Determine endpoint based on IsUserInvoked
    var endpoint = queued.IsUserInvoked
        ? "/api/location/check-in"
        : "/api/location/log-location";

    // ... rest of HTTP request logic ...
}
```

## What Does NOT Change

These remain unchanged:

- **Server response format** - No changes to response structure
- **Idempotency handling** - Still uses `Idempotency-Key` header
- **Reconciliation logic** - Still uses `ServerId` from response
- **Error handling** - Same HTTP status codes and error formats
- **Rate limiting** - Same limits apply
- **Threshold filtering** - Server still filters by time/distance/accuracy

---

# Export Format Specification

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
        "IsUserInvoked": true,
        "Provider": "gps",
        "Bearing": 180.0,
        "AppVersion": "1.2.3",
        "AppBuild": "45",
        "DeviceModel": "Pixel 7 Pro",
        "OsVersion": "Android 14",
        "BatteryLevel": 85,
        "IsCharging": false,

        // Extra debug fields (ignored by backend, useful for diagnostics)
        "Id": 12345,
        "Status": "Pending",
        "SyncAttempts": 0,
        "LastSyncAttempt": null,
        "IsRejected": false,
        "RejectionReason": null,
        "LastError": null,
        "ActivityTypeId": 5
      }
    }
  ]
}
```

### Property Mapping

#### Backend-Compatible Fields (Required for Import)

| Mobile Field | Export Property | Notes |
|--------------|-----------------|-------|
| `Timestamp` | `TimestampUtc` | ISO 8601 UTC with Z suffix |
| (computed) | `LocalTimestamp` | ISO 8601 no offset (e.g., "2024-01-15T12:30:00.0000000") |
| `TimeZoneId` | `TimeZoneId` | IANA timezone (e.g., "Europe/Athens") |
| `Accuracy` | `Accuracy` | Double, meters |
| `Altitude` | `Altitude` | **Authoritative** - backend reads from here, not geometry Z |
| `Speed` | `Speed` | Double |
| `ActivityTypeId` | `Activity` | **Lowercase string name** (e.g., "walking", not "Walking") |
| `CheckInNotes` | `Notes` | String, renamed |
| `IsUserInvoked` | `IsUserInvoked` | Boolean, whether user manually triggered the check-in |
| `Provider` | `Provider` | String (e.g., "gps", "network", "fused") |
| `Bearing` | `Bearing` | Double, degrees (0-360) |
| `AppVersion` | `AppVersion` | String (e.g., "1.2.3") |
| `AppBuild` | `AppBuild` | String (e.g., "45") |
| `DeviceModel` | `DeviceModel` | String (e.g., "Pixel 7 Pro", "iPhone 14") |
| `OsVersion` | `OsVersion` | String (e.g., "Android 14", "iOS 17.2") |
| `BatteryLevel` | `BatteryLevel` | Integer 0-100, or null if unavailable |
| `IsCharging` | `IsCharging` | Boolean, or null if unavailable |

#### Extra Debug Fields (Ignored by Backend)

| Mobile Field | Export Property | Notes |
|--------------|-----------------|-------|
| `Id` | `Id` | Local DB ID |
| `SyncStatus` | `Status` | Human-readable status string |
| `SyncAttempts` | `SyncAttempts` | Integer |
| `LastSyncAttempt` | `LastSyncAttempt` | ISO 8601 or null |
| `IsRejected` | `IsRejected` | Boolean |
| `RejectionReason` | `RejectionReason` | String or null |
| `LastError` | `LastError` | String or null |
| `ActivityTypeId` | `ActivityTypeId` | Numeric ID - ignored by backend |

### Geometry

- Coordinate order: `[longitude, latitude]` or `[longitude, latitude, altitude]`
- Z-coordinate (altitude) in geometry is optional for GeoJSON spec compliance
- **Backend reads Altitude from properties, not geometry Z**
- SRID: WGS84 (4326)

## CSV Format

### Header Row

```
Latitude,Longitude,TimestampUtc,LocalTimestamp,TimeZoneId,Accuracy,Altitude,Speed,Activity,Notes,IsUserInvoked,Provider,Bearing,AppVersion,AppBuild,DeviceModel,OsVersion,BatteryLevel,IsCharging,Id,Status,SyncAttempts,LastSyncAttempt,IsRejected,RejectionReason,LastError,ActivityTypeId
```

### Example Row

```
40.8497007,25.869276,2024-01-15T10:30:00.0000000Z,2024-01-15T12:30:00.0000000,Europe/Athens,15.5,250.0,5.2,walking,Manual check-in,true,gps,180.0,1.2.3,45,Pixel 7 Pro,Android 14,85,false,12345,Pending,0,,false,,,5
```

### Notes

- First 19 columns are backend-compatible (required for import)
- Remaining columns are extra debug fields (ignored by backend)
- Empty values represented as empty string (not "null")
- Formula injection protection: Values starting with `=`, `+`, `-`, `@`, `\t`, `\r` are prefixed with single quote

## Activity Resolution

The `Activity` field requires resolving `ActivityTypeId` to its **lowercase** name string.

**Important:** Backend matches activity names case-insensitively, but expects lowercase for consistency.

| ActivityTypeId | Activity Name |
|----------------|---------------|
| 1 | walking |
| 2 | running |
| 3 | cycling |
| 4 | driving |
| ... | ... |

**Resolution logic:**
1. If `ActivityTypeId` is null → `Activity` is null
2. If `ActivityTypeId` maps to known type → Use activity name **(always lowercase)**
3. If `ActivityTypeId` is unknown → Use null (don't guess)

**Note:** Activity types should be fetched from a local cache or hardcoded mapping. The exact mapping depends on the backend's `ActivityType` table. The `ActivityTypeId` is also included in debug fields but is ignored by the backend import.

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

### Entity & Data Layer
- [ ] Verify existing fields in `QueuedLocation` entity:
  - [ ] `Bearing` (double?)
  - [ ] `Provider` (string?)
  - [ ] `IsUserInvoked` (bool)
- [ ] Add new fields to `QueuedLocation` entity:
  - [ ] `TimeZoneId` (string?)
  - [ ] `AppVersion` (string?)
  - [ ] `AppBuild` (string?)
  - [ ] `DeviceModel` (string?)
  - [ ] `OsVersion` (string?)
  - [ ] `BatteryLevel` (int?)
  - [ ] `IsCharging` (bool?)
- [ ] Add migration in `DatabaseService.cs` (version 4)

### Location Capture (LocationQueueRepository)
- [ ] Ensure `Bearing` is captured from `Location.Course`
- [ ] Ensure `Provider` is captured appropriately
- [ ] Ensure `IsUserInvoked` is set correctly
- [ ] Capture `TimeZoneId` from `TimeZoneInfo.Local.Id`
- [ ] Capture `AppVersion` from `AppInfo.VersionString`
- [ ] Capture `AppBuild` from `AppInfo.BuildString`
- [ ] Capture `DeviceModel` from `DeviceInfo.Model`
- [ ] Capture `OsVersion` from `$"{DeviceInfo.Platform} {DeviceInfo.VersionString}"`
- [ ] Capture `BatteryLevel` from `(int?)(Battery.ChargeLevel * 100)`
- [ ] Capture `IsCharging` from `Battery.State`
- [ ] Add helper methods: `GetBatteryLevel()`, `GetIsCharging()`, `GetProviderString()`

### Sync Service (API Submission)
- [ ] Update `log-location` request DTO to include all metadata fields
- [ ] Update `check-in` request DTO to include all metadata fields
- [ ] Ensure `isUserInvoked` is set correctly (false for log-location, true for check-in)
- [ ] Update sync service to populate all metadata fields from `QueuedLocation`

### Export Service
- [ ] Add `GetPendingLocationsForExportAsync()` method to repository (excludes Synced, Syncing, Rejected)
- [ ] Update `QueueExportService.ExportToGeoJsonAsync()`:
  - [ ] Use new pending-only query
  - [ ] Use PascalCase for backend-compatible properties
  - [ ] Add `TimestampUtc`, `LocalTimestamp`, `TimeZoneId`
  - [ ] Resolve `ActivityTypeId` to `Activity` name (lowercase)
  - [ ] Rename `CheckInNotes` to `Notes`
  - [ ] Move `IsUserInvoked`, `Provider`, `Bearing` from debug to required section
  - [ ] Add metadata fields: `AppVersion`, `AppBuild`, `DeviceModel`, `OsVersion`, `BatteryLevel`, `IsCharging`
  - [ ] Keep extra debug fields with PascalCase
- [ ] Update `QueueExportService.ExportToCsvAsync()`:
  - [ ] Use new pending-only query
  - [ ] Update headers to match backend format (19 required + 8 debug columns)
  - [ ] Add `LocalTimestamp`, `TimeZoneId` columns
  - [ ] Resolve `ActivityTypeId` to `Activity` name (lowercase)
  - [ ] Rename `CheckInNotes` to `Notes`
  - [ ] Move `IsUserInvoked`, `Provider`, `Bearing` from debug to required section
  - [ ] Add metadata fields: `AppVersion`, `AppBuild`, `DeviceModel`, `OsVersion`, `BatteryLevel`, `IsCharging`

### Filename Generation
- [ ] Update filename format: `wayfarer_app_queue_fromDate_{date}_toDate_{date}.{ext}`
- [ ] Extract date range from exported data
- [ ] Handle empty export (no pending locations) with user message

### UI Updates
- [ ] Update export button to show count of exportable (pending) locations
- [ ] Show message if no pending locations to export
