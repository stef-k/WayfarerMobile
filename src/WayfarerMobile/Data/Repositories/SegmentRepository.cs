using SQLite;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for offline segment operations.
/// Manages segments (routes between places) for downloaded trips.
/// </summary>
public class SegmentRepository : RepositoryBase, ISegmentRepository
{
    /// <summary>
    /// Creates a new instance of SegmentRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public SegmentRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<List<OfflineSegmentEntity>> GetOfflineSegmentsAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflineSegmentEntity>()
            .Where(s => s.TripId == tripId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task SaveOfflineSegmentsAsync(int tripId, IEnumerable<OfflineSegmentEntity> segments)
    {
        var db = await GetConnectionAsync();

        // Clear existing segments for this trip
        await db.ExecuteAsync("DELETE FROM OfflineSegments WHERE TripId = ?", tripId);

        // Set TripId and bulk insert
        var segmentList = segments.ToList();
        foreach (var segment in segmentList)
        {
            segment.TripId = tripId;
        }

        if (segmentList.Count > 0)
        {
            await db.InsertAllAsync(segmentList);
        }
    }

    /// <inheritdoc />
    public async Task<OfflineSegmentEntity?> GetOfflineSegmentAsync(int tripId, Guid originId, Guid destinationId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflineSegmentEntity>()
            .Where(s => s.TripId == tripId && s.OriginId == originId && s.DestinationId == destinationId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<OfflineSegmentEntity?> GetOfflineSegmentByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<OfflineSegmentEntity>()
            .Where(s => s.ServerId == serverId)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task UpdateOfflineSegmentAsync(OfflineSegmentEntity segment)
    {
        var db = await GetConnectionAsync();
        await db.UpdateAsync(segment);
    }

    /// <inheritdoc />
    public async Task DeleteSegmentsForTripAsync(int tripId)
    {
        var db = await GetConnectionAsync();
        await db.ExecuteAsync("DELETE FROM OfflineSegments WHERE TripId = ?", tripId);
    }
}
