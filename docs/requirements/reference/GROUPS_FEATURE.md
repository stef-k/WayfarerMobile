# Groups Feature (Mobile) — Detailed Design

## Overview

- Goal: Deliver a mobile “My Groups” experience that mirrors the backend group timeline behavior so a user can view all members’ locations in a selected group, by single-day date selection, with live updates on Today via SSE.
- Scope (mobile):
  - View-only: choose a group you already belong to and visualize members’ locations.
  - Live map for Today (SSE) and historical single-day maps (no SSE) with the same marker semantics as backend timeline: live, latest, rest.
  - Group selection via modal; legend as an expanding bottom sheet; detailed location modal on marker tap.
- Non-goals (mobile): group CRUD, invitations, membership actions; those remain in the backend web app.

## Existing Mobile Infrastructure To Reuse

- Pages and map patterns: `Pages/TimelinePage.xaml(.cs)` for Mapsui scaffolding, clustering thresholds, connectivity banners/toasts.
- API patterns and DTO mapping: `Services/Api/TimelineService.cs` (bearer auth, error handling, JSON options).
- Settings and persistence: `Services/Storage/SettingsStore.cs` for `ServerUrl`, `ApiToken`, and new keys for last-selected group and legend state.
- Connectivity UX: reuse `Connectivity.ConnectivityChanged` handling and the Offline banner/toast conventions from `TimelinePage`.
- Logging: `Services/Logging/AppLoggingService` for concise instrumentation.

## Functional Requirements

- Default view is Today (live): open SSE streams for selected users and reflect updates in near real time.
- Single-day selection only: support a quick picker that jumps year → month → day; always end with an exact date.
- Group selection: modal popup initiated by a button. Persist last-used group across app launches.
- Offline parity with Timeline: disable fetch actions, show banner/toasts, allow viewing stale cached data where available.
- Visual semantics: mirror backend logic (live vs latest vs rest) but with thicker strokes and larger markers for mobile legibility.
- Marker details: on tap, show a rich modal with member identity (username + display name), dual times (member-local and viewer-local), address, lat/lon, and actions: Open in Google Maps, Wikipedia (en.wikipedia.org geosearch), Share location.
- Color mapping: deterministic per user (username-hash → palette), exactly mirroring backend to ensure cross-platform consistency.

## Navigation & Shell

- AppShell: add a new entry “My Groups”.
- Page: `Pages/MyGroupsPage.xaml(.cs)` — dedicated screen for the group map/timeline experience.

## UX & Flows

- First open:
  - If there is a persisted group id, load it immediately; otherwise, prompt with the group selection modal.
  - Default date is Today; attach SSE for all selected members.
- Group picker (modal):
  - Lists groups from `/api/groups?scope=joined`; supports search and tap-to-select.
  - On select: persist group id and refresh map state/colors/legend.
- Date picker (single-day):
  - Quick jump UI: Year → Month → Day (always results in one date).
  - Today: enable SSE; Past day: disable SSE and only render historical data.
- Legend (expanding bottom sheet):
  - Expands/collapses over the map; scrollable content following Material “expanding bottom sheet”.
  - Displays members with color chips; supports “Only this” and “Select All/None”.
  - Persist conservative view state (expanded/collapsed) and restore on page re-open.
- Marker details modal:
  - Includes: username + display name, member-local time (server-computed) and viewer-local time (client), address, lat/lon.
  - Actions: Open in Google Maps, Wikipedia geosearch (English domain), Share location.

## Map & Visual Behavior

- Layers and clustering: reuse Timeline patterns for zoom thresholds and client-side clustering; adjust constants if needed for multi-user density.
- Marker semantics:
  - Live: within server-provided threshold window (red/strong icon in web; use matching semantics with thicker stroke on mobile).
  - Latest: the latest known but outside “live” threshold (green/latest icon in web; match with mobile style).
  - Rest: other locations within current viewport/date.
- Colors: stable per-user colors derived deterministically from username; palette order and hash function must match backend.

## Data & Models (Mobile)

Note: We reuse the server’s DTOs where possible. For clarity, the mobile code will bind to shapes equivalent to the following:

- GroupListItem: `{ id: Guid, name: string, description?: string }` from `GET /api/groups?scope=joined`.
- GroupMemberDto: `{ userId: string, userName: string, displayName?: string }` from `GET /api/groups/{groupId}/members`.
- PublicLocationDto (as used by backend): includes coordinates, timestamps (UTC and Local), timezone, accuracy, address fields, `IsLatestLocation`, and (for group responses) a `UserId` field set by controller. Mobile binds this to a `GroupLocationDto` equivalent with an added `userId` property when needed for grouping by member.

