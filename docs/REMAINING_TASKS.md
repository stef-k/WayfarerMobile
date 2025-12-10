# WayfarerMobile - Remaining Tasks

**Created:** December 10, 2025
**Purpose:** Comprehensive list of remaining work before device testing
**Reference:** Based on REWRITE_ASSESSMENT_REPORT.md findings

---

## Priority Legend

| Priority | Meaning |
|----------|---------|
| **P0** | Critical - Must fix before any testing |
| **P1** | High - Should fix before release |
| **P2** | Medium - Nice to have for release |
| **P3** | Low - Future enhancement |

---

## Section 1: Code Quality Fixes (P0)

These are quick fixes that improve code quality and should be done first.

### 1.1 Extract Classes from TripNavigationService

**Priority:** P0
**Effort:** Low (30 min)
**Files:** `Services/TripNavigationService.cs`

Extract these classes to separate files:
- `NavigationRoute` → `Core/Models/NavigationRoute.cs`
- `NavigationWaypoint` → `Core/Models/NavigationWaypoint.cs`
- `TripNavigationState` → `Core/Models/TripNavigationState.cs`
- `WaypointType` → `Core/Enums/WaypointType.cs`
- `NavigationStatus` → `Core/Enums/NavigationStatus.cs`

### 1.2 Remove Duplicate PolylineDecoder

**Priority:** P0
**Effort:** Trivial (10 min)
**Files:**
- `Services/TripNavigationService.cs` - Remove `DecodePolyline()` method
- `Helpers/PolylineDecoder.cs` - Keep this one

Update `TripNavigationService` to use `PolylineDecoder.Decode()` instead of its internal method.

### 1.3 Fix Duplicate ApiClient DI Registration

**Priority:** P0
**Effort:** Trivial (5 min)
**File:** `MauiProgram.cs`

Find and remove duplicate registration:
```csharp
// REMOVE one of these:
services.AddSingleton<IApiClient, ApiClient>();
services.AddSingleton<ApiClient>(); // Remove this duplicate
```

### 1.4 Add Logging to Exception Handlers

**Priority:** P0
**Effort:** Low (20 min)
**Files:** All ViewModels with `catch (Exception ex)` blocks

Pattern to apply:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to {Action}", actionDescription);
    ErrorMessage = $"Failed to {userFriendlyAction}: {ex.Message}";
}
```

### 1.5 Commit Untracked PolylineDecoder.cs

**Priority:** P0
**Effort:** Trivial (1 min)
**File:** `Helpers/PolylineDecoder.cs`

```bash
git add src/WayfarerMobile/Helpers/PolylineDecoder.cs
git commit -m "chore: add PolylineDecoder helper"
```

---

## Section 2: Project Structure (P1)

### 2.1 Reorganize to Feature Folders

**Priority:** P1 (optional but recommended)
**Effort:** Medium (2-3 hours)
**Impact:** Better maintainability, matches documented architecture

Current structure:
```
ViewModels/
  MainViewModel.cs
  TripsViewModel.cs
  ...
Views/
  MainPage.xaml
  TripsPage.xaml
  ...
```

Target structure:
```
Features/
  Map/
    MainPage.xaml
    MainPage.xaml.cs
    MainViewModel.cs
  Trips/
    TripsPage.xaml
    TripsPage.xaml.cs
    TripsViewModel.cs
  Timeline/
    TimelinePage.xaml
    TimelinePage.xaml.cs
    TimelineViewModel.cs
  Groups/
    GroupsPage.xaml
    GroupsPage.xaml.cs
    GroupsViewModel.cs
  Settings/
    SettingsPage.xaml
    SettingsPage.xaml.cs
    SettingsViewModel.cs
  Onboarding/
    OnboardingPage.xaml
    OnboardingPage.xaml.cs
    OnboardingViewModel.cs
  CheckIn/
    CheckInPage.xaml
    CheckInPage.xaml.cs
    CheckInViewModel.cs
  Security/
    LockScreenPage.xaml
    LockScreenPage.xaml.cs
    LockScreenViewModel.cs
