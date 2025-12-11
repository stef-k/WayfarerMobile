namespace WayfarerMobile.Tests.Unit.Navigation;

/// <summary>
/// Unit tests for TripNavigationGraph A* pathfinding and graph operations.
/// </summary>
public class TripNavigationGraphTests
{
    #region AddNode / AddEdge Tests

    [Fact]
    public void AddNode_AddsNodeToGraph()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        var node = CreateNode("A", "Place A", 51.5074, -0.1278);

        // Act
        graph.AddNode(node);

        // Assert
        graph.Nodes.Should().ContainKey("A");
        graph.Nodes["A"].Should().Be(node);
    }

    [Fact]
    public void AddNode_OverwritesExistingNode()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        var node1 = CreateNode("A", "Place A", 51.5074, -0.1278);
        var node2 = CreateNode("A", "Place A Updated", 52.0, -0.5);

        // Act
        graph.AddNode(node1);
        graph.AddNode(node2);

        // Assert
        graph.Nodes.Should().HaveCount(1);
        graph.Nodes["A"].Name.Should().Be("Place A Updated");
    }

    [Fact]
    public void AddEdge_AddsEdgeToGraph()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        var edge = CreateEdge("A", "B", 1.5);

        // Act
        graph.AddEdge(edge);

        // Assert
        graph.Edges.Should().Contain(edge);
    }

    #endregion

    #region FindNearestNode Tests

    [Fact]
    public void FindNearestNode_EmptyGraph_ReturnsNull()
    {
        // Arrange
        var graph = new TripNavigationGraph();

        // Act
        var result = graph.FindNearestNode(51.5074, -0.1278);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FindNearestNode_SingleNode_ReturnsThatNode()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        var node = CreateNode("A", "Place A", 51.5074, -0.1278);
        graph.AddNode(node);

        // Act
        var result = graph.FindNearestNode(0, 0); // Far away

        // Assert
        result.Should().Be(node);
    }

    [Fact]
    public void FindNearestNode_MultipleNodes_ReturnsNearest()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Query point is at node A's location
        // Act
        var result = graph.FindNearestNode(51.5074, -0.1278);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("A");
    }

    [Fact]
    public void FindNearestNode_QueryBetweenNodes_ReturnsClosest()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Place B", 0.0, 1.0)); // ~111km east

        // Query point slightly closer to A
        // Act
        var result = graph.FindNearestNode(0.0, 0.3); // Closer to A

        // Assert
        result!.Id.Should().Be("A");

        // Query point slightly closer to B
        result = graph.FindNearestNode(0.0, 0.7); // Closer to B
        result!.Id.Should().Be("B");
    }

    #endregion

    #region FindPath (A* Algorithm) Tests

    [Fact]
    public void FindPath_StartNotInGraph_ReturnsEmptyList()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var path = graph.FindPath("X", "C");

        // Assert
        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_EndNotInGraph_ReturnsEmptyList()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var path = graph.FindPath("A", "X");

        // Assert
        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_SameStartAndEnd_ReturnsSingleNode()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var path = graph.FindPath("A", "A");

        // Assert
        path.Should().Equal("A");
    }

    [Fact]
    public void FindPath_DirectConnection_ReturnsDirectPath()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var path = graph.FindPath("A", "B");

        // Assert
        path.Should().Equal("A", "B");
    }

    [Fact]
    public void FindPath_IndirectConnection_ReturnsShortestPath()
    {
        // Arrange - Graph: A -> B -> C
        var graph = CreateLinearGraph();

        // Act
        var path = graph.FindPath("A", "C");

        // Assert
        path.Should().Equal("A", "B", "C");
    }

    [Fact]
    public void FindPath_NoConnection_ReturnsEmptyList()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));
        graph.AddNode(CreateNode("B", "Place B", 52.0, -0.5));
        // No edges

        // Act
        var path = graph.FindPath("A", "B");

        // Assert
        path.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_MultiplePathsAvailable_ReturnsShortestByDistance()
    {
        // Arrange - Diamond graph:
        //     B (short)
        //    / \
        //   A   D
        //    \ /
        //     C (long)
        var graph = CreateDiamondGraph();

        // Act
        var path = graph.FindPath("A", "D");

        // Assert - Should go through B (shorter path)
        path.Should().Equal("A", "B", "D");
    }

    [Fact]
    public void FindPath_ComplexGraph_FindsOptimalPath()
    {
        // Arrange - More complex graph with multiple routes
        var graph = CreateComplexGraph();

        // Act
        var path = graph.FindPath("A", "E");

        // Assert - Should find a valid path
        path.Should().NotBeEmpty();
        path.First().Should().Be("A");
        path.Last().Should().Be("E");
    }

    #endregion

    #region GetEdgesFromNode Tests

    [Fact]
    public void GetEdgesFromNode_NoEdges_ReturnsEmpty()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));

        // Act
        var edges = graph.GetEdgesFromNode("A").ToList();

        // Assert
        edges.Should().BeEmpty();
    }

    [Fact]
    public void GetEdgesFromNode_HasEdges_ReturnsCorrectEdges()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var edges = graph.GetEdgesFromNode("A").ToList();

        // Assert
        edges.Should().HaveCount(2); // A->B and A->C
        edges.Should().AllSatisfy(e => e.FromNodeId.Should().Be("A"));
    }

    [Fact]
    public void GetEdgesFromNode_NonExistentNode_ReturnsEmpty()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var edges = graph.GetEdgesFromNode("X").ToList();

        // Assert
        edges.Should().BeEmpty();
    }

    #endregion

    #region GetEdgeBetween / HasEdgeBetween Tests

    [Fact]
    public void GetEdgeBetween_EdgeExists_ReturnsEdge()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var edge = graph.GetEdgeBetween("A", "B");

        // Assert
        edge.Should().NotBeNull();
        edge!.FromNodeId.Should().Be("A");
        edge.ToNodeId.Should().Be("B");
    }

    [Fact]
    public void GetEdgeBetween_EdgeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act
        var edge = graph.GetEdgeBetween("B", "A"); // Reverse direction

        // Assert
        edge.Should().BeNull();
    }

    [Fact]
    public void HasEdgeBetween_EdgeExists_ReturnsTrue()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act & Assert
        graph.HasEdgeBetween("A", "B").Should().BeTrue();
    }

    [Fact]
    public void HasEdgeBetween_EdgeDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var graph = CreateSimpleGraph();

        // Act & Assert
        graph.HasEdgeBetween("B", "A").Should().BeFalse();
        graph.HasEdgeBetween("X", "Y").Should().BeFalse();
    }

    #endregion

    #region IsWithinSegmentRoutingRange Tests

    [Fact]
    public void IsWithinSegmentRoutingRange_EmptyGraph_ReturnsFalse()
    {
        // Arrange
        var graph = new TripNavigationGraph();

        // Act
        bool result = graph.IsWithinSegmentRoutingRange(51.5074, -0.1278);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsWithinSegmentRoutingRange_ExactlyOnNode_ReturnsTrue()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));

        // Act
        bool result = graph.IsWithinSegmentRoutingRange(51.5074, -0.1278);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsWithinSegmentRoutingRange_WithinThreshold_ReturnsTrue()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));

        // Move 30m away (within 50m threshold)
        var (newLat, newLon) = GeoMath.CalculateDestination(51.5074, -0.1278, 45, 30);

        // Act
        bool result = graph.IsWithinSegmentRoutingRange(newLat, newLon);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsWithinSegmentRoutingRange_OutsideThreshold_ReturnsFalse()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));

        // Move 100m away (outside 50m threshold)
        var (newLat, newLon) = GeoMath.CalculateDestination(51.5074, -0.1278, 45, 100);

        // Act
        bool result = graph.IsWithinSegmentRoutingRange(newLat, newLon);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsOffRoute Tests

    [Fact]
    public void IsOffRoute_OnDirectLine_ReturnsFalse()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Place B", 0.0, 1.0));
        var edge = CreateEdge("A", "B", 111);
        graph.AddEdge(edge);

        // Point on the line (at longitude 0.5)
        // Act
        bool result = graph.IsOffRoute(0.0, 0.5, edge);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsOffRoute_FarFromLine_ReturnsTrue()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Place B", 0.0, 1.0));
        var edge = CreateEdge("A", "B", 111);
        graph.AddEdge(edge);

        // Point far north of the line (~1 degree = 111km)
        // Act
        bool result = graph.IsOffRoute(1.0, 0.5, edge);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOffRoute_WithDetailedGeometry_ChecksAgainstRoute()
    {
        // Arrange
        var graph = new TripNavigationGraph();
        graph.AddNode(CreateNode("A", "Place A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Place B", 0.0, 2.0));

        var edge = CreateEdge("A", "B", 222);
        // Route goes via point at (0.5, 1.0) - a detour north
        edge.RouteGeometry = new List<RoutePoint>
        {
            new() { Latitude = 0.0, Longitude = 0.0 },
            new() { Latitude = 0.5, Longitude = 1.0 },
            new() { Latitude = 0.0, Longitude = 2.0 }
        };
        graph.AddEdge(edge);

        // Point near the route detour
        var (nearLat, nearLon) = GeoMath.CalculateDestination(0.5, 1.0, 90, 50); // 50m from detour point

        // Act
        bool result = graph.IsOffRoute(nearLat, nearLon, edge);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static NavigationNode CreateNode(string id, string name, double lat, double lon)
    {
        return new NavigationNode
        {
            Id = id,
            Name = name,
            Latitude = lat,
            Longitude = lon,
            Type = NavigationNodeType.Place
        };
    }

    private static NavigationEdge CreateEdge(string from, string to, double distanceKm)
    {
        return new NavigationEdge
        {
            FromNodeId = from,
            ToNodeId = to,
            DistanceKm = distanceKm,
            TransportMode = "walking",
            EdgeType = NavigationEdgeType.UserSegment
        };
    }

    /// <summary>
    /// Creates a simple triangle graph: A -> B, A -> C, B -> C
    /// </summary>
    private static TripNavigationGraph CreateSimpleGraph()
    {
        var graph = new TripNavigationGraph { TripId = Guid.NewGuid() };

        graph.AddNode(CreateNode("A", "Place A", 51.5074, -0.1278));
        graph.AddNode(CreateNode("B", "Place B", 51.51, -0.13));
        graph.AddNode(CreateNode("C", "Place C", 51.505, -0.12));

        graph.AddEdge(CreateEdge("A", "B", 0.5));
        graph.AddEdge(CreateEdge("A", "C", 0.8));
        graph.AddEdge(CreateEdge("B", "C", 0.3));

        return graph;
    }

    /// <summary>
    /// Creates a linear graph: A -> B -> C
    /// </summary>
    private static TripNavigationGraph CreateLinearGraph()
    {
        var graph = new TripNavigationGraph { TripId = Guid.NewGuid() };

        graph.AddNode(CreateNode("A", "Place A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Place B", 0.0, 1.0));
        graph.AddNode(CreateNode("C", "Place C", 0.0, 2.0));

        graph.AddEdge(CreateEdge("A", "B", 111)); // ~111km at equator
        graph.AddEdge(CreateEdge("B", "C", 111));

        return graph;
    }

    /// <summary>
    /// Creates a diamond graph where path through B is shorter than through C.
    /// </summary>
    private static TripNavigationGraph CreateDiamondGraph()
    {
        var graph = new TripNavigationGraph { TripId = Guid.NewGuid() };

        graph.AddNode(CreateNode("A", "Start", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "Upper", 0.5, 0.5));
        graph.AddNode(CreateNode("C", "Lower", -0.5, 0.5));
        graph.AddNode(CreateNode("D", "End", 0.0, 1.0));

        // Path through B: 1 + 1 = 2
        graph.AddEdge(CreateEdge("A", "B", 1.0));
        graph.AddEdge(CreateEdge("B", "D", 1.0));

        // Path through C: 2 + 2 = 4 (longer)
        graph.AddEdge(CreateEdge("A", "C", 2.0));
        graph.AddEdge(CreateEdge("C", "D", 2.0));

        return graph;
    }

    /// <summary>
    /// Creates a more complex graph for testing.
    /// </summary>
    private static TripNavigationGraph CreateComplexGraph()
    {
        var graph = new TripNavigationGraph { TripId = Guid.NewGuid() };

        // Grid-like structure
        graph.AddNode(CreateNode("A", "A", 0.0, 0.0));
        graph.AddNode(CreateNode("B", "B", 0.0, 1.0));
        graph.AddNode(CreateNode("C", "C", 1.0, 0.0));
        graph.AddNode(CreateNode("D", "D", 1.0, 1.0));
        graph.AddNode(CreateNode("E", "E", 1.0, 2.0));

        graph.AddEdge(CreateEdge("A", "B", 1.0));
        graph.AddEdge(CreateEdge("A", "C", 1.0));
        graph.AddEdge(CreateEdge("B", "D", 1.0));
        graph.AddEdge(CreateEdge("C", "D", 1.0));
        graph.AddEdge(CreateEdge("D", "E", 1.0));
        graph.AddEdge(CreateEdge("B", "E", 2.0)); // Direct but longer

        return graph;
    }

    #endregion
}
