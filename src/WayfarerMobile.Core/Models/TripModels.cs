using System.ComponentModel;
using System.Text.Json.Serialization;
using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Core.Models;

/// <summary>
/// Trip summary for list display.
/// </summary>
public class TripSummary
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trip description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the countries covered.
    /// </summary>
    public List<string> Countries { get; set; } = new();

    /// <summary>
    /// Gets or sets the cities covered.
    /// </summary>
    public List<string> Cities { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the trip is public.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Gets or sets the last modified date.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the version number for sync tracking.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the bounding box.
    /// </summary>
    public BoundingBox? BoundingBox { get; set; }

    /// <summary>
    /// Gets or sets the number of places (server sends as placesCount).
    /// </summary>
    [JsonPropertyName("placesCount")]
    public int PlacesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of regions (server sends as regionsCount).
    /// </summary>
    [JsonPropertyName("regionsCount")]
    public int RegionsCount { get; set; }

    /// <summary>
    /// Gets a stats text showing region and place counts.
    /// Format: "X Regions / Y Places" or "Empty trip".
    /// </summary>
    [JsonIgnore]
    public string StatsText
    {
        get
        {
            if (RegionsCount == 0 && PlacesCount == 0)
                return "Empty trip";

            var parts = new List<string>();
            if (RegionsCount > 0)
                parts.Add($"{RegionsCount} Region{(RegionsCount == 1 ? "" : "s")}");
            if (PlacesCount > 0)
                parts.Add($"{PlacesCount} Place{(PlacesCount == 1 ? "" : "s")}");

            return string.Join(" / ", parts);
        }
    }
}

/// <summary>
/// Tag associated with a trip.
/// </summary>
public class TripTag
{
    /// <summary>
    /// Gets or sets the tag ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the URL-safe slug (e.g., "road-trip").
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name (e.g., "Road Trip").
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Full trip details with places and segments.
/// </summary>
public class TripDetails : INotifyPropertyChanged
{
    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trip description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the trip notes (HTML).
    /// Server sends as "notes".
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// Server sends as "coverImageUrl".
    /// </summary>
    [JsonPropertyName("coverImageUrl")]
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the tags associated with this trip.
    /// </summary>
    public List<TripTag> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the center latitude.
    /// </summary>
    public double? CenterLat { get; set; }

    /// <summary>
    /// Gets or sets the center longitude.
    /// </summary>
    public double? CenterLon { get; set; }

    /// <summary>
    /// Gets or sets the default zoom level.
    /// </summary>
    public int? Zoom { get; set; }

    /// <summary>
    /// Gets or sets the bounding box.
    /// </summary>
    public BoundingBox? BoundingBox { get; set; }

    /// <summary>
    /// Gets or sets the regions.
    /// </summary>
    public List<TripRegion> Regions { get; set; } = new();

    /// <summary>
    /// Gets or sets the segments.
    /// </summary>
    public List<TripSegment> Segments { get; set; } = new();

    /// <summary>
    /// Gets or sets the version number for sync tracking.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// Server sends as "updatedAt".
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets whether this trip has tags.
    /// </summary>
    [JsonIgnore]
    public bool HasTags => Tags.Count > 0;

    /// <summary>
    /// Gets the tags as a comma-separated string for display.
    /// </summary>
    [JsonIgnore]
    public string TagsDisplay => Tags.Count > 0
        ? string.Join(", ", Tags.Select(t => t.Name))
        : string.Empty;

    /// <summary>
    /// Gets whether this trip has a cover image.
    /// </summary>
    [JsonIgnore]
    public bool HasCoverImage => !string.IsNullOrEmpty(CoverImageUrl);

    /// <summary>
    /// Gets the cover image URL with any proxy wrapping removed.
    /// Use this for display instead of CoverImageUrl to handle double-proxied URLs.
    /// </summary>
    [JsonIgnore]
    public string? CleanCoverImageUrl => ImageProxyHelper.UnwrapProxyUrl(CoverImageUrl);

    /// <summary>
    /// Gets whether this trip has meaningful notes content.
    /// Filters out empty HTML like Quill.js default &lt;p&gt;&lt;/p&gt; or &lt;p&gt;&lt;br&gt;&lt;/p&gt;.
    /// </summary>
    [JsonIgnore]
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
                return false;

