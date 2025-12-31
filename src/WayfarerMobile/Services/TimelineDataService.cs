using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>
/// Abstraction layer for timeline data access with offline fallback.
/// Provides access to local timeline entries and handles server enrichment.
/// </summary>
/// <remarks>
/// <para>
/// This service acts as the data source for timeline viewing.
/// It reads from local storage first (offline-first) and enriches
/// from server when online.
/// </para>
/// </remarks>
public class TimelineDataService
{
    private readonly DatabaseService _databaseService;
    private readonly IApiClient _apiClient;
    private readonly ILogger<TimelineDataService> _logger;

    /// <summary>
    /// Creates a new instance of TimelineDataService.
    /// </summary>
    public TimelineDataService(
        DatabaseService databaseService,
        IApiClient apiClient,
        ILogger<TimelineDataService> logger)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets timeline entries for a specific date.
    /// Returns local entries (offline-first approach).
    /// Call <see cref="EnrichFromServerAsync"/> separately if online enrichment is needed.
    /// </summary>
    /// <param name="date">The date to retrieve entries for.</param>
    /// <returns>List of local timeline entries for the date, ordered by timestamp descending.</returns>
    public async Task<List<LocalTimelineEntry>> GetEntriesForDateAsync(DateTime date)
    {
        try
        {
            var entries = await _databaseService.GetLocalTimelineEntriesForDateAsync(date);
            _logger.LogDebug("Retrieved {Count} local entries for {Date:yyyy-MM-dd}", entries.Count, date);
            return entries;
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting local entries for {Date:yyyy-MM-dd}", date);
            return new List<LocalTimelineEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting local entries for {Date:yyyy-MM-dd}", date);
            return new List<LocalTimelineEntry>();
        }
    }

    /// <summary>
    /// Gets timeline entries within a date range.
    /// Returns local entries (offline-first approach).
    /// </summary>
    /// <param name="fromDate">Start date (inclusive).</param>
    /// <param name="toDate">End date (inclusive).</param>
    /// <returns>List of local timeline entries in the range.</returns>
    public async Task<List<LocalTimelineEntry>> GetEntriesInRangeAsync(DateTime fromDate, DateTime toDate)
    {
        try
        {
            var entries = await _databaseService.GetLocalTimelineEntriesInRangeAsync(fromDate, toDate);
            _logger.LogDebug(
                "Retrieved {Count} local entries for range {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
                entries.Count, fromDate, toDate);
            return entries;
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting local entries for range");
            return new List<LocalTimelineEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting local entries for range");
            return new List<LocalTimelineEntry>();
        }
    }

    /// <summary>
    /// Gets all timeline entries for export.
    /// </summary>
    /// <returns>All local timeline entries ordered by timestamp descending.</returns>
    public async Task<List<LocalTimelineEntry>> GetAllEntriesAsync()
    {
        try
        {
            return await _databaseService.GetAllLocalTimelineEntriesAsync();
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting all local entries");
            return new List<LocalTimelineEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting all local entries");
            return new List<LocalTimelineEntry>();
        }
    }

    /// <summary>
    /// Checks if online and enrichment should be attempted.
    /// </summary>
    public bool IsOnline =>
        (Connectivity.Current.NetworkAccess == NetworkAccess.Internet ||
         Connectivity.Current.NetworkAccess == NetworkAccess.ConstrainedInternet) &&
        _apiClient.IsConfigured;

