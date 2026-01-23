# Features Overview

Wayfarer Mobile provides a comprehensive set of features for location tracking, trip planning, and group sharing. This guide covers all major features.

---

## Main Map

The main screen displays an interactive map powered by OpenStreetMap.

### Map Display

- **Your location**: Blue dot with accuracy circle
- **Heading indicator**: Cone showing your direction of movement
- **Accuracy circle**: Shows GPS accuracy (smaller = more accurate)
- **Zoom controls**: Pinch to zoom or use buttons

### Map Controls (FAB Buttons)

The map has four floating action buttons on the right side:

| Button | Icon | Function |
|--------|------|----------|
| Trip | Trip icon | Open trip sheet (visible when a trip is loaded) |
| Drop Pin | Pin icon | Drop a temporary marker at map center |
| Center | Crosshair | Return to and zoom on your current location |
| Check-in | Checkmark | Create a manual location entry |

### Drop Pin Feature

Tap the **Drop Pin** button to place a temporary marker at the center of the map. Once dropped, tap the pin to see available actions:

| Action | Description |
|--------|-------------|
| Navigate | Start navigation to this location |
| Share | Share the coordinates with others |
| Wikipedia | Search for nearby places on Wikipedia |
| Google Maps | Open location in Google/Apple Maps |
| Copy | Copy coordinates to clipboard |
| Clear | Remove the dropped pin |

This is useful for:
- Navigating to a point you see on the map
- Sharing a location that isn't a saved place
- Exploring what's nearby any map location

### Location Indicator States

| State | Appearance | Meaning |
|-------|------------|---------|
| Active (blue) | Solid blue dot | Fresh GPS fix |
| Stale (gray) | Gray dot | No recent GPS update (30+ seconds) |
| Navigating (orange) | Orange dot | Active navigation mode |

---

## Timeline Tracking

Timeline tracking automatically records your location history to your server.

### How It Works

1. Your device acquires GPS coordinates using sleep/wake optimization
2. Locations are filtered by accuracy (<100m) and time/distance thresholds
3. Filtered locations are queued locally (up to 25,000 entries)
4. Queue syncs to your server when online
5. Timeline entries are cached locally for offline viewing

### Viewing Your Timeline

1. Tap the **Timeline** tab
2. Browse locations grouped by hour
3. Use date navigation to view different days:
   - **Previous**: Go to yesterday
   - **Today**: Jump to current day
   - **Next**: Go to tomorrow
   - **Tap date**: Open date picker for any date

### Timeline Entry Details

Tap any entry to see:
- Timestamp and coordinates
- GPS accuracy and speed
- Activity type
- Sync status
- Mini-map preview

### Editing Timeline Entries

1. Tap an entry to open details
2. Tap **Edit** to modify:
   - **Date/Time**: Adjust the timestamp
   - **Notes**: Add or update notes
3. Tap **Save**

> **Note**: Coordinates cannot be edited as they are GPS data.

### Editing Activity Types

Timeline entries can have an activity type assigned:

1. Tap an entry to open details
2. Tap **Edit Activity**
3. In the activity picker popup:
   - Select an activity from the list
   - Tap **Refresh** to sync latest activities from server
   - Tap **Clear** to remove the activity assignment
4. Changes sync to server automatically (or queue if offline)

### Exporting Timeline Data

Export your location history for backup or analysis:

1. Go to **Settings** > **Data** > **Export Timeline**
2. Choose format:
   - **CSV**: Spreadsheet-compatible, includes all fields
   - **GeoJSON**: Geographic format for mapping tools
3. Select date range (optional)
4. Tap **Export**
5. Share or save the exported file

**CSV Fields**:
| Field | Description |
|-------|-------------|
| `id` | Local entry ID |
| `server_id` | Server ID (if synced) |
| `timestamp` | ISO 8601 UTC timestamp |
| `latitude`, `longitude` | Coordinates |
| `accuracy` | GPS accuracy in meters |
| `altitude`, `speed`, `bearing` | Motion data |
| `address`, `place`, `region`, `country` | Enrichment from server |
| `activity_type` | Activity classification |
| `notes` | User notes |

### Importing Timeline Data

Import previously exported data or data from other sources:

1. Go to **Settings** > **Data** > **Import Timeline**
2. Select a CSV or GeoJSON file
3. Review import summary:
   - **Imported**: New entries added
   - **Updated**: Existing entries enriched with new data
   - **Skipped**: Duplicates detected

**Duplicate Detection**:
- Entries within **2 seconds** of an existing timestamp
- And within **10 meters** of the same location
- Are considered duplicates and skipped or merged