            // Strip HTML tags and check if there's actual content
            var stripped = System.Text.RegularExpressions.Regex.Replace(Notes, "<[^>]*>", "").Trim();
            return !string.IsNullOrWhiteSpace(stripped);
        }
    }

    /// <summary>
    /// Gets regions sorted by display order for UI binding.
    /// Places and areas within each region are also sorted.
    /// </summary>
    [JsonIgnore]
    public List<TripRegion> SortedRegions =>
        Regions
            .OrderBy(r => r.SortOrder)
            .Select(r => new TripRegion
            {
                Id = r.Id,
                Name = r.Name,
                Notes = r.Notes,
                CoverImageUrl = r.CoverImageUrl,
                CenterLatitude = r.CenterLatitude,
                CenterLongitude = r.CenterLongitude,
                SortOrder = r.SortOrder,
                Places = r.Places.OrderBy(p => p.SortOrder).ToList(),
                Areas = r.Areas.OrderBy(a => a.SortOrder).ToList()
            })
            .ToList();

    /// <summary>
    /// Gets all places from all regions.
    /// </summary>
    [JsonIgnore]
    public List<TripPlace> AllPlaces =>
        Regions.SelectMany(r => r.Places).ToList();

    /// <summary>
    /// Gets all areas from all regions.
    /// </summary>
    [JsonIgnore]
    public List<TripArea> AllAreas =>
        Regions.SelectMany(r => r.Areas).ToList();

    /// <summary>
    /// Notifies that the SortedRegions property has changed.
    /// Call this after modifying regions to refresh the UI.
    /// </summary>
    public void NotifySortedRegionsChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortedRegions)));
    }
}

/// <summary>
/// Geographic bounding box.
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// Gets or sets the north latitude.
    /// </summary>
    [JsonPropertyName("north")]
    public double North { get; set; }

    /// <summary>
    /// Gets or sets the south latitude.
    /// </summary>
    [JsonPropertyName("south")]
    public double South { get; set; }

    /// <summary>
    /// Gets or sets the east longitude.
    /// </summary>
    [JsonPropertyName("east")]
    public double East { get; set; }

    /// <summary>
    /// Gets or sets the west longitude.
    /// </summary>
    [JsonPropertyName("west")]
    public double West { get; set; }

    /// <summary>
    /// Gets whether this bounding box has valid coordinates.
    /// A valid bounding box has North >= South (allows single-point), coordinates within valid ranges,
    /// and is not all zeros (default uninitialized state).
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        North >= South &&
        North is >= -90 and <= 90 &&
        South is >= -90 and <= 90 &&
        East is >= -180 and <= 180 &&
        West is >= -180 and <= 180 &&
        !(North == 0 && South == 0 && East == 0 && West == 0);
}

