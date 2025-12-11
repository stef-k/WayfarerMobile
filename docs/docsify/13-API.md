# API Integration

This document covers backend API integration, including endpoints, authentication, request/response formats, and error handling.

## Overview

WayfarerMobile communicates with a backend server for:
- Location synchronization
- Trip management
- Group location sharing
- User authentication
- Configuration sync

## HTTP Client Configuration

### IHttpClientFactory

The app uses `IHttpClientFactory` for efficient HTTP client management:

```csharp
// In MauiProgram.cs
services.AddHttpClient("WayfarerApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

services.AddHttpClient("Osrm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "WayfarerMobile/1.0");
});
```

### Creating Requests

```csharp
private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
{
    var baseUrl = _settings.ServerUrl?.TrimEnd('/') ?? "";
    var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");

    if (!string.IsNullOrEmpty(_settings.ApiToken))
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
    }

    request.Headers.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));

    return request;
}
```

## Authentication

### Bearer Token

All authenticated requests include a Bearer token in the `Authorization` header:

```http
GET /api/mobile/trips HTTP/1.1
Host: your-server.com
Authorization: Bearer <api-token>
Accept: application/json
```

### Token Storage

The API token is stored in `SecureStorage` for encrypted storage:

```csharp
// Store token
await SecureStorage.Default.SetAsync("api_token", token);

// Retrieve token
var token = await SecureStorage.Default.GetAsync("api_token");
```

### Configuration via QR Code

The app can be configured by scanning a QR code containing:

```json
{
  "serverUrl": "https://your-server.com",
  "apiToken": "your-api-token"
}
```

**Important**: The QR code contains only the server URL and API token - NO user ID. The user's identity is determined by the server based on the API token during authentication. This means mobile app users do not need email accounts; authentication is entirely token-based.

## Polly Retry Policy

The `ApiClient` uses Polly for resilient HTTP operations:

```csharp
_retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
    .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .Handle<TaskCanceledException>(ex =>
                ex.InnerException is TimeoutException)
            .HandleResult(response =>
                TransientStatusCodes.Contains(response.StatusCode))
    })
    .Build();
```

### Transient Status Codes

These HTTP status codes trigger automatic retry:

| Code | Name | Retry |
|------|------|-------|
| 408 | Request Timeout | Yes |
| 429 | Too Many Requests | Yes |
| 500 | Internal Server Error | Yes |
| 502 | Bad Gateway | Yes |
| 503 | Service Unavailable | Yes |
| 504 | Gateway Timeout | Yes |

## API Endpoints

### Location Endpoints

#### Log Location

Logs a single location to the server timeline.

**Endpoint**: `POST /api/location/log-location`

**Request**:
```json
{
  "lat": 51.5074,
  "lon": -0.1278,
  "accuracy": 15.0,
  "altitude": 25.0,
  "speed": 1.5,
  "bearing": 180.0,
  "timestamp": "2025-01-15T14:30:00Z"
}
```

**Response** (Success):
```json
{
  "success": true
}
```

**Response** (Threshold Skipped):
```json
{
  "success": false,
  "error": "threshold skipped"
}
```

#### Batch Log Locations

Logs multiple locations in a single request.

**Endpoint**: `POST /api/location/log-locations`

**Request**:
```json
{
  "locations": [
    {
      "lat": 51.5074,
      "lon": -0.1278,
      "accuracy": 15.0,
      "timestamp": "2025-01-15T14:30:00Z"
    },
    {
      "lat": 51.5080,
      "lon": -0.1285,
      "accuracy": 12.0,
      "timestamp": "2025-01-15T14:35:00Z"
    }
  ]
}
```

**Response**:
```json
{
  "success": true,
  "accepted": 2,
  "rejected": 0
}
```

### Timeline Endpoints

#### Get Timeline for Date

**Endpoint**: `GET /api/mobile/timeline/{date}`

**Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| date | string | ISO date (yyyy-MM-dd) |

**Response**:
```json
{
  "date": "2025-01-15",
  "entries": [
    {
      "id": "entry-1",
      "type": "visit",
      "placeName": "Coffee Shop",
      "startTime": "2025-01-15T08:00:00Z",
      "endTime": "2025-01-15T09:30:00Z",
      "latitude": 51.5074,
      "longitude": -0.1278
    },
    {
      "id": "entry-2",
      "type": "trip",
      "startTime": "2025-01-15T09:30:00Z",
      "endTime": "2025-01-15T10:00:00Z",
      "distanceKm": 3.5,
      "polyline": "encoded_polyline_string"
    }
  ]
}
```

### Trip Endpoints

#### List Trips

**Endpoint**: `GET /api/mobile/trips`

**Response**:
```json
{
  "trips": [
    {
      "id": "trip-1",
      "name": "Europe 2025",
      "startDate": "2025-03-01",
      "endDate": "2025-03-15",
      "placeCount": 12,
      "thumbnailUrl": "https://..."
    }
  ]
}
```

#### Get Trip Details

**Endpoint**: `GET /api/mobile/trips/{tripId}`

**Response**:
```json
{
  "id": "trip-1",
  "name": "Europe 2025",
  "description": "Spring trip to Europe",
  "startDate": "2025-03-01",
  "endDate": "2025-03-15",
  "regions": [
    {
      "id": "region-1",
      "name": "Paris",
      "places": [
        {
          "id": "place-1",
          "name": "Eiffel Tower",
          "latitude": 48.8584,
          "longitude": 2.2945,
          "icon": "landmark",
          "markerColor": "red",
          "notes": "Visit at sunset",
          "sortOrder": 1
        }
      ]
    }
  ],
  "segments": [
    {
      "id": "segment-1",
      "originId": "place-1",
      "destinationId": "place-2",
      "transportMode": "walking",
      "distanceKm": 1.2,
      "durationMinutes": 15,
      "geometry": "encoded_polyline_string"
    }
  ]
}
```

