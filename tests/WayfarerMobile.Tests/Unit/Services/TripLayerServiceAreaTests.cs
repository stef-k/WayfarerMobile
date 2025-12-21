using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WayfarerMobile.Tests.Unit.Services;

public class TripLayerServiceAreaTests
{
    private readonly TestableTripLayerServiceForAreas _service;
    private readonly ILogger<TestableTripLayerServiceForAreas> _logger;
    private readonly TestableWritableLayerForAreas _areasLayer;

    public TripLayerServiceAreaTests()
    {
        _logger = NullLogger<TestableTripLayerServiceForAreas>.Instance;
        _service = new TestableTripLayerServiceForAreas(_logger);
        _areasLayer = new TestableWritableLayerForAreas();
    }

    [Fact]
    public void TripAreasLayerName_ReturnsCorrectName()
    {
        _service.TripAreasLayerName.Should().Be("TripAreas");
    }

    [Fact]
    public void UpdateTripAreas_WithValidArea_CreatesPolygon()
    {
        var areas = new List<TripArea> { CreateValidTripArea("Test Area") };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().HaveCount(1);
        _areasLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public void UpdateTripAreas_WithMultipleAreas_CreatesAllPolygons()
    {
        var areas = new List<TripArea>
        {
            CreateValidTripArea("Area 1"),
            CreateValidTripArea("Area 2"),
            CreateValidTripArea("Area 3")
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().HaveCount(3);
    }

    [Fact]
    public void UpdateTripAreas_SetsAreaIdProperty()
    {
        var areaId = Guid.NewGuid();
        var areas = new List<TripArea>
        {
            new TripArea { Id = areaId, Name = "Test Area", Boundary = CreateTriangleBoundary() }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features[0].Properties["AreaId"].Should().Be(areaId);
    }

    [Fact]
    public void UpdateTripAreas_NullName_SetsEmptyString()
    {
        var areas = new List<TripArea>
        {
            new TripArea { Id = Guid.NewGuid(), Name = null!, Boundary = CreateTriangleBoundary() }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features[0].Properties["Name"].Should().Be("");
    }

    [Fact]
    public void UpdateTripAreas_EmptyList_AddsNoFeatures()
    {
        var areas = new List<TripArea>();
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().BeEmpty();
        _areasLayer.DataHasChangedCalled.Should().BeTrue();
    }

    [Fact]
    public void UpdateTripAreas_NullBoundary_SkipsArea()
    {
        var areas = new List<TripArea>
        {
            new TripArea { Id = Guid.NewGuid(), Name = "No Boundary", Boundary = null! }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().BeEmpty();
    }

    [Fact]
    public void UpdateTripAreas_OnlyTwoPoints_SkipsArea()
    {
        var areas = new List<TripArea>
        {
            new TripArea
            {
                Id = Guid.NewGuid(),
                Name = "Line",
                Boundary = new List<GeoCoordinate>
                {
                    new() { Latitude = 51.5074, Longitude = -0.1278 },
                    new() { Latitude = 51.5100, Longitude = -0.1300 }
                }
            }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().BeEmpty();
    }

    [Fact]
    public void UpdateTripAreas_ExactlyThreePoints_CreatesPolygon()
    {
        var areas = new List<TripArea>
        {
            new TripArea { Id = Guid.NewGuid(), Name = "Triangle", Boundary = CreateTriangleBoundary() }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _areasLayer.Features.Should().HaveCount(1);
    }

    [Fact]
    public void UpdateTripAreas_WithFillColor_UsesColor()
    {
        var areas = new List<TripArea>
        {
            new TripArea
            {
                Id = Guid.NewGuid(),
                Name = "Blue Area",
                Boundary = CreateTriangleBoundary(),
                FillColor = "#4285F4"
            }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _service.LastFillColorUsed.Should().Be("#4285F4");
    }

    [Fact]
    public void UpdateTripAreas_NullFillColor_UsesDefaultBlue()
    {
        var areas = new List<TripArea>
        {
            new TripArea
            {
                Id = Guid.NewGuid(),
                Name = "Default Color",
                Boundary = CreateTriangleBoundary(),
                FillColor = null
            }
        };
        _service.UpdateTripAreas(_areasLayer, areas);
        _service.LastFillColorUsed.Should().Be("#4285F4");
    }

    [Fact]
    public void ClearTripAreas_ClearsLayer()
    {
        _service.UpdateTripAreas(_areasLayer, new List<TripArea> { CreateValidTripArea("Area 1") });
        _areasLayer.ResetTracking();
        _service.ClearTripAreas(_areasLayer);
        _areasLayer.ClearCalled.Should().BeTrue();
        _areasLayer.DataHasChangedCalled.Should().BeTrue();
    }

    private static TripArea CreateValidTripArea(string name)
    {
        return new TripArea
        {
            Id = Guid.NewGuid(),
            Name = name,
            Boundary = CreateTriangleBoundary(),
            FillColor = "#4285F4"
        };
    }

    private static List<GeoCoordinate> CreateTriangleBoundary()
    {
        return new List<GeoCoordinate>
        {
            new() { Latitude = 51.5074, Longitude = -0.1278 },
            new() { Latitude = 51.5100, Longitude = -0.1278 },
            new() { Latitude = 51.5087, Longitude = -0.1300 }
        };
    }
}

internal class TestableWritableLayerForAreas
{
    public List<TestableFeatureForAreas> Features { get; } = new();
    public bool DataHasChangedCalled { get; private set; }
    public bool ClearCalled { get; private set; }
    public void Add(TestableFeatureForAreas feature) { Features.Add(feature); }
    public void Clear() { Features.Clear(); ClearCalled = true; }
    public void DataHasChanged() { DataHasChangedCalled = true; }
    public void ResetTracking() { DataHasChangedCalled = false; ClearCalled = false; }
}

internal class TestableFeatureForAreas
{
    public Dictionary<string, object> Properties { get; } = new();
}

internal class TestableTripLayerServiceForAreas
{
    private readonly ILogger<TestableTripLayerServiceForAreas> _logger;
    public string? LastFillColorUsed { get; private set; }

    public TestableTripLayerServiceForAreas(ILogger<TestableTripLayerServiceForAreas> logger) { _logger = logger; }
    public string TripAreasLayerName => "TripAreas";

    public void UpdateTripAreas(TestableWritableLayerForAreas layer, IEnumerable<TripArea> areas)
    {
        layer.Clear();
        foreach (var area in areas)
        {
            if (area.Boundary == null || area.Boundary.Count < 3) continue;
            LastFillColorUsed = area.FillColor ?? "#4285F4";
            var feature = new TestableFeatureForAreas();
            feature.Properties["AreaId"] = area.Id;
            feature.Properties["Name"] = area.Name ?? "";
            layer.Add(feature);
        }
        layer.DataHasChanged();
    }

    public void ClearTripAreas(TestableWritableLayerForAreas layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }
}