/// <summary>
/// Trip region containing places and areas.
/// Implements INotifyPropertyChanged for UI binding support.
/// </summary>
public class TripRegion : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _notes;
    private string? _coverImageUrl;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The reserved name for the built-in unassigned places region.
    /// </summary>
    public const string UnassignedPlacesName = "Unassigned Places";

    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the region name.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>
    /// Gets or sets the region notes (HTML).
    /// </summary>
    public string? Notes
    {
        get => _notes;
        set
        {
            if (_notes != value)
            {
                _notes = value;
                OnPropertyChanged(nameof(Notes));
                OnPropertyChanged(nameof(HasNotes));
            }
        }
    }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? CoverImageUrl
    {
        get => _coverImageUrl;
        set
        {
            if (_coverImageUrl != value)
            {
                _coverImageUrl = value;
                OnPropertyChanged(nameof(CoverImageUrl));
                OnPropertyChanged(nameof(HasCoverImage));
            }
        }
    }

    /// <summary>
    /// Gets or sets the center latitude.
    /// Populated from Center property during deserialization.
    /// </summary>
    [JsonIgnore]
    public double? CenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the center longitude.
    /// Populated from Center property during deserialization.
    /// </summary>
    [JsonIgnore]
    public double? CenterLongitude { get; set; }

    /// <summary>
    /// Center as [lon, lat] array for API deserialization.
    /// Server sends coordinates in GeoJSON format: [longitude, latitude].
    /// </summary>
    [JsonPropertyName("center")]
    public double[]? Center
    {
        get => CenterLatitude.HasValue && CenterLongitude.HasValue
            ? new[] { CenterLongitude.Value, CenterLatitude.Value }
            : null;
        set
        {
            if (value is { Length: >= 2 })
            {
                CenterLongitude = value[0];
                CenterLatitude = value[1];
            }
        }
    }

    /// <summary>
    /// Gets or sets the places in this region.
    /// </summary>
    public List<TripPlace> Places { get; set; } = new();

    /// <summary>
    /// Gets or sets the areas (polygons) in this region.
    /// </summary>
    public List<TripArea> Areas { get; set; } = new();

    /// <summary>
    /// Gets or sets the sort order.
    /// Server sends as "displayOrder".
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets whether this is the built-in "Unassigned Places" region.
    /// </summary>
    [JsonIgnore]
    public bool IsUnassignedRegion => string.Equals(Name, UnassignedPlacesName, StringComparison.Ordinal);

    /// <summary>
    /// Gets whether this region has any content (places or areas).
    /// </summary>
    [JsonIgnore]
    public bool HasContent => Places.Count > 0 || Areas.Count > 0;

    /// <summary>
    /// Gets whether this region should be shown in the UI.
    /// "Unassigned Places" is hidden when empty; other regions always shown.
    /// </summary>
    [JsonIgnore]
    public bool IsVisibleInUi => !IsUnassignedRegion || HasContent;

    /// <summary>
    /// Gets whether this region can be deleted.
    /// "Unassigned Places" cannot be deleted.
    /// </summary>
    [JsonIgnore]
    public bool CanDelete => !IsUnassignedRegion;

    /// <summary>
    /// Gets whether this region can be renamed.
    /// "Unassigned Places" cannot be renamed.
    /// </summary>
    [JsonIgnore]
    public bool CanRename => !IsUnassignedRegion;

    /// <summary>
    /// Gets whether this region has a cover image.
    /// </summary>
    [JsonIgnore]
    public bool HasCoverImage => !string.IsNullOrEmpty(CoverImageUrl);

    /// <summary>
    /// Gets the cover image URL with any proxy wrapping removed.
    /// Use this for display instead of CoverImageUrl to handle double-proxied URLs.
    /// </summary>
    [JsonIgnore]
    public string? CleanCoverImageUrl => ImageProxyHelper.UnwrapProxyUrl(CoverImageUrl);

    /// <summary>
    /// Gets whether this region has meaningful notes content.
    /// Filters out empty HTML like Quill.js default.
    /// </summary>
    [JsonIgnore]
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
                return false;
            var stripped = System.Text.RegularExpressions.Regex.Replace(Notes, "<[^>]*>", "").Trim();
            return !string.IsNullOrWhiteSpace(stripped);
        }
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Area (polygon zone) within a trip region.
/// Represents geographic boundaries like neighborhoods, parks, or zones.
/// </summary>
public class TripArea
{
    /// <summary>
    /// Gets or sets the area ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the area name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the area notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the fill color for the polygon.
    /// Server sends as "fillHex".
    /// </summary>
    [JsonPropertyName("fillHex")]
    public string? FillColor { get; set; }

    /// <summary>
    /// Gets or sets the stroke/border color for the polygon.
    /// </summary>
    public string? StrokeColor { get; set; }

    /// <summary>
    /// Backing field for GeometryGeoJson.
    /// </summary>
    private string? _geometryGeoJson;

    /// <summary>
    /// Backing field for parsed boundary.
    /// </summary>
    private List<GeoCoordinate>? _boundary;

    /// <summary>
    /// Gets or sets the polygon boundary coordinates.
    /// Populated from GeometryGeoJson during deserialization or set directly for offline.
    /// </summary>
    [JsonIgnore]
    public List<GeoCoordinate> Boundary
    {
        get
        {
            // Lazy parse on first access if not already parsed
            if (_boundary == null && !string.IsNullOrEmpty(_geometryGeoJson))
            {
                _boundary = ParseGeoJsonPolygon(_geometryGeoJson);
            }
            return _boundary ?? new List<GeoCoordinate>();
        }
        set => _boundary = value;
    }

    /// <summary>
    /// GeoJSON string for the polygon geometry.
    /// Server sends as "geometryGeoJson". Parsed into Boundary on access.
    /// </summary>
    [JsonPropertyName("geometryGeoJson")]
    public string? GeometryGeoJson
    {
        get => _geometryGeoJson;
        set
        {
            _geometryGeoJson = value;
            _boundary = null; // Clear cached boundary to force re-parse
        }
    }

    /// <summary>
    /// Gets or sets the sort order.
    /// Server sends as "displayOrder" (nullable).
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int? SortOrder { get; set; }

    /// <summary>
    /// Parses a GeoJSON polygon string into a list of coordinates.
    /// </summary>
    private static List<GeoCoordinate> ParseGeoJsonPolygon(string geoJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(geoJson);
            var root = doc.RootElement;

            // GeoJSON Polygon format: { "type": "Polygon", "coordinates": [[[lon,lat], [lon,lat], ...]] }
            if (root.TryGetProperty("coordinates", out var coordinates) &&
                coordinates.GetArrayLength() > 0)
            {
                var ring = coordinates[0]; // First ring (outer boundary)
                var result = new List<GeoCoordinate>();

                foreach (var point in ring.EnumerateArray())
                {
                    if (point.GetArrayLength() >= 2)
                    {
                        var lon = point[0].GetDouble();
                        var lat = point[1].GetDouble();
                        result.Add(new GeoCoordinate { Latitude = lat, Longitude = lon });
                    }
                }

                return result;
            }
        }
        catch
        {
            // Silently fail - invalid GeoJSON returns empty list
        }

