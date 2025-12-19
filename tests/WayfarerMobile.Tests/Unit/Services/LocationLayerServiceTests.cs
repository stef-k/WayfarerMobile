using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for LocationLayerService.
/// Tests location indicator management, feature reuse patterns, animation state,
/// and navigation state handling.
/// </summary>
/// <remarks>
/// LocationLayerService manages the current user's location indicator on the map,
/// including the blue dot, accuracy circle, heading cone, and pulsing animation.
///
/// Key behaviors tested:
/// - UpdateLocation creates/updates marker, accuracy circle, heading cone
/// - ClearLocation removes all features and resets state
/// - ShowLastKnownLocation shows gray marker when GPS is unavailable
/// - StartAnimation/StopAnimation control pulsing animation
/// - SetNavigationState changes indicator color based on route state
/// - Feature reuse pattern for performance optimization
///
/// Note: This test file includes local test copies of the service classes since the main
/// WayfarerMobile project targets MAUI platforms (android/ios) which cannot be directly
/// referenced from a pure .NET test project.
/// </remarks>
public class LocationLayerServiceTests : IDisposable
{
    #region Test Setup

    private readonly TestLocationLayerService _service;
    private readonly TestLocationIndicatorService _indicatorService;
    private readonly ILogger<TestLocationLayerService> _logger;
    private readonly TestWritableLayer _layer;

    public LocationLayerServiceTests()
    {
        _logger = NullLogger<TestLocationLayerService>.Instance;
        var indicatorLogger = NullLogger<TestLocationIndicatorService>.Instance;
        _indicatorService = new TestLocationIndicatorService(indicatorLogger);
        _service = new TestLocationLayerService(_logger, _indicatorService);
        _layer = new TestWritableLayer();
    }

    public void Dispose()
    {
        _service.Dispose();
        _indicatorService.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Initial State Tests

    [Fact]
    public void Constructor_InitialState_HasNoLocation()
    {
        _service.LastMapPoint.Should().BeNull("because no location has been updated yet");
        _service.LastAccuracy.Should().Be(0, "because no location has been updated yet");
        _service.LastHeading.Should().Be(-1, "because no heading available initially");
    }

    [Fact]
    public void Constructor_InitialState_LocationLayerName()
    {
        _service.LocationLayerName.Should().Be("CurrentLocation");
    }

    [Fact]
    public void Constructor_InitialState_IsLocationStale()
    {
        _service.IsLocationStale.Should().BeFalse("because no location has been recorded yet");
    }

    [Fact]
    public void Constructor_InitialState_SecondsSinceLastUpdate()
    {
        _service.SecondsSinceLastUpdate.Should().Be(double.MaxValue,
            "because no location has been recorded yet");
    }

    #endregion

    #region UpdateLocation Tests - Marker Feature

    [Fact]
    public void UpdateLocation_ValidLocation_CreatesMarkerFeature()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0,
            Accuracy = 10.0
        };

        _service.UpdateLocation(_layer, location);

