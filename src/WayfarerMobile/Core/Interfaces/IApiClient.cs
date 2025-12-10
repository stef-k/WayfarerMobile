using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;

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
    /// <param name="location">The location to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure with optional message.</returns>
    Task<ApiResult> LogLocationAsync(QueuedLocation location, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a manual check-in (bypasses time/distance thresholds).
    /// </summary>
    /// <param name="location">The location to check in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure with optional message.</returns>
    Task<ApiResult> CheckInAsync(QueuedLocation location, CancellationToken cancellationToken = default);

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
    /// Gets or sets the user's display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user's email.
    /// </summary>
    public string? Email { get; set; }
}
