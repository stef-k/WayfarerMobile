# Testing

This document covers the testing approach, frameworks, and practices for WayfarerMobile.

## Test Framework Stack

| Tool | Purpose | Version |
|------|---------|---------|
| xUnit | Test framework | 2.9.2 |
| FluentAssertions | Assertion library | 7.0.0 |
| Moq | Mocking framework | 4.20.72 |
| Coverlet | Code coverage | 6.0.2 |

## Project Structure

```
tests/
+-- WayfarerMobile.Tests/
    +-- WayfarerMobile.Tests.csproj
    +-- Algorithms/
    |   +-- GeoMathTests.cs
    |   +-- ThresholdFilterTests.cs
    |   +-- AStarPathfinderTests.cs
    |   +-- PolylineDecoderTests.cs
    +-- Models/
    |   +-- LocationDataTests.cs
    |   +-- TripModelsTests.cs
    +-- Navigation/
    |   +-- TripNavigationGraphTests.cs
    +-- Services/
    |   +-- SettingsServiceTests.cs
    |   +-- LocationSyncServiceTests.cs
    +-- Helpers/
    |   +-- TestDataBuilder.cs
    |   +-- MockFactory.cs
```

## Running Tests

### Command Line

```bash
# Run all tests
cd tests/WayfarerMobile.Tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~GeoMathTests"

# Run with code coverage
dotnet test /p:CollectCoverage=true
```

### Visual Studio

1. Open Test Explorer (Test > Test Explorer)
2. Click "Run All" or right-click specific tests
3. View results in Test Explorer window

### VS Code

1. Install C# Dev Kit extension
2. Open Testing panel in sidebar
3. Click run buttons next to tests

## Code Coverage

Coverage is collected using Coverlet with the following configuration:

```xml
<!-- In WayfarerMobile.Tests.csproj -->
<PropertyGroup>
  <CollectCoverage>true</CollectCoverage>
  <CoverletOutputFormat>cobertura,lcov</CoverletOutputFormat>
  <CoverletOutput>./coverage/</CoverletOutput>
  <Include>[WayfarerMobile.Core]*</Include>
  <Exclude>[WayfarerMobile.Tests]*</Exclude>
</PropertyGroup>
```

### Generate Coverage Report

```bash
# Run tests with coverage
dotnet test /p:CollectCoverage=true

# View coverage report
# Coverage files are generated in tests/WayfarerMobile.Tests/coverage/

# Optional: Generate HTML report with ReportGenerator
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:coverage/coverage.cobertura.xml -targetdir:coverage/html
```

## Writing Tests

### Basic Test Structure

```csharp
using FluentAssertions;
using Xunit;

namespace WayfarerMobile.Tests.Algorithms;

public class GeoMathTests
{
    [Fact]
    public void CalculateDistance_SamePoint_ReturnsZero()
    {
        // Arrange
        var lat = 51.5074;
        var lon = -0.1278;

        // Act
        var distance = GeoMath.CalculateDistance(lat, lon, lat, lon);

        // Assert
        distance.Should().Be(0);
    }

    [Theory]
    [InlineData(51.5074, -0.1278, 48.8566, 2.3522, 343550, 5000)] // London to Paris
    [InlineData(40.7128, -74.0060, 34.0522, -118.2437, 3940000, 50000)] // NYC to LA
    public void CalculateDistance_KnownDistances_ReturnsExpectedValue(
        double lat1, double lon1, double lat2, double lon2,
        double expectedMeters, double toleranceMeters)
    {
        // Act
        var distance = GeoMath.CalculateDistance(lat1, lon1, lat2, lon2);

        // Assert
        distance.Should().BeApproximately(expectedMeters, toleranceMeters);
    }
}
```

### Testing with Mocks

```csharp
using FluentAssertions;
using Moq;
using Xunit;

namespace WayfarerMobile.Tests.Services;

public class LocationSyncServiceTests
{
    private readonly Mock<IApiClient> _apiClientMock;
    private readonly Mock<IDatabaseService> _databaseMock;
    private readonly Mock<ILogger<LocationSyncService>> _loggerMock;
    private readonly LocationSyncService _sut; // System Under Test

    public LocationSyncServiceTests()
    {
        _apiClientMock = new Mock<IApiClient>();
        _databaseMock = new Mock<IDatabaseService>();
        _loggerMock = new Mock<ILogger<LocationSyncService>>();

        _sut = new LocationSyncService(
            _apiClientMock.Object,
            _databaseMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SyncAsync_WithPendingLocations_CallsApi()
    {
        // Arrange
        var locations = new List<QueuedLocation>
        {
            new() { Id = 1, Latitude = 51.5074, Longitude = -0.1278 },
            new() { Id = 2, Latitude = 51.5080, Longitude = -0.1285 }
        };

        _databaseMock
            .Setup(x => x.GetPendingLocationsAsync(It.IsAny<int>()))
            .ReturnsAsync(locations);

        _apiClientMock
            .Setup(x => x.SendLocationsAsync(It.IsAny<IEnumerable<LocationDto>>()))
            .ReturnsAsync(new ApiResult { Success = true });

        // Act
        await _sut.SyncAsync();

        // Assert
        _apiClientMock.Verify(
            x => x.SendLocationsAsync(It.Is<IEnumerable<LocationDto>>(
                l => l.Count() == 2)),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenApiFails_DoesNotMarkAsSynced()
    {
        // Arrange
        var locations = new List<QueuedLocation>
        {
            new() { Id = 1, Latitude = 51.5074, Longitude = -0.1278 }
        };

        _databaseMock
            .Setup(x => x.GetPendingLocationsAsync(It.IsAny<int>()))
            .ReturnsAsync(locations);

        _apiClientMock
            .Setup(x => x.SendLocationsAsync(It.IsAny<IEnumerable<LocationDto>>()))
            .ReturnsAsync(new ApiResult { Success = false });

        // Act
        await _sut.SyncAsync();

        // Assert
        _databaseMock.Verify(
            x => x.MarkLocationsSyncedAsync(It.IsAny<IEnumerable<int>>()),
            Times.Never);
    }
}
```