        return new List<GeoCoordinate>();
    }

    /// <summary>
    /// Gets the center point of the area (centroid of polygon).
    /// </summary>
    [JsonIgnore]
    public GeoCoordinate? Center
    {
        get
        {
            if (Boundary.Count == 0)
                return null;

            var avgLat = Boundary.Average(c => c.Latitude);
            var avgLon = Boundary.Average(c => c.Longitude);
            return new GeoCoordinate { Latitude = avgLat, Longitude = avgLon };
        }
    }

    /// <summary>
    /// Gets whether this area has meaningful notes content.
    /// Filters out empty HTML like Quill.js default.
    /// </summary>
    [JsonIgnore]
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
                return false;
            var stripped = System.Text.RegularExpressions.Regex.Replace(Notes, "<[^>]*>", "").Trim();
            return !string.IsNullOrWhiteSpace(stripped);
        }
    }
}

/// <summary>
/// Geographic coordinate (latitude/longitude pair).
/// </summary>
public class GeoCoordinate
{
    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }
}

/// <summary>
/// Place within a trip.
/// </summary>
public class TripPlace : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string? _notes;
    private string? _icon;
    private string? _markerColor;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the place ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    /// <summary>
    /// Gets or sets the latitude.
    /// Can be set directly (offline code) or via Location property (API deserialization).
    /// </summary>
    [JsonIgnore]
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// Can be set directly (offline code) or via Location property (API deserialization).
    /// </summary>
    [JsonIgnore]
    public double Longitude { get; set; }

    /// <summary>
    /// Location as [lon, lat] array for API deserialization.
    /// Server sends coordinates in GeoJSON format: [longitude, latitude].
    /// </summary>
    [JsonPropertyName("location")]
    public double[]? Location
    {
        get => Latitude != 0 || Longitude != 0 ? new[] { Longitude, Latitude } : null;
        set
        {
            if (value is { Length: >= 2 })
            {
                Longitude = value[0];
                Latitude = value[1];
            }
        }
    }

    /// <summary>
    /// Gets or sets the place notes (HTML).
    /// </summary>
    public string? Notes
    {
        get => _notes;
        set
        {
            if (_notes != value)
            {
                _notes = value;
                OnPropertyChanged(nameof(Notes));
            }
        }
    }

    /// <summary>
    /// Gets or sets the icon name.
    /// Server sends as "iconName".
    /// </summary>
    [JsonPropertyName("iconName")]
    public string? Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                OnPropertyChanged(nameof(Icon));
                OnPropertyChanged(nameof(IconPath));
            }
        }
    }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    public string? MarkerColor
    {
        get => _markerColor;
        set
        {
            if (_markerColor != value)
            {
                _markerColor = value;
                OnPropertyChanged(nameof(MarkerColor));
                OnPropertyChanged(nameof(IconPath));
            }
        }
    }

    /// <summary>
    /// Gets or sets the address of the place.
    /// Server sends as "address".
    /// </summary>
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// Server sends as "displayOrder" (nullable).
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int? SortOrder { get; set; }

    /// <summary>
    /// Gets the icon resource path for UI binding.
    /// Uses IconCatalog to resolve the icon and color to a resource path.
    /// </summary>
    [JsonIgnore]
    public string IconPath => Helpers.IconCatalog.GetIconResourcePath(Icon, MarkerColor);

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Segment connecting two places.
/// </summary>
public class TripSegment
{
    /// <summary>
    /// Gets or sets the segment ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the origin place ID.
    /// Server sends as "fromPlaceId".
    /// </summary>
    [JsonPropertyName("fromPlaceId")]
    public Guid? OriginId { get; set; }

    /// <summary>
    /// Gets or sets the origin place name (populated during loading for display).
    /// </summary>
    [JsonIgnore]
    public string? OriginName { get; set; }

    /// <summary>
    /// Gets or sets the destination place ID.
    /// Server sends as "toPlaceId".
    /// </summary>
    [JsonPropertyName("toPlaceId")]
    public Guid? DestinationId { get; set; }

    /// <summary>
    /// Gets or sets the destination place name (populated during loading for display).
    /// </summary>
    [JsonIgnore]
    public string? DestinationName { get; set; }

