# Phase 5: Hybrid Tile Caching & Trip Integration Design

## Overview

This document describes the design and implementation strategy for merging LiveTileCache (current location prefetch) with TripTileCache (downloaded trip areas) to create a seamless hybrid offline experience in Wayfarer Mobile.

## Problem Statement

Current implementation (Phases 1-4) only supports local area caching around current GPS location. Users cannot:

- Cache destinations before traveling
- Use offline maps in foreign countries/cities
- Have comprehensive coverage for planned trips

Phase 5 solves this by integrating web-planned trips with mobile offline caching.

---

## Architecture Overview

### Current State (Phase 4)

```
┌─────────────────┐    ┌─────────────────┐
│  LiveTileCache  │────│  OSM Tiles      │
│  (500m radius)  │    │  (Download)     │
└─────────────────┘    └─────────────────┘
         │
         ▼
┌─────────────────┐
│   Map Display   │
└─────────────────┘
```

### Target State (Phase 5)

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│  TripTileCache  │    │  LiveTileCache  │    │  OSM Tiles      │
│  (Trip Areas)   │    │  (Current Loc)  │    │  (Download)     │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 ▼
                    ┌─────────────────────────┐
                    │ UnifiedTileCacheService │
                    └─────────────────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │     Map Display         │
                    └─────────────────────────┘
```

---

## Core Components

### 1. UnifiedTileCacheService

**Purpose**: Smart tile source selection with priority system
**Location**: `Services/TileCache/UnifiedTileCacheService.cs`

```csharp
public class UnifiedTileCacheService
{
    // Priority: Trip → Live → Download
    public async Task<FileInfo?> GetTileAsync(int z, int x, int y, Location? currentLocation)

    // Detect if user is in downloaded trip area
    public async Task<Trip?> GetActiveTripAsync(Location currentLocation)

    // Adaptive prefetch based on trip context
    public async Task HybridPrefetchAsync(Location currentLocation)
}
```

### 2. TripTileCacheService

**Purpose**: Manage downloaded trip tiles and routing files
**Location**: `Services/TileCache/TripTileCacheService.cs`

```csharp
public class TripTileCacheService
{
    // Calculate and download tiles for trip area from OSM
    public async Task<bool> DownloadTripAsync(Trip trip, IProgress<DownloadProgress> progress)

    // Check if tile exists in any downloaded trip
    public async Task<FileInfo?> GetTripTileAsync(int z, int x, int y, Location location)

    // Calculate tile coordinates from trip metadata
    public List<TileCoordinate> CalculateRequiredTiles(Trip trip, int[] zoomLevels)

    // Manage trip storage and deletion
    public async Task DeleteTripAsync(int tripId)
    public async Task<List<DownloadedTrip>> GetDownloadedTripsAsync()
}
```

### 3. TripDownloadManager

**Purpose**: Handle trip download UI and progress
**Location**: `Services/Trip/TripDownloadManager.cs`

```csharp
public class TripDownloadManager
{
    // Download with progress tracking
    public async Task<bool> DownloadTripAsync(Trip trip, CancellationToken ct)

    // Background download management
    public async Task ResumeIncompleteDownloadsAsync()