        _service.HasMarkerFeature.Should().BeTrue("because marker should be created");
        _layer.FeatureCount.Should().BeGreaterThanOrEqualTo(1, "because at least marker should be added");
        _layer.DataChangedCount.Should().Be(1, "because DataHasChanged should be called once");
    }

    [Fact]
    public void UpdateLocation_SecondUpdate_ReusesMarkerFeature()
    {
        var location1 = new LocationData(51.5074, -0.1278) { Speed = 5.0, Bearing = 90.0 };
        var location2 = new LocationData(51.5075, -0.1279) { Speed = 5.0, Bearing = 95.0 };

        _service.UpdateLocation(_layer, location1);
        var addCountAfterFirst = _layer.AddCount;

        _service.UpdateLocation(_layer, location2);
        var addCountAfterSecond = _layer.AddCount;

        addCountAfterSecond.Should().Be(addCountAfterFirst, "because marker should be reused, not re-added");
    }

    [Fact]
    public void UpdateLocation_UpdatesLastMapPoint()
    {
        var location = new LocationData(51.5074, -0.1278);

        _service.UpdateLocation(_layer, location);

        _service.LastMapPoint.Should().NotBeNull();
    }

    #endregion

    #region UpdateLocation Tests - Accuracy Circle

    [Fact]
    public void UpdateLocation_AccuracyGreaterThanZero_CreatesAccuracyCircle()
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = 25.0 };

        _service.UpdateLocation(_layer, location);

        _service.HasAccuracyFeature.Should().BeTrue("because accuracy > 0 should create accuracy circle");
        _service.LastAccuracy.Should().Be(25.0);
    }

    [Fact]
    public void UpdateLocation_AccuracyZero_NoAccuracyCircle()
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = 0 };

        _service.UpdateLocation(_layer, location);

        _service.HasAccuracyFeature.Should().BeFalse("because accuracy = 0 should not create accuracy circle");
        _service.LastAccuracy.Should().Be(0);
    }

    [Fact]
    public void UpdateLocation_AccuracyNull_NoAccuracyCircle()
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = null };

        _service.UpdateLocation(_layer, location);

        _service.HasAccuracyFeature.Should().BeFalse("because null accuracy should not create accuracy circle");
        _service.LastAccuracy.Should().Be(0);
    }

    [Fact]
    public void UpdateLocation_AccuracyChangesToZero_RemovesAccuracyCircle()
    {
        var location1 = new LocationData(51.5074, -0.1278) { Accuracy = 25.0 };
        _service.UpdateLocation(_layer, location1);
        _service.HasAccuracyFeature.Should().BeTrue();

        var location2 = new LocationData(51.5074, -0.1278) { Accuracy = 0 };
        _service.UpdateLocation(_layer, location2);

        _service.HasAccuracyFeature.Should().BeFalse("because accuracy circle should be removed when accuracy = 0");
        _layer.RemoveCount.Should().BeGreaterThan(0, "because accuracy feature should be removed");
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(10.0)]
    [InlineData(50.0)]
    [InlineData(100.0)]
    public void UpdateLocation_VariousAccuracyValues_CreatesAccuracyCircle(double accuracy)
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = accuracy };

        _service.UpdateLocation(_layer, location);

        _service.HasAccuracyFeature.Should().BeTrue();
        _service.LastAccuracy.Should().Be(accuracy);
    }

    #endregion

    #region UpdateLocation Tests - Heading Cone

    [Fact]
    public void UpdateLocation_ValidHeading_CreatesHeadingCone()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 45.0
        };

        _service.UpdateLocation(_layer, location);

        _service.HasHeadingFeature.Should().BeTrue("because valid heading should create heading cone");
        _service.LastHeading.Should().Be(45.0);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(90.0)]
    [InlineData(180.0)]
    [InlineData(270.0)]
    [InlineData(359.9)]
    public void UpdateLocation_BoundaryHeadingValues_CreatesHeadingCone(double heading)
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = heading
        };

        _service.UpdateLocation(_layer, location);

        _service.HasHeadingFeature.Should().BeTrue($"because heading {heading} is valid (0-360)");
        _service.LastHeading.Should().Be(heading);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(360.0)]
    [InlineData(400.0)]
    public void UpdateLocation_InvalidHeading_NoHeadingCone(double invalidHeading)
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 0.5,
            Bearing = invalidHeading
        };

        _service.UpdateLocation(_layer, location);

        _service.HasHeadingFeature.Should().BeFalse(
            "because heading is not valid when speed is below threshold");
    }

    [Fact]
    public void UpdateLocation_NullHeading_NoHeadingCone()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 0.5,
            Bearing = null
        };

        _service.UpdateLocation(_layer, location);

        _service.HasHeadingFeature.Should().BeFalse("because null bearing should not create heading cone");
    }

    [Fact]
    public void UpdateLocation_HeadingBecomesInvalid_RemovesHeadingCone()
    {
        var location1 = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0
        };
        _service.UpdateLocation(_layer, location1);
        _service.HasHeadingFeature.Should().BeTrue();

        _indicatorService.Reset();
        var location2 = new LocationData(51.5074, -0.1278)
        {
            Speed = 0.0,
            Bearing = null
        };
        _service.UpdateLocation(_layer, location2);

        _service.HasHeadingFeature.Should().BeFalse(
            "because heading cone should be removed when heading becomes invalid");
    }

    #endregion

    #region ClearLocation Tests

    [Fact]
    public void ClearLocation_RemovesAllFeatures()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0,
            Accuracy = 25.0
        };
        _service.UpdateLocation(_layer, location);
        _service.HasMarkerFeature.Should().BeTrue();
        _service.HasAccuracyFeature.Should().BeTrue();
        _service.HasHeadingFeature.Should().BeTrue();

        _service.ClearLocation(_layer);

        _service.HasMarkerFeature.Should().BeFalse("because marker should be removed");
        _service.HasAccuracyFeature.Should().BeFalse("because accuracy circle should be removed");
        _service.HasHeadingFeature.Should().BeFalse("because heading cone should be removed");
        _layer.DataChangedCount.Should().BeGreaterThan(1, "because DataHasChanged should be called");
    }

    [Fact]
    public void ClearLocation_ResetsStateValues()
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = 25.0 };
        _service.UpdateLocation(_layer, location);
        _service.LastMapPoint.Should().NotBeNull();
        _service.LastAccuracy.Should().Be(25.0);

        _service.ClearLocation(_layer);

        _service.LastMapPoint.Should().BeNull("because LastMapPoint should be reset");
        _service.LastAccuracy.Should().Be(0, "because LastAccuracy should be reset");
        _service.LastHeading.Should().Be(-1, "because LastHeading should be reset");
    }

    [Fact]
    public void ClearLocation_CallsIndicatorServiceReset()
    {
        var location = new LocationData(51.5074, -0.1278) { Speed = 5.0, Bearing = 90.0 };
        _service.UpdateLocation(_layer, location);

        _service.ClearLocation(_layer);

        _indicatorService.ResetCallCount.Should().Be(1, "because Reset should be called on indicator service");
    }

    [Fact]
    public void ClearLocation_OnEmptyLayer_DoesNotThrow()
    {
        var action = () => _service.ClearLocation(_layer);
        action.Should().NotThrow("because clearing empty layer should be safe");
    }

    [Fact]
    public void ClearLocation_CallsDataHasChanged()
    {
        _layer.ResetCounts();

        _service.ClearLocation(_layer);

        _layer.DataChangedCount.Should().Be(1, "because DataHasChanged should be called once");
    }

    #endregion

    #region ShowLastKnownLocation Tests

    [Fact]
    public void ShowLastKnownLocation_NoLastLocation_ReturnsFalse()
    {
        _indicatorService.LastKnownLocation.Should().BeNull();

        var result = _service.ShowLastKnownLocation(_layer);

        result.Should().BeFalse("because there is no last known location to show");
    }

    [Fact]
    public void ShowLastKnownLocation_HasLastLocation_ReturnsTrue()
    {
        var location = new LocationData(51.5074, -0.1278);
        _indicatorService.CalculateBestHeading(location);
        _indicatorService.LastKnownLocation.Should().NotBeNull();

        var result = _service.ShowLastKnownLocation(_layer);

        result.Should().BeTrue("because there is a last known location to show");
    }

    [Fact]
    public void ShowLastKnownLocation_ShowsGrayMarker()
    {
        var location = new LocationData(51.5074, -0.1278);
        _indicatorService.CalculateBestHeading(location);

        _service.ShowLastKnownLocation(_layer);

        _service.HasMarkerFeature.Should().BeTrue("because marker should be shown");
        _service.LastMarkerColor.Should().Be("#9E9E9E", "because stale location shows gray marker");
    }

    [Fact]
    public void ShowLastKnownLocation_ClearsAccuracyAndHeading()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0,
            Accuracy = 25.0
        };
        _service.UpdateLocation(_layer, location);
        _service.HasAccuracyFeature.Should().BeTrue();
        _service.HasHeadingFeature.Should().BeTrue();

        _service.ShowLastKnownLocation(_layer);

        _service.HasAccuracyFeature.Should().BeFalse("because stale location should not show accuracy circle");
        _service.HasHeadingFeature.Should().BeFalse("because stale location should not show heading cone");
    }

    [Fact]
    public void ShowLastKnownLocation_UpdatesLastMapPoint()
    {
        var location = new LocationData(51.5074, -0.1278);
        _indicatorService.CalculateBestHeading(location);
        _service.LastMapPoint.Should().BeNull();

        _service.ShowLastKnownLocation(_layer);

        _service.LastMapPoint.Should().NotBeNull("because LastMapPoint should be set");
    }

    [Fact]
    public void ShowLastKnownLocation_CallsDataHasChanged()
    {
        var location = new LocationData(51.5074, -0.1278);
        _indicatorService.CalculateBestHeading(location);
        _layer.ResetCounts();

        _service.ShowLastKnownLocation(_layer);

        _layer.DataChangedCount.Should().Be(1, "because DataHasChanged should be called");
    }

    #endregion

    #region Animation Start/Stop Tests

    [Fact]
    public void StartAnimation_StartsAnimation()
    {
        Action onTick = () => { };

        _service.StartAnimation(_layer, onTick);

        _service.IsAnimationEnabled.Should().BeTrue("because animation should be started");
        _indicatorService.IsNavigating.Should().BeTrue("because navigating flag should be set");
    }

    [Fact]
    public void StartAnimation_AlreadyStarted_DoesNotRestart()
    {
        var tickCount = 0;
        Action onTick = () => tickCount++;
        _service.StartAnimation(_layer, onTick);

        _service.StartAnimation(_layer, onTick);

        _service.IsAnimationEnabled.Should().BeTrue();
    }

    [Fact]
    public void StartAnimation_AfterDispose_DoesNotStart()
    {
        Action onTick = () => { };
        _service.Dispose();

        _service.StartAnimation(_layer, onTick);

        _service.IsAnimationEnabled.Should().BeFalse("because animation should not start after dispose");
    }

    [Fact]
    public void StopAnimation_StopsAnimation()
    {
        Action onTick = () => { };
        _service.StartAnimation(_layer, onTick);
        _service.IsAnimationEnabled.Should().BeTrue();

        _service.StopAnimation();

        _service.IsAnimationEnabled.Should().BeFalse("because animation should be stopped");
        _indicatorService.IsNavigating.Should().BeFalse("because navigating flag should be cleared");
    }

    [Fact]
    public void StopAnimation_WhenNotRunning_DoesNotThrow()
    {
        var action = () => _service.StopAnimation();
        action.Should().NotThrow("because stopping non-running animation should be safe");
    }

    [Fact]
    public void StopAnimation_UpdatesFeaturesWithStaticDisplay()
    {
        var location = new LocationData(51.5074, -0.1278) { Accuracy = 25.0 };
        _service.UpdateLocation(_layer, location);
        _service.StartAnimation(_layer, () => { });
        _layer.ResetCounts();

        _service.StopAnimation();

        _layer.DataChangedCount.Should().BeGreaterThan(0,
            "because features should be updated to static display");
    }

    [Fact]
    public void StopAnimation_NoLastLocation_DoesNotUpdateFeatures()
    {
        _service.StartAnimation(_layer, () => { });
        _layer.ResetCounts();

        _service.StopAnimation();

        _layer.DataChangedCount.Should().Be(0,
            "because there are no features to update without a location");
    }

    #endregion

    #region SetNavigationState Tests

    [Fact]
    public void SetNavigationState_OnRoute_SetsIsOnRoute()
    {
        _service.SetNavigationState(isOnRoute: true);

        _indicatorService.IsOnRoute.Should().BeTrue();
    }

    [Fact]
    public void SetNavigationState_OffRoute_ClearsIsOnRoute()
    {
        _service.SetNavigationState(isOnRoute: false);

        _indicatorService.IsOnRoute.Should().BeFalse();
    }

    [Fact]
    public void SetNavigationState_AffectsIndicatorColor()
    {
        var location = new LocationData(51.5074, -0.1278);
        _indicatorService.CalculateBestHeading(location);
        _indicatorService.IsNavigating = true;

        _service.SetNavigationState(isOnRoute: true);
        _indicatorService.GetIndicatorColor().Should().Be("#4285F4", "because on-route shows blue");

        _service.SetNavigationState(isOnRoute: false);
        _indicatorService.GetIndicatorColor().Should().Be("#FBBC04", "because off-route shows orange");
    }

    #endregion

    #region Feature Reuse Pattern Tests

    [Fact]
    public void FeatureReuse_MultipleUpdates_ReusesFeatures()
    {
        var locations = Enumerable.Range(0, 10).Select(i =>
            new LocationData(51.5074 + i * 0.0001, -0.1278)
            {
                Speed = 5.0,
                Bearing = 90.0,
                Accuracy = 25.0
            }).ToList();

        foreach (var location in locations)
        {
            _service.UpdateLocation(_layer, location);
        }

        _layer.AddCount.Should().Be(3, "because features should be added once and then reused");
    }

    [Fact]
    public void FeatureReuse_AccuracyComesAndGoes_AddsAndRemoves()
    {
        var locationWithAccuracy = new LocationData(51.5074, -0.1278) { Accuracy = 25.0 };
        var locationWithoutAccuracy = new LocationData(51.5074, -0.1278) { Accuracy = 0 };

        _service.UpdateLocation(_layer, locationWithAccuracy);
        var addCountWithAccuracy = _layer.AddCount;

        _service.UpdateLocation(_layer, locationWithoutAccuracy);
        var removeCountAfterNoAccuracy = _layer.RemoveCount;

        _service.UpdateLocation(_layer, locationWithAccuracy);
        var addCountAfterReAdd = _layer.AddCount;

        addCountWithAccuracy.Should().Be(2, "because marker + accuracy = 2 adds initially");
        removeCountAfterNoAccuracy.Should().Be(1, "because accuracy feature should be removed");
        addCountAfterReAdd.Should().Be(3, "because accuracy feature is re-added");
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_StopsAnimation()
    {
        _service.StartAnimation(_layer, () => { });
        _service.IsAnimationEnabled.Should().BeTrue();

        _service.Dispose();

        _service.IsAnimationEnabled.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ClearsFeatureReferences()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0,
            Accuracy = 25.0
        };
        _service.UpdateLocation(_layer, location);

        _service.Dispose();

        _service.HasMarkerFeature.Should().BeFalse("because marker reference should be cleared");
        _service.HasAccuracyFeature.Should().BeFalse("because accuracy reference should be cleared");
        _service.HasHeadingFeature.Should().BeFalse("because heading reference should be cleared");
        _service.LastMapPoint.Should().BeNull("because LastMapPoint should be cleared");
    }

    [Fact]
    public void Dispose_MultipleDisposeCalls_DoesNotThrow()
    {
        _service.Dispose();
        var action = () => _service.Dispose();
        action.Should().NotThrow("because multiple dispose calls should be safe");
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void Scenario_TrackingSession_FullLifecycle()
    {
        _service.HasMarkerFeature.Should().BeFalse();

        var location1 = new LocationData(51.5074, -0.1278)
        {
            Speed = 2.0,
            Bearing = 0.0,
            Accuracy = 15.0
        };
        _service.UpdateLocation(_layer, location1);
        _service.HasMarkerFeature.Should().BeTrue();
        _service.HasAccuracyFeature.Should().BeTrue();

        var location2 = new LocationData(51.5084, -0.1278)
        {
            Speed = 5.0,
            Bearing = 0.0,
            Accuracy = 10.0
        };
        _service.UpdateLocation(_layer, location2);
        _service.LastAccuracy.Should().Be(10.0);

        _service.ClearLocation(_layer);
        _service.HasMarkerFeature.Should().BeFalse();
        _service.LastMapPoint.Should().BeNull();
    }

    [Fact]
    public void Scenario_NavigationSession_WithAnimation()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 15.0,
            Bearing = 45.0,
            Accuracy = 5.0
        };
        _service.UpdateLocation(_layer, location);
        _service.StartAnimation(_layer, () => { });

        _service.SetNavigationState(isOnRoute: true);
        _indicatorService.IsOnRoute.Should().BeTrue();

        _service.SetNavigationState(isOnRoute: false);
        _indicatorService.IsOnRoute.Should().BeFalse();

        _service.SetNavigationState(isOnRoute: true);
        _indicatorService.IsOnRoute.Should().BeTrue();

        _service.StopAnimation();
        _service.IsAnimationEnabled.Should().BeFalse();
    }

    [Fact]
    public void Scenario_GpsSignalLoss_ShowsStaleLocation()
    {
        var location = new LocationData(51.5074, -0.1278)
        {
            Speed = 5.0,
            Bearing = 90.0,
            Accuracy = 10.0
        };
        _service.UpdateLocation(_layer, location);
        _indicatorService.CalculateBestHeading(location);

        var result = _service.ShowLastKnownLocation(_layer);
        result.Should().BeTrue();
        _service.LastMarkerColor.Should().Be("#9E9E9E", "because stale location shows gray");
        _service.HasAccuracyFeature.Should().BeFalse("because stale location has no accuracy circle");
        _service.HasHeadingFeature.Should().BeFalse("because stale location has no heading cone");
    }

    #endregion
}

