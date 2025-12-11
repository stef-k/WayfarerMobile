namespace WayfarerMobile.Core.Models;

/// <summary>
/// Request payload for fetching latest locations.
/// POST /api/mobile/groups/{groupId}/locations/latest
/// </summary>
public class GroupLatestLocationsRequest
{
    /// <summary>
    /// Optional list of user IDs to filter; null = all active members.
    /// </summary>
    public List<string>? IncludeUserIds { get; set; }
}

/// <summary>
/// Request payload for querying historical locations.
/// POST /api/mobile/groups/{groupId}/locations/query
/// </summary>
public class GroupLocationsQueryRequest
{
    /// <summary>Minimum longitude of bounding box.</summary>
    public double MinLng { get; set; }

    /// <summary>Minimum latitude of bounding box.</summary>
    public double MinLat { get; set; }

    /// <summary>Maximum longitude of bounding box.</summary>
    public double MaxLng { get; set; }

    /// <summary>Maximum latitude of bounding box.</summary>
    public double MaxLat { get; set; }

    /// <summary>Zoom level for clustering hints.</summary>
    public double ZoomLevel { get; set; }

    /// <summary>Optional list of user IDs to filter.</summary>
    public List<string>? UserIds { get; set; }

    /// <summary>Date type filter: "day", "month", or "year".</summary>
    public string? DateType { get; set; }

    /// <summary>Year for date filtering.</summary>
    public int? Year { get; set; }

    /// <summary>Month for date filtering (1-12).</summary>
    public int? Month { get; set; }

    /// <summary>Day for date filtering (1-31).</summary>
    public int? Day { get; set; }

    /// <summary>Page size for pagination.</summary>
    public int? PageSize { get; set; }

    /// <summary>Continuation token for pagination.</summary>
    public string? ContinuationToken { get; set; }
}

/// <summary>
/// Response from location query with pagination.
/// </summary>
public class GroupLocationsQueryResponse
{
    /// <summary>Total items matching the query.</summary>
    public int TotalItems { get; set; }

    /// <summary>Number of items returned in this response.</summary>
    public int ReturnedItems { get; set; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; set; }

    /// <summary>Whether more results are available.</summary>
    public bool HasMore { get; set; }

    /// <summary>Token for fetching next page.</summary>
    public string? NextPageToken { get; set; }

    /// <summary>Whether results were truncated.</summary>
    public bool IsTruncated { get; set; }

    /// <summary>Location results.</summary>
    public List<GroupLocationResult> Results { get; set; } = new();
}

/// <summary>
/// Individual location result from query.
/// </summary>
public class GroupLocationResult
{
    /// <summary>Location ID.</summary>
    public int Id { get; set; }

    /// <summary>User ID who logged this location.</summary>
    public string? UserId { get; set; }

    /// <summary>Latitude in degrees.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude in degrees.</summary>
    public double Longitude { get; set; }

    /// <summary>UTC timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Local timestamp in user's timezone.</summary>
    public DateTime LocalTimestamp { get; set; }

    /// <summary>Short address.</summary>
    public string? Address { get; set; }

    /// <summary>Full address.</summary>
    public string? FullAddress { get; set; }

    /// <summary>Whether this is the user's latest location.</summary>
    public bool IsLatestLocation { get; set; }

    /// <summary>Whether the user is currently live/active.</summary>
    public bool IsLive { get; set; }
}

/// <summary>
/// Request to update peer visibility.
/// PATCH /api/mobile/groups/{groupId}/peer-visibility
/// </summary>
public class PeerVisibilityUpdateRequest
{
    /// <summary>Whether peer visibility is disabled.</summary>
    public bool Disabled { get; set; }
}
