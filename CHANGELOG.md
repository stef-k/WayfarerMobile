# Changelog

## 1.0

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
