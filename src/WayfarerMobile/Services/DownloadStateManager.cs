using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Repositories;

namespace WayfarerMobile.Services;

/// <summary>
/// Manages download pause/resume state, progress tracking, and stop requests.
/// Provides persistence for download checkpoints and handles interruption coordination.
/// </summary>
public sealed class DownloadStateManager : IDownloadStateManager
{
    private readonly IDownloadStateRepository _downloadStateRepository;
    private readonly ILogger<DownloadStateManager> _logger;

    // Thread-safe stop request tracking
    private readonly ConcurrentDictionary<int, string> _stopRequests = new();

    /// <summary>
    /// Creates a new instance of DownloadStateManager.
    /// </summary>
    /// <param name="downloadStateRepository">Repository for download state operations.</param>
    /// <param name="logger">Logger instance.</param>
    public DownloadStateManager(
        IDownloadStateRepository downloadStateRepository,
        ILogger<DownloadStateManager> logger)
    {
        _downloadStateRepository = downloadStateRepository;
        _logger = logger;
    }

    #region Stop Request Management

    /// <inheritdoc/>
    public void RequestStop(int tripId, string reason)
    {
        _stopRequests[tripId] = reason;
        _logger.LogDebug("Stop requested for trip {TripId}: {Reason}", tripId, reason);
    }

    /// <inheritdoc/>
    public bool IsStopRequested(int tripId)
    {
        return _stopRequests.ContainsKey(tripId);
    }

    /// <inheritdoc/>
    public bool TryGetStopReason(int tripId, out string reason)
    {
        return _stopRequests.TryGetValue(tripId, out reason!);
    }

    /// <inheritdoc/>
    public void ClearStopRequest(int tripId)
    {
        if (_stopRequests.TryRemove(tripId, out var reason))
        {
            _logger.LogDebug("Cleared stop request for trip {TripId} (was: {Reason})", tripId, reason);
        }
    }

    #endregion

    #region State Persistence

    /// <inheritdoc/>
    public async Task SaveStateAsync(DownloadState state)
    {
        var entity = new TripDownloadStateEntity
        {
            TripId = state.TripId,
            TripServerId = state.TripServerId,
            TripName = state.TripName,
            RemainingTilesJson = JsonSerializer.Serialize(state.RemainingTiles),
            CompletedTileCount = state.CompletedTileCount,
            TotalTileCount = state.TotalTileCount,
            DownloadedBytes = state.DownloadedBytes,
            Status = MapStatusToString(state.Status),
            InterruptionReason = state.InterruptionReason,
            PausedAt = state.UpdatedAt,
            LastSaveTime = DateTime.UtcNow
        };

        await _downloadStateRepository.SaveDownloadStateAsync(entity);

        _logger.LogInformation(
            "Saved download state for trip {TripId}: {Completed}/{Total} tiles, status: {Status}",
            state.TripId, state.CompletedTileCount, state.TotalTileCount, state.Status);
    }

    /// <inheritdoc/>
    public async Task<DownloadState?> GetStateAsync(int tripId)
    {
        var entity = await _downloadStateRepository.GetDownloadStateAsync(tripId);
        return entity != null ? MapToDownloadState(entity) : null;
    }

    /// <inheritdoc/>
    public async Task<DownloadState?> GetStateByServerIdAsync(Guid tripServerId)
    {
        var entity = await _downloadStateRepository.GetDownloadStateByServerIdAsync(tripServerId);
        return entity != null ? MapToDownloadState(entity) : null;
    }

    /// <inheritdoc/>
    public async Task DeleteStateAsync(int tripId)
    {
        await _downloadStateRepository.DeleteDownloadStateAsync(tripId);
        _logger.LogDebug("Deleted download state for trip {TripId}", tripId);
    }

    #endregion

    #region State Queries

    /// <inheritdoc/>
    public async Task<List<DownloadState>> GetPausedDownloadsAsync()
    {
        var entities = await _downloadStateRepository.GetPausedDownloadsAsync();
        return entities.Select(MapToDownloadState).ToList();
    }

    /// <inheritdoc/>
    public async Task<bool> IsPausedAsync(int tripId)
    {
        // Check in-memory stop request first
        if (TryGetStopReason(tripId, out var reason) &&
            reason == DownloadStopReason.UserPause)
        {
            return true;
        }

        // Check persisted state
        var state = await GetStateAsync(tripId);
        return state?.Status is DownloadStatus.Paused or DownloadStatus.LimitReached;
    }

    /// <inheritdoc/>
    public async Task<bool> HasStateAsync(int tripId)
    {
        var state = await _downloadStateRepository.GetDownloadStateAsync(tripId);
        return state != null;
    }

    #endregion

    #region Mapping Helpers

    /// <summary>
    /// Maps an entity to a DownloadState record.
    /// </summary>
    private static DownloadState MapToDownloadState(TripDownloadStateEntity entity)
    {
        var remainingTiles = new List<TileCoordinate>();
        if (!string.IsNullOrEmpty(entity.RemainingTilesJson))
        {
            try
            {
                remainingTiles = JsonSerializer.Deserialize<List<TileCoordinate>>(entity.RemainingTilesJson) ?? [];
            }
            catch
            {
                // If deserialization fails, use empty list
            }
        }

        return new DownloadState
        {
            TripId = entity.TripId,
            TripServerId = entity.TripServerId,
            TripName = entity.TripName,
            RemainingTiles = remainingTiles,
            CompletedTileCount = entity.CompletedTileCount,
            TotalTileCount = entity.TotalTileCount,
            DownloadedBytes = entity.DownloadedBytes,
            Status = MapStringToStatus(entity.Status),
            InterruptionReason = entity.InterruptionReason,
            UpdatedAt = entity.PausedAt
        };
    }

    /// <summary>
    /// Maps a DownloadStatus enum to persistence string.
    /// </summary>
    private static string MapStatusToString(DownloadStatus status) => status switch
    {
        DownloadStatus.Paused => DownloadStateStatus.Paused,
        DownloadStatus.InProgress => DownloadStateStatus.InProgress,
        DownloadStatus.Cancelled => DownloadStateStatus.Cancelled,
        DownloadStatus.LimitReached => DownloadStateStatus.LimitReached,
        _ => DownloadStateStatus.Paused
    };

    /// <summary>
    /// Maps a persistence string to DownloadStatus enum.
    /// </summary>
    private static DownloadStatus MapStringToStatus(string status) => status switch
    {
        DownloadStateStatus.Paused => DownloadStatus.Paused,
        DownloadStateStatus.InProgress => DownloadStatus.InProgress,
        DownloadStateStatus.Cancelled => DownloadStatus.Cancelled,
        DownloadStateStatus.LimitReached => DownloadStatus.LimitReached,
        _ => DownloadStatus.Paused
    };

    #endregion
}
