using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using BatchDownloadResult = WayfarerMobile.Core.Interfaces.BatchDownloadResult;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;
using WayfarerMobile.Services.TileCache;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for downloading and managing offline trips.
/// Orchestrates trip lifecycle (download, sync, delete) while delegating
/// tile download mechanics to ITileDownloadOrchestrator.
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
    private readonly ITripMetadataBuilder _metadataBuilder;
    private readonly ITripContentService _contentService;
    private readonly ITileDownloadOrchestrator _tileDownloadOrchestrator;
    private readonly ILogger<TripDownloadService> _logger;

    #region Constants

    // Tile download configuration - use centralized constants for consistency
    private const long EstimatedTileSizeBytes = TileCacheConstants.EstimatedTileSizeBytes;
    private const int TempFileMaxAgeHours = 1; // Max age for orphaned temp files

    // Absolute maximum tile count to prevent memory exhaustion (regardless of cache size)
    private const int AbsoluteMaxTileCount = 150000;

    #endregion

    #region Fields

    // Active download guard - prevents concurrent downloads of the same trip
    // Keyed by server trip ID (Guid) - available at start of download before local ID exists
    private readonly ConcurrentDictionary<Guid, bool> _activeDownloads = new();

    // Disposal tracking
    private bool _disposed;

    #endregion

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
        ITripMetadataBuilder metadataBuilder,
        ITripContentService contentService,
        ITileDownloadOrchestrator tileDownloadOrchestrator,
        ILogger<TripDownloadService> logger)
    {
        _apiClient = apiClient;
        _databaseService = databaseService;
        _settingsService = settingsService;
        _tripSyncService = tripSyncService;
        _tileDownloadService = tileDownloadService;
        _downloadStateManager = downloadStateManager;
        _cacheLimitEnforcer = cacheLimitEnforcer;
        _metadataBuilder = metadataBuilder;
        _contentService = contentService;
        _tileDownloadOrchestrator = tileDownloadOrchestrator;
        _logger = logger;

        // Wire up events from the tile download orchestrator
        _tileDownloadOrchestrator.ProgressChanged += OnTileProgressChanged;
        _tileDownloadOrchestrator.CacheWarning += (s, e) => CacheWarning?.Invoke(this, e);
        _tileDownloadOrchestrator.CacheCritical += (s, e) => CacheCritical?.Invoke(this, e);
        _tileDownloadOrchestrator.CacheLimitReached += (s, e) => CacheLimitReached?.Invoke(this, e);
        _tileDownloadOrchestrator.DownloadPaused += (s, e) => DownloadPaused?.Invoke(this, e);
    }

    /// <summary>
    /// Handles progress events from tile orchestrator and converts to trip progress.
    /// </summary>
    private void OnTileProgressChanged(object? sender, TileDownloadProgressEventArgs e)
    {
        RaiseProgress(e.TripId, e.ProgressPercent, e.StatusMessage);
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

            // Convert and save areas/regions using metadata builder
            var areas = _metadataBuilder.BuildAreas(tripDetails);
            await _databaseService.SaveOfflineAreasAsync(tripEntity.Id, areas);
            tripEntity.RegionCount = areas.Count;

            RaiseProgress(tripEntity.Id, 30, "Saving places...");

            // Convert and save places with region info
            var places = _metadataBuilder.BuildPlaces(tripDetails);
            await _databaseService.SaveOfflinePlacesAsync(tripEntity.Id, places);
            tripEntity.PlaceCount = places.Count;

            RaiseProgress(tripEntity.Id, 40, "Saving segments...");

            // Convert and save segments with place names
            var segments = _metadataBuilder.BuildSegments(tripDetails);
            await _databaseService.SaveOfflineSegmentsAsync(tripEntity.Id, segments);
            tripEntity.SegmentCount = segments.Count;

            // Save polygon zones (TripArea) from each region
            var polygons = _metadataBuilder.BuildPolygons(tripDetails);
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

                var tileCoords = _tileDownloadOrchestrator.CalculateTilesForBoundingBox(boundingBox);
                _logger.LogInformation("Trip {TripName} requires {TileCount} tiles", tripSummary.Name, tileCoords.Count);

                if (tileCoords.Count > 0)
                {
                    // Initialize per-trip warning state
                    _tileDownloadOrchestrator.InitializeWarningState(tripEntity.Id);

                    // Use orchestrator for tile downloads with pause/resume and cache limits
                    var downloadResult = await _tileDownloadOrchestrator.DownloadTilesAsync(
                        tripEntity.Id,
                        tripEntity.ServerId,
                        tripEntity.Name,
                        tileCoords,
                        initialCompleted: 0,
                        totalTiles: tileCoords.Count,
                        initialBytes: 0,
                        cancellationToken);

                    // Clean up warning state
                    _tileDownloadOrchestrator.ClearWarningState(tripEntity.Id);

                    // Track actual tiles downloaded vs requested for accurate reporting
                    tripEntity.TileCount = downloadResult.TilesDownloaded;
                    tripEntity.TotalSizeBytes = downloadResult.TotalBytes;

                    // Check if download was paused or hit cache limit
                    if (downloadResult.WasPaused || downloadResult.WasLimitReached)
                    {
                        _logger.LogInformation("Download stopped for trip {TripName}: Paused={Paused}, LimitReached={LimitReached}",
                            tripSummary.Name, downloadResult.WasPaused, downloadResult.WasLimitReached);
                        return tripEntity; // State already saved by orchestrator
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
                _tileDownloadOrchestrator.ClearWarningState(localTripId.Value);
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
    public Task<List<TripPlace>> GetOfflinePlacesAsync(Guid tripServerId)
        => _contentService.GetOfflinePlacesAsync(tripServerId);

    /// <summary>
    /// Gets complete offline trip details for navigation.
    /// Returns a TripDetails object populated from offline storage.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>Complete trip details or null if not downloaded.</returns>
    public Task<TripDetails?> GetOfflineTripDetailsAsync(Guid tripServerId)
        => _contentService.GetOfflineTripDetailsAsync(tripServerId);

    /// <summary>
    /// Gets offline segments for a downloaded trip.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>List of trip segments.</returns>
    public Task<List<TripSegment>> GetOfflineSegmentsAsync(Guid tripServerId)
        => _contentService.GetOfflineSegmentsAsync(tripServerId);

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
            _tileDownloadOrchestrator.InitializeWarningState(tripId);

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

            // Resume tile download using orchestrator
            var downloadResult = await _tileDownloadOrchestrator.DownloadTilesAsync(
                trip.Id,
                trip.ServerId,
                trip.Name,
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
            _tileDownloadOrchestrator.ClearWarningState(tripId);
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
            _tileDownloadOrchestrator.ClearWarningState(tripId);
        }
        else
        {
            // Keep partial download but mark as cancelled (distinct from failed)
            trip.Status = TripDownloadStatus.Cancelled;
            trip.LastError = "Download cancelled by user";
            await _databaseService.SaveDownloadedTripAsync(trip);
            _logger.LogInformation("Cancelled trip {TripId}, keeping partial data", tripId);

            // Clean up warning state but keep stop request until download loop exits
            _tileDownloadOrchestrator.ClearWarningState(tripId);
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
    public Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId)
        => _contentService.CheckTripUpdateNeededAsync(tripServerId);

    /// <summary>
    /// Syncs a downloaded trip with the server (updates places, segments, areas and re-downloads tiles if bounding box changed).
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
            // Delegate metadata sync to content service
            var progress = new Progress<DownloadProgressEventArgs>(args =>
            {
                // Scale content service progress (0-100) to our range (5-75)
                var scaledPercent = 5 + (int)(args.ProgressPercent * 0.7);
                RaiseProgress(args.TripId, scaledPercent, args.StatusMessage ?? "Syncing...");
            });

            var (syncedTrip, boundingBoxChanged) = await _contentService.SyncTripMetadataAsync(
                tripServerId, forceSync, progress, cancellationToken);

            if (syncedTrip == null)
            {
                return null;
            }

            // If bounding box changed, re-download tiles
            if (boundingBoxChanged)
            {
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
                    _logger.LogInformation("Bounding box changed for trip {TripId}, re-downloading tiles", tripServerId);
                    RaiseProgress(syncedTrip.Id, 80, "Downloading new map tiles...");

                    // Re-download tiles for new bounding box using unified download path
                    var boundingBox = new BoundingBox
                    {
                        North = syncedTrip.BoundingBoxNorth,
                        South = syncedTrip.BoundingBoxSouth,
                        East = syncedTrip.BoundingBoxEast,
                        West = syncedTrip.BoundingBoxWest
                    };

                    var tileCoords = _tileDownloadOrchestrator.CalculateTilesForBoundingBox(boundingBox);
                    if (tileCoords.Count > 0)
                    {
                        // Initialize per-trip warning state for sync download
                        _tileDownloadOrchestrator.InitializeWarningState(syncedTrip.Id);

                        var downloadResult = await _tileDownloadOrchestrator.DownloadTilesAsync(
                            syncedTrip.Id,
                            syncedTrip.ServerId,
                            syncedTrip.Name,
                            tileCoords,
                            initialCompleted: 0,
                            totalTiles: tileCoords.Count,
                            initialBytes: 0,
                            cancellationToken);

                        syncedTrip.TileCount = downloadResult.TilesDownloaded;
                        syncedTrip.TotalSizeBytes = downloadResult.TotalBytes;

                        // Clean up warning state
                        _tileDownloadOrchestrator.ClearWarningState(syncedTrip.Id);

                        // Save updated tile counts
                        await _databaseService.SaveDownloadedTripAsync(syncedTrip);

                        // If paused or limit reached, log but don't fail
                        if (downloadResult.WasPaused || downloadResult.WasLimitReached)
                        {
                            _logger.LogWarning("Sync tile download stopped for trip {TripId}: Paused={Paused}, LimitReached={LimitReached}",
                                tripServerId, downloadResult.WasPaused, downloadResult.WasLimitReached);
                        }
                    }
                }
                finally
                {
                    _activeDownloads.TryRemove(tripServerId, out _);
                }
            }

            RaiseProgress(syncedTrip.Id, 100, "Sync complete");
            return syncedTrip;
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
    public Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync()
        => _contentService.GetTripsNeedingUpdateAsync();

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
        return _tileDownloadOrchestrator.GetCachedTilePath(tripId, zoom, x, y);
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
        return _tileDownloadService.CleanupOrphanedTempFilesAsync(TempFileMaxAgeHours)
            .GetAwaiter().GetResult();
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

    #endregion
}
