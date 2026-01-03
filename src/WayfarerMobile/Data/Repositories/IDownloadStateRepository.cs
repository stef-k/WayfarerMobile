using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for trip download state operations.
/// Manages pause/resume state for trip downloads.
/// </summary>
public interface IDownloadStateRepository
{
    /// <summary>
    /// Gets a download state for a trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>The download state or null if not found.</returns>
    Task<TripDownloadStateEntity?> GetDownloadStateAsync(int tripId);

    /// <summary>
    /// Gets a download state by server trip ID.
    /// </summary>
    /// <param name="tripServerId">The server-side trip ID.</param>
    /// <returns>The download state or null if not found.</returns>
    Task<TripDownloadStateEntity?> GetDownloadStateByServerIdAsync(Guid tripServerId);

    /// <summary>
    /// Saves a download state (insert or replace).
    /// </summary>
    /// <param name="state">The download state to save.</param>
    Task SaveDownloadStateAsync(TripDownloadStateEntity state);

    /// <summary>
    /// Deletes a download state for a trip.
    /// Called when download completes or is cancelled with cleanup.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeleteDownloadStateAsync(int tripId);

    /// <summary>
    /// Gets all paused download states.
    /// Used to show resumable downloads in UI.
    /// </summary>
    /// <returns>List of paused download states.</returns>
    Task<List<TripDownloadStateEntity>> GetPausedDownloadsAsync();

    /// <summary>
    /// Gets all active download states (in progress or paused).
    /// </summary>
    /// <returns>List of active download states.</returns>
    Task<List<TripDownloadStateEntity>> GetActiveDownloadStatesAsync();
}