> **Note**: Imported entries are local-only and not synced to server.

---

## Trip Management

Trips are planned routes with places and segments that can be downloaded for offline use.

### Trips Page Tabs

The Trips page has two tabs:

| Tab | Content |
|-----|---------|
| **My Trips** | Your personal trips from the server |
| **Public Trips** | Shared/public trips you can browse and download |

### Viewing Available Trips

1. Go to **Trips** tab
2. Switch between **My Trips** and **Public Trips** tabs
3. Downloaded trips show a checkmark

### Downloading a Trip

1. Find the trip in the list
2. Tap **Download**
3. Wait for download to complete
4. Progress shows percentage and status

**Download Process**:

| Phase | Progress | Description |
|-------|----------|-------------|
| 1. Fetch Details | 10-25% | Download trip metadata from server |
| 2. Save Regions | 25-30% | Store regions and areas locally |
| 3. Save Places | 30-40% | Store places with coordinates |
| 4. Save Segments | 40-50% | Store route segments with geometry |
| 5. Download Tiles | 55-95% | Fetch map tiles for offline use |
| 6. Complete | 100% | Finalize and verify |

**What gets downloaded**:
- Trip metadata (name, notes, cover image)
- Regions and areas with polygon zones
- Places with coordinates, icons, and notes
- Segments with polyline geometry
- Map tiles for zoom levels 8-17

**Tile Download Features**:
- **Atomic writes**: Temp file then move (prevents corruption)
- **Rate limiting**: Respects tile server policies
- **Resume support**: Skips already-downloaded tiles
- **Adaptive zoom**: Smaller areas get higher detail (up to z17)

**Estimated Storage**:
| Area Size | Tiles | Storage |
|-----------|-------|---------|
| City neighborhood | ~500 | ~20 MB |
| City area | ~2,000 | ~80 MB |
| State/province | ~10,000 | ~400 MB |

### Viewing Trip Details

When a trip is selected:

1. Map shows trip boundary
2. Place markers appear with custom icons
3. Route segments connect places with colored lines
4. Sidebar shows places and segment list

### Trip Sheet

When a trip is loaded, tap the **Trip** FAB button to open the trip sheet. The sheet shows:

- **Trip info**: Name and notes
- **Regions**: Expandable sections containing places
- **Places**: Listed with icons and names
- **Areas**: Geographic zones within the trip
- **Segments**: Routes between places with transport mode

**Trip Sheet Features:**
- **Search**: Filter places by name using the search bar
- **Notes**: View notes for trip, regions, areas, and segments
- **Selection**: Tap items to highlight them on the map

Tap a place to see the detail panel with:
- Name, address, and coordinates
- Notes (if any)
- Action buttons (Navigate, Maps, Wikipedia, Share, Copy)

### Trip Editing

The mobile app supports on-the-go trip editing for quick updates:

**Place Actions:**
| Action | Description |
|--------|-------------|
| Edit Coordinates | Drag the place marker to a new location |
| Navigate | Start navigation to this place |
| Open in Maps | View in Google/Apple Maps |
| Wikipedia | Search nearby on Wikipedia |
| Share | Share location with others |
| Copy Coordinates | Copy lat/lon to clipboard |

**Region Management:**
- **Reorder regions**: Move regions up or down in the list
- **Delete regions**: Remove a region (places move to default region)
- **Edit notes**: Update region notes

**Place Management:**
- **Reorder places**: Move places up or down within their region
- **Edit coordinates**: Drag marker on map then save

**Add to Trip:**
From the main map, you can add your current location to the loaded trip as a new place.

> **Note**: Full trip creation and advanced editing is done in the Wayfarer web app. Mobile editing is for on-the-go adjustments.

### Transport Mode Colors

| Mode | Color | Icon |
|------|-------|------|
| Walking | Blue | Pedestrian |
| Driving | Green | Car |
| Transit | Orange | Bus/Train |
| Cycling | Purple | Bicycle |
| Other | Gray | Generic |

---

## Turn-by-Turn Navigation

Navigate to destinations with intelligent routing that adapts to context.

### Navigation Contexts

The app supports navigation in different contexts:

| Context | Started From | Features |
|---------|--------------|----------|
| **Trip Navigation** | Trip sidebar → place | Uses trip segments, full route priority |
| **Group Navigation** | Groups → member | OSRM routing to member location |
| **Map Navigation** | Long-press on map | OSRM routing to any point |

### Starting Trip Navigation

1. Open a downloaded trip
2. Tap a place in the sidebar
3. Tap **Navigate**
4. Select transport mode (Walk/Drive/Bike)
5. Navigation overlay appears

