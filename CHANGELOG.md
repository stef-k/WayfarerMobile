# Changelog

## 1.0

### 2026-01-30
- **Fix: Downloaded trips not showing when offline (#212)**
  - Trips with completed tile downloads now display correctly when device is offline
  - Fixed race condition in offline trip loading logic
- **Fix: Offline banner hiding top info overlay (#207)**
  - Offline connectivity banner no longer obscures the top information overlay
  - Adjusted banner positioning and z-index for proper layering
- **Documentation: Fix inconsistencies (#208)**
  - Updated Architecture and Contributing docs to use current MapBuilder + layer services pattern
  - Replaced outdated MapService references with ILocationLayerService and IMapBuilder
  - Added 20+ new service documentation sections in Services.md
  - Added `/api/location/check-in` endpoint documentation

### 2026-01-29
- **Feature: Zoom level display and extended tile cache (PR #209)**
  - Added zoom level indicator to map display
  - Extended tile cache support to zoom level 18 for higher detail offline maps
- **Fix: Offline queue not working due to silent location loss (#205)**
  - Fixed issue where queued locations were silently lost when offline
  - Improved queue persistence and recovery logic
- **Fix: GPS high performance mode not applied at cold start (PR #206)**
  - High performance GPS mode now correctly activates on app cold start
  - Fixed initialization order issue in location tracking service

### 2026-01-24
- **Documentation: Review fixes**
  - Fixed CLAUDE.md reference to non-existent navigation docs
  - Corrected TimelineTrackingEnabled default value (false, not true)
  - Removed broken image placeholder in Getting Started guide
  - Clarified QR code security claim (contains credentials, not encrypted)
  - Added About page with project info, attribution, and disclaimer
  - Simplified Location Tracking guide for end-users (removed architecture diagrams)
  - Added warranty disclaimer to documentation README
  - Documented 14 undocumented settings in Services.md
  - Fixed LiveCachePrefetchRadius range (1-10, not 1-9)
  - Archived QUEUE.md spec to docs/specs/

### 2026-01-19 (PR #179)
- **Feature: Offline queue UX/controls (#152)**
  - Added Settings > Offline Queue section for user-facing queue management
  - Queue status display: total count, pending/retrying/synced/rejected breakdown, health status
  - Configurable queue limit (1-100,000, default 25,000) with storage warning above 50k
  - Coverage estimate showing current span and remaining headroom based on time threshold
  - Export to CSV and GeoJSON formats with formula injection protection
  - Clear actions: Clear Synced (synced+rejected), Clear Pending, Clear All with confirmations
  - Rolling buffer cleanup: when at limit, removes synced/rejected first, then oldest pending (never syncing)
  - Diagnostics page now read-only for queue info, points to Settings for management

### 2026-01-18 (PR #178)
- **Feature: Background processing for timeline sync (#177)**
  - Timeline mutations now sync automatically without requiring Timeline page to be open
  - Timer-based processing (60s interval) with self-contained connectivity subscription
  - Piggybacks on background location service wakeups for sync while app is backgrounded
  - Integrates with AppLifecycleService to trigger sync on suspend/resume
  - Removed sync responsibility from TimelineViewModel (now UI state only)
  - Adapted QueueDrainService patterns for lower-traffic timeline mutations
  - Full exception isolation to protect background location services

### 2026-01-18 (PR #176)
- **Feature: Timeline activity editing (#153)**
  - Added "Edit Activity" option to timeline location edit menu
  - Activity picker popup with custom ComboBox control (consistent with check-in flow)
  - Shows current activity, allows selection from synced activity types
  - Refresh button to sync activities from server
  - Clear button to remove activity from location
  - Full offline support with queue and rollback

### 2026-01-18 (PR #174)
- **Refactor: Consistent network error handling (#162)**
  - Created `NetworkLoggingExtensions` helper to check connectivity before logging
  - Updated ~30 `HttpRequestException` handlers across ViewModels, Services, and Platform code
  - Network errors only log when device is online (unexpected failures)
  - Reduces log noise when offline - expected network errors are silently handled
  - Rate limiting: same message template logs at most once per 30 seconds
  - Shows suppression count when logging resumes (e.g., "suppressed 5 similar warnings")
  - Thread-safe: uses `ConcurrentDictionary` for throttle state tracking

### 2026-01-18 (PR #173)
- **Fix: Prevent visit notification spam (#142)**
  - Added place+date deduplication to prevent GPS jitter re-entry spam
  - Same place notified only once per day; fresh notifications on different days

### 2026-01-17 (PR #172)
- **Fix: Prevent repeat navigation announcements (#143)**
  - Each navigation step/waypoint now announced only once per session
  - Fixes alternating announcements when multiple steps within range

### 2026-01-17 (PR #171)
- **Fix: Map recenter behavior (#97)**
  - Map centers + zooms to street level when page appears (no trip loaded)
  - Location updates move dot without auto-recentering - users can pan freely
  - "Center on Location" button available for manual recentering

### 2026-01-17 (PR #170)
- **UI: Improved heading and accuracy indicator visibility (#166)**
  - Heading cone: increased opacity (alpha 80→120) and size (length 35→50px, inner radius 12→14px)
  - Accuracy circle: increased fill opacity (alpha 30-60→50-90), outline opacity (100→150), and width (1→1.5px)
  - Both indicators now more prominent and easier to see against the map background

### 2026-01-17 (PR #168)
- **Performance: Faster Offline Queue Sync (#101)**
  - Reduced sync rate limit from 65s to 12s (server allows 10s, 2s safety margin)
  - Added continuous drain loop that processes queue until empty
  - Sync time for 100 queued locations: ~50 min → ~17 min
  - Drain loop exits cleanly on: queue empty, device offline, too many failures, service disposed
- **Code Quality Improvements**
  - Extracted shared drain logic into `ClaimAndProcessOneLocationAsync` (eliminates duplication)
  - Added `IsDrainLoopRunning` property with early check to reduce invocation overhead
  - Added verbose trace logging to empty catch blocks in platform services
  - Documented static delegate lifecycle (set once at startup, never cleared)

### 2026-01-17 (PR #164, #165)
- Initial public release
- **Architecture Overhaul**
  - Complete ViewModel extraction pattern (MainViewModel, TripsViewModel, GroupsViewModel, TimelineViewModel, SettingsViewModel)
  - Repository pattern for DatabaseService (reduced from 1,461 to 250 lines)
  - Service extractions: TripDownloadService, TripSyncService, MutationQueueService
  - Phase 0 infrastructure: ITripStateManager, CompositeDisposable, ISyncEventBus, IDownloadProgressAggregator
- **Quick Wins (PR #163)**
  - Center on Location button zooms to street level (#103)
  - Keep Screen On setting uses native WakeLockService for reliability (#155)
  - Simplified diagnostics button in Settings (#156)
  - Navigation voice default volume set to 70% (#157)
  - iOS WakeLockService race condition fix
  - Consolidated keep-screen-on to single mechanism
- **Server & Sync**
  - Server authority for live location submissions via log-location endpoint (#160)
  - Improved network error handling and logging
  - Offline check-in fallback and real-time diagnostics network status
  - IsUserInvoked fields for diagnostics queue exports

### 2026-01-15 (PR #159)
- Location sync race condition fixes (#150)
- GPS accuracy filter v2 improvements
- Remove LocationSyncService, consolidate all syncs to QueueDrainService
- Real-time queue updates in DiagnosticsViewModel
- SafeFireAndForget helper for proper exception logging
- Thread-safe initialization in TripSyncService

### 2026-01-12 (PR #147, #149)
- Offline-first CREATE operations for Places and Regions (#145)
- Unified download state management (#148)
- Reactive UI updates for download state changes
- TempId to ServerId reconciliation for offline entries
- Memory leak fixes in TimelineViewModel and SettingsViewModel

### 2026-01-10 (PR #146, #151)
- Download pause/resume/cancel improvements (#129)
- Bounding box validation from all sources before tile download
- Allow loading trips with incomplete tile downloads
- Location sync race condition between LocationSyncService and QueueDrainService (#150)
- Idempotency keys for location sync
- Crash safety and edge case recovery for location sync

### 2026-01-08 (PR #98, #100)
- ViewModel refactoring: extract child ViewModels from MainViewModel (#75)
- TripsViewModel restructured into coordinator pattern (#76)
- Service refactoring: TripSyncService and TripDownloadService extractions (#77)
- Extract MutationQueueService, CacheLimitEnforcer, DownloadStateManager, TileDownloadService
- Characterization tests for TripSyncService

## 0.9

### 2025-12-20
- Trip download with offline tile caching (.mbtiles)
- Navigation with voice announcements
- Timeline view for location history
- Groups and membership management

### 2025-12-01
- Initial beta release
- Basic trip planning and viewing
- Location tracking (foreground and background)
- Check-in functionality with QR scanning
