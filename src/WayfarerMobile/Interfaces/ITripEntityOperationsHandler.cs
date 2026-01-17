using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Handles trip entity update operations (Trip, Segment, Area) with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public interface ITripEntityOperationsHandler
{
    /// <summary>
    /// Updates a trip's metadata (name, notes) with optimistic UI pattern.
    /// 1. Reads current values from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    /// <param name="tripId">The trip ID to update.</param>
    /// <param name="name">Optional new name.</param>
    /// <param name="notes">Optional new notes.</param>
    /// <param name="includeNotes">Whether to update notes (even if null).</param>
    /// <returns>Operation result.</returns>
    Task<EntityOperationResult> UpdateTripAsync(
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false);

    /// <summary>
    /// Updates a segment's notes with optimistic UI pattern.
    /// 1. Reads current notes from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    /// <param name="segmentId">The segment ID to update.</param>
    /// <param name="tripId">The trip ID containing the segment.</param>
    /// <param name="notes">The new notes value.</param>
    /// <returns>Operation result.</returns>
    Task<EntityOperationResult> UpdateSegmentNotesAsync(
        Guid segmentId,
        Guid tripId,
        string? notes);

    /// <summary>
    /// Updates an area's (polygon) notes with optimistic UI pattern.
    /// 1. Reads current notes from offline table
    /// 2. Updates offline table (optimistic)
    /// 3. Stores original in queue for restoration
    /// 4. Syncs to server
    /// </summary>
    /// <param name="tripId">The trip ID containing the area.</param>
    /// <param name="areaId">The area ID to update.</param>
    /// <param name="notes">The new notes value.</param>
    /// <returns>Operation result.</returns>
    Task<EntityOperationResult> UpdateAreaNotesAsync(
        Guid tripId,
        Guid areaId,
        string? notes);
}
