using WayfarerMobile.Core.Enums;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing download state transitions with validation and events.
/// This is the single point of truth for all download state changes.
/// </summary>
public class DownloadStateService : IDownloadStateService
{
    private readonly ITripRepository _tripRepository;
    private readonly object _eventLock = new();

    /// <inheritdoc />
    public event EventHandler<DownloadStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="DownloadStateService"/> class.
    /// </summary>
    /// <param name="tripRepository">The trip repository.</param>
    public DownloadStateService(ITripRepository tripRepository)
    {
        _tripRepository = tripRepository;
    }

    /// <inheritdoc />
    public async Task<UnifiedDownloadState?> GetStateAsync(Guid tripServerId)
    {
        var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripServerId);
        return trip?.UnifiedState;
    }

    /// <inheritdoc />
    public async Task<bool> TransitionAsync(Guid tripServerId, UnifiedDownloadState newState, string? reason = null)
    {
        var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
        {
            Console.WriteLine($"[DownloadStateService] Trip not found: {tripServerId}");
            return false;
        }

        var currentState = trip.UnifiedState;

        // Validate transition
        if (!IsValidTransition(currentState, newState))
        {
            Console.WriteLine($"[DownloadStateService] Invalid transition: {currentState} -> {newState} for trip {tripServerId}");
            return false;
        }

        // Skip if already in target state
        if (currentState == newState)
        {
            Console.WriteLine($"[DownloadStateService] Already in state {newState} for trip {tripServerId}");
            return true;
        }

        // Update the entity
        trip.UnifiedState = newState;
        trip.StateChangedAt = DateTime.UtcNow;
        trip.UpdatedAt = DateTime.UtcNow;

        // Set reason for paused/failed states
        if (newState.IsPaused() || newState == UnifiedDownloadState.Failed)
        {
            trip.PauseReason = reason;
            if (newState == UnifiedDownloadState.Failed)
            {
                trip.LastError = reason;
            }
        }
        else
        {
            trip.PauseReason = null;
        }

        // Update legacy status for backward compatibility
        UpdateLegacyStatus(trip, newState);

        await _tripRepository.SaveDownloadedTripAsync(trip);

        Console.WriteLine($"[DownloadStateService] Transitioned trip {tripServerId}: {currentState} -> {newState}");

        // Raise event
        RaiseStateChanged(trip, currentState, newState, reason);

        return true;
    }

    /// <inheritdoc />
    public bool IsValidTransition(UnifiedDownloadState currentState, UnifiedDownloadState newState)
    {
        // Same state is always valid (no-op)
        if (currentState == newState)
            return true;

        return currentState switch
        {
            UnifiedDownloadState.ServerOnly => newState == UnifiedDownloadState.DownloadingMetadata,

            UnifiedDownloadState.DownloadingMetadata => newState is
                UnifiedDownloadState.DownloadingTiles or
                UnifiedDownloadState.MetadataOnly or
                UnifiedDownloadState.PausedByUser or
                UnifiedDownloadState.PausedNetworkLost or
                UnifiedDownloadState.Failed or
                UnifiedDownloadState.Cancelled,

            UnifiedDownloadState.DownloadingTiles => newState is
                UnifiedDownloadState.Complete or
                UnifiedDownloadState.MetadataOnly or
                UnifiedDownloadState.PausedByUser or
                UnifiedDownloadState.PausedNetworkLost or
                UnifiedDownloadState.PausedStorageLow or
                UnifiedDownloadState.PausedCacheLimit or
                UnifiedDownloadState.Failed or
                UnifiedDownloadState.Cancelled,

            // Paused states can resume, cancel, or be deleted (delete handled separately)
            UnifiedDownloadState.PausedByUser or
            UnifiedDownloadState.PausedNetworkLost or
            UnifiedDownloadState.PausedStorageLow or
            UnifiedDownloadState.PausedCacheLimit => newState is
                UnifiedDownloadState.DownloadingTiles or
                UnifiedDownloadState.DownloadingMetadata or
                UnifiedDownloadState.Cancelled,

            // Failed can retry or be deleted
            UnifiedDownloadState.Failed => newState is
                UnifiedDownloadState.DownloadingMetadata or
                UnifiedDownloadState.DownloadingTiles or
                UnifiedDownloadState.Cancelled,

            // Cancelled is terminal (record should be deleted)
            UnifiedDownloadState.Cancelled => false,

            // MetadataOnly can add tiles or be deleted
            UnifiedDownloadState.MetadataOnly => newState == UnifiedDownloadState.DownloadingTiles,

            // Complete can remove tiles (-> MetadataOnly) or be deleted
            UnifiedDownloadState.Complete => newState == UnifiedDownloadState.MetadataOnly,

            _ => false
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetResumableTripsAsync()
    {
        var trips = await _tripRepository.GetDownloadedTripsAsync();
        return trips
            .Where(t => t.UnifiedState.CanResume())
            .Select(t => t.ServerId)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetActiveDownloadsAsync()
    {
        var trips = await _tripRepository.GetDownloadedTripsAsync();
        return trips
            .Where(t => t.UnifiedState.IsDownloading())
            .Select(t => t.ServerId)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<int> RecoverStuckDownloadsAsync()
    {
        var trips = await _tripRepository.GetDownloadedTripsAsync();
        var stuckTrips = trips.Where(t => t.UnifiedState.IsDownloading()).ToList();

        if (stuckTrips.Count == 0)
            return 0;

        Console.WriteLine($"[DownloadStateService] Recovering {stuckTrips.Count} stuck downloads...");

        foreach (var trip in stuckTrips)
        {
            var newState = trip.IsMetadataComplete
                ? UnifiedDownloadState.PausedByUser
                : UnifiedDownloadState.Failed;

            var previousState = trip.UnifiedState;
            trip.UnifiedState = newState;
            trip.StateChangedAt = DateTime.UtcNow;
            trip.PauseReason = "App was closed during download";
            trip.UpdatedAt = DateTime.UtcNow;

            UpdateLegacyStatus(trip, newState);
            await _tripRepository.SaveDownloadedTripAsync(trip);

            Console.WriteLine($"[DownloadStateService] Recovered trip {trip.ServerId}: {previousState} -> {newState}");

            RaiseStateChanged(trip, previousState, newState, "App was closed during download");
        }

        return stuckTrips.Count;
    }

    /// <inheritdoc />
    public async Task UpdateTileProgressAsync(Guid tripServerId, int tilesCompleted, int tilesTotal)
    {
        var trip = await _tripRepository.GetDownloadedTripByServerIdAsync(tripServerId);
        if (trip == null)
            return;

        trip.TilesCompleted = tilesCompleted;
        trip.TilesTotal = tilesTotal;
        trip.TileCount = tilesCompleted;
        trip.ProgressPercent = tilesTotal > 0 ? (int)(tilesCompleted * 100.0 / tilesTotal) : 0;
        trip.UpdatedAt = DateTime.UtcNow;

        await _tripRepository.SaveDownloadedTripAsync(trip);
    }

    /// <summary>
    /// Updates the legacy Status field for backward compatibility.
    /// </summary>
    private static void UpdateLegacyStatus(DownloadedTripEntity trip, UnifiedDownloadState newState)
    {
#pragma warning disable CS0618 // Obsolete - intentionally updating for compatibility
        trip.Status = newState switch
        {
            UnifiedDownloadState.ServerOnly => TripDownloadStatus.Pending,
            UnifiedDownloadState.DownloadingMetadata => TripDownloadStatus.Downloading,
            UnifiedDownloadState.DownloadingTiles => TripDownloadStatus.Downloading,
            UnifiedDownloadState.PausedByUser => TripDownloadStatus.Downloading,
            UnifiedDownloadState.PausedNetworkLost => TripDownloadStatus.Downloading,
            UnifiedDownloadState.PausedStorageLow => TripDownloadStatus.Downloading,
            UnifiedDownloadState.PausedCacheLimit => TripDownloadStatus.Downloading,
            UnifiedDownloadState.Failed => TripDownloadStatus.Failed,
            UnifiedDownloadState.Cancelled => TripDownloadStatus.Cancelled,
            UnifiedDownloadState.MetadataOnly => TripDownloadStatus.MetadataOnly,
            UnifiedDownloadState.Complete => TripDownloadStatus.Complete,
            _ => TripDownloadStatus.Pending
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Raises the StateChanged event.
    /// </summary>
    private void RaiseStateChanged(
        DownloadedTripEntity trip,
        UnifiedDownloadState previousState,
        UnifiedDownloadState newState,
        string? reason)
    {
        var args = new DownloadStateChangedEventArgs
        {
            TripServerId = trip.ServerId,
            PreviousState = previousState,
            NewState = newState,
            IsMetadataComplete = trip.IsMetadataComplete,
            HasTiles = trip.HasTiles,
            Reason = reason
        };

        lock (_eventLock)
        {
            StateChanged?.Invoke(this, args);
        }
    }
}
