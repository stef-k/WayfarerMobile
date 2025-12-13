using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Service interface for managing activity types with server sync and local caching.
/// </summary>
public interface IActivitySyncService
{
    /// <summary>
    /// Gets all available activity types.
    /// Returns server activities if available, otherwise returns defaults.
    /// </summary>
    /// <returns>List of activity types.</returns>
    Task<List<ActivityType>> GetActivityTypesAsync();

    /// <summary>
    /// Gets an activity by its ID.
    /// </summary>
    /// <param name="id">The activity ID.</param>
    /// <returns>The activity type or null if not found.</returns>
    Task<ActivityType?> GetActivityByIdAsync(int id);

    /// <summary>
    /// Syncs activities from the server.
    /// </summary>
    /// <returns>True if sync was successful.</returns>
    Task<bool> SyncWithServerAsync();

    /// <summary>
    /// Automatically syncs if needed (e.g., data is stale).
    /// </summary>
    /// <returns>True if sync was successful or not needed.</returns>
    Task<bool> AutoSyncIfNeededAsync();
}