### Group Endpoints

#### List Groups

**Endpoint**: `GET /api/mobile/groups?scope=all`

**Response**:
```json
[
  {
    "id": "group-1",
    "name": "Family",
    "memberCount": 4,
    "isOwner": true
  }
]
```

#### Get Group Members

**Endpoint**: `GET /api/mobile/groups/{groupId}/members`

**Response**:
```json
[
  {
    "userId": "user-1",
    "displayName": "John",
    "email": "john@example.com",
    "colorHex": "#FF5722"
  }
]
```

#### Get Latest Locations

**Endpoint**: `POST /api/mobile/groups/{groupId}/locations/latest`

**Request**:
```json
{
  "includeUserIds": ["user-1", "user-2"]
}
```

**Response**:
```json
[
  {
    "userId": "user-1",
    "coordinates": {
      "x": -0.1278,
      "y": 51.5074
    },
    "localTimestamp": "2025-01-15T14:30:00Z",
    "isLive": true,
    "fullAddress": "London, UK"
  }
]
```

### Configuration Endpoint

#### Get User Configuration

**Endpoint**: `GET /api/mobile/configuration`

**Response**:
```json
{
  "locationTimeThresholdMinutes": 1,
  "locationDistanceThresholdMeters": 50,
  "userId": "user-123",
  "email": "user@example.com"
}
```

## Error Handling

### Error Response Format

```json
{
  "success": false,
  "error": "Error message",
  "errorCode": "ERROR_CODE"
}
```

### Common Error Codes

| Code | Description | Action |
|------|-------------|--------|
| `UNAUTHORIZED` | Invalid or expired token | Re-authenticate |
| `FORBIDDEN` | Insufficient permissions | Check permissions |
| `NOT_FOUND` | Resource not found | Handle gracefully |
| `RATE_LIMITED` | Too many requests | Back off and retry |
| `SERVER_ERROR` | Internal server error | Retry with backoff |

### Client-Side Error Handling

```csharp
public async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request)
{
    try
    {
        var response = await _retryPipeline.ExecuteAsync(
            async ct => await _httpClient.SendAsync(request, ct),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return ApiResult<T>.Failure(response.StatusCode, error);
        }

        var data = await response.Content.ReadFromJsonAsync<T>();
        return ApiResult<T>.Success(data);
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "Network error");
        return ApiResult<T>.NetworkError(ex.Message);
    }
    catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
    {
        _logger.LogWarning("Request timeout");
        return ApiResult<T>.Timeout();
    }
}
```

## OSRM Routing API

The app uses the public OSRM demo server for route calculation when no user-defined segment exists.

### Route Request

**Endpoint**: `GET https://router.project-osrm.org/route/v1/{profile}/{coordinates}`

**Parameters**:
| Parameter | Description |
|-----------|-------------|
| profile | `foot`, `car`, or `bike` |
| coordinates | `{lon1},{lat1};{lon2},{lat2}` |

**Query Parameters**:
| Parameter | Value | Description |
|-----------|-------|-------------|
| overview | `full` | Return full route geometry |
| geometries | `polyline` | Encoded polyline format |
| steps | `false` | Don't return turn-by-turn steps |

**Example**:
```
GET https://router.project-osrm.org/route/v1/foot/-0.1278,51.5074;-0.1300,51.5100?overview=full&geometries=polyline
```

**Response**:
```json
{
  "code": "Ok",
  "routes": [
    {
      "geometry": "encoded_polyline",
      "legs": [
        {
          "distance": 450.5,
          "duration": 324.0
        }
      ],
      "distance": 450.5,
      "duration": 324.0
    }
  ]
}
```

### Rate Limiting

The OSRM demo server has rate limits:
- Maximum 1 request per second
- No API key required

The `OsrmRoutingService` implements rate limiting:

```csharp
private static readonly SemaphoreSlim _rateLimiter = new(1, 1);
private static DateTime _lastRequestTime = DateTime.MinValue;
private const int MinRequestIntervalMs = 1100; // Slightly over 1 second

public async Task<OsrmRouteResult?> GetRouteAsync(...)
{
    await _rateLimiter.WaitAsync();
    try
    {
        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed.TotalMilliseconds < MinRequestIntervalMs)
        {
            await Task.Delay(MinRequestIntervalMs - (int)elapsed.TotalMilliseconds);
        }

        // Make request...
        _lastRequestTime = DateTime.UtcNow;
    }
    finally
    {
        _rateLimiter.Release();
    }
}
```

## JSON Serialization

The app uses System.Text.Json with camelCase naming:

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Serialize
var json = JsonSerializer.Serialize(data, JsonOptions);

// Deserialize
var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
```

## Connectivity Handling

### Network Status Check

```csharp
public bool IsConnected =>
    Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

public void MonitorConnectivity()
{
    Connectivity.Current.ConnectivityChanged += (s, e) =>
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            // Trigger sync
            _locationSyncService.SyncNow();
        }
    };
}
```

### Offline Behavior

When offline:
1. Locations continue to be queued locally
2. API calls fail gracefully with cached data
3. UI shows offline banner
4. Sync resumes automatically when connected

## Next Steps

- [Testing](14-Testing.md) - Testing API integration
- [Security](15-Security.md) - Security considerations for API calls
- [Services](12-Services.md) - ApiClient implementation details