    /// <summary>
    /// Gets or sets the transportation mode.
    /// Server sends as "mode".
    /// </summary>
    [JsonPropertyName("mode")]
    public string? TransportMode { get; set; }

    /// <summary>
    /// Gets or sets the distance in kilometers.
    /// Server sends as "estimatedDistanceKm".
    /// </summary>
    [JsonPropertyName("estimatedDistanceKm")]
    public double? DistanceKm { get; set; }

    /// <summary>
    /// Gets or sets the duration in minutes.
    /// Server sends as "estimatedDurationMinutes".
    /// </summary>
    [JsonPropertyName("estimatedDurationMinutes")]
    public double? DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the segment notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// Server sends as "displayOrder".
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the route geometry (encoded polyline or JSON).
    /// Server sends as "routeJson".
    /// </summary>
    [JsonPropertyName("routeJson")]
    public string? Geometry { get; set; }

    /// <summary>
    /// Gets whether this segment has meaningful notes content.
    /// Filters out empty HTML like Quill.js default.
    /// </summary>
    [JsonIgnore]
    public bool HasNotes
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Notes))
                return false;
            var stripped = System.Text.RegularExpressions.Regex.Replace(Notes, "<[^>]*>", "").Trim();
            return !string.IsNullOrWhiteSpace(stripped);
        }
    }
}

/// <summary>
/// Downloaded trip stored locally.
/// </summary>
public class DownloadedTrip
{
    /// <summary>
    /// Gets or sets the local ID.
    /// </summary>
    public Guid LocalId { get; set; }

    /// <summary>
    /// Gets or sets the server ID.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the download status.
    /// </summary>
    public string Status { get; set; } = "metadata_only";

    /// <summary>
    /// Gets or sets when the trip was downloaded.
    /// </summary>
    public DateTime DownloadedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of places.
    /// </summary>
    public int PlaceCount { get; set; }

    /// <summary>
    /// Gets or sets the number of segments.
    /// </summary>
    public int SegmentCount { get; set; }

    /// <summary>
    /// Gets whether the trip has full offline data.
    /// </summary>
    [JsonIgnore]
    public bool IsFullyDownloaded => Status == "complete";
}

/// <summary>
/// Trip boundary response from server for tile download calculation.
/// </summary>
public class TripBoundaryResponse
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    [JsonPropertyName("tripId")]
    public Guid TripId { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bounding box.
    /// </summary>
    [JsonPropertyName("boundingBox")]
    public BoundingBox BoundingBox { get; set; } = new();
}

/// <summary>
/// Tile coordinate for download.
/// Implements IEquatable for proper collection operations (e.g., List.Remove).
/// This class is immutable after construction (init-only properties).
/// </summary>
public class TileCoordinate : IEquatable<TileCoordinate>
{
    /// <summary>
    /// Gets the zoom level.
    /// </summary>
    public int Zoom { get; init; }

    /// <summary>
    /// Gets the X coordinate.
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Gets the Y coordinate.
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// Gets the tile URL from a server template.
    /// </summary>
    /// <param name="urlTemplate">URL template with {z}, {x}, {y} placeholders.</param>
    /// <returns>Full tile URL.</returns>
    public string GetTileUrl(string urlTemplate) =>
        urlTemplate.Replace("{z}", Zoom.ToString())
                   .Replace("{x}", X.ToString())
                   .Replace("{y}", Y.ToString());

    /// <summary>
    /// Gets a unique identifier for this tile.
    /// </summary>
    public string Id => $"{Zoom}-{X}-{Y}";

    /// <inheritdoc />
    public bool Equals(TileCoordinate? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Zoom == other.Zoom && X == other.X && Y == other.Y;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TileCoordinate);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Zoom, X, Y);

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(TileCoordinate? left, TileCoordinate? right) =>
        left?.Equals(right) ?? right is null;

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(TileCoordinate? left, TileCoordinate? right) =>
        !(left == right);
}

/// <summary>
/// Location from the timeline API with full details.
/// </summary>
public class TimelineLocation
{
    /// <summary>
    /// Gets or sets the location ID.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the server timestamp (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the local timestamp (converted to user's timezone).
    /// </summary>
    public DateTime LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the coordinates.
    /// </summary>
    public TimelineCoordinates? Coordinates { get; set; }

    /// <summary>
    /// Gets or sets the timezone identifier.
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Gets or sets the accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the altitude.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the speed.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the location type.
    /// </summary>
    public string? LocationType { get; set; }

    /// <summary>
    /// Gets or sets the activity type name.
    /// </summary>
    public string? ActivityType { get; set; }

    /// <summary>
    /// Gets or sets the short address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the full address.
    /// </summary>
    public string? FullAddress { get; set; }

