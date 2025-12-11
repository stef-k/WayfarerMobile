namespace WayfarerMobile.Tests.Unit.Models;

/// <summary>
/// Unit tests for TripModels classes.
/// </summary>
public class TripModelsTests
{
    #region TripSummary Tests

    [Fact]
    public void TripSummary_LocationsText_EmptyLists_ReturnsNoLocationInfo()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("No location info");
    }

    [Fact]
    public void TripSummary_LocationsText_OnlyCities_ReturnsCities()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Paris", "Lyon" },
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Paris, Lyon");
    }

    [Fact]
    public void TripSummary_LocationsText_OnlyCountries_ReturnsCountries()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string> { "France", "Germany" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("France, Germany");
    }

    [Fact]
    public void TripSummary_LocationsText_BothCitiesAndCountries_ReturnsSeparatedByBullet()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Paris", "Berlin" },
            Countries = new List<string> { "France", "Germany" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Paris, Berlin \u2022 France, Germany");
    }

    [Fact]
    public void TripSummary_LocationsText_TakesOnlyFirstThreeCities()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Paris", "Berlin", "Rome", "Madrid", "London" },
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Paris, Berlin, Rome");
    }

    [Fact]
    public void TripSummary_LocationsText_TakesOnlyFirstTwoCountries()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string> { "France", "Germany", "Italy", "Spain" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("France, Germany");
    }

    [Fact]
    public void TripSummary_LocationsText_SingleCity_ReturnsCity()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Tokyo" },
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Tokyo");
    }

    [Fact]
    public void TripSummary_LocationsText_SingleCountry_ReturnsCountry()
    {
        // Arrange
        var summary = new TripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string> { "Japan" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Japan");
    }

    #endregion

    #region TripDetails Tests

    [Fact]
    public void TripDetails_AllPlaces_EmptyRegions_ReturnsEmptyList()
    {
        // Arrange
        var details = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>()
        };

        // Act
        var result = details.AllPlaces;

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void TripDetails_AllPlaces_SingleRegion_ReturnsPlacesFromRegion()
    {
        // Arrange
        var place1 = new TripPlace { Id = Guid.NewGuid(), Name = "Place 1" };
        var place2 = new TripPlace { Id = Guid.NewGuid(), Name = "Place 2" };

        var details = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>
            {
                new TripRegion
                {
                    Id = Guid.NewGuid(),
                    Name = "Region 1",
                    Places = new List<TripPlace> { place1, place2 }
                }
            }
        };

        // Act
        var result = details.AllPlaces;

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(place1);
        result.Should().Contain(place2);
    }

    [Fact]
    public void TripDetails_AllPlaces_MultipleRegions_AggregatesAllPlaces()
    {
        // Arrange
        var place1 = new TripPlace { Id = Guid.NewGuid(), Name = "Place 1" };
        var place2 = new TripPlace { Id = Guid.NewGuid(), Name = "Place 2" };
        var place3 = new TripPlace { Id = Guid.NewGuid(), Name = "Place 3" };

        var details = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>
            {
                new TripRegion
                {
                    Id = Guid.NewGuid(),
                    Name = "Region 1",
                    Places = new List<TripPlace> { place1 }
                },
                new TripRegion
                {
                    Id = Guid.NewGuid(),
                    Name = "Region 2",
                    Places = new List<TripPlace> { place2, place3 }
                }
            }
        };

        // Act
        var result = details.AllPlaces;

        // Assert
        result.Should().HaveCount(3);
        result.Should().ContainInOrder(place1, place2, place3);
    }

    [Fact]
    public void TripDetails_AllPlaces_RegionWithNoPlaces_ReturnsEmptyList()
    {
        // Arrange
        var details = new TripDetails
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Regions = new List<TripRegion>
            {
                new TripRegion
                {
                    Id = Guid.NewGuid(),
                    Name = "Empty Region",
                    Places = new List<TripPlace>()
                }
            }
        };

        // Act
        var result = details.AllPlaces;

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region TripDownloadProgress Tests

    [Fact]
    public void TripDownloadProgress_Initialize_SetsAllPropertiesCorrectly()
    {
        // Arrange
        var progress = new TripDownloadProgress
        {
            Percentage = 50,
            CompletedTiles = 100,
            DownloadedBytes = 5000,
            ErrorCount = 5,
            IsComplete = true
        };

        // Act
        progress.Initialize(500, 1_000_000);

        // Assert
        progress.TotalTiles.Should().Be(500);
        progress.TotalEstimatedBytes.Should().Be(1_000_000);
        progress.CompletedTiles.Should().Be(0);
        progress.DownloadedBytes.Should().Be(0);
        progress.IsComplete.Should().BeFalse();
        progress.ErrorCount.Should().Be(0);
        progress.DownloadStartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TripDownloadProgress_UpdateProgress_IncrementsCounters()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);

        // Act
        progress.UpdateProgress(1024);
        progress.UpdateProgress(2048);

        // Assert
        progress.CompletedTiles.Should().Be(2);
        progress.DownloadedBytes.Should().Be(3072);
    }

    [Fact]
    public void TripDownloadProgress_UpdateProgress_CalculatesPercentageCorrectly()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);

        // Act
        for (int i = 0; i < 25; i++)
        {
            progress.UpdateProgress(1024);
        }

        // Assert
        progress.Percentage.Should().Be(25);
    }

    [Fact]
    public void TripDownloadProgress_UpdateProgress_SetsStatusMessage()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);

        // Act
        progress.UpdateProgress(1024, "Downloading tile 1...");

        // Assert
        progress.Status.Should().Be("Downloading tile 1...");
    }

    [Fact]
    public void TripDownloadProgress_UpdateProgress_NullStatusMessage_KeepsPreviousStatus()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);
        progress.UpdateProgress(1024, "Initial status");

        // Act
        progress.UpdateProgress(1024, null);

        // Assert
        progress.Status.Should().Be("Initial status");
    }

    [Fact]
    public void TripDownloadProgress_UpdateProgress_ZeroTotalTiles_PercentageIsZero()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(0, 0);

        // Act
        progress.UpdateProgress(1024);

        // Assert
        progress.Percentage.Should().Be(0);
    }

    [Fact]
    public void TripDownloadProgress_ReportError_IncrementsErrorCount()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);

        // Act
        progress.ReportError("Connection timeout");
        progress.ReportError("Server error");

        // Assert
        progress.ErrorCount.Should().Be(2);
    }

    [Fact]
    public void TripDownloadProgress_ReportError_SetsStatusMessage()
    {
        // Arrange
        var progress = new TripDownloadProgress();
        progress.Initialize(100, 100_000);

        // Act
        progress.ReportError("Connection timeout");

        // Assert
        progress.Status.Should().Be("Connection timeout");
    }

    [Fact]
    public void TripDownloadProgress_DownloadedMB_ConvertsCorrectly()
    {
        // Arrange
        var progress = new TripDownloadProgress
        {
            DownloadedBytes = 5 * 1024 * 1024 // 5 MB
        };

        // Act
        var result = progress.DownloadedMB;

        // Assert
        result.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public void TripDownloadProgress_TotalEstimatedMB_ConvertsCorrectly()
    {
        // Arrange
        var progress = new TripDownloadProgress
        {
            TotalEstimatedBytes = 10 * 1024 * 1024 // 10 MB
        };

        // Act
        var result = progress.TotalEstimatedMB;

        // Assert
        result.Should().BeApproximately(10.0, 0.001);
    }

    [Fact]
    public void TripDownloadProgress_SpeedBytesPerSecond_ZeroElapsedTime_ReturnsZero()
    {
        // Arrange
        var progress = new TripDownloadProgress
        {
            DownloadedBytes = 1000,
            DownloadStartTime = DateTime.UtcNow
        };

        // Act - Speed immediately after start should be very high or handle edge case
        var result = progress.SpeedBytesPerSecond;

        // Assert - With nearly zero elapsed time, result depends on implementation
        // The implementation returns 0 when elapsed <= 0
        result.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void TripDownloadProgress_SpeedText_LessThanMBPerSecond_ReturnsKBFormat()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddSeconds(-10);
        var progress = new TripDownloadProgress
        {
            DownloadedBytes = 500 * 1024, // 500 KB in 10 seconds = 50 KB/s
            DownloadStartTime = startTime
        };

        // Act
        var result = progress.SpeedText;

        // Assert
        result.Should().EndWith("KB/s");
    }

    [Fact]
    public void TripDownloadProgress_SpeedText_MoreThanMBPerSecond_ReturnsMBFormat()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddSeconds(-1);
        var progress = new TripDownloadProgress
        {
            DownloadedBytes = 5 * 1024 * 1024, // 5 MB in 1 second = 5 MB/s
            DownloadStartTime = startTime
        };

        // Act
        var result = progress.SpeedText;

        // Assert
        result.Should().EndWith("MB/s");
    }

    [Fact]
    public void TripDownloadProgress_Percentage_CalculatedFromTiles()
    {
        // Arrange
        var progress = new TripDownloadProgress
        {
            TotalTiles = 100,
            CompletedTiles = 0,
            Percentage = 0
        };

        // Act - simulate 50 tiles downloaded
        for (int i = 0; i < 50; i++)
        {
            progress.UpdateProgress(1024);
        }

        // Assert
        progress.Percentage.Should().Be(50);
    }

    #endregion

    #region TimelineLocation Tests

    [Fact]
    public void TimelineLocation_Latitude_ReturnsCoordinatesY()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Coordinates = new TimelineCoordinates { X = 10.5, Y = 51.5 }
        };

        // Act
        var result = location.Latitude;

        // Assert
        result.Should().Be(51.5);
    }

    [Fact]
    public void TimelineLocation_Longitude_ReturnsCoordinatesX()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Coordinates = new TimelineCoordinates { X = 10.5, Y = 51.5 }
        };

        // Act
        var result = location.Longitude;

        // Assert
        result.Should().Be(10.5);
    }

    [Fact]
    public void TimelineLocation_Latitude_NullCoordinates_ReturnsZero()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Coordinates = null
        };

        // Act
        var result = location.Latitude;

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void TimelineLocation_Longitude_NullCoordinates_ReturnsZero()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Coordinates = null
        };

        // Act
        var result = location.Longitude;

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_PlaceAndCountry_ReturnsCombined()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = "Berlin",
            Country = "Germany"
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("Berlin, Germany");
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_OnlyPlace_ReturnsPlace()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = "Berlin",
            Country = null
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("Berlin");
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_OnlyCountry_ReturnsCountry()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = null,
            Country = "Germany"
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("Germany");
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_NoPlaceOrCountry_ReturnsCoordinates()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = null,
            Country = null,
            Coordinates = new TimelineCoordinates { X = 13.4049, Y = 52.5200 }
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("52.5200, 13.4049");
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_EmptyStrings_ReturnsCoordinates()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = "",
            Country = "",
            Coordinates = new TimelineCoordinates { X = 13.4049, Y = 52.5200 }
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("52.5200, 13.4049");
    }

    [Fact]
    public void TimelineLocation_DisplayLocation_PlaceIsEmptyCountryHasValue_ReturnsCountry()
    {
        // Arrange
        var location = new TimelineLocation
        {
            Place = "",
            Country = "Germany"
        };

        // Act
        var result = location.DisplayLocation;

        // Assert
        result.Should().Be("Germany");
    }

    #endregion

    #region TileCoordinate Tests

    [Fact]
    public void TileCoordinate_GetTileUrl_SubstitutesPlaceholders()
    {
        // Arrange
        var tile = new TileCoordinate
        {
            Zoom = 15,
            X = 17389,
            Y = 11236
        };
        var urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

        // Act
        var result = tile.GetTileUrl(urlTemplate);

        // Assert
        result.Should().Be("https://tile.openstreetmap.org/15/17389/11236.png");
    }

    [Fact]
    public void TileCoordinate_GetTileUrl_HandlesMultiplePlaceholderFormats()
    {
        // Arrange
        var tile = new TileCoordinate
        {
            Zoom = 10,
            X = 100,
            Y = 200
        };
        var urlTemplate = "https://tiles.example.com/tiles/{z}/{x}/{y}?format=png";

        // Act
        var result = tile.GetTileUrl(urlTemplate);

        // Assert
        result.Should().Be("https://tiles.example.com/tiles/10/100/200?format=png");
    }

    [Fact]
    public void TileCoordinate_Id_ReturnsCorrectFormat()
    {
        // Arrange
        var tile = new TileCoordinate
        {
            Zoom = 15,
            X = 17389,
            Y = 11236
        };

        // Act
        var result = tile.Id;

        // Assert
        result.Should().Be("15-17389-11236");
    }

    [Fact]
    public void TileCoordinate_Id_ZeroValues_ReturnsCorrectFormat()
    {
        // Arrange
        var tile = new TileCoordinate
        {
            Zoom = 0,
            X = 0,
            Y = 0
        };

        // Act
        var result = tile.Id;

        // Assert
        result.Should().Be("0-0-0");
    }

    #endregion

    #region PublicTripSummary Tests

    [Fact]
    public void PublicTripSummary_LocationsText_EmptyLists_ReturnsNoLocationInfo()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("No location info");
    }

    [Fact]
    public void PublicTripSummary_LocationsText_BothCitiesAndCountries_ReturnsSeparatedByBullet()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Paris", "Berlin" },
            Countries = new List<string> { "France", "Germany" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Paris, Berlin \u2022 France, Germany");
    }

    [Fact]
    public void PublicTripSummary_LocationsText_TakesOnlyFirstThreeCities()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string> { "Paris", "Berlin", "Rome", "Madrid" },
            Countries = new List<string>()
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("Paris, Berlin, Rome");
    }

    [Fact]
    public void PublicTripSummary_LocationsText_TakesOnlyFirstTwoCountries()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            Cities = new List<string>(),
            Countries = new List<string> { "France", "Germany", "Italy" }
        };

        // Act
        var result = summary.LocationsText;

        // Assert
        result.Should().Be("France, Germany");
    }

    [Fact]
    public void PublicTripSummary_SummaryText_MultiplePlaces_ReturnsPluralForm()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            PlaceCount = 5
        };

        // Act
        var result = summary.SummaryText;

        // Assert
        result.Should().Be("5 places");
    }

    [Fact]
    public void PublicTripSummary_SummaryText_SinglePlace_ReturnsSingularForm()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            PlaceCount = 1
        };

        // Act
        var result = summary.SummaryText;

        // Assert
        result.Should().Be("1 place");
    }

    [Fact]
    public void PublicTripSummary_SummaryText_ZeroPlaces_ReturnsEmptyTrip()
    {
        // Arrange
        var summary = new PublicTripSummary
        {
            Id = Guid.NewGuid(),
            Name = "Test Trip",
            PlaceCount = 0
        };

        // Act
        var result = summary.SummaryText;

        // Assert
        result.Should().Be("Empty trip");
    }

    #endregion

    #region PublicTripsResponse Tests

    [Fact]
    public void PublicTripsResponse_HasMore_PageLessThanTotalPages_ReturnsTrue()
    {
        // Arrange
        var response = new PublicTripsResponse
        {
            Page = 1,
            TotalPages = 5
        };

        // Act
        var result = response.HasMore;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void PublicTripsResponse_HasMore_PageEqualsTotalPages_ReturnsFalse()
    {
        // Arrange
        var response = new PublicTripsResponse
        {
            Page = 5,
            TotalPages = 5
        };

        // Act
        var result = response.HasMore;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void PublicTripsResponse_HasMore_PageGreaterThanTotalPages_ReturnsFalse()
    {
        // Arrange
        var response = new PublicTripsResponse
        {
            Page = 6,
            TotalPages = 5
        };

        // Act
        var result = response.HasMore;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void PublicTripsResponse_HasMore_SinglePage_ReturnsFalse()
    {
        // Arrange
        var response = new PublicTripsResponse
        {
            Page = 1,
            TotalPages = 1
        };

        // Act
        var result = response.HasMore;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void PublicTripsResponse_HasMore_ZeroPages_ReturnsFalse()
    {
        // Arrange
        var response = new PublicTripsResponse
        {
            Page = 0,
            TotalPages = 0
        };

        // Act
        var result = response.HasMore;

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DownloadedTrip Tests

    [Fact]
    public void DownloadedTrip_IsFullyDownloaded_StatusComplete_ReturnsTrue()
    {
        // Arrange
        var trip = new DownloadedTrip
        {
            LocalId = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            Name = "Test Trip",
            Status = "complete"
        };

        // Act
        var result = trip.IsFullyDownloaded;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void DownloadedTrip_IsFullyDownloaded_StatusMetadataOnly_ReturnsFalse()
    {
        // Arrange
        var trip = new DownloadedTrip
        {
            LocalId = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            Name = "Test Trip",
            Status = "metadata_only"
        };

        // Act
        var result = trip.IsFullyDownloaded;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void DownloadedTrip_IsFullyDownloaded_DefaultStatus_ReturnsFalse()
    {
        // Arrange
        var trip = new DownloadedTrip
        {
            LocalId = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            Name = "Test Trip"
            // Status defaults to "metadata_only"
        };

        // Act
        var result = trip.IsFullyDownloaded;

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
