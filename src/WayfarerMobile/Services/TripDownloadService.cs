using System.Collections.Concurrent;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for downloading and managing offline trips.
/// Implements IDisposable to properly clean up HttpClient resources.
/// </summary>
public class TripDownloadService : ITripDownloadService
{
    private readonly IApiClient _apiClient;
    private readonly DatabaseService _databaseService;
    private readonly ISettingsService _settingsService;
    private readonly ITripSyncService _tripSyncService;
    private readonly ITileDownloadService _tileDownloadService;
    private readonly IDownloadStateManager _downloadStateManager;
    private readonly ICacheLimitEnforcer _cacheLimitEnforcer;
    private readonly ILogger<TripDownloadService> _logger;

    #region Constants

    // Tile download configuration - use centralized constants for consistency
    private static int[] DownloadZoomLevels => TileCacheConstants.AllZoomLevels;
    private const int TileTimeoutMs = TileCacheConstants.TileTimeoutMs;
    private const long EstimatedTileSizeBytes = TileCacheConstants.EstimatedTileSizeBytes;
    private const long MinRequiredSpaceMB = 50; // Minimum free space required

    // Download state save intervals
    private const int StateSaveIntervalTiles = 25; // Save state every N tiles
    private const int CacheLimitCheckIntervalTiles = 100; // Check limit every N tiles
    private const int StorageCheckIntervalTiles = 200; // Check storage every N tiles
    private const int TempFileMaxAgeHours = 1; // Max age for orphaned temp files

    // Tile download retry configuration
    private const int MaxTileRetries = 2;
    private const int RetryDelayMs = 1000;

    // PNG file signature (first 8 bytes)
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // Absolute maximum tile count to prevent memory exhaustion (regardless of cache size)
    private const int AbsoluteMaxTileCount = 150000;

    #endregion

    #region Fields

    // Active download guard - prevents concurrent downloads of the same trip
    // Keyed by server trip ID (Guid) - available at start of download before local ID exists
    private readonly ConcurrentDictionary<Guid, bool> _activeDownloads = new();

    // Per-trip warning flags - tracks if warning/critical events have been raised
    // Keyed by local trip ID (int) - available after trip entity is saved
    private readonly ConcurrentDictionary<int, TripWarningState> _tripWarningStates = new();

    // Shared HttpClient for tile downloads (avoids socket exhaustion)
    private readonly HttpClient _tileHttpClient;

    // Disposal tracking
    private bool _disposed;

    #endregion

    // Configurable settings (read from ISettingsService)
    private int MaxConcurrentDownloads => _settingsService.MaxConcurrentTileDownloads;
    private int MinRequestDelayMs => _settingsService.MinTileRequestDelayMs;

    /// <summary>
    /// Maximum tile count derived from MaxTripCacheSizeMB setting.
    /// Capped at AbsoluteMaxTileCount to prevent memory exhaustion.
    /// </summary>
    private int MaxTileCount
    {
        get
        {
            // Calculate max tiles based on cache size setting
            var maxCacheBytes = (long)_settingsService.MaxTripCacheSizeMB * 1024 * 1024;
            var calculatedMax = (int)(maxCacheBytes / EstimatedTileSizeBytes);

            // Cap at absolute maximum to prevent memory issues
            return Math.Min(calculatedMax, AbsoluteMaxTileCount);
        }
    }

    /// <summary>
    /// Event raised when download progress changes.
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads. Subscribers
    /// must marshal UI updates to the main thread using <c>MainThread.BeginInvokeOnMainThread</c>.</para>
    /// <para>Memory: Subscribers should unsubscribe when no longer needed to prevent memory leaks.</para>
    /// </remarks>
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Event raised when cache usage reaches warning level (80%).
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads during tile downloads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// <para>Deduplication: This event is raised only once per trip download, not per-check.</para>
    /// </remarks>
    public event EventHandler<CacheLimitEventArgs>? CacheWarning;

    /// <summary>
    /// Event raised when cache usage reaches critical level (90%).
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads during tile downloads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// <para>Deduplication: This event is raised only once per trip download, not per-check.</para>
    /// </remarks>
    public event EventHandler<CacheLimitEventArgs>? CacheCritical;

    /// <summary>
    /// Event raised when cache limit is reached (100%).
    /// The download will be paused automatically with state saved for resumption.
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads during tile downloads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// <para>Download State: When this event fires, the download state is automatically saved
    /// and the download status remains "Downloading" (paused). Users can resume by increasing
    /// cache limit or freeing space, then calling ResumeDownloadAsync.</para>
    /// </remarks>
    public event EventHandler<CacheLimitEventArgs>? CacheLimitReached;

    /// <summary>
    /// Event raised when a download completes successfully.
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// </remarks>
    public event EventHandler<DownloadTerminalEventArgs>? DownloadCompleted;

    /// <summary>
    /// Event raised when a download fails.
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// </remarks>
    public event EventHandler<DownloadTerminalEventArgs>? DownloadFailed;

    /// <summary>
    /// Event raised when a download is paused (user request, network loss, storage low, or cache limit).
    /// </summary>
    /// <remarks>
    /// <para>Thread Safety: This event may be raised from background threads.
    /// Subscribers must marshal UI updates to the main thread.</para>
    /// </remarks>
    public event EventHandler<DownloadPausedEventArgs>? DownloadPaused;

    /// <summary>
    /// Creates a new instance of TripDownloadService.
    /// </summary>
    public TripDownloadService(
        IApiClient apiClient,
        DatabaseService databaseService,
        ISettingsService settingsService,
        ITripSyncService tripSyncService,
        ITileDownloadService tileDownloadService,
        IDownloadStateManager downloadStateManager,
        ICacheLimitEnforcer cacheLimitEnforcer,
        ILogger<TripDownloadService> logger)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _settingsService = settingsService;
        _tripSyncService = tripSyncService;
        _tileDownloadService = tileDownloadService;
        _downloadStateManager = downloadStateManager;
        _cacheLimitEnforcer = cacheLimitEnforcer;
        _logger = logger;

