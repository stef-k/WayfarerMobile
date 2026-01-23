# Tile Cache Technical Reference

This document provides technical details about how the tile caching system works, including the relationship between settings, cache limits, and download behavior.

---

## Overview

Wayfarer Mobile uses two separate tile caches:

| Cache | Purpose | Eviction Policy |
|-------|---------|-----------------|
| **Live Cache** | Tiles from map browsing and background prefetch | LRU (Least Recently Used) - automatic |
| **Trip Cache** | Tiles downloaded with trips for offline use | Manual - user must delete trips |

Both caches store tiles from zoom levels 8-17, covering regional overview to street-level detail.

---

## Settings Reference

### Cache Size Limits

| Setting | Default | Range | Effect |
|---------|---------|-------|--------|
| **Max Live Cache** | 500 MB | 100-2000 MB | When exceeded, oldest tiles are automatically deleted |
| **Max Trip Cache** | 2000 MB | 500-5000 MB | Downloads pause when limit reached |

### Live Cache Prefetch Radius

| Setting | Default | Range | Effect |
|---------|---------|-------|--------|
| **Prefetch Radius** | 5 | 1-10 | Number of tiles in each direction from center |

The prefetch radius creates a square grid of tiles centered on your location:
- Radius 1 = 3×3 grid = 9 tiles per zoom level
- Radius 5 = 11×11 grid = 121 tiles per zoom level
- Radius 10 = 21×21 grid = 441 tiles per zoom level

With 10 zoom levels (8-17), total tiles prefetched:
- Radius 1: ~90 tiles
- Radius 5: ~1,210 tiles
- Radius 10: ~4,410 tiles

### Download Throttling

| Setting | Default | Range | Purpose |
|---------|---------|-------|---------|
| **Concurrent Downloads** | 2 | 1-8 | Parallel tile requests |
| **Request Delay** | 100ms | 0-500ms | Pause between batches |

> **Note**: These settings respect OpenStreetMap's usage policy. Avoid aggressive settings.

---

## Live Cache: How It Works

### Prefetch Trigger

Live tile prefetching occurs when:
1. Your location changes significantly
2. You're connected to the internet
3. Offline caching is enabled in settings

### Prefetch Process

```
Location Update (lat, lon)
    │
    ├─► For each zoom level (8-17, priority ordered):
    │       │
    │       └─► Calculate center tile: LatLonToTile(lat, lon, zoom)
    │           │
    │           └─► For dx = -radius to +radius:
    │               For dy = -radius to +radius:
    │                   │
    │                   └─► If tile not already cached:
    │                       Add to download queue
    │
    └─► Download tiles (parallel, respecting concurrency limit)
```

### Zoom Level Priority

Tiles are prefetched in priority order, not sequential:
```
Priority: 15, 14, 16, 13, 12, 11, 10, 9, 8, 17
```
This prioritizes commonly-used navigation zoom levels (14-16) first.

### LRU Eviction

When the live cache exceeds its size limit:
1. System identifies least-recently-accessed tiles
2. Deletes oldest tiles until under limit
3. Eviction happens automatically in background

### Coverage Radius Calculation

The actual ground distance covered depends on zoom level and latitude:

```
Tile size (meters) = (40,075,016 / 2^zoom) × cos(latitude)
Coverage radius = prefetch_radius × √2 × tile_size
```

Example at latitude 14° (Philippines), radius 5:

| Zoom | Tile Size | Coverage Radius |
|------|-----------|-----------------|
| 15 | ~1.2 km | ~8.5 km |
| 16 | ~600 m | ~4.2 km |
| 17 | ~300 m | ~2.1 km |

---

## Trip Cache: How It Works

### Download Types

| Type | What's Downloaded | Use Case |
|------|-------------------|----------|
| **Metadata Only** | Places, segments, trip info | Quick sync, online use |
| **Full Download** | Metadata + map tiles | Complete offline use |

### Bounding Box Calculation

When downloading a trip, the app:
1. Checks `TripSummary.BoundingBox` (from trip list)
2. Falls back to `TripDetails.BoundingBox` (from trip details)
3. Falls back to stored bounding box (from previous download)
4. Finally calls `/api/trips/{id}/boundary` API endpoint

The server calculates the bounding box from all places, areas, and segment routes in the trip, adding a smart buffer based on trip scale.

### Adaptive Zoom Levels

To prevent excessive tile counts for large trips, the maximum zoom level adapts to the trip's geographic size:

