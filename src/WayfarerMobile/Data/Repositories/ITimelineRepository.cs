using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for local timeline operations.
/// Manages local timeline entries for GPS location history display.
/// </summary>
public interface ITimelineRepository
{
    #region CRUD Operations

    /// <summary>
    /// Inserts a new local timeline entry.
    /// </summary>
    /// <param name="entry">The entry to insert.</param>
    /// <returns>The inserted entry's ID.</returns>
    Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry);

    /// <summary>
    /// Updates an existing local timeline entry.
    /// </summary>
    /// <param name="entry">The entry to update.</param>
    Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry);

    /// <summary>
    /// Deletes a local timeline entry by ID.
    /// </summary>
    /// <param name="id">The local ID.</param>
    Task DeleteLocalTimelineEntryAsync(int id);

    /// <summary>
    /// Deletes a local timeline entry by timestamp.
    /// Uses a tolerance window to handle minor timestamp differences.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>Number of entries deleted.</returns>
    Task<int> DeleteLocalTimelineEntryByTimestampAsync(DateTime timestamp, int toleranceSeconds = 2);

    #endregion

    #region Query Operations

    /// <summary>
    /// Gets a local timeline entry by ID.
    /// </summary>
    /// <param name="id">The local ID.</param>
    /// <returns>The entry or null if not found.</returns>
    Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id);

    /// <summary>
    /// Gets a local timeline entry by server ID.
    /// </summary>
    /// <param name="serverId">The server ID.</param>
    /// <returns>The entry or null if not found.</returns>
    Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId);

    /// <summary>
    /// Gets a local timeline entry by timestamp.
    /// Uses a tolerance window to handle minor timestamp differences.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>The entry or null if not found.</returns>
    Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(DateTime timestamp, int toleranceSeconds = 2);

    /// <summary>
    /// Gets the most recent local timeline entry.
    /// Used by LocalTimelineFilter to initialize last stored location.
    /// </summary>
    /// <returns>The most recent entry or null if none exist.</returns>
    Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync();

    #endregion

    #region Range Queries

    /// <summary>
    /// Gets all local timeline entries for a specific date.
    /// </summary>
    /// <param name="date">The date to retrieve entries for.</param>
    /// <returns>List of entries for that date, ordered by timestamp descending.</returns>
    Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date);

    /// <summary>
    /// Gets all local timeline entries within a date range.
    /// </summary>
    /// <param name="fromDate">Start date (inclusive).</param>
    /// <param name="toDate">End date (inclusive).</param>
    /// <returns>List of entries in the range, ordered by timestamp descending.</returns>
    Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(DateTime fromDate, DateTime toDate);

    /// <summary>
    /// Gets all local timeline entries for export.
    /// </summary>
    /// <returns>All entries ordered by timestamp descending.</returns>
    Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync();

    #endregion

    #region Bulk Operations

    /// <summary>
    /// Bulk inserts local timeline entries.
    /// Used for import operations.
    /// </summary>
    /// <param name="entries">The entries to insert.</param>
    /// <returns>Number of entries inserted.</returns>
    Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> entries);

    /// <summary>
    /// Clears all local timeline entries.
    /// Use with caution - this deletes all local timeline history.
    /// </summary>
    /// <returns>Number of entries deleted.</returns>
    Task<int> ClearAllLocalTimelineEntriesAsync();

    #endregion

    #region Sync Operations

    /// <summary>
    /// Updates the ServerId for a local timeline entry matched by timestamp.
    /// Used when sync confirms a location was stored on server.
    /// </summary>
    /// <param name="timestamp">The timestamp to match (UTC).</param>
    /// <param name="serverId">The server-assigned ID.</param>
    /// <param name="toleranceSeconds">Tolerance window in seconds (default 2).</param>
    /// <returns>True if an entry was updated.</returns>
    Task<bool> UpdateLocalTimelineServerIdAsync(DateTime timestamp, int serverId, int toleranceSeconds = 2);

    /// <summary>
    /// Gets the total count of local timeline entries.
    /// </summary>
    /// <returns>The count of entries.</returns>
    Task<int> GetLocalTimelineEntryCountAsync();

    #endregion
}
