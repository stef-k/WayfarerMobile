using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TripLayerService.
/// Tests place marker creation, segment polyline styling, and layer management.
/// </summary>
/// <remarks>
/// TripLayerService manages trip-related map layers including:
/// - Place markers with custom icons or colored dot fallbacks
/// - Segment polylines with transport mode styling
///
/// This test file includes test-local implementations since the main
/// WayfarerMobile project targets MAUI platforms (android/ios) which cannot be directly
/// referenced from a pure .NET test project. The tests verify the core logic:
/// - Place marker creation with valid coordinates
/// - Empty places list handling
/// - Zero coordinates skipped
/// - Transport mode styling (walk, drive, bike, transit colors)
/// - Segment polyline creation
/// - Layer clearing
/// </remarks>
public class TripLayerServiceTests
{
    #region Test Setup

    private readonly TestableTripLayerService _service;
    private readonly ILogger<TestableTripLayerService> _logger;
    private readonly TestableWritableLayer _placesLayer;
    private readonly TestableWritableLayer _segmentsLayer;

    public TripLayerServiceTests()
    {
        _logger = NullLogger<TestableTripLayerService>.Instance;
        _service = new TestableTripLayerService(_logger);
        _placesLayer = new TestableWritableLayer();
        _segmentsLayer = new TestableWritableLayer();
    }

    #endregion

    #region Layer Names Tests

    [Fact]
    public void TripPlacesLayerName_ReturnsCorrectName()
    {
        _service.TripPlacesLayerName.Should().Be("TripPlaces");
    }

    [Fact]
    public void TripSegmentsLayerName_ReturnsCorrectName()
    {
        _service.TripSegmentsLayerName.Should().Be("TripSegments");
    }

    #endregion

    #region UpdateTripPlacesAsync Tests - Basic Place Creation