```

**Note:** This requires updating namespaces, DI registrations, and XAML references.

---

## Section 3: Feature Implementations (P1)

### 3.1 Trip Segment List UI

**Priority:** P1
**Effort:** Medium (1-2 hours)
**Status:** Segment visualization on map is DONE; segment LIST UI is missing

**What exists:**
- `MapService.UpdateTripSegments()` - Draws polylines on map
- Segment data loaded from offline storage

**What's needed:**
- Add segment list to trip sidebar (show transport mode, distance, duration)
- Tap segment to highlight on map

**Files to modify:**
- `Views/TripsPage.xaml` - Add segment list in drawer
- `ViewModels/TripsViewModel.cs` - Add segment collection and selection

**Reference:** Old app's `TripSidebar/SegmentList.xaml`

### 3.2 Background Notifications

**Priority:** P1
**Effort:** Medium (2-3 hours)
**Status:** Only foreground service notification exists

**What's needed:**
- Tracking state change notifications (started/stopped)
- Download progress notifications
- Sync status notifications (optional)

**Files to create/modify:**
- `Services/NotificationService.cs` - Central notification manager
- `Platforms/Android/Services/LocationTrackingService.cs` - Update notification content
- `Services/TripDownloadService.cs` - Add download notifications

**Reference:** Old app's notification handling in `BackgroundTracker`

### 3.3 About Page

**Priority:** P1
**Effort:** Low-Medium (1-2 hours)

**What's needed:**
- App version display
- Open source libraries used (licenses)
- Links to project/documentation
- Better UI with Syncfusion components

**Files to create:**
- `Features/Settings/AboutPage.xaml`
- `Features/Settings/AboutViewModel.cs`

**Reference:** Old app's `AboutPage.xaml`

**Libraries to acknowledge:**
- Mapsui (MIT)
- CommunityToolkit.Mvvm (MIT)
- Syncfusion.Maui.Toolkit (MIT)
- sqlite-net-pcl (MIT)
- ZXing.Net.MAUI (Apache 2.0)
- Serilog (Apache 2.0)
- Polly (BSD-3-Clause)

---

## Section 4: Settings Features (P1)

### 4.1 Navigation Settings

**Priority:** P1
**Effort:** Medium (2-3 hours)

Add to `SettingsService.cs`:
```csharp
// Navigation settings
public bool NavigationAudioEnabled { get; set; } = true;
public float NavigationVolume { get; set; } = 1.0f;
public string NavigationLanguage { get; set; } = "en-US";
public bool NavigationVibrationEnabled { get; set; } = true;
public bool AutoRerouteEnabled { get; set; } = true;
public string DistanceUnits { get; set; } = "kilometers"; // or "miles"
public string LastTransportMode { get; set; } = "walk";
```

Add to `SettingsPage.xaml`:
- Navigation section with `SfExpander`
- Audio toggle with `SfSwitch`
- Volume slider
- Language picker
- Vibration toggle
- Auto-reroute toggle
- Distance units picker

Update `NavigationAudioService.cs`:
- Read settings from `ISettingsService`
- Apply volume setting to TTS
- Apply language setting to TTS

### 4.2 Cache Settings

**Priority:** P2
**Effort:** Low (1 hour)

Add to `SettingsService.cs`:
```csharp
public int LiveCachePrefetchRadius { get; set; } = 3; // 1-9 tiles
public string? TileServerUrl { get; set; } = null; // null = default OSM
```

Add to `SettingsPage.xaml`:
- Prefetch radius slider (1-9)
- Tile server URL text entry (advanced)

### 4.3 Diagnostic Tools

**Priority:** P2
**Effort:** Medium (2-3 hours)

**What's needed:**
- Health check button (shows GPS status, permissions, connectivity)
- Export database button (CSV export of queued locations)
- Clear sync queue button
- View logs button

**Files to create:**
- `Features/Settings/DiagnosticsPage.xaml`
- `Features/Settings/DiagnosticsViewModel.cs`

### 4.4 Groups State Persistence

**Priority:** P2
**Effort:** Low (30 min)

Add to `SettingsService.cs`:
```csharp
public string? LastSelectedGroupId { get; set; }
public string? LastSelectedGroupName { get; set; }
public bool GroupsLegendExpanded { get; set; } = true;
```

Update `GroupsViewModel.cs`:
- Save selected group on change
- Restore selected group on page load

---

## Section 5: Animations (P2)

### 5.1 Sidebar Animations

**Priority:** P2
**Effort:** Low (1 hour)
**File:** `Views/TripsPage.xaml.cs` or drawer settings

The `SfNavigationDrawer` has built-in animation. Configure:
```xaml
<navigationDrawer:SfNavigationDrawer
    DrawerOpenMode="SlideOnTop"
    Duration="0.3">