### Starting Ad-Hoc Navigation

1. From **Groups**: Tap member → **Navigate**
2. From **Map**: Long-press location → **Navigate**
3. Select transport mode or **External Maps**

### Navigation Display

The overlay shows:
- **Distance remaining**: How far to destination
- **Time remaining**: Estimated arrival time
- **Direction**: Cardinal direction (N, NE, E, etc.)
- **Current instruction**: What to do next

### Route Sources (Priority Order)

Route calculation differs based on navigation context:

**Trip Navigation** (has trip context):
| Priority | Source | When Used |
|----------|--------|-----------|
| 1 | **User Segments** | Trip has pre-defined route geometry |
| 2 | **Cached OSRM** | Valid cache exists (same dest, <50m origin, <5 min old) |
| 3 | **OSRM Fetch** | Online and no cache available |
| 4 | **Direct Route** | Offline fallback (straight-line with bearing) |

**Ad-Hoc Navigation** (groups, map locations):
| Priority | Source | When Used |
|----------|--------|-----------|
| 1 | **OSRM Fetch** | Online route calculation |
| 2 | **Direct Route** | Offline fallback (straight-line with bearing) |

> **Note**: Ad-hoc navigation doesn't have user segments or route caching since there's no trip context.

**User Segments**: Routes you defined when planning the trip. These include the exact polyline geometry and are always preferred over calculated routes.

**Cached OSRM**: Previously fetched routes are cached and reused if:
- Same destination
- Origin within 50 meters of cached origin
- Less than 5 minutes old

**OSRM Fetch**: Online route calculation from OSRM (Open Source Routing Machine). Supports walking, driving, and cycling profiles. Rate limited to 1 request per second.

**Direct Route**: When offline and no cached route exists, shows straight-line navigation with:
- Cardinal direction (N, NE, E, etc.)
- Distance to destination
- Bearing-based heading

### External Maps Integration

For any navigation, you can choose **External Maps** to hand off to:
- Google Maps (Android)
- Apple Maps (iOS)
- Other installed navigation apps

### Audio Announcements

The app announces:
- Distance at regular intervals
- Arrival at destination
- Waypoint instructions
- Transport mode changes

Configure audio in **Settings** > **Navigation**:
- Enable/disable audio
- Enable/disable vibration
- Units (kilometers/miles)

### Off-Route Detection

If you deviate more than 100m from the planned route:
- Orange "off route" indicator appears
- Automatic reroute available
- Tap **Reroute** to recalculate

---

## Group Location Sharing

Share your location with group members in real-time.

### Viewing Groups

1. Go to **Groups** tab
2. See groups you belong to
3. Select a group to view

### Group Map View

The map shows:
- Each member's location with a colored marker
- Member names on markers
- Update times

### View Toggle

Switch between:
- **List View**: Member list with location details
- **Map View**: All members on the map

### Member Colors

Each member has a unique color derived from their username. Colors are consistent across the web app and mobile.

### Live Updates

For the current day ("Today"):
- Locations update automatically
- SSE (Server-Sent Events) push new positions
- Refresh interval: approximately 30 seconds

For historical days:
- Static view of that day's locations
- No live updates

---

## Manual Check-In

Create location entries manually at memorable places.

### Creating a Check-In

1. Tap the **Check-in** button on the main map
2. View your current location preview
3. Select an **Activity Type** (optional)
4. Add **Notes** (optional)
5. Tap **Submit**

### Activity Types

Activity types categorize your check-ins. The app includes 20 built-in defaults with icons:

| Activity | Icon | Activity | Icon |
|----------|------|----------|------|
| Walking | walk | Running | run |
| Cycling | bike | Travel | car |
| Eating | eat | Drinking | drink |
| At Work | marker | Meeting | flag |
| Shopping | shopping | Pharmacy | pharmacy |
| ATM | atm | Fitness | fitness |
| Doctor | hospital | Hotel | hotel |
| Airport | flight | Gas Station | gas |
| Park | park | Museum | museum |
| Photography | camera | General | marker |

**Server Sync**:
- Custom activity types sync from your server every 6 hours
- Server activities (positive IDs) take precedence over defaults
- Defaults (negative IDs) are always available as fallback
- Icons are automatically suggested based on activity name

### When to Use Check-In

- At places you want to remember
- When GPS is unreliable
- To add notes about a location
- To force an immediate sync

---

## PIN Lock Protection

Secure the app with a PIN code.

### Setting Up PIN Lock

1. Go to **Settings** > **Privacy & Security**
2. Enable **PIN Lock**
3. Enter a 4-digit PIN
4. Confirm the PIN