#region Test Infrastructure

/// <summary>
/// Test implementation of WritableLayer that tracks operations.
/// </summary>
internal class TestWritableLayer
{
    public int FeatureCount { get; private set; }
    public int AddCount { get; private set; }
    public int RemoveCount { get; private set; }
    public int DataChangedCount { get; private set; }

    public void Add(object feature)
    {
        FeatureCount++;
        AddCount++;
    }

    public bool TryRemove(object feature)
    {
        if (FeatureCount > 0)
        {
            FeatureCount--;
            RemoveCount++;
            return true;
        }
        return false;
    }

    public void DataHasChanged()
    {
        DataChangedCount++;
    }

    public void ResetCounts()
    {
        DataChangedCount = 0;
        AddCount = 0;
        RemoveCount = 0;
    }
}

/// <summary>
/// Test point class simulating Mapsui MPoint.
/// </summary>
internal class TestMPoint
{
    public double X { get; }
    public double Y { get; }

    public TestMPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// Test implementation of LocationLayerService that mirrors production behavior.
/// </summary>
internal class TestLocationLayerService : IDisposable
{
    private readonly ILogger<TestLocationLayerService> _logger;
    private readonly TestLocationIndicatorService _indicatorService;

    private object? _accuracyFeature;
    private object? _headingFeature;
    private object? _markerFeature;
    private TestMPoint? _lastMapPoint;
    private double _lastAccuracy;
    private double _lastHeading = -1;

