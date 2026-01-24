# Location Tracking

This guide explains how Wayfarer Mobile tracks your location, privacy controls, and battery considerations.

---

## How Tracking Works

Wayfarer Mobile uses a background service to continuously track your location.

### How the App Tracks Your Location

On both Android and iOS, the app runs a background service that:

1. **Acquires GPS coordinates** using your device's location services
2. **Filters low-accuracy readings** (only keeps locations accurate to within 100m)
3. **Applies thresholds** to avoid logging every tiny movement
4. **Saves locations locally** until they can be synced to your server
5. **Syncs to server** when you're online

On Android, a notification shows that tracking is active. On iOS, a blue bar appears when the app is using your location in the background.

---

## Timeline Tracking vs Live Location

The app separates two concepts:

### Timeline Tracking

- Records your location history to the server
- Controlled by the settings toggle
- Creates your personal timeline on the web
- Runs 24/7 when enabled

### Live Location

- Shows your position on the map
- Always works when the app is open
- Not affected by timeline tracking setting
- Used for navigation and trip progress

| Timeline Setting | GPS Active | Server Logging | Map Display |
|------------------|------------|----------------|-------------|
| ON | Yes | Yes | Yes |
| OFF (app open) | Yes | No | Yes |
| OFF (app closed) | No | No | N/A |

---

## Enabling Timeline Tracking

### First-Time Setup

During onboarding, you can enable timeline tracking by:
1. Granting background location permission
2. Allowing battery optimization exemption
3. Completing server setup

### Manual Enable/Disable

1. Go to **Settings**
2. Find **Timeline Tracking** section
3. Toggle **Enable Timeline Tracking**

When enabled:
- Foreground service starts (Android)
- Notification shows tracking status
- Locations sync to server

When disabled:
- Background tracking stops
- No new locations logged to server
- Map still shows your position when app is open

---

## Location Thresholds

To save battery and storage, not every GPS reading is saved. The server defines thresholds:

### Time Threshold

Minimum time between logged locations. Default: **5 minutes** (server-configurable).

| Setting | Effect |
|---------|--------|
| 1 min | Frequent logging, more detail |
| 5 min | Balanced (default setting) |
| 15 min | Less frequent, saves battery |

### Distance Threshold

Minimum distance between logged locations. Default: **15 meters** (server-configurable).

| Setting | Effect |
|---------|--------|
| 15m | Detailed tracking (default) |
| 50m | Moderate detail |
| 100m | Coarse tracking |

### How Filtering Works (AND Logic)

A location is logged only if **BOTH** conditions are met:
- Enough time has passed since last log, **AND**
- You've moved enough distance

Example with 5 min / 15m thresholds:
- You log at 10:00 at Point A
- At 10:02, you're at Point B (50m away) - NOT logged (only 2 min passed)
- At 10:05, still at Point B - logged (5 min passed AND 50m > 15m)
- At 10:10, at Point B - NOT logged (0m moved, fails distance threshold)

---

## GPS Accuracy Filtering

Low-quality GPS readings are discarded:

### Quality Criteria

| Metric | Filter |
|--------|--------|
| Accuracy | Must be < 100m (configurable) |
| Age | Must be recent (not stale) |
| Altitude | Checked for reasonableness |

### Accuracy Indicator

In the timeline, accuracy is color-coded:

| Color | Accuracy | Quality |
|-------|----------|---------|
| Green | < 10m | Excellent |
| Yellow | 10-30m | Good |
| Orange | 30-50m | Fair |
| Red | > 50m | Poor |

---

## Performance Modes

The service adjusts GPS frequency based on context:

### High Performance Mode

When: App is open on main map or during navigation

- **Update interval**: 1 second
- **Accuracy**: High (GPS preferred)
- **Battery**: Higher usage
- **Purpose**: Real-time map updates

### Normal Mode (Sleep/Wake Optimization)

When: App is in background or on other pages

Normal mode uses a **three-phase sleep/wake optimization** to save battery while ensuring high-accuracy GPS fixes for server logging:

