using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>Coordinates synchronization of provider-independent downloaded Trip content.</summary>
public sealed class TripSyncCoordinator : ITripSyncCoordinator
{
    private readonly ITripContentService _content;
    private readonly ITripRepository _trips;
    private readonly ILogger<TripSyncCoordinator> _logger;
    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    public TripSyncCoordinator(ITripContentService content, ITripRepository trips, ILogger<TripSyncCoordinator> logger)
    {
        _content = content;
        _trips = trips;
        _logger = logger;
    }

    public Task<bool> CheckTripUpdateNeededAsync(Guid tripServerId) => _content.CheckTripUpdateNeededAsync(tripServerId);

    public async Task<DownloadedTripEntity?> SyncTripAsync(Guid tripServerId, bool forceSync = false, CancellationToken cancellationToken = default)
    {
        var progress = new Progress<DownloadProgressEventArgs>(e => ProgressChanged?.Invoke(this, e));
        return await _content.SyncTripMetadataAsync(tripServerId, forceSync, progress, cancellationToken);
    }

    public Task<List<DownloadedTripEntity>> GetTripsNeedingUpdateAsync() => _content.GetTripsNeedingUpdateAsync();

    public async Task<int> SyncAllTripsAsync(CancellationToken cancellationToken = default)
    {
        var downloaded = (await _trips.GetDownloadedTripsAsync()).Where(t => t.UnifiedState == UnifiedDownloadState.Downloaded).ToList();
        var count = 0;
        foreach (var trip in downloaded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { if (await SyncTripAsync(trip.ServerId, cancellationToken: cancellationToken) is not null) count++; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to sync Trip {TripId}", trip.ServerId); }
        }
        return count;
    }
}
