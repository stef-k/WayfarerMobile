# QUEUE-CODEX

Purpose
- Capture requirements, findings, calculations, and implementation proposals for offline queue UX and controls.

User Requirements (Issue #152 + follow-up)
- Settings owns queue controls and exports; Diagnostics stays read-only for queue data.
- Queue limit is user-adjustable, clamped, and applied without being unnecessarily destructive.
- Coverage/headroom is shown in Settings. Coverage uses full queue stats; headroom estimate uses time threshold from Settings and worst-case logging (all time-threshold hits).
- Queue export includes all statuses (pending, retrying, synced, rejected).
- GeoJSON export is added alongside CSV.

Worst-case storage calculation for queue limit
Assumptions (worst case, per user request):
- CheckInNotes contains 20,000 chars of HTML rich text (no images).
- SQLite stores text as UTF-16 (2 bytes per char). This is conservative; UTF-8 ASCII would be about half.
- Other fields (coords, timestamps, status, ids, provider, idempotency key, errors) are minor compared to notes.

Per-entry notes payload
- 20,000 chars * 2 bytes = 40,000 bytes (about 39.1 KiB)

Estimated DB size from notes alone
- 25,000 entries: 1,000,000,000 bytes (about 0.93 GiB)
- 50,000 entries: 2,000,000,000 bytes (about 1.86 GiB)
- 100,000 entries: 4,000,000,000 bytes (about 3.73 GiB)

Notes
- UTF-8 ASCII would cut the above roughly in half (0.46 GiB, 0.93 GiB, 1.86 GiB).
- SQLite row and index overhead will add additional size beyond these estimates.

Findings
- Queue limit is hard-coded in two cleanup paths, so the setting must be applied in both places to avoid inconsistent behavior.
- Current cleanup deletes only one record when over limit, so lowering the limit will not reduce size until enough new entries arrive.
- Queue health uses a fixed threshold (pending > 1000) which will be misleading with adjustable limits.
- Coverage should use targeted queries (oldest and newest pending) rather than full queue loads.
- Export and clear logic is duplicated between Diagnostics and Settings; a shared service is needed to avoid drift.
- Tests do not cover limit clamping, limit-driven cleanup, or coverage/headroom calculations.

Proposals
- Settings and limits
  - Add QueueLimit to SettingsService and ISettingsService with clamp.
  - Default stays 25,000; allow max 100,000 with a storage warning based on the calculation above.
  - Apply QueueLimit in both LocationQueueRepository and DatabaseService cleanup paths.

- Progressive, non-destructive trimming
  - On limit decrease, do not immediately delete pending entries.
  - On each enqueue, if count > limit, delete in priority order: oldest synced, oldest rejected, then oldest pending as last resort.
  - Optionally trim synced/rejected immediately on limit change if count is over limit, but never delete pending without a new enqueue event.

- Coverage and headroom
  - Coverage span = newest pending timestamp minus oldest pending timestamp.
  - Headroom estimate (approx) = (QueueLimit - TotalQueueCount) * LocationTimeThresholdMinutes.
  - Label headroom as approximate and worst-case based on threshold logging.

- Diagnostics vs Settings UI
  - Move queue export/clear actions to Settings; keep Diagnostics read-only for queue status.
  - Add Offline Queue section in Settings with status summary, limit control, exports, and clear actions.

- Export
  - Introduce QueueExportService for CSV and GeoJSON. Use in Settings and remove duplicate implementations.
  - Export includes all statuses.

- Health status
  - Make queue health relative to limit (for example: warning at 80% of limit, critical at 95%+).

- Tests
  - Add tests for QueueLimit clamping, limit-driven cleanup in both paths, and headroom math.

Open questions
- Should the UI block setting very high limits (for example > 50,000) on low-storage devices, or only warn?
- Should TotalQueueCount for headroom include synced/rejected entries or only pending?