    /// <summary>
    /// Gets or sets the street name.
    /// </summary>
    public string? StreetName { get; set; }

    /// <summary>
    /// Gets or sets the postal code.
    /// </summary>
    public string? PostCode { get; set; }

    /// <summary>
    /// Gets or sets the place (city).
    /// </summary>
    public string? Place { get; set; }

    /// <summary>
    /// Gets or sets the region (state/province).
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Gets or sets the country.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether this is the latest location.
    /// </summary>
    public bool IsLatestLocation { get; set; }

    /// <summary>
    /// Gets or sets the location time threshold (minutes).
    /// </summary>
    public int LocationTimeThresholdMinutes { get; set; }

    /// <summary>
    /// Gets the latitude from coordinates.
    /// </summary>
    [JsonIgnore]
    public double Latitude => Coordinates?.Y ?? 0;

    /// <summary>
    /// Gets the longitude from coordinates.
    /// </summary>
    [JsonIgnore]
    public double Longitude => Coordinates?.X ?? 0;

    /// <summary>
    /// Gets a display string for the location.
    /// </summary>
    [JsonIgnore]
    public string DisplayLocation
    {
        get
        {
            if (!string.IsNullOrEmpty(Place) && !string.IsNullOrEmpty(Country))
                return $"{Place}, {Country}";
            if (!string.IsNullOrEmpty(Place))
                return Place;
            if (!string.IsNullOrEmpty(Country))
                return Country;
            return $"{Latitude:F4}, {Longitude:F4}";
        }
    }
}

/// <summary>
/// Coordinates structure for timeline locations.
/// Matches server response format with latitude/longitude properties.
/// </summary>
public class TimelineCoordinates
{
    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    [JsonPropertyName("longitude")]
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double Y { get; set; }
}

/// <summary>
/// Response from the timeline API.
/// </summary>
public class TimelineResponse
{
    /// <summary>
    /// Gets or sets whether the request was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the location data.
    /// </summary>
    public List<TimelineLocation>? Data { get; set; }

    /// <summary>
    /// Gets or sets the total number of items.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Gets or sets the date type (day, month, year).
    /// </summary>
    public string? DateType { get; set; }

    /// <summary>
    /// Gets or sets the year.
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// Gets or sets the month.
    /// </summary>
    public int? Month { get; set; }

    /// <summary>
    /// Gets or sets the day.
    /// </summary>
    public int? Day { get; set; }
}

/// <summary>
/// Progress information for trip download.
/// </summary>
public class TripDownloadProgress
{
    /// <summary>
    /// Gets or sets the download percentage (0-100).
    /// </summary>
    public int Percentage { get; set; }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the download is complete.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Gets or sets the total tile count.
    /// </summary>
    public int TotalTiles { get; set; }

    /// <summary>
    /// Gets or sets the completed tile count.
    /// </summary>
    public int CompletedTiles { get; set; }

    /// <summary>
    /// Gets or sets the bytes downloaded.
    /// </summary>
    public long DownloadedBytes { get; set; }

    /// <summary>
    /// Gets or sets the total estimated bytes.
    /// </summary>
    public long TotalEstimatedBytes { get; set; }

    /// <summary>
    /// Gets or sets when the download started.
    /// </summary>
    public DateTime DownloadStartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the error count.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Gets the downloaded MB.
    /// </summary>
    [JsonIgnore]
    public double DownloadedMB => DownloadedBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Gets the total estimated MB.
    /// </summary>
    [JsonIgnore]
    public double TotalEstimatedMB => TotalEstimatedBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Gets the download speed in bytes per second.
    /// </summary>
    [JsonIgnore]
    public double SpeedBytesPerSecond
    {
        get
        {
            var elapsed = (DateTime.UtcNow - DownloadStartTime).TotalSeconds;
            return elapsed > 0 ? DownloadedBytes / elapsed : 0;
        }
    }

    /// <summary>
    /// Gets the formatted speed string.
    /// </summary>
    [JsonIgnore]
    public string SpeedText
    {
        get
        {
            var speedKB = SpeedBytesPerSecond / 1024;
            return speedKB >= 1024
                ? $"{speedKB / 1024:F1} MB/s"
                : $"{speedKB:F0} KB/s";
        }
    }

    /// <summary>
    /// Initializes progress for a new download.
    /// </summary>
    /// <param name="totalTiles">Total number of tiles.</param>
    /// <param name="estimatedBytes">Estimated total bytes.</param>
    public void Initialize(int totalTiles, long estimatedBytes)
    {
        TotalTiles = totalTiles;
        TotalEstimatedBytes = estimatedBytes;
        CompletedTiles = 0;
        DownloadedBytes = 0;
        DownloadStartTime = DateTime.UtcNow;
        IsComplete = false;
        ErrorCount = 0;
    }

