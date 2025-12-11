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

### Map Controls

| Button | Function |
|--------|----------|
| Center | Return to your current location |
| Trips | Open trip sidebar |
| Check-in | Create a manual location entry |

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

1. Your device acquires GPS coordinates periodically
2. Locations are filtered by time and distance thresholds
3. Filtered locations are queued locally
4. Queue syncs to your server when online

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
2. Tap **Edit**
3. Modify date/time, activity type, or notes
4. Tap **Save**

> **Note**: Coordinates cannot be edited as they are GPS data.

---

## Trip Management

Trips are planned routes with places and segments that can be downloaded for offline use.

### Viewing Available Trips

1. Go to **Trips** tab
2. See your trips from the server
3. Downloaded trips show a checkmark

### Downloading a Trip

1. Find the trip in the list
2. Tap **Download**
3. Wait for tile download to complete
4. Progress shows percentage and tile count

What gets downloaded:
- Trip metadata (name, places, segments)
- Map tiles for all zoom levels within trip area
- Route geometry for navigation

### Viewing Trip Details

When a trip is selected:

1. Map shows trip boundary
2. Place markers appear with custom icons
3. Route segments connect places with colored lines
4. Sidebar shows places and segment list

### Trip Sidebar

Open the sidebar to see:

- **Places**: Listed with icons and names
- **Segments**: Routes between places with transport mode

Tap a place to:
- View details in bottom sheet
- Start navigation
- See notes and address

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

Navigate to trip places with routing support.

### Starting Navigation

1. Open a downloaded trip
2. Tap a place in the sidebar
3. Tap **Navigate**
4. Navigation overlay appears

### Navigation Display

The overlay shows:
- **Distance remaining**: How far to destination
- **Time remaining**: Estimated arrival time
- **Direction**: Cardinal direction (N, NE, E, etc.)
- **Current instruction**: What to do next

### Route Sources (Priority Order)

1. **User Segments**: Routes you defined in the trip (always preferred)
2. **Cached OSRM**: Previously fetched route if still valid
3. **OSRM Fetch**: Online route calculation (requires internet)
4. **Direct Route**: Straight-line bearing and distance (offline fallback)

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

Common activity types include:
- Home
- Work
- Restaurant
- Shopping
- Exercise
- Travel
- Entertainment
- Healthcare
- Education

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
- Diagnostics link

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