    // Storage management
    public async Task<long> GetTotalTripCacheSizeAsync()
}
```

---

## User Experience Flows

### Scenario 1: Europe → Japan Trip

#### Pre-Travel (Europe)

```
1. User plans "Japan 2024" trip in web app
2. Mobile app fetches trip details from server
3. Mobile calculates required tiles from trip bounding box + places
4. User downloads Japan trip (tiles from OSM + routing from server)
5. Storage: /tiles/trips/japan-2024/
```

#### In Japan (Hybrid Mode)

```
1. GPS detects Tokyo location
2. System: "User is in Japan trip area"
3. Map switches to trip tiles (instant offline)
4. Live prefetch: Minimal (only zoom 17 gaps)
5. Navigation: Uses japan.routing files
```

#### Outside Trip Area

```
1. User travels to rural Japan (not in trip)
2. System: "User left trip coverage"
3. Map switches to full live prefetch mode
4. Downloads rural Japan tiles as needed
```

### Scenario 2: Local Area Enhancement

```
1. User at home with downloaded trip nearby
2. Live cache provides immediate surroundings
3. Trip cache provides broader city coverage
4. Seamless transition between sources
```

---

## Technical Implementation

### Phase 5.1: Core Infrastructure

**Tasks:**

- [ ] Create `UnifiedTileCacheService`
- [ ] Create `TripTileCacheService`
- [ ] Create `TripDownloadManager`
- [ ] Design trip storage schema
- [ ] Implement trip boundary detection

### Phase 5.2: Server Integration

**Tasks:**

- [ ] Implement `GET /api/trips` endpoint
- [ ] Implement `GET /api/trips/{id}` trip details
- [ ] Implement `POST /api/trips/{id}/tiles` tile list generation
- [ ] Implement `GET /api/routing/{region}.routing` file download
- [ ] Add trip bounding box calculations

### Phase 5.3: Mobile UI

**Tasks:**

- [ ] Create trip list page
- [ ] Create trip download UI with progress
- [ ] Add download management page
- [ ] Add offline indicators
- [ ] Create trip storage management

### Phase 5.4: Map Integration

**Tasks:**

- [ ] Replace OSM tile source with UnifiedTileSource
- [ ] Update MainPage prefetch logic
- [ ] Implement context switching (Live/Trip/Global)
- [ ] Add offline mode detection
- [ ] Update map indicators

### Phase 5.5: Testing & Optimization

**Tasks:**

- [ ] Test hybrid tile switching
- [ ] Optimize cache performance
- [ ] Test large trip downloads
- [ ] Verify offline navigation
- [ ] Performance testing

---

## Data Storage Structure

### File System Layout

```
/AppData/
├── tiles/
│   ├── live/                    # LiveTileCache (LRU evicted)
│   │   ├── 12/341/512.png
│   │   └── 13/682/1024.png
│   └── trips/                   # TripTileCache (permanent)
│       ├── trip-123/            # Japan 2024
│       │   ├── metadata.json
│       │   ├── tiles/
│       │   │   ├── 10/512/341.png
│       │   │   └── 11/1024/682.png
│       │   └── routing/
│       │       ├── japan-tokyo.routing
│       │       └── japan-osaka.routing
│       └── trip-456/            # Europe 2024
│           ├── metadata.json
│           └── tiles/...
└── wayfarer.db                 # SQLite metadata
```

### Database Schema

```sql
-- Existing LiveTile table (unchanged)
CREATE TABLE LiveTile (
    Id TEXT PRIMARY KEY,
    Zoom INTEGER,
    X INTEGER,
    Y INTEGER,
    FilePath TEXT,
    FileSizeBytes INTEGER,
    LastAccessUtc DATETIME
);

-- New trip-related tables
CREATE TABLE DownloadedTrip (
    Id INTEGER PRIMARY KEY,
    ServerId INTEGER,
    Name TEXT,
    BoundingBoxNorth REAL,
    BoundingBoxSouth REAL,
    BoundingBoxEast REAL,
    BoundingBoxWest REAL,
    DownloadedAt DATETIME,
    TotalSizeBytes INTEGER,
    Status TEXT -- 'downloading', 'complete', 'error'
);

CREATE TABLE TripTile (
    Id TEXT PRIMARY KEY,
    TripId INTEGER,
    Zoom INTEGER,
    X INTEGER,
    Y INTEGER,
    FilePath TEXT,
    FileSizeBytes INTEGER,
    FOREIGN KEY (TripId) REFERENCES DownloadedTrip(Id)
);
```

---

## API Specifications

### Mobile → Server Endpoints

#### Get User Trips

```http
GET /api/trips
Authorization: Bearer {token}

Response:
[
  {
    "id": 123,
    "name": "Japan 2024",
    "countries": ["Japan"],
    "cities": ["Tokyo", "Osaka"],
    "boundingBox": {
      "north": 35.8,
      "south": 34.6,
      "east": 139.8,
      "west": 135.4
    },
    "estimatedTileSize": "450 MB",
    "estimatedRoutingSize": "15 MB",
    "status": "ready_for_download"
  }
]
```

#### Get Trip Tile List

```http
POST /api/trips/{id}/tiles
Authorization: Bearer {token}
Content-Type: application/json

{
  "zoomLevels": [10, 11, 12, 13, 14, 15, 16, 17],
  "tileFormat": "png"
}

Response:
{
  "tiles": [
    {"zoom": 10, "x": 512, "y": 341},
    {"zoom": 11, "x": 1024, "y": 682},
    // ... thousands of tiles
  ],
  "routingFiles": [
    "japan-tokyo.routing",
    "japan-osaka.routing"
  ],
  "totalEstimatedSize": 467108864
}
```

#### Download Routing File

```http
GET /api/routing/{filename}
Authorization: Bearer {token}

