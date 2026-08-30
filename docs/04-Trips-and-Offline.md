# Trips and Offline Content

Trips are planned journeys containing metadata, Places, Segments or routes, and Areas or polygons. Trips are created in the Wayfarer web app and can be downloaded to the mobile app for provider-independent offline use.

WayfarerMobile versions that include issue #236 preserve Wayfarer v1.9 ordered
intermediate Segment Places online and offline. Earlier mobile versions retain
the endpoints and effective route geometry, but do not provide full
intermediate-Place navigation or Start/Via/End presentation.

## Downloading a Trip

Tap **Download** on a Trip to store its metadata and geographic content locally. Downloaded content includes:

- Trip metadata and bounds
- Places and their details
- Segment and route geometry
- Areas and polygon geometry
- Navigation data supplied by the Trip

Raster basemap tiles are not part of a Trip download. Downloading a Trip therefore does not guarantee an offline basemap for its area.

## Viewing the Map

The map uses the standard OpenStreetMap layer during ordinary interactive pan and zoom. Tiles requested by the renderer are saved in the bounded live cache as they are viewed. Previously viewed tiles may remain available while cached, but the live cache is not an offline-area package and does not promise complete coverage.

The live cache can be inspected and cleared from **Settings** > **Map Cache**. Clearing it does not remove downloaded Trip data.

## Using Trip Content Offline

Without a network connection, downloaded Places, Segments, Areas, and Trip metadata remain available. Valid planned
Segment geometry remains the highest navigation authority. When it is unavailable, Mobile can reuse an exact retained
Wayfarer route previously authorized by the backend with storage mode `persistent`; otherwise it provides honest
Direct distance and bearing guidance. When a retained match exists, the focused chooser can use it offline, request
one fresh Wayfarer route, or choose Direct. Retained use performs no discovery, capability, or route request. A failed,
cancelled, or invalid fresh request leaves the prior retained guidance active and stored.
Direct is straight-line guidance, not road-aware turn-by-turn routing.

Retained Wayfarer routes are a separate bounded SQLite data set, not part of downloaded Trip geometry or the OSM live
tile cache. At most 200 are kept across all servers and local account partitions. Successful use refreshes local LRU
recency; routes do not expire merely because they are old. Malformed installed rows fail closed and do not refresh
recency. Clearing retained routes is idempotent, and older in-flight repository work cannot restore rows afterward. It does not change
Trips, Places, Segments, saved geometry, Timeline data or queues, credentials/settings, or OSM tiles.

Timeline data, queued locations, pending mutations, authentication state, and ordinary synchronization are independent of Trip downloads and the interactive map cache.

## Managing Downloaded Trips

Deleting downloaded Trip data removes the locally stored Trip and its provider-independent content. It does not alter the server copy. Updating or synchronizing a Trip refreshes its data without downloading raster tiles.

## Troubleshooting

- If Trip content is missing, reconnect and download or synchronize the Trip again.
- If the basemap is blank while offline, reconnect so the interactive renderer can request the required OpenStreetMap tiles.
- If navigation cannot obtain an online route, an exact retained route may be labeled **Wayfarer offline** with its age and attribution; otherwise use stored Segment geometry or Direct.

## Next Steps

- [Learn about location tracking](05-Location-Tracking.md)
- [Set up group sharing](06-Groups-and-Sharing.md)
- [Troubleshoot issues](07-Troubleshooting.md)
