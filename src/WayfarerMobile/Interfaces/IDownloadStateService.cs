using WayfarerMobile.Core.Enums;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Event arguments for download state changes.
/// </summary>
public class DownloadStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the trip server ID.
    /// </summary>
    public Guid TripServerId { get; init; }

    /// <summary>
    /// Gets the previous state.
    /// </summary>
    public UnifiedDownloadState PreviousState { get; init; }

    /// <summary>
    /// Gets the new state.
    /// </summary>
    public UnifiedDownloadState NewState { get; init; }

    /// <summary>
    /// Gets whether metadata is complete for this trip.
    /// </summary>
    public bool IsMetadataComplete { get; init; }

    /// <summary>
    /// Gets whether this trip has tiles.
    /// </summary>
    public bool HasTiles { get; init; }

    /// <summary>
    /// Gets optional additional context (error message, pause reason, etc.).
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Service for managing download state transitions with validation and events.
/// This is the single point of truth for all download state changes.
/// </summary>
public interface IDownloadStateService
{
    /// <summary>
    /// Event raised when a download state changes.
    /// </summary>
    event EventHandler<DownloadStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the current unified state for a trip.
    /// </summary>
    /// <param name="tripServerId">The trip server ID.</param>
    /// <returns>The current state, or null if trip not found.</returns>
    Task<UnifiedDownloadState?> GetStateAsync(Guid tripServerId);

    /// <summary>
    /// Attempts to transition a trip to a new state.
    /// Validates the transition and raises StateChanged if successful.
    /// </summary>
    /// <param name="tripServerId">The trip server ID.</param>
    /// <param name="newState">The target state.</param>
    /// <param name="reason">Optional reason for the transition (error message, pause reason, etc.).</param>
    /// <returns>True if transition succeeded, false if invalid or trip not found.</returns>
    Task<bool> TransitionAsync(Guid tripServerId, UnifiedDownloadState newState, string? reason = null);

    /// <summary>
    /// Checks if a state transition is valid.
    /// </summary>
    /// <param name="currentState">The current state.</param>
    /// <param name="newState">The target state.</param>
    /// <returns>True if the transition is allowed.</returns>
    bool IsValidTransition(UnifiedDownloadState currentState, UnifiedDownloadState newState);

    /// <summary>
    /// Gets all trips that can be resumed (paused or failed states).
    /// </summary>
    /// <returns>List of trip server IDs that can be resumed.</returns>
    Task<IReadOnlyList<Guid>> GetResumableTripsAsync();

    /// <summary>
    /// Gets all trips currently downloading.
    /// </summary>
    /// <returns>List of trip server IDs that are actively downloading.</returns>
    Task<IReadOnlyList<Guid>> GetActiveDownloadsAsync();

    /// <summary>
    /// Recovers stuck downloads on app startup.
    /// Transitions any "downloading" state trips to appropriate paused state.
    /// </summary>
    /// <returns>Number of trips recovered.</returns>
    Task<int> RecoverStuckDownloadsAsync();

    /// <summary>
    /// Updates tile progress for a downloading trip.
    /// </summary>
    /// <param name="tripServerId">The trip server ID.</param>
    /// <param name="tilesCompleted">Number of tiles downloaded.</param>
    /// <param name="tilesTotal">Total tiles expected.</param>
    Task UpdateTileProgressAsync(Guid tripServerId, int tilesCompleted, int tilesTotal);
}
