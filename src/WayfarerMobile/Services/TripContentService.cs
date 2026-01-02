using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Interfaces;

// Using DownloadProgressEventArgs from Core.Interfaces

namespace WayfarerMobile.Services;

/// <summary>
/// Service for trip content operations: metadata fetch, sync, and offline retrieval.
/// Handles all trip data operations without tile management.
/// </summary>
public class TripContentService : ITripContentService
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly ITripMetadataBuilder _metadataBuilder;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<TripContentService> _logger;

    /// <summary>
    /// Creates a new instance of TripContentService.
    /// </summary>
    public TripContentService(
        IApiClient apiClient,
        DatabaseService databaseService,
        ITripMetadataBuilder metadataBuilder,
        IConnectivity connectivity,
        ILogger<TripContentService> logger)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _metadataBuilder = metadataBuilder;
        _connectivity = connectivity;
        _logger = logger;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<(DownloadedTripEntity? Trip, bool BoundingBoxChanged)> SyncTripMetadataAsync(
        Guid tripServerId,
        bool forceSync = false,
        IProgress<DownloadProgressEventArgs>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var localTrip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
            if (localTrip == null)
            {
                _logger.LogWarning("Cannot sync trip {TripId} - not downloaded", tripServerId);
                return (null, false);
            }

            if (!IsNetworkAvailable())
            {
                _logger.LogWarning("Cannot sync trip - no network connection");
                return (null, false);
            }

            _logger.LogInformation("Starting metadata sync for trip: {TripName}", localTrip.Name);
            RaiseProgress(progress, localTrip.Id, 5, "Checking for updates...");

            // Fetch full trip details from server
            var serverTrip = await _apiClient.GetTripDetailsAsync(tripServerId, cancellationToken);
            if (serverTrip == null)
            {
                _logger.LogWarning("Failed to fetch trip details for sync: {TripId}", tripServerId);
                return (null, false);
            }

            // Check if update is needed (unless force sync)
            if (!forceSync && serverTrip.Version <= localTrip.Version)
            {
                _logger.LogInformation("Trip {TripName} is already up to date (v{Version})", localTrip.Name, localTrip.Version);
                return (localTrip, false);
            }

            RaiseProgress(progress, localTrip.Id, 15, "Updating regions...");

            // Update areas/regions using metadata builder
            var areas = _metadataBuilder.BuildAreas(serverTrip);
            await _databaseService.SaveOfflineAreasAsync(localTrip.Id, areas);
            localTrip.RegionCount = areas.Count;

            RaiseProgress(progress, localTrip.Id, 35, "Updating places...");

            // Update places with region info
            var places = _metadataBuilder.BuildPlaces(serverTrip);
            await _databaseService.SaveOfflinePlacesAsync(localTrip.Id, places);
            localTrip.PlaceCount = places.Count;

            RaiseProgress(progress, localTrip.Id, 55, "Updating segments...");

            // Update segments with place names
            var segments = _metadataBuilder.BuildSegments(serverTrip);
            await _databaseService.SaveOfflineSegmentsAsync(localTrip.Id, segments);
            localTrip.SegmentCount = segments.Count;

            RaiseProgress(progress, localTrip.Id, 65, "Updating polygon zones...");

            // Update polygon zones (TripArea) from each region
            var polygons = _metadataBuilder.BuildPolygons(serverTrip);
            await _databaseService.SaveOfflinePolygonsAsync(localTrip.Id, polygons);
            localTrip.AreaCount = polygons.Count;

            RaiseProgress(progress, localTrip.Id, 75, "Checking map coverage...");

            // Check if bounding box changed significantly (caller will handle tile re-download)
            var boundingBoxChanged = serverTrip.BoundingBox != null &&
                HasBoundingBoxChangedSignificantly(localTrip, serverTrip.BoundingBox);

            if (boundingBoxChanged && serverTrip.BoundingBox != null)
            {
                // Update bounding box metadata from server
                localTrip.BoundingBoxNorth = serverTrip.BoundingBox.North;
                localTrip.BoundingBoxSouth = serverTrip.BoundingBox.South;
                localTrip.BoundingBoxEast = serverTrip.BoundingBox.East;
                localTrip.BoundingBoxWest = serverTrip.BoundingBox.West;

                _logger.LogInformation("Bounding box changed for trip {TripName}", localTrip.Name);
            }

            // Update version and timestamps
            localTrip.Version = serverTrip.Version;
            localTrip.ServerUpdatedAt = DateTime.UtcNow;
            localTrip.UpdatedAt = DateTime.UtcNow;
            localTrip.Name = serverTrip.Name; // In case name changed

            await _databaseService.SaveDownloadedTripAsync(localTrip);

            RaiseProgress(progress, localTrip.Id, 100, "Metadata sync complete");
            _logger.LogInformation("Trip metadata synced: {TripName} (v{Version}, {PlaceCount} places, {SegmentCount} segments)",
                localTrip.Name, localTrip.Version, places.Count, segments.Count);

            return (localTrip, boundingBoxChanged);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Trip metadata sync cancelled: {TripId}", tripServerId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync trip metadata: {TripId}", tripServerId);
            return (null, false);
        }
    }

    /// <inheritdoc/>
    public async Task<int> SyncAllTripsMetadataAsync(CancellationToken cancellationToken = default)
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

        _logger.LogInformation("Starting metadata sync for {Count} downloaded trips", completedTrips.Count);

        var syncedCount = 0;
        foreach (var trip in completedTrips)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (result, _) = await SyncTripMetadataAsync(trip.ServerId, forceSync: false, cancellationToken: cancellationToken);
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
                _logger.LogWarning(ex, "Failed to sync trip metadata {TripName}", trip.Name);
            }
        }

        _logger.LogInformation("Metadata sync complete: {SyncedCount}/{TotalCount} trips updated", syncedCount, completedTrips.Count);
        return syncedCount;
    }

    /// <inheritdoc/>
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
        var polygonsTask = _databaseService.GetOfflinePolygonsAsync(trip.Id);

        await Task.WhenAll(placesTask, segmentsTask, areasTask, polygonsTask);

        var placeEntities = await placesTask;
        var segmentEntities = await segmentsTask;
        var areaEntities = await areasTask;
        var polygonEntities = await polygonsTask;

        // Group polygons by region
        var polygonsByRegion = polygonEntities.GroupBy(p => p.RegionId).ToDictionary(g => g.Key, g => g.ToList());

        // Build regions with places and areas (polygon zones)
        var regions = new List<TripRegion>();
        var placesByRegion = placeEntities.GroupBy(p => p.RegionId ?? Guid.Empty);

        foreach (var regionGroup in placesByRegion)
        {
            var area = areaEntities.FirstOrDefault(a => a.ServerId == regionGroup.Key);

            // Build TripArea list for this region
            var tripAreas = new List<TripArea>();
            if (polygonsByRegion.TryGetValue(regionGroup.Key, out var regionPolygons))
            {
                tripAreas = regionPolygons.Select(p => new TripArea
                {
                    Id = p.ServerId,
                    Name = p.Name,
                    Notes = p.Notes,
                    FillColor = p.FillColor,
                    StrokeColor = p.StrokeColor,
                    GeometryGeoJson = p.GeometryGeoJson,
                    SortOrder = p.SortOrder
                }).OrderBy(a => a.SortOrder).ToList();
            }

            var region = new TripRegion
            {
                Id = regionGroup.Key,
                Name = area?.Name ?? regionGroup.First().RegionName ?? "Places",
                Notes = area?.Notes,
                CoverImageUrl = area?.CoverImageUrl,
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
                    Address = p.Address,
                    SortOrder = p.SortOrder
                }).OrderBy(p => p.SortOrder).ToList(),
                Areas = tripAreas
            };
            regions.Add(region);
        }

        // Build segments
        var segments = segmentEntities.Select(s => new TripSegment
        {
            Id = s.ServerId,
            OriginId = s.OriginId,
            OriginName = s.OriginName,
            DestinationId = s.DestinationId,
            DestinationName = s.DestinationName,
            TransportMode = s.TransportMode,
            DistanceKm = s.DistanceKm,
            DurationMinutes = s.DurationMinutes,
            Notes = s.Notes,
            Geometry = s.Geometry
        }).ToList();

        // Build trip details
        var tripDetails = new TripDetails
        {
            Id = trip.ServerId,
            Name = trip.Name,
            Notes = trip.Notes,
            CoverImageUrl = trip.CoverImageUrl,
            UpdatedAt = trip.ServerUpdatedAt ?? trip.UpdatedAt,
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

        // Debug: Log loaded data to verify SQLite storage
        _logger.LogInformation("Loaded offline trip: {TripName} ({PlaceCount} places, {SegmentCount} segments)",
            trip.Name, placeEntities.Count, segmentEntities.Count);
        foreach (var place in tripDetails.AllPlaces.Take(3))
        {
            _logger.LogDebug("  Loaded place '{Name}': Lat={Lat}, Lon={Lon}",
                place.Name, place.Latitude, place.Longitude);
        }

        return tripDetails;
    }

    /// <inheritdoc/>
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
            Address = e.Address,
            SortOrder = e.SortOrder
        }).ToList();
    }

    /// <inheritdoc/>
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
            OriginName = s.OriginName,
            DestinationId = s.DestinationId,
            DestinationName = s.DestinationName,
            TransportMode = s.TransportMode,
            DistanceKm = s.DistanceKm,
            DurationMinutes = s.DurationMinutes,
            Notes = s.Notes,
            Geometry = s.Geometry
        }).ToList();
    }

    /// <inheritdoc/>
    public bool HasBoundingBoxChangedSignificantly(DownloadedTripEntity trip, BoundingBox serverBoundingBox)
    {
        const double threshold = 0.01; // ~1km at equator

        return Math.Abs(trip.BoundingBoxNorth - serverBoundingBox.North) > threshold ||
               Math.Abs(trip.BoundingBoxSouth - serverBoundingBox.South) > threshold ||
               Math.Abs(trip.BoundingBoxEast - serverBoundingBox.East) > threshold ||
               Math.Abs(trip.BoundingBoxWest - serverBoundingBox.West) > threshold;
    }

    /// <summary>
    /// Checks if network is available.
    /// </summary>
    private bool IsNetworkAvailable()
    {
        return _connectivity.NetworkAccess == NetworkAccess.Internet;
    }

    /// <summary>
    /// Raises progress event if progress reporter is provided.
    /// </summary>
    private static void RaiseProgress(IProgress<DownloadProgressEventArgs>? progress, int tripId, int percentage, string status)
    {
        progress?.Report(new DownloadProgressEventArgs
        {
            TripId = tripId,
            ProgressPercent = percentage,
            StatusMessage = status
        });
    }
}