Response: Binary routing file data
```

---

## Performance Considerations

### Memory Management

- **Trip tiles**: Loaded on-demand, not kept in memory
- **Live tiles**: Small memory footprint with LRU
- **Metadata**: SQLite indexes for fast boundary queries

### Storage Optimization

- **Compression**: PNG tiles (~15KB each)
- **Deduplication**: Same tiles shared between trips
- **Cleanup**: User-controlled trip deletion
- **Limits**: Per-trip size limits (server-enforced)

### Network Efficiency

- **Batch downloads**: Multiple tiles per request
- **Resume capability**: Partial download recovery
- **WiFi preference**: Large downloads on WiFi only
- **Progress tracking**: Real-time download progress

---

## Configuration Settings

### New Settings (SettingsStore.cs)

```csharp
/// <summary>
/// Maximum storage for all downloaded trips (MB)
/// </summary>
public static int MaxTripCacheTotalMB
{
    get => Preferences.Get("MaxTripCacheTotalMB", 2048); // 2GB default
    set => Preferences.Set("MaxTripCacheTotalMB", value);
}

/// <summary>
/// WiFi-only downloads for large trips
/// </summary>
public static bool WiFiOnlyTripDownloads
{
    get => Preferences.Get("WiFiOnlyTripDownloads", true);
    set => Preferences.Set("WiFiOnlyTripDownloads", value);
}

/// <summary>
/// Auto-delete old trips after days
/// </summary>
public static int AutoDeleteTripDays
{
    get => Preferences.Get("AutoDeleteTripDays", 90); // 3 months
    set => Preferences.Set("AutoDeleteTripDays", value);
}
```

---

## Testing Strategy

### Unit Tests

- [ ] Boundary detection algorithms
- [ ] Tile priority selection logic
- [ ] Trip metadata management
- [ ] Download progress tracking

### Integration Tests

- [ ] Live → Trip cache switching
- [ ] Large trip download/resume
- [ ] Offline map rendering
- [ ] Navigation with routing files

### User Acceptance Tests

- [ ] Europe → Japan scenario end-to-end
- [ ] Trip download on WiFi vs cellular
- [ ] Storage management and cleanup
- [ ] Performance with multiple trips

---

## Future Enhancements (Post-Phase 5)

### Smart Caching

- **Predictive prefetch**: Cache ahead of movement direction
- **Route-based caching**: Cache along planned routes
- **Popular area caching**: Pre-cache commonly visited areas

### Advanced Trip Features

- **Partial trip downloads**: Download only specific cities from trip
- **Trip sharing**: Share downloaded trips between devices
- **Offline updates**: Update trip tiles without full re-download

### Performance Optimizations

- **Vector tiles**: Switch from raster to vector for smaller downloads
- **Differential updates**: Only download changed tiles
- **Compression**: Advanced tile compression algorithms

---

## Success Metrics

### Technical Metrics

- **Cache hit ratio**: >90% for tiles in downloaded trips
- **Download success rate**: >95% for trip downloads
- **Storage efficiency**: <2GB for typical multi-country trip
- **Switching latency**: <100ms between cache sources

### User Experience Metrics

- **Offline usage**: % of map views that work offline
- **Download completion**: % of users who complete trip downloads
- **User satisfaction**: Offline feature ratings
- **Data savings**: Reduced cellular usage in trip areas

---

## Implementation Timeline

| Week | Phase | Deliverable |
|------|-------|------------|
| 1-2 | 5.1 | Core infrastructure classes |
| 3-4 | 5.2 | Server API integration |
| 5-6 | 5.3 | Mobile UI for trip management |
| 7-8 | 5.4 | Map integration and hybrid switching |
| 9-10 | 5.5 | Testing, optimization, and documentation |

---

## Conclusion

Phase 5 transforms Wayfarer Mobile from a local-area mapping app to a comprehensive global offline solution. By merging live location caching with trip-based downloads, users get the best of both worlds:

- **Immediate local coverage** through live prefetch
- **Comprehensive destination coverage** through trip downloads
- **Seamless switching** between cache sources
- **Efficient storage** with smart prioritization

This design maintains the simplicity of the current implementation while adding powerful trip-based offline capabilities that scale globally.
