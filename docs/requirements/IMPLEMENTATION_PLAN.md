# WayfarerMobile - Implementation Plan to 100% Completion

**Created:** December 8, 2025
**Last Updated:** December 10, 2025
**Current Status:** ~94% Complete (Business Logic Done, UI Remaining)
**Target:** 100% Feature Parity + Production Ready

---

## Current Session Status

### Completed This Session (Business Logic)

| Task | Status | Description |
|------|--------|-------------|
| Global Exception Handler | DONE | `IExceptionHandlerService` + `ExceptionHandlerService` |
| Polly Retry Logic | DONE | Exponential backoff in `ApiClient` for transient failures |
| Serilog File Logging | DONE | Daily rotation, 7-day retention, 10MB per file |
| Notification Icon | DONE | Proper monochrome vector drawable for Android |
| Trip Sidebar Toggle | DONE | `MainViewModel` with `LoadedTrip`, `SelectedPlace`, toggle logic |
| Wake Lock Service | DONE | Android + iOS platform implementations |
| App Lifecycle Handling | DONE | `IAppLifecycleService` with state persistence |
| Error Handling Audit | DONE | Fixed async void methods, added try/catch |
| Warning Elimination | DONE | 0 warnings, 0 errors |

### Next Session: UI Implementation

All remaining work is **UI/presentation layer** focused. Use Syncfusion components as per `DESIGN_SPEC.md`.

---

## UI Implementation Phase (Next Session)

### Priority Order for UI Tasks

#### P0 - Critical UI (Must Have)

| Task | Component | Description | Syncfusion |
|------|-----------|-------------|------------|
| 1.1 Route Polyline | `MapService` | Draw navigation route on map | Mapsui |
| 4.3 Lock Screen Overlay | `LockScreenPage` | PIN entry on app resume | `SfNumericEntry` |
| 5.3 Download Notifications | Native | Progress notification during downloads | Native |

#### P1 - High Priority UI

| Task | Component | Description | Syncfusion |
|------|-----------|-------------|------------|
| 4.1 Place Context Menu | `PlaceContextMenu` | Tap place → actions popup | `SfPopup` |
| 4.2 Location Details Modal | `LocationDetailsModal` | Bottom sheet with place info | `SfBottomSheet` |
| Trip Sidebar Panel | `TripSidebar` | Sliding panel animation | `SfNavigationDrawer` |
| Segment Visualization | `MapService` | Show segments on map | Mapsui polylines |

#### P2 - Medium Priority UI

| Task | Component | Description | Syncfusion |
|------|-----------|-------------|------------|
| Loading States | All pages | Consistent loading indicators | `SfBusyIndicator` |
| Animations | Various | Smooth transitions | MAUI animations |
| iOS Background Banner | iOS-specific | Blue status bar indicator | Native |

#### P3 - Polish

| Task | Component | Description | Syncfusion |
|------|-----------|-------------|------------|
| App Icon | Resources | Final app icon design | - |
| Splash Screen | Resources | Splash screen | MAUI splash |

---

## Detailed UI Task Specifications

### Task 1.1: Navigation Route Visualization

**Priority:** P0
**Files:** `Services/MapService.cs`

**Requirements:**
- [ ] Add route polyline layer to map
- [ ] Support different colors for completed/remaining route
- [ ] Update route display as user progresses
- [ ] Clear route when navigation ends

**Implementation:**
```csharp
// Already stubbed in MapService - implement visual layer
void ShowNavigationRoute(NavigationRoute route);
void UpdateNavigationRouteProgress(double lat, double lon);
void ClearNavigationRoute();
```

---

### Task 4.3: Lock Screen Overlay

**Priority:** P0
**Files:** `Views/LockScreenPage.xaml`, `ViewModels/LockScreenViewModel.cs`

**Reference:** `AppLockService` (already implemented)

**Requirements:**
- [ ] PIN entry overlay on app resume (when locked)
- [ ] 4-digit PIN input with numeric keypad
- [ ] Wrong PIN attempt handling (shake animation, delay after 3 attempts)
- [ ] Biometric option (if available)

**Syncfusion Components:**
- `SfNumericEntry` for PIN digits
- `SfBusyIndicator` for verification

---

### Task 4.1: Place Context Menu

**Priority:** P1
**Files:** `Views/Controls/PlaceContextMenu.xaml`

**Requirements:**
- [ ] Show on place tap (map or list)
- [ ] Options: Navigate, View Details, Center Map, Share
- [ ] Dismiss on tap outside

**Syncfusion Components:**
- `SfPopup` for the menu

**Actions:**
```csharp
[RelayCommand] void NavigateToPlace(TripPlace place);
[RelayCommand] void ViewPlaceDetails(TripPlace place);
[RelayCommand] void CenterOnPlace(TripPlace place);
[RelayCommand] void SharePlace(TripPlace place);
```

