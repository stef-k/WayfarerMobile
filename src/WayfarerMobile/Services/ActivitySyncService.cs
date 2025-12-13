using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SQLite;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Data.Entities;
using WayfarerMobile.Data.Services;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for managing activity types with server sync and local caching.
/// </summary>
public class ActivitySyncService : IActivitySyncService
{
    #region Private Types

    /// <summary>
    /// Response model for activity types from server API.
    /// </summary>
    private class ActivityTypeResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    #endregion

    #region Constants

    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Fields

    private readonly ILogger<ActivitySyncService> _logger;
    private readonly DatabaseService _databaseService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    #endregion

    /// <summary>
    /// Creates a new instance of ActivitySyncService.
    /// </summary>
    public ActivitySyncService(
        ILogger<ActivitySyncService> logger,
        DatabaseService databaseService,
        IHttpClientFactory httpClientFactory,
        ISettingsService settingsService)
    {
        _logger = logger;
        _databaseService = databaseService;
        _httpClientFactory = httpClientFactory;
        _settingsService = settingsService;
    }

    #region Initialization

    /// <summary>
    /// Ensures the activity types table is initialized with defaults.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            var db = await _databaseService.GetConnectionAsync();
            await db.CreateTableAsync<ActivityType>();

            // Seed defaults if no activities exist
            var count = await db.Table<ActivityType>().CountAsync();
            if (count == 0)
            {
                await SeedDefaultActivitiesAsync(db);
                _logger.LogInformation("Seeded default activity types");
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Seeds default activity types for offline use.
    /// Uses negative IDs to avoid conflicts with server activities.
    /// </summary>
    private static async Task SeedDefaultActivitiesAsync(SQLiteAsyncConnection db)
    {
        var defaults = new[]
        {
            new ActivityType { Id = -1, Name = "Walking", Description = "Taking a walk", IconName = "walk" },
            new ActivityType { Id = -2, Name = "Running", Description = "Running outdoors", IconName = "run" },
            new ActivityType { Id = -3, Name = "Cycling", Description = "Riding a bicycle", IconName = "bike" },
            new ActivityType { Id = -4, Name = "Travel", Description = "Traveling by vehicle", IconName = "car" },
            new ActivityType { Id = -5, Name = "Eating", Description = "Having a meal", IconName = "eat" },
            new ActivityType { Id = -6, Name = "Drinking", Description = "Having drinks", IconName = "drink" },
            new ActivityType { Id = -7, Name = "At Work", Description = "Working at location", IconName = "marker" },
            new ActivityType { Id = -8, Name = "Meeting", Description = "Attending a meeting", IconName = "flag" },
            new ActivityType { Id = -9, Name = "Shopping", Description = "Shopping for items", IconName = "shopping" },
            new ActivityType { Id = -10, Name = "Pharmacy", Description = "Visiting pharmacy", IconName = "pharmacy" },
            new ActivityType { Id = -11, Name = "ATM", Description = "Using ATM or banking", IconName = "atm" },
            new ActivityType { Id = -12, Name = "Fitness", Description = "Physical exercise", IconName = "fitness" },
            new ActivityType { Id = -13, Name = "Doctor", Description = "Medical appointment", IconName = "hospital" },
            new ActivityType { Id = -14, Name = "Hotel", Description = "Checking into accommodation", IconName = "hotel" },
            new ActivityType { Id = -15, Name = "Airport", Description = "At airport for travel", IconName = "flight" },
            new ActivityType { Id = -16, Name = "Gas Station", Description = "Getting fuel", IconName = "gas" },
            new ActivityType { Id = -17, Name = "Park", Description = "Visiting a park", IconName = "park" },
            new ActivityType { Id = -18, Name = "Museum", Description = "Visiting museum", IconName = "museum" },
            new ActivityType { Id = -19, Name = "Photography", Description = "Taking photographs", IconName = "camera" },
            new ActivityType { Id = -20, Name = "General", Description = "General check-in", IconName = "marker" }
        };

        foreach (var activity in defaults)
        {
            await db.InsertAsync(activity);
        }
    }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public async Task<List<ActivityType>> GetActivityTypesAsync()
    {
        try
        {
            await EnsureInitializedAsync();

            var db = await _databaseService.GetConnectionAsync();
            var all = await db.Table<ActivityType>()
                .OrderBy(a => a.Name)
                .ToListAsync();

            // Return server activities if available, otherwise defaults
            var serverActivities = all.Where(a => a.Id > 0).ToList();
            return serverActivities.Count > 0 ? serverActivities : all.Where(a => a.Id < 0).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting activity types");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<ActivityType?> GetActivityByIdAsync(int id)
    {
        try
        {
            await EnsureInitializedAsync();

            var db = await _databaseService.GetConnectionAsync();
            return await db.Table<ActivityType>()
                .Where(a => a.Id == id)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting activity by ID {Id}", id);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SyncWithServerAsync()
    {
        try
        {
            await EnsureInitializedAsync();

            if (!_settingsService.IsConfigured)
            {
                _logger.LogDebug("API not configured, skipping activity sync");
                return false;
            }

            // Fetch from server using HttpClient
            var client = _httpClientFactory.CreateClient("WayfarerApi");
            var baseUrl = _settingsService.ServerUrl?.TrimEnd('/') ?? "";
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/activity");

            if (!string.IsNullOrEmpty(_settingsService.ApiToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settingsService.ApiToken);
            }

            var httpResponse = await client.SendAsync(request);
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch activities: {StatusCode}", httpResponse.StatusCode);
                return false;
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<ActivityTypeResponse[]>(JsonOptions);
            if (response == null || response.Length == 0)
            {
                _logger.LogWarning("No activities received from server");
                return false;
            }

            var db = await _databaseService.GetConnectionAsync();
            var syncTime = DateTime.UtcNow;

            // Clear existing server activities
            await db.ExecuteAsync("DELETE FROM ActivityTypes WHERE Id > 0");

            // Insert new server activities
            var inserted = 0;
            foreach (var serverActivity in response.Where(a => a.Id > 0 && !string.IsNullOrWhiteSpace(a.Name)))
            {
                var local = new ActivityType
                {
                    Id = serverActivity.Id,
                    Name = serverActivity.Name,
                    Description = serverActivity.Description,
                    IconName = SuggestIconForActivity(serverActivity.Name),
                    LastSyncAt = syncTime
                };

                await db.InsertOrReplaceAsync(local);
                inserted++;
            }

            _logger.LogInformation("Synced {Count} activity types from server", inserted);
            return inserted > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing activities from server");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> AutoSyncIfNeededAsync()
    {
        try
        {
            if (!_settingsService.IsConfigured)
            {
                return false;
            }

            await EnsureInitializedAsync();

            var db = await _databaseService.GetConnectionAsync();

            // Check if we have server activities and when they were last synced
            var lastServerActivity = await db.Table<ActivityType>()
                .Where(a => a.Id > 0)
                .OrderByDescending(a => a.LastSyncAt)
                .FirstOrDefaultAsync();

            if (lastServerActivity == null)
            {
                // No server activities, trigger initial sync
                return await SyncWithServerAsync();
            }

            // Check if sync is needed
            var timeSinceSync = DateTime.UtcNow - lastServerActivity.LastSyncAt;
            if (timeSinceSync > SyncInterval)
            {
                return await SyncWithServerAsync();
            }

            return true; // No sync needed, data is fresh
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto sync check");
            return false;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Suggests an appropriate icon based on activity name.
    /// </summary>
    private static string SuggestIconForActivity(string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
            return "marker";

        var name = activityName.ToLowerInvariant();

        return name switch
        {
            _ when name.Contains("walk") => "walk",
            _ when name.Contains("run") => "run",
            _ when name.Contains("bike") || name.Contains("cycl") => "bike",
            _ when name.Contains("car") || name.Contains("drive") || name.Contains("travel") => "car",
            _ when name.Contains("eat") || name.Contains("food") || name.Contains("meal") || name.Contains("restaurant") => "eat",
            _ when name.Contains("drink") || name.Contains("coffee") || name.Contains("bar") => "drink",
            _ when name.Contains("shop") || name.Contains("store") || name.Contains("mall") => "shopping",
            _ when name.Contains("gym") || name.Contains("fitness") || name.Contains("exercise") => "fitness",
            _ when name.Contains("hospital") || name.Contains("doctor") || name.Contains("medical") || name.Contains("clinic") => "hospital",
            _ when name.Contains("hotel") || name.Contains("accommodation") || name.Contains("lodge") => "hotel",
            _ when name.Contains("airport") || name.Contains("flight") || name.Contains("plane") => "flight",
            _ when name.Contains("gas") || name.Contains("fuel") || name.Contains("petrol") => "gas",
            _ when name.Contains("park") || name.Contains("outdoor") || name.Contains("nature") => "park",
            _ when name.Contains("museum") || name.Contains("gallery") || name.Contains("exhibition") => "museum",
            _ when name.Contains("photo") || name.Contains("camera") || name.Contains("picture") => "camera",
            _ when name.Contains("atm") || name.Contains("bank") => "atm",
            _ when name.Contains("pharmacy") || name.Contains("drug") || name.Contains("medicine") => "pharmacy",
            _ when name.Contains("train") || name.Contains("railway") => "train",
            _ => "marker"
        };
    }

    #endregion
}
