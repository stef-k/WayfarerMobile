using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Coordinates synchronization of downloaded trips with the server.
/// Handles checking for updates, syncing metadata, and re-downloading tiles when needed.
/// </summary>
public class TripSyncCoordinator : ITripSyncCoordinator
{
    private readonly ITripContentService _contentService;
    private readonly ITileDownloadOrchestrator _tileDownloadOrchestrator;
    private readonly ITripRepository _tripRepository;
    private readonly ITileDownloadService _tileDownloadService;
    private readonly ILogger<TripSyncCoordinator> _logger;

    /// <summary>
    /// Active sync guard - prevents concurrent syncs of the same trip.
    /// Keyed by server trip ID (Guid).
    /// </summary>
    private readonly ConcurrentDictionary<Guid, bool> _activeSyncs = new();

    /// <summary>
    /// Event raised when sync progress changes.
    /// </summary>
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="TripSyncCoordinator"/> class.
    /// </summary>
    public TripSyncCoordinator(
        ITripContentService contentService,
        ITileDownloadOrchestrator tileDownloadOrchestrator,
        ITripRepository tripRepository,
        ITileDownloadService tileDownloadService,
        ILogger<TripSyncCoordinator> logger)
    {
        _contentService = contentService;
        _tileDownloadOrchestrator = tileDownloadOrchestrator;
        _tripRepository = tripRepository;
        _tileDownloadService = tileDownloadService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId)
        => _contentService.CheckTripUpdateNeededAsync(tripServerId);

    /// <inheritdoc/>
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
                if (!_activeSyncs.TryAdd(tripServerId, true))
                {
                    // Another sync in progress - metadata is synced but tiles are not
                    // Return null to signal sync didn't fully complete; caller can retry later
                    _logger.LogWarning("Tile sync already in progress for trip {TripId}, sync incomplete", tripServerId);
                    return null;
                }

                try
                {
                    _logger.LogInformation("Bounding box changed for trip {TripId}, re-downloading tiles", tripServerId);
                    RaiseProgress(syncedTrip.Id, 80, "Downloading new map tiles...");

                    // Re-download tiles for new bounding box
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
                        await _tripRepository.SaveDownloadedTripAsync(syncedTrip);

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
                    _activeSyncs.TryRemove(tripServerId, out _);
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

    /// <inheritdoc/>
    public Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync()
        => _contentService.GetTripsNeedingUpdateAsync();

    /// <inheritdoc/>
    public async Task<int> SyncAllTripsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tileDownloadService.IsNetworkAvailable())
        {
            _logger.LogWarning("Cannot sync trips - no network connection");
            return 0;
        }

        var downloadedTrips = await _tripRepository.GetDownloadedTripsAsync();
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
    /// Raises the ProgressChanged event.
    /// </summary>
    private void RaiseProgress(int tripId, int percent, string message)
    {
        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
        {
            TripId = tripId,
            ProgressPercent = percent,
            StatusMessage = message
        });
    }
}