    private Timer? _animationTimer;
    private bool _animationEnabled;
    private bool _disposed;
    private TestWritableLayer? _animationLayer;
    private Action? _animationCallback;

    public string? LastMarkerColor { get; private set; }

    public TestLocationLayerService(
        ILogger<TestLocationLayerService> logger,
        TestLocationIndicatorService indicatorService)
    {
        _logger = logger;
        _indicatorService = indicatorService;
    }

    public string LocationLayerName => "CurrentLocation";
    public bool IsLocationStale => _indicatorService.IsLocationStale;
    public double SecondsSinceLastUpdate => _indicatorService.SecondsSinceLastUpdate;
    public TestMPoint? LastMapPoint => _lastMapPoint;
    public double LastAccuracy => _lastAccuracy;
    public double LastHeading => _lastHeading;

    public bool HasAccuracyFeature => _accuracyFeature != null;
    public bool HasHeadingFeature => _headingFeature != null;
    public bool HasMarkerFeature => _markerFeature != null;
    public bool IsAnimationEnabled => _animationEnabled;

    public void UpdateLocation(TestWritableLayer layer, LocationData location)
    {
        var point = new TestMPoint(location.Longitude * 111320, location.Latitude * 110574);
        _lastMapPoint = point;

        var heading = _indicatorService.CalculateBestHeading(location);

        var accuracy = location.Accuracy ?? 0;
        _lastAccuracy = accuracy;
        _lastHeading = heading;

        UpdateLocationFeatures(layer, point, accuracy, heading);

        layer.DataHasChanged();
    }