---

### Task 4.2: Location Details Modal

**Priority:** P1
**Files:** `Views/Modals/LocationDetailsModal.xaml`

**Requirements:**
- [ ] Bottom sheet style (drag to dismiss)
- [ ] Place name, icon, coordinates
- [ ] Notes/description (HTML rendered)
- [ ] Region name
- [ ] Distance from current location
- [ ] Action buttons (Navigate, Edit, Share)

**Syncfusion Components:**
- `SfBottomSheet` for modal

---

### Task: Trip Sidebar Sliding Panel

**Priority:** P1
**Files:** `Views/Controls/TripSidebar.xaml`

**Requirements:**
- [ ] Slide-in from right edge
- [ ] Shows loaded trip places
- [ ] Swipe/button to dismiss
- [ ] Place selection triggers map center

**Syncfusion Components:**
- `SfNavigationDrawer` with `Position="Right"`

---

### Task: Segment Visualization

**Priority:** P1
**Files:** `Services/MapService.cs`

**Requirements:**
- [ ] Draw segments as polylines on map
- [ ] Color by transport mode (walk=blue, drive=green, transit=orange)
- [ ] Show when trip is loaded
- [ ] Clear when trip unloaded

---

### Task 5.3: Download Notification Service

**Priority:** P0
**Files:** `Platforms/Android/Services/DownloadNotificationService.cs`

**Requirements:**
- [ ] Progress notification during trip downloads
- [ ] Update progress percentage
- [ ] Completion notification
- [ ] Tap to open app
- [ ] Cancel action

---

### Task: Loading States

**Priority:** P2
**Files:** All pages

**Requirements:**
- [ ] `SfBusyIndicator` during data loading
- [ ] Consistent placement (centered overlay)
- [ ] Optional message text

**Pattern:**
```xaml
<sfBusy:SfBusyIndicator IsVisible="{Binding IsBusy}"
                        AnimationType="CircularMaterial"
                        Title="Loading..." />
```

---

## Syncfusion Component Reference

Per `DESIGN_SPEC.md`:

| Component | Usage |
|-----------|-------|
| `SfSwitch` | Toggle switches (settings) |
| `SfExpander` | Collapsible sections |
| `SfListView` | Lists with grouping |
| `SfBusyIndicator` | Loading states |
| `SfCircularProgressBar` | Download progress |
| `SfPopup` | Context menus, alerts |
| `SfBottomSheet` | Modal details |
| `SfNavigationDrawer` | Sliding panels |
| `SfNumericEntry` | PIN input |

---

## Development Standards (Reminder)

### MVVM Pattern (Mandatory)

```
View (XAML)
├── No business logic
├── Data binding only
├── Commands for user actions
└── No direct service calls

ViewModel
├── [ObservableProperty] for state
├── [RelayCommand] for actions
├── Calls services via injected interfaces
└── No UI framework references
```

### Code Checklist Before Completion

- [ ] Follows MVVM (no logic in code-behind)
- [ ] Uses dependency injection
- [ ] Has XML documentation
- [ ] Handles errors gracefully
- [ ] Uses async/await properly
- [ ] Uses Syncfusion components as specified

---

## Files to Create/Modify

### New Files

| File | Purpose |
|------|---------|
| `Views/LockScreenPage.xaml` | PIN lock overlay |
| `Views/Controls/PlaceContextMenu.xaml` | Place action menu |
| `Views/Modals/LocationDetailsModal.xaml` | Place details |
| `Views/Controls/TripSidebar.xaml` | Trip places panel |
| `Platforms/Android/Services/DownloadNotificationService.cs` | Download progress |

### Files to Modify

| File | Changes |
|------|---------|
| `Services/MapService.cs` | Route polyline, segment visualization |
| `App.xaml.cs` | Lock screen integration |
| `MauiProgram.cs` | Register new services |
| Various pages | Add `SfBusyIndicator` |

---

## Success Criteria

### UI Complete

- [ ] Navigation route visible on map during navigation
- [ ] Lock screen works on app resume
- [ ] Place context menu shows on tap
- [ ] Location details modal displays correctly
- [ ] Trip sidebar slides smoothly
- [ ] Segments visible on map
- [ ] Download progress shows in notifications
- [ ] Loading indicators on all async operations

### Production Ready

- [ ] All UI uses Syncfusion components per spec
- [ ] Smooth 60 FPS animations
- [ ] Consistent styling across app
- [ ] Works on Android and iOS

---

## Change Log

| Date | Change |
|------|--------|
| 2025-12-08 | Initial plan created |
| 2025-12-08 | Marked business logic complete, reorganized for UI focus |