## API Contracts (Mobile Consumption)

- GET `/api/groups?scope=joined`
  - Response: `[{ id, name, description? }]`
- GET `/api/groups/{groupId}/members`
  - Response: `[{ userId, userName, displayName, groupRole, status }]`
- POST `/api/groups/{groupId}/locations/latest`
  - Body: `{ includeUserIds?: string[] }` (optional; default = all active members respecting visibility rules).
  - Response: `PublicLocationDto[]` with `IsLatestLocation = true` and `LocationTimeThresholdMinutes` populated.
- POST `/api/groups/{groupId}/locations/query`
  - Body: `{ minLng, minLat, maxLng, maxLat, zoomLevel, userIds?: string[], dateType: "day", year, month, day }`
  - Response: `{ totalItems, results: PublicLocationDto[] }` (controller sets `UserId` on each item for grouping).
- SSE `/api/sse/stream/location-update/{userName}`
  - Response stream: `text/event-stream` with `data: { LocationId, TimeStamp, Type? }` lines when that user logs a location (check-in or background logging).

Auth: Mobile uses Bearer auth for standard HTTP endpoints. SSE should accept Bearer tokens as well (see Backend Gaps).

## Live Updates (SSE)

- Today only: open one SSE subscription per selected user (`location-update-{username}`).
- On event: minimally refresh that member’s latest (small `latest` call filtered to that user) and update the marker/legend.
- Reconnect strategy: incremental backoff (e.g., 1s, 2s, 5s… up to cap) on disconnect; stop SSE when navigating away or switching to non-Today.
- Keepalive: client will tolerate idle periods; server heartbeats are recommended (see Backend Gaps) to avoid mobile network timeouts.

## Persistence (SettingsStore)

- `MyGroups_LastSelectedGroupId` (string) — last used group across launches.

## Offline Behavior

- Follow Timeline: show Offline banner, disable network actions, allow users to view any locally cached map tiles and last-rendered state for the selected day.
- When returning online: show toast and auto-refresh the current date view.

## Security

- All HTTP calls include `Authorization: Bearer <token>` from Settings.
- No secrets stored beyond token; no PII in logs beyond what Timeline already logs.
- SSE should be authorized similarly (see Backend Gaps), or contain only non-sensitive metadata if left unauthenticated.

## Performance

- Debounce viewport changes before POST `/locations/query`.
- Limit selection by default for very large groups; allow “Select All” with a quick confirmation.
- Throttle UI updates on SSE bursts (coalesce sequential updates for the same member).

## Testing Strategy

- Unit: date request shaping, color mapping, SSE parser resilience, legend state persistence.
- Manual: multi-user live updates with backend staging; offline/online switching; large group selection stress.

---

## Backend Gaps for 100% Mobile Completeness

This section lists concrete backend actions to ensure the mobile feature is robust, secure, and performant. Each item includes rationale and success criteria to support a smooth handover.

1) Secure SSE with Bearer authentication

- Current: `SseController` (`/api/sse/stream/{type}/{id}`) has no `[Authorize]` attribute; any client can subscribe to any channel if they know its name.
- Change: Add `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` at class or action level, or configure a policy scheme that authenticates via Cookie for web and JWT for mobile.
- Rationale: Aligns SSE security with API endpoints; prevents unauthenticated listening for user activity.
- Notes:
  - If the app currently uses Cookie auth by default, configure `DefaultScheme` via a policy scheme that picks JWT when `Authorization: Bearer` is present.
  - Ensure `SuppressResponseBody` or compression does not buffer SSE responses; keep streaming enabled.
- Acceptance:
  - `curl -H "Authorization: Bearer <token>" /api/sse/stream/location-update/<username>` returns 200 and streams; omit header returns 401/403.

2) SSE heartbeats (keepalive)

- Current: `SseService.SubscribeAsync` holds the connection and only writes on broadcasts. Idle connections may be dropped by proxies/mobile networks.
- Change: emit periodic heartbeat frames every 15–30 seconds to each subscriber.
- Implementation options:
  - Send SSE comments `:\n\n` or `data: {"type":"ping"}\n\n` from a timer tied to the subscription lifetime.
- Acceptance:
  - Observed heartbeat frames at the configured interval on quiet channels; mobile maintains long-lived connections reliably.

3) Optional: Group-level aggregated SSE channel for location updates

