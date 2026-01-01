using WayfarerMobile.Core.Models;

namespace WayfarerMobile.ViewModels;

/// <summary>
/// Callback interface for TripDownloadViewModel to communicate with TripsViewModel.
/// Enables the download ViewModel to update the trip list without direct coupling.
/// </summary>
public interface ITripDownloadCallbacks
{
    /// <summary>
    /// Refreshes the trips list from the server and local database.
    /// Called after download completes or fails to update status.
    /// </summary>
    Task RefreshTripsAsync();

    /// <summary>
    /// Moves a trip item to the correct group based on its current GroupName.
    /// Called after download state changes to regroup the item.
    /// </summary>
    /// <param name="item">The trip item to move.</param>
    void MoveItemToCorrectGroup(TripListItem item);

    /// <summary>
    /// Finds a trip item by its server ID.
    /// </summary>
    /// <param name="serverId">The server-assigned trip ID.</param>
    /// <returns>The trip item if found; otherwise, null.</returns>
    TripListItem? FindItemByServerId(Guid serverId);

    /// <summary>
    /// Updates the download progress on a specific trip item.
    /// </summary>
    /// <param name="serverId">The server ID of the trip being downloaded.</param>
    /// <param name="progress">The download progress (0.0-1.0).</param>
    /// <param name="isDownloading">Whether the download is active.</param>
    void UpdateItemProgress(Guid serverId, double progress, bool isDownloading);

    /// <summary>
    /// Gets the collection of trip groupings for iteration.
    /// </summary>
    IReadOnlyList<TripGrouping> TripGroups { get; }

    /// <summary>
    /// Checks for paused downloads from previous sessions.
    /// Called after download operations to update paused count.
    /// </summary>
    Task CheckForPausedDownloadsAsync();
}
