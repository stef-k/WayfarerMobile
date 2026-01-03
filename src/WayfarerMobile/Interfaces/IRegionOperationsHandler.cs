using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Handles region CRUD operations with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public interface IRegionOperationsHandler
{
    /// <summary>
    /// Creates a new region with optimistic UI pattern.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="name">The region name.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="coverImageUrl">Optional cover image URL.</param>
    /// <param name="centerLatitude">Optional center latitude.</param>
    /// <param name="centerLongitude">Optional center longitude.</param>
    /// <param name="displayOrder">Optional display order.</param>
    /// <returns>Operation result with entity ID (server ID or temp client ID).</returns>
    Task<RegionOperationResult> CreateRegionAsync(
        Guid tripId,
        string name,
        string? notes = null,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null);

    /// <summary>
    /// Updates a region with optimistic UI pattern.
    /// Stores original values for restoration on rejection.
    /// </summary>
    /// <param name="regionId">The region ID to update.</param>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="name">Optional new name.</param>
    /// <param name="notes">Optional new notes.</param>
    /// <param name="includeNotes">Whether to update notes (even if null).</param>
    /// <param name="coverImageUrl">Optional new cover image URL.</param>
    /// <param name="centerLatitude">Optional new center latitude.</param>
    /// <param name="centerLongitude">Optional new center longitude.</param>
    /// <param name="displayOrder">Optional new display order.</param>
    /// <returns>Operation result.</returns>
    Task<RegionOperationResult> UpdateRegionAsync(
        Guid regionId,
        Guid tripId,
        string? name = null,
        string? notes = null,
        bool includeNotes = false,
        string? coverImageUrl = null,
        double? centerLatitude = null,
        double? centerLongitude = null,
        int? displayOrder = null);

    /// <summary>
    /// Deletes a region with optimistic UI pattern.
    /// Stores original data for restoration on rejection.
    /// </summary>
    /// <param name="regionId">The region ID to delete.</param>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>Operation result.</returns>
    Task<RegionOperationResult> DeleteRegionAsync(Guid regionId, Guid tripId);
}
