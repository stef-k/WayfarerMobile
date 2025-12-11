namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for GroupLocationModels (GroupLatestLocationsRequest, GroupLocationsQueryRequest,
/// GroupLocationsQueryResponse, GroupLocationResult, PeerVisibilityUpdateRequest).
/// </summary>
public class GroupLocationModelsTests
{
    #region GroupLatestLocationsRequest Tests

    [Fact]
    public void GroupLatestLocationsRequest_DefaultValues_AreCorrect()
    {
        // Arrange
        var request = new GroupLatestLocationsRequest();

        // Assert
        request.IncludeUserIds.Should().BeNull();
    }

    [Fact]
    public void GroupLatestLocationsRequest_WithUserIds_CanBeSetAndRetrieved()
    {
        // Arrange
        var userIds = new List<string> { "user-1", "user-2", "user-3" };
        var request = new GroupLatestLocationsRequest
        {
            IncludeUserIds = userIds
        };

        // Assert
        request.IncludeUserIds.Should().NotBeNull();
        request.IncludeUserIds.Should().HaveCount(3);
        request.IncludeUserIds.Should().ContainInOrder("user-1", "user-2", "user-3");
    }

    [Fact]
    public void GroupLatestLocationsRequest_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange
        var request = new GroupLatestLocationsRequest
        {
            IncludeUserIds = new List<string>()
        };

        // Assert
        request.IncludeUserIds.Should().NotBeNull();
        request.IncludeUserIds.Should().BeEmpty();
    }

    #endregion

    #region GroupLocationsQueryRequest Tests

    [Fact]
    public void GroupLocationsQueryRequest_DefaultValues_AreCorrect()
    {
        // Arrange
        var request = new GroupLocationsQueryRequest();

        // Assert
        request.MinLng.Should().Be(0);
        request.MinLat.Should().Be(0);
        request.MaxLng.Should().Be(0);
        request.MaxLat.Should().Be(0);
        request.ZoomLevel.Should().Be(0);
        request.UserIds.Should().BeNull();
        request.DateType.Should().BeNull();
        request.Year.Should().BeNull();
        request.Month.Should().BeNull();
        request.Day.Should().BeNull();
        request.PageSize.Should().BeNull();
        request.ContinuationToken.Should().BeNull();
    }

    [Fact]
    public void GroupLocationsQueryRequest_BoundingBox_CanBeSetAndRetrieved()
    {
        // Arrange - Berlin area bounding box
        var request = new GroupLocationsQueryRequest
        {
            MinLng = 13.08,
            MinLat = 52.34,
            MaxLng = 13.76,
            MaxLat = 52.68
        };

        // Assert
        request.MinLng.Should().Be(13.08);
        request.MinLat.Should().Be(52.34);
        request.MaxLng.Should().Be(13.76);
        request.MaxLat.Should().Be(52.68);
    }

    [Fact]
    public void GroupLocationsQueryRequest_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var request = new GroupLocationsQueryRequest
        {
            MinLng = -122.5,
            MinLat = 37.5,
            MaxLng = -122.0,
            MaxLat = 38.0,
            ZoomLevel = 12.5,
            UserIds = new List<string> { "user-a", "user-b" },
            DateType = "day",
            Year = 2025,
            Month = 12,
            Day = 11,
            PageSize = 50,
            ContinuationToken = "token-xyz"
        };

        // Assert
        request.MinLng.Should().Be(-122.5);
        request.MinLat.Should().Be(37.5);
        request.MaxLng.Should().Be(-122.0);
        request.MaxLat.Should().Be(38.0);
        request.ZoomLevel.Should().Be(12.5);
        request.UserIds.Should().HaveCount(2);
        request.UserIds.Should().Contain("user-a");
        request.UserIds.Should().Contain("user-b");
        request.DateType.Should().Be("day");
        request.Year.Should().Be(2025);
        request.Month.Should().Be(12);
        request.Day.Should().Be(11);
        request.PageSize.Should().Be(50);
        request.ContinuationToken.Should().Be("token-xyz");
    }

    [Theory]
    [InlineData("day")]
    [InlineData("month")]
    [InlineData("year")]
    public void GroupLocationsQueryRequest_DateType_AcceptsValidValues(string dateType)
    {
        // Arrange
        var request = new GroupLocationsQueryRequest
        {
            DateType = dateType
        };

        // Assert
        request.DateType.Should().Be(dateType);
    }

    [Fact]
    public void GroupLocationsQueryRequest_NegativeCoordinates_AreSupported()
    {
        // Arrange - Western hemisphere coordinates
        var request = new GroupLocationsQueryRequest
        {
            MinLng = -180.0,
            MinLat = -90.0,
            MaxLng = 0.0,
            MaxLat = 0.0
        };

        // Assert
        request.MinLng.Should().Be(-180.0);
        request.MinLat.Should().Be(-90.0);
        request.MaxLng.Should().Be(0.0);
        request.MaxLat.Should().Be(0.0);
    }

    #endregion

    #region GroupLocationsQueryResponse Tests

    [Fact]
    public void GroupLocationsQueryResponse_DefaultValues_AreCorrect()
    {
        // Arrange
        var response = new GroupLocationsQueryResponse();

        // Assert
        response.TotalItems.Should().Be(0);
        response.ReturnedItems.Should().Be(0);
        response.PageSize.Should().Be(0);
        response.HasMore.Should().BeFalse();
        response.NextPageToken.Should().BeNull();
        response.IsTruncated.Should().BeFalse();
        response.Results.Should().NotBeNull();
        response.Results.Should().BeEmpty();
    }

    [Fact]
    public void GroupLocationsQueryResponse_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var results = new List<GroupLocationResult>
        {
            new GroupLocationResult { Id = 1, Latitude = 52.52, Longitude = 13.405 },
            new GroupLocationResult { Id = 2, Latitude = 48.8566, Longitude = 2.3522 }
        };

        var response = new GroupLocationsQueryResponse
        {
            TotalItems = 100,
            ReturnedItems = 2,
            PageSize = 50,
            HasMore = true,
            NextPageToken = "next-page-token",
            IsTruncated = false,
            Results = results
        };

        // Assert
        response.TotalItems.Should().Be(100);
        response.ReturnedItems.Should().Be(2);
        response.PageSize.Should().Be(50);
        response.HasMore.Should().BeTrue();
        response.NextPageToken.Should().Be("next-page-token");
        response.IsTruncated.Should().BeFalse();
        response.Results.Should().HaveCount(2);
    }

    [Fact]
    public void GroupLocationsQueryResponse_Pagination_HasMoreTrue_IndicatesMoreResults()
    {
        // Arrange
        var response = new GroupLocationsQueryResponse
        {
            TotalItems = 150,
            ReturnedItems = 50,
            PageSize = 50,
            HasMore = true,
            NextPageToken = "continuation-token-123"
        };

        // Assert
        response.HasMore.Should().BeTrue();
        response.NextPageToken.Should().NotBeNullOrEmpty();
        response.TotalItems.Should().BeGreaterThan(response.ReturnedItems);
    }

    [Fact]
    public void GroupLocationsQueryResponse_Truncated_IndicatesLimitReached()
    {
        // Arrange
        var response = new GroupLocationsQueryResponse
        {
            TotalItems = 10000,
            ReturnedItems = 1000,
            PageSize = 1000,
            IsTruncated = true,
            HasMore = false
        };

        // Assert
        response.IsTruncated.Should().BeTrue();
        response.TotalItems.Should().BeGreaterThan(response.ReturnedItems);
    }

    #endregion

    #region GroupLocationResult Tests

    [Fact]
    public void GroupLocationResult_DefaultValues_AreCorrect()
    {
        // Arrange
        var result = new GroupLocationResult();

        // Assert
        result.Id.Should().Be(0);
        result.UserId.Should().BeNull();
        result.Latitude.Should().Be(0);
        result.Longitude.Should().Be(0);
        result.Timestamp.Should().Be(default);
        result.LocalTimestamp.Should().Be(default);
        result.Address.Should().BeNull();
        result.FullAddress.Should().BeNull();
        result.IsLatestLocation.Should().BeFalse();
        result.IsLive.Should().BeFalse();
    }

    [Fact]
    public void GroupLocationResult_AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var timestamp = new DateTime(2025, 12, 11, 14, 30, 0, DateTimeKind.Utc);
        var localTimestamp = new DateTime(2025, 12, 11, 15, 30, 0, DateTimeKind.Local);

        var result = new GroupLocationResult
        {
            Id = 12345,
            UserId = "user-xyz",
            Latitude = 51.5074,
            Longitude = -0.1278,
            Timestamp = timestamp,
            LocalTimestamp = localTimestamp,
            Address = "London",
            FullAddress = "Westminster, London SW1A 0AA, UK",
            IsLatestLocation = true,
            IsLive = true
        };

        // Assert
        result.Id.Should().Be(12345);
        result.UserId.Should().Be("user-xyz");
        result.Latitude.Should().Be(51.5074);
        result.Longitude.Should().Be(-0.1278);
        result.Timestamp.Should().Be(timestamp);
        result.LocalTimestamp.Should().Be(localTimestamp);
        result.Address.Should().Be("London");
        result.FullAddress.Should().Be("Westminster, London SW1A 0AA, UK");
        result.IsLatestLocation.Should().BeTrue();
        result.IsLive.Should().BeTrue();
    }

    [Fact]
    public void GroupLocationResult_CoordinateHandling_NegativeValues()
    {
        // Arrange - Buenos Aires coordinates
        var result = new GroupLocationResult
        {
            Latitude = -34.6037,
            Longitude = -58.3816
        };

        // Assert
        result.Latitude.Should().BeNegative();
        result.Longitude.Should().BeNegative();
        result.Latitude.Should().BeApproximately(-34.6037, 0.0001);
        result.Longitude.Should().BeApproximately(-58.3816, 0.0001);
    }

    [Fact]
    public void GroupLocationResult_CoordinateHandling_BoundaryValues()
    {
        // Arrange - Extreme coordinate values
        var result = new GroupLocationResult
        {
            Latitude = 90.0,   // North Pole
            Longitude = 180.0  // International Date Line
        };

        // Assert
        result.Latitude.Should().Be(90.0);
        result.Longitude.Should().Be(180.0);
    }

    [Fact]
    public void GroupLocationResult_CoordinateHandling_Precision()
    {
        // Arrange - High precision coordinates
        var result = new GroupLocationResult
        {
            Latitude = 52.520008123456789,
            Longitude = 13.404953987654321
        };

        // Assert - Double precision should be maintained
        result.Latitude.Should().BeApproximately(52.520008123456789, 1e-10);
        result.Longitude.Should().BeApproximately(13.404953987654321, 1e-10);
    }

    [Fact]
    public void GroupLocationResult_IsLatestAndIsLive_IndependentFlags()
    {
        // Arrange - Latest location but not currently live
        var result1 = new GroupLocationResult
        {
            IsLatestLocation = true,
            IsLive = false
        };

        // Arrange - Not latest location but user is live (edge case)
        var result2 = new GroupLocationResult
        {
            IsLatestLocation = false,
            IsLive = true
        };

        // Assert
        result1.IsLatestLocation.Should().BeTrue();
        result1.IsLive.Should().BeFalse();
        result2.IsLatestLocation.Should().BeFalse();
        result2.IsLive.Should().BeTrue();
    }

    #endregion

    #region PeerVisibilityUpdateRequest Tests

    [Fact]
    public void PeerVisibilityUpdateRequest_DefaultValues_AreCorrect()
    {
        // Arrange
        var request = new PeerVisibilityUpdateRequest();

        // Assert
        request.Disabled.Should().BeFalse();
    }

    [Fact]
    public void PeerVisibilityUpdateRequest_DisabledTrue_CanBeSet()
    {
        // Arrange
        var request = new PeerVisibilityUpdateRequest
        {
            Disabled = true
        };

        // Assert
        request.Disabled.Should().BeTrue();
    }

    [Fact]
    public void PeerVisibilityUpdateRequest_DisabledFalse_CanBeSet()
    {
        // Arrange
        var request = new PeerVisibilityUpdateRequest
        {
            Disabled = false
        };

        // Assert
        request.Disabled.Should().BeFalse();
    }

    #endregion
}