    /// <summary>
    /// Updates progress after downloading a tile.
    /// </summary>
    /// <param name="tileSize">Size of the downloaded tile.</param>
    /// <param name="statusMessage">Optional status message.</param>
    public void UpdateProgress(long tileSize, string? statusMessage = null)
    {
        CompletedTiles++;
        DownloadedBytes += tileSize;
        Percentage = TotalTiles > 0 ? (int)(CompletedTiles * 100.0 / TotalTiles) : 0;
        if (statusMessage != null) Status = statusMessage;
    }

    /// <summary>
    /// Reports an error during download.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public void ReportError(string errorMessage)
    {
        ErrorCount++;
        Status = errorMessage;
    }
}

#region Location Logging

/// <summary>
/// Request to log a location to the server.
/// Used by IApiClient.LogLocationAsync and CheckInAsync methods.
/// </summary>
public class LocationLogRequest
{
    /// <summary>
    /// Gets or sets the latitude in degrees.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude in degrees.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the altitude in meters above sea level.
    /// </summary>
    public double? Altitude { get; set; }

    /// <summary>
    /// Gets or sets the horizontal accuracy in meters.
    /// </summary>
    public double? Accuracy { get; set; }

    /// <summary>
    /// Gets or sets the speed in meters per second.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this location was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the location provider (GPS, Network, etc.).
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets or sets the activity type ID for manual check-ins.
    /// </summary>
    public int? ActivityTypeId { get; set; }

    /// <summary>
    /// Gets or sets notes for this location.
    /// </summary>
    public string? Notes { get; set; }
}

#endregion

#region CRUD Request/Response Models

/// <summary>
/// Request to create a new place.
/// </summary>
public class PlaceCreateRequest
{
    /// <summary>
    /// Gets or sets the region ID (optional).
    /// </summary>
    [JsonPropertyName("regionId")]
    public Guid? RegionId { get; set; }

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    [JsonPropertyName("iconName")]
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    [JsonPropertyName("markerColor")]
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Gets the icon resource path for UI binding.
    /// Uses IconCatalog to resolve the icon and color to a resource path.
    /// </summary>
    [JsonIgnore]
    public string IconPath => Helpers.IconCatalog.GetIconResourcePath(IconName, MarkerColor);
}

