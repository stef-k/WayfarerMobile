using WayfarerMobile.Core.Algorithms;

namespace WayfarerMobile.Core.Navigation;

/// <summary>
/// Local routing graph created from trip data.
/// Enables pathfinding and rerouting within trip area.
/// </summary>
public class TripNavigationGraph
{
    /// <summary>
    /// Gets or sets the trip ID.
    /// </summary>
    public Guid TripId { get; set; }

    /// <summary>
    /// Gets the navigation nodes (places).
    /// </summary>
    public Dictionary<string, NavigationNode> Nodes { get; } = new();

    /// <summary>
    /// Gets the navigation edges (routes between places).
    /// </summary>
    public List<NavigationEdge> Edges { get; } = new();

    /// <summary>
    /// Proximity threshold for segment routing (meters).
    /// Navigation uses segments when within this distance of a trip place.
    /// </summary>
    public const double SegmentRoutingThresholdMeters = 50;

    /// <summary>
    /// Off-route threshold (meters).
    /// </summary>
    public const double OffRouteThresholdMeters = 100;

    /// <summary>
    /// Adds a node to the graph.
    /// </summary>
    public void AddNode(NavigationNode node)
    {
        Nodes[node.Id] = node;
    }

    /// <summary>
    /// Adds an edge to the graph.
    /// </summary>
    public void AddEdge(NavigationEdge edge)
    {
        Edges.Add(edge);
    }

    /// <summary>
    /// Finds the nearest node to a location.
    /// </summary>
    /// <param name="latitude">Latitude.</param>
    /// <param name="longitude">Longitude.</param>
    /// <returns>The nearest node or null if no nodes exist.</returns>
    public NavigationNode? FindNearestNode(double latitude, double longitude)
    {
        if (Nodes.Count == 0)
            return null;

        NavigationNode? nearest = null;
        double minDistance = double.MaxValue;

        foreach (var node in Nodes.Values)
        {
            var distance = GeoMath.CalculateDistance(latitude, longitude, node.Latitude, node.Longitude);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = node;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Finds a route between two nodes using A* algorithm.
    /// </summary>
    /// <param name="startId">Start node ID.</param>
    /// <param name="endId">End node ID.</param>
    /// <returns>List of node IDs in the path, or empty if no path found.</returns>
    public List<string> FindPath(string startId, string endId)
    {
        if (!Nodes.ContainsKey(startId) || !Nodes.ContainsKey(endId))
            return new List<string>();

        var openSet = new PriorityQueue<string, double>();
        var gScore = new Dictionary<string, double> { [startId] = 0 };
        var fScore = new Dictionary<string, double>();
        var cameFrom = new Dictionary<string, string>();
        var visited = new HashSet<string>();

        fScore[startId] = HeuristicDistance(startId, endId);
        openSet.Enqueue(startId, fScore[startId]);

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (visited.Contains(current))
                continue;

            visited.Add(current);

            if (current == endId)
                return ReconstructPath(cameFrom, current);

            foreach (var edge in GetEdgesFromNode(current))
            {
                var neighbor = edge.ToNodeId;
                if (visited.Contains(neighbor))
                    continue;

                var tentativeGScore = gScore[current] + edge.DistanceKm * 1000; // Convert to meters

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = tentativeGScore + HeuristicDistance(neighbor, endId);
                    openSet.Enqueue(neighbor, fScore[neighbor]);
                }
            }
        }

        return new List<string>(); // No path found
    }

    /// <summary>
    /// Calculates heuristic distance (straight-line) between two nodes.
    /// </summary>
    private double HeuristicDistance(string fromId, string toId)
    {
        if (!Nodes.TryGetValue(fromId, out var from) || !Nodes.TryGetValue(toId, out var to))
            return double.MaxValue;

        return GeoMath.CalculateDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
    }