### Testing Async Code

```csharp
[Fact]
public async Task StartAsync_SetsStateToActive()
{
    // Arrange
    var tcs = new TaskCompletionSource<TrackingState>();
    _sut.StateChanged += (_, state) => tcs.TrySetResult(state);

    // Act
    await _sut.StartAsync();
    var finalState = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

    // Assert
    finalState.Should().Be(TrackingState.Active);
}
```

### Testing Events

```csharp
[Fact]
public void OnLocationReceived_RaisesEvent()
{
    // Arrange
    LocationData? receivedLocation = null;
    _sut.LocationReceived += (_, loc) => receivedLocation = loc;

    var testLocation = new LocationData
    {
        Latitude = 51.5074,
        Longitude = -0.1278,
        Accuracy = 10
    };

    // Act
    _sut.SimulateLocationReceived(testLocation);

    // Assert
    receivedLocation.Should().NotBeNull();
    receivedLocation!.Latitude.Should().Be(51.5074);
}
```

## Test Categories

Use traits to categorize tests:

```csharp
[Fact]
[Trait("Category", "Unit")]
public void UnitTest_Example() { }

[Fact]
[Trait("Category", "Integration")]
public void IntegrationTest_Example() { }

[Fact]
[Trait("Category", "Slow")]
public void SlowTest_Example() { }
```

Run specific categories:

```bash
# Run only unit tests
dotnet test --filter "Category=Unit"

# Exclude slow tests
dotnet test --filter "Category!=Slow"
```

## Test Helpers

### Test Data Builder

```csharp
namespace WayfarerMobile.Tests.Helpers;

public class TestDataBuilder
{
    public static LocationData CreateLocation(
        double lat = 51.5074,
        double lon = -0.1278,
        double accuracy = 10,
        double? bearing = null)
    {
        return new LocationData
        {
            Latitude = lat,
            Longitude = lon,
            Accuracy = accuracy,
            Bearing = bearing,
            Timestamp = DateTime.UtcNow,
            Provider = "test"
        };
    }

    public static TripDetails CreateTrip(string name = "Test Trip")
    {
        return new TripDetails
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Regions = new List<TripRegion>
            {
                new()
                {
                    Id = 1,
                    Name = "Region 1",
                    Places = new List<TripPlace>
                    {
                        CreatePlace("Place 1", 51.5074, -0.1278),
                        CreatePlace("Place 2", 51.5080, -0.1285)
                    }
                }
            },
            Segments = new List<TripSegment>()
        };
    }

    public static TripPlace CreatePlace(
        string name,
        double lat,
        double lon,
        string icon = "marker",
        string color = "red")
    {
        return new TripPlace
        {
            Id = Random.Shared.Next(),
            Name = name,
            Latitude = lat,
            Longitude = lon,
            Icon = icon,
            MarkerColor = color
        };
    }
}
```

### Mock Factory

```csharp
namespace WayfarerMobile.Tests.Helpers;

public static class MockFactory
{
    public static Mock<ISettingsService> CreateSettingsService(
        bool isConfigured = true,
        string? serverUrl = "https://test.com",
        string? apiToken = "test-token")
    {
        var mock = new Mock<ISettingsService>();
        mock.Setup(x => x.IsConfigured).Returns(isConfigured);
        mock.Setup(x => x.ServerUrl).Returns(serverUrl);
        mock.Setup(x => x.ApiToken).Returns(apiToken);
        return mock;
    }

    public static Mock<ILogger<T>> CreateLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }

    public static Mock<IHttpClientFactory> CreateHttpClientFactory(
        HttpResponseMessage? response = null)
    {
        var mock = new Mock<IHttpClientFactory>();
        var handler = new MockHttpMessageHandler(response ??
            new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler);

        mock.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(client);

        return mock;
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public MockHttpMessageHandler(HttpResponseMessage response)
    {
        _response = response;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }
}
```

## Testing Algorithms

### GeoMath Tests