        // Initialize shared HttpClient with appropriate timeout
        _tileHttpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(TileTimeoutMs) };
        _tileHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WayfarerMobile/1.0");

        // Wire up cache limit events from the enforcer to our events
        _cacheLimitEnforcer.CacheWarning += (s, e) => CacheWarning?.Invoke(this, e);
        _cacheLimitEnforcer.CacheCritical += (s, e) => CacheCritical?.Invoke(this, e);
        _cacheLimitEnforcer.CacheLimitReached += (s, e) => CacheLimitReached?.Invoke(this, e);
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
        ThrowIfDisposed();

        // Validate trip ID
        if (tripSummary.Id == Guid.Empty)
        {
            _logger.LogWarning("Cannot download trip with empty ID");
            return null;
        }

        // Guard against concurrent downloads of the same trip
        if (!_activeDownloads.TryAdd(tripSummary.Id, true))
        {
            _logger.LogWarning("Download already in progress for trip {TripName} ({TripId})",
                tripSummary.Name, tripSummary.Id);
            return null;
        }

        // Track local trip ID for cleanup in finally block (set after trip is saved)
        int? localTripId = null;

        try
        {
            _logger.LogInformation("Starting download for trip: {TripName}", tripSummary.Name);

            // Check network connectivity
            if (!_tileDownloadService.IsNetworkAvailable())
            {
                _logger.LogWarning("Cannot download trip - no network connection");
                return null;
            }

            // Check storage space
            if (!_tileDownloadService.HasSufficientStorage())
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
            localTripId = tripEntity.Id; // Capture for cleanup
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

            // Debug: Log parsed data to verify deserialization
            _logger.LogInformation("Trip '{TripName}' fetched: {RegionCount} regions, {PlaceCount} places",
                tripDetails.Name, tripDetails.Regions.Count, tripDetails.AllPlaces.Count);
            foreach (var place in tripDetails.AllPlaces.Take(3))
            {
                _logger.LogDebug("  Place '{Name}': Lat={Lat}, Lon={Lon}",
                    place.Name, place.Latitude, place.Longitude);
            }

            RaiseProgress(tripEntity.Id, 25, "Saving regions...");

            // Convert and save areas/regions
            var areas = tripDetails.Regions.Select((r, index) => new OfflineAreaEntity
            {
                ServerId = r.Id,
                Name = r.Name,
                Notes = r.Notes,
                CoverImageUrl = r.CoverImageUrl,
                SortOrder = r.SortOrder > 0 ? r.SortOrder : index,
                PlaceCount = r.Places.Count
            }).ToList();

            await _databaseService.SaveOfflineAreasAsync(tripEntity.Id, areas);
            tripEntity.RegionCount = areas.Count;

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
                        Address = place.Address,
                        SortOrder = place.SortOrder is > 0 ? place.SortOrder.Value : placeIndex++
                    });
                }
            }

            await _databaseService.SaveOfflinePlacesAsync(tripEntity.Id, places);
            tripEntity.PlaceCount = places.Count;

            RaiseProgress(tripEntity.Id, 40, "Saving segments...");

            // Build place name lookup for segment origin/destination (includes region name: "Place, Region")
            var placeNameLookup = new Dictionary<Guid, string>();
            foreach (var region in tripDetails.Regions)
            {
                foreach (var place in region.Places)
                {
                    // Format: "PlaceName, RegionName" (or just "PlaceName" if region has same name)
                    var displayName = string.Equals(place.Name, region.Name, StringComparison.OrdinalIgnoreCase)
                        ? place.Name
                        : $"{place.Name}, {region.Name}";
                    placeNameLookup[place.Id] = displayName;
                }
            }

            // Convert and save segments with place names
            var segments = tripDetails.Segments.Select((s, index) => new OfflineSegmentEntity
            {
                ServerId = s.Id,
                OriginId = s.OriginId ?? Guid.Empty,
                OriginName = s.OriginId.HasValue && placeNameLookup.TryGetValue(s.OriginId.Value, out var fromName) ? fromName : null,
                DestinationId = s.DestinationId ?? Guid.Empty,
                DestinationName = s.DestinationId.HasValue && placeNameLookup.TryGetValue(s.DestinationId.Value, out var toName) ? toName : null,
                TransportMode = s.TransportMode,
                DistanceKm = s.DistanceKm,
                DurationMinutes = (int?)s.DurationMinutes,
                Notes = s.Notes,
                Geometry = s.Geometry,
                SortOrder = index
            }).ToList();

            await _databaseService.SaveOfflineSegmentsAsync(tripEntity.Id, segments);
            tripEntity.SegmentCount = segments.Count;

            // Save polygon zones (TripArea) from each region
            var polygons = new List<OfflinePolygonEntity>();
            foreach (var region in tripDetails.Regions)
            {
                foreach (var area in region.Areas)
                {
                    polygons.Add(new OfflinePolygonEntity
                    {
                        ServerId = area.Id,
                        RegionId = region.Id,
                        Name = area.Name,
                        Notes = area.Notes,
                        FillColor = area.FillColor,
                        StrokeColor = area.StrokeColor,
                        GeometryGeoJson = area.GeometryGeoJson,
                        SortOrder = area.SortOrder ?? 0
                    });
                }
            }
            await _databaseService.SaveOfflinePolygonsAsync(tripEntity.Id, polygons);
            tripEntity.AreaCount = polygons.Count;

            tripEntity.ProgressPercent = 50;
            await _databaseService.SaveDownloadedTripAsync(tripEntity);

            RaiseProgress(tripEntity.Id, 50, $"Saved {places.Count} places, {segments.Count} segments, {polygons.Count} polygons");

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
                    // Initialize per-trip warning state
                    _tripWarningStates[tripEntity.Id] = new TripWarningState();

                    // Use unified download method that supports pause/resume and cache limits
                    var downloadResult = await DownloadTilesWithStateAsync(
                        tripEntity,
                        tileCoords,
                        initialCompleted: 0,
                        totalTiles: tileCoords.Count,
                        initialBytes: 0,
                        cancellationToken);

                    // Track actual tiles downloaded vs requested for accurate reporting
                    tripEntity.TileCount = downloadResult.TilesDownloaded;
                    tripEntity.TotalSizeBytes = downloadResult.TotalBytes;

                    // Check if download was paused or hit cache limit
                    if (downloadResult.WasPaused || downloadResult.WasLimitReached)
                    {
                        _logger.LogInformation("Download stopped for trip {TripName}: Paused={Paused}, LimitReached={LimitReached}",
                            tripSummary.Name, downloadResult.WasPaused, downloadResult.WasLimitReached);
                        return tripEntity; // State already saved by DownloadTilesWithStateAsync
                    }
                }

                tripEntity.Status = TripDownloadStatus.Complete;
            }
            else
            {
                // No bounding box - metadata only
                tripEntity.Status = TripDownloadStatus.MetadataOnly;
                _logger.LogWarning("No bounding box for trip {TripName}, skipping tiles", tripSummary.Name);
            }

            // Store version and trip details for sync tracking
            tripEntity.Version = tripDetails.Version;
            tripEntity.ServerUpdatedAt = tripDetails.UpdatedAt;
            tripEntity.Notes = tripDetails.Notes;
            tripEntity.CoverImageUrl = tripDetails.CoverImageUrl;
            tripEntity.ProgressPercent = 100;
            await _databaseService.SaveDownloadedTripAsync(tripEntity);

            RaiseProgress(tripEntity.Id, 100, "Download complete");
            _logger.LogInformation("Trip downloaded: {TripName} ({PlaceCount} places, {TileCount} tiles, v{Version})",
                tripSummary.Name, places.Count, tripEntity.TileCount, tripEntity.Version);

            // Raise download completed event
            DownloadCompleted?.Invoke(this, new DownloadTerminalEventArgs
            {
                TripId = tripEntity.Id,
                TripServerId = tripSummary.Id,
                TripName = tripSummary.Name,
                TilesDownloaded = tripEntity.TileCount,
                TotalBytes = tripEntity.TotalSizeBytes
            });

            return tripEntity;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Trip download cancelled: {TripName}", tripSummary.Name);

            // Save state for potential resume if we have a trip entity
            var tripEntityForCancel = await _databaseService.GetDownloadedTripByServerIdAsync(tripSummary.Id);
            if (tripEntityForCancel != null)
            {
                // Get remaining tiles from download state if available
                var existingState = await _databaseService.GetDownloadStateAsync(tripEntityForCancel.Id);
                if (existingState != null)
                {
                    // State already saved by DownloadTilesWithStateAsync (periodic checkpoint)
                    existingState.Status = DownloadStateStatus.Paused;
                    existingState.InterruptionReason = DownloadPauseReason.UserCancel;
                    await _databaseService.SaveDownloadStateAsync(existingState);
                }

                tripEntityForCancel.Status = TripDownloadStatus.Failed;
                tripEntityForCancel.LastError = "Download cancelled by user";
                await _databaseService.SaveDownloadedTripAsync(tripEntityForCancel);

                // Raise download paused event
                DownloadPaused?.Invoke(this, new DownloadPausedEventArgs
                {
                    TripId = tripEntityForCancel.Id,
                    TripServerId = tripSummary.Id,
                    TripName = tripSummary.Name,
                    Reason = DownloadPauseReasonType.UserCancel,
                    TilesCompleted = existingState?.CompletedTileCount ?? 0,
                    TotalTiles = existingState?.TotalTileCount ?? 0,
                    CanResume = false
                });
            }

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

                // Raise download failed event
                DownloadFailed?.Invoke(this, new DownloadTerminalEventArgs
                {
                    TripId = tripEntity.Id,
                    TripServerId = tripSummary.Id,
                    TripName = tripSummary.Name,
                    TilesDownloaded = tripEntity.TileCount,
                    TotalBytes = tripEntity.TotalSizeBytes,
                    ErrorMessage = ex.Message
                });
            }

            return null;
        }
        finally
        {
            // Always release the active download guard
            _activeDownloads.TryRemove(tripSummary.Id, out _);

            // Clean up per-trip warning state using captured ID (avoids database round-trip)
            if (localTripId.HasValue)
            {
                _tripWarningStates.TryRemove(localTripId.Value, out _);
            }
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
            Address = e.Address,
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

    /// <summary>
    /// Deletes a downloaded trip and clears any pending mutations.
    /// </summary>
    public async Task DeleteTripAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip != null)
        {
            // Clear pending mutations for this trip first
            await _tripSyncService.ClearPendingMutationsForTripAsync(tripServerId);

            await _databaseService.DeleteDownloadedTripAsync(trip.Id);
            _logger.LogInformation("Deleted trip and cleared pending mutations: {TripId}", tripServerId);
        }
    }

    /// <summary>
    /// Deletes only the cached map tiles for a trip, keeping trip data intact.
    /// Trip status is updated to reflect no offline maps available.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Number of tiles deleted.</returns>
    public async Task<int> DeleteTripTilesAsync(Guid tripServerId)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot delete tiles - trip {TripId} not found", tripServerId);
            return 0;
        }

        // Get file paths and delete from database
        var filePaths = await _databaseService.DeleteTripTilesAsync(trip.Id);

        // Delete actual tile files
        var deletedCount = 0;
        foreach (var filePath in filePaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    deletedCount++;
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "I/O error deleting tile file: {FilePath}", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied deleting tile file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error deleting tile file: {FilePath}", filePath);
            }
        }

        // Update trip to reflect no tiles - set status to metadata only
        trip.TileCount = 0;
        trip.TotalSizeBytes = 0;
        trip.Status = TripDownloadStatus.MetadataOnly;
        await _databaseService.SaveDownloadedTripAsync(trip);

        _logger.LogInformation("Deleted {Count} tiles for trip {TripId}", deletedCount, tripServerId);
        return deletedCount;
    }

    #region Trip Editing

    /// <summary>
    /// Updates a trip's name in local storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="newName">The new trip name.</param>
    public async Task UpdateTripNameAsync(Guid tripServerId, string newName)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot update trip name - trip {TripId} not found", tripServerId);
            return;
        }

        trip.Name = newName;
        await _databaseService.SaveDownloadedTripAsync(trip);
        _logger.LogInformation("Updated trip name to '{NewName}' for trip {TripId}", newName, tripServerId);
    }

    /// <summary>
    /// Updates a trip's notes in local storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <param name="newNotes">The new trip notes (HTML).</param>
    public async Task UpdateTripNotesAsync(Guid tripServerId, string? newNotes)
    {
        var trip = await _databaseService.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot update trip notes - trip {TripId} not found", tripServerId);
            return;
        }

        trip.Notes = newNotes;
        await _databaseService.SaveDownloadedTripAsync(trip);
        _logger.LogInformation("Updated trip notes for trip {TripId}", tripServerId);
    }

    #endregion

    #region Pause/Resume/Cancel

    /// <summary>
    /// Pauses a download in progress.
    /// Sets a flag that the download loop checks to gracefully pause.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>True if pause was initiated, false if download not found.</returns>
    public async Task<bool> PauseDownloadAsync(int tripId)
    {
        var trip = await _databaseService.GetDownloadedTripAsync(tripId);
        if (trip == null || trip.Status != TripDownloadStatus.Downloading)
        {
            _logger.LogWarning("Cannot pause - trip {TripId} not downloading", tripId);
            return false;
        }

        _downloadStateManager.RequestStop(tripId, DownloadStopReason.UserPause);
        _logger.LogInformation("Pause requested for trip {TripId}", tripId);
        return true;
    }

    /// <summary>
    /// Resumes a paused download.
    /// Loads the saved state and continues downloading remaining tiles.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if resumed successfully, false if no saved state found.</returns>
    public async Task<bool> ResumeDownloadAsync(int tripId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var state = await _databaseService.GetDownloadStateAsync(tripId);
        if (state == null)
        {
            _logger.LogWarning("Cannot resume - no saved state for trip {TripId}", tripId);
            return false;
        }

        var trip = await _databaseService.GetDownloadedTripAsync(tripId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot resume - trip {TripId} not found", tripId);
            return false;
        }

        // Guard against concurrent downloads of the same trip
        if (!_activeDownloads.TryAdd(trip.ServerId, true))
        {
            _logger.LogWarning("Download already in progress for trip {TripName} ({TripId})",
                trip.Name, trip.ServerId);
            return false;
        }

        try
        {
            // Clear stop request and reset per-trip warning state for resumed download
            _downloadStateManager.ClearStopRequest(tripId);
            _tripWarningStates[tripId] = new TripWarningState();

            // Update state to in progress
            state.Status = DownloadStateStatus.InProgress;
            state.InterruptionReason = string.Empty;
            await _databaseService.SaveDownloadStateAsync(state);

            // Update trip status
            trip.Status = TripDownloadStatus.Downloading;
            await _databaseService.SaveDownloadedTripAsync(trip);

            _logger.LogInformation("Resuming download for trip {TripId}: {Completed}/{Total} tiles",
                tripId, state.CompletedTileCount, state.TotalTileCount);

            // Parse remaining tiles with graceful error handling
            List<TileCoordinate> remainingTiles;
            try
            {
                remainingTiles = JsonSerializer.Deserialize<List<TileCoordinate>>(state.RemainingTilesJson)
                    ?? new List<TileCoordinate>();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize remaining tiles for trip {TripId}, clearing corrupt state", tripId);
                await _databaseService.DeleteDownloadStateAsync(tripId);
                trip.Status = TripDownloadStatus.Failed;
                trip.LastError = "Corrupt download state - please restart download";
                await _databaseService.SaveDownloadedTripAsync(trip);
                return false;
            }

            if (remainingTiles.Count == 0)
            {
                // All tiles already downloaded
                trip.Status = TripDownloadStatus.Complete;
                await _databaseService.SaveDownloadedTripAsync(trip);
                await _databaseService.DeleteDownloadStateAsync(tripId);
                return true;
            }

            // Resume tile download
            var downloadResult = await DownloadTilesWithStateAsync(
                trip,
                remainingTiles,
                state.CompletedTileCount,
                state.TotalTileCount,
                state.DownloadedBytes,
                cancellationToken);

            // Check if paused or hit limit during resume
            if (downloadResult.WasPaused || downloadResult.WasLimitReached)
            {
                _logger.LogInformation("Download stopped during resume for trip {TripId}: Paused={Paused}, LimitReached={LimitReached}",
                    tripId, downloadResult.WasPaused, downloadResult.WasLimitReached);
                return true; // State already saved
            }

            // Complete - update tile count to reflect actual downloads
            trip.Status = TripDownloadStatus.Complete;
            trip.TileCount = state.CompletedTileCount + downloadResult.TilesDownloaded;
            trip.TotalSizeBytes = downloadResult.TotalBytes;
            trip.ProgressPercent = 100;
            await _databaseService.SaveDownloadedTripAsync(trip);
            // Note: DownloadTilesWithStateAsync already deletes state on successful completion

            RaiseProgress(tripId, 100, "Download complete");
            _logger.LogInformation("Resumed download complete for trip {TripId}", tripId);

            // Raise download completed event
            DownloadCompleted?.Invoke(this, new DownloadTerminalEventArgs
            {
                TripId = trip.Id,
                TripServerId = trip.ServerId,
                TripName = trip.Name,
                TilesDownloaded = trip.TileCount,
                TotalBytes = trip.TotalSizeBytes
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Resumed download cancelled for trip {TripId}", tripId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming download for trip {TripId}", tripId);
            trip.Status = TripDownloadStatus.Failed;
            trip.LastError = ex.Message;
            await _databaseService.SaveDownloadedTripAsync(trip);

            // Delete orphaned download state to avoid stale resume attempts
            await _databaseService.DeleteDownloadStateAsync(tripId);

            // Raise download failed event
            DownloadFailed?.Invoke(this, new DownloadTerminalEventArgs
            {
                TripId = trip.Id,
                TripServerId = trip.ServerId,
                TripName = trip.Name,
                TilesDownloaded = trip.TileCount,
                TotalBytes = trip.TotalSizeBytes,
                ErrorMessage = ex.Message
            });

            return false;
        }
        finally
        {
            // Always release the active download guard
            _activeDownloads.TryRemove(trip.ServerId, out _);
            // Clean up per-trip warning state
            _tripWarningStates.TryRemove(tripId, out _);
        }
    }

    /// <summary>
    /// Cancels a download and optionally cleans up partial data.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="cleanup">If true, delete all downloaded tiles and trip data.</param>
    /// <returns>True if cancelled successfully.</returns>
    public async Task<bool> CancelDownloadAsync(int tripId, bool cleanup = false)
    {
        ThrowIfDisposed();

        // Set cancel flag to stop download loop - distinct from pause to prevent state saving
        // The flag will be cleaned up when starting a new download for this trip
        _downloadStateManager.RequestStop(tripId, DownloadStopReason.UserCancel);

        var trip = await _databaseService.GetDownloadedTripAsync(tripId);
        if (trip == null)
        {
            _logger.LogWarning("Cannot cancel - trip {TripId} not found", tripId);
            // Still clean up the stop request if trip doesn't exist
            _downloadStateManager.ClearStopRequest(tripId);
            return false;
        }

        // Delete the download state - cancelled downloads are not resumable
        await _databaseService.DeleteDownloadStateAsync(tripId);

        if (cleanup)
        {
            // Delete trip and all associated data
            await _databaseService.DeleteDownloadedTripAsync(tripId);
            _logger.LogInformation("Cancelled and cleaned up trip {TripId}", tripId);

            // Clean up stop request and warning state since trip is deleted
            _downloadStateManager.ClearStopRequest(tripId);
            _tripWarningStates.TryRemove(tripId, out _);
        }
        else
        {
            // Keep partial download but mark as cancelled (distinct from failed)
            trip.Status = TripDownloadStatus.Cancelled;
            trip.LastError = "Download cancelled by user";
            await _databaseService.SaveDownloadedTripAsync(trip);
            _logger.LogInformation("Cancelled trip {TripId}, keeping partial data", tripId);

            // Clean up warning state but keep stop request until download loop exits
            _tripWarningStates.TryRemove(tripId, out _);
        }

        return true;
    }

    /// <summary>
    /// Gets all paused/resumable downloads.
    /// </summary>
    /// <returns>List of download states that can be resumed.</returns>
    public async Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync()
    {
        return await _databaseService.GetPausedDownloadsAsync();
    }

    /// <summary>
    /// Checks if a download is paused for a specific trip.
    /// Checks both in-memory pause flag and persisted database state.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>True if download is paused.</returns>
    public async Task<bool> IsDownloadPausedAsync(int tripId)
    {
        // Check in-memory flag first (faster, catches recent pause requests)
        // Only UserPause is considered "paused" - UserCancel is not resumable
        if (_downloadStateManager.TryGetStopReason(tripId, out var reason) &&
            reason == DownloadStopReason.UserPause)
        {
            return true;
        }

        // Check persisted state (for pauses from previous sessions)
        var state = await _databaseService.GetDownloadStateAsync(tripId);
        return state?.Status == DownloadStateStatus.Paused ||
               state?.Status == DownloadStateStatus.LimitReached;
    }

    /// <summary>
    /// Saves the current download state for later resumption.
    /// </summary>
    /// <param name="trip">The trip entity.</param>
    /// <param name="remainingTiles">Tiles remaining to download.</param>
    /// <param name="completedCount">Number of tiles completed.</param>
    /// <param name="totalCount">Total tiles in download.</param>
    /// <param name="downloadedBytes">Bytes downloaded so far.</param>
    /// <param name="interruptionReason">Reason for interruption.</param>
    /// <param name="status">Download state status (defaults to Paused).</param>
    private async Task SaveDownloadStateAsync(
        DownloadedTripEntity trip,
        List<TileCoordinate> remainingTiles,
        int completedCount,
        int totalCount,
        long downloadedBytes,
        string interruptionReason,
        string status = DownloadStateStatus.Paused)
    {
        var state = new TripDownloadStateEntity
        {
            TripId = trip.Id,
            TripServerId = trip.ServerId,
            TripName = trip.Name,
            RemainingTilesJson = JsonSerializer.Serialize(remainingTiles),
            CompletedTileCount = completedCount,
            TotalTileCount = totalCount,
            DownloadedBytes = downloadedBytes,
            Status = status,
            InterruptionReason = interruptionReason,
            PausedAt = DateTime.UtcNow
        };

        await _databaseService.SaveDownloadStateAsync(state);
        _logger.LogInformation("Saved download state for trip {TripId}: {Completed}/{Total} tiles, status: {Status}, reason: {Reason}",
            trip.Id, completedCount, totalCount, status, interruptionReason);
    }

    #endregion

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

            if (!_tileDownloadService.IsNetworkAvailable())
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

            if (!_tileDownloadService.IsNetworkAvailable())
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
                Notes = r.Notes,
                CoverImageUrl = r.CoverImageUrl,
                SortOrder = r.SortOrder > 0 ? r.SortOrder : index,
                PlaceCount = r.Places.Count
            }).ToList();

            await _databaseService.SaveOfflineAreasAsync(localTrip.Id, areas);
            localTrip.RegionCount = areas.Count;

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
                        SortOrder = place.SortOrder is > 0 ? place.SortOrder.Value : placeIndex++
                    });
                }
            }

            await _databaseService.SaveOfflinePlacesAsync(localTrip.Id, places);
            localTrip.PlaceCount = places.Count;

            RaiseProgress(localTrip.Id, 55, "Updating segments...");

            // Build place name lookup for segment origin/destination (includes region name: "Place, Region")
            var syncPlaceNameLookup = new Dictionary<Guid, string>();
            foreach (var region in serverTrip.Regions)
            {
                foreach (var place in region.Places)
                {
                    // Format: "PlaceName, RegionName" (or just "PlaceName" if region has same name)
                    var displayName = string.Equals(place.Name, region.Name, StringComparison.OrdinalIgnoreCase)
                        ? place.Name
                        : $"{place.Name}, {region.Name}";
                    syncPlaceNameLookup[place.Id] = displayName;
                }
            }

            // Update segments with place names
            var segments = serverTrip.Segments.Select((s, index) => new OfflineSegmentEntity
            {
                ServerId = s.Id,
                OriginId = s.OriginId ?? Guid.Empty,
                OriginName = s.OriginId.HasValue && syncPlaceNameLookup.TryGetValue(s.OriginId.Value, out var fromName) ? fromName : null,
                DestinationId = s.DestinationId ?? Guid.Empty,
                DestinationName = s.DestinationId.HasValue && syncPlaceNameLookup.TryGetValue(s.DestinationId.Value, out var toName) ? toName : null,
                TransportMode = s.TransportMode,
                DistanceKm = s.DistanceKm,
                DurationMinutes = (int?)s.DurationMinutes,
                Notes = s.Notes,
                Geometry = s.Geometry,
                SortOrder = index
            }).ToList();

            await _databaseService.SaveOfflineSegmentsAsync(localTrip.Id, segments);
            localTrip.SegmentCount = segments.Count;

            RaiseProgress(localTrip.Id, 65, "Updating polygon zones...");

            // Update polygon zones (TripArea) from each region
            var polygons = new List<OfflinePolygonEntity>();
            foreach (var region in serverTrip.Regions)
            {
                foreach (var tripArea in region.Areas)
                {
                    polygons.Add(new OfflinePolygonEntity
                    {
                        ServerId = tripArea.Id,
                        RegionId = region.Id,
                        Name = tripArea.Name,
                        Notes = tripArea.Notes,
                        FillColor = tripArea.FillColor,
                        StrokeColor = tripArea.StrokeColor,
                        GeometryGeoJson = tripArea.GeometryGeoJson,
                        SortOrder = tripArea.SortOrder ?? 0
                    });
                }
            }
            await _databaseService.SaveOfflinePolygonsAsync(localTrip.Id, polygons);
            localTrip.AreaCount = polygons.Count;

            RaiseProgress(localTrip.Id, 75, "Checking map coverage...");

            // Check if bounding box changed significantly (needs tile re-download)
            var boundingBoxChanged = serverTrip.BoundingBox != null && HasBoundingBoxChangedSignificantly(
                localTrip.BoundingBoxNorth, localTrip.BoundingBoxSouth, localTrip.BoundingBoxEast, localTrip.BoundingBoxWest,
                serverTrip.BoundingBox);

            if (boundingBoxChanged)
            {
                // Always update bounding box metadata from server (independent of tile download)
                localTrip.BoundingBoxNorth = serverTrip.BoundingBox!.North;
                localTrip.BoundingBoxSouth = serverTrip.BoundingBox.South;
                localTrip.BoundingBoxEast = serverTrip.BoundingBox.East;
                localTrip.BoundingBoxWest = serverTrip.BoundingBox.West;

                // Guard against concurrent tile downloads for the same trip
                if (!_activeDownloads.TryAdd(tripServerId, true))
                {
                    // Another download in progress - metadata is synced but tiles are not
                    // Return null to signal sync didn't fully complete; caller can retry later
                    _logger.LogWarning("Tile download already in progress for trip {TripId}, sync incomplete", tripServerId);
                    return null;
                }

                try
                {
                    _logger.LogInformation("Bounding box changed for trip {TripName}, re-downloading tiles", localTrip.Name);
                    RaiseProgress(localTrip.Id, 80, "Downloading new map tiles...");

                    // Re-download tiles for new bounding box using unified download path
                    // This respects cache limits and supports pause/resume during sync
                    var tileCoords = CalculateTilesForBoundingBox(serverTrip.BoundingBox);
                    if (tileCoords.Count > 0)
                    {
                        // Initialize per-trip warning state for sync download
                        _tripWarningStates[localTrip.Id] = new TripWarningState();

                        var downloadResult = await DownloadTilesWithStateAsync(
                            localTrip,
                            tileCoords,
                            initialCompleted: 0,
                            totalTiles: tileCoords.Count,
                            initialBytes: 0,
                            cancellationToken);

                        localTrip.TileCount = downloadResult.TilesDownloaded;
                        localTrip.TotalSizeBytes = downloadResult.TotalBytes;

                        // Clean up warning state
                        _tripWarningStates.TryRemove(localTrip.Id, out _);

                        // If paused or limit reached, don't mark as complete
                        if (downloadResult.WasPaused || downloadResult.WasLimitReached)
                        {
                            _logger.LogWarning("Sync tile download stopped for trip {TripName}: Paused={Paused}, LimitReached={LimitReached}",
                                localTrip.Name, downloadResult.WasPaused, downloadResult.WasLimitReached);
                            // Trip remains in current state, tiles partially downloaded
                        }
                    }
                }
                finally
                {
                    _activeDownloads.TryRemove(tripServerId, out _);
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

        if (!_tileDownloadService.IsNetworkAvailable())
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
        if (!_tileDownloadService.IsNetworkAvailable())
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
        RaiseEventSafe(ProgressChanged, new DownloadProgressEventArgs
        {
            TripId = tripId,
            ProgressPercent = percent,
            StatusMessage = message
        });
    }

    /// <summary>
    /// Safely raises an event, catching and logging any subscriber exceptions.
    /// </summary>
    private void RaiseEventSafe<T>(EventHandler<T>? eventHandler, T args) where T : class
    {
        if (eventHandler == null)
            return;

        try
        {
            eventHandler.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event handler threw exception for {EventType}", typeof(T).Name);
        }
    }

    #region Tile Download

    /// <summary>
    /// Calculates all tile coordinates needed for a bounding box.
    /// Uses intelligent zoom level selection based on area size.
    /// Enforces maximum tile count to prevent memory exhaustion.
    /// </summary>
    /// <param name="bbox">The bounding box.</param>
    /// <returns>List of tile coordinates (capped at MaxTileCount).</returns>
    private List<TileCoordinate> CalculateTilesForBoundingBox(BoundingBox bbox)
    {
        var tiles = new List<TileCoordinate>();

        // Calculate area to determine appropriate max zoom
        var areaSquareDegrees = (bbox.North - bbox.South) * (bbox.East - bbox.West);
        var recommendedMaxZoom = GetRecommendedMaxZoom(areaSquareDegrees);

        int minZoom = TileCacheConstants.MinZoomLevel;
        var effectiveMaxZoom = Math.Min(recommendedMaxZoom, TileCacheConstants.MaxZoomLevel);

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

                    // Enforce maximum tile count to prevent memory exhaustion
                    if (tiles.Count >= MaxTileCount)
                    {
                        _logger.LogWarning("Tile count limit reached ({MaxTiles}), truncating at zoom {Zoom}",
                            MaxTileCount, zoom);
                        return tiles;
                    }
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
    /// Downloads tiles with state saving, pause/limit checking, retry logic, and network monitoring.
    /// Used for both initial downloads and resume operations.
    /// </summary>
    /// <param name="trip">The trip entity.</param>
    /// <param name="tiles">Remaining tiles to download.</param>
    /// <param name="initialCompleted">Number of tiles already completed.</param>
    /// <param name="totalTiles">Total tiles in the download.</param>
    /// <param name="initialBytes">Bytes already downloaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing total bytes downloaded and actual tiles completed.</returns>
    private async Task<BatchDownloadResult> DownloadTilesWithStateAsync(
        DownloadedTripEntity trip,
        List<TileCoordinate> tiles,
        int initialCompleted,
        int totalTiles,
        long initialBytes,
        CancellationToken cancellationToken)
    {
        // Early return for empty tile list
        if (tiles.Count == 0)
        {
            return new BatchDownloadResult(
                TotalBytes: initialBytes,
                TilesDownloaded: 0,
                WasPaused: false,
                WasLimitReached: false);
        }

        // Clear any stale stop request from previous cancel/pause
        // This allows re-downloading a trip that was previously cancelled
        _downloadStateManager.ClearStopRequest(trip.Id);

        // Thread-safe counters for parallel downloads
        long totalBytes = initialBytes;
        int processed = 0; // Tiles processed this session (success or fail)
        int tilesDownloadedThisSession = 0;
        int lastStateSaveProcessed = 0;
        var tileCacheDir = _tileDownloadService.GetTileCacheDirectory(trip.Id);
        var failedTiles = new ConcurrentBag<TileCoordinate>();
        var succeededIndices = new ConcurrentDictionary<int, bool>(); // Track which tile indices succeeded

        // Ensure cache directory exists
        Directory.CreateDirectory(tileCacheDir);

        // Check cache limit at start (before downloading any tiles)
        var initialLimitCheck = await _cacheLimitEnforcer.CheckLimitAsync();
        if (initialLimitCheck.IsLimitReached)
        {
            var eventArgs = new CacheLimitEventArgs
            {
                TripId = trip.Id,
                TripName = trip.Name,
                CurrentUsageMB = initialLimitCheck.CurrentSizeMB,
                MaxSizeMB = initialLimitCheck.MaxSizeMB,
                UsagePercent = initialLimitCheck.UsagePercent,
                Level = CacheLimitLevel.LimitReached
            };
            CacheLimitReached?.Invoke(this, eventArgs);

            await SaveDownloadStateAsync(trip, tiles, initialCompleted, totalTiles, totalBytes,
                DownloadPauseReason.CacheLimitReached, DownloadStateStatus.LimitReached);
            trip.Status = TripDownloadStatus.Downloading;
            await _databaseService.SaveDownloadedTripAsync(trip);
            _logger.LogWarning("Cache limit already reached before download for trip {TripId}", trip.Id);

            return new BatchDownloadResult(
                TotalBytes: totalBytes,
                TilesDownloaded: 0,
                WasPaused: false,
                WasLimitReached: true);
        }

        // Track stop reason for parallel download
        var stopReason = new ParallelDownloadStopReason();
        var progressLock = new object();
        var lastProgressReport = 0;

        // Get concurrency setting
        var maxConcurrency = MaxConcurrentDownloads;
        _logger.LogInformation("Starting parallel tile download for trip {TripId}: {TileCount} tiles, concurrency {Concurrency}",
            trip.Id, tiles.Count, maxConcurrency);

        // Use Parallel.ForEachAsync for parallel downloads with controlled concurrency
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        };

        try
        {
            await Parallel.ForEachAsync(
                tiles.Select((tile, index) => (tile, index)),
                parallelOptions,
                async (item, ct) =>
                {
                    // Check if we should stop (pause, cancel, limit reached)
                    if (stopReason.ShouldStop)
                        return;

                    // Check stop request (pause or cancel)
                    if (_downloadStateManager.TryGetStopReason(trip.Id, out var requestedStopReason))
                    {
                        stopReason.SetPaused(requestedStopReason);
                        return;
                    }

                    // Check network (only first tile in batch to avoid thrashing)
                    if (item.index % maxConcurrency == 0 && !_tileDownloadService.IsNetworkAvailable())
                    {
                        stopReason.SetPaused(DownloadPauseReason.NetworkLost);
                        _logger.LogWarning("Network lost during download for trip {TripId}", trip.Id);
                        return;
                    }

                    // Download the tile
                    var bytes = await DownloadTileWithRetryAsync(trip.Id, item.tile, tileCacheDir, ct);

                    if (bytes > 0)
                    {
                        Interlocked.Add(ref totalBytes, bytes);
                        Interlocked.Increment(ref tilesDownloadedThisSession);
                        succeededIndices[item.index] = true; // Track successful tile index
                    }
                    else
                    {
                        failedTiles.Add(item.tile);
                    }

                    var currentProcessed = Interlocked.Increment(ref processed);
                    var currentCompleted = initialCompleted + currentProcessed;

                    // Thread-safe progress reporting (throttled to avoid UI overload)
                    lock (progressLock)
                    {
                        if (currentCompleted - lastProgressReport >= maxConcurrency || currentCompleted == totalTiles)
                        {
                            lastProgressReport = currentCompleted;
                            var tilesToDownload = totalTiles - initialCompleted;
                            // Convert to double first to avoid integer overflow for large tile counts
                            int percent = tilesToDownload > 0
                                ? Math.Min(95, 55 + (int)(((double)currentCompleted - initialCompleted) * 40.0 / tilesToDownload))
                                : 95;
                            RaiseProgress(trip.Id, percent, $"Downloading tiles: {currentCompleted}/{totalTiles}");
                        }
                    }

                    // Periodic cache limit check (synchronized across parallel threads)
                    if (currentCompleted % CacheLimitCheckIntervalTiles == 0)
                    {
                        var limitResult = await _cacheLimitEnforcer.GetCachedLimitCheckAsync();

                        // Raise warning/critical events using per-trip state
                        if (_tripWarningStates.TryGetValue(trip.Id, out var warningState))
                        {
                            if (limitResult.UsagePercent >= 90 && limitResult.UsagePercent < 100)
                            {
                                if (warningState.TrySetCriticalRaised())
                                {
                                    RaiseEventSafe(CacheCritical, new CacheLimitEventArgs
                                    {
                                        TripId = trip.Id,
                                        TripName = trip.Name,
                                        CurrentUsageMB = limitResult.CurrentSizeMB,
                                        MaxSizeMB = limitResult.MaxSizeMB,
                                        UsagePercent = limitResult.UsagePercent,
                                        Level = CacheLimitLevel.Critical
                                    });
                                }
                            }
                            else if (limitResult.UsagePercent >= 80 && limitResult.UsagePercent < 90)
                            {
                                if (warningState.TrySetWarningRaised())
                                {
                                    RaiseEventSafe(CacheWarning, new CacheLimitEventArgs
                                    {
                                        TripId = trip.Id,
                                        TripName = trip.Name,
                                        CurrentUsageMB = limitResult.CurrentSizeMB,
                                        MaxSizeMB = limitResult.MaxSizeMB,
                                        UsagePercent = limitResult.UsagePercent,
                                        Level = CacheLimitLevel.Warning
                                    });
                                }
                            }
                        }

                        if (limitResult.IsLimitReached)
                        {
                            stopReason.SetLimitReached();
                        }
                    }

                    // Periodic storage check
                    if (currentCompleted % StorageCheckIntervalTiles == 0)
                    {
                        if (!_tileDownloadService.HasSufficientStorage())
                        {
                            stopReason.SetPaused(DownloadPauseReason.StorageLow);
                            _logger.LogWarning("Storage low during download for trip {TripId}", trip.Id);
                        }
                    }

                    // Periodic state save
                    var lastSave = Volatile.Read(ref lastStateSaveProcessed);
                    if (currentProcessed - lastSave >= StateSaveIntervalTiles)
                    {
                        // Only one thread saves state at a time
                        if (Interlocked.CompareExchange(ref lastStateSaveProcessed, currentProcessed, lastSave) == lastSave)
                        {
                            // Take atomic snapshot of succeeded indices for consistent remaining calculation
                            // Remaining = all tiles not in the succeeded set (includes unprocessed + failed)
                            var succeededSnapshot = succeededIndices.Keys.ToHashSet();
                            var remaining = tiles.Where((_, idx) => !succeededSnapshot.Contains(idx)).ToList();
                            await SaveDownloadStateAsync(trip, remaining, initialCompleted + succeededSnapshot.Count, totalTiles,
                                Interlocked.Read(ref totalBytes), DownloadPauseReason.PeriodicSave, DownloadStateStatus.InProgress);
                        }
                    }
                });
        }
        catch (OperationCanceledException)
        {
            // Check if this was a pause request (which also cancels CTS) or actual cancel
            // This handles the race where CTS cancellation fires before workers see _downloadStateManager
            if (_downloadStateManager.TryGetStopReason(trip.Id, out var requestedReason) &&
                requestedReason == DownloadStopReason.UserPause)
            {
                stopReason.SetPaused(DownloadPauseReason.UserPause);
            }
            else
            {
                stopReason.SetPaused(DownloadPauseReason.UserCancel);
            }
        }

        // Handle stop reason if download was interrupted
        var finalProcessed = Volatile.Read(ref processed);
        var finalSucceeded = succeededIndices.Count;
        var finalBytes = Interlocked.Read(ref totalBytes);

        if (stopReason.ShouldStop)
        {
            // Remaining = tiles not yet processed + failed tiles (not succeeded)
            var remainingTiles = tiles.Where((_, idx) => !succeededIndices.ContainsKey(idx)).ToList();

            var actualCompleted = initialCompleted + finalSucceeded;

            if (stopReason.WasLimitReached)
            {
                var limitResult = await _cacheLimitEnforcer.CheckLimitAsync();
                var eventArgs = new CacheLimitEventArgs
                {
                    TripId = trip.Id,
                    TripName = trip.Name,
                    CurrentUsageMB = limitResult.CurrentSizeMB,
                    MaxSizeMB = limitResult.MaxSizeMB,
                    UsagePercent = limitResult.UsagePercent,
                    Level = CacheLimitLevel.LimitReached
                };
                CacheLimitReached?.Invoke(this, eventArgs);

                await SaveDownloadStateAsync(trip, remainingTiles, actualCompleted, totalTiles, finalBytes,
                    DownloadPauseReason.CacheLimitReached, DownloadStateStatus.LimitReached);

                trip.Status = TripDownloadStatus.Downloading;
                await _databaseService.SaveDownloadedTripAsync(trip);
            }
            else if (stopReason.PauseReason != DownloadPauseReason.UserCancel)
            {
                // Save state for pause (resumable) - but NOT for cancel
                // CancelDownloadAsync already deleted state and set trip.Status = Cancelled
                await SaveDownloadStateAsync(trip, remainingTiles, actualCompleted, totalTiles, finalBytes,
                    stopReason.PauseReason);

                trip.Status = TripDownloadStatus.Downloading;
                await _databaseService.SaveDownloadedTripAsync(trip);
            }
            // For UserCancel: don't save state or update trip status
            // CancelDownloadAsync already handled cleanup

            _logger.LogInformation("Download stopped for trip {TripId}: {Completed}/{Total} tiles (processed {Processed}), Reason: {Reason}",
                trip.Id, actualCompleted, totalTiles, finalProcessed, stopReason.PauseReason);

            // Raise download paused event
            var pauseReasonType = stopReason.PauseReason switch
            {
                DownloadPauseReason.UserPause => DownloadPauseReasonType.UserRequest,
                DownloadPauseReason.UserCancel => DownloadPauseReasonType.UserCancel,
                DownloadPauseReason.NetworkLost => DownloadPauseReasonType.NetworkLost,
                DownloadPauseReason.StorageLow => DownloadPauseReasonType.StorageLow,
                DownloadPauseReason.CacheLimitReached => DownloadPauseReasonType.CacheLimitReached,
                _ => DownloadPauseReasonType.UserRequest
            };

            DownloadPaused?.Invoke(this, new DownloadPausedEventArgs
            {
                TripId = trip.Id,
                TripServerId = trip.ServerId,
                TripName = trip.Name,
                Reason = pauseReasonType,
                TilesCompleted = actualCompleted,
                TotalTiles = totalTiles,
                CanResume = stopReason.PauseReason != DownloadPauseReason.UserCancel
            });

            return new BatchDownloadResult(
                TotalBytes: finalBytes,
                TilesDownloaded: Volatile.Read(ref tilesDownloadedThisSession),
                WasPaused: stopReason.WasPaused,
                WasLimitReached: stopReason.WasLimitReached);
        }

        // Retry failed tiles once at the end (sequentially to avoid overwhelming server)
        var failedTilesList = failedTiles.ToList();
        if (failedTilesList.Count > 0)
        {
            _logger.LogInformation("Retrying {Count} failed tiles for trip {TripId}", failedTilesList.Count, trip.Id);
            foreach (var tile in failedTilesList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var bytes = await DownloadTileWithRetryAsync(trip.Id, tile, tileCacheDir, cancellationToken);
                if (bytes > 0)
                {
                    Interlocked.Add(ref totalBytes, bytes);
                    Interlocked.Increment(ref tilesDownloadedThisSession);
                }
            }
        }

        // Clean up download state on successful completion
        await _databaseService.DeleteDownloadStateAsync(trip.Id);

        return new BatchDownloadResult(
            TotalBytes: Interlocked.Read(ref totalBytes),
            TilesDownloaded: Volatile.Read(ref tilesDownloadedThisSession),
            WasPaused: false,
            WasLimitReached: false);
    }

    /// <summary>
    /// Downloads a tile with retry logic.
    /// </summary>
    private async Task<long> DownloadTileWithRetryAsync(
        int tripId,
        TileCoordinate tile,
        string cacheDir,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= MaxTileRetries; attempt++)
        {
            var bytes = await DownloadTileAsync(_tileHttpClient, tripId, tile, cacheDir, cancellationToken);
            if (bytes > 0)
                return bytes;

            if (attempt < MaxTileRetries)
            {
                _logger.LogDebug("Retry {Attempt}/{Max} for tile {TileId}", attempt + 1, MaxTileRetries, tile.Id);
                await Task.Delay(RetryDelayMs * (attempt + 1), cancellationToken); // Exponential backoff
            }
        }

        _logger.LogWarning("All retries failed for tile {TileId}", tile.Id);
        return 0;
    }

    /// <summary>
    /// Checks if the trip cache limit has been reached.
    /// </summary>
    /// <returns>Result indicating current usage and whether limit is reached.</returns>
    public Task<CacheLimitCheckResult> CheckTripCacheLimitAsync()
    {
        return _cacheLimitEnforcer.CheckLimitAsync();
    }

    /// <summary>
    /// Estimates the download size for a trip.
    /// </summary>
    /// <param name="tileCount">Number of tiles to download.</param>
    /// <returns>Estimated size in bytes.</returns>
    public long EstimateDownloadSize(int tileCount)
    {
        return _cacheLimitEnforcer.EstimateDownloadSize(tileCount);
    }

    /// <summary>
    /// Estimates the tile count for a trip based on its bounding box.
    /// </summary>
    /// <param name="boundingBox">The trip's bounding box.</param>
    /// <returns>Estimated number of tiles.</returns>
    public int EstimateTileCount(BoundingBox? boundingBox)
    {
        if (boundingBox == null)
            return 0;

        return _cacheLimitEnforcer.EstimateTileCount(boundingBox);
    }

    /// <summary>
    /// Checks if there's enough cache quota for a trip download.
    /// </summary>
    /// <param name="boundingBox">The trip's bounding box.</param>
    /// <returns>Result with quota details and tile count.</returns>
    public Task<CacheQuotaCheckResult> CheckCacheQuotaForTripAsync(BoundingBox? boundingBox)
    {
        if (boundingBox == null)
        {
            return Task.FromResult(new CacheQuotaCheckResult
            {
                TileCount = 0,
                EstimatedSizeBytes = 0,
                HasSufficientQuota = true
            });
        }

        return _cacheLimitEnforcer.CheckQuotaForTripAsync(boundingBox);
    }

    /// <summary>
    /// Checks if there's enough cache quota for a download.
    /// </summary>
    /// <param name="estimatedBytes">Estimated download size in bytes.</param>
    /// <returns>Result with quota details.</returns>
    public Task<CacheQuotaCheckResult> CheckCacheQuotaAsync(long estimatedBytes)
    {
        return _cacheLimitEnforcer.CheckQuotaAsync(estimatedBytes);
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
            if (!_tileDownloadService.IsNetworkAvailable())
            {
                _logger.LogDebug("Waiting for network before downloading tile {TileId}...", tile.Id);
                if (!await _tileDownloadService.WaitForNetworkAsync(TimeSpan.FromSeconds(30), cancellationToken))
                {
                    _logger.LogWarning("Network not available for tile {TileId}", tile.Id);
                    return 0;
                }
            }

            // Enforce rate limiting
            await _tileDownloadService.EnforceRateLimitAsync(cancellationToken);

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

            // Verify PNG integrity - check file signature
            if (!IsValidPng(bytes))
            {
                _logger.LogWarning("Invalid PNG data for tile {TileId} (signature mismatch)", tile.Id);
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
    /// Gets a cached tile file path.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="zoom">Zoom level.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>File path if exists, null otherwise.</returns>
    public string? GetCachedTilePath(int tripId, int zoom, int x, int y)
    {
        ThrowIfDisposed();

        var filePath = Path.Combine(_tileDownloadService.GetTileCacheDirectory(tripId), $"{zoom}", $"{x}", $"{y}.png");
        return File.Exists(filePath) ? filePath : null;
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// Cleans up orphaned temporary files from interrupted downloads.
    /// Should be called periodically (e.g., on app startup or after download completion).
    /// </summary>
    /// <returns>Number of temp files cleaned up.</returns>
    public int CleanupOrphanedTempFiles()
    {
        ThrowIfDisposed();

        var cleanedCount = 0;
        try
        {
            var tilesRootDir = Path.Combine(FileSystem.CacheDirectory, "tiles");
            if (!Directory.Exists(tilesRootDir))
                return 0;

            // Find all .tmp files in the tiles directory tree
            var tempFiles = Directory.GetFiles(tilesRootDir, "*.tmp", SearchOption.AllDirectories);
            var maxAge = DateTime.UtcNow.AddHours(-TempFileMaxAgeHours);

            foreach (var tempFile in tempFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(tempFile);

                    // Only delete temp files older than configured age (to avoid deleting active downloads)
                    if (fileInfo.LastWriteTimeUtc < maxAge)
                    {
                        File.Delete(tempFile);
                        cleanedCount++;
                    }
                }
                catch (IOException ex)
                {
                    _logger.LogDebug(ex, "I/O error deleting temp file: {FilePath}", tempFile);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogDebug(ex, "Access denied deleting temp file: {FilePath}", tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Unexpected error deleting temp file: {FilePath}", tempFile);
                }
            }

            if (cleanedCount > 0)
            {
                _logger.LogInformation("Cleaned up {Count} orphaned temp files", cleanedCount);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error during temp file cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error during temp file cleanup");
        }

        return cleanedCount;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Clear active downloads tracking (graceful shutdown)
            // Active downloads will be stopped when their cancellation tokens are triggered
            _activeDownloads.Clear();
            _tripWarningStates.Clear();

            // Dispose managed resources
            _tileHttpClient?.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Throws ObjectDisposedException if the service has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TripDownloadService));
        }
    }

    /// <summary>
    /// Verifies that the byte array contains a valid PNG file by checking the file signature.
    /// </summary>
    /// <param name="bytes">The byte array to verify.</param>
    /// <returns>True if the data starts with a valid PNG signature, false otherwise.</returns>
    private static bool IsValidPng(byte[] bytes)
    {
        if (bytes.Length < PngSignature.Length)
            return false;

        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (bytes[i] != PngSignature[i])
                return false;
        }

        return true;
    }

    #endregion
}