- Current: Mobile must open N SSE connections for N selected users (`location-update-{username}`). This may be heavy for large groups.
- Change: Add server broadcasts to `group-location-update-{groupId}` with payload `{ userName, userId, locationId, timeStamp }` whenever a member logs a location. Expose `GET /api/sse/stream/group-location-update/{groupId}`.
- AuthZ: require membership in the group to subscribe.
- Rationale: Single SSE connection per group reduces mobile socket load and battery/network usage.
- Acceptance:
  - Subscribing as a member yields updates for all active members; non-members receive 403.

4) Deterministic color mapping exposure

- Current: Web uses deterministic per-user colors (username-hash → palette), but the palette/algorithm is not published. Mobile must match exactly.
- Change (one of):
  - A) Extend `GET /api/groups/{groupId}/members` to include `color` (hex) computed by the backend, or
  - B) Add a small endpoint (or static JSON) that publishes the palette and mapping rules so mobile can mirror it precisely.
- Rationale: Prevent color mismatches across platforms and ensure stable user identity recognition.
- Acceptance:
  - Members API returns a stable `color` property per user, or palette JSON is available and matches web.

5) SSE payload enrichment (optional optimization)

- Current: SSE `location-update-{username}` payload includes `{ LocationId, TimeStamp, Type? }`; mobile then calls `latest` for the user.
- Change: Include `{ userName, userId, locationId, timeStamp, coordinates, isLive }` in the SSE `data` where possible, so clients can update immediately and optionally skip an extra HTTP call.
- Rationale: Reduce latency and HTTP overhead on bursts of updates.
- Acceptance:
  - On update, mobile can update the map without a follow-up `latest` call.

6) Authorize SSE by resource intent (defense-in-depth)

- Current: `SseController` accepts arbitrary `{type}/{id}` pairs with no resource-level checks.
- Change: For sensitive channels (e.g., `location-update-{username}`), optionally enforce that the subscriber is authorized to view the target user’s data (e.g., via group membership or an existing visibility rule), or document that SSE carries only non-sensitive data and server-side data endpoints enforce access.
- Rationale: Prevent metadata leakage (e.g., “someone is active now”) via SSE.
- Acceptance:
  - Either: resource-level checks return 403 for unauthorized listeners; or: SSE payloads remain non-sensitive and all data endpoints enforce strict authorization.

7) Pagination/limits for group queries (load-safety)

- Current: `POST /api/groups/{groupId}/locations/query` returns `{ totalItems, results }` but does not expose pagination inputs in the DTO.
- Change (optional): Define `page`/`pageSize` or server-side caps + documentation. Mobile can handle partial pages or server limits gracefully.
- Rationale: Large groups/day views can be heavy; rate-limiting and paging keep UX responsive.
- Acceptance:
  - Documented caps or implemented pagination; endpoint returns within target latency under expected loads.

8) Confirm Bearer acceptance for SSE

- Current: APIs are `[Authorize]`, while SSE has no `[Authorize]` (see #1). Once secured, confirm JWT Bearer works for streaming requests.
- Change: Ensure JWT Bearer is active for `/api/sse/stream/**` and not overridden by cookie defaults.
- Acceptance:
  - Automated test or manual `curl` verifies 200 with valid JWT and 401/403 otherwise.

9) Response compression and buffering settings

- Current: Not explicitly configured for SSE in the snippet.
- Change: Ensure SSE responses aren’t buffered or compressed in a way that delays frames (e.g., disable response compression for `text/event-stream`).
- Acceptance:
  - Frames arrive immediately (no multi-second buffering) under compression-enabled deployments.

---

## Verification Recipes (Backend + Mobile)

- SSE auth quick test:
  - `curl -i \`
     -H "Authorization: Bearer <token>" \`
     -H "Accept: text/event-stream" \`
     https://<host>/api/sse/stream/location-update/<username>`
  - Expect 200 and streamed frames on activity.
- Group endpoints:
  - `curl -H "Authorization: Bearer <token>" https://<host>/api/groups?scope=joined`
  - `curl -H "Authorization: Bearer <token>" https://<host>/api/groups/<gid>/members`
  - `curl -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"includeUserIds":["<uid>"]}' https://<host>/api/groups/<gid>/locations/latest`
  - `curl -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"minLng":...,"minLat":...,"maxLng":...,"maxLat":...,"zoomLevel":12,"dateType":"day","year":2025,"month":10,"day":21}' https://<host>/api/groups/<gid>/locations/query`

---

## Open Questions

- Color exposure: prefer extending Members API with `color` or publish palette JSON?
- Group-level SSE channel: desired for v1 or keep multi-connection approach and iterate later?
- Server caps for query endpoint: do we want explicit pagination now or document current caps?
