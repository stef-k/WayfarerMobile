using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Builds offline entity collections from trip details.
/// Pure transformation service with no side effects or dependencies.
/// </summary>
public class TripMetadataBuilder : ITripMetadataBuilder
{
    /// <inheritdoc/>
    public List<OfflineAreaEntity> BuildAreas(TripDetails trip)
    {
        // Server always sends SortOrder (displayOrder in JSON), use it directly
        // Only fall back to index if server sends 0 and it looks like a default value
        return trip.Regions.Select((r, index) => new OfflineAreaEntity
        {
            ServerId = r.Id,
            Name = r.Name,
            Notes = r.Notes,
            CoverImageUrl = r.CoverImageUrl,
            CenterLatitude = r.CenterLatitude,
            CenterLongitude = r.CenterLongitude,
            SortOrder = r.SortOrder,
            PlaceCount = r.Places.Count
        }).ToList();
    }

    /// <inheritdoc/>
    public List<OfflinePlaceEntity> BuildPlaces(TripDetails trip)
    {
        var places = new List<OfflinePlaceEntity>();

        foreach (var region in trip.Regions)
        {
            // Track index per region, not globally
            int regionPlaceIndex = 0;

            foreach (var place in region.Places)
            {
                places.Add(new OfflinePlaceEntity
                {
                    ServerId = place.Id,
                    RegionId = region.Id,
                    RegionName = region.Name,
                    Name = place.Name,
                    Latitude = place.Latitude,
                    Longitude = place.Longitude,
                    Notes = place.Notes,
                    IconName = place.Icon,
                    MarkerColor = place.MarkerColor,
                    Address = place.Address,
                    // Use server value if present, otherwise use per-region index
                    // Note: SortOrder 0 is valid (first place in region)
                    SortOrder = place.SortOrder ?? regionPlaceIndex
                });
                regionPlaceIndex++;
            }
        }

        return places;
    }

    /// <inheritdoc/>
    public List<OfflineSegmentEntity> BuildSegments(TripDetails trip)
    {
        var placeNameLookup = BuildPlaceNameLookup(trip);

        return trip.Segments.Select((s, index) => new OfflineSegmentEntity
        {
            ServerId = s.Id,
            OriginId = s.OriginId ?? Guid.Empty,
            OriginName = s.OriginId.HasValue && placeNameLookup.TryGetValue(s.OriginId.Value, out var fromName) ? fromName : null,
            DestinationId = s.DestinationId ?? Guid.Empty,
            DestinationName = s.DestinationId.HasValue && placeNameLookup.TryGetValue(s.DestinationId.Value, out var toName) ? toName : null,
            TransportMode = s.TransportMode,
            DistanceKm = s.DistanceKm,
            DurationMinutes = (int?)s.DurationMinutes,
            Notes = s.Notes,
            Geometry = s.Geometry,
            SortOrder = index
        }).ToList();
    }

    /// <inheritdoc/>
    public List<OfflinePolygonEntity> BuildPolygons(TripDetails trip)
    {
        var polygons = new List<OfflinePolygonEntity>();

        foreach (var region in trip.Regions)
        {
            foreach (var area in region.Areas)
            {
                polygons.Add(new OfflinePolygonEntity
                {
                    ServerId = area.Id,
                    RegionId = region.Id,
                    Name = area.Name,
                    Notes = area.Notes,
                    FillColor = area.FillColor,
                    StrokeColor = area.StrokeColor,
                    GeometryGeoJson = area.GeometryGeoJson,
                    SortOrder = area.SortOrder ?? 0
                });
            }
        }

        return polygons;
    }

    /// <inheritdoc/>
    public Dictionary<Guid, string> BuildPlaceNameLookup(TripDetails trip)
    {
        var lookup = new Dictionary<Guid, string>();

        foreach (var region in trip.Regions)
        {
            foreach (var place in region.Places)
            {
                // Format: "PlaceName, RegionName" (or just "PlaceName" if region has same name)
                var displayName = string.Equals(place.Name, region.Name, StringComparison.OrdinalIgnoreCase)
                    ? place.Name
                    : $"{place.Name}, {region.Name}";
                lookup[place.Id] = displayName;
            }
        }

        return lookup;
    }
}