```
┌─────────────────────────────────────────────────────────────────┐
│                    5-Minute Threshold Example                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  0:00       1:20       2:00       3:20       4:20    5:00        │
│    │         │          │          │          │        │          │
│    ▼         ▼          ▼          ▼          ▼        ▼          │
│  LOG ════════╪══════════╪══════════╪══════════╪════════╪ LOG     │
│    │         │          │          │          │        │          │
│    │◄───────►│◄────────►│◄────────►│◄────────►│        │          │
│    │  Deep   │  Deep    │ Approach │   Wake   │(early  │          │
│    │  Sleep  │  Sleep   │  (100s)  │  (100s)  │ stop)  │          │
│    │ Balanced│ Balanced │ Balanced │HighAccur │Balanced│          │
│    │  ~100m  │  ~100m   │  ~100m   │  ≤20m    │ stored │          │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

#### Phase 1: Deep Sleep (>200s before threshold)
- **Priority**: Balanced (cell/WiFi positioning)
- **Interval**: Long (~100s for 5-min threshold)
- **Accuracy**: ~100m (acceptable, not logged)
- **Battery**: Minimal usage

#### Phase 2: Approach (100-200s before threshold)
- **Priority**: Balanced
- **Interval**: 1 second (prepare for wake)
- **Purpose**: Shorter polling to catch the wake window reliably
- **Battery**: Low usage

#### Phase 3: Wake (≤100s before threshold)
- **Priority**: HighAccuracy (GPS)
- **Interval**: 1 second (collect many samples)
- **Accuracy**: Target ≤20m (excellent GPS)
- **Purpose**: Acquire high-accuracy fix for logging (100s allows for cold start GPS)
- **Best location tracking**: Keeps the best GPS fix seen during wake

#### Early GPS Shutoff

When excellent GPS accuracy (≤20m) is acquired during wake phase:
- **Action**: Immediately switch to Balanced mode (GPS off)
- **Stored sample**: Best GPS fix is stored for use at threshold time
- **Battery savings**: Typically saves 80+ seconds of GPS usage per cycle
- **Result**: At threshold time, stored sample is logged with accurate timestamp

#### Bad GPS Handling

If GPS accuracy doesn't reach ≤20m within the wake phase:
- **At threshold time**: Logs best available sample regardless of accuracy
- **No delay**: Logging always occurs at threshold time, never later
- **Fallback priority**: Best stored sample > current reading
- **Result**: Logs whatever accuracy is available, then sleeps

> **Note**: Even with poor GPS (>100m), the app logs at threshold time rather than skipping. A coarse location is better than no location for timeline continuity.

### Power Saver Mode

When: Battery drops below 20%

- **Update interval**: 5 minutes
- **Accuracy**: Reduced
- **Battery**: Minimal usage
- **Purpose**: Extend battery life

---

## Battery Considerations

Background location tracking uses battery. Here's how to optimize:

### Expected Battery Usage

| Mode | Approximate Usage |
|------|-------------------|
| High Performance | 5-10% per hour |
| Normal | 1-3% per hour |
| Power Saver | < 1% per hour |

### Battery Optimization Tips

1. **Use default thresholds**: More frequent updates use more battery
2. **Disable when not needed**: Turn off timeline tracking temporarily
3. **Keep app updated**: Newer versions often improve efficiency
4. **Use WiFi when possible**: Less battery than cellular

### Battery Monitor

The app monitors battery and automatically:
- Warns when battery is low
- Can auto-pause tracking on critical battery
- Resumes when charging

Configure in **Settings** > **Advanced**.

---

## Location Queue and Sync

Locations are queued locally before syncing to the server.

### Queue Behavior

1. GPS provides location
2. Passes accuracy filter (<100m by default, configurable)
3. Passes threshold filter (time AND distance)
4. Added to local SQLite queue
5. Queue syncs when online via continuous drain loop

### Queue Limits

- **Default queue size**: 25,000 locations (configurable 1-100,000)
- **Automatic cleanup**: When limit exceeded, oldest synced/rejected locations are purged first
- **Rolling buffer**: If still over limit, oldest pending entries are removed (never entries currently syncing)

### Sync Schedule

- **Online**: Continuous drain loop processes queue until empty
- **Rate limit**: One location every 12 seconds (server allows 10s, 2s safety margin)
- **Batch size**: Up to 100 locations per claim cycle
- **Retry**: Automatic with exponential backoff
- **Offline**: Queue grows until reconnected
- **Sync time**: ~17 minutes for 100 queued locations (improved from ~50 minutes)

### Managing the Queue

Go to **Settings** > **Offline Queue** to manage your location queue:

**Status Display:**
- **Total/Pending/Synced/Rejected counts**: See queue composition at a glance
- **Health status**: Color-coded (Green=Healthy, Orange=Warning, Red=Critical/Over Limit)
- **Coverage span**: Time range covered by queued locations
- **Remaining headroom**: Estimated time until queue fills (based on your time threshold)

**Queue Limit:**
- Configurable from 1 to 100,000 locations
- Storage warning shown above 50,000 entries
- Lowering below current count prompts confirmation if pending data will be lost

**Export Options:**
- **Export CSV**: Download all queued locations as spreadsheet
- **Export GeoJSON**: Download as geographic data format
- Formula injection protection applied to exports

**Clear Actions:**
| Action | What's Cleared | Confirmation |
|--------|---------------|--------------|
| Clear Synced | Synced + rejected entries | Yes |
| Clear Pending | Unsynced entries (data loss) | Yes (with count) |
| Clear All | Entire queue | Yes (with warning if pending) |

### Legacy Diagnostics Access

The Diagnostics page (**Settings** > **About** > **Diagnostics**) shows read-only queue information and links to Settings for full management.

---

## Privacy Controls

You have control over your location data:

### Stop Tracking

Disable timeline tracking in settings to stop all server logging.

### Clear Local Data

In **Settings** > **Data**:
- **Clear Location Queue**: Delete unsent locations
- **Clear All Data**: Remove all local data

### Server-Side Privacy

Contact your server administrator for:
- Deleting historical data
- Adjusting retention periods
- Privacy policy information

### What Data is Stored

| Data | Local | Server |
|------|-------|--------|
| Coordinates | Yes | Yes (if synced) |
| Timestamp | Yes | Yes |
| Accuracy | Yes | Yes |
| Speed | Yes | Yes |
| Device info | No | No |
| Other apps | No | No |

---

## Notification (Android)

Android requires a notification for background services:

### Notification Content

The tracking notification shows:
- **Title**: "Wayfarer Mobile"
- **Text**: "Tracking active" or "Paused"
- **Last update**: Time of last GPS reading

### Notification Actions

Quick actions in the notification:
- **Pause**: Temporarily stop tracking
- **Resume**: Restart after pause
- **Open**: Launch the app

### Managing the Notification

The notification cannot be dismissed while tracking is active (Android requirement). To hide it:
1. Stop timeline tracking in settings, or
2. Use Android notification settings to minimize

---

## iOS Background Tracking

iOS handles background location differently:

### Location Authorization

- **When in Use**: Only tracks when app is visible
- **Always**: Tracks in background (required for timeline)

### Background Indicator

When tracking in background:
- Blue banner at top of screen
- "Wayfarer is using your location"

### iOS Battery Optimizations

iOS may reduce tracking frequency to save battery. To improve:
1. Use the app frequently
2. Keep significant-change monitoring enabled
3. Avoid force-closing the app

---

## Troubleshooting Tracking

### Tracking Stops Randomly

1. Check battery optimization is disabled for the app
2. Verify all permissions are granted
3. Don't force-close the app
4. See device-specific settings in [Installation Guide](02-Installation.md)

### GPS Inaccurate

1. Go outside (buildings block GPS)
2. Wait for GPS to "warm up" (30-60 seconds)
3. Check location permissions
4. Restart the app

### Locations Not Syncing

1. Check internet connection
2. Verify server URL is correct
3. Try logging out and back in
4. Check queue in diagnostics

### High Battery Drain

1. Check you're not in High Performance mode constantly
2. Reduce update frequency via server settings
3. Enable power saver mode
4. Disable tracking when not needed

---

## Advanced Settings

Power users can access additional settings in **Settings** > **Advanced**:

| Setting | Purpose |
|---------|---------|
| GPS timeout | How long to wait for fix |
| Accuracy requirement | Minimum accuracy to accept |
| Batch size | Locations per sync request |
| Sync interval | How often to attempt sync |

> **Warning**: Changing advanced settings may affect reliability or battery life.

---

## Next Steps

- [Learn about group sharing](06-Groups-and-Sharing.md)
- [Troubleshoot issues](07-Troubleshooting.md)
- [View FAQ](08-FAQ.md)
