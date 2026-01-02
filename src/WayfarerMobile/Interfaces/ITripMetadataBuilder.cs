using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

namespace WayfarerMobile.Interfaces;

/// <summary>
/// Builds offline entity collections from trip details.
/// Pure transformation service with no side effects.
/// </summary>
public interface ITripMetadataBuilder
{
    /// <summary>
    /// Builds offline area entities from trip regions.
    /// </summary>
    /// <param name="trip">The trip details from the server.</param>
    /// <returns>List of area entities ready for database storage.</returns>
    List<OfflineAreaEntity> BuildAreas(TripDetails trip);

    /// <summary>
    /// Builds offline place entities from trip regions.
    /// </summary>
    /// <param name="trip">The trip details from the server.</param>
    /// <returns>List of place entities ready for database storage.</returns>
    List<OfflinePlaceEntity> BuildPlaces(TripDetails trip);

    /// <summary>
    /// Builds offline segment entities from trip segments.
    /// </summary>
    /// <param name="trip">The trip details from the server.</param>
    /// <returns>List of segment entities ready for database storage.</returns>
    List<OfflineSegmentEntity> BuildSegments(TripDetails trip);

    /// <summary>
    /// Builds offline polygon entities from trip region areas.
    /// </summary>
    /// <param name="trip">The trip details from the server.</param>
    /// <returns>List of polygon entities ready for database storage.</returns>
    List<OfflinePolygonEntity> BuildPolygons(TripDetails trip);

    /// <summary>
    /// Builds a lookup dictionary mapping place IDs to display names.
    /// Display names are formatted as "PlaceName, RegionName" unless the place and region have the same name.
    /// </summary>
    /// <param name="trip">The trip details from the server.</param>
    /// <returns>Dictionary mapping place server IDs to formatted display names.</returns>
    Dictionary<Guid, string> BuildPlaceNameLookup(TripDetails trip);
}
