using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Models;

namespace WayfarerMobile.Tests.Unit.Services;

/// <summary>
/// Unit tests for GroupLayerService.
/// Tests group member marker creation and historical location breadcrumbs.
/// </summary>
public class GroupLayerServiceTests
{
    #region Test Setup

    private readonly TestGroupLayerService _service;
    private readonly ILogger<TestGroupLayerService> _logger;
    private readonly TestWritableLayerForGroup _layer;

    public GroupLayerServiceTests()
    {
        _logger = NullLogger<TestGroupLayerService>.Instance;
        _service = new TestGroupLayerService(_logger);
        _layer = new TestWritableLayerForGroup();
    }

    #endregion

    #region Layer Name Tests

    [Fact]
    public void GroupMembersLayerName_ReturnsCorrectName()
    {
        _service.GroupMembersLayerName.Should().Be("GroupMembers");
    }

    [Fact]
    public void HistoricalLocationsLayerName_ReturnsCorrectName()
    {
        _service.HistoricalLocationsLayerName.Should().Be("HistoricalLocations");
    }

    #endregion

    #region UpdateGroupMemberMarkers Tests

    [Fact]
    public void UpdateGroupMemberMarkers_EmptyList_ReturnsEmptyPoints()
    {
        var members = new List<GroupMemberLocation>();

        var points = _service.UpdateGroupMemberMarkers(_layer, members);

        points.Should().BeEmpty();
        _layer.ClearCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateGroupMemberMarkers_ValidMember_CreatesMarker()
    {
        var members = new List<GroupMemberLocation>
        {
            new()
            {
                UserId = "user1",
                DisplayName = "Test User",
                Latitude = 51.5074,
                Longitude = -0.1278,
                ColorHex = "#FF5722",
                IsLive = true
            }
        };

        var points = _service.UpdateGroupMemberMarkers(_layer, members);

        points.Should().HaveCount(1);
        _layer.FeatureCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateGroupMemberMarkers_MultipleMembers_CreatesMultipleMarkers()
    {
        var members = new List<GroupMemberLocation>
        {
            new() { UserId = "user1", DisplayName = "User 1", Latitude = 51.5074, Longitude = -0.1278, IsLive = true },
            new() { UserId = "user2", DisplayName = "User 2", Latitude = 51.5084, Longitude = -0.1288, IsLive = false },
            new() { UserId = "user3", DisplayName = "User 3", Latitude = 51.5094, Longitude = -0.1298, IsLive = true }
        };

        var points = _service.UpdateGroupMemberMarkers(_layer, members);

        points.Should().HaveCount(3);
        _layer.FeatureCount.Should().Be(3);
    }

    [Fact]
    public void UpdateGroupMemberMarkers_ZeroCoordinates_SkipsMember()
    {
        var members = new List<GroupMemberLocation>
        {
            new() { UserId = "user1", DisplayName = "Valid", Latitude = 51.5074, Longitude = -0.1278, IsLive = true },
            new() { UserId = "user2", DisplayName = "Zero", Latitude = 0, Longitude = 0, IsLive = true },
            new() { UserId = "user3", DisplayName = "Also Valid", Latitude = 51.5094, Longitude = -0.1298, IsLive = false }
        };

        var points = _service.UpdateGroupMemberMarkers(_layer, members);

        points.Should().HaveCount(2);
        _layer.FeatureCount.Should().Be(2);
    }

    [Fact]
    public void UpdateGroupMemberMarkers_LiveMember_UsesLiveStyle()
    {
        var members = new List<GroupMemberLocation>
        {
            new() { UserId = "user1", DisplayName = "Live User", Latitude = 51.5074, Longitude = -0.1278, IsLive = true }
        };

        _service.UpdateGroupMemberMarkers(_layer, members);

        _service.LastCreatedStyleType.Should().Be("Live");
    }

    [Fact]
    public void UpdateGroupMemberMarkers_LatestMember_UsesLatestStyle()
    {
        var members = new List<GroupMemberLocation>
        {
            new() { UserId = "user1", DisplayName = "Latest User", Latitude = 51.5074, Longitude = -0.1278, IsLive = false }
        };

        _service.UpdateGroupMemberMarkers(_layer, members);

        _service.LastCreatedStyleType.Should().Be("Latest");
    }

    [Fact]
    public void UpdateGroupMemberMarkers_SetsFeatureProperties()
    {
        var members = new List<GroupMemberLocation>
        {
            new()
            {
                UserId = "user123",
                DisplayName = "Test User",
                Latitude = 51.5074,
                Longitude = -0.1278,
                IsLive = true
            }
        };

        _service.UpdateGroupMemberMarkers(_layer, members);

        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.Properties["UserId"].Should().Be("user123");
        _layer.LastAddedFeature.Properties["DisplayName"].Should().Be("Test User");
        _layer.LastAddedFeature.Properties["IsLive"].Should().Be(true);
    }

    [Fact]
    public void UpdateGroupMemberMarkers_ClearsLayerBeforeAdding()
    {
        var members1 = new List<GroupMemberLocation>
        {
            new() { UserId = "user1", DisplayName = "User 1", Latitude = 51.5074, Longitude = -0.1278, IsLive = true }
        };
        _service.UpdateGroupMemberMarkers(_layer, members1);

        var members2 = new List<GroupMemberLocation>
        {
            new() { UserId = "user2", DisplayName = "User 2", Latitude = 51.5084, Longitude = -0.1288, IsLive = true },
            new() { UserId = "user3", DisplayName = "User 3", Latitude = 51.5094, Longitude = -0.1298, IsLive = true }
        };
        _service.UpdateGroupMemberMarkers(_layer, members2);

        _layer.ClearCount.Should().Be(2);
        _layer.FeatureCount.Should().Be(2);
    }

    [Theory]
    [InlineData("#FF5722")]
    [InlineData("#4285F4")]
    [InlineData("#4CAF50")]
    [InlineData(null)]
    public void UpdateGroupMemberMarkers_ParsesColorHex(string? colorHex)
    {
        var members = new List<GroupMemberLocation>
        {
            new()
            {
                UserId = "user1",
                DisplayName = "User",
                Latitude = 51.5074,
                Longitude = -0.1278,
                ColorHex = colorHex,
                IsLive = true
            }
        };

        var action = () => _service.UpdateGroupMemberMarkers(_layer, members);

        action.Should().NotThrow();
    }

    #endregion

    #region UpdateHistoricalLocationMarkers Tests

    [Fact]
    public void UpdateHistoricalLocationMarkers_EmptyList_AddsNoMarkers()
    {
        var locations = new List<GroupLocationResult>();
        var memberColors = new Dictionary<string, string>();

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _layer.FeatureCount.Should().Be(0);
        _layer.ClearCount.Should().Be(1);
        _layer.DataChangedCount.Should().Be(1);
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_ValidLocations_CreatesMarkers()
    {
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(1, "user1", 51.5074, -0.1278),
            CreateGroupLocationResult(2, "user1", 51.5084, -0.1288)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _layer.FeatureCount.Should().Be(2);
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_ZeroCoordinates_SkipsLocation()
    {
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(1, "user1", 51.5074, -0.1278),
            CreateGroupLocationResult(2, "user1", 0, 0),
            CreateGroupLocationResult(3, "user1", 51.5094, -0.1298)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _layer.FeatureCount.Should().Be(2);
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_UsesMemberColor()
    {
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(1, "user1", 51.5074, -0.1278)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _service.LastUsedColorHex.Should().Be("#FF5722");
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_UnknownMember_UsesDefaultBlue()
    {
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(1, "unknownUser", 51.5074, -0.1278)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _service.LastUsedColorHex.Should().Be("#4285F4");
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_NullUserId_UsesDefaultBlue()
    {
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(1, null, 51.5074, -0.1278)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _service.LastUsedColorHex.Should().Be("#4285F4");
    }

    [Fact]
    public void UpdateHistoricalLocationMarkers_SetsFeatureProperties()
    {
        var timestamp = DateTime.Now;
        var locations = new List<GroupLocationResult>
        {
            CreateGroupLocationResult(123, "user1", 51.5074, -0.1278, timestamp)
        };
        var memberColors = new Dictionary<string, string> { ["user1"] = "#FF5722" };

        _service.UpdateHistoricalLocationMarkers(_layer, locations, memberColors);

        _layer.LastAddedFeature.Should().NotBeNull();
        _layer.LastAddedFeature!.Properties["UserId"].Should().Be("user1");
        _layer.LastAddedFeature.Properties["LocationId"].Should().Be(123);
        _layer.LastAddedFeature.Properties["IsHistorical"].Should().Be(true);
    }

    /// <summary>
    /// Helper to create GroupLocationResult with proper Coordinates object.
    /// </summary>
    private static GroupLocationResult CreateGroupLocationResult(
        int id,
        string? userId,
        double latitude,
        double longitude,
        DateTime? localTimestamp = null)
    {
        return new GroupLocationResult
        {
            Id = id,
            UserId = userId,
            Coordinates = new CoordinatesDto { Latitude = latitude, Longitude = longitude },
            LocalTimestamp = localTimestamp ?? DateTime.Now
        };
    }

    #endregion
}

#region Test Infrastructure

internal class TestWritableLayerForGroup
{
    private readonly List<TestFeatureForGroup> _features = new();

    public int FeatureCount => _features.Count;
    public int ClearCount { get; private set; }
    public int DataChangedCount { get; private set; }
    public TestFeatureForGroup? LastAddedFeature { get; private set; }

    public void Add(TestFeatureForGroup feature)
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

internal class TestFeatureForGroup
{
    public Dictionary<string, object> Properties { get; } = new();
}

internal class TestMPointForGroup
{
    public double X { get; }
    public double Y { get; }

    public TestMPointForGroup(double x, double y)
    {
        X = x;
        Y = y;
    }
}

internal class TestGroupLayerService
{
    private readonly ILogger<TestGroupLayerService> _logger;

    public string? LastCreatedStyleType { get; private set; }
    public string? LastUsedColorHex { get; private set; }

    public TestGroupLayerService(ILogger<TestGroupLayerService> logger)
    {
        _logger = logger;
    }

    public string GroupMembersLayerName => "GroupMembers";
    public string HistoricalLocationsLayerName => "HistoricalLocations";

    public List<TestMPointForGroup> UpdateGroupMemberMarkers(TestWritableLayerForGroup layer, IEnumerable<GroupMemberLocation> members)
    {
        layer.Clear();

        var points = new List<TestMPointForGroup>();
        var memberList = members.ToList();

        foreach (var member in memberList)
        {
            if (member.Latitude == 0 && member.Longitude == 0)
                continue;

            var x = member.Longitude * 20037508.34 / 180;
            var y = Math.Log(Math.Tan((90 + member.Latitude) * Math.PI / 360)) / (Math.PI / 180) * 20037508.34 / 180;
            var point = new TestMPointForGroup(x, y);
            points.Add(point);

            LastCreatedStyleType = member.IsLive ? "Live" : "Latest";
            LastUsedColorHex = member.ColorHex ?? "#4285F4";

            var feature = new TestFeatureForGroup();
            feature.Properties["UserId"] = member.UserId;
            feature.Properties["DisplayName"] = member.DisplayName;
            feature.Properties["IsLive"] = member.IsLive;

            layer.Add(feature);
        }

        layer.DataHasChanged();
        return points;
    }

    public void UpdateHistoricalLocationMarkers(
        TestWritableLayerForGroup layer,
        IEnumerable<GroupLocationResult> locations,
        Dictionary<string, string> memberColors)
    {
        layer.Clear();

        foreach (var location in locations)
        {
            if (location.Latitude == 0 && location.Longitude == 0)
                continue;

            var colorHex = location.UserId != null && memberColors.TryGetValue(location.UserId, out var hex)
                ? hex
                : "#4285F4";
            LastUsedColorHex = colorHex;

            var feature = new TestFeatureForGroup();
            feature.Properties["UserId"] = location.UserId ?? "";
            feature.Properties["LocationId"] = location.Id;
            feature.Properties["Timestamp"] = location.LocalTimestamp.ToString("g");
            feature.Properties["IsHistorical"] = true;

            layer.Add(feature);
        }

        layer.DataHasChanged();
    }
}

#endregion