/// <summary>
/// Per-trip warning state to track if warning/critical events have been raised.
/// Prevents duplicate warnings when multiple trips download concurrently.
/// </summary>
/// <remarks>
/// <para>Thread Safety: This class uses Interlocked for atomic compare-and-swap operations,
/// ensuring only one thread can transition each flag from false to true.</para>
/// <para>Lifecycle: Created when a download starts, cleaned up when download completes
/// (success, failure, or cancellation).</para>
/// </remarks>
internal class TripWarningState
{
    private int _warningRaised;
    private int _criticalRaised;

    /// <summary>
    /// Whether the warning event (80%) has been raised for this trip.
    /// </summary>
    public bool WarningRaised => _warningRaised == 1;

    /// <summary>
    /// Whether the critical event (90%) has been raised for this trip.
    /// </summary>
    public bool CriticalRaised => _criticalRaised == 1;

    /// <summary>
    /// Atomically sets the warning flag if not already set.
    /// </summary>
    /// <returns>True if this call set the flag, false if already set.</returns>
    public bool TrySetWarningRaised() =>
        Interlocked.CompareExchange(ref _warningRaised, 1, 0) == 0;

    /// <summary>
    /// Atomically sets the critical flag if not already set.
    /// </summary>
    /// <returns>True if this call set the flag, false if already set.</returns>
    public bool TrySetCriticalRaised() =>
        Interlocked.CompareExchange(ref _criticalRaised, 1, 0) == 0;
}

