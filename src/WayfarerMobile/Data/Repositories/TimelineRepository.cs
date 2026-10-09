using SQLite;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for local timeline operations.
/// Manages local timeline entries for GPS location history display.
/// </summary>
public class TimelineRepository : RepositoryBase, ITimelineRepository
{
    /// <summary>
    /// Creates a new instance of TimelineRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public TimelineRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    #region CRUD Operations

    /// <inheritdoc />
    public async Task<int> InsertLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        var db = await GetConnectionAsync();
        if (entry.QueuedLocationId is > 0)
        {
            // Backfill and a delayed platform callback may both insert the same originating capture.
            // Preserve any existing bindings, including ambiguous historical ones, without adopting their data.
            await db.RunInTransactionAsync(connection =>
            {
                if (connection.Table<LocalTimelineEntry>().Where(e => e.QueuedLocationId == entry.QueuedLocationId).Count() > 0)
                    return;
                var queued = connection.Find<QueuedLocation>(entry.QueuedLocationId.Value);
                if (queued?.IsRejected == true && !entry.IsSynced)
                    return;
                // Confirmation may arrive before the callback or startup backfill inserts this row.
                if (queued?.ServerConfirmed == true && queued.ServerId is > 0
                    && (entry.ServerId == null || entry.ServerId == queued.ServerId))
                {
                    entry.ServerId = queued.ServerId;
                    entry.ServerLinkageConfirmed = true;
                }
                connection.Insert(entry);
            });
            return entry.Id;
        }
        await db.InsertAsync(entry);
        return entry.Id;
    }

    /// <inheritdoc />
    public async Task UpdateLocalTimelineEntryAsync(LocalTimelineEntry entry)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(entry);
    }

    /// <inheritdoc />
    public async Task DeleteLocalTimelineEntryAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM LocalTimelineEntries WHERE Id = ?", id);
    }

    #endregion

    #region Query Operations

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByServerIdAsync(int serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .FirstOrDefaultAsync(e => e.ServerId == serverId && e.ServerLinkageConfirmed);
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetLocalTimelineEntryByTimestampAsync(
        DateTime timestamp,
        int toleranceSeconds = 2)
    {
        var db = await GetConnectionAsync();

        var minTime = timestamp.AddSeconds(-toleranceSeconds);
        var maxTime = timestamp.AddSeconds(toleranceSeconds);

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= minTime && e.Timestamp <= maxTime)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetMostRecentLocalTimelineEntryAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync();
    }

    #endregion

    #region Range Queries

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesForDateAsync(DateTime date)
    {
        var db = await GetConnectionAsync();

        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startOfDay && e.Timestamp < endOfDay)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> PrepareConfirmedTimelineEntriesForEnrichmentAsync(DateTime date)
    {
        var db = await GetConnectionAsync();
        var startOfDay = date.Date.ToUniversalTime();
        var endOfDay = date.Date.AddDays(1).ToUniversalTime();
        var primaryEntries = new List<LocalTimelineEntry>();
        await db.RunInTransactionAsync(connection =>
        {
            // Enrichment can precede sync-service initialization; use its existing additive table setup.
            connection.CreateTable<PendingTimelineMutation>();
            var referencedIds = connection.QueryScalars<int>("""
                SELECT DISTINCT LocalEntryId FROM PendingTimelineMutations WHERE LocalEntryId IS NOT NULL
                """).ToHashSet();
            var entries = connection.Table<LocalTimelineEntry>()
                .Where(e => e.Timestamp >= startOfDay && e.Timestamp < endOfDay
                    && e.ServerLinkageConfirmed && e.ServerId > 0).ToList();
            foreach (var group in entries.GroupBy(e => e.ServerId!.Value))
            {
                var ordered = group.OrderByDescending(e => referencedIds.Contains(e.Id))
                    .ThenByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).ToList();
                primaryEntries.Add(ordered[0]);
                foreach (var duplicate in ordered.Skip(1))
                {
                    if (!referencedIds.Contains(duplicate.Id))
                        connection.Delete<LocalTimelineEntry>(duplicate.Id);
                }
            }
            // The reference query and deletion share a transaction, so enqueue cannot invalidate the check.
        });
        return primaryEntries;
    }

    /// <inheritdoc />
    public async Task EnrichLocalTimelineEntryAsync(int id, TimelineLocation serverLocation)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(connection =>
        {
            connection.CreateTable<PendingTimelineMutation>();
            // Reload within the transaction rather than writing a stale pre-enqueue copy of the whole row.
            var entry = connection.Find<LocalTimelineEntry>(id);
            if (entry?.IsSynced != true || entry.ServerId != serverLocation.Id)
                return;
            var hasPendingUpdate = connection.Table<PendingTimelineMutation>()
                .Where(m => m.LocalEntryId == id && m.OperationType == "Update" && !m.IsRejected).Count() > 0;
            entry.Address = serverLocation.Address;
            entry.FullAddress = serverLocation.FullAddress;
            entry.Place = serverLocation.Place;
            entry.Region = serverLocation.Region;
            entry.Country = serverLocation.Country;
            entry.PostCode = serverLocation.PostCode;
            entry.TimeZoneId = serverLocation.Timezone;
            if (!hasPendingUpdate)
            {
                entry.ActivityType = serverLocation.ActivityType;
                if (string.IsNullOrEmpty(entry.Notes))
                    entry.Notes = serverLocation.Notes;
            }
            entry.LastEnrichedAt = DateTime.UtcNow;
            connection.Update(entry);
        });
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetLocalTimelineEntriesInRangeAsync(
        DateTime fromDate,
        DateTime toDate)
    {
        var db = await GetConnectionAsync();

        var startTime = fromDate.Date.ToUniversalTime();
        var endTime = toDate.Date.AddDays(1).ToUniversalTime();

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.Timestamp >= startTime && e.Timestamp < endTime)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetAllLocalTimelineEntriesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    #endregion

    #region Bulk Operations

    /// <inheritdoc />
    public async Task<int> BulkInsertLocalTimelineEntriesAsync(IEnumerable<LocalTimelineEntry> entries)
    {
        var db = await GetConnectionAsync();
        var entryList = entries.ToList();
        if (entryList.Count == 0)
            return 0;

        await db.InsertAllAsync(entryList);
        return entryList.Count;
    }

    /// <inheritdoc />
    public async Task<int> ClearAllLocalTimelineEntriesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.ExecuteAsync("DELETE FROM LocalTimelineEntries");
    }

    #endregion

    #region Sync Operations

    /// <inheritdoc />
    public async Task<int> GetLocalTimelineEntryCountAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>().CountAsync();
    }

    /// <inheritdoc />
    public async Task<List<LocalTimelineEntry>> GetEntriesMissingServerIdAsync(DateTime? sinceTimestamp = null)
    {
        var db = await GetConnectionAsync();

        if (sinceTimestamp.HasValue)
        {
            return await db.Table<LocalTimelineEntry>()
                .Where(e => e.ServerId == null && e.Timestamp >= sinceTimestamp.Value)
                .OrderBy(e => e.Timestamp)
                .ToListAsync();
        }

        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.ServerId == null)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<bool> UpdateServerIdByQueuedLocationIdAsync(int queuedLocationId, int serverId)
    {
        if (queuedLocationId <= 0 || serverId <= 0)
            return false;

        var db = await GetConnectionAsync();
        // Resolve exactly one originating row and confirm authority in the same atomic statement.
        var affected = await db.ExecuteAsync("""
            UPDATE LocalTimelineEntries SET ServerId = ?, ServerLinkageConfirmed = 1
            WHERE Id = (
                SELECT MIN(Id) FROM LocalTimelineEntries WHERE QueuedLocationId = ? HAVING COUNT(*) = 1
            ) AND (ServerId IS NULL OR ServerId = ?) AND COALESCE(ServerLinkageConfirmed, 0) = 0
            AND EXISTS (
                SELECT 1 FROM QueuedLocations WHERE Id = ? AND ServerConfirmed = 1 AND ServerId = ?
            )
            """, serverId, queuedLocationId, serverId, queuedLocationId, serverId);
        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<int> DeleteByQueuedLocationIdAsync(int queuedLocationId)
    {
        if (queuedLocationId <= 0)
            return 0;

        var db = await GetConnectionAsync();
        // A late skip must not remove confirmed history or choose among duplicate queue bindings.
        return await db.ExecuteAsync("""
            DELETE FROM LocalTimelineEntries
            WHERE Id = (
                SELECT MIN(Id) FROM LocalTimelineEntries WHERE QueuedLocationId = ? HAVING COUNT(*) = 1
            ) AND ServerId IS NULL
            AND NOT EXISTS (SELECT 1 FROM QueuedLocations WHERE Id = ? AND ServerConfirmed = 1)
            """, queuedLocationId, queuedLocationId);
    }

    /// <inheritdoc />
    public async Task<LocalTimelineEntry?> GetByQueuedLocationIdAsync(int queuedLocationId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<LocalTimelineEntry>()
            .Where(e => e.QueuedLocationId == queuedLocationId)
            .FirstOrDefaultAsync();
    }

    #endregion
}
