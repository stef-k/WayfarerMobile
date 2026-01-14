using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Client for communicating with the Wayfarer backend API.
/// </summary>
public interface IApiClient
{
    /// <summary>
    /// Gets whether the client is configured with valid credentials.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Logs a location to the server.
    /// </summary>
    /// <param name="location">The location data to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure with optional message.</returns>
    Task<ApiResult> LogLocationAsync(LocationLogRequest location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a manual check-in (bypasses time/distance thresholds).
    /// </summary>
    /// <param name="location">The location data to check in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure with optional message.</returns>
    Task<ApiResult> CheckInAsync(LocationLogRequest location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the server is reachable and credentials are valid.</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches server settings (thresholds, configuration).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Server settings or null if unavailable.</returns>
    Task<ServerSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets timeline locations for a specific date period.
    /// </summary>
    /// <param name="dateType">Type of period: "day", "month", or "year".</param>
    /// <param name="year">Year to filter.</param>
    /// <param name="month">Month to filter (1-12), required for day/month types.</param>
    /// <param name="day">Day to filter (1-31), required for day type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Timeline response with locations or null if unavailable.</returns>
    Task<TimelineResponse?> GetTimelineLocationsAsync(
        string dateType,
        int year,
        int? month = null,
        int? day = null,
        CancellationToken cancellationToken = default);

    #region Place Operations

    /// <summary>
    /// Creates a new place in a trip.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="request">The place create request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Place response or null on failure.</returns>
    Task<PlaceResponse?> CreatePlaceAsync(
        Guid tripId,
        PlaceCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing place.
    /// </summary>
    /// <param name="placeId">The place ID.</param>
    /// <param name="request">The place update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Place response or null on failure.</returns>
    Task<PlaceResponse?> UpdatePlaceAsync(
        Guid placeId,
        PlaceUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a place.
    /// </summary>
    /// <param name="placeId">The place ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DeletePlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Region Operations

    /// <summary>
    /// Creates a new region in a trip.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="request">The region create request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Region response or null on failure.</returns>
    Task<RegionResponse?> CreateRegionAsync(
        Guid tripId,
        RegionCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing region.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <param name="request">The region update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Region response or null on failure.</returns>
    Task<RegionResponse?> UpdateRegionAsync(
        Guid regionId,
        RegionUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a region.
    /// </summary>
    /// <param name="regionId">The region ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DeleteRegionAsync(
        Guid regionId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Trip Operations

    /// <summary>
    /// Gets the list of trips from the server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of trip summaries.</returns>
    Task<List<TripSummary>> GetTripsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets trip details by ID.
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trip details or null if not found.</returns>
    Task<TripDetails?> GetTripDetailsAsync(Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets public trips with search and pagination.
    /// </summary>
    /// <param name="searchQuery">Optional search query.</param>
    /// <param name="sort">Sort option (updated, newest, name, places).</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated public trips response.</returns>
    Task<PublicTripsResponse?> GetPublicTripsAsync(
        string? searchQuery = null,
        string sort = "updated",
        int page = 1,
        int pageSize = 24,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clones a public trip to the current user's account.
    /// </summary>
    /// <param name="tripId">The trip ID to clone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Clone response with new trip ID.</returns>
    Task<CloneTripResponse?> CloneTripAsync(Guid tripId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing trip's metadata (name, notes).
    /// </summary>
    /// <param name="tripId">The trip ID.</param>
    /// <param name="request">The trip update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Trip update response or null on failure.</returns>
    Task<TripUpdateResponse?> UpdateTripAsync(
        Guid tripId,
        TripUpdateRequest request,
        CancellationToken cancellationToken = default);

    #endregion

    #region Segment Operations

    /// <summary>
    /// Updates a segment's notes.
    /// </summary>
    /// <param name="segmentId">The segment ID.</param>
    /// <param name="request">The segment notes update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Segment update response or null on failure.</returns>
    Task<SegmentUpdateResponse?> UpdateSegmentNotesAsync(
        Guid segmentId,
        SegmentNotesUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an area's (polygon) notes.
    /// </summary>
    /// <param name="areaId">The area ID.</param>
    /// <param name="request">The area notes update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Area update response or null on failure.</returns>
    Task<AreaUpdateResponse?> UpdateAreaNotesAsync(
        Guid areaId,
        AreaNotesUpdateRequest request,
        CancellationToken cancellationToken = default);

    #endregion

    #region Visit Operations

    /// <summary>
    /// Gets recent visits from the server (for background polling when SSE is unavailable).
    /// </summary>
    /// <param name="sinceSeconds">Number of seconds to look back for visits (default 30).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of recent visit events, or empty list on failure.</returns>
    Task<List<SseVisitStartedEvent>> GetRecentVisitsAsync(
        int sinceSeconds = 30,
        CancellationToken cancellationToken = default);

    #endregion

    #region Timeline Operations

    /// <summary>
    /// Updates a timeline location.
    /// </summary>
    /// <param name="locationId">The location ID.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update response or null on failure.</returns>
    Task<TimelineUpdateResponse?> UpdateTimelineLocationAsync(
        int locationId,
        TimelineLocationUpdateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a timeline location.
    /// </summary>
    /// <param name="locationId">The location ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> DeleteTimelineLocationAsync(
        int locationId,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Result from an API operation.
/// </summary>
public class ApiResult
{
    /// <summary>
    /// Gets or sets whether the operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the response message.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets whether the location was skipped due to thresholds.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code if available.
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// Gets or sets the server-assigned location ID.
    /// Populated when server stores the location (not set when skipped).
    /// </summary>
    public int? LocationId { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static ApiResult Ok(string? message = null) =>
        new() { Success = true, Message = message };

    /// <summary>
    /// Creates a skipped result (thresholds not met).
    /// </summary>
    public static ApiResult SkippedResult(string? message = null) =>
        new() { Success = true, Skipped = true, Message = message };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    public static ApiResult Fail(string? message, int? statusCode = null) =>
        new() { Success = false, Message = message, StatusCode = statusCode };
}

/// <summary>
/// Server settings from the API (location thresholds, configuration).
/// </summary>
public class ServerSettings
{
    /// <summary>
    /// Gets or sets the minimum time threshold between logged locations (minutes).
    /// </summary>
    public int LocationTimeThresholdMinutes { get; set; }

    /// <summary>
    /// Gets or sets the minimum distance threshold between logged locations (meters).
    /// </summary>
    public int LocationDistanceThresholdMeters { get; set; }

    /// <summary>
    /// Gets or sets the maximum acceptable GPS accuracy for location logging (meters).
    /// Locations with accuracy worse (higher) than this value are rejected.
    /// </summary>
    public int LocationAccuracyThresholdMeters { get; set; }

    /// <summary>
    /// Gets or sets the user's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user's email.
    /// </summary>
    public string? Email { get; set; }
}