/// <summary>
/// Result of a batch tile download operation for a trip.
/// </summary>
/// <param name="TotalBytes">Total bytes downloaded (including any previously downloaded).</param>
/// <param name="TilesDownloaded">Number of tiles successfully downloaded in this session.</param>
/// <param name="WasPaused">Whether the download was paused (user, network, storage).</param>
/// <param name="WasLimitReached">Whether the download was stopped due to cache limit.</param>
internal record BatchDownloadResult(
    long TotalBytes,
    int TilesDownloaded,
    bool WasPaused,
    bool WasLimitReached);

/// <summary>
/// Thread-safe helper for tracking stop reasons during parallel tile downloads.
/// </summary>
internal class ParallelDownloadStopReason
{
    private int _shouldStop;
    private int _wasPaused;
    private int _wasLimitReached;
    private object? _pauseReason;

    /// <summary>
    /// Whether the download should stop.
    /// </summary>
    public bool ShouldStop => Volatile.Read(ref _shouldStop) == 1;

    /// <summary>
    /// Whether stopped due to pause (user, network, storage).
    /// </summary>
    public bool WasPaused => Volatile.Read(ref _wasPaused) == 1;

    /// <summary>
    /// Whether stopped due to cache limit reached.
    /// </summary>
    public bool WasLimitReached => Volatile.Read(ref _wasLimitReached) == 1;

    /// <summary>
    /// Gets the pause reason if paused. Thread-safe read.
    /// </summary>
    public string PauseReason => Volatile.Read(ref _pauseReason) as string ?? string.Empty;

    /// <summary>
    /// Sets the stop reason to paused with the given reason.
    /// Only the first call takes effect. Thread-safe.
    /// </summary>
    public void SetPaused(string reason)
    {
        if (Interlocked.CompareExchange(ref _shouldStop, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _pauseReason, reason);
            Interlocked.Exchange(ref _wasPaused, 1);
        }
    }

    /// <summary>
    /// Sets the stop reason to cache limit reached.
    /// Only the first call takes effect. Thread-safe.
    /// </summary>
    public void SetLimitReached()
    {
        if (Interlocked.CompareExchange(ref _shouldStop, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _pauseReason, DownloadPauseReason.CacheLimitReached);
            Interlocked.Exchange(ref _wasLimitReached, 1);
        }
    }
}