    private void UpdateLocationFeatures(TestWritableLayer layer, TestMPoint point, double accuracy, double heading)
    {
        var indicatorColor = _indicatorService.GetIndicatorColor();
        LastMarkerColor = indicatorColor;

        if (accuracy > 0)
        {
            if (_accuracyFeature == null)
            {
                _accuracyFeature = new object();
                layer.Add(_accuracyFeature);
            }
        }
        else if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }

        if (heading >= 0 && heading < 360)
        {
            if (_headingFeature == null)
            {
                _headingFeature = new object();
                layer.Add(_headingFeature);
            }
        }
        else if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
            _headingFeature = null;
        }

        if (_markerFeature == null)
        {
            _markerFeature = new object();
            layer.Add(_markerFeature);
        }
    }

    public void ClearLocation(TestWritableLayer layer)
    {
        if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
            _headingFeature = null;
        }
        if (_markerFeature != null)
        {
            layer.TryRemove(_markerFeature);
            _markerFeature = null;
        }

        _lastMapPoint = null;
        _lastAccuracy = 0;
        _lastHeading = -1;

        _indicatorService.Reset();
        layer.DataHasChanged();
    }

    public bool ShowLastKnownLocation(TestWritableLayer layer)
    {
        if (_indicatorService.LastKnownLocation == null)
            return false;

        var location = _indicatorService.LastKnownLocation;
        var point = new TestMPoint(location.Longitude * 111320, location.Latitude * 110574);
        _lastMapPoint = point;

        if (_accuracyFeature != null)
        {
            layer.TryRemove(_accuracyFeature);
            _accuracyFeature = null;
        }
        if (_headingFeature != null)
        {
            layer.TryRemove(_headingFeature);
            _headingFeature = null;
        }

        LastMarkerColor = "#9E9E9E";
        if (_markerFeature == null)
        {
            _markerFeature = new object();
            layer.Add(_markerFeature);
        }

        layer.DataHasChanged();
        return true;
    }

    public void StartAnimation(TestWritableLayer layer, Action onTick)
    {
        if (_animationEnabled || _disposed)
            return;

        _animationEnabled = true;
        _animationLayer = layer;
        _animationCallback = onTick;
        _indicatorService.IsNavigating = true;
    }

    public void StopAnimation()
    {
        if (!_animationEnabled)
            return;

        _animationEnabled = false;
        _indicatorService.IsNavigating = false;

        _animationTimer?.Dispose();
        _animationTimer = null;

        if (_lastMapPoint != null && _animationLayer != null)
        {
            UpdateLocationFeatures(_animationLayer, _lastMapPoint, _lastAccuracy, _lastHeading);
            _animationLayer.DataHasChanged();
        }

        _animationLayer = null;
        _animationCallback = null;
    }

    public void SetNavigationState(bool isOnRoute)
    {
        _indicatorService.IsOnRoute = isOnRoute;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAnimation();

        _accuracyFeature = null;
        _headingFeature = null;
        _markerFeature = null;
        _lastMapPoint = null;
        _animationLayer = null;
        _animationCallback = null;
    }
}

