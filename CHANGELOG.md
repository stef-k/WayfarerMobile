# Changelog

## 1.0

### 2026-01-17
- Initial public release
- Center on Location button zooms to street level (#103)
- Keep Screen On setting uses native wake lock for reliability (#155)
- Simplified diagnostics UI in Settings (#156)
- Navigation voice default volume set to 70% (#157)
- iOS WakeLockService race condition fix
- Server authority for live location submissions (#160)
- Improved network error handling and logging
- Offline check-in fallback improvements

### 2026-01-15
- Location sync race condition fixes (#150)
- GPS accuracy filter improvements
- Remove LocationSyncService, consolidate to QueueDrainService
- Real-time diagnostics queue updates

### 2026-01-12
- Offline-first CREATE operations for Places and Regions (#145)
- Unified download state management (#148)
- Download pause/resume/cancel improvements (#129)
- Memory leak fixes in TimelineViewModel and SettingsViewModel

### 2026-01-08
- Complete ViewModel refactoring (MainViewModel, TripsViewModel, SettingsViewModel)
- Repository pattern for DatabaseService
- Service extractions (TripDownloadService, TripSyncService)
- Phase 0 infrastructure (ITripStateManager, CompositeDisposable, ISyncEventBus)

## 0.9

### 2025-12-20
- Trip download with offline tile caching
- Navigation with voice announcements
- Timeline view for location history

### 2025-12-01
- Initial beta release
- Basic trip planning and viewing
- Location tracking (foreground and background)
- Check-in functionality
