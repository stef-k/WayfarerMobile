# Frequently Asked Questions

Quick answers to common questions about Wayfarer Mobile.

---

## General

### What is Wayfarer Mobile?

Wayfarer Mobile is a location tracking and trip planning companion app for the Wayfarer server. It allows you to:
- Build a personal timeline of your location history
- Navigate planned trips with offline maps
- Share your location with groups
- Check in at memorable places

### Do I need a server to use this app?

Yes. Wayfarer Mobile requires a Wayfarer server to sync your data. The app connects to your server using a QR code or manual configuration. Contact your organization's administrator if you don't have server access.

### Is Wayfarer Mobile free?

The app itself is open source (MIT license). However, you need access to a Wayfarer server, which may be self-hosted or provided by an organization.

### What platforms are supported?

- Android 7.0 (Nougat) and later
- iOS 15 and later

Windows and Mac are not supported.

---

## Setup and Connection

### How do I connect to my server?

The easiest way is to scan a QR code:

1. Get the QR code from your Wayfarer web app (Settings > Mobile App)
2. In the mobile app, tap "Scan QR Code"
3. Point your camera at the QR code
4. Connection is configured automatically

Alternatively, enter the server URL manually in settings.

### Why won't my QR code scan?

- Ensure good lighting
- Hold phone about 6 inches from the code
- Clean your camera lens
- Make sure the QR code is fully visible
- Try regenerating the QR code on your server

### Can I use multiple servers?

Not currently. The app connects to one server at a time. To switch servers, log out and scan a new QR code.

### How do I log out?

Go to Settings > Account > Logout. This clears your connection but preserves local settings like dark mode preference.

---

## Location Tracking

### What is timeline tracking?

Timeline tracking records your location history to your server. When enabled, the app runs in the background and periodically logs your position. You can view this history on the Wayfarer web app or in the Timeline tab.

### Does the app track me when it's closed?

Yes, if timeline tracking is enabled and you've granted background location permission. The app runs a background service (Android) or uses background location updates (iOS) to continue tracking even when the app isn't visible.

### How often does the app log my location?

The frequency depends on your server settings (time and distance thresholds). Typical settings are every 5 minutes or when you've moved 100 meters. More frequent updates use more battery.

### Can I stop tracking temporarily?

Yes. In Settings, toggle off "Enable Timeline Tracking". Your live location still works when the app is open, but no new locations are sent to the server.

### Does tracking use a lot of battery?

Battery usage varies:
- High performance mode (map visible): 5-10% per hour
- Normal background mode: 1-3% per hour
- Power saver mode: < 1% per hour

Actual usage depends on your device and settings.

---

## Privacy

### Who can see my location?

- **Your server**: Receives all locations you log
- **Group members**: Can see your locations if you're in shared groups
- **No one else**: Data is only on your server, not shared with third parties

### How do I stop sharing my location?

1. Disable timeline tracking in Settings
2. Leave groups via the web app
3. Clear local data in Settings > Data

### Is my data secure?

- All communication with the server uses HTTPS
- Your QR code/API token is stored securely on device
- PIN lock adds an extra layer of protection
- No data is sent to third parties

### What data does the app collect?

The app only collects:
- GPS coordinates and timestamps
- GPS accuracy and speed
- Activity type (if you specify during check-in)

It does NOT collect:
- Contacts, messages, or call logs
- Photos (except camera for QR scanning)
- App usage or browsing history
- Personal information beyond location

---

## Maps and Offline

### What map does the app use?

The app uses OpenStreetMap tiles via the Mapsui library. Maps are rendered from raster tiles (images) downloaded from OSM tile servers.

### Can I use the app offline?

Partially. Offline capabilities include:
- Viewing downloaded trip maps
- GPS location (always works)
- Queuing locations for later sync
- Basic navigation with downloaded trips

Requires internet:
- Viewing areas you haven't cached
- Syncing timeline to server
- Group location sharing
- Downloading new trips

### How do I download maps for offline use?

Maps are downloaded automatically when you download a trip:

1. Go to Trips tab
2. Tap Download on a trip
3. Wait for download to complete

This caches all map tiles for the trip area at multiple zoom levels.

### How much storage do offline maps use?

Depends on the trip size:
- Small city trip: 50-200 MB
- Large city: 200-500 MB
- Multi-city trip: 500+ MB

You can configure cache limits in Settings.

---

## Trips

### How do I create a trip?