    /// <summary>
    /// Enriches local entries from server data for a specific date.
    /// Fetches server timeline and merges enrichment data (addresses, activities, etc.)
    /// into local entries. Also inserts any server entries not found locally.
    /// </summary>
    /// <param name="date">The date to enrich.</param>
    /// <returns>True if enrichment was performed successfully.</returns>
    public async Task<bool> EnrichFromServerAsync(DateTime date)
    {
        if (!IsOnline)
        {
            _logger.LogDebug("Skipping enrichment - offline or not configured");
            return false;
        }

        try
        {
            // Fetch from server
            var serverData = await _apiClient.GetTimelineLocationsAsync(
                "day",
                date.Year,
                date.Month,
                date.Day);

            if (serverData?.Data == null || serverData.Data.Count == 0)
            {
                _logger.LogDebug("No server data for {Date:yyyy-MM-dd}", date);
                return true; // Not an error, just no data
            }

            _logger.LogDebug(
                "Fetched {Count} server entries for {Date:yyyy-MM-dd}",
                serverData.Data.Count, date);

            // Load existing local entries for this date
            var localEntries = await _databaseService.GetLocalTimelineEntriesForDateAsync(date);
            var localByServerId = localEntries
                .Where(e => e.ServerId.HasValue)
                .ToDictionary(e => e.ServerId!.Value);

            var updatedCount = 0;
            var insertedCount = 0;

            foreach (var serverLocation in serverData.Data)
            {
                if (localByServerId.TryGetValue(serverLocation.Id, out var existing))
                {
                    // EXISTS locally - update enrichment fields
                    existing.Address = serverLocation.Address;
                    existing.FullAddress = serverLocation.FullAddress;
                    existing.Place = serverLocation.Place;
                    existing.Region = serverLocation.Region;
                    existing.Country = serverLocation.Country;
                    existing.PostCode = serverLocation.PostCode;
                    existing.ActivityType = serverLocation.ActivityType;
                    existing.Timezone = serverLocation.Timezone;

                    // Preserve local Notes if user edited offline
                    if (string.IsNullOrEmpty(existing.Notes))
                        existing.Notes = serverLocation.Notes;

                    existing.LastEnrichedAt = DateTime.UtcNow;

                    await _databaseService.UpdateLocalTimelineEntryAsync(existing);
                    updatedCount++;
                }
                else
                {
                    // NOT found locally - insert (manual web entry, other device, historical)
                    var newEntry = new LocalTimelineEntry
                    {
                        ServerId = serverLocation.Id,
                        Latitude = serverLocation.Latitude,
                        Longitude = serverLocation.Longitude,
                        Timestamp = serverLocation.Timestamp,
                        Accuracy = serverLocation.Accuracy,
                        Altitude = serverLocation.Altitude,
                        Speed = serverLocation.Speed,
                        Address = serverLocation.Address,
                        FullAddress = serverLocation.FullAddress,
                        Place = serverLocation.Place,
                        Region = serverLocation.Region,
                        Country = serverLocation.Country,
                        PostCode = serverLocation.PostCode,
                        ActivityType = serverLocation.ActivityType,
                        Notes = serverLocation.Notes,
                        Timezone = serverLocation.Timezone,
                        CreatedAt = DateTime.UtcNow,
                        LastEnrichedAt = DateTime.UtcNow
                    };

                    await _databaseService.InsertLocalTimelineEntryAsync(newEntry);
                    insertedCount++;
                }
            }

            _logger.LogDebug(
                "Enrichment complete for {Date:yyyy-MM-dd}: {Updated} updated, {Inserted} inserted",
                date, updatedCount, insertedCount);

            return true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error enriching from server for {Date:yyyy-MM-dd}", date);
            return false;
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "Request timed out enriching from server for {Date:yyyy-MM-dd}", date);
            return false;
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error enriching from server for {Date:yyyy-MM-dd}", date);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error enriching from server for {Date:yyyy-MM-dd}", date);
            return false;
        }
    }

    /// <summary>
    /// Converts a local timeline entry to a TimelineLocation for display compatibility.
    /// </summary>
    /// <param name="entry">The local entry to convert.</param>
    /// <returns>A TimelineLocation compatible with existing display logic.</returns>
    public static TimelineLocation ToTimelineLocation(LocalTimelineEntry entry)
    {
        return new TimelineLocation
        {
            Id = entry.ServerId ?? entry.Id,
            Timestamp = entry.Timestamp,
            LocalTimestamp = ConvertToLocalTime(entry.Timestamp, entry.Timezone),
            Coordinates = new TimelineCoordinates { X = entry.Longitude, Y = entry.Latitude },
            Timezone = entry.Timezone,
            Accuracy = entry.Accuracy,
            Altitude = entry.Altitude,
            Speed = entry.Speed,
            ActivityType = entry.ActivityType,
            Address = entry.Address,
            FullAddress = entry.FullAddress,
            Place = entry.Place,
            Region = entry.Region,
            Country = entry.Country,
            PostCode = entry.PostCode,
            Notes = entry.Notes
        };
    }

    /// <summary>
    /// Converts a list of local entries to TimelineLocation objects.
    /// </summary>
    public static List<TimelineLocation> ToTimelineLocations(IEnumerable<LocalTimelineEntry> entries)
    {
        return entries.Select(ToTimelineLocation).ToList();
    }

    /// <summary>
    /// Converts UTC timestamp to local time using timezone identifier.
    /// </summary>
    private static DateTime ConvertToLocalTime(DateTime utcTimestamp, string? timezoneId)
    {
        if (string.IsNullOrEmpty(timezoneId))
            return utcTimestamp.ToLocalTime();

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, tz);
        }
        catch
        {
            // Fallback to local time if timezone lookup fails
            return utcTimestamp.ToLocalTime();
        }
    }

    /// <summary>
    /// Gets the total count of stored timeline entries.
    /// </summary>
    public async Task<int> GetEntryCountAsync()
    {
        try
        {
            return await _databaseService.GetLocalTimelineEntryCountAsync();
        }
        catch (SQLiteException ex)
        {
            _logger.LogError(ex, "Database error getting entry count");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting entry count");
            return 0;
        }
    }
}
