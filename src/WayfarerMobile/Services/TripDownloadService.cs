using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for downloading and managing offline trips.
/// </summary>
public class TripDownloadService
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TripDownloadService> _logger;

    // Tile download configuration
    private static readonly int[] DownloadZoomLevels = { 10, 11, 12, 13, 14, 15, 16 };
    private const int TileTimeoutMs = 10000;
    private const long EstimatedTileSizeBytes = 15000; // ~15KB average
    private const long MinRequiredSpaceMB = 50; // Minimum free space required

    // Rate limiting state
    private readonly object _rateLimitLock = new();
    private DateTime _lastRequestTime = DateTime.MinValue;

    // Configurable settings (read from ISettingsService)
    private int MaxConcurrentDownloads => _settingsService.MaxConcurrentTileDownloads;
    private int MinRequestDelayMs => _settingsService.MinTileRequestDelayMs;

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Creates a new instance of TripDownloadService.
    /// </summary>
    public TripDownloadService(
        IApiClient apiClient,
        DatabaseService databaseService,
        ISettingsService settingsService,
        ILogger<TripDownloadService> logger)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Downloads a trip for offline access (metadata and places).
    /// </summary>
    /// <param name="tripSummary">The trip summary to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The downloaded trip entity.</returns>
    public async Task<DownloadedTripEntity?> DownloadTripAsync(
        TripSummary tripSummary,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting download for trip: {TripName}", tripSummary.Name);

            // Check network connectivity
            if (!IsNetworkAvailable())
            {
                _logger.LogWarning("Cannot download trip - no network connection");
                return null;
            }

            // Check storage space
            if (!await HasSufficientStorageAsync())
            {
                _logger.LogWarning("Cannot download trip - insufficient storage space");
                return null;
            }

            // Check if already downloaded
            var existing = await _databaseService.GetDownloadedTripByServerIdAsync(tripSummary.Id);
            if (existing != null && existing.Status == TripDownloadStatus.Complete)
            {
                _logger.LogInformation("Trip already downloaded: {TripName}", tripSummary.Name);
                return existing;
            }

            // Create or update trip entity
            var tripEntity = existing ?? new DownloadedTripEntity
            {
                ServerId = tripSummary.Id,
                Name = tripSummary.Name,
                DownloadedAt = DateTime.UtcNow
            };

            tripEntity.Status = TripDownloadStatus.Downloading;
            tripEntity.ProgressPercent = 0;

            // Set bounding box
            if (tripSummary.BoundingBox != null)
            {
                tripEntity.BoundingBoxNorth = tripSummary.BoundingBox.North;
                tripEntity.BoundingBoxSouth = tripSummary.BoundingBox.South;
                tripEntity.BoundingBoxEast = tripSummary.BoundingBox.East;
                tripEntity.BoundingBoxWest = tripSummary.BoundingBox.West;
            }

            await _databaseService.SaveDownloadedTripAsync(tripEntity);
            RaiseProgress(tripEntity.Id, 10, "Fetching trip details...");

            // Fetch full trip details
            var tripDetails = await _apiClient.GetTripDetailsAsync(tripSummary.Id, cancellationToken);
            if (tripDetails == null)
            {
                tripEntity.Status = TripDownloadStatus.Failed;
                tripEntity.LastError = "Failed to fetch trip details";
                await _databaseService.SaveDownloadedTripAsync(tripEntity);
                return null;
            }

            RaiseProgress(tripEntity.Id, 25, "Saving regions...");

            // Convert and save areas/regions
            var areas = tripDetails.Regions.Select((r, index) => new OfflineAreaEntity
            {
                ServerId = r.Id,
                Name = r.Name,
                SortOrder = r.SortOrder > 0 ? r.SortOrder : index,
                PlaceCount = r.Places.Count
            }).ToList();

            await _databaseService.SaveOfflineAreasAsync(tripEntity.Id, areas);
            tripEntity.AreaCount = areas.Count;

            RaiseProgress(tripEntity.Id, 30, "Saving places...");

            // Convert and save places with region info
            var places = new List<OfflinePlaceEntity>();
            int placeIndex = 0;
            foreach (var region in tripDetails.Regions)
            {
                foreach (var place in region.Places)
                {
                    places.Add(new OfflinePlaceEntity
                    {
                        ServerId = place.Id,
                        RegionId = region.Id,
                        RegionName = region.Name,
                        Name = place.Name,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                        Notes = place.Notes,
                        IconName = place.Icon,
                        MarkerColor = place.MarkerColor,
                        SortOrder = place.SortOrder > 0 ? place.SortOrder : placeIndex++
                    });
                }
            }

            await _databaseService.SaveOfflinePlacesAsync(tripEntity.Id, places);
            tripEntity.PlaceCount = places.Count;

            RaiseProgress(tripEntity.Id, 40, "Saving segments...");

            // Convert and save segments
            var segments = tripDetails.Segments.Select((s, index) => new OfflineSegmentEntity
            {
                ServerId = s.Id,
                OriginId = s.OriginId,
                DestinationId = s.DestinationId,
                TransportMode = s.TransportMode,
                DistanceKm = s.DistanceKm,
                DurationMinutes = s.DurationMinutes,
                Geometry = s.Geometry,
                SortOrder = index
            }).ToList();

            await _databaseService.SaveOfflineSegmentsAsync(tripEntity.Id, segments);
            tripEntity.SegmentCount = segments.Count;

            tripEntity.ProgressPercent = 50;
            await _databaseService.SaveDownloadedTripAsync(tripEntity);

            RaiseProgress(tripEntity.Id, 50, $"Saved {places.Count} places, {segments.Count} segments");

            // Get bounding box for tile download
            var boundingBox = tripSummary.BoundingBox ?? tripDetails.BoundingBox;
            if (boundingBox != null)
            {
                // Download tiles for offline map
                RaiseProgress(tripEntity.Id, 55, "Calculating tiles...");

                var tileCoords = CalculateTilesForBoundingBox(boundingBox);
                _logger.LogInformation("Trip {TripName} requires {TileCount} tiles", tripSummary.Name, tileCoords.Count);

                if (tileCoords.Count > 0)
                {
                    var downloadedBytes = await DownloadTilesAsync(
                        tripEntity.Id,
                        tileCoords,
                        cancellationToken);

                    tripEntity.TileCount = tileCoords.Count;
                    tripEntity.TotalSizeBytes = downloadedBytes;
                }

                tripEntity.Status = TripDownloadStatus.Complete;
            }
            else
            {
                // No bounding box - metadata only
                tripEntity.Status = TripDownloadStatus.MetadataOnly;
                _logger.LogWarning("No bounding box for trip {TripName}, skipping tiles", tripSummary.Name);
            }

            // Store version for sync tracking
            tripEntity.Version = tripDetails.Version;
            tripEntity.ServerUpdatedAt = DateTime.UtcNow;
            tripEntity.ProgressPercent = 100;
            await _databaseService.SaveDownloadedTripAsync(tripEntity);

            RaiseProgress(tripEntity.Id, 100, "Download complete");
            _logger.LogInformation("Trip downloaded: {TripName} ({PlaceCount} places, {TileCount} tiles, v{Version})",
                tripSummary.Name, places.Count, tripEntity.TileCount, tripEntity.Version);

            return tripEntity;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Trip download cancelled: {TripName}", tripSummary.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download trip: {TripName}", tripSummary.Name);

            // Update status to failed
            var tripEntity = await _databaseService.GetDownloadedTripByServerIdAsync(tripSummary.Id);
            if (tripEntity != null)
            {
                tripEntity.Status = TripDownloadStatus.Failed;
                tripEntity.LastError = ex.Message;
                await _databaseService.SaveDownloadedTripAsync(tripEntity);
            }

            return null;
        }
    }

    /// <summary>
    /// Gets all downloaded trips.
    /// </summary>
    public async Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync()
    {
        return await _databaseService.GetDownloadedTripsAsync();
    }

    /// <summary>
    /// Checks if a trip is downloaded.
    /// </summary>
    public async Task<bool> IsTripDownloadedAsync(Guid tripId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripId);
        return trip != null &&
               (trip.Status == TripDownloadStatus.Complete ||
                trip.Status == TripDownloadStatus.MetadataOnly);
    }

    /// <summary>
    /// Gets offline places for a downloaded trip.
    /// </summary>
    public async Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
            return new List<TripPlace>();

        var entities = await _databaseService.GetOfflinePlacesAsync(trip.Id);
        return entities.Select(e => new TripPlace
        {
            Id = e.ServerId,
            Name = e.Name,
            Latitude = e.Latitude,
            Longitude = e.Longitude,
            Notes = e.Notes,
            Icon = e.IconName,
            MarkerColor = e.MarkerColor,
            SortOrder = e.SortOrder
        }).ToList();
    }

    /// <summary>
    /// Gets complete offline trip details for navigation.
    /// Returns a TripDetails object populated from offline storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Complete trip details or null if not downloaded.</returns>
    public async Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Trip not found in offline storage: {TripId}", tripServerId);
            return null;
        }

        // Load all offline data in parallel
        var placesTask = _databaseService.GetOfflinePlacesAsync(trip.Id);
        var segmentsTask = _databaseService.GetOfflineSegmentsAsync(trip.Id);
        var areasTask = _databaseService.GetOfflineAreasAsync(trip.Id);

        await Task.WhenAll(placesTask, segmentsTask, areasTask);

        var placeEntities = await placesTask;
        var segmentEntities = await segmentsTask;
        var areaEntities = await areasTask;

        // Build regions with places
        var regions = new List<TripRegion>();
        var placesByRegion = placeEntities.GroupBy(p => p.RegionId ?? Guid.Empty);

        foreach (var regionGroup in placesByRegion)
        {
            var area = areaEntities.FirstOrDefault(a => a.ServerId == regionGroup.Key);
            var region = new TripRegion
            {
                Id = regionGroup.Key,
                Name = area?.Name ?? regionGroup.First().RegionName ?? "Places",
                SortOrder = area?.SortOrder ?? 0,
                Places = regionGroup.Select(p => new TripPlace
                {
                    Id = p.ServerId,
                    Name = p.Name,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    Notes = p.Notes,
                    Icon = p.IconName,
                    MarkerColor = p.MarkerColor,
                    SortOrder = p.SortOrder
                }).OrderBy(p => p.SortOrder).ToList()
            };
            regions.Add(region);
        }

        // Build segments
        var segments = segmentEntities.Select(s => new TripSegment
        {
            Id = s.ServerId,
            OriginId = s.OriginId,
            DestinationId = s.DestinationId,
            TransportMode = s.TransportMode,
            DistanceKm = s.DistanceKm,
            DurationMinutes = s.DurationMinutes,
            Geometry = s.Geometry
        }).ToList();

        // Build trip details
        var tripDetails = new TripDetails
        {
            Id = trip.ServerId,
            Name = trip.Name,
            BoundingBox = new BoundingBox
            {
                North = trip.BoundingBoxNorth,
                South = trip.BoundingBoxSouth,
                East = trip.BoundingBoxEast,
                West = trip.BoundingBoxWest
            },
            Regions = regions.OrderBy(r => r.SortOrder).ToList(),
            Segments = segments
        };

        _logger.LogInformation("Loaded offline trip: {TripName} ({PlaceCount} places, {SegmentCount} segments)",
            trip.Name, placeEntities.Count, segmentEntities.Count);

        return tripDetails;
    }

    /// <summary>
    /// Gets offline segments for a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip segments.</returns>
    public async Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
            return new List<TripSegment>();

        var entities = await _databaseService.GetOfflineSegmentsAsync(trip.Id);
        return entities.Select(s => new TripSegment
        {
            Id = s.ServerId,
            OriginId = s.OriginId,
            DestinationId = s.DestinationId,
            TransportMode = s.TransportMode,
            DistanceKm = s.DistanceKm,
            DurationMinutes = s.DurationMinutes,
            Geometry = s.Geometry
        }).ToList();
    }

    /// <summary>
    /// Deletes a downloaded trip.
    /// </summary>
    public async Task DeleteTripAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip != null)
        {
            await _databaseService.DeleteDownloadedTripAsync(trip.Id);
            _logger.LogInformation("Deleted trip: {TripId}", tripServerId);
        }
    }

    #region Trip Sync/Update

    /// <summary>
    /// Checks if a downloaded trip needs updating based on server version.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>True if update is available, false otherwise.</returns>
    public async Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId)
    {
        try
        {
            var localTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
            if (localTrip == null)
                return false;

            if (!IsNetworkAvailable())
                return false;

            // Fetch current trip summary from server
            var serverTrip = await _apiClient.GetTripDetailsAsync(tripServerId);
            if (serverTrip == null)
                return false;

            var needsUpdate = serverTrip.Version > localTrip.Version;
            if (needsUpdate)
            {
                _logger.LogInformation("Trip {TripName} needs update: local v{LocalVersion} < server v{ServerVersion}",
                    localTrip.Name, localTrip.Version, serverTrip.Version);
            }

            return needsUpdate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check update for trip {TripId}", tripServerId);
            return false;
        }
    }

    /// <summary>
    /// Syncs a downloaded trip with the server (updates places, segments, areas without re-downloading tiles).
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="forceSync">If true, sync regardless of version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated trip entity or null if sync failed.</returns>
    public async Task<DownloadedTripEntity?> SyncTripAsync(
        Guid tripServerId,
        bool forceSync = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var localTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
            if (localTrip == null)
            {
                _logger.LogWarning("Cannot sync trip {TripId} - not downloaded", tripServerId);
                return null;
            }

            if (!IsNetworkAvailable())
            {
                _logger.LogWarning("Cannot sync trip - no network connection");
                return null;
            }

            _logger.LogInformation("Starting sync for trip: {TripName}", localTrip.Name);
            RaiseProgress(localTrip.Id, 5, "Checking for updates...");

            // Fetch full trip details from server
            var serverTrip = await _apiClient.GetTripDetailsAsync(tripServerId, cancellationToken);
            if (serverTrip == null)
            {
                _logger.LogWarning("Failed to fetch trip details for sync: {TripId}", tripServerId);
                return null;
            }

            // Check if update is needed (unless force sync)
            if (!forceSync && serverTrip.Version <= localTrip.Version)
            {
                _logger.LogInformation("Trip {TripName} is already up to date (v{Version})", localTrip.Name, localTrip.Version);
                return localTrip;
            }

            RaiseProgress(localTrip.Id, 15, "Updating regions...");

            // Update areas/regions
            var areas = serverTrip.Regions.Select((r, index) => new OfflineAreaEntity
            {
                ServerId = r.Id,
                Name = r.Name,
                SortOrder = r.SortOrder > 0 ? r.SortOrder : index,
                PlaceCount = r.Places.Count
            }).ToList();

            await _databaseService.SaveOfflineAreasAsync(localTrip.Id, areas);
            localTrip.AreaCount = areas.Count;

            RaiseProgress(localTrip.Id, 35, "Updating places...");

            // Update places with region info
            var places = new List<OfflinePlaceEntity>();
            int placeIndex = 0;
            foreach (var region in serverTrip.Regions)
            {
                foreach (var place in region.Places)
                {
                    places.Add(new OfflinePlaceEntity
                    {
                        ServerId = place.Id,
                        RegionId = region.Id,
                        RegionName = region.Name,
                        Name = place.Name,
                        Latitude = place.Latitude,
                        Longitude = place.Longitude,
                        Notes = place.Notes,
                        IconName = place.Icon,
                        MarkerColor = place.MarkerColor,
                        SortOrder = place.SortOrder > 0 ? place.SortOrder : placeIndex++
                    });
                }
            }

            await _databaseService.SaveOfflinePlacesAsync(localTrip.Id, places);
            localTrip.PlaceCount = places.Count;

            RaiseProgress(localTrip.Id, 55, "Updating segments...");

            // Update segments
            var segments = serverTrip.Segments.Select((s, index) => new OfflineSegmentEntity
            {
                ServerId = s.Id,
                OriginId = s.OriginId,
                DestinationId = s.DestinationId,
                TransportMode = s.TransportMode,
                DistanceKm = s.DistanceKm,
                DurationMinutes = s.DurationMinutes,
                Geometry = s.Geometry,
                SortOrder = index
            }).ToList();

            await _databaseService.SaveOfflineSegmentsAsync(localTrip.Id, segments);
            localTrip.SegmentCount = segments.Count;

            RaiseProgress(localTrip.Id, 75, "Checking map coverage...");

            // Check if bounding box changed significantly (needs tile re-download)
            var boundingBoxChanged = serverTrip.BoundingBox != null && HasBoundingBoxChangedSignificantly(
                localTrip.BoundingBoxNorth, localTrip.BoundingBoxSouth, localTrip.BoundingBoxEast, localTrip.BoundingBoxWest,
                serverTrip.BoundingBox);

            if (boundingBoxChanged)
            {
                _logger.LogInformation("Bounding box changed for trip {TripName}, re-downloading tiles", localTrip.Name);
                RaiseProgress(localTrip.Id, 80, "Downloading new map tiles...");

                // Update bounding box
                localTrip.BoundingBoxNorth = serverTrip.BoundingBox!.North;
                localTrip.BoundingBoxSouth = serverTrip.BoundingBox.South;
                localTrip.BoundingBoxEast = serverTrip.BoundingBox.East;
                localTrip.BoundingBoxWest = serverTrip.BoundingBox.West;

                // Re-download tiles for new bounding box
                var tileCoords = CalculateTilesForBoundingBox(serverTrip.BoundingBox);
                if (tileCoords.Count > 0)
                {
                    var downloadedBytes = await DownloadTilesAsync(localTrip.Id, tileCoords, cancellationToken);
                    localTrip.TileCount = tileCoords.Count;
                    localTrip.TotalSizeBytes = downloadedBytes;
                }
            }

            // Update version and timestamps
            localTrip.Version = serverTrip.Version;
            localTrip.ServerUpdatedAt = DateTime.UtcNow;
            localTrip.UpdatedAt = DateTime.UtcNow;
            localTrip.Name = serverTrip.Name; // In case name changed

            await _databaseService.SaveDownloadedTripAsync(localTrip);

            RaiseProgress(localTrip.Id, 100, "Sync complete");
            _logger.LogInformation("Trip synced: {TripName} (v{Version}, {PlaceCount} places, {SegmentCount} segments)",
                localTrip.Name, localTrip.Version, places.Count, segments.Count);

            return localTrip;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Trip sync cancelled: {TripId}", tripServerId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync trip: {TripId}", tripServerId);
            return null;
        }
    }

    /// <summary>
    /// Gets all downloaded trips that need syncing.
    /// </summary>
    /// <returns>List of trip entities that have updates available.</returns>
    public async Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync()
    {
        var tripsNeedingUpdate = new List<DownloadedTripEntity>();

        if (!IsNetworkAvailable())
            return tripsNeedingUpdate;

        var downloadedTrips = await _databaseService.GetDownloadedTripsAsync();

        foreach (var trip in downloadedTrips.Where(t =>
            t.Status == TripDownloadStatus.Complete || t.Status == TripDownloadStatus.MetadataOnly))
        {
            try
            {
                if (await CheckTripUpdateNeededAsync(trip.ServerId))
                {
                    tripsNeedingUpdate.Add(trip);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check update for trip {TripName}", trip.Name);
            }
        }

        return tripsNeedingUpdate;
    }

    /// <summary>
    /// Syncs all downloaded trips that have updates available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of trips successfully synced.</returns>
    public async Task<int> SyncAllTripsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsNetworkAvailable())
        {
            _logger.LogWarning("Cannot sync trips - no network connection");
            return 0;
        }

        var downloadedTrips = await _databaseService.GetDownloadedTripsAsync();
        var completedTrips = downloadedTrips.Where(t =>
            t.Status == TripDownloadStatus.Complete || t.Status == TripDownloadStatus.MetadataOnly).ToList();

        if (completedTrips.Count == 0)
        {
            _logger.LogInformation("No trips to sync");
            return 0;
        }

        _logger.LogInformation("Starting sync for {Count} downloaded trips", completedTrips.Count);

        var syncedCount = 0;
        foreach (var trip in completedTrips)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await SyncTripAsync(trip.ServerId, forceSync: false, cancellationToken);
                if (result != null)
                {
                    syncedCount++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync trip {TripName}", trip.Name);
            }
        }

        _logger.LogInformation("Sync complete: {SyncedCount}/{TotalCount} trips updated", syncedCount, completedTrips.Count);
        return syncedCount;
    }

    /// <summary>
    /// Checks if bounding box has changed significantly (more than ~1km at equator).
    /// </summary>
    private static bool HasBoundingBoxChangedSignificantly(
        double oldNorth, double oldSouth, double oldEast, double oldWest,
        BoundingBox newBox)
    {
        const double threshold = 0.01; // ~1km at equator

        return Math.Abs(oldNorth - newBox.North) > threshold ||
               Math.Abs(oldSouth - newBox.South) > threshold ||
               Math.Abs(oldEast - newBox.East) > threshold ||
               Math.Abs(oldWest - newBox.West) > threshold;
    }

    #endregion

    /// <summary>
    /// Gets the total size of all downloaded trips.
    /// </summary>
    public async Task<long> GetTotalCacheSizeAsync()
    {
        return await _databaseService.GetTotalTripCacheSizeAsync();
    }

    /// <summary>
    /// Raises the progress changed event.
    /// </summary>
    private void RaiseProgress(int tripId, int percent, string message)
    {
        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs(tripId, percent, message));
    }

    #region Tile Download

    /// <summary>
    /// Calculates all tile coordinates needed for a bounding box.
    /// Uses intelligent zoom level selection based on area size.
    /// </summary>
    /// <param name="bbox">The bounding box.</param>
    /// <returns>List of tile coordinates.</returns>
    private List<TileCoordinate> CalculateTilesForBoundingBox(BoundingBox bbox)
    {
        var tiles = new List<TileCoordinate>();

        // Calculate area to determine appropriate max zoom
        var areaSquareDegrees = (bbox.North - bbox.South) * (bbox.East - bbox.West);
        var recommendedMaxZoom = GetRecommendedMaxZoom(areaSquareDegrees);

        const int minZoom = 10;
        var effectiveMaxZoom = Math.Min(recommendedMaxZoom, DownloadZoomLevels.Max());

        _logger.LogInformation("Area: {Area:F2} sq degrees, using zoom levels {Min}-{Max}",
            areaSquareDegrees, minZoom, effectiveMaxZoom);

        for (int zoom = minZoom; zoom <= effectiveMaxZoom; zoom++)
        {
            var (minX, maxY) = LatLonToTile(bbox.North, bbox.West, zoom);
            var (maxX, minY) = LatLonToTile(bbox.South, bbox.East, zoom);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    tiles.Add(new TileCoordinate { Zoom = zoom, X = x, Y = y });
                }
            }
        }

        return tiles;
    }

    /// <summary>
    /// Recommends maximum zoom level based on trip area size.
    /// Prevents excessive downloads for very large areas.
    /// </summary>
    private static int GetRecommendedMaxZoom(double areaSquareDegrees)
    {
        return areaSquareDegrees switch
        {
            > 100 => 12,   // Very large area (multiple countries) - low detail only
            > 25 => 13,    // Large area (country/large region) - medium detail
            > 5 => 14,     // Medium area (state/province) - good detail
            > 1 => 15,     // Small area (city) - high detail
            > 0.1 => 16,   // Very small area (neighborhood) - very high detail
            _ => 17        // Tiny area - maximum detail
        };
    }

    /// <summary>
    /// Converts latitude/longitude to tile coordinates.
    /// </summary>
    private (int X, int Y) LatLonToTile(double lat, double lon, int zoom)
    {
        var n = Math.Pow(2, zoom);
        var x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        var latRad = lat * Math.PI / 180.0;
        var y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);

        // Clamp to valid range
        x = Math.Max(0, Math.Min((int)n - 1, x));
        y = Math.Max(0, Math.Min((int)n - 1, y));

        return (x, y);
    }

    /// <summary>
    /// Downloads tiles for a trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="tiles">The tile coordinates to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total bytes downloaded.</returns>
    private async Task<long> DownloadTilesAsync(
        int tripId,
        List<TileCoordinate> tiles,
        CancellationToken cancellationToken)
    {
        var totalBytes = 0L;
        var completed = 0;
        var total = tiles.Count;
        var tileCacheDir = GetTileCacheDirectory(tripId);

        // Ensure cache directory exists
        Directory.CreateDirectory(tileCacheDir);

        // Use semaphore to limit concurrent downloads
        using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(TileTimeoutMs) };

        // Add user agent to comply with OSM tile usage policy
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");

        var tasks = tiles.Select(async tile =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var bytes = await DownloadTileAsync(httpClient, tripId, tile, tileCacheDir, cancellationToken);
                Interlocked.Add(ref totalBytes, bytes);
                var count = Interlocked.Increment(ref completed);

                // Update progress (55-95% range for tiles)
                var percent = 55 + (int)(count * 40.0 / total);
                RaiseProgress(tripId, percent, $"Downloading tiles: {count}/{total}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return totalBytes;
    }

    /// <summary>
    /// Downloads a single tile with atomic file writes, rate limiting, and network monitoring.
    /// </summary>
    private async Task<long> DownloadTileAsync(
        HttpClient httpClient,
        int tripId,
        TileCoordinate tile,
        string cacheDir,
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(cacheDir, $"{tile.Zoom}", $"{tile.X}", $"{tile.Y}.png");
        var tempPath = filePath + ".tmp";

        try
        {
            var url = tile.GetTileUrl(_settingsService.TileServerUrl);

            // Skip if already exists and has content
            if (File.Exists(filePath))
            {
                var existingSize = new FileInfo(filePath).Length;
                if (existingSize > 0)
                    return existingSize;
            }

            // Check network before download
            if (!IsNetworkAvailable())
            {
                _logger.LogDebug("Waiting for network before downloading tile {TileId}...", tile.Id);
                if (!await WaitForNetworkAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    _logger.LogWarning("Network not available for tile {TileId}", tile.Id);
                    return 0;
                }
            }

            // Enforce rate limiting
            await EnforceRateLimitAsync(cancellationToken);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);

            // Download tile with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var response = await httpClient.GetAsync(url, combinedCts.Token);

            // Handle rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Get retry-after header if available, otherwise use default backoff
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                _logger.LogWarning("Rate limited (429) for tile {TileId}, waiting {RetryAfter}s", tile.Id, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter, cancellationToken);
                return 0;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download tile {TileId}: {StatusCode}", tile.Id, response.StatusCode);
                return 0;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(combinedCts.Token);
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Empty tile data for {TileId}", tile.Id);
                return 0;
            }

            // Atomic write: temp file then move with overwrite (fixes race condition)
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);

            // Save to database
            var tileEntity = new TripTileEntity
            {
                Id = $"{tripId}/{tile.Zoom}/{tile.X}/{tile.Y}",
                TripId = tripId,
                Zoom = tile.Zoom,
                X = tile.X,
                Y = tile.Y,
                FilePath = filePath,
                FileSizeBytes = bytes.Length,
                DownloadedAt = DateTime.UtcNow
            };
            await _databaseService.SaveTripTileAsync(tileEntity);

            return bytes.Length;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Clean up temp file on cancellation
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        catch (HttpRequestException ex)
        {
            // Network error - wait and continue
            _logger.LogWarning(ex, "Network error downloading tile {TileId}", tile.Id);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            // Clean up temp file on error
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            _logger.LogWarning(ex, "Error downloading tile {TileId}", tile.Id);
            return 0;
        }
    }

    /// <summary>
    /// Gets the tile cache directory for a trip.
    /// </summary>
    private string GetTileCacheDirectory(int tripId)
    {
        var cacheDir = Path.Combine(FileSystem.CacheDirectory, "tiles", $"trip_{tripId}");
        return cacheDir;
    }

    /// <summary>
    /// Gets a cached tile file path.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>File path if exists, null otherwise.</returns>
    public string? GetCachedTilePath(int tripId, int zoom, int x, int y)
    {
        var filePath = Path.Combine(GetTileCacheDirectory(tripId), $"{zoom}", $"{x}", $"{y}.png");
        return File.Exists(filePath) ? filePath : null;
    }

    #endregion

    #region Network and Storage Monitoring

    /// <summary>
    /// Checks if network is available.
    /// </summary>
    private static bool IsNetworkAvailable()
    {
        return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    }

    /// <summary>
    /// Waits for network to become available with timeout.
    /// </summary>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if network became available, false if timed out.</returns>
    private async Task<bool> WaitForNetworkAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (IsNetworkAvailable())
            return true;

        var tcs = new TaskCompletionSource<bool>();
        var startTime = DateTime.UtcNow;

        void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                tcs.TrySetResult(true);
            }
        }

        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            combined.Token.Register(() => tcs.TrySetResult(false));

            return await tcs.Task;
        }
        finally
        {
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        }
    }

    /// <summary>
    /// Checks if sufficient storage space is available.
    /// </summary>
    private async Task<bool> HasSufficientStorageAsync()
    {
        try
        {
            var cacheDir = FileSystem.CacheDirectory;
            var driveInfo = new DriveInfo(Path.GetPathRoot(cacheDir)!);
            var freeSpaceMB = driveInfo.AvailableFreeSpace / (1024 * 1024);

            _logger.LogDebug("Available storage: {FreeSpace} MB", freeSpaceMB);
            return freeSpaceMB >= MinRequiredSpaceMB;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check storage space, assuming sufficient");
            return true;
        }
    }

    /// <summary>
    /// Enforces minimum delay between requests to respect tile server.
    /// </summary>
    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        TimeSpan waitTime;
        lock (_rateLimitLock)
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            var minimumDelay = TimeSpan.FromMilliseconds(MinRequestDelayMs);

            if (timeSinceLastRequest < minimumDelay)
            {
                waitTime = minimumDelay - timeSinceLastRequest;
            }
            else
            {
                waitTime = TimeSpan.Zero;
            }

            _lastRequestTime = DateTime.UtcNow + waitTime;
        }

        if (waitTime > TimeSpan.Zero)
        {
            await Task.Delay(waitTime, cancellationToken);
        }
    }

    #endregion
}

/// <summary>
/// Event args for download progress updates.
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the trip ID being downloaded.
    /// </summary>
    public int TripId { get; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; }

    /// <summary>
    /// Gets the status message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a new instance of DownloadProgressEventArgs.
    /// </summary>
    public DownloadProgressEventArgs(int tripId, int progressPercent, string message)
    {
        TripId = tripId;
        ProgressPercent = progressPercent;
        Message = message;
    }
}
