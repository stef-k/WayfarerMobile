using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Handles place CRUD operations with optimistic UI pattern.
/// Returns operation results instead of raising events directly.
/// </summary>
public interface IPlaceOperationsHandler
{
    /// <summary>
    /// Creates a new place with optimistic UI pattern.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="regionId">The optional region ID.</param>
    /// <param name="name">The place name.</param>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <param name="notes">Optional notes.</param>
    /// <param name="iconName">Optional icon name.</param>
    /// <param name="markerColor">Optional marker color.</param>
    /// <param name="displayOrder">Optional display order.</param>
    /// <param name="clientTempId">Optional client-generated temp ID. If provided, used instead of generating a new one.</param>
    /// <returns>Operation result with entity ID (server ID or temp client ID) and TempClientId for reconciliation.</returns>
    Task<PlaceOperationResult> CreatePlaceAsync(
        Guid tripId,
        Guid? regionId,
        string name,
        double latitude,
        double longitude,
        string? notes = null,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null,
        Guid? clientTempId = null);

    /// <summary>
    /// Updates a place with optimistic UI pattern.
    /// Stores original values for restoration on rejection.
    /// </summary>
    /// <param name="placeId">The place ID to update.</param>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="name">Optional new name.</param>
    /// <param name="latitude">Optional new latitude.</param>
    /// <param name="longitude">Optional new longitude.</param>
    /// <param name="notes">Optional new notes.</param>
    /// <param name="includeNotes">Whether to update notes (even if null).</param>
    /// <param name="iconName">Optional new icon name.</param>
    /// <param name="markerColor">Optional new marker color.</param>
    /// <param name="displayOrder">Optional new display order.</param>
    /// <returns>Operation result.</returns>
    Task<PlaceOperationResult> UpdatePlaceAsync(
        Guid placeId,
        Guid tripId,
        string? name = null,
        double? latitude = null,
        double? longitude = null,
        string? notes = null,
        bool includeNotes = false,
        string? iconName = null,
        string? markerColor = null,
        int? displayOrder = null);

    /// <summary>
    /// Deletes a place with optimistic UI pattern.
    /// Stores original data for restoration on rejection.
    /// </summary>
    /// <param name="placeId">The place ID to delete.</param>
    /// <param name="tripId">The trip ID.</param>
    /// <returns>Operation result.</returns>
    Task<PlaceOperationResult> DeletePlaceAsync(Guid placeId, Guid tripId);
}
