# Issue #152: Offline Queue UX/Controls

## Summary

Add user-facing controls for the offline location queue in Settings, making queue capacity visible and configurable while keeping Diagnostics read-only.

## Current State

### Queue Limit
- **Hard-coded** at 25,000 in `LocationQueueRepository.cs:14`
- Cleanup is silent - users have no visibility into when/why locations are removed
- Priority order: oldest synced → oldest rejected → oldest pending (data loss as last resort)

### Existing Code (Not Surfaced)
`TimelineDataViewModel` already has queue operations that are **never used in UI**:
- `ClearQueueAsync()` - clears pending queue with confirmation
- `ExportQueueAsync()` - exports to CSV

### Diagnostics Page
Currently has **both** read-only status AND actions:
- Status: pending/retrying/synced/rejected counts, oldest pending age, last sync time
- Actions: Export CSV, Clear Synced, Clear All

### Settings Page
"Timeline Data" section shows only timeline import/export, **not queue management**.

### Relevant Files

| File | Purpose |
|------|---------|
| `src/WayfarerMobile/Data/Repositories/LocationQueueRepository.cs` | Queue storage, cleanup logic |
| `src/WayfarerMobile/Services/AppDiagnosticService.cs` | Queue diagnostics queries |
| `src/WayfarerMobile/ViewModels/DiagnosticsViewModel.cs` | Diagnostics UI + actions |
| `src/WayfarerMobile/ViewModels/Settings/TimelineDataViewModel.cs` | Unused queue operations |
| `src/WayfarerMobile/Services/SettingsService.cs` | App settings persistence |
| `src/WayfarerMobile/Views/SettingsPage.xaml` | Settings UI |

## Gaps Identified

1. **No Settings UI** for queue limit or user control
2. **No visibility** into queue capacity or when cleanup occurs
3. **No coverage estimate** (how much offline time remaining)
4. **No GeoJSON export** for queue (CSV only)
5. **Duplicate export code** in DiagnosticsViewModel and TimelineDataViewModel
6. **Missing repository method**: `GetNewestPendingLocationAsync()` for coverage span
7. **Queue actions in Diagnostics** - should be read-only for troubleshooting

## Proposal

### Settings: "Offline Queue" Section

Add a new expander section in Settings (near "Timeline Data") with:

#### Status Summary
- Count / Limit (e.g., "5,000 / 25,000 locations")
- Oldest pending age
- Last sync time
- Health indicator

#### Queue Limit Setting
- Slider or picker with clamped range (5,000 - 50,000)
- Persisted via `ISettingsService`
- Applied to cleanup in `LocationQueueRepository`

#### Coverage Estimate
Display two values:

1. **Current span**: `newest_pending_timestamp - oldest_pending_timestamp`
   - Shows how much time is currently queued
   - Requires adding `GetNewestPendingLocationAsync()` to repository

2. **Remaining headroom**: `(queue_limit - current_count) × time_threshold_minutes`
   - Uses existing `LocationTimeThresholdMinutes` from settings
   - Worst-case calculation assuming every interval logs a location
   - Example: (25,000 - 5,000) × 5 min = 100,000 min ≈ 69 days
   - Label as approximate

#### Export Actions
- Export CSV button
- Export GeoJSON button
- Reuse `QueueExportService` (new, extracted)

#### Clear Actions
- Clear Pending button (with confirmation)
- Clear Synced button (with confirmation)
- Clear All button (with destructive warning)

### Diagnostics: Read-Only

Remove queue export/clear actions from Diagnostics page. Keep:
- Queue counts (pending, retrying, synced, rejected)
- Oldest pending age
- Last sync time
- Health status
- Queue details view (recent entries)

### New Service: QueueExportService

Extract export logic to single implementation:
- `ExportToCsvAsync()` - existing CSV format
- `ExportToGeoJsonAsync()` - new GeoJSON format
- Used by Settings (and optionally Diagnostics if ever needed)

## Implementation Plan

### 1. Settings Model

Add to `ISettingsService` and `SettingsService`:

```csharp
private const string KeyQueueLimitMaxLocations = "queue_limit_max_locations";

/// <summary>
/// Maximum number of locations to keep in queue before cleanup.
/// Range: 5,000 - 50,000. Default: 25,000.
/// </summary>
public int QueueLimitMaxLocations
{
    get => Preferences.Get(KeyQueueLimitMaxLocations, 25000);
    set => Preferences.Set(KeyQueueLimitMaxLocations, Math.Clamp(value, 5000, 50000));
}
```

### 2. Repository Updates

Add to `ILocationQueueRepository` and `LocationQueueRepository`:

```csharp
/// <summary>
/// Gets the newest pending location for coverage span calculation.
/// </summary>
Task<QueuedLocation?> GetNewestPendingLocationAsync();

/// <summary>
/// Gets the total count of all queued locations.
/// </summary>
Task<int> GetTotalCountAsync();
```