Trips are created in the Wayfarer web application. The mobile app is for viewing, navigating, and minimally editing trips you've already created.

### Can I edit trips on mobile?

Basic editing is available for on-the-go updates:
- View place details
- Add/edit notes on places
- Update basic trip information

Full trip editing (adding places, creating segments, detailed planning) is done on the web.

### Why doesn't navigation show a route line?

Navigation only shows detailed routes if:
- The trip has segments defined (created on web)
- Or OSRM can calculate a route (requires internet)
- Without either, you get direct bearing/distance guidance

### What's the 50-meter rule?

When navigating, the app uses trip segments when you're within ~50 meters of a trip place. Beyond that distance, it calculates a direct route. This ensures you get the planned route when you're following the trip.

---

## Groups

### How do I create a group?

Groups are created in the Wayfarer web application. In the mobile app, you can only view groups you've already joined.

### How do I join a group?

Accept an invitation through the Wayfarer web app. Once joined, the group appears in your mobile app automatically.

### Why can't I see other members' locations?

Check that:
- You're viewing "Today" for live updates
- Members have timeline tracking enabled
- Members have recently logged locations
- You have internet connection

### How real-time are group updates?

Very real-time when viewing "Today". The app uses Server-Sent Events (SSE) to receive instant updates when group members log new locations.

---

## Technical

### Why does the app need background location?

Background location ("Allow all the time") is required for 24/7 timeline tracking. Without it, the app can only track your location when you're actively using it.

### Why does Android show a notification?

Android requires a persistent notification for any app running a background service. This notification shows tracking status and cannot be dismissed while tracking is active. It's an Android system requirement, not an app design choice.

### What's the difference between live cache and trip cache?

- **Live cache**: Tiles downloaded while browsing the map, automatically managed (LRU eviction)
- **Trip cache**: Tiles deliberately downloaded for specific trips, permanent until you delete the trip

### Does the app use Google Play Services?

On Android with Play Services, the app uses Google's Fused Location Provider for better accuracy. On devices without Play Services (Huawei etc.), it falls back to standard Android location APIs.

### How do I enable debug/diagnostic mode?

Go to Settings > About > Diagnostics to view detailed information about:
- Location queue status
- Tile cache statistics
- Tracking service health
- Performance metrics

---

## Troubleshooting

### The app won't start / keeps crashing

1. Clear app data: Settings > Apps > Wayfarer > Clear Data
2. Reinstall the app
3. Ensure sufficient storage space
4. Update to latest app version

### My timeline has gaps

Common causes:
- Tracking was disabled
- Device killed background service (battery optimization)
- Poor GPS signal indoors
- Server sync issues

See [Troubleshooting](07-Troubleshooting.md) for detailed solutions.

### Battery drain is too high

1. Check tracking frequency settings
2. Disable timeline tracking when not needed
3. Use normal mode instead of high performance
4. Keep the app updated

### How do I report a bug?

1. Note the steps to reproduce
2. Export diagnostic report from Settings > About > Diagnostics
3. Contact your server administrator
4. Include device model, OS version, and app version

---

## Miscellaneous

### Can I use this with other tracking apps?

Yes, Wayfarer Mobile runs independently. However, multiple tracking apps will increase battery usage.

### Is there a web version?

Yes, the Wayfarer web application provides full access to your timeline, trips, groups, and settings. The mobile app is designed as a companion for on-the-go use.

### How do I delete my data?

- **Local data**: Settings > Data > Clear All Data
- **Server data**: Since Wayfarer is self-hosted, manage your data via the web dashboard

### What open source licenses does the app use?

Go to Settings > About to see attribution for:
- OpenStreetMap (map data)
- Mapsui (map rendering)
- Syncfusion MAUI Toolkit (UI components)
- Other open source libraries

### I have a feature request

**Issues, Ideas & PRs**

This is a spare-time project. I'll improve it when I can, but there's no guaranteed schedule.

- **Issues & feature requests**: Please open them on GitHub - I'll read when I can
- **Pull requests**: Welcomed, but reviews may be delayed
- **To improve your chances**: Keep PRs small and focused, explain motivation and user impact

This project is MIT-licensed and provided "as is" without warranty.

---

## Still Have Questions?

If your question isn't answered here:

1. Check the full [Troubleshooting](07-Troubleshooting.md) guide
2. Review other documentation sections
3. Export diagnostics for technical support
4. Check the web dashboard or GitHub discussions