```csharp
public class GeoMathTests
{
    [Fact]
    public void CalculateBearing_NorthDirection_ReturnsZero()
    {
        var bearing = GeoMath.CalculateBearing(51.5, -0.1, 52.5, -0.1);
        bearing.Should().BeApproximately(0, 1);
    }

    [Fact]
    public void CalculateBearing_EastDirection_Returns90()
    {
        var bearing = GeoMath.CalculateBearing(51.5, -0.1, 51.5, 0.9);
        bearing.Should().BeApproximately(90, 1);
    }

    [Fact]
    public void CalculateBearing_SouthDirection_Returns180()
    {
        var bearing = GeoMath.CalculateBearing(51.5, -0.1, 50.5, -0.1);
        bearing.Should().BeApproximately(180, 1);
    }

    [Fact]
    public void CalculateBearing_WestDirection_Returns270()
    {
        var bearing = GeoMath.CalculateBearing(51.5, 0.9, 51.5, -0.1);
        bearing.Should().BeApproximately(270, 1);
    }
}
```

### ThresholdFilter Tests

```csharp
public class ThresholdFilterTests
{
    private readonly ThresholdFilter _filter;

    public ThresholdFilterTests()
    {
        _filter = new ThresholdFilter(
            timeThresholdMinutes: 1,
            distanceThresholdMeters: 50);
    }

    [Fact]
    public void ShouldAccept_FirstLocation_ReturnsTrue()
    {
        var location = TestDataBuilder.CreateLocation();

        var result = _filter.ShouldAccept(location);

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldAccept_SameLocation_WithinTime_ReturnsFalse()
    {
        var location1 = TestDataBuilder.CreateLocation();
        var location2 = TestDataBuilder.CreateLocation();

        _filter.ShouldAccept(location1);
        var result = _filter.ShouldAccept(location2);

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldAccept_DifferentLocation_BeyondThreshold_ReturnsTrue()
    {
        var location1 = TestDataBuilder.CreateLocation(lat: 51.5000);
        var location2 = TestDataBuilder.CreateLocation(lat: 51.5010); // ~100m away

        _filter.ShouldAccept(location1);
        var result = _filter.ShouldAccept(location2);

        result.Should().BeTrue();
    }
}
```

### Polyline Decoder Tests

```csharp
public class PolylineDecoderTests
{
    [Fact]
    public void Decode_ValidPolyline_ReturnsPoints()
    {
        // Encoded polyline from Google's example
        var encoded = "_p~iF~ps|U_ulLnnqC_mqNvxq`@";

        var points = PolylineDecoder.Decode(encoded);

        points.Should().HaveCount(3);
        points[0].Latitude.Should().BeApproximately(38.5, 0.01);
        points[0].Longitude.Should().BeApproximately(-120.2, 0.01);
    }

    [Fact]
    public void Decode_EmptyString_ReturnsEmptyList()
    {
        var points = PolylineDecoder.Decode("");

        points.Should().BeEmpty();
    }

    [Fact]
    public void Decode_NullString_ReturnsEmptyList()
    {
        var points = PolylineDecoder.Decode(null);

        points.Should().BeEmpty();
    }
}
```

## Best Practices

### Test Naming Convention

Use descriptive names following the pattern:
`MethodName_Scenario_ExpectedResult`

```csharp
// Good
CalculateDistance_TwoDistantCities_ReturnsCorrectDistance()
SyncAsync_WhenOffline_QueuesLocations()
ValidatePin_InvalidFormat_ReturnsFalse()

// Avoid
Test1()
TestCalculateDistance()
ItWorks()
```

### Arrange-Act-Assert Pattern

Always structure tests with clear sections:

```csharp
[Fact]
public void MethodName_Scenario_ExpectedResult()
{
    // Arrange - Set up test data and mocks
    var input = CreateTestInput();

    // Act - Execute the method under test
    var result = _sut.MethodUnderTest(input);

    // Assert - Verify the expected outcome
    result.Should().BeExpectedValue();
}
```

### One Assert Per Test (When Practical)

Focus each test on a single behavior:

```csharp
// Good - focused test
[Fact]
public void ValidatePin_TooShort_ReturnsFalse()
{
    var result = _sut.ValidatePin("123");
    result.Should().BeFalse();
}

[Fact]
public void ValidatePin_TooLong_ReturnsFalse()
{
    var result = _sut.ValidatePin("12345");
    result.Should().BeFalse();
}

// Okay for related assertions on same result
[Fact]
public void GetTrip_ValidId_ReturnsCompleteTrip()
{
    var trip = _sut.GetTrip("trip-1");

    trip.Should().NotBeNull();
    trip.Name.Should().NotBeEmpty();
    trip.Regions.Should().NotBeEmpty();
}
```

### Avoid Test Interdependence

Each test should be independent and not rely on other tests:

```csharp
// Bad - depends on test execution order
private static int _counter = 0;

[Fact]
public void Test1() { _counter++; }

[Fact]
public void Test2() { Assert.Equal(1, _counter); }

// Good - independent setup
[Fact]
public void Test1()
{
    var counter = 0;
    counter++;
    counter.Should().Be(1);
}
```

## Next Steps

- [Security](15-Security.md) - Security testing considerations
- [Contributing](16-Contributing.md) - Test requirements for contributions
- [Architecture](11-Architecture.md) - Understanding code structure for testing
