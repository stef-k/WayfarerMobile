# WayfarerMobile - Design Specification Document

**Purpose**: Comprehensive analysis and design guide for building WayfarerMobile from scratch with lessons learned from Wayfarer.Mobile.

**Status**: DRAFT - Iterating
**Last Updated**: 2025-12-07
**Target Framework**: .NET 10 only (no legacy support)

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Feature Analysis](#2-feature-analysis)
3. [Architecture Assessment](#3-architecture-assessment)
4. [UI/UX Assessment](#4-uiux-assessment)
5. [Technical Debt & Lessons Learned](#5-technical-debt--lessons-learned)
6. [New Architecture Design](#6-new-architecture-design)
7. [Technology Stack](#7-technology-stack)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Code Reuse Analysis](#9-code-reuse-analysis)
10. [Open Questions](#10-open-questions)
11. [Research Findings & External Resources](#11-research-findings--external-resources)
12. [Appendix: Code Snippets](#12-appendix-code-snippets-for-reference)
13. [User Remarks & Decisions](#13-user-remarks--decisions-2025-12-07-review)
14. [Docs Review & Corrections](#14-docs-review--corrections-2025-12-07)

---

## 1. Executive Summary

> **UPDATE (Section 14)**: After reviewing existing docs, the architecture assessment below may be too harsh. See [Section 14 - Docs Review & Corrections](#14-docs-review--corrections-2025-12-07) for important clarifications.

### 1.1 Why Rewrite?

The current Wayfarer.Mobile app has accumulated significant technical debt, particularly in the background tracking architecture. Key issues:

1. **Background tracking is fundamentally broken** - The Android foreground service is "pure plumbing" while the actual GPS tracking lives in a C# object that dies when the app process is killed.
   > **CORRECTION**: Architecture doc (13-Architecture.md) shows this design is INTENTIONAL - both components run in same process, service keeps process alive. Investigate actual bug before assuming architecture is wrong.

2. **MVVM is inconsistent** - Some ViewModels exist, but most logic remains in code-behind files.
   > **CORRECTION**: MVVM is ~80% complete per MVVM_MIGRATION_PLAN.md. Only ManualCheckInPage needs ViewModel.

3. **Permission handling is scattered** - No unified flow for first-run permissions.

4. **State management is complex** - Multiple boolean flags across different classes make reasoning about state difficult.

### 1.2 Goals for WayfarerMobile

- **Reliable 24/7 background tracking** - GPS logic lives in Android Service, survives app kill
- **Clean MVVM architecture** - ViewModels everywhere, testable code
- **Professional UI** - Syncfusion MAUI Toolkit for modern, consistent look
- **Proper permission flow** - Dedicated onboarding with clear explanations
- **Maintainable codebase** - Clear separation of concerns, documented patterns

### 1.3 Estimated Effort

| Approach | Effort | Risk |
|----------|--------|------|
| Continue patching current app | Ongoing | High - fundamental issues remain |
| Full rewrite from scratch | 4-6 weeks | Low - clean foundation |
| Hybrid (keep UI, rewrite services) | 3-4 weeks | Medium - integration complexity |

**Recommendation**: Full rewrite with ~50% code reuse from current app.

---

## 2. Feature Analysis

### 2.1 Core Features

| Feature | Current State | Assessment | Action |
|---------|---------------|------------|--------|
| **Background Location Tracking** | Broken architecture | CRITICAL - needs full redesign | Rewrite |
| **Live Map Display** | Works well with Mapsui | Keep approach, improve MVVM | Refactor |
| **Tile Caching** | Good implementation | Solid, keep most code | Reuse |
| **Trip Display** | Works well | Keep, clean up code | Refactor |
| **Timeline View** | Basic but functional | Needs UI improvement | Redesign UI |
| **Manual Check-ins** | Works | Keep | Reuse |
| **Server Sync** | Works but complex | Simplify queue logic | Refactor |
| **QR Configuration** | Works | Keep | Reuse |
| **Navigation (turn-by-turn)** | Works with audio | Keep | Reuse |
| **Groups/Live Location** | Read-only, works | Keep | Reuse |
| **PIN Lock Security** | Works | Keep | Reuse |
| **Offline Mode** | Works via tile cache | Keep | Reuse |

### 2.2 Feature Details

#### 2.2.1 Background Location Tracking (CRITICAL)

**Current Problems:**
- `TrackingForegroundService` is "pure plumbing" - just shows notification
- `EnhancedAndroidBackgroundTracker` does actual GPS work but is a C# object
- When app process is killed, tracker dies but service (notification) survives
- User sees notification but no tracking happens
- Complex adaptive polling logic is hard to debug

**New Design:**
```
LocationTrackingService (Android Service)
├── Owns LocationManager directly
├── Has its own timer for periodic updates
├── Handles ALL GPS acquisition
├── Writes directly to SQLite
├── Survives app process death
└── Communicates with UI via Broadcast/Messenger
```

**Key Principles:**
1. Service owns GPS - doesn't delegate to C# object
2. Service writes to SQLite directly - no dependency on app process
3. UI subscribes to location updates when visible
4. Clear state machine: NotStarted → Running → Paused → Stopped

#### 2.2.2 Live Map Display

**Current State - GOOD:**
- Mapsui integration works well
- Layer management for trip display is solid
- User location indicator works
- Tile caching is effective

**Issues:**
- MainPage.xaml.cs is ~3000 lines split across partials
- Mixed MVVM - some state in ViewModel, some in code-behind
- Multiple event handlers with similar logic

**New Design:**
- Pure MVVM with `MainMapViewModel`
- Map control interactions via commands
- Location updates via observable properties
- Clear separation: ViewModel for state, code-behind for map-specific APIs only

#### 2.2.3 Tile Caching

**Current State - GOOD:**
- `WayfarerCachedTileSource` works well
- Live cache + Trip cache separation is smart
- SQLite storage is efficient
- Rate limiting for downloads

**Keep:**
- Cache architecture
- Tile storage format
- Rate limiting logic

**Improve:**
- Move configuration to settings service
- Better progress reporting
- Cleaner error handling

#### 2.2.4 Trip Display & Sidebar

**Current State - GOOD:**
- `TripSidebar` control works well
- `TripMapDisplayService` manages layers effectively
- Place/segment display is clear

**Issues:**
- `TripSidebarViewModel` exists but not fully utilized
- Some state in code-behind

**New Design:**
- Full MVVM for sidebar
- `TripDisplayViewModel` for trip state
- Commands for all interactions

#### 2.2.5 Timeline View

**Current State - FUNCTIONAL:**
- Shows location history from server
- Map integration works
- Basic filtering

**Issues:**
- UI is basic/plain
- No grouping by day
- No rich interactions

**New Design with Syncfusion:**
- Use `SfListView` with grouping
- Day headers with summaries
- Pull-to-refresh
- Better visual design
- Activity type icons

#### 2.2.6 Settings Page

**Current State - FUNCTIONAL:**
- Uses DataTemplates for sections
- CollectionView with template selector
- Many settings organized in sections

**Issues:**
- Very long XAML file (900+ lines)
- All templates in one file
- Some sections are cluttered
- Inconsistent styling

**New Design with Syncfusion:**
- Split templates into separate files
- Use `SfExpander` for collapsible sections
- Cleaner visual hierarchy
- Better input controls

#### 2.2.7 Server Sync

**Current State - WORKS:**
- Queue-based sync for offline locations
- Check-in endpoint for forced sync
- Status tracking

**Issues:**
- Complex queue logic
- Multiple sync paths
- Hard to debug

**New Design:**
- Simplified sync service
- Clear queue management
- Better retry logic
- Observable sync status

#### 2.2.8 Navigation (Turn-by-turn)

**Current State - GOOD:**
- Audio announcements via TTS
- Visual overlay
- Route calculation
- Off-route detection

**Keep:**
- Navigation algorithm
- Audio service
- Route display

**Improve:**
- Cleaner ViewModel
- Better state management
- More responsive UI

---

## 3. Architecture Assessment

### 3.1 Current Architecture Problems

#### 3.1.1 Background Service Architecture (BROKEN)

```
CURRENT PROBLEM:

┌─────────────────────────────────────────────────────────────┐
│ TrackingForegroundService (Android Service)                 │
│ └─ Just shows notification, does NO GPS work                │
│     Survives app kill → notification shows                  │
└─────────────────────────────────────────────────────────────┘
                    ↑ survives
                    ↓ dies
┌─────────────────────────────────────────────────────────────┐
│ EnhancedAndroidBackgroundTracker (C# object)                │
│ └─ Has timer, does ALL GPS work                             │
│     Dies when app killed → tracking stops                   │
└─────────────────────────────────────────────────────────────┘

RESULT: User sees notification but tracking stopped!
```

#### 3.1.2 MVVM Inconsistency

| Component | ViewModel | Code-Behind Logic |
|-----------|-----------|-------------------|
| MainPage | Partial | Heavy (2500+ lines) |
| SettingsPage | Multiple VMs | Some event handlers |
| TimelinePage | TimelineViewModel | Some |
| TripManagerPage | TripManagerViewModel | Heavy |
| MyGroupsPage | MyGroupsViewModel | Some |

**Problem**: No clear pattern - some pages MVVM, some not.

#### 3.1.3 State Management

**Current**: Boolean flags scattered across classes
- `IsRunning` in tracker
- `TrackingEnabled` in SettingsStore
- `IsGpsRunning` in coordinator
- `HasGpsPermissions` in coordinator
- `_isTrackingEnabled` in ViewModel

**Problem**: Hard to know the "true" state at any moment.

#### 3.1.4 Permission Handling

**Current**: Scattered across multiple files
- `TrackingCoordinator.EnsureGpsActiveAsync()` - checks permissions
- `App.InitializeTrackingAsync()` - called from MainPage
- Various permission requests in different places

**Problem**: No unified first-run experience.

### 3.2 Current Architecture Strengths

| Component | Strength |
|-----------|----------|
| Tile caching | Well-designed, efficient |
| API client | Clean, works well |
| Database (SQLite) | Solid schema, works well |
| Geo algorithms | Accurate, well-tested |
| Mapsui integration | Good layer management |
| Navigation audio | TTS works well |

---

## 4. UI/UX Assessment

### 4.1 Page-by-Page Analysis

#### 4.1.1 MainPage (Live Map)

**Looks:**
- Clean map display ✓
- Floating action buttons in corner ✓
- Trip sidebar slides in ✓

**Issues:**
- Buttons could use more polish (shadows, animations)
- No visual feedback on button press
- Sidebar has no swipe-to-close gesture
- Toast notifications are basic

**Improvements:**
- Add Syncfusion `SfButton` with ripple effects
- Add swipe gestures for sidebar
- Better toast/snackbar (Syncfusion `SfPopup`)
- Loading states for map operations

#### 4.1.2 SettingsPage

**Looks:**
- Organized sections ✓
- Icons for each section ✓
- Info boxes with explanations ✓

**Issues:**
- Very long scroll
- All sections expanded - overwhelming
- Dark gray buttons don't stand out
- Entry fields lack visual distinction
- Switch controls are basic

**Improvements:**
- Use `SfExpander` for collapsible sections
- Better section headers
- Syncfusion form controls
- Cleaner button styling
- Visual grouping

#### 4.1.3 TimelinePage

**Looks:**
- Basic list of locations
- Map at top

**Issues:**
- Plain white list items
- No grouping by day
- No activity icons
- No rich interactions
- Map could be larger or collapsible

**Improvements:**
- Day grouping headers
- Activity type icons/colors
- Pull-to-refresh
- Swipe actions for items
- Collapsible map

#### 4.1.4 TripManagerPage

**Looks:**
- List of trips
- Download/delete actions

**Issues:**
- Plain list style
- Download progress could be better
- No search/filter

**Improvements:**
- Card-style trip items
- Better progress indicators
- Search functionality
- Filter by downloaded/available

#### 4.1.5 MyGroupsPage

**Looks:**
- Map with group member locations
- List of groups

**Issues:**
- Basic styling
- No group avatars
- Connection status unclear

**Improvements:**
- Group cards with member count
- Avatar circles for members on map
- Clear online/offline status
- Last seen timestamps

#### 4.1.6 Manual Check-in Page

**Looks:**
- Simple form

**Issues:**
- Basic controls
- No location preview

**Improvements:**
- Show mini map with pin
- Better activity type selector
- Photo attachment option
- Notes with rich text

### 4.2 Common UI Issues

| Issue | Impact | Solution |
|-------|--------|----------|
| Basic MAUI controls | App looks dated | Syncfusion MAUI Toolkit |
| No animations | Feels static | Add transitions, loading states |
| Inconsistent spacing | Unprofessional | Design system with tokens |
| Dark gray buttons | Low contrast | Primary color buttons |
| Plain lists | Boring | Card layouts, icons |
| No loading states | Confusing | Skeleton loaders, spinners |

### 4.3 Recommended Syncfusion Components

| Current | Replace With | Benefit |
|---------|--------------|---------|
| Switch | SfSwitch | Animations, custom colors |
| ListView | SfListView | Grouping, swipe, pull-refresh |
| Button | SfButton | Ripple effects, icons |
| Entry | SfTextInputLayout | Floating labels, validation |
| Picker | SfComboBox | Better styling, search |
| Slider | SfSlider | Labels, tooltips |
| - | SfExpander | Collapsible sections |
| - | SfPopup | Toast/dialog |
| - | SfBadgeView | Notification badges |
| - | SfChip | Tags, filters |
| - | SfSegmentedControl | Tab-like selection |

---

## 5. Technical Debt & Lessons Learned

### 5.1 Architecture Lessons

| Lesson | Impact | New Approach |
|--------|--------|--------------|
| Service can't be "pure plumbing" | Tracking dies with app | Service owns GPS |
| C# object in app process dies | Tracking unreliable | Service-based architecture |
| Scattered permission checks | Confusing UX | Unified permission manager |
| Mixed MVVM/code-behind | Hard to maintain | Strict MVVM everywhere |
| Boolean flags for state | Bug-prone | State machine pattern |
| No clear initialization order | Race conditions | Defined startup sequence |

### 5.2 Code Quality Lessons

| Lesson | Current Issue | New Approach |
|--------|---------------|--------------|
| MainPage.xaml.cs too large | 2500+ lines | Split into smaller ViewModels |
| SettingsPage.xaml too large | 900+ lines | Split templates into files |
| Commented-out code | Confusing | Remove, use git history |
| Inconsistent logging | Hard to debug | Structured logging levels |
| Magic numbers | Unclear intent | Named constants, config |

### 5.3 Android-Specific Lessons

| Lesson | Current Issue | New Approach |
|--------|---------------|--------------|
| StartForeground 5-sec timeout | Crashes on .NET 10 | Start immediately in OnStartCommand |
| Process death | Tracker dies | Service handles everything |
| Battery optimization | Service killed | Request battery exemption properly |
| POST_NOTIFICATIONS | Notification hidden | Request in permission flow |
| Background location | Stricter in Android 10+ | Explain why, request properly |

### 5.4 What Works Well (KEEP)

| Component | Reason |
|-----------|--------|
| Tile caching strategy | Efficient, well-designed |
| API client structure | Clean, extensible |
| SQLite database schema | Solid, normalized |
| Geo algorithms | Accurate calculations |
| Navigation audio | TTS integration works |
| Mapsui layer management | Clean layer handling |
| Rate limiting for tiles | Respectful to servers |
| QR code configuration | User-friendly setup |

---

## 6. New Architecture Design

### 6.1 Why Single-Component Service is Better

**Current Architecture Problem (2 components, same process):**

```
┌──────────────────────────────────────────────────────────────┐
│ App Process                                                   │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ TrackingForegroundService (Android Service)             │  │
│  │  - Shows notification                                   │  │
│  │  - Keeps process alive                                  │  │
│  │  - Does NOT own GPS                                     │  │
│  └────────────────────────────────────────────────────────┘  │
│                          │                                    │
│                          │ references                         │
│                          ▼                                    │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ EnhancedAndroidBackgroundTracker (C# object)            │  │
│  │  - Has Timer                                            │  │
│  │  - Owns GPS acquisition logic                           │  │
│  │  - Can be garbage collected?                            │  │
│  │  - Timer can stop for various reasons?                  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

**Even if they're in the same process, there are coordination issues:**
- Who starts the tracker?
- Who holds the reference to prevent GC?
- What happens if the service restarts but tracker initialization fails?
- What if timer stops due to exception?
- Who monitors the tracker's health?

**New Design (1 component, owns everything):**

```
┌──────────────────────────────────────────────────────────────┐
│ LocationTrackingService (Android Service)                     │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ OWNS DIRECTLY:                                          │  │
│  │  - LocationManager (GPS)                                │  │
│  │  - Timer for polling                                    │  │
│  │  - SQLite connection                                    │  │
│  │  - Quality/threshold filters                            │  │
│  │  - Notification                                         │  │
│  │  - State machine                                        │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  NO external dependencies, NO coordination needed            │
└──────────────────────────────────────────────────────────────┘
```

**Why this is fundamentally cleaner:**

| Aspect | Current (2 components) | New (1 component) |
|--------|----------------------|-------------------|
| **GPS Ownership** | Tracker owns GPS | Service owns GPS |
| **Lifecycle** | Service + Tracker coordinated | Service = everything |
| **State** | Flags scattered | State machine in service |
| **Failure Modes** | Many (coordination issues) | Few (self-contained) |
| **Debugging** | Check service AND tracker | Check service only |
| **Testing** | Hard to test coordination | Easy to test service |
| **GC Risk** | Tracker object could be collected | Service is OS-managed |
| **Restart** | Need to recreate tracker | Service restarts clean |

### 6.2 Do We Need Foreground Service?

**YES, absolutely.** Here's why:

| Without Foreground Service | With Foreground Service |
|---------------------------|------------------------|
| Android kills app after ~15 min | Service stays alive |
| Background location limited | Full location access |
| No notification = no tracking | User sees "Tracking active" |
| Doze mode stops everything | Foreground service exempt |
| User doesn't know status | User always knows |

**The foreground service IS the tracking service in the new design:**

```
LocationTrackingService extends Service
├── IS a foreground service (shows notification)
├── OWNS GPS directly (no delegation)
├── OWNS timer (for periodic updates)
├── OWNS SQLite (writes directly)
└── BROADCASTS to UI when visible
```

### 6.3 Complete App Flow: First Run

```
┌─────────────────────────────────────────────────────────────────────┐
│                         FIRST RUN                                    │
└─────────────────────────────────────────────────────────────────────┘

[App Install]
      │
      ▼
┌─────────────────┐
│ App.OnCreate    │  Check: IsFirstRun = true
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      ONBOARDING FLOW                                 │
│  ┌────────────────┐                                                  │
│  │ Welcome Screen │  "WayfarerMobile tracks your location 24/7"     │
│  └───────┬────────┘                                                  │
│          ▼                                                           │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Permission Step 1: Foreground Location                          │ │
│  │ "We need location access to show you on the map"                │ │
│  │ [Request ACCESS_FINE_LOCATION]                                  │ │
│  └───────┬────────────────────────────────────────────────────────┘ │
│          ▼                                                           │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Permission Step 2: Background Location                          │ │
│  │ "For 24/7 tracking, we need 'Allow all the time'"               │ │
│  │ [Request ACCESS_BACKGROUND_LOCATION]                            │ │
│  └───────┬────────────────────────────────────────────────────────┘ │
│          ▼                                                           │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Permission Step 3: Notifications (Android 13+)                  │ │
│  │ "To show tracking status, we need notification permission"      │ │
│  │ [Request POST_NOTIFICATIONS]                                    │ │
│  └───────┬────────────────────────────────────────────────────────┘ │
│          ▼                                                           │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Battery Optimization Exemption                                  │ │
│  │ "For reliable tracking, please disable battery optimization"    │ │
│  │ [Request IGNORE_BATTERY_OPTIMIZATIONS]                          │ │
│  └───────┬────────────────────────────────────────────────────────┘ │
│          ▼                                                           │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Server Setup (QR or Manual)                                     │ │
│  │ "Scan QR code or enter server URL and token"                    │ │
│  └───────┬────────────────────────────────────────────────────────┘ │
│          ▼                                                           │
│  Set: IsFirstRun = false, TrackingEnabled = true                    │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    START TRACKING SERVICE                            │
│  Intent: ACTION_START → LocationTrackingService                      │
│  Service: StartForeground(notification) + RequestLocationUpdates    │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│ MainPage (Map)  │  Subscribe to location broadcasts
└─────────────────┘
```

### 6.4 Complete App Flow: Subsequent Runs

```
┌─────────────────────────────────────────────────────────────────────┐
│                      SUBSEQUENT RUN                                  │
└─────────────────────────────────────────────────────────────────────┘

[App Launch]
      │
      ▼
┌─────────────────┐
│ App.OnCreate    │  Check: IsFirstRun = false
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Check: TrackingEnabled && HasAllPermissions?                         │
│                                                                      │
│   YES → Service already running (Sticky)? Just navigate to MainPage │
│         Service not running? Start service with ACTION_START         │
│                                                                      │
│   NO  → Navigate to Settings, show permission status                 │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│ MainPage (Map)  │
│                 │  OnAppearing:
│                 │   - Subscribe to location broadcasts
│                 │   - Send SET_HIGH_PERFORMANCE to service
│                 │
│                 │  OnDisappearing:
│                 │   - Unsubscribe from broadcasts
│                 │   - Send SET_NORMAL to service
└─────────────────┘
```

### 6.5 Service Lifecycle: Clear State Machine

```
                    ┌─────────────────────────┐
                    │    NOT_INITIALIZED      │
                    │  (Service not created)  │
                    └───────────┬─────────────┘
                                │
                         App sends ACTION_START
                                │
                                ▼
                    ┌─────────────────────────┐
     ┌──────────────│      STARTING           │
     │              │ - StartForeground()     │
     │              │ - Check permissions     │
     │              │ - Open SQLite           │
     │              │ - Create timer          │
     │              └───────────┬─────────────┘
     │                          │
     │                  Success │
     │                          ▼
     │              ┌─────────────────────────┐
     │              │        ACTIVE           │◄─────────────────┐
     │              │ - GPS requesting        │                  │
     │              │ - Timer running         │    ACTION_RESUME │
     │              │ - Writing to SQLite     │                  │
     │              │ - Notification: "ON"    │                  │
     │              └───────────┬─────────────┘                  │
     │                          │                                │
     │              ACTION_PAUSE│         ACTION_STOP            │
     │                          ▼                   │            │
     │              ┌─────────────────────────┐     │            │
     │              │        PAUSED           │─────┘            │
     │              │ - GPS stopped           │──────────────────┘
     │              │ - Timer stopped         │
     │              │ - Service still alive   │
     │              │ - Notification: "PAUSED"│
     │              └───────────┬─────────────┘
     │                          │
     │                  ACTION_STOP (from Paused)
     │                          │
     │                          ▼
     │              ┌─────────────────────────┐
     └─────────────►│       STOPPED           │
      (on error)    │ - StopForeground()      │
                    │ - StopSelf()            │
                    │ - Clean up resources    │
                    └─────────────────────────┘
```

### 6.6 Timeline Tracking vs Live Location (Important Distinction)

**Two Independent Concerns:**

```
┌─────────────────────────────────────────────────────────────────────┐
│                    TIMELINE TRACKING                                 │
│                    (Settings Toggle)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ Purpose: Log locations to server for user's timeline history        │
│                                                                      │
│ When ENABLED:                                                        │
│   - Service logs locations to SQLite queue                          │
│   - Queue syncs to server when online                               │
│   - Locations appear in user's timeline on web                      │
│   - Respects server thresholds (time/distance)                      │
│                                                                      │
│ When DISABLED:                                                       │
│   - NO logging to queue/server                                      │
│   - User's timeline stops updating                                  │
│   - GPS service STILL WORKS for live location/navigation            │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                    LIVE LOCATION / NAVIGATION                        │
│                    (Always Available)                                │
├─────────────────────────────────────────────────────────────────────┤
│ Purpose: Show user on map, provide navigation guidance              │
│                                                                      │
│ NOT affected by Timeline Tracking setting:                          │
│   - User location indicator on MainPage map                         │
│   - Navigation turn-by-turn directions                              │
│   - "Center on me" functionality                                    │
│   - Trip progress tracking                                          │
│                                                                      │
│ This uses GPS but does NOT log to server                            │
└─────────────────────────────────────────────────────────────────────┘
```

**How This Affects Service Design:**

```
LocationTrackingService:
├── GPS Acquisition (always available when service running)
│   ├── Broadcasts to UI for live location display
│   └── Used for navigation guidance
│
└── Timeline Logging (controlled by setting)
    ├── IF TimelineTrackingEnabled:
    │   ├── Apply time/distance thresholds
    │   ├── Write to SQLite queue
    │   └── Sync to server when online
    │
    └── IF TimelineTrackingEnabled = false:
        └── Skip logging, GPS still works for UI
```

**Service States with Timeline Setting:**

| Timeline Setting | Service Running | GPS Active | Logging | UI Location |
|-----------------|-----------------|------------|---------|-------------|
| ON | Yes | Yes | Yes | Yes |
| OFF | Yes (if app open) | Yes | NO | Yes |
| OFF | No (app closed) | No | NO | N/A |

**Key Principle**: Timeline Tracking controls SERVER LOGGING, not GPS functionality.

**When Does the Foreground Service Run?**

```
┌─────────────────────────────────────────────────────────────────────┐
│ Timeline Tracking = ON                                               │
├─────────────────────────────────────────────────────────────────────┤
│ → Foreground service runs 24/7                                       │
│ → Notification shows "Tracking active"                               │
│ → Logs to server even when app closed                               │
│ → Service survives app kill (Sticky)                                │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Timeline Tracking = OFF, App Open (MainPage visible)                 │
├─────────────────────────────────────────────────────────────────────┤
│ → Service starts for live location only                             │
│ → GPS active for map display                                        │
│ → NO logging to server                                              │
│ → Service stops when app closed (not Sticky)                        │
│ → OR: Use simple location requests without service                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│ Timeline Tracking = OFF, App Closed                                  │
├─────────────────────────────────────────────────────────────────────┤
│ → NO service running                                                │
│ → NO GPS activity                                                   │
│ → NO battery usage                                                  │
└─────────────────────────────────────────────────────────────────────┘
```

**Implementation Options for "OFF + App Open":**

| Option | Approach | Pros | Cons |
|--------|----------|------|------|
| A | Use same service, just skip logging | Simple, unified code | Service overhead when not needed |
| B | Use MAUI Geolocation API directly | No service needed | Different code path |
| C | Start service without Sticky flag | Consistent API | Slight overhead |

**Recommendation**: Option A (same service, skip logging) - keeps architecture simple.

### 6.7 Performance Mode Switching

The service has modes for "fast when using, slow when not":

```
┌─────────────────────────────────────────────────────────────────────┐
│                    PERFORMANCE MODES                                 │
│                                                                      │
│  HIGH_PERFORMANCE (1 second updates)                                 │
│  ─────────────────────────────────                                   │
│  When: MainPage visible, Navigation active                           │
│  GPS: High accuracy, frequent polling                                │
│  Purpose: Real-time map updates                                      │
│                                                                      │
│  NORMAL (server-configured: e.g., 60 seconds)                        │
│  ─────────────────────────────────────────────                       │
│  When: App backgrounded, other pages visible                         │
│  GPS: Balanced accuracy, respects server thresholds                  │
│  Purpose: Timeline logging, battery efficient                        │
│                                                                      │
│  POWER_SAVER (5 minutes)                                             │
│  ───────────────────────                                             │
│  When: Battery < 20%                                                 │
│  GPS: Reduced frequency                                              │
│  Purpose: Extend battery life                                        │
└─────────────────────────────────────────────────────────────────────┘

How UI communicates mode:
──────────────────────────

MainPage.OnAppearing():
    Send Intent(ACTION_SET_HIGH_PERFORMANCE) to service
    Subscribe to location broadcasts

MainPage.OnDisappearing():
    Send Intent(ACTION_SET_NORMAL) to service
    Unsubscribe from broadcasts

Service receives intent:
    Switch performance mode
    Recreate timer with new interval
    Update notification text
```

### 6.8 UI Subscribing to Service

```
┌──────────────────────────────────────────────────────────────────┐
│                LocationTrackingService                            │
│                                                                   │
│  onLocationChanged(Location location):                            │
│    // 1. Filter                                                   │
│    if (!passesQualityFilter(location)) return;                    │
│    if (!passesThresholdFilter(location)) return;                  │
│                                                                   │
│    // 2. Store (service owns database)                            │
│    database.insert(location);                                     │
│                                                                   │
│    // 3. Broadcast to UI (if anyone is listening)                 │
│    Intent broadcast = new Intent("LOCATION_UPDATE");              │
│    broadcast.putExtra("lat", location.latitude);                  │
│    broadcast.putExtra("lon", location.longitude);                 │
│    LocalBroadcastManager.send(broadcast);                         │
│                                                                   │
│    // 4. Update notification                                      │
│    updateNotification("Last: " + DateTime.Now);                   │
└──────────────────────────────────────────────────────────────────┘
                              │
                              │ LocalBroadcast
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│                    MAUI App (when visible)                        │
│                                                                   │
│  LocationBridge (singleton):                                      │
│    - Registered BroadcastReceiver                                 │
│    - Receives "LOCATION_UPDATE" broadcasts                        │
│    - Raises C# event: LocationReceived(location)                  │
│                                                                   │
│  MainMapViewModel:                                                │
│    - Subscribes to LocationBridge.LocationReceived                │
│    - Updates CurrentLocation property                             │
│    - Map binding updates automatically                            │
│                                                                   │
│  When UI not visible:                                             │
│    - BroadcastReceiver unregistered                               │
│    - No events raised                                             │
│    - Service continues logging to SQLite anyway                   │
└──────────────────────────────────────────────────────────────────┘
```

### 6.9 Clean Separation of Concerns

```
┌─────────────────────────────────────────────────────────────────────┐
│ LAYER 1: Android Service (Platform)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ LocationTrackingService.cs                                          │
│  - Pure Android code                                                │
│  - Owns GPS, Timer, SQLite                                          │
│  - Broadcasts locations                                             │
│  - State machine (Starting, Active, Paused, Stopped)                │
│  - Performance mode switching                                       │
│  - NO MAUI dependencies                                             │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ Broadcasts / Intents
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ LAYER 2: LocationBridge (Infrastructure)                            │
├─────────────────────────────────────────────────────────────────────┤
│ LocationBridge.cs                                                   │
│  - Wraps Android BroadcastReceiver                                  │
│  - Exposes C# events                                                │
│  - Sends commands to service                                        │
│  - Observable: CurrentLocation, TrackingState                       │
│  - Hides Android details from ViewModels                            │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ C# Events / Properties
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ LAYER 3: ViewModels (Presentation)                                   │
├─────────────────────────────────────────────────────────────────────┤
│ MainMapViewModel.cs                                                 │
│  - Subscribes to LocationBridge events                              │
│  - Commands: StartTracking, StopTracking, CenterOnUser              │
│  - Observable: CurrentLocation, IsTracking, TrackingStatus          │
│  - NO Android code, NO direct GPS                                   │
│                                                                      │
│ SettingsViewModel.cs                                                │
│  - TrackingEnabled toggle                                           │
│  - Permission status display                                        │
│  - Commands to request permissions                                  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ Bindings
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ LAYER 4: Views (UI)                                                  │
├─────────────────────────────────────────────────────────────────────┤
│ MainMapPage.xaml                                                    │
│  - Map control (Mapsui)                                             │
│  - Binds to ViewModel properties                                    │
│  - Only UI-specific code in code-behind                             │
└─────────────────────────────────────────────────────────────────────┘
```

### 6.10 Project Structure

```
WayfarerMobile/
├── Core/                           # Pure C#, no MAUI dependencies
│   ├── Models/
│   │   ├── Location.cs
│   │   ├── Trip.cs
│   │   ├── Place.cs
│   │   ├── Segment.cs
│   │   ├── Group.cs
│   │   └── ...
│   ├── Interfaces/
│   │   ├── ILocationStore.cs
│   │   ├── ISettingsService.cs
│   │   ├── IApiClient.cs
│   │   └── ...
│   ├── Algorithms/
│   │   ├── GeoMath.cs
│   │   ├── DistanceCalculator.cs
│   │   ├── ThresholdFilter.cs
│   │   └── BearingCalculator.cs
│   └── Enums/
│       ├── TrackingState.cs
│       ├── SyncStatus.cs
│       └── ...
│
├── Infrastructure/                  # Platform-agnostic implementations
│   ├── Database/
│   │   ├── SqliteLocationStore.cs
│   │   ├── SqliteTripStore.cs
│   │   └── DatabaseService.cs
│   ├── Api/
│   │   ├── WayfarerApiClient.cs
│   │   ├── TimelineApiService.cs
│   │   └── ...
│   └── Services/
│       ├── SettingsService.cs
│       ├── SyncService.cs
│       └── ...
│
├── Platforms/
│   ├── Android/
│   │   ├── Services/
│   │   │   ├── LocationTrackingService.cs   # THE service (owns everything)
│   │   │   └── LocationBridge.cs            # Broadcasts ↔ C# events
│   │   ├── Permissions/
│   │   │   └── AndroidPermissionManager.cs
│   │   └── MainActivity.cs
│   └── iOS/
│       ├── Services/
│       │   └── iOSLocationManager.cs
│       └── ...
│
├── Features/                        # Feature-based organization
│   ├── Map/
│   │   ├── MainMapPage.xaml
│   │   ├── MainMapViewModel.cs
│   │   └── Controls/
│   ├── Timeline/
│   │   ├── TimelinePage.xaml
│   │   ├── TimelineViewModel.cs
│   │   └── TimelineItemTemplate.xaml
│   ├── Trips/
│   │   ├── TripManagerPage.xaml
│   │   ├── TripManagerViewModel.cs
│   │   └── TripDetailPage.xaml
│   ├── Settings/
│   │   ├── SettingsPage.xaml
│   │   ├── SettingsViewModel.cs
│   │   └── Sections/
│   ├── Groups/
│   │   ├── GroupsPage.xaml
│   │   └── GroupsViewModel.cs
│   ├── CheckIn/
│   │   ├── CheckInPage.xaml
│   │   └── CheckInViewModel.cs
│   └── Onboarding/
│       ├── OnboardingPage.xaml
│       ├── OnboardingViewModel.cs
│       └── PermissionFlowService.cs
│
├── Shared/
│   ├── Controls/
│   │   ├── MapView.xaml
│   │   ├── LoadingIndicator.xaml
│   │   └── ...
│   ├── Converters/
│   ├── Styles/
│   │   ├── Colors.xaml
│   │   ├── Typography.xaml
│   │   └── Controls.xaml
│   └── Extensions/
│
├── App.xaml.cs                      # Minimal, just DI setup
└── MauiProgram.cs                   # DI configuration
```

### 6.11 State Machine for Tracking (Code)

```csharp
public enum TrackingState
{
    NotInitialized,      // App just installed
    PermissionsNeeded,   // Need to request permissions
    PermissionsDenied,   // User denied, show explanation
    Ready,               // Has permissions, not tracking
    Starting,            // Transitioning to active
    Active,              // GPS running, logging locations
    Paused,              // User paused, service alive but not logging
    Stopping,            // Transitioning to stopped
    Error                // Something went wrong
}
```

---

## 7. Technology Stack

### 7.1 Core Technologies

| Category | Technology | Version | Notes |
|----------|------------|---------|-------|
| Framework | .NET 10 | 10.0 | No legacy support |
| UI | .NET MAUI | 10.0 | Single project |
| Maps | Mapsui | 5.0.0 | Keep current |
| UI Components | Syncfusion MAUI Toolkit | Latest | Open source, MIT |
| Database | SQLite-net-pcl | Latest | Keep current |
| MVVM | CommunityToolkit.Mvvm | Latest | Keep current |
| QR | ZXing.Net.MAUI | Latest | Keep current |
| HTTP | System.Net.Http | Built-in | Standard |

### 7.2 Syncfusion Components to Use

| Component | Use Case |
|-----------|----------|
| SfListView | Timeline, Trip list, Groups |
| SfExpander | Settings sections |
| SfSwitch | All toggles |
| SfButton | All buttons |
| SfTextInputLayout | Form inputs |
| SfComboBox | Pickers |
| SfSlider | Volume, radius settings |
| SfPopup | Toasts, dialogs |
| SfBadgeView | Sync queue count |
| SfChip | Tags, filters |
| SfSegmentedControl | Tab selection |
| SfBusyIndicator | Loading states |

---

## 8. Implementation Roadmap

### Phase 1: Foundation (Week 1)

- [ ] Create new WayfarerMobile solution
- [ ] Set up project structure (Core, Infrastructure, Features)
- [ ] Configure DI container
- [ ] Set up Syncfusion MAUI Toolkit
- [ ] Create base styles (Colors, Typography)
- [ ] Migrate Core models from current app
- [ ] Set up SQLite database

### Phase 2: Location Service (Week 1-2)

- [ ] Design LocationTrackingService for Android
- [ ] Implement service lifecycle (Start, Pause, Stop)
- [ ] Implement GPS acquisition in service
- [ ] Implement quality filtering in service
- [ ] Implement threshold filtering in service
- [ ] Implement SQLite writes in service
- [ ] Implement notification management
- [ ] Create LocationBridge for MAUI communication
- [ ] Test survival of app kill

### Phase 3: Permission Flow (Week 2)

- [ ] Create OnboardingPage
- [ ] Create PermissionFlowService
- [ ] Implement permission request sequence
- [ ] Handle permission denial gracefully
- [ ] Implement server configuration (QR + manual)
- [ ] Store first-run completion flag

### Phase 4: Main Map (Week 2-3)

- [ ] Create MainMapPage with Mapsui
- [ ] Create MainMapViewModel
- [ ] Implement location indicator
- [ ] Implement tile caching (migrate from current)
- [ ] Implement floating action buttons
- [ ] Implement toast notifications
- [ ] Implement performance mode switching

### Phase 5: Trip Display (Week 3)

- [ ] Migrate TripMapDisplayService
- [ ] Create trip sidebar control
- [ ] Create TripSidebarViewModel
- [ ] Implement place/segment display
- [ ] Implement trip download
- [ ] Implement offline mode

### Phase 6: Timeline (Week 3-4)

- [ ] Create TimelinePage with SfListView
- [ ] Create TimelineViewModel
- [ ] Implement day grouping
- [ ] Implement pull-to-refresh
- [ ] Implement map integration
- [ ] Add activity type icons

### Phase 7: Settings (Week 4)

- [ ] Create SettingsPage with SfExpander
- [ ] Split into section ViewModels
- [ ] Migrate all settings functionality
- [ ] Improve diagnostic tools
- [ ] Improve cache management UI

### Phase 8: Additional Features (Week 4-5)

- [ ] Trip Manager page
- [ ] Groups page
- [ ] Manual check-in page
- [ ] Navigation (turn-by-turn)
- [ ] QR scanner

### Phase 9: Polish & Testing (Week 5-6)

- [ ] Test all features
- [ ] Fix bugs
- [ ] Add animations
- [ ] Performance optimization
- [ ] Final UI polish

---

## 9. Code Reuse Analysis

### 9.1 Direct Reuse (80%+ usable)

| Component | Files | Notes |
|-----------|-------|-------|
| Models | `Models/*.cs` | Minor updates for new patterns |
| Geo algorithms | `GeoMath.cs`, distance calcs | Direct copy |
| API DTOs | `Models/Dto/*.cs` | Direct copy |
| Tile caching | `WayfarerCachedTileSource.cs` | Refactor slightly |
| Database schema | Table structures | Keep same |
| Navigation audio | `NavigationAudioService.cs` | Minor refactor |
| QR parsing | `QrScannerService.cs` | Direct copy |

### 9.2 Refactor & Reuse (50-80% usable)

| Component | Files | Changes Needed |
|-----------|-------|----------------|
| API client | `WayfarerHttpService.cs` | Interface-based |
| Database service | `WayfarerDatabaseService.cs` | Split responsibilities |
| Settings store | `SettingsStore.cs` | Service-based |
| Trip display | `TripMapDisplayService.cs` | MVVM |
| Timeline service | `TimelineService.cs` | Simplify |
| Sync logic | `LocationSyncService.cs` | Move to service |

### 9.3 Rewrite (< 50% usable)

| Component | Reason |
|-----------|--------|
| Background tracking | Fundamental architecture change |
| Permission handling | Need unified flow |
| App lifecycle | New initialization pattern |
| MainPage | Full MVVM rewrite |
| SettingsPage | New UI components |

---

## 10. Open Questions

### 10.1 Design Decisions Needed

- [x] **Onboarding flow**: Multi-step wizard (permission flow)
- [x] **Settings organization**: Collapsible sections with SfExpander
- [x] **Timeline grouping**: By day, expandable design for future
- [x] **Trip sidebar**: BOTH swipe gesture AND button toggle
- [x] **Map buttons**: Syncfusion Circular buttons (FAB style)
- [x] **Color scheme**: Keep current + light/dark mode option using Wayfarer colors
- [x] **Offline indicator**: Main menu indicator + Toast for view-specific

### 10.2 Technical Decisions Needed

- [x] **iOS tracking**: Same design tailored for iOS rules
- [x] **Windows support**: NO - Android and iOS only
- [x] **Database migration**: NOT NEEDED - Fresh start
- [x] **API versioning**: Current API sufficient, document changes separately
- [x] **Tile format**: Keep current (raster MBTiles), support multiple providers

### 10.3 Items to Research

- [x] Syncfusion MAUI Toolkit - MIT licensed, 30+ controls, well documented
- [ ] Android 15 background service changes
- [ ] iOS background location best practices
- [ ] MAUI performance optimization patterns

### 10.4 Remaining Open Items

- [ ] **Onboarding UI Design**: Specific screens and flow
- [ ] **Advanced Settings**: Which GPS settings to expose to power users?
- [ ] **Light/Dark Theme**: Exact color palettes based on Wayfarer icon

---

## 13. User Remarks & Decisions (2025-12-07 Review)

### 13.1 Syncfusion Components

**DECISION**: Use Syncfusion components to replace similar UI elements throughout the app.

| Current Element | Replace With | Location |
|-----------------|--------------|----------|
| MAUI Switch | SfSwitch | All settings toggles |
| MAUI Entry | SfTextInputLayout | All text inputs |
| MAUI Picker | SfComboBox | Dropdowns |
| Numeric Entry | SfNumericEntry | Number inputs |
| CollectionView | SfListView | Timeline, Trips, Groups |
| Manual expander | SfExpander | Settings sections, Trip sidebar |
| - | SfCharts | Statistics (future) |
| - | SfShimmer | Loading states |
| - | SfCircularButton | FAB replacement |

### 13.2 GPS Accuracy & Responsiveness

**EXISTING CODE ANALYSIS:**

The current codebase already has sophisticated GPS handling:

1. **LocationFusionEngine** (`Services/Location/LocationFusionEngine.cs`)
   - Fuses multiple location providers with weighted averaging
   - Filters stale network locations (>30s old)
   - Provider weighting: GPS (1.0) > Fused (0.95) > Passive (0.85) > Network (0.65)
   - Accuracy bonuses: <5m (+80%), <10m (+40%), <20m (+10%), >50m (-40%)

2. **GpsAccuracyFilter** (`Services/Location/GpsAccuracyFilter.cs`)
   - Transportation mode detection (Walking, Cycling, Driving, Train, Air)
   - Mode-specific jump limits and speed limits
   - Outlier rejection based on detected mode

3. **BearingStabilityTracker** (`Services/Location/BearingStabilityTracker.cs`)
   - Uses accelerometer to detect movement vs stationary
   - Variance-based stability detection

4. **FusedLocationProvider** - NOT IMPLEMENTED (stub only)
   - Would require Google Play Services NuGet packages

**IMPROVEMENTS FOR WAYFARERMOBILE:**

| Improvement | Description | Reference |
|-------------|-------------|-----------|
| **Implement FusedLocationProviderClient** | Use Google Play Services for optimal Android location | [Google Fused Location](https://developers.google.com/location-context/fused-location-provider) |
| **Kalman Filter for Smoothing** | Reduce GPS jitter and jumps | [Research Paper](https://www.researchgate.net/publication/304371469) |
| **Low-Pass Filter for Heading** | Smooth compass/bearing data | [Algorithm](http://blog.thomnichols.org/2011/08/smoothing-sensor-data-with-a-low-pass-filter) |
| **Performance Mode Switching** | Already exists, ensure clean implementation |
| **Request Last Known Location First** | Show cached location while waiting for GPS fix |

**PERFORMANCE MODES:**

| Mode | Polling Interval | Use Case |
|------|------------------|----------|
| High Performance | 1 second | MainPage visible, navigation active |
| Normal | Based on server settings | App in background |
| Power Saver | 5 minutes | Low battery |

### 13.3 Server Queue Design

**CURRENT SERVER API:**

The server expects `GpsLoggerLocationDto`:
```csharp
public class GpsLoggerLocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }  // Local device time
    public double? Accuracy { get; set; }
    public double? Altitude { get; set; }
    public double? Speed { get; set; }
    public string? LocationType { get; set; }
    public string? Notes { get; set; }
    public int? ActivityTypeId { get; set; }
}
```

**SERVER SETTINGS (`ApiSettingsDto`):**
- `LocationTimeThresholdMinutes` - Minimum time between logged locations
- `LocationDistanceThresholdMeters` - Minimum distance between logged locations

**QUEUE DESIGN PRINCIPLES:**

1. **Server Authority** - App respects server's time/distance thresholds
2. **Offline Capable** - Queue locations to SQLite when offline
3. **Sync on Reconnect** - Batch sync when connection restored
4. **Future Extensible** - Design for additional data fields

**PROPOSED EXTENDED DTO (for future):**
```csharp
public class ExtendedLocationDto : GpsLoggerLocationDto
{
    // Device Info (future)
    public string? DeviceModel { get; set; }
    public string? DeviceManufacturer { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }

    // Enhanced Location (future)
    public double? Bearing { get; set; }
    public double? BearingAccuracy { get; set; }
    public double? VerticalAccuracy { get; set; }
    public int? SatelliteCount { get; set; }

    // Battery (future)
    public double? BatteryLevel { get; set; }
    public bool? IsCharging { get; set; }
}
```

**NOTE**: Server API changes will be documented separately. Current API is sufficient for initial implementation.

### 13.4 UI/UX Decisions

#### 13.4.1 Trip Sidebar
- **DECISION**: Use **SfExpander** for collapsible sections
- Already works well, enhance with Syncfusion components

#### 13.4.2 Trips View
- **DECISION**: Use **Bottom Sheets** for features like trip details, download options
- Google Maps style bottom sheet pattern

#### 13.4.3 Timeline Location Edits
- **ISSUE**: Current edit experience is amateur
- **DECISION**: Follow Trip editing pattern which is better
- Use bottom sheet or modal for editing
- Show mini-map preview of location
- Proper date/time picker
- Activity type selector with icons

#### 13.4.4 Settings UI
- **DECISION**: Use Syncfusion input components:
  - `SfTextInputLayout` for text entries
  - `SfNumericEntry` for numbers
  - `SfSwitch` for toggles
  - `SfExpander` for collapsible sections

#### 13.4.5 Groups Page
- **DECISION**: NO avatars needed
- Keep simple list of group members
- Focus on location display on map

#### 13.4.6 Map Buttons
- **DECISION**: Use **SfCircularButton** (Syncfusion FAB) to replace current FABs
- Consistent styling across app

### 13.5 Location Services Refactoring

**CURRENT ISSUES:**
- Too much code to fuse providers
- Settings scattered across multiple files
- Complex configuration

**REFACTORING PLAN:**

1. **Consolidate Settings**
   - Move all location-related settings to single `LocationConfig` class
   - Expose only necessary settings to users (if any)

2. **Simplify Provider Architecture**
   - Service owns GPS directly (no delegation to C# object)
   - Single source of truth for location

3. **Advanced Settings Page**
   - TBD: May expose some settings for power users
   - Example: GPS accuracy threshold, update frequency override

### 13.6 Design Decisions (RESOLVED)

| Decision | Answer |
|----------|--------|
| **Timeline grouping** | By day, with expandable design for future (e.g., by activity type) |
| **Trip sidebar** | **BOTH** swipe gesture AND button toggle |
| **Map buttons** | Use Syncfusion Circular buttons (FAB style) |
| **Color scheme** | Keep current + offer light/dark mode in settings using Wayfarer colors from app icon |
| **Offline indicator** | Main menu indicator for device offline; Toast for view-specific online requirements (Timeline, Groups) |

### 13.7 Technical Decisions (RESOLVED)

| Decision | Answer |
|----------|--------|
| **iOS tracking** | Same design tailored for iOS rules; unified approach |
| **Windows support** | **NO** - Android and iOS only |
| **Database migration** | **NOT NEEDED** - Starting fresh |
| **API versioning** | Current API is sufficient; document any server changes separately |
| **Tile formats** | Support multiple providers via settings (OSM default); See Mapsui analysis below |

### 13.8 Mapsui Tile Format Analysis

**CURRENT SUPPORT (Mapsui 5.0):**
- [MBTiles (Raster)](https://mapsui.com/v5/) via BruTile.MBTiles
- OpenStreetMap tiles
- WMS, WFS, WMTS standards

**VECTOR TILES:**
- NOT natively supported in Mapsui core
- Third-party library available: [Mapsui.VectorTiles](https://github.com/charlenni/Mapsui.VectorTiles)
- Supports Mapbox Vector Tile (MVT) format

**RECOMMENDATION:**
- Continue with raster MBTiles for offline (current approach works)
- Vector tiles could be future enhancement for better zoom quality
- Keep tile caching architecture (WayfarerCachedTileSource) - it works well

---

## Document History

| Date | Author | Changes |
|------|--------|---------|
| 2025-12-07 | Claude | Initial draft - focus on tracking architecture |
| 2025-12-07 | Claude | Added research findings (GPS, Syncfusion, MVVM, etc.) |
| 2025-12-07 | User/Claude | Added user remarks and design decisions |
| 2025-12-07 | Claude | Docs review - compared against all /docs/ files |
| 2025-12-07 | User/Claude | Expanded Section 6 with detailed architecture explanation |

---

## 14. Docs Review & Corrections (2025-12-07)

### 14.1 Architecture Assessment Correction

**CRITICAL FINDING**: After reviewing `docs/13-Architecture.md`, the existing architecture may not be as "broken" as initially assessed.

**What the Architecture Doc Says:**
- `TrackingForegroundService` and `EnhancedAndroidBackgroundTracker` run in the **SAME PROCESS**
- When the foreground service stays alive (Sticky), the app process stays alive
- The tracker continues running because it's in the same process as the service
- This was **INTENTIONAL design**, not an accident

**From 13-Architecture.md:**
```
Process killed → Android restarts foreground service (Sticky)
Service destroyed → EnhancedAndroidBackgroundTracker destroyed with it
```

**The diagram shows:**
```
When app closed + Tracking ON:
→ Service STAYS ALIVE
→ App PROCESS stays alive (foreground service prevents kill)
→ EnhancedAndroidBackgroundTracker keeps running
→ Location updates continue ✅
```

**REVISED ASSESSMENT:**
- The architecture is not "fundamentally broken" - it was designed to work this way
- The REAL issue may be specific bugs in implementation, not architecture
- Need to investigate: WHY does tracking stop if the design says it should work?
- Possible causes: service not Sticky, process killed anyway, timer issues, etc.

**RECOMMENDATION**: Before rewriting, investigate the actual failure mode. The existing architecture SHOULD work according to its own documentation.

### 14.2 MVVM Status Correction

**INCORRECT**: Design doc says "Some ViewModels exist, but most logic remains in code-behind files"

**ACTUAL STATUS** (from `docs/archive/MVVM_MIGRATION_PLAN.md`):

| Phase | Status | ViewModel |
|-------|--------|-----------|
| Phase 1 - TimelinePage | ✅ COMPLETED | TimelineViewModel (713 lines) |
| Phase 2 - MyGroupsPage | ✅ COMPLETED | MyGroupsViewModel (570 lines) |
| Phase 3 - MainPage | ✅ COMPLETED | MainPageViewModel (392 lines) |
| Phase 4 - ManualCheckInPage | ❌ NOT DONE | Needs ViewModel |

**Already MVVM:**
- SettingsPage → SettingsPageViewModel + 8 sub-VMs
- TripSidebar → TripSidebarViewModel
- TripManagerPage → TripManagerViewModel
- PublicTripsBrowserPage → PublicTripsBrowserViewModel
- NavigationOverlay → NavigationOverlayViewModel

**CORRECTION**: MVVM is ~80% complete, not "inconsistent". Only ManualCheckInPage needs ViewModel extraction.

### 14.3 Missing Features/Details

**From Existing Docs Not Reflected:**

1. **Adaptive GPS Strategy** (`docs/archive/adaptive-gps-implementation-plan.md`)
   - Detailed plan exists for single adaptive background service
   - Performance mode switching (1s high perf, 60s normal)
   - `BackgroundLocationServiceConfig.SetPerformanceMode()` pattern
   - LiveLocationService removal already planned

2. **Groups Feature** (`docs/archive/Groups.md`)
   - SSE live updates for Today view
   - Per-user color mapping (deterministic from username hash)
   - Legend with "Only this" and "Select All/None"
   - Backend gaps documented (SSE auth, heartbeats, etc.)

3. **50-Meter Rule for Navigation** (`docs/04-Using-Trips-and-Offline.md`)
   - Segment routing activates when within ~50m of a trip place
   - Otherwise direct navigation from current GPS
   - This threshold aligns with typical GPS accuracy

4. **A* Pathfinding** (`docs/archive/Trip GPS Navigation System...`)
   - Trip-based routing graph using Places + Segments
   - No PBF/Itinero dependency
   - Walking distance optimization

5. **Phase 5 Hybrid Caching** (`docs/archive/phase5_hybrid_caching_design.md`)
   - `UnifiedTileCacheService` design
   - Priority: Trip → Live → Download
   - Trip boundary detection
   - Storage management (2GB default)

6. **Syncfusion Already In Progress** (`docs/archive/UI revamp.md`)
   - UI revamp branch already exists
   - Modal replacements in progress
   - ComboBox replacements documented
   - Per-page refactoring approach

### 14.4 Items Already Well-Documented

The following are well-documented in existing docs and should be referenced:

| Topic | Document |
|-------|----------|
| Background tracking architecture | `docs/13-Architecture.md` |
| Adaptive GPS strategy | `docs/archive/adaptive-gps-implementation-plan.md` |
| Groups feature design | `docs/archive/Groups.md` |
| Navigation system | `docs/archive/Trip GPS Navigation System...` |
| Hybrid tile caching | `docs/archive/phase5_hybrid_caching_design.md` |
| MVVM migration status | `docs/archive/MVVM_MIGRATION_PLAN.md` |
| UI revamp approach | `docs/archive/UI revamp.md` |
| Phase status | `docs/archive/TEST-REFACTOR-UPGRADE/PHASE_STATUS_REPORT.md` |

### 14.5 Final Recommendation: New Architecture is Still Better

**CLARIFICATION**: While the existing architecture doc shows the current design was intentional, the NEW architecture (Section 6) is still fundamentally cleaner and recommended for the following reasons:

**Why New Design is Superior (even if current "works"):**

| Issue | Current (2 components) | New (1 component) |
|-------|------------------------|-------------------|
| **Coordination** | Service + Tracker must coordinate | No coordination needed |
| **GC Risk** | C# tracker object can be collected | Service is OS-managed |
| **Failure Modes** | Many (timer stops, reference lost, etc.) | Few (self-contained) |
| **Debugging** | Check service AND tracker AND their connection | Check service only |
| **State** | Boolean flags scattered across classes | Single state machine |
| **Restart** | Must recreate tracker correctly | Service restarts clean |
| **Testing** | Hard to test coordination logic | Easy to test service |

**The Key Insight:**
- Current design TRIES to achieve reliability by keeping components in same process
- But this adds coordination overhead that creates subtle failure modes
- New design has ONE source of truth: the service IS the tracker
- Nothing to coordinate, nothing to lose reference to, nothing to get out of sync

**Decision: Proceed with New Architecture**

The new architecture (Section 6) should be implemented because:

1. **Cleaner separation of concerns** - Service owns GPS, UI subscribes via broadcasts
2. **Single source of truth** - No coordination between service and tracker
3. **Clear state machine** - Easy to understand and debug tracking state
4. **Proper app flow** - First run onboarding → permissions → service start
5. **Performance mode switching** - Clean mechanism for fast/slow updates
6. **Better testability** - Service can be tested in isolation

**What to Reuse from Current Codebase:**
- Tile caching (~80% reusable)
- API client and DTOs (~80% reusable)
- Geo algorithms (100% reusable)
- Database schema (keep same)
- Navigation audio (minor refactor)
- QR parsing (100% reusable)
- MVVM patterns established (adapt to new structure)

---

## Next Steps

1. **Create new WayfarerMobile solution** - Fresh start with new architecture
2. **Implement LocationTrackingService** - Single-component service that owns GPS, timer, SQLite
3. **Implement LocationBridge** - Broadcasts to C# events bridge
4. **Create Onboarding flow** - Permission wizard for first run
5. **Port reusable components** - Tile caching, API client, geo algorithms, navigation
6. **Build UI with Syncfusion** - Apply new UI components from start

---

## 11. Research Findings & External Resources

### 11.1 Android Background Location Best Practices

**Key Sources:**
- [Google Navigation SDK - Background Location Usage](https://developers.google.com/maps/documentation/navigation/android-sdk/background-location-usage)
- [Android Developers - Background Location Limits](https://developer.android.com/about/versions/oreo/background-location-limits)
- [Android Developers - Request Location Permissions](https://developer.android.com/develop/sensors-and-location/location/permissions)
- [Medium - Beyond the Foreground: Location Tracking That Actually Works](https://medium.com/@shubya8451/beyond-the-foreground-how-to-build-location-tracking-that-actually-works-in-the-background-ae86a22488f0)

**Key Findings:**

1. **Foreground Service is Required** - Android requires foreground services for reliable background location tracking. WorkManager has a 15-minute minimum interval and is unsuitable for real-time tracking.

2. **Android 14+ Changes** - Starting with Android 14, apps must have `ACCESS_BACKGROUND_LOCATION` permission and declare `android:foregroundServiceType="location"`.

3. **Minimize Scope** - Request minimum necessary permissions (coarse before fine, foreground before background).

4. **OEM Battery Optimization** - Samsung, Xiaomi, OnePlus, Huawei have aggressive battery optimization that can kill foreground services. Request battery optimization exemption and test on multiple OEMs.

5. **Doze Mode** - Foreground services are NOT affected by Doze mode and continue running. This is the recommended approach for location tracking.

**Required Permissions:**
```xml
<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
<uses-permission android:name="android.permission.ACCESS_BACKGROUND_LOCATION" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_LOCATION" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" /> <!-- Android 13+ -->
```

**Battery Optimization Exemption:**
```csharp
// Request user to disable battery optimization for reliable tracking
if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
{
    var pm = (PowerManager)GetSystemService(PowerService);
    if (!pm.IsIgnoringBatteryOptimizations(PackageName))
    {
        var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations);
        intent.SetData(Android.Net.Uri.Parse("package:" + PackageName));
        StartActivity(intent);
    }
}
```

### 11.2 Syncfusion MAUI Toolkit

**Key Sources:**
- [Syncfusion MAUI Toolkit (Official)](https://www.syncfusion.com/net-maui-toolkit)
- [GitHub - syncfusion/maui-toolkit](https://github.com/syncfusion/maui-toolkit)
- [Syncfusion Blog - Open Source .NET MAUI Controls](https://www.syncfusion.com/blogs/post/syncfusion-open-source-net-maui-controls-cross-platform)
- [Syncfusion Documentation](https://help.syncfusion.com/maui-toolkit/)

**Key Findings:**

1. **MIT Licensed** - 100% free, no licensing fees, unrestricted use for personal and commercial projects.

2. **30+ Open Source Controls** - Charts, Carousel, Navigation Drawer, Accordion, Cards, Expander, Shimmer, Pull to Refresh, and more.

3. **Installation:**
```xml
<PackageReference Include="Syncfusion.Maui.Toolkit" Version="*" />
```

4. **Setup in MauiProgram.cs:**
```csharp
builder.ConfigureSyncfusionToolkit();
```

**Recommended Controls for WayfarerMobile:**

| Control | Use Case in WayfarerMobile |
|---------|---------------------------|
| SfExpander | Settings page collapsible sections |
| SfListView | Timeline, Trip list, Groups |
| SfSwitch | All toggle switches |
| SfCharts | Statistics, trip analytics |
| SfCards | Trip cards, place cards |
| SfShimmer | Loading placeholders |
| SfPullToRefresh | Timeline, trip list refresh |

### 11.3 MVVM Best Practices with CommunityToolkit

**Key Sources:**
- [Microsoft Learn - MVVM Toolkit Features](https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm-community-toolkit-features)
- [Microsoft Learn - Introduction to MVVM Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [Telerik Blog - Using MVVM Toolkit in .NET MAUI](https://www.telerik.com/blogs/using-mvvm-toolkit-net-maui-applications)
- [MindStick - Building Scalable Applications with .NET MAUI 2025](https://www.mindstick.com/articles/339165/building-scalable-applications-with-dot-net-maui-best-practices-for-2025)

**Key Patterns:**

1. **ObservableProperty Attribute** - Auto-generates property changed notifications:
```csharp
[ObservableProperty]
private string _userName;
// Generates: public string UserName { get; set; } with INotifyPropertyChanged
```

2. **RelayCommand Attribute** - Auto-generates commands:
```csharp
[RelayCommand]
private async Task LoadDataAsync()
{
    // Command logic
}
// Generates: public IAsyncRelayCommand LoadDataCommand
```

3. **AsyncRelayCommand** - Prevents concurrent execution by default, great for preventing double-taps.

4. **IMessenger** - Replacement for MessagingCenter, use for cross-ViewModel communication:
```csharp
WeakReferenceMessenger.Default.Send(new LocationUpdatedMessage(location));
```

5. **DI Registration:**
```csharp
builder.Services.AddTransient<MainMapPage>();
builder.Services.AddTransient<MainMapViewModel>();
```

### 11.4 UI/UX Design Inspiration

**Design Resources:**
- [Dribbble - Location Tracker Designs](https://dribbble.com/tags/location-tracker)
- [Dribbble - Trip Planner App](https://dribbble.com/tags/trip-planner-app)
- [Figma - Trip Planner App UI Kit](https://www.figma.com/community/file/1339623842259457307/trip-planner-app-ui-kit)
- [Behance - Travel App UI](https://www.behance.net/search/projects/travel%20app%20ui)
- [Pixso - Travel App UI Design Case Studies](https://pixso.net/tips/travel-app-ui/)

**2025 Mobile UI Trends:**
- [Design Studio - Mobile App UI/UX Trends 2025](https://www.designstudiouiux.com/blog/mobile-app-ui-ux-design-trends/)
- [Chop Dawg - UI/UX Design Trends 2025](https://www.chopdawg.com/ui-ux-design-trends-in-mobile-apps-for-2025/)

**Key Design Trends for 2025:**

1. **Glassmorphism** - Translucent panels with blur effects (Apple's "Liquid Glass" in iOS 26)

2. **Minimalist Layouts with Bold Accents** - Clean interfaces with strategic use of color

3. **Micro-interactions** - Subtle animations for feedback and delight

4. **Dark Mode** - Essential, not optional

5. **Gesture Navigation** - Swipe actions, pull-to-refresh

6. **AI-Driven Personalization** - Adaptive interfaces based on user behavior

7. **Nature-Inspired Colors** - Sea, sky, mountain, landscape palettes for travel apps

### 11.5 Similar Apps for Reference

**Location/Tracking Apps:**
- Google Maps Timeline - Gold standard for location history UI
- Strava - Excellent activity tracking with maps
- Life360 - Family location sharing
- OwnTracks - Open source location tracking

**Trip Planning Apps:**
- TripIt - Trip organization
- Wanderlog - Trip planning with maps
- Roadtrippers - Road trip planning

**Key UI Patterns to Study:**
- Bottom sheet for details (Google Maps style)
- Floating action buttons for quick actions
- Card-based list items
- Map + list split view
- Collapsible headers on scroll

### 11.6 Android Service Architecture References

**Key Sources:**
- [Android Developers - Foreground Services](https://developer.android.com/develop/background-work/services/foreground-services)
- [ArcGIS - Background Location Tracking (.NET MAUI)](https://developers.arcgis.com/net/device-location/background-location-tracking/)
- [Google Codelabs - Location Updates in Kotlin](https://codelabs.developers.google.com/codelabs/while-in-use-location)

**Architecture Pattern for Reliable Location Tracking:**

```
┌────────────────────────────────────────────────────────────┐
│              Android Foreground Service                     │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ LocationManager (GPS/Fused Provider)                  │  │
│  │ → Request location updates directly                   │  │
│  │ → Filter by accuracy/confidence                       │  │
│  │ → Apply time/distance thresholds                      │  │
│  │ → Write to SQLite (no app dependency!)                │  │
│  │ → Broadcast to UI when available                      │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                            │
│  Notification: "Tracking active - 42 locations today"      │
│  Actions: [Pause] [Open App]                               │
└────────────────────────────────────────────────────────────┘
         ↑                              ↓
    Commands                      Broadcasts
    (Start/Stop)                 (Location updates)
         ↑                              ↓
┌────────────────────────────────────────────────────────────┐
│                    .NET MAUI App                           │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ LocationBridge                                        │  │
│  │ → Receives broadcasts when app is visible             │  │
│  │ → Sends commands to start/stop service                │  │
│  │ → Exposes CurrentLocation as observable               │  │
│  └──────────────────────────────────────────────────────┘  │
│                          ↓                                 │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ MainMapViewModel                                      │  │
│  │ → Binds to LocationBridge.CurrentLocation             │  │
│  │ → No direct GPS interaction                           │  │
│  └──────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────┘
```

**Why This Works:**
1. Service owns GPS - survives app kill
2. Service writes to SQLite - data persists independently
3. UI subscribes when visible - no wasted resources
4. Clear separation - easy to test and maintain

---

## 12. Appendix: Code Snippets for Reference

### 12.1 Android Foreground Service Template

```csharp
[Service(ForegroundServiceType = ForegroundService.TypeLocation)]
public class LocationTrackingService : Service, ILocationListener
{
    private const int NOTIFICATION_ID = 1001;
    private LocationManager _locationManager;
    private SqliteConnection _database;

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case "START":
                StartTracking();
                break;
            case "PAUSE":
                PauseTracking();
                break;
            case "STOP":
                StopTracking();
                StopSelf();
                break;
        }
        return StartCommandResult.Sticky;
    }

    private void StartTracking()
    {
        // Create notification FIRST (5-second timeout!)
        var notification = CreateNotification("Tracking active");
        StartForeground(NOTIFICATION_ID, notification);

        // Then start GPS
        _locationManager = (LocationManager)GetSystemService(LocationService);
        _locationManager.RequestLocationUpdates(
            LocationManager.GpsProvider,
            minTimeMs: 1000,
            minDistanceM: 0,
            this);
    }

    public void OnLocationChanged(Location location)
    {
        // Filter, store, broadcast - all in service
        if (PassesQualityFilter(location))
        {
            SaveToDatabase(location);
            BroadcastLocation(location);
            UpdateNotification($"Last update: {DateTime.Now:HH:mm}");
        }
    }
}
```

### 12.2 ViewModel with CommunityToolkit.Mvvm

```csharp
public partial class MainMapViewModel : ObservableObject
{
    private readonly ILocationBridge _locationBridge;

    [ObservableProperty]
    private Location? _currentLocation;

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackingStatus = "Not tracking";

    public MainMapViewModel(ILocationBridge locationBridge)
    {
        _locationBridge = locationBridge;
        _locationBridge.LocationReceived += OnLocationReceived;
    }

    private void OnLocationReceived(object sender, Location location)
    {
        CurrentLocation = location;
        TrackingStatus = $"Updated: {DateTime.Now:HH:mm:ss}";
    }

    [RelayCommand]
    private async Task StartTrackingAsync()
    {
        var result = await _locationBridge.StartAsync();
        IsTracking = result;
    }

    [RelayCommand]
    private async Task StopTrackingAsync()
    {
        await _locationBridge.StopAsync();
        IsTracking = false;
    }
}
```

### 12.3 Syncfusion Expander for Settings

```xml
<toolkit:SfExpander IsExpanded="False">
    <toolkit:SfExpander.Header>
        <Grid Padding="16,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Image Source="location_on.png" WidthRequest="24" />
            <Label Grid.Column="1" Text="Location Settings"
                   FontSize="18" FontAttributes="Bold"
                   VerticalOptions="Center" Margin="12,0,0,0" />
        </Grid>
    </toolkit:SfExpander.Header>
    <toolkit:SfExpander.Content>
        <VerticalStackLayout Padding="16" Spacing="12">
            <Grid ColumnDefinitions="*,Auto">
                <Label Text="Enable Background Tracking" VerticalOptions="Center" />
                <toolkit:SfSwitch Grid.Column="1"
                                  IsOn="{Binding IsTrackingEnabled}" />
            </Grid>
            <!-- More settings... -->
        </VerticalStackLayout>
    </toolkit:SfExpander.Content>
</toolkit:SfExpander>
```

---

*This document should be updated as we iterate and make decisions.*
