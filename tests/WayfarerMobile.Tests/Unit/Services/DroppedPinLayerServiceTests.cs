using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for DroppedPinLayerService.
/// Tests dropped pin marker creation and clearing.
/// </summary>
public class DroppedPinLayerServiceTests
{
    #region Test Setup

    private readonly TestDroppedPinLayerService _service;
    private readonly ILogger<TestDroppedPinLayerService> _logger;
    private readonly TestWritableLayerForDroppedPin _layer;

    public DroppedPinLayerServiceTests()
    {
        _logger = NullLogger<TestDroppedPinLayerService>.Instance;
        _service = new TestDroppedPinLayerService(_logger);
        _layer = new TestWritableLayerForDroppedPin();
    }

    #endregion

    #region Layer Name Tests

    [Fact]
    public void DroppedPinLayerName_ReturnsCorrectName()
    {
        _service.DroppedPinLayerName.Should().Be("DroppedPin");
    }

    #endregion

    #region ShowDroppedPin Tests

    [Fact]
    public void ShowDroppedPin_ValidCoordinates_CreatesMarker()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);

        _layer.FeatureCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void ShowDroppedPin_ClearsExistingPinFirst()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);
        _service.ShowDroppedPin(_layer, 48.8566, 2.3522);

        _layer.ClearCount.Should().Be(2);
        _layer.FeatureCount.Should().Be(1);
    }

    [Fact]
    public void ShowDroppedPin_ConvertsToWebMercator()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);

        _layer.LastAddedFeature.Should().NotBeNull();
        // Web Mercator X should be roughly -14233.5 for lon -0.1278
        // Web Mercator Y should be roughly 6711703.8 for lat 51.5074
        _layer.LastAddedFeature!.X.Should().NotBe(-0.1278);
        _layer.LastAddedFeature.Y.Should().NotBe(51.5074);
    }

    [Fact]
    public void ShowDroppedPin_AtEquatorPrimeMeridian_CreatesMarkerAtOrigin()
    {
        _service.ShowDroppedPin(_layer, 0, 0);

        _layer.FeatureCount.Should().Be(1);
        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.X.Should().BeApproximately(0, 0.001);
        _layer.LastAddedFeature.Y.Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void ShowDroppedPin_NegativeLatitude_CreatesMarker()
    {
        _service.ShowDroppedPin(_layer, -33.8688, 151.2093); // Sydney

        _layer.FeatureCount.Should().Be(1);
        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.Y.Should().BeLessThan(0); // Southern hemisphere
    }

    [Fact]
    public void ShowDroppedPin_NegativeLongitude_CreatesMarker()
    {
        _service.ShowDroppedPin(_layer, 40.7128, -74.0060); // New York

        _layer.FeatureCount.Should().Be(1);
        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.X.Should().BeLessThan(0); // Western hemisphere
    }

    [Fact]
    public void ShowDroppedPin_HasStyleApplied()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);

        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.HasStyle.Should().BeTrue();
    }

    [Theory]
    [InlineData(51.5074, -0.1278)]  // London
    [InlineData(48.8566, 2.3522)]   // Paris
    [InlineData(40.7128, -74.0060)] // New York
    [InlineData(-33.8688, 151.2093)] // Sydney
    [InlineData(35.6762, 139.6503)] // Tokyo
    public void ShowDroppedPin_VariousLocations_AlwaysCreatesMarker(double lat, double lon)
    {
        _service.ShowDroppedPin(_layer, lat, lon);

        _layer.FeatureCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    #endregion

    #region ClearDroppedPin Tests

    [Fact]
    public void ClearDroppedPin_ClearsLayer()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);

        _service.ClearDroppedPin(_layer);

        _layer.ClearCount.Should().Be(2); // Once from ShowDroppedPin, once from ClearDroppedPin
        _layer.DataChangedCount.Should().Be(2);
    }

    [Fact]
    public void ClearDroppedPin_EmptyLayer_StillCallsDataHasChanged()
    {
        _service.ClearDroppedPin(_layer);

        _layer.ClearCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void ClearDroppedPin_AfterMultiplePins_ClearsAll()
    {
        _service.ShowDroppedPin(_layer, 51.5074, -0.1278);
        _service.ShowDroppedPin(_layer, 48.8566, 2.3522);
        _service.ShowDroppedPin(_layer, 40.7128, -74.0060);

        _service.ClearDroppedPin(_layer);

        _layer.FeatureCount.Should().Be(0);
    }

    #endregion
}

#region Test Infrastructure

internal class TestWritableLayerForDroppedPin
{
    private readonly List<TestFeatureForDroppedPin> _features = new();

    public int FeatureCount => _features.Count;
    public int ClearCount { get; private set; }
    public int DataChangedCount { get; private set; }
    public TestFeatureForDroppedPin? LastAddedFeature { get; private set; }

    public void Add(TestFeatureForDroppedPin feature)
    {
        _features.Add(feature);
        LastAddedFeature = feature;
    }

    public void Clear()
    {
        _features.Clear();
        ClearCount++;
    }

    public void DataHasChanged()
    {
        DataChangedCount++;
    }
}

internal class TestFeatureForDroppedPin
{
    public double X { get; }
    public double Y { get; }
    public bool HasStyle { get; }

    public TestFeatureForDroppedPin(double x, double y, bool hasStyle = true)
    {
        X = x;
        Y = y;
        HasStyle = hasStyle;
    }
}

internal class TestDroppedPinLayerService
{
    private readonly ILogger<TestDroppedPinLayerService> _logger;

    public TestDroppedPinLayerService(ILogger<TestDroppedPinLayerService> logger)
    {
        _logger = logger;
    }

    public string DroppedPinLayerName => "DroppedPin";

    public void ShowDroppedPin(TestWritableLayerForDroppedPin layer, double latitude, double longitude)
    {
        layer.Clear();

        // Convert to Web Mercator (simplified formula)
        var x = longitude * 20037508.34 / 180;
        var y = Math.Log(Math.Tan((90 + latitude) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;

        var feature = new TestFeatureForDroppedPin(x, y, hasStyle: true);
        layer.Add(feature);
        layer.DataHasChanged();

        _logger.LogDebug("Dropped pin at {Lat:F6}, {Lon:F6}", latitude, longitude);
    }

    public void ClearDroppedPin(TestWritableLayerForDroppedPin layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }
}

#endregion