/// <summary>
/// Test implementation of LocationIndicatorService that mirrors production behavior.
/// </summary>
internal class TestLocationIndicatorService : IDisposable
{
    private const double MinSpeedForGpsCourse = 1.0;
    private const double BearingHoldDurationSeconds = 20.0;
    private const double LocationStaleDurationSeconds = 30.0;

    private readonly ILogger<TestLocationIndicatorService> _logger;
    private LocationData? _previousLocation;
    private LocationData? _lastKnownLocation;
    private double _calculatedHeading = -1;
    private DateTime _lastHeadingCalculation = DateTime.MinValue;
    private DateTime _lastLocationUpdate = DateTime.MinValue;
    private bool _disposed;

    public int ResetCallCount { get; private set; }

    public TestLocationIndicatorService(ILogger<TestLocationIndicatorService> logger)
    {
        _logger = logger;
    }

    public bool IsNavigating { get; set; }
    public bool IsOnRoute { get; set; } = true;
    public double PulseScale { get; private set; } = 1.0;
    public double CurrentHeading => _calculatedHeading;
    public bool HasValidHeading => _calculatedHeading >= 0;
    public LocationData? LastKnownLocation => _lastKnownLocation;
    public double ConeAngle => 45.0;

    public bool IsLocationStale =>
        _lastLocationUpdate != DateTime.MinValue &&
        (DateTime.UtcNow - _lastLocationUpdate).TotalSeconds > LocationStaleDurationSeconds;