/// <summary>
/// Request to update a place.
/// </summary>
public class PlaceUpdateRequest
{
    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    [JsonPropertyName("iconName")]
    public string? IconName { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    [JsonPropertyName("markerColor")]
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    [JsonPropertyName("regionId")]
    public Guid? RegionId { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    [JsonPropertyName("displayOrder")]
    public int? DisplayOrder { get; set; }
}

/// <summary>
/// Request to create a new region.
/// </summary>
public class RegionCreateRequest
{
    /// <summary>
    /// Gets or sets the region name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the center latitude.
    /// </summary>
    public double? CenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the center longitude.
    /// </summary>
    public double? CenterLongitude { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int? DisplayOrder { get; set; }
}

/// <summary>
/// Request to update a region.
/// </summary>
public class RegionUpdateRequest
{
    /// <summary>
    /// Gets or sets the region name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the cover image URL.
    /// </summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the center latitude.
    /// </summary>
    public double? CenterLatitude { get; set; }

    /// <summary>
    /// Gets or sets the center longitude.
    /// </summary>
    public double? CenterLongitude { get; set; }

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int? DisplayOrder { get; set; }
}

/// <summary>
/// Request to update a trip.
/// </summary>
public class TripUpdateRequest
{
    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the notes (HTML).
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response from trip update.
/// </summary>
public class TripUpdateResponse
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the updated trip name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the updated trip notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Request to update segment notes.
/// </summary>
public class SegmentNotesUpdateRequest
{
    /// <summary>
    /// Gets or sets the notes (HTML).
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Request to update area (polygon) notes.
/// </summary>
public class AreaNotesUpdateRequest
{
    /// <summary>
    /// Gets or sets the notes (HTML).
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response from area notes update.
/// </summary>
public class AreaUpdateResponse
{
    /// <summary>
    /// Gets or sets the area ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the updated notes.
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Response from segment notes update.
/// </summary>
public class SegmentUpdateResponse
{
    /// <summary>
    /// Gets or sets the segment ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the updated notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Response from place creation/update.
/// </summary>
public class PlaceResponse
{
    /// <summary>
    /// Gets or sets the place ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the created/updated place.
    /// </summary>
    public TripPlace? Place { get; set; }
}

/// <summary>
/// Response from region creation/update.
/// </summary>
public class RegionResponse
{
    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the created/updated region.
    /// </summary>
    public TripRegion? Region { get; set; }
}

#endregion

#region Timeline

/// <summary>
/// Request for updating a timeline location.
/// Matches server API: PUT /api/location/{id}
/// </summary>
public class TimelineLocationUpdateRequest
{
    /// <summary>
    /// Gets or sets the latitude (null if not changed).
    /// Must be provided together with Longitude.
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude (null if not changed).
    /// Must be provided together with Latitude.
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the local timestamp (null if not changed).
    /// </summary>
    public DateTime? LocalTimestamp { get; set; }

    /// <summary>
    /// Gets or sets the notes HTML (null if not changed).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether to clear notes (server will set notes to null).
    /// </summary>
    public bool? ClearNotes { get; set; }

    /// <summary>
    /// Gets or sets the activity type ID (null if not changed).
    /// </summary>
    public int? ActivityTypeId { get; set; }

    /// <summary>
    /// Gets or sets whether to clear the activity type.
    /// </summary>
    public bool? ClearActivity { get; set; }
}

/// <summary>
/// Response from timeline location update.
/// </summary>
public class TimelineUpdateResponse
{
    /// <summary>
    /// Gets or sets the location ID.
    /// </summary>
    public int LocationId { get; set; }

    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

#endregion

#region Public Trips

/// <summary>
/// Summary of a public trip for browsing.
/// </summary>
public class PublicTripSummary
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trip description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the countries covered.
    /// </summary>
    public List<string> Countries { get; set; } = new();

    /// <summary>
    /// Gets or sets the cities covered.
    /// </summary>
    public List<string> Cities { get; set; } = new();

    /// <summary>
    /// Gets or sets the number of places (server sends as placesCount).
    /// </summary>
    [JsonPropertyName("placesCount")]
    public int PlacesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of regions (server sends as regionsCount).
    /// </summary>
    [JsonPropertyName("regionsCount")]
    public int RegionsCount { get; set; }

    /// <summary>
    /// Gets or sets the owner's display name (server sends as ownerDisplayName).
    /// </summary>
    [JsonPropertyName("ownerDisplayName")]
    public string? OwnerDisplayName { get; set; }

    /// <summary>
    /// Gets the author name (alias for OwnerDisplayName for UI binding).
    /// </summary>
    [JsonIgnore]
    public string? AuthorName => OwnerDisplayName;

    /// <summary>
    /// Gets or sets whether the current user owns this trip.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets a stats text showing region and place counts.
    /// Format: "X Regions / Y Places" or "Empty trip".
    /// </summary>
    [JsonIgnore]
    public string StatsText
    {
        get
        {
            if (RegionsCount == 0 && PlacesCount == 0)
                return "Empty trip";

            var parts = new List<string>();
            if (RegionsCount > 0)
                parts.Add($"{RegionsCount} Region{(RegionsCount == 1 ? "" : "s")}");
            if (PlacesCount > 0)
                parts.Add($"{PlacesCount} Place{(PlacesCount == 1 ? "" : "s")}");

            return string.Join(" / ", parts);
        }
    }
}

/// <summary>
/// Paginated response for public trips.
/// </summary>
public class PublicTripsResponse
{
    /// <summary>
    /// Gets or sets the list of trips (server sends as "items").
    /// </summary>
    [JsonPropertyName("items")]
    public List<PublicTripSummary> Trips { get; set; } = new();

    /// <summary>
    /// Gets or sets the current page number (1-based).
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the total number of trips.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Gets whether there are more pages.
    /// </summary>
    [JsonIgnore]
    public bool HasMore => Page < TotalPages;
}

/// <summary>
/// Response from cloning a trip.
/// </summary>
public class CloneTripResponse
{
    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the new trip ID.
    /// </summary>
    public Guid? NewTripId { get; set; }

    /// <summary>
    /// Gets or sets the new trip name.
    /// </summary>
    public string? NewTripName { get; set; }

    /// <summary>
    /// Gets or sets the error message if failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Sort options for public trips.
/// </summary>
public static class PublicTripsSortOptions
{
    /// <summary>Sort by most recently updated.</summary>
    public const string Updated = "updated";

    /// <summary>Sort by creation date (newest first).</summary>
    public const string Newest = "newest";

    /// <summary>Sort by name alphabetically.</summary>
    public const string Name = "name";

    /// <summary>Sort by place count (most places first).</summary>
    public const string Places = "places";
}

#endregion