```

If custom animation needed for sidebar minimize on navigation start:
```csharp
private async Task MinimizeSidebarAsync()
{
    var animation = new Animation(v =>
        DrawerView.WidthRequest = v,
        DrawerView.Width, 0);
    animation.Commit(this, "SidebarCollapse", 16, 250, Easing.CubicOut);
}
```

### 5.2 Toast Notification System

**Priority:** P2
**Effort:** Medium (1-2 hours)

**Files to create:**
- `Shared/Controls/ToastOverlay.xaml`
- `Shared/Controls/ToastOverlay.xaml.cs`
- `Services/ToastService.cs`

**Implementation:**
```csharp
public interface IToastService
{
    Task ShowAsync(string message, int durationMs = 3000);
}

// In ToastOverlay.xaml.cs
public async Task ShowAsync(string message)
{
    ToastLabel.Text = message;
    ToastFrame.Opacity = 0;
    ToastFrame.IsVisible = true;
    await ToastFrame.FadeTo(1, 150);
    await Task.Delay(3000);
    await ToastFrame.FadeTo(0, 150);
    ToastFrame.IsVisible = false;
}
```

### 5.3 Page Transition Animations

**Priority:** P3
**Effort:** Low (30 min)

Already have `Shell.PresentationMode="ModalAnimated"` on:
- LockScreenPage
- QrScannerPage
- CheckInPage

Add to other modal pages if needed.

---

## Section 6: Polish Items (P2-P3)

### 6.1 Loading States Integration

**Priority:** P2
**Effort:** Low (1 hour)
**Status:** `LoadingOverlay` control exists

Verify integration on all pages:
- [x] TripsPage (IsLoadingDetails)
- [x] GroupsPage (IsBusy)
- [x] TimelinePage (IsBusy)
- [x] PublicTripsPage (IsCloning)
- [ ] MainPage (initial load)
- [ ] SettingsPage (server settings fetch)

### 6.2 User-Friendly Error Dialogs

**Priority:** P2
**Effort:** Low (1 hour)

Create `Services/DialogService.cs`:
```csharp
public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task ShowSuccessAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
}
```

Replace `DisplayAlert` calls throughout app with centralized service.

### 6.3 Battery Optimization Detection

**Priority:** P3
**Effort:** Low (1 hour)

Add to `PermissionsService.cs`:
```csharp
public bool IsBatteryOptimizationIgnored()
{
#if ANDROID
    var pm = Android.App.Application.Context.GetSystemService(
        Android.Content.Context.PowerService) as Android.OS.PowerManager;
    return pm?.IsIgnoringBatteryOptimizations(
        Android.App.Application.Context.PackageName) ?? false;
#else
    return true; // iOS handles differently
#endif
}
```

Show warning in Settings if not ignored.

### 6.4 Performance Profiling

**Priority:** P3
**Effort:** Medium (2-3 hours)

Areas to profile:
- Map rendering performance
- Tile cache hit rate
- Location update processing time
- Database query performance

Add timing logs:
```csharp
var sw = Stopwatch.StartNew();
// operation
_logger.LogDebug("Operation took {ElapsedMs}ms", sw.ElapsedMilliseconds);
```

---

## Section 7: GPS Filtering (NO ACTION NEEDED)

**Status:** Confirmed - Native FusedLocationProvider handles filtering

The new app correctly delegates to native providers:
- Android: `FusedLocationProviderClient` (Google Play Services)
- iOS: `CLLocationManager`

These native providers already handle:
- Accuracy filtering
- Jump detection
- Speed validation
- Multi-sensor fusion

**No porting needed from old GpsAccuracyFilter.**

Bearing handling is ALREADY implemented and IMPROVED in `LocationIndicatorService.cs`:
- Circular averaging (same algorithm as old app)
- BearingAccuracy tracking (NEW - not in old app)
- Dynamic cone angle (NEW)
- Stale location detection (NEW)

---

## Section 8: Documentation Updates (P0)

### 8.1 Update CLAUDE.md

**Priority:** P0
**File:** `CLAUDE.md`

Update "Current Status" section to reflect:
- Progress: ~94% → Update with actual completion after fixes
- Completed Features: Add segment visualization, loading overlay
- Next Tasks: Update with remaining items from this document

### 8.2 Update IMPLEMENTATION_CHECKLIST.md

**Priority:** P0
**File:** `docs/IMPLEMENTATION_CHECKLIST.md`

Mark as complete:
- [x] Phase 8.2 - Sliding panel animation (done via SfNavigationDrawer)
- [x] Phase 8.2 - Segment visualization (done via MapService)
- [x] Phase 12.2 - Detect connectivity changes (done via OfflineBanner)
- [x] Phase 12.2 - Show offline banner (done)
- [ ] Phase 12.1 - User-friendly error messages → in progress
- [ ] Phase 12.6 - Loading states → partially done
- [ ] Phase 12.6 - App icon and splash screen → done but verify

Update Phase 12 completion from ~50% to ~75%.

### 8.3 Update REWRITE_ASSESSMENT_REPORT.md

**Priority:** P1
**File:** `docs/REWRITE_ASSESSMENT_REPORT.md`

After completing fixes, update:
- Code Quality score from 8.9 to target 9.5+
- Project Structure score from 7/10 (if reorganized)
- Action items table (mark completed)

---

## Implementation Order

### Day 1: Code Quality (P0)
1. Extract classes from TripNavigationService (30 min)
2. Remove duplicate PolylineDecoder (10 min)
3. Fix duplicate ApiClient registration (5 min)
4. Add logging to exception handlers (20 min)
5. Commit PolylineDecoder.cs (1 min)
6. Update IMPLEMENTATION_CHECKLIST.md (15 min)
7. Build and verify (10 min)

**Estimated: 1.5 hours**

### Day 2: Settings Features (P1)
1. Navigation settings in SettingsService (1 hour)
2. Navigation settings UI in SettingsPage (1 hour)
3. Update NavigationAudioService to use settings (30 min)
4. Groups state persistence (30 min)

**Estimated: 3 hours**

### Day 3: Feature Implementations (P1)
1. Trip segment list UI (2 hours)
2. About page with licenses (1.5 hours)
3. Background notifications (2.5 hours)

**Estimated: 6 hours**

### Day 4: Polish (P2)
1. Toast notification system (1.5 hours)
2. Loading states verification (30 min)
3. User-friendly error dialogs (1 hour)
4. Sidebar animation tuning (30 min)

**Estimated: 3.5 hours**

### Day 5: Optional & Testing
1. Project structure reorganization (optional, 3 hours)
2. Cache settings UI (1 hour)
3. Diagnostic tools page (2 hours)
4. Device testing preparation

**Estimated: 3-6 hours**

---

## Total Effort Estimate

| Category | Effort |
|----------|--------|
| Code Quality (P0) | 1.5 hours |
| Settings Features (P1) | 3 hours |
| Feature Implementations (P1) | 6 hours |
| Polish (P2) | 3.5 hours |
| Optional (P2-P3) | 3-6 hours |
| **Total** | **17-20 hours** |

---

## Verification Checklist

After completing all tasks:

- [ ] All P0 items completed
- [ ] All P1 items completed
- [ ] Build succeeds without warnings
- [ ] All documentation updated
- [ ] Git status clean (no untracked files)
- [ ] Ready for device testing
