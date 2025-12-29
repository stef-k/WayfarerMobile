# Trips and Offline Maps

This guide explains how to work with trips and use offline maps in Wayfarer Mobile.

---

## Understanding Trips

Trips in Wayfarer are planned journeys containing:

- **Places**: Destinations you want to visit
- **Segments**: Routes connecting places with transport modes
- **Areas**: Geographic regions within the trip
- **Map tiles**: Cached map images for offline viewing

Trips are created in the Wayfarer web app and downloaded to your mobile device. Basic trip information can be minimally edited in the mobile app for on-the-go updates.

---

## Viewing Your Trips

### Trip List

1. Tap the **Trips** tab
2. View all trips from your server

Each trip card shows:
- Trip name
- Number of places
- Download status (checkmark if downloaded)
- Estimated size

### Trip Status Icons

| Icon | Status |
|------|--------|
| Download | Not downloaded, tap to download |
| Checkmark | Downloaded and ready for offline use |
| Progress | Currently downloading |
| Warning | Download incomplete or error |

---

## Downloading a Trip

Downloading a trip makes it available offline. You have two download options:

### Download Options

| Option | What's Included | Best For |
|--------|-----------------|----------|
| **Download** | Places, segments, trip metadata | Quick access, online use |
| **+Offline Maps** | Everything above + map tiles (zoom 8-17) | Full offline use |

### Download Process

1. Find the trip in your list
2. Choose your download option:
   - **Download**: Quick download of trip data only
   - **+Offline Maps**: Full download including map tiles
3. Wait for the download to complete:
   - Progress bar shows completion percentage
   - Current tile count displayed (for offline maps)
   - Download size shown

### Pause and Resume

Downloads can be paused and resumed:
- **Pause**: Tap the progress area during download
- **Resume**: Tap the paused trip to continue
- **Cancel**: Long-press or use the cancel button

Paused downloads preserve progress and can be resumed later, even after restarting the app.

### Cache Limit Behavior

When the trip cache limit is reached during download:
- Download automatically pauses
- You're notified of the limit
- Free up space by removing other trips or their offline maps
- Resume the download when space is available

### Download Considerations

| Factor | Recommendation |
|--------|----------------|
| Internet | Use WiFi for large trips |
| Storage | Ensure 500MB+ free space |
| Battery | Keep device charged during download |
| Time | Large trips may take several minutes |

### What Gets Downloaded

For a typical trip:

```
Trip: Tokyo 2024
├── Metadata: 5 KB
├── Places: 12 places, 50 KB
├── Segments: 8 routes with geometry, 200 KB
└── Map Tiles: 3,500 tiles across zoom levels 8-17
    └── Total: ~450 MB
```

### Tile Coverage

Map tiles are downloaded for:
- **Zoom levels 8-17**: Regional overview to street level
- **Bounding box**: Area covering all places + buffer
- **High-detail areas**: Extra detail around each place

---

## Using Trips Offline

### Selecting a Trip

1. Tap a downloaded trip
2. Map centers on trip area
3. Place markers appear
4. Sidebar shows trip details

### Map Display

With a trip loaded:
- **Colored markers**: Each place with custom icon
- **Segment lines**: Routes between places
- **Trip boundary**: Subtle outline of covered area

### Trip Sidebar

Open the sidebar (swipe from right or tap button) to see:

**Places Section**
- Place name and icon
- Tap to view details or navigate

**Segments Section**
- Transport mode icon
- From/To places
- Estimated distance and time

### Place Details

Tap a place to see the bottom sheet with:
- Place name and category
- Full address
- Coordinates
- Notes (if any)
- Action buttons

### Place Actions

| Action | Description |
|--------|-------------|
| Navigate | Start turn-by-turn navigation |
| Open in Maps | Open location in Google/Apple Maps |
| Wikipedia | Search nearby on Wikipedia |
| Share | Share location link |

---

## Offline Navigation

### Route Priority

When you start navigation, the app determines the best route using this priority:

1. **User-Defined Trip Segments** - Routes you planned in the trip (always preferred when within ~50m of trip places)
   - Uses exact polyline geometry
   - Respects transport mode
   - Best experience

2. **Cached OSRM Route** - Previously calculated route if still valid
   - Same destination
   - Origin within 50m of cached origin
   - Less than 5 minutes old

3. **Fresh OSRM Route** - Fetched online (requires internet)
   - Uses open-source routing service
   - Walking, driving, or cycling profiles
   - Cached for reuse