| Area Size | Max Zoom | Typical Use |
|-----------|----------|-------------|
| > 100 sq° | 12 | Multiple countries |
| > 25 sq° | 13 | Country / large region |
| > 5 sq° | 14 | State / province |
| > 1 sq° | 15 | City / metropolitan area |
| > 0.1 sq° | 16 | Neighborhood |
| ≤ 0.1 sq° | 17 | Tiny area (max detail) |

**Why?** Each zoom level has 4× more tiles than the previous. Without adaptive limits:

| Zoom Range | Tiles (3.7 sq°) | Size (@ 80KB) |
|------------|-----------------|---------------|
| 8-15 | ~20,000 | ~1.6 GB |
| 8-16 | ~80,000 | ~6.4 GB |
| 8-17 | ~325,000 | ~26 GB |

### Tile Coordinate Calculation

For a bounding box, tiles are calculated using Web Mercator projection:

```csharp
// Convert lat/lon to tile coordinates
x = floor((longitude + 180) / 360 × 2^zoom)
y = floor((1 - log(tan(lat) + 1/cos(lat)) / π) / 2 × 2^zoom)

// Note: Y is inverted (North = lower Y, South = higher Y)
(minX, minY) = LatLonToTile(North, West, zoom)  // Top-left
(maxX, maxY) = LatLonToTile(South, East, zoom)  // Bottom-right
```

### Download Pause/Resume

Trip downloads support pause and resume:
- **User Pause**: Tap during download, resume anytime
- **Cache Limit**: Auto-pauses when limit reached
- **Network Loss**: Auto-pauses, resumes when connected
- **App Restart**: Resume from saved state

Download state is persisted including:
- Tiles completed
- Bytes downloaded
- Current position in tile list

### Cache Limit Warnings

During download, warnings appear at:
- **80%** of cache limit: Warning notification
- **90%** of cache limit: Critical warning
- **100%** of cache limit: Download pauses

---

## Cache Priority

When resolving a tile request, the system checks in order:

```
1. Trip Cache (downloaded trips)
   └─► If found: Use trip tile

2. Live Cache (browsing/prefetch)
   └─► If found: Use live tile, update access time

3. Network Download
   └─► If online: Fetch tile, save to live cache

4. Fallback
   └─► Return placeholder or empty
```

Trip tiles are never evicted automatically (user must delete trip).
Live tiles are evicted by LRU when cache is full.

---

## Storage Locations

Tiles are stored in the app's private data directory:

```
{AppData}/
├── live_tiles/
│   └── {zoom}/{x}/{y}.png
│
└── trip_tiles/
    └── {tripId}/
        └── {zoom}/{x}/{y}.png
```

Metadata is stored in SQLite databases:
- `wayfarer.db` - Trip metadata, places, segments
- `live_tile_cache.db` - Live tile index with access timestamps
- `trip_tiles.db` - Trip tile index

---

## Estimated Tile Sizes

Tile sizes vary significantly based on content:

| Content Type | Typical Size |
|--------------|--------------|
| Ocean / empty | 5-15 KB |
| Rural | 15-30 KB |
| Suburban | 30-50 KB |
| Urban | 50-90 KB |

The system uses 80 KB as a conservative estimate for download size predictions (biased toward urban areas where most trip activity occurs).

---

## Related Settings

All cache-related settings in **Settings > Cache**:

| Setting | Controls |
|---------|----------|
| Enable Offline Cache | Master switch for all caching |
| Live Cache Prefetch Radius | Grid size for background prefetch |
| Max Live Cache Size | Auto-eviction threshold |
| Max Trip Cache Size | Download pause threshold |
| Concurrent Downloads | Parallel tile requests |
| Request Delay | Throttle between requests |
| Clear Live Cache | Manual cache clear button |

---

## Troubleshooting

### "0 tiles" on Full Download

If a trip downloads with 0 tiles:
1. Check the trip has places with coordinates
2. Verify the bounding box API returns valid data
3. Check logs for tile calculation output

### Cache Not Filling

If live cache stays empty:
1. Verify "Enable Offline Cache" is on
2. Check internet connectivity
3. Ensure you're moving (triggers prefetch)
4. Check prefetch radius isn't 0

### Downloads Always Pause

If downloads pause immediately:
1. Check current cache usage in Settings
2. Delete old trips to free space
3. Increase Max Trip Cache Size

---

## Next Steps

- [Trips and Offline Maps](04-Trips-and-Offline.md) - User guide
- [Architecture](11-Architecture.md) - System design
- [Services](12-Services.md) - Service layer details
