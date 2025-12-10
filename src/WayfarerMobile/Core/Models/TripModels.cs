using System.Text.Json.Serialization;

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
    /// Gets a display string for locations.
    /// </summary>
    [JsonIgnore]
    public string LocationsText
    {
        get
        {
            var parts = new List<string>();
            if (Cities.Any()) parts.Add(string.Join(", ", Cities.Take(3)));
            if (Countries.Any()) parts.Add(string.Join(", ", Countries.Take(2)));
            return parts.Any() ? string.Join(" • ", parts) : "No location info";
        }
    }
}

/// <summary>
/// Full trip details with places and segments.
/// </summary>
public class TripDetails
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
    /// Gets or sets the trip notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

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
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets all places from all regions.
    /// </summary>
    [JsonIgnore]
    public List<TripPlace> AllPlaces =>
        Regions.SelectMany(r => r.Places).ToList();
}

/// <summary>
/// Geographic bounding box.
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// Gets or sets the north latitude.
    /// </summary>
    public double North { get; set; }

    /// <summary>
    /// Gets or sets the south latitude.
    /// </summary>
    public double South { get; set; }

    /// <summary>
    /// Gets or sets the east longitude.
    /// </summary>
    public double East { get; set; }

    /// <summary>
    /// Gets or sets the west longitude.
    /// </summary>
    public double West { get; set; }
}

/// <summary>
/// Trip region containing places.
/// </summary>
public class TripRegion
{
    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the region name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the places in this region.
    /// </summary>
    public List<TripPlace> Places { get; set; } = new();

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Place within a trip.
/// </summary>
public class TripPlace
{
    /// <summary>
    /// Gets or sets the place ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the place notes (HTML).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the sort order.
    /// </summary>
    public int SortOrder { get; set; }
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
    /// </summary>
    public Guid OriginId { get; set; }

    /// <summary>
    /// Gets or sets the destination place ID.
    /// </summary>
    public Guid DestinationId { get; set; }

    /// <summary>
    /// Gets or sets the transportation mode.
    /// </summary>
    public string? TransportMode { get; set; }

    /// <summary>
    /// Gets or sets the distance in kilometers.
    /// </summary>
    public double? DistanceKm { get; set; }

    /// <summary>
    /// Gets or sets the duration in minutes.
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the route geometry (encoded polyline).
    /// </summary>
    public string? Geometry { get; set; }
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
    public Guid TripId { get; set; }

    /// <summary>
    /// Gets or sets the trip name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bounding box.
    /// </summary>
    public BoundingBox BoundingBox { get; set; } = new();
}

/// <summary>
/// Tile coordinate for download.
/// </summary>
public class TileCoordinate
{
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public int Zoom { get; set; }

    /// <summary>
    /// Gets or sets the X coordinate.
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate.
    /// </summary>
    public int Y { get; set; }

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
/// </summary>
public class TimelineCoordinates
{
    /// <summary>
    /// Gets or sets the X coordinate (longitude).
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate (latitude).
    /// </summary>
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

#region CRUD Request/Response Models

/// <summary>
/// Request to create a new place.
/// </summary>
public class PlaceCreateRequest
{
    /// <summary>
    /// Gets or sets the region ID (optional).
    /// </summary>
    public Guid? RegionId { get; set; }

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    public string? MarkerColor { get; set; }
}

/// <summary>
/// Request to update a place.
/// </summary>
public class PlaceUpdateRequest
{
    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Gets or sets the notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Gets or sets the marker color.
    /// </summary>
    public string? MarkerColor { get; set; }

    /// <summary>
    /// Gets or sets the region ID.
    /// </summary>
    public Guid? RegionId { get; set; }
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
    /// Gets or sets the sort order.
    /// </summary>
    public int? SortOrder { get; set; }
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
    /// Gets or sets the sort order.
    /// </summary>
    public int? SortOrder { get; set; }
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
    /// Gets or sets the number of places.
    /// </summary>
    public int PlaceCount { get; set; }

    /// <summary>
    /// Gets or sets the owner's display name.
    /// </summary>
    public string? OwnerName { get; set; }

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
    /// Gets a display string for locations.
    /// </summary>
    [JsonIgnore]
    public string LocationsText
    {
        get
        {
            var parts = new List<string>();
            if (Cities.Any()) parts.Add(string.Join(", ", Cities.Take(3)));
            if (Countries.Any()) parts.Add(string.Join(", ", Countries.Take(2)));
            return parts.Any() ? string.Join(" • ", parts) : "No location info";
        }
    }

    /// <summary>
    /// Gets a short summary text.
    /// </summary>
    [JsonIgnore]
    public string SummaryText =>
        PlaceCount > 0 ? $"{PlaceCount} place{(PlaceCount == 1 ? "" : "s")}" : "Empty trip";
}

/// <summary>
/// Paginated response for public trips.
/// </summary>
public class PublicTripsResponse
{
    /// <summary>
    /// Gets or sets the list of trips.
    /// </summary>
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
