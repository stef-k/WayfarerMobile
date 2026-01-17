using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Repository interface for offline segment operations.
/// Manages segments (routes between places) for downloaded trips.
/// </summary>
public interface ISegmentRepository
{
    /// <summary>
    /// Gets all segments for a downloaded trip.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <returns>List of segments ordered by sort order.</returns>
    Task<List<OfflineSegmentEntity>> GetOfflineSegmentsAsync(int tripId);

    /// <summary>
    /// Saves offline segments for a trip (replaces existing).
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="segments">The segments to save.</param>
    Task SaveOfflineSegmentsAsync(int tripId, IEnumerable<OfflineSegmentEntity> segments);

    /// <summary>
    /// Gets a segment by origin and destination.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    /// <param name="originId">The origin place server ID.</param>
    /// <param name="destinationId">The destination place server ID.</param>
    /// <returns>The segment or null if not found.</returns>
    Task<OfflineSegmentEntity?> GetOfflineSegmentAsync(int tripId, Guid originId, Guid destinationId);

    /// <summary>
    /// Gets an offline segment by server ID.
    /// </summary>
    /// <param name="serverId">The server-side segment ID.</param>
    /// <returns>The segment or null if not found.</returns>
    Task<OfflineSegmentEntity?> GetOfflineSegmentByServerIdAsync(Guid serverId);

    /// <summary>
    /// Updates an offline segment.
    /// </summary>
    /// <param name="segment">The segment to update.</param>
    Task UpdateOfflineSegmentAsync(OfflineSegmentEntity segment);

    /// <summary>
    /// Deletes all segments for a trip.
    /// Used for cascade delete operations.
    /// </summary>
    /// <param name="tripId">The local trip ID.</param>
    Task DeleteSegmentsForTripAsync(int tripId);
}
