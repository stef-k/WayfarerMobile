namespace WayfarerMobile.Tests.Infrastructure.TestData;

/// <summary>
/// Sample trip data for testing.
/// </summary>
public static class SampleTrips
{
    /// <summary>
    /// Creates a simple trip summary for testing.
    /// </summary>
    public static TripSummary CreateSummary(
        string name = "Test Trip",
        Guid? id = null,
        bool isPublic = false)
    {
        return new TripSummary
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = $"Description for {name}",
            Countries = new List<string> { "United Kingdom" },
            Cities = new List<string> { "London" },
            IsPublic = isPublic,
            UpdatedAt = DateTime.UtcNow,
            Version = 1,
            BoundingBox = new BoundingBox
            {
                North = 51.52,
                South = 51.49,
                East = -0.10,
                West = -0.15
            }
        };
    }

    /// <summary>
    /// Creates a trip details object with places.
    /// </summary>
    public static TripDetails CreateDetails(
        string name = "Test Trip",
        int placeCount = 3,
        Guid? id = null)
    {
        var tripId = id ?? Guid.NewGuid();
        var places = new List<TripPlace>();

        for (int i = 0; i < placeCount; i++)
        {
            places.Add(CreatePlace($"Place {i + 1}", i));
        }

        return new TripDetails
        {
            Id = tripId,
            Name = name,
            Notes = $"Notes for {name}",
            Regions = new List<TripRegion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Main Region",
                    Places = places,
                    SortOrder = 0
                }
            },
            Segments = CreateSegments(places),
            BoundingBox = new BoundingBox
            {
                North = 51.52,
                South = 51.49,
                East = -0.10,
                West = -0.15
            }
        };
    }

    /// <summary>
    /// Creates a single trip place.
    /// </summary>
    public static TripPlace CreatePlace(
        string name,
        int sortOrder,
        double? latitude = null,
        double? longitude = null)
    {
        // Default to London area with slight offset based on sort order
        var lat = latitude ?? 51.5074 + (sortOrder * 0.002);
        var lon = longitude ?? -0.1278 + (sortOrder * 0.003);

        return new TripPlace
        {
            Id = Guid.NewGuid(),
            Name = name,
            Latitude = lat,
            Longitude = lon,
            SortOrder = sortOrder,
            Notes = $"Notes for {name}"
        };
    }

    /// <summary>
    /// Creates segments connecting places in order.
    /// </summary>
    public static List<TripSegment> CreateSegments(List<TripPlace> places)
    {
        var segments = new List<TripSegment>();

        for (int i = 0; i < places.Count - 1; i++)
        {
            var from = places[i];
            var to = places[i + 1];

            var distance = GeoMath.CalculateDistance(
                from.Latitude, from.Longitude,
                to.Latitude, to.Longitude) / 1000; // km

            segments.Add(new TripSegment
            {
                Id = Guid.NewGuid(),
                OriginId = from.Id,
                DestinationId = to.Id,
                TransportMode = "walking",
                DistanceKm = distance,
                DurationMinutes = (int)(distance * 12) // ~5 km/h walking
            });
        }

        return segments;
    }

    /// <summary>
    /// Creates a navigation graph from trip details.
    /// </summary>
    public static TripNavigationGraph CreateNavigationGraph(TripDetails trip)
    {
        var graph = new TripNavigationGraph { TripId = trip.Id };

        foreach (var place in trip.AllPlaces)
        {
            graph.AddNode(new NavigationNode
            {
                Id = place.Id.ToString(),
                Name = place.Name,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Type = NavigationNodeType.Place,
                SortOrder = place.SortOrder
            });
        }

        foreach (var segment in trip.Segments)
        {
            graph.AddEdge(new NavigationEdge
            {
                FromNodeId = (segment.OriginId ?? Guid.Empty).ToString(),
                ToNodeId = (segment.DestinationId ?? Guid.Empty).ToString(),
                TransportMode = segment.TransportMode ?? "walking",
                DistanceKm = segment.DistanceKm ?? 0,
                DurationMinutes = (int)(segment.DurationMinutes ?? 0),
                EdgeType = NavigationEdgeType.UserSegment
            });
        }

        return graph;
    }

    /// <summary>
    /// Creates a London walking tour trip for realistic testing.
    /// </summary>
    public static TripDetails CreateLondonWalkingTour()
    {
        var tripId = Guid.NewGuid();
        var places = new List<TripPlace>
        {
            CreatePlace("Big Ben", 0, 51.5007, -0.1246),
            CreatePlace("Westminster Abbey", 1, 51.4994, -0.1273),
            CreatePlace("Buckingham Palace", 2, 51.5014, -0.1419),
            CreatePlace("Trafalgar Square", 3, 51.5080, -0.1281),
            CreatePlace("British Museum", 4, 51.5194, -0.1270)
        };

        return new TripDetails
        {
            Id = tripId,
            Name = "London Walking Tour",
            Notes = "A walking tour of London's famous landmarks",
            Regions = new List<TripRegion>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Central London",
                    Places = places,
                    SortOrder = 0
                }
            },
            Segments = CreateSegments(places),
            BoundingBox = new BoundingBox
            {
                North = 51.52,
                South = 51.49,
                East = -0.12,
                West = -0.15
            }
        };
    }
}