    /// <summary>
    /// Reconstructs the path from A* search.
    /// </summary>
    private static List<string> ReconstructPath(Dictionary<string, string> cameFrom, string current)
    {
        var path = new List<string> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }
        return path;
    }

    /// <summary>
    /// Gets all edges from a node.
    /// </summary>
    public IEnumerable<NavigationEdge> GetEdgesFromNode(string nodeId)
    {
        return Edges.Where(e => e.FromNodeId == nodeId);
    }

    /// <summary>
    /// Gets the edge between two nodes if it exists.
    /// </summary>
    public NavigationEdge? GetEdgeBetween(string fromId, string toId)
    {
        return Edges.FirstOrDefault(e => e.FromNodeId == fromId && e.ToNodeId == toId);
    }

    /// <summary>
    /// Checks if an edge exists between two nodes.
    /// </summary>
    public bool HasEdgeBetween(string fromId, string toId)
    {
        return Edges.Any(e => e.FromNodeId == fromId && e.ToNodeId == toId);
    }


    /// <summary>
    /// Checks if the user is within the segment routing threshold of any trip place.
    /// When true, use segment-based routing instead of direct navigation.
    /// </summary>
    /// <param name="latitude">Current latitude.</param>
    /// <param name="longitude">Current longitude.</param>
    /// <returns>True if within threshold of any place.</returns>
    public bool IsWithinSegmentRoutingRange(double latitude, double longitude)
    {
        return Nodes.Values.Any(node =>
            GeoMath.CalculateDistance(latitude, longitude, node.Latitude, node.Longitude)
            <= SegmentRoutingThresholdMeters);
    }

    /// <summary>
    /// Checks if the user is off-route from a given edge.
    /// </summary>
    /// <param name="latitude">Current latitude.</param>
    /// <param name="longitude">Current longitude.</param>
    /// <param name="edge">The expected edge.</param>
    /// <returns>True if off-route.</returns>
    public bool IsOffRoute(double latitude, double longitude, NavigationEdge edge)
    {
        // If edge has detailed geometry, check distance to route line
        if (edge.HasDetailedGeometry)
        {
            var minDistance = CalculateDistanceToRoute(latitude, longitude, edge.RouteGeometry!);
            return minDistance > OffRouteThresholdMeters;
        }

        // Otherwise, check distance to straight line between nodes
        if (!Nodes.TryGetValue(edge.FromNodeId, out var from) ||
            !Nodes.TryGetValue(edge.ToNodeId, out var to))
            return false;

        var distanceToLine = CalculateDistanceToLine(
            latitude, longitude,
            from.Latitude, from.Longitude,
            to.Latitude, to.Longitude);

        return distanceToLine > OffRouteThresholdMeters;
    }

    /// <summary>
    /// Calculates the minimum distance from a point to a route polyline.
    /// </summary>
    private static double CalculateDistanceToRoute(double lat, double lon, List<RoutePoint> route)
    {
        double minDistance = double.MaxValue;

        for (int i = 0; i < route.Count - 1; i++)
        {
            var distance = CalculateDistanceToLine(
                lat, lon,
                route[i].Latitude, route[i].Longitude,
                route[i + 1].Latitude, route[i + 1].Longitude);

            if (distance < minDistance)
                minDistance = distance;
        }

        return minDistance;
    }

    /// <summary>
    /// Calculates the perpendicular distance from a point to a line segment.
    /// </summary>
    private static double CalculateDistanceToLine(
        double pointLat, double pointLon,
        double line1Lat, double line1Lon,
        double line2Lat, double line2Lon)
    {
        // Use haversine for endpoint distances
        var distToStart = GeoMath.CalculateDistance(pointLat, pointLon, line1Lat, line1Lon);
        var distToEnd = GeoMath.CalculateDistance(pointLat, pointLon, line2Lat, line2Lon);

        // For short segments, just return minimum endpoint distance
        var segmentLength = GeoMath.CalculateDistance(line1Lat, line1Lon, line2Lat, line2Lon);
        if (segmentLength < 1) // Less than 1 meter
            return Math.Min(distToStart, distToEnd);

        // Calculate cross-track distance using simplified formula
        var bearing1 = GeoMath.CalculateBearing(line1Lat, line1Lon, pointLat, pointLon);
        var bearing2 = GeoMath.CalculateBearing(line1Lat, line1Lon, line2Lat, line2Lon);
        var bearingDiff = Math.Abs(bearing1 - bearing2) * Math.PI / 180;

        var crossTrack = Math.Abs(Math.Asin(Math.Sin(distToStart / 6371000) * Math.Sin(bearingDiff)) * 6371000);

        // Check if perpendicular point is within segment
        var alongTrack = Math.Acos(Math.Cos(distToStart / 6371000) / Math.Cos(crossTrack / 6371000)) * 6371000;

        if (alongTrack > segmentLength)
            return distToEnd;
        if (alongTrack < 0)
            return distToStart;

        return crossTrack;
    }
}

/// <summary>
/// Navigation node (place in trip).
/// </summary>
public class NavigationNode
{
    /// <summary>
    /// Gets or sets the node ID (place ID).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the place name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Gets or sets the node type.
    /// </summary>
    public NavigationNodeType Type { get; set; } = NavigationNodeType.Place;

    /// <summary>
    /// Gets or sets the sort order for sequencing.
    /// </summary>
    public int? SortOrder { get; set; }

    /// <summary>
    /// Gets or sets the place notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the icon name.
    /// </summary>
    public string? IconName { get; set; }
}

/// <summary>
/// Types of navigation nodes.
/// </summary>
public enum NavigationNodeType
{
    /// <summary>Trip place.</summary>
    Place,
    /// <summary>Route waypoint.</summary>
    Waypoint
}

/// <summary>
/// Navigation edge (route between places).
/// </summary>
public class NavigationEdge
{
    /// <summary>
    /// Gets or sets the origin node ID.
    /// </summary>
    public string FromNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination node ID.
    /// </summary>
    public string ToNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport mode (walking, driving, etc.).
    /// </summary>
    public string TransportMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the distance in kilometers.
    /// </summary>
    public double DistanceKm { get; set; }

    /// <summary>
    /// Gets or sets the estimated duration in minutes.
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the detailed route geometry (encoded polyline decoded).
    /// </summary>
    public List<RoutePoint>? RouteGeometry { get; set; }

    /// <summary>
    /// Gets or sets user notes for this segment.
    /// </summary>
    public string? UserNotes { get; set; }

    /// <summary>
    /// Gets or sets the edge type.
    /// </summary>
    public NavigationEdgeType EdgeType { get; set; } = NavigationEdgeType.UserSegment;

    /// <summary>
    /// Gets whether this edge has detailed route geometry.
    /// </summary>
    public bool HasDetailedGeometry => RouteGeometry?.Count > 1;
}

/// <summary>
/// Types of navigation edges.
/// </summary>
public enum NavigationEdgeType
{
    /// <summary>User-defined segment from trip data.</summary>
    UserSegment,
    /// <summary>Route fetched from third-party routing service (OSRM, etc.).</summary>
    Fetched
}

/// <summary>
/// Point in a route geometry.
/// </summary>
public class RoutePoint
{
    /// <summary>
    /// Gets or sets the latitude.
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Gets or sets the longitude.
    /// </summary>
    public double Longitude { get; set; }
}
