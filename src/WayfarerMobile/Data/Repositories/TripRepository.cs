using SQLite;
using WayfarerMobile.Core.Enums;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository for downloaded trip operations.
/// Manages trip metadata and coordinates cascade deletes.
/// </summary>
public class TripRepository : RepositoryBase, ITripRepository
{
    /// <summary>
    /// Creates a new instance of TripRepository.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    public TripRepository(Func<Task<SQLiteAsyncConnection>> connectionFactory)
        : base(connectionFactory)
    {
    }

    /// <inheritdoc />
    public async Task<List<DownloadedTripEntity>> GetDownloadedTripsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<DownloadedTripEntity>()
            .OrderByDescending(t => t.DownloadedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<DownloadedTripEntity?> GetDownloadedTripByServerIdAsync(Guid serverId)
    {
        var db = await GetConnectionAsync();
        return await db.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.ServerId == serverId);
    }

    /// <inheritdoc />
    public async Task<DownloadedTripEntity?> GetDownloadedTripAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.Table<DownloadedTripEntity>()
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    /// <inheritdoc />
    public async Task<int> SaveDownloadedTripAsync(DownloadedTripEntity trip)
    {
        var db = await GetConnectionAsync();
        trip.UpdatedAt = DateTime.UtcNow;

        if (trip.Id == 0)
        {
            await db.InsertAsync(trip);
        }
        else
        {
            await db.UpdateAsync(trip);
        }
        return trip.Id;
    }

    /// <inheritdoc />
    public async Task DeleteDownloadedTripAsync(int tripId)
    {
        var db = await GetConnectionAsync();

        // Use transaction to ensure atomic cascade delete - partial failure leaves no orphaned data
        await db.RunInTransactionAsync(conn =>
        {
            // Delete associated tiles
            conn.Execute("DELETE FROM TripTiles WHERE TripId = ?", tripId);

            // Delete associated places
            conn.Execute("DELETE FROM OfflinePlaces WHERE TripId = ?", tripId);

            // Delete associated segments
            conn.Execute("DELETE FROM OfflineSegments WHERE TripId = ?", tripId);

            // Delete associated areas
            conn.Execute("DELETE FROM OfflineAreas WHERE TripId = ?", tripId);

            // Delete associated polygons
            conn.Execute("DELETE FROM OfflinePolygons WHERE TripId = ?", tripId);

            // Delete associated download state (1:1 relationship)
            conn.Execute("DELETE FROM TripDownloadStates WHERE TripId = ?", tripId);

            // Delete the trip
            conn.Execute("DELETE FROM DownloadedTrips WHERE Id = ?", tripId);
        });
    }

    /// <inheritdoc />
    public async Task<long> GetTotalTripCacheSizeAsync()
    {
        var db = await GetConnectionAsync();
        // Use UnifiedStateValue (single source of truth) for cache size calculation
        var result = await db.ExecuteScalarAsync<long>(
            "SELECT COALESCE(SUM(TotalSizeBytes), 0) FROM DownloadedTrips WHERE UnifiedStateValue = ?",
            (int)UnifiedDownloadState.Complete);
        return result;
    }
}