    [Fact]
    public async Task UpdateTripPlacesAsync_WithValidPlace_CreatesMarker()
    {
        var places = new List<TripPlace>
        {
            new() { Id = Guid.NewGuid(), Name = "Test Place", Latitude = 51.5074, Longitude = -0.1278, Icon = "marker", MarkerColor = "bg-blue" }
        };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().HaveCount(1);
        _placesLayer.Features.Should().HaveCount(1);
        _placesLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_WithMultiplePlaces_CreatesAllMarkers()
    {
        var places = new List<TripPlace>
        {
            new() { Id = Guid.NewGuid(), Name = "Place 1", Latitude = 51.5074, Longitude = -0.1278 },
            new() { Id = Guid.NewGuid(), Name = "Place 2", Latitude = 48.8566, Longitude = 2.3522 },
            new() { Id = Guid.NewGuid(), Name = "Place 3", Latitude = 40.7128, Longitude = -74.0060 }
        };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().HaveCount(3);
        _placesLayer.Features.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_SetsPlaceIdProperty()
    {
        var placeId = Guid.NewGuid();
        var places = new List<TripPlace> { new() { Id = placeId, Name = "Test", Latitude = 51.5074, Longitude = -0.1278 } };

        await _service.UpdateTripPlacesAsync(_placesLayer, places);

        _placesLayer.Features[0].Properties["PlaceId"].Should().Be(placeId);
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_SetsNameProperty()
    {
        var places = new List<TripPlace> { new() { Id = Guid.NewGuid(), Name = "Big Ben", Latitude = 51.5074, Longitude = -0.1278 } };

        await _service.UpdateTripPlacesAsync(_placesLayer, places);

        _placesLayer.Features[0].Properties["Name"].Should().Be("Big Ben");
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_NullName_SetsEmptyString()
    {
        var places = new List<TripPlace> { new() { Id = Guid.NewGuid(), Name = null!, Latitude = 51.5074, Longitude = -0.1278 } };

        await _service.UpdateTripPlacesAsync(_placesLayer, places);

        _placesLayer.Features[0].Properties["Name"].Should().Be("");
    }

    #endregion

    #region UpdateTripPlacesAsync Tests - Empty and Invalid Input

    [Fact]
    public async Task UpdateTripPlacesAsync_EmptyList_ReturnsEmptyPoints()
    {
        var places = new List<TripPlace>();

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().BeEmpty();
        _placesLayer.Features.Should().BeEmpty();
        _placesLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_ZeroCoordinates_SkipsPlace()
    {
        var places = new List<TripPlace> { new() { Id = Guid.NewGuid(), Name = "Invalid", Latitude = 0, Longitude = 0 } };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().BeEmpty();
        _placesLayer.Features.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_ZeroLatitudeOnly_DoesNotSkip()
    {
        var places = new List<TripPlace> { new() { Id = Guid.NewGuid(), Name = "Equator", Latitude = 0, Longitude = 10.5 } };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_ZeroLongitudeOnly_DoesNotSkip()
    {
        var places = new List<TripPlace> { new() { Id = Guid.NewGuid(), Name = "Prime Meridian", Latitude = 51.5, Longitude = 0 } };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_MixedValidAndInvalid_OnlyCreatesValidMarkers()
    {
        var places = new List<TripPlace>
        {
            new() { Id = Guid.NewGuid(), Name = "Valid 1", Latitude = 51.5074, Longitude = -0.1278 },
            new() { Id = Guid.NewGuid(), Name = "Invalid", Latitude = 0, Longitude = 0 },
            new() { Id = Guid.NewGuid(), Name = "Valid 2", Latitude = 48.8566, Longitude = 2.3522 }
        };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, places);

        points.Should().HaveCount(2);
        _placesLayer.Features.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateTripPlacesAsync_ClearsExistingFeatures()
    {
        await _service.UpdateTripPlacesAsync(_placesLayer, new List<TripPlace>
        {
            new() { Id = Guid.NewGuid(), Name = "Initial", Latitude = 51.5074, Longitude = -0.1278 }
        });

        var newPlaces = new List<TripPlace>
        {
            new() { Id = Guid.NewGuid(), Name = "New 1", Latitude = 48.8566, Longitude = 2.3522 },
            new() { Id = Guid.NewGuid(), Name = "New 2", Latitude = 40.7128, Longitude = -74.0060 }
        };

        var points = await _service.UpdateTripPlacesAsync(_placesLayer, newPlaces);

        points.Should().HaveCount(2);
        _placesLayer.Features.Should().HaveCount(2);
        _placesLayer.Features.Should().NotContain(f => f.Properties.ContainsKey("Name") && f.Properties["Name"].ToString() == "Initial");
    }

    #endregion

    #region UpdateTripSegments Tests

    [Fact]
    public void UpdateTripSegments_EmptyList_AddsNoFeatures()
    {
        var segments = new List<TripSegment>();

        _service.UpdateTripSegments(_segmentsLayer, segments);

        _segmentsLayer.Features.Should().BeEmpty();
        _segmentsLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public void UpdateTripSegments_ValidSegment_CreatesPolyline()
    {
        var segments = new List<TripSegment>
        {
            new() { Id = 1, TransportMode = "driving", Geometry = "_p~iF~ps|U_ulLnnqC_mqNvxq`@" }
        };

        _service.UpdateTripSegments(_segmentsLayer, segments);

        _segmentsLayer.Features.Should().HaveCount(1);
    }

    [Fact]
    public void UpdateTripSegments_NullGeometry_SkipsSegment()
    {
        var segments = new List<TripSegment>
        {
            new() { Id = 1, TransportMode = "driving", Geometry = null },
            new() { Id = 2, TransportMode = "walking", Geometry = "_p~iF~ps|U_ulLnnqC_mqNvxq`@" }
        };

        _service.UpdateTripSegments(_segmentsLayer, segments);

        _segmentsLayer.Features.Should().HaveCount(1);
    }

    [Fact]
    public void UpdateTripSegments_SetsSegmentIdProperty()
    {
        var segments = new List<TripSegment>
        {
            new() { Id = 123, TransportMode = "driving", Geometry = "_p~iF~ps|U_ulLnnqC_mqNvxq`@" }
        };

        _service.UpdateTripSegments(_segmentsLayer, segments);

        _segmentsLayer.Features[0].Properties["SegmentId"].Should().Be(123);
    }

    [Theory]
    [InlineData("driving", "Blue")]
    [InlineData("walking", "Green")]
    [InlineData("cycling", "Orange")]
    [InlineData("transit", "Purple")]
    [InlineData(null, "Gray")]
    public void UpdateTripSegments_UsesCorrectStyleForTransportMode(string? mode, string expectedColor)
    {
        var segments = new List<TripSegment>
        {
            new() { Id = 1, TransportMode = mode, Geometry = "_p~iF~ps|U_ulLnnqC_mqNvxq`@" }
        };

        _service.UpdateTripSegments(_segmentsLayer, segments);

        _service.LastStyleColorUsed.Should().Be(expectedColor);
    }

    #endregion

    #region Clear Methods Tests

    [Fact]
    public void ClearTripPlaces_ClearsLayer()
    {
        _service.ClearTripPlaces(_placesLayer);

        _placesLayer.ClearCalled.Should().BeTrue();
        _placesLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public void ClearTripSegments_ClearsLayer()
    {
        _service.ClearTripSegments(_segmentsLayer);

        _segmentsLayer.ClearCalled.Should().BeTrue();
        _segmentsLayer.DataHasChangedCalled.Should().BeTrue();
    }

    #endregion
}

#region Test Infrastructure

internal class TestableWritableLayer
{
    public List<TestableFeature> Features { get; } = new();
    public bool DataHasChangedCalled { get; private set; }
    public bool ClearCalled { get; private set; }

    public void Add(TestableFeature feature)
    {
        Features.Add(feature);
    }

    public void Clear()
    {
        Features.Clear();
        ClearCalled = true;
    }

    public void DataHasChanged()
    {
        DataHasChangedCalled = true;
    }
}

internal class TestableFeature
{
    public Dictionary<string, object> Properties { get; } = new();
}

internal class TestablePoint
{
    public double X { get; }
    public double Y { get; }

    public TestablePoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

internal class TripPlace
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Icon { get; set; }
    public string? MarkerColor { get; set; }
}

internal class TripSegment
{
    public int Id { get; set; }
    public string? TransportMode { get; set; }
    public string? Geometry { get; set; }
}

internal class TestableTripLayerService
{
    private readonly ILogger<TestableTripLayerService> _logger;

    public string? LastStyleColorUsed { get; private set; }

    public TestableTripLayerService(ILogger<TestableTripLayerService> logger)
    {
        _logger = logger;
    }

    public string TripPlacesLayerName => "TripPlaces";
    public string TripSegmentsLayerName => "TripSegments";

    public Task<List<TestablePoint>> UpdateTripPlacesAsync(TestableWritableLayer layer, IEnumerable<TripPlace> places)
    {
        layer.Clear();

        var points = new List<TestablePoint>();

        foreach (var place in places)
        {
            if (place.Latitude == 0 && place.Longitude == 0)
                continue;

            var x = place.Longitude * 20037508.34 / 180;
            var y = Math.Log(Math.Tan((90 + place.Latitude) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;
            points.Add(new TestablePoint(x, y));

            var feature = new TestableFeature();
            feature.Properties["PlaceId"] = place.Id;
            feature.Properties["Name"] = place.Name ?? "";

            layer.Add(feature);
        }

        layer.DataHasChanged();
        return Task.FromResult(points);
    }

    public void ClearTripPlaces(TestableWritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    public void UpdateTripSegments(TestableWritableLayer layer, IEnumerable<TripSegment> segments)
    {
        layer.Clear();

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Geometry))
                continue;

            LastStyleColorUsed = GetStyleColor(segment.TransportMode?.ToLowerInvariant());

            var feature = new TestableFeature();
            feature.Properties["SegmentId"] = segment.Id;
            feature.Properties["TransportMode"] = segment.TransportMode ?? "";

            layer.Add(feature);
        }

        layer.DataHasChanged();
    }

    public void ClearTripSegments(TestableWritableLayer layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }

    private static string GetStyleColor(string? mode)
    {
        return mode switch
        {
            "driving" or "car" => "Blue",
            "walking" or "walk" or "foot" => "Green",
            "cycling" or "bicycle" or "bike" => "Orange",
            "transit" or "bus" or "train" or "subway" => "Purple",
            _ => "Gray"
        };
    }
}

#endregion
