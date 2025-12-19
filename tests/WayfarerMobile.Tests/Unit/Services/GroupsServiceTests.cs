using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq.Protected;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for GroupsService covering JSON parsing and API interactions.
/// </summary>
public class GroupsServiceTests
{
    private readonly Mock<ISettingsService> _mockSettings;
    private readonly Mock<ILogger<TestGroupsService>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;

    public GroupsServiceTests()
    {
        _mockSettings = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<TestGroupsService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();

        // Default to configured state
        _mockSettings.Setup(s => s.IsConfigured).Returns(true);
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.example.com");
        _mockSettings.Setup(s => s.ApiToken).Returns("test-token-123");

        var httpClient = new HttpClient(_mockHttpHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient("WayfarerApi")).Returns(httpClient);
    }

    private TestGroupsService CreateService()
    {
        return new TestGroupsService(
            _mockSettings.Object,
            _mockLogger.Object,
            _mockHttpClientFactory.Object);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullSettings_ThrowsArgumentNullException()
    {
        var act = () => new TestGroupsService(null!, _mockLogger.Object, _mockHttpClientFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new TestGroupsService(_mockSettings.Object, null!, _mockHttpClientFactory.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_ThrowsArgumentNullException()
    {
        var act = () => new TestGroupsService(_mockSettings.Object, _mockLogger.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ValidDependencies_CreatesInstance()
    {
        var service = CreateService();
        service.Should().NotBeNull();
    }

    #endregion

    #region ParseLatestLocationsResponse Tests

    [Fact]
    public async Task GetLatestLocationsAsync_ParsesDirectLatLongFormat()
    {
        // Arrange - response with direct latitude/longitude properties
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.7128,
                ""longitude"": -74.0060,
                ""localTimestamp"": ""2025-12-17T10:30:00Z"",
                ""isLive"": true,
                ""fullAddress"": ""123 Main St, New York, NY""
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().ContainKey("user-1");
        var location = result["user-1"];
        location.Latitude.Should().Be(40.7128);
        location.Longitude.Should().Be(-74.0060);
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ParsesCoordinatesFormat()
    {
        // Arrange - response with coordinates.x/y format
        var responseJson = @"[
            {
                ""userId"": ""user-2"",
                ""coordinates"": {
                    ""x"": -122.4194,
                    ""y"": 37.7749
                },
                ""timestampUtc"": ""2025-12-17T15:00:00Z"",
                ""isLive"": false
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().ContainKey("user-2");
        var location = result["user-2"];
        location.Latitude.Should().Be(37.7749);  // y = latitude
        location.Longitude.Should().Be(-122.4194);  // x = longitude
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ExtractsTimestamp_FromLocalTimestamp()
    {
        // Arrange
        var expectedTimestamp = new DateTime(2025, 12, 17, 10, 30, 0, DateTimeKind.Utc);
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""localTimestamp"": ""2025-12-17T10:30:00Z""
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result["user-1"].Timestamp.Should().Be(expectedTimestamp);
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ExtractsTimestamp_FromTimestampUtc()
    {
        // Arrange - using timestampUtc when localTimestamp is absent
        var expectedTimestamp = new DateTime(2025, 12, 17, 15, 0, 0, DateTimeKind.Utc);
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""timestampUtc"": ""2025-12-17T15:00:00Z""
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result["user-1"].Timestamp.Should().Be(expectedTimestamp);
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ExtractsIsLiveFlag()
    {
        // Arrange
        var responseJson = @"[
            {
                ""userId"": ""live-user"",
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""isLive"": true
            },
            {
                ""userId"": ""inactive-user"",
                ""latitude"": 41.0,
                ""longitude"": -75.0,
                ""isLive"": false
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result["live-user"].IsLive.Should().BeTrue();
        result["inactive-user"].IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ExtractsAddress()
    {
        // Arrange
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""fullAddress"": ""456 Oak Ave, Brooklyn, NY 11201""
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result["user-1"].Address.Should().Be("456 Oak Ave, Brooklyn, NY 11201");
    }

    [Fact]
    public async Task GetLatestLocationsAsync_HandlesEmptyArray()
    {
        // Arrange
        var responseJson = "[]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestLocationsAsync_HandlesMalformedJson()
    {
        // Arrange - malformed JSON should return empty result
        var responseJson = "{ invalid json [";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert - should return empty dict, not throw
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestLocationsAsync_SkipsEntriesWithoutUserId()
    {
        // Arrange - entries missing userId should be skipped
        var responseJson = @"[
            {
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""isLive"": true
            },
            {
                ""userId"": """",
                ""latitude"": 41.0,
                ""longitude"": -75.0
            },
            {
                ""userId"": ""valid-user"",
                ""latitude"": 42.0,
                ""longitude"": -76.0
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("valid-user");
    }

    [Fact]
    public async Task GetLatestLocationsAsync_PrefersDirectLatLong_OverCoordinates()
    {
        // Arrange - when both formats exist, direct lat/long should take precedence
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.0,
                ""longitude"": -74.0,
                ""coordinates"": {
                    ""x"": -122.0,
                    ""y"": 37.0
                }
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result["user-1"].Latitude.Should().Be(40.0);
        result["user-1"].Longitude.Should().Be(-74.0);
    }

    #endregion

    #region GetGroupsAsync Tests

    [Fact]
    public async Task GetGroupsAsync_ReturnsEmptyList_WhenNotConfigured()
    {
        // Arrange
        _mockSettings.Setup(s => s.IsConfigured).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.GetGroupsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupsAsync_ReturnsEmptyList_OnHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "");
        var service = CreateService();

        // Act
        var result = await service.GetGroupsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupsAsync_ReturnsGroups_OnSuccess()
    {
        // Arrange
        var responseJson = @"[
            {
                ""id"": ""11111111-1111-1111-1111-111111111111"",
                ""name"": ""Test Group"",
                ""memberCount"": 5
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetGroupsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Test Group");
    }

    #endregion

    #region GetGroupMembersAsync Tests

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsEmptyList_WhenNotConfigured()
    {
        // Arrange
        _mockSettings.Setup(s => s.IsConfigured).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.GetGroupMembersAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsEmptyList_OnHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "");
        var service = CreateService();

        // Act
        var result = await service.GetGroupMembersAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupMembersAsync_ReturnsMembers_OnSuccess()
    {
        // Arrange
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""userName"": ""testuser"",
                ""displayName"": ""Test User"",
                ""groupRole"": ""Member"",
                ""status"": ""Active""
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetGroupMembersAsync(Guid.NewGuid());

        // Assert
        result.Should().HaveCount(1);
        result[0].UserName.Should().Be("testuser");
        result[0].DisplayName.Should().Be("Test User");
    }

    #endregion

    #region GetLatestLocationsAsync API Tests

    [Fact]
    public async Task GetLatestLocationsAsync_ReturnsEmptyDict_WhenNotConfigured()
    {
        // Arrange
        _mockSettings.Setup(s => s.IsConfigured).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ReturnsEmptyDict_OnHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Unauthorized, "");
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region QueryLocationsAsync Tests

    [Fact]
    public async Task QueryLocationsAsync_ThrowsForNullRequest()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.QueryLocationsAsync(Guid.NewGuid(), null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task QueryLocationsAsync_ReturnsNull_WhenNotConfigured()
    {
        // Arrange
        _mockSettings.Setup(s => s.IsConfigured).Returns(false);
        var service = CreateService();
        var request = new GroupLocationsQueryRequest
        {
            MinLat = 40.0,
            MinLng = -74.0,
            MaxLat = 41.0,
            MaxLng = -73.0,
            ZoomLevel = 10
        };

        // Act
        var result = await service.QueryLocationsAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryLocationsAsync_ReturnsNull_OnHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.BadRequest, "");
        var service = CreateService();
        var request = new GroupLocationsQueryRequest
        {
            MinLat = 40.0,
            MinLng = -74.0,
            MaxLat = 41.0,
            MaxLng = -73.0,
            ZoomLevel = 10
        };

        // Act
        var result = await service.QueryLocationsAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryLocationsAsync_ReturnsResponse_OnSuccess()
    {
        // Arrange
        var responseJson = @"{
            ""totalItems"": 100,
            ""returnedItems"": 50,
            ""pageSize"": 50,
            ""hasMore"": true,
            ""nextPageToken"": ""abc123"",
            ""isTruncated"": false,
            ""results"": [
                {
                    ""id"": 1,
                    ""userId"": ""user-1"",
                    ""latitude"": 40.7128,
                    ""longitude"": -74.0060,
                    ""timestamp"": ""2025-12-17T10:00:00Z"",
                    ""localTimestamp"": ""2025-12-17T05:00:00-05:00"",
                    ""isLive"": true
                }
            ]
        }";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();
        var request = new GroupLocationsQueryRequest
        {
            MinLat = 40.0,
            MinLng = -74.0,
            MaxLat = 41.0,
            MaxLng = -73.0,
            ZoomLevel = 10
        };

        // Act
        var result = await service.QueryLocationsAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().NotBeNull();
        result!.TotalItems.Should().Be(100);
        result.ReturnedItems.Should().Be(50);
        result.HasMore.Should().BeTrue();
        result.NextPageToken.Should().Be("abc123");
        result.Results.Should().HaveCount(1);
    }

    #endregion

    #region UpdatePeerVisibilityAsync Tests

    [Fact]
    public async Task UpdatePeerVisibilityAsync_ReturnsFalse_WhenNotConfigured()
    {
        // Arrange
        _mockSettings.Setup(s => s.IsConfigured).Returns(false);
        var service = CreateService();

        // Act
        var result = await service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePeerVisibilityAsync_ReturnsFalse_OnHttpError()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.Forbidden, "");
        var service = CreateService();

        // Act
        var result = await service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: true);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePeerVisibilityAsync_ReturnsTrue_OnSuccess()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "");
        var service = CreateService();

        // Act
        var result = await service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: false);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePeerVisibilityAsync_SendsCorrectPayload()
    {
        // Arrange
        string? capturedContent = null;
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.Content != null)
                {
                    capturedContent = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var service = CreateService();

        // Act
        await service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: true);

        // Assert
        capturedContent.Should().NotBeNull();
        capturedContent.Should().Contain("\"disabled\"");
        capturedContent.Should().Contain("true");
    }

    #endregion

    #region Request Header Tests

    [Fact]
    public async Task GetGroupsAsync_SetsAuthorizationHeader()
    {
        // Arrange
        _mockSettings.Setup(s => s.ApiToken).Returns("my-secret-token");
        string? capturedAuthHeader = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedAuthHeader = req.Headers.Authorization?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });

        var service = CreateService();

        // Act
        await service.GetGroupsAsync();

        // Assert
        capturedAuthHeader.Should().Be("Bearer my-secret-token");
    }

    [Fact]
    public async Task GetGroupsAsync_BuildsCorrectUrl()
    {
        // Arrange
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.wayfarer.io");
        string? capturedUrl = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });

        var service = CreateService();

        // Act
        await service.GetGroupsAsync();

        // Assert
        capturedUrl.Should().Be("https://api.wayfarer.io/api/mobile/groups?scope=all");
    }

    [Fact]
    public async Task GetGroupMembersAsync_BuildsCorrectUrl()
    {
        // Arrange
        var groupId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        _mockSettings.Setup(s => s.ServerUrl).Returns("https://api.wayfarer.io/");  // trailing slash
        string? capturedUrl = null;

        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });

        var service = CreateService();

        // Act
        await service.GetGroupMembersAsync(groupId);

        // Assert
        capturedUrl.Should().Be($"https://api.wayfarer.io/api/mobile/groups/{groupId}/members");
    }

    #endregion

    #region Exception Handling Tests

    [Fact]
    public async Task GetGroupsAsync_ReturnsEmptyList_OnException()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var service = CreateService();

        // Act
        var result = await service.GetGroupsAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestLocationsAsync_ReturnsEmptyDict_OnException()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryLocationsAsync_ReturnsNull_OnException()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService();
        var request = new GroupLocationsQueryRequest
        {
            MinLat = 40.0,
            MinLng = -74.0,
            MaxLat = 41.0,
            MaxLng = -73.0,
            ZoomLevel = 10
        };

        // Act
        var result = await service.QueryLocationsAsync(Guid.NewGuid(), request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePeerVisibilityAsync_ReturnsFalse_OnException()
    {
        // Arrange
        _mockHttpHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var service = CreateService();

        // Act
        var result = await service.UpdatePeerVisibilityAsync(Guid.NewGuid(), disabled: true);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Multiple Locations Tests

    [Fact]
    public async Task GetLatestLocationsAsync_ParsesMultipleLocations()
    {
        // Arrange
        var responseJson = @"[
            {
                ""userId"": ""user-1"",
                ""latitude"": 40.7128,
                ""longitude"": -74.0060,
                ""isLive"": true
            },
            {
                ""userId"": ""user-2"",
                ""latitude"": 34.0522,
                ""longitude"": -118.2437,
                ""isLive"": false
            },
            {
                ""userId"": ""user-3"",
                ""coordinates"": { ""x"": -122.4194, ""y"": 37.7749 },
                ""isLive"": true
            }
        ]";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert
        result.Should().HaveCount(3);
        result.Should().ContainKeys("user-1", "user-2", "user-3");
    }

    #endregion

    #region JSON Root Element Tests

    [Fact]
    public async Task GetLatestLocationsAsync_HandlesNonArrayResponse()
    {
        // Arrange - if server returns an object instead of array
        var responseJson = @"{ ""error"": ""invalid request"" }";

        SetupHttpResponse(HttpStatusCode.OK, responseJson);
        var service = CreateService();

        // Act
        var result = await service.GetLatestLocationsAsync(Guid.NewGuid());

        // Assert - should handle gracefully and return empty
        result.Should().BeEmpty();
    }

    #endregion
}

/// <summary>
/// Test implementation of GroupsService for unit testing.
/// This is a copy of the production GroupsService to enable testing
/// without referencing the MAUI project.
/// </summary>
public class TestGroupsService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TestGroupsService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new instance of TestGroupsService.
    /// </summary>
    public TestGroupsService(ISettingsService settings, ILogger<TestGroupsService> logger, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _settings = settings;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    private HttpClient HttpClientInstance => _httpClientFactory.CreateClient("WayfarerApi");

    public async Task<List<GroupSummary>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch groups - API not configured");
            return new List<GroupSummary>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Get, "/api/mobile/groups?scope=all");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch groups: {StatusCode}", response.StatusCode);
                return new List<GroupSummary>();
            }

            var groups = await response.Content.ReadFromJsonAsync<List<GroupSummary>>(JsonOptions, cancellationToken);
            _logger.LogDebug("Fetched {Count} groups", groups?.Count ?? 0);

            return groups ?? new List<GroupSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching groups");
            return new List<GroupSummary>();
        }
    }

    public async Task<List<GroupMember>> GetGroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch group members - API not configured");
            return new List<GroupMember>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Get, $"/api/mobile/groups/{groupId}/members");
            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch group members: {StatusCode}", response.StatusCode);
                return new List<GroupMember>();
            }

            var members = await response.Content.ReadFromJsonAsync<List<GroupMember>>(JsonOptions, cancellationToken);
            _logger.LogDebug("Fetched {Count} members for group {GroupId}", members?.Count ?? 0, groupId);

            return members ?? new List<GroupMember>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching group members for {GroupId}", groupId);
            return new List<GroupMember>();
        }
    }

    public async Task<Dictionary<string, MemberLocation>> GetLatestLocationsAsync(
        Guid groupId,
        List<string>? userIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot fetch locations - API not configured");
            return new Dictionary<string, MemberLocation>();
        }

        try
        {
            var request = CreateRequest(HttpMethod.Post, $"/api/mobile/groups/{groupId}/locations/latest");
            request.Content = JsonContent.Create(new { includeUserIds = userIds }, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch latest locations: {StatusCode}", response.StatusCode);
                return new Dictionary<string, MemberLocation>();
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var locations = ParseLatestLocationsResponse(responseBody);

            _logger.LogDebug("Fetched {Count} latest locations for group {GroupId}", locations.Count, groupId);

            return locations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching latest locations for {GroupId}", groupId);
            return new Dictionary<string, MemberLocation>();
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint)
    {
        var baseUrl = _settings.ServerUrl?.TrimEnd('/') ?? "";
        var request = new HttpRequestMessage(method, $"{baseUrl}{endpoint}");

        if (!string.IsNullOrEmpty(_settings.ApiToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiToken);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public async Task<GroupLocationsQueryResponse?> QueryLocationsAsync(
        Guid groupId,
        GroupLocationsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot query locations - API not configured");
            return null;
        }

        try
        {
            var httpRequest = CreateRequest(HttpMethod.Post, $"/api/mobile/groups/{groupId}/locations/query");
            httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(httpRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to query locations: {StatusCode}", response.StatusCode);
                return null;
            }

            var queryResponse = await response.Content.ReadFromJsonAsync<GroupLocationsQueryResponse>(JsonOptions, cancellationToken);
            _logger.LogDebug(
                "Queried {Count} locations (total {Total}) for group {GroupId}",
                queryResponse?.ReturnedItems ?? 0,
                queryResponse?.TotalItems ?? 0,
                groupId);

            return queryResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying locations for {GroupId}", groupId);
            return null;
        }
    }

    public async Task<bool> UpdatePeerVisibilityAsync(
        Guid groupId,
        bool disabled,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            _logger.LogWarning("Cannot update peer visibility - API not configured");
            return false;
        }

        try
        {
            var request = CreateRequest(new HttpMethod("PATCH"), $"/api/mobile/groups/{groupId}/peer-visibility");
            request.Content = JsonContent.Create(new PeerVisibilityUpdateRequest { Disabled = disabled }, options: JsonOptions);

            var response = await HttpClientInstance.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to update peer visibility: {StatusCode}", response.StatusCode);
                return false;
            }

            _logger.LogDebug("Updated peer visibility for group {GroupId}: disabled={Disabled}", groupId, disabled);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating peer visibility for {GroupId}", groupId);
            return false;
        }
    }

    private static Dictionary<string, MemberLocation> ParseLatestLocationsResponse(string responseBody)
    {
        var result = new Dictionary<string, MemberLocation>();

        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            // The response is an array of location objects with userId
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var userId = element.TryGetProperty("userId", out var userIdProp)
                        ? userIdProp.GetString()
                        : null;

                    if (string.IsNullOrEmpty(userId))
                        continue;

                    var location = new MemberLocation();

                    // Try direct latitude/longitude properties first (preferred format)
                    if (element.TryGetProperty("latitude", out var latProp))
                        location.Latitude = latProp.GetDouble();
                    else if (element.TryGetProperty("coordinates", out var coords) &&
                             coords.TryGetProperty("y", out var coordLat))
                        location.Latitude = coordLat.GetDouble();

                    if (element.TryGetProperty("longitude", out var lonProp))
                        location.Longitude = lonProp.GetDouble();
                    else if (element.TryGetProperty("coordinates", out var coordsLon) &&
                             coordsLon.TryGetProperty("x", out var coordLon))
                        location.Longitude = coordLon.GetDouble();

                    if (element.TryGetProperty("localTimestamp", out var timestamp))
                        location.Timestamp = timestamp.GetDateTime();
                    else if (element.TryGetProperty("timestampUtc", out var timestampUtc))
                        location.Timestamp = timestampUtc.GetDateTime();

                    if (element.TryGetProperty("isLive", out var isLive))
                        location.IsLive = isLive.GetBoolean();

                    if (element.TryGetProperty("fullAddress", out var address))
                        location.Address = address.GetString();

                    result[userId] = location;
                }
            }
        }
        catch (JsonException ex)
        {
            // Log parse error but don't propagate - return partial results
            System.Diagnostics.Debug.WriteLine($"[GroupsService] Failed to parse locations response: {ex.Message}");
        }

        return result;
    }
}