### Using PIN Lock

When enabled:
- App requires PIN on launch
- App requires PIN when returning from background
- Three wrong attempts show a warning

### Managing PIN

- **Change PIN**: Enter old PIN, then new PIN twice
- **Disable PIN**: Toggle off and enter current PIN

---

## Offline Mode

The app works offline with downloaded content.

### What Works Offline

| Feature | Offline | Notes |
|---------|---------|-------|
| View map (cached areas) | Yes | Downloaded tiles only |
| GPS location | Yes | Shows on map |
| Trip navigation | Yes | With downloaded trip |
| Timeline viewing | Partial | Cached entries only |
| Location logging | Yes | Queued for later sync |
| Group viewing | No | Requires internet |
| Check-in | Partial | Queued for later sync |

### Offline Banner

When offline, a banner appears at the top of relevant pages:
- "You are offline - some features may be unavailable"
- Disappears when connection restored

### Sync When Online

When you reconnect:
- Queued locations sync automatically
- Check-ins upload
- Timeline refreshes

---

## Settings

Configure app behavior in the Settings page.

### Account

- View logged-in email
- Last sync time
- Logout option
- QR code scanning for reconnection

### Timeline Tracking

- **Enable/Disable**: Toggle 24/7 location logging
- **Time threshold**: Minimum minutes between logged locations
- **Distance threshold**: Minimum meters between logged locations

> **Note**: Thresholds are set by your server and sync automatically.

### Offline Queue

Manage the local queue of locations waiting to sync to the server.

**Queue Status Display:**
| Field | Description |
|-------|-------------|
| Total | All queued locations |
| Pending | Waiting to sync |
| Retrying | Failed sync attempts being retried |
| Synced | Successfully sent to server |
| Rejected | Server rejected (invalid data) |
| Health | Overall queue health (Healthy/Warning/Critical/Over Limit) |

**Queue Limit:**
- Configurable from 1 to 100,000 locations (default: 25,000)
- Storage warning shown above 50,000
- Coverage estimate shows how much time the current queue spans
- Headroom shows estimated time until queue fills

**Export Options:**
- **CSV**: Export all queued locations to CSV format
- **GeoJSON**: Export as geographic data for mapping tools

**Clear Actions:**
| Action | What It Clears |
|--------|----------------|
| Clear Synced | Synced + rejected locations (safe to clear) |
| Clear Pending | Unsynced locations (data loss warning) |
| Clear All | Entire queue (confirmation required) |

**Rolling Buffer Cleanup:**
When the queue reaches its limit, automatic cleanup runs:
1. First removes synced/rejected entries
2. Then removes oldest pending entries (never entries currently syncing)

### Map Cache

- **Enable offline caching**: Store tiles for offline use
- **Live cache size**: Maximum storage for current area tiles
- **Trip cache size**: Maximum storage for downloaded trips
- **Clear cache**: Free up storage

### Navigation

- **Audio enabled**: Turn-by-turn voice announcements
- **Vibration enabled**: Haptic feedback at waypoints
- **Auto-reroute**: Automatically recalculate when off route
- **Distance units**: Kilometers or miles

### Visit Notifications

Get notified when you arrive at places in your loaded trip.

- **Enable/Disable**: Toggle arrival notifications on or off
- **Notification style**: Choose how you want to be alerted:
  - **Notification**: System notification only
  - **Voice**: Voice announcement only
  - **Both**: Notification and voice announcement

When enabled and you have a trip loaded, the app monitors your location and notifies you when you arrive at a trip place. This works even when the app is in the background.

### Appearance

- **Dark mode**: Toggle dark/light theme
- Theme applies immediately

### Privacy & Security

- **PIN lock**: Require PIN to access app
- **Change PIN**: Update your PIN code

### About

- App version
- Open source licenses
- OpenStreetMap attribution
- **Diagnostics**: Access detailed system diagnostics
- **Rerun Setup**: Re-run the onboarding wizard to reconfigure server connection

---

## Diagnostics

For troubleshooting, access detailed diagnostics:

1. Go to **Settings** > **About** > **Diagnostics**
2. View sections:
   - **Location Queue**: Pending sync items
   - **Tile Cache**: Cache statistics
   - **Tracking**: Service status
   - **Navigation**: Route cache info
3. Export diagnostic report for support

---

## Next Steps

Learn more about specific features:
- [Trips and Offline Maps](04-Trips-and-Offline.md)
- [Location Tracking Details](05-Location-Tracking.md)
- [Groups and Sharing](06-Groups-and-Sharing.md)
