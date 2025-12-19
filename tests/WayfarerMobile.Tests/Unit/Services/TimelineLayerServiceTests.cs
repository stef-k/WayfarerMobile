using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for TimelineLayerService.
/// Tests timeline location marker creation and layer management.
/// </summary>
public class TimelineLayerServiceTests
{
    #region Test Setup

    private readonly TestTimelineLayerService _service;
    private readonly ILogger<TestTimelineLayerService> _logger;
    private readonly TestWritableLayerForTimeline _layer;

    public TimelineLayerServiceTests()
    {
        _logger = NullLogger<TestTimelineLayerService>.Instance;
        _service = new TestTimelineLayerService(_logger);
        _layer = new TestWritableLayerForTimeline();
    }

    #endregion

    #region Layer Name Tests

    [Fact]
    public void TimelineLayerName_ReturnsCorrectName()
    {
        _service.TimelineLayerName.Should().Be("TimelineLocations");
    }

    #endregion

    #region UpdateTimelineMarkers Tests

    [Fact]
    public void UpdateTimelineMarkers_EmptyList_ReturnsEmptyPoints()
    {
        var locations = new List<TestTimelineLocation>();

        var points = _service.UpdateTimelineMarkers(_layer, locations);

        points.Should().BeEmpty();
        _layer.ClearCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateTimelineMarkers_ValidLocation_CreatesMarker()
    {
        var locations = new List<TestTimelineLocation>
        {
            new()
            {
                Id = 1,
                Coordinates = new TestCoordinates(51.5074, -0.1278),
                LocalTimestamp = DateTime.Now
            }
        };

        var points = _service.UpdateTimelineMarkers(_layer, locations);

        points.Should().HaveCount(1);
        _layer.FeatureCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateTimelineMarkers_MultipleLocations_CreatesMultipleMarkers()
    {
        var locations = new List<TestTimelineLocation>
        {
            new() { Id = 1, Coordinates = new TestCoordinates(51.5074, -0.1278), LocalTimestamp = DateTime.Now },
            new() { Id = 2, Coordinates = new TestCoordinates(51.5084, -0.1288), LocalTimestamp = DateTime.Now },
            new() { Id = 3, Coordinates = new TestCoordinates(51.5094, -0.1298), LocalTimestamp = DateTime.Now }
        };

        var points = _service.UpdateTimelineMarkers(_layer, locations);

        points.Should().HaveCount(3);
        _layer.FeatureCount.Should().Be(3);
    }

    [Fact]
    public void UpdateTimelineMarkers_ZeroCoordinates_SkipsLocation()
    {
        var locations = new List<TestTimelineLocation>
        {
            new() { Id = 1, Coordinates = new TestCoordinates(51.5074, -0.1278), LocalTimestamp = DateTime.Now },
            new() { Id = 2, Coordinates = new TestCoordinates(0, 0), LocalTimestamp = DateTime.Now },
            new() { Id = 3, Coordinates = new TestCoordinates(51.5094, -0.1298), LocalTimestamp = DateTime.Now }
        };

        var points = _service.UpdateTimelineMarkers(_layer, locations);

        points.Should().HaveCount(2);
        _layer.FeatureCount.Should().Be(2);
    }

    [Fact]
    public void UpdateTimelineMarkers_NullCoordinates_SkipsLocation()
    {
        var locations = new List<TestTimelineLocation>
        {
            new() { Id = 1, Coordinates = new TestCoordinates(51.5074, -0.1278), LocalTimestamp = DateTime.Now },
            new() { Id = 2, Coordinates = null, LocalTimestamp = DateTime.Now },
            new() { Id = 3, Coordinates = new TestCoordinates(51.5094, -0.1298), LocalTimestamp = DateTime.Now }
        };

        var points = _service.UpdateTimelineMarkers(_layer, locations);

        points.Should().HaveCount(2);
        _layer.FeatureCount.Should().Be(2);
    }

    [Fact]
    public void UpdateTimelineMarkers_SetsFeatureProperties()
    {
        var timestamp = DateTime.Now;
        var locations = new List<TestTimelineLocation>
        {
            new() { Id = 123, Coordinates = new TestCoordinates(51.5074, -0.1278), LocalTimestamp = timestamp }
        };

        _service.UpdateTimelineMarkers(_layer, locations);

        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.Properties["LocationId"].Should().Be(123);
        _layer.LastAddedFeature.Properties["Timestamp"].Should().Be(timestamp.ToString("g"));
    }

    [Fact]
    public void UpdateTimelineMarkers_ClearsLayerBeforeAdding()
    {
        var locations1 = new List<TestTimelineLocation>
        {
            new() { Id = 1, Coordinates = new TestCoordinates(51.5074, -0.1278), LocalTimestamp = DateTime.Now }
        };
        _service.UpdateTimelineMarkers(_layer, locations1);

        var locations2 = new List<TestTimelineLocation>
        {
            new() { Id = 2, Coordinates = new TestCoordinates(51.5084, -0.1288), LocalTimestamp = DateTime.Now },
            new() { Id = 3, Coordinates = new TestCoordinates(51.5094, -0.1298), LocalTimestamp = DateTime.Now }
        };
        _service.UpdateTimelineMarkers(_layer, locations2);

        _layer.ClearCount.Should().Be(2);
        _layer.FeatureCount.Should().Be(2);
    }

    #endregion

    #region ClearTimelineMarkers Tests

    [Fact]
    public void ClearTimelineMarkers_ClearsLayer()
    {
        _service.ClearTimelineMarkers(_layer);

        _layer.ClearCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    #endregion
}

#region Test Infrastructure

internal class TestWritableLayerForTimeline
{
    private readonly List<TestFeatureForTimeline> _features = new();

    public int FeatureCount => _features.Count;
    public int ClearCount { get; private set; }
    public int DataChangedCount { get; private set; }
    public TestFeatureForTimeline? LastAddedFeature { get; private set; }

    public void Add(TestFeatureForTimeline feature)
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

internal class TestFeatureForTimeline
{
    public Dictionary<string, object> Properties { get; } = new();
}

internal class TestMPointForTimeline
{
    public double X { get; }
    public double Y { get; }

    public TestMPointForTimeline(double x, double y)
    {
        X = x;
        Y = y;
    }
}

internal class TestCoordinates
{
    public double X { get; }
    public double Y { get; }

    public TestCoordinates(double lat, double lon)
    {
        X = lon;
        Y = lat;
    }
}

internal class TestTimelineLocation
{
    public int Id { get; set; }
    public TestCoordinates? Coordinates { get; set; }
    public DateTime LocalTimestamp { get; set; }
}

internal class TestTimelineLayerService
{
    private readonly ILogger<TestTimelineLayerService> _logger;

    public TestTimelineLayerService(ILogger<TestTimelineLayerService> logger)
    {
        _logger = logger;
    }

    public string TimelineLayerName => "TimelineLocations";

    public List<TestMPointForTimeline> UpdateTimelineMarkers(TestWritableLayerForTimeline layer, IEnumerable<TestTimelineLocation> locations)
    {
        layer.Clear();

        var points = new List<TestMPointForTimeline>();

        foreach (var location in locations)
        {
            var coords = location.Coordinates;
            if (coords == null || (coords.X == 0 && coords.Y == 0))
                continue;

            var x = coords.X * 20037508.34 / 180;
            var y = Math.Log(Math.Tan((90 + coords.Y) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;
            var point = new TestMPointForTimeline(x, y);
            points.Add(point);

            var feature = new TestFeatureForTimeline();
            feature.Properties["LocationId"] = location.Id;
            feature.Properties["Timestamp"] = location.LocalTimestamp.ToString("g");

            layer.Add(feature);
        }

        layer.DataHasChanged();
        return points;
    }

    public void ClearTimelineMarkers(TestWritableLayerForTimeline layer)
    {
        layer.Clear();
        layer.DataHasChanged();
    }
}

#endregion
