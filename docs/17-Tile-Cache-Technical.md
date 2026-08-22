# Interactive Map Cache

Wayfarer Mobile displays the canonical OpenStreetMap standard raster layer while the user pans and zooms. The renderer requests only tiles needed for the visible map and its normal look-ahead.

Tiles are stored in the bounded live cache under the stable `osm-standard` namespace. Fresh entries are served without a network request. Expired entries are conditionally revalidated with `ETag` and `Last-Modified`; a `304 Not Modified` keeps the cached bytes and refreshes metadata, while a successful `200 OK` atomically replaces bytes and metadata. `Cache-Control` and `Expires` determine freshness. If neither contains a usable lifetime, the cache uses a seven-day fallback.

The cache coalesces concurrent requests for the same tile, but does not globally serialize distinct tiles. The Settings page retains controls for the live-cache size, usage refresh, and clearing the live cache.

Wayfarer Mobile does not proactively prefetch raster tiles or download raster maps for Trips. Downloaded Trip metadata, Places, Segments, Areas, navigation data, and other provider-independent content remain available independently of the interactive map cache.

Map data attribution: [© OpenStreetMap contributors](https://www.openstreetmap.org/copyright).