4. **Direct Route** - Straight-line bearing/distance fallback (when offline)
   - Straight line to destination
   - Shows bearing and distance
   - Example: "Beach is 2km SE"

### Navigation Without Internet

When offline, you can still navigate:
- Downloaded trip segments work fully
- Cached OSRM routes work until they expire
- Direct route always available as fallback

---

## Managing Downloaded Trips

### Storage Usage

View storage in **Settings** > **Cache**:
- Trip cache size: Total space used
- Per-trip sizes in trip list

### Removing Trip Data

You have two options for removing downloaded trip data:

| Option | What's Removed | What's Kept |
|--------|----------------|-------------|
| **Remove Maps** | Cached map tiles only | Trip metadata, places, segments |
| **Remove All** | Everything | Nothing (trip removed from device) |

**Remove Maps** is useful when you need to free up storage but want to keep the trip for online use or to re-download tiles later.

**Remove All** completely removes the trip from your device. Your data on the server is not affected.

### Cache Limits

Configure limits in **Settings** > **Cache**:

| Setting | Default | Range |
|---------|---------|-------|
| Max trip cache | 2000 MB | 500-5000 MB |
| Max live cache | 500 MB | 100-2000 MB |

When limits are reached:
- **Live cache**: Old tiles deleted automatically (LRU eviction)
- **Trip cache**: Download pauses automatically, can resume after freeing space

You'll receive warnings at 80% and 90% cache usage during downloads.

---

## Live Tile Cache

Separate from trip downloads, the app caches tiles as you browse:

### How It Works

1. You pan/zoom the map
2. Visible tiles are downloaded
3. Tiles saved to local cache
4. Future views load from cache

### Cache Behavior

- **Priority**: Trip tiles > Live tiles > Download
- **Eviction**: Least recently used tiles deleted when full
- **Coverage**: Only areas you've viewed

### Clearing Live Cache

If you need space:
1. Go to **Settings** > **Cache**
2. Tap **Clear Live Cache**
3. Confirm

This does not affect downloaded trips.

---

## Tile Download Settings

Fine-tune tile downloading in **Settings** > **Cache**:

| Setting | Purpose | Default |
|---------|---------|---------|
| Concurrent downloads | How many tiles at once | 2 |
| Request delay | Pause between requests | 100ms |

> **Note**: These settings respect OpenStreetMap's usage policy. Don't increase them excessively.

---

## Trip Planning Tips

### Before Your Trip

1. **Download on WiFi**: Save mobile data
2. **Verify download**: Open the trip and browse the area
3. **Check zoom levels**: Zoom in to ensure detail is cached
4. **Download relevant trips**: Get all trips for your destination

### During Your Trip

1. **Open trip at start**: Loads it into memory
2. **Use navigation**: Get turn-by-turn directions
3. **Check sidebar**: Quick access to all places
4. **Update when online**: Sync any new places

### After Your Trip

1. **Consider deleting**: Free up storage if no longer needed
2. **Keep favorites**: Downloaded trips ready for revisits
3. **View timeline**: See where you actually went

---

## Troubleshooting Trips

### Download Stuck

If download stops progressing:
1. Check internet connection
2. Wait for retry (automatic)
3. Cancel and restart if stuck
4. Try on WiFi instead of cellular

### Map Tiles Missing

If areas appear blank:
1. Zoom to the level that's missing
2. Wait briefly for tiles to load
3. If offline, you may not have that zoom level
4. Re-download the trip with more coverage

### Navigation Not Working

If navigation won't start:
1. Ensure trip is downloaded
2. Check GPS is enabled
3. Wait for initial GPS fix
4. Try a different place

### Segments Not Showing

If route lines are missing:
1. The trip may not have segments defined
2. Add segments in the web app
3. Re-download the trip

---

## Advanced: Custom Icons

Trip places can have custom icons. The app includes 63 icons in 5 colors:

### Icon Categories

- **Accommodation**: Hotel, hostel, camping
- **Food**: Restaurant, cafe, bar
- **Transport**: Airport, train, bus
- **Shopping**: Market, mall, store
- **Sightseeing**: Museum, monument, viewpoint
- **Nature**: Park, beach, mountain
- **Services**: Hospital, bank, post office

### Icon Colors

- Red, Blue, Green, Orange, Purple

Icons are set in the web app when creating places.

---

## Next Steps

- [Learn about location tracking](05-Location-Tracking.md)
- [Set up group sharing](06-Groups-and-Sharing.md)
- [Troubleshoot issues](07-Troubleshooting.md)