    public double SecondsSinceLastUpdate =>
        _lastLocationUpdate == DateTime.MinValue ? double.MaxValue :
        (DateTime.UtcNow - _lastLocationUpdate).TotalSeconds;

    public double CalculateBestHeading(LocationData currentLocation)
    {
        if (_disposed || currentLocation == null)
            return -1;

        _lastLocationUpdate = DateTime.UtcNow;
        _lastKnownLocation = currentLocation;

        if (currentLocation.Bearing.HasValue &&
            currentLocation.Bearing >= 0 &&
            currentLocation.Bearing < 360 &&
            currentLocation.Speed.HasValue &&
            currentLocation.Speed >= MinSpeedForGpsCourse)
        {
            _calculatedHeading = currentLocation.Bearing.Value;
            _lastHeadingCalculation = DateTime.UtcNow;
            return _calculatedHeading;
        }

        if (_calculatedHeading >= 0)
        {
            var timeSinceLastCalculation = DateTime.UtcNow - _lastHeadingCalculation;
            if (timeSinceLastCalculation.TotalSeconds < BearingHoldDurationSeconds)
            {
                return _calculatedHeading;
            }
        }

        _previousLocation = currentLocation;
        return -1;
    }

    public void UpdateAnimation()
    {
        if (!IsNavigating)
        {
            PulseScale = 1.0;
            return;
        }
        PulseScale = 1.0 + Math.Sin(DateTime.UtcNow.Ticks) * 0.15;
    }

    public string GetIndicatorColor()
    {
        if (IsLocationStale)
            return "#9E9E9E";

        if (IsNavigating && !IsOnRoute)
            return "#FBBC04";

        return "#4285F4";
    }

    public void Reset()
    {
        ResetCallCount++;
        _previousLocation = null;
        _calculatedHeading = -1;
        _lastHeadingCalculation = DateTime.MinValue;
        PulseScale = 1.0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

#endregion