Update `CleanupOldLocationsAsync` to use settings:

```csharp
private async Task CleanupOldLocationsAsync(SQLiteAsyncConnection db, int maxQueuedLocations)
{
    var count = await db.Table<QueuedLocation>().CountAsync();
    if (count < maxQueuedLocations)
        return;
    // ... existing cleanup logic
}
```

### 3. Queue Status Service

Extend `AppDiagnosticService` or create `QueueStatusService`:

```csharp
public class QueueStatusInfo
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int SyncedCount { get; set; }
    public int RejectedCount { get; set; }
    public int QueueLimit { get; set; }
    public DateTime? OldestPendingTimestamp { get; set; }
    public DateTime? NewestPendingTimestamp { get; set; }
    public DateTime? LastSyncedTimestamp { get; set; }
    public string HealthStatus { get; set; }

    // Computed properties
    public TimeSpan? CurrentCoverageSpan =>
        NewestPendingTimestamp.HasValue && OldestPendingTimestamp.HasValue
            ? NewestPendingTimestamp.Value - OldestPendingTimestamp.Value
            : null;

    public TimeSpan? RemainingHeadroom(int timeThresholdMinutes) =>
        TimeSpan.FromMinutes((QueueLimit - TotalCount) * timeThresholdMinutes);
}
```

### 4. Export Service

Create `Services/QueueExportService.cs`:

```csharp
public class QueueExportService
{
    public async Task<string> ExportToCsvAsync();
    public async Task<string> ExportToGeoJsonAsync();
    public async Task ShareExportAsync(string format); // "csv" or "geojson"
}
```

### 5. Settings ViewModel

Create `ViewModels/Settings/OfflineQueueSettingsViewModel.cs`:

Following the pattern of `CacheSettingsViewModel`:
- Observable properties for queue status
- Queue limit binding
- Export commands
- Clear commands with confirmations
- Refresh command

### 6. Settings UI

Add "Offline Queue" expander to `SettingsPage.xaml`:

```xml
<!-- Offline Queue Section -->
<sfExpander:SfExpander IsExpanded="False">
    <sfExpander:SfExpander.Header>
        <Grid HeightRequest="48" Padding="10,0">
            <Label Text="Offline Queue"
                   FontSize="16"
                   FontAttributes="Bold"
                   VerticalOptions="Center" />
        </Grid>
    </sfExpander:SfExpander.Header>
    <sfExpander:SfExpander.Content>
        <!-- Status summary -->
        <!-- Queue limit slider -->
        <!-- Coverage estimate -->
        <!-- Export buttons -->
        <!-- Clear buttons -->
    </sfExpander:SfExpander.Content>
</sfExpander:SfExpander>
```

### 7. Diagnostics Cleanup

Remove from `DiagnosticsViewModel`:
- `ClearSyncedAsync()` command
- `ClearAllQueueAsync()` command
- `ExportQueueAsync()` command

Keep read-only display of queue status and details.

## Acceptance Criteria

- [ ] Users can see queue count and limit in Settings
- [ ] Users can adjust queue limit (clamped 5K-50K) and it persists
- [ ] Queue limit affects cleanup behavior in repository
- [ ] Users can see current coverage span (time queued)
- [ ] Users can see estimated remaining headroom (marked as approximate)
- [ ] Users can export queue to CSV from Settings
- [ ] Users can export queue to GeoJSON from Settings
- [ ] Users can clear pending/synced/all with confirmation from Settings
- [ ] Diagnostics page is read-only for queue information
- [ ] No duplicate export code between Settings and Diagnostics

## File Changes Summary

| Action | File |
|--------|------|
| Modify | `src/WayfarerMobile/Core/Interfaces/ISettingsService.cs` |
| Modify | `src/WayfarerMobile/Services/SettingsService.cs` |
| Modify | `src/WayfarerMobile/Data/Repositories/ILocationQueueRepository.cs` |
| Modify | `src/WayfarerMobile/Data/Repositories/LocationQueueRepository.cs` |
| Create | `src/WayfarerMobile/Services/QueueExportService.cs` |
| Create | `src/WayfarerMobile/ViewModels/Settings/OfflineQueueSettingsViewModel.cs` |
| Modify | `src/WayfarerMobile/ViewModels/SettingsViewModel.cs` |
| Modify | `src/WayfarerMobile/Views/SettingsPage.xaml` |
| Modify | `src/WayfarerMobile/Services/AppDiagnosticService.cs` |
| Modify | `src/WayfarerMobile/ViewModels/DiagnosticsViewModel.cs` |
| Modify | `src/WayfarerMobile/MauiProgram.cs` (DI registration) |

## References

- Issue: https://github.com/stef-k/WayfarerMobile/issues/152
- Pattern reference: "Offline Map Cache" section in Settings
- Existing queue code: `TimelineDataViewModel` (unused)
